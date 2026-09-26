// HSA loader: injected via DYLD_INSERT_LIBRARIES through Battle.net.
// Inside the Hearthstone process only, read-only accesses to files under the
// game folder are served from an overlay folder when the overlay has a file of
// the same relative path:
//   /Applications/Hearthstone/<rel>  ->  /Applications/Hearthstone/HearthstoneAccess/overlay/<rel>
// That covers the patched Managed/Assembly-CSharp.dll, TolkDotNet.dll, the
// Accessibility sounds and Strings/*/ACCESSIBILITY.txt. Nothing inside the game
// folder is modified, so Battle.net's repair has nothing to restore.
#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <unistd.h>
#include <limits.h>
#include <sys/stat.h>
#include <time.h>
#include <dlfcn.h>
#include <CommonCrypto/CommonDigest.h>
#include <dispatch/dispatch.h>

#define DYLD_INTERPOSE(_r, _o) \
    __attribute__((used)) static struct { const void *r; const void *o; } _interpose_##_o \
    __attribute__((section("__DATA,__interpose"))) = { (const void *)&_r, (const void *)&_o };

#define GAME_DIR "/Applications/Hearthstone/"
#define OVERLAY_DIR "/Applications/Hearthstone/HearthstoneAccess/overlay/"
#define HSA_DIR "/Applications/Hearthstone/HearthstoneAccess/"
#define GAME_ASM GAME_DIR "Hearthstone.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll"

static int g_active;
static char g_logpath[PATH_MAX];

static void hlog(const char *fmt, const char *a) {
    if (!g_logpath[0]) return;
    FILE *f = fopen(g_logpath, "a");
    if (!f) return;
    time_t t = time(NULL); char ts[32];
    strftime(ts, sizeof ts, "%H:%M:%S", localtime(&t));
    fprintf(f, "[%s pid %d] ", ts, getpid());
    fprintf(f, fmt, a);
    fputc('\n', f);
    fclose(f);
}

// SHA-256 of a file as lowercase hex; 0 on success
static int sha256_file(const char *path, char out[65]) {
    FILE *f = fopen(path, "rb");
    if (!f) return -1;
    CC_SHA256_CTX c; CC_SHA256_Init(&c);
    static unsigned char buf[1 << 16]; size_t n;
    while ((n = fread(buf, 1, sizeof buf, f)) > 0) CC_SHA256_Update(&c, buf, (CC_LONG)n);
    fclose(f);
    unsigned char d[CC_SHA256_DIGEST_LENGTH]; CC_SHA256_Final(d, &c);
    for (int i = 0; i < CC_SHA256_DIGEST_LENGTH; i++) sprintf(out + 2 * i, "%02x", d[i]);
    return 0;
}

// Tell the player through VoiceOver once the game window is up.
static void announce_later(const char *text) {
    static char msg[512];
    snprintf(msg, sizeof msg, "%s", text);
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 25LL * NSEC_PER_SEC), dispatch_get_main_queue(), ^{
        void *h = dlopen(HSA_DIR "libHSAVoiceOver.dylib", RTLD_NOW);
        int (*out)(const char *, int) = h ? (int (*)(const char *, int))dlsym(h, "hsa_vo_output") : NULL;
        if (out) out(msg, 1);
    });
}

__attribute__((constructor)) static void hsa_init(void) {
    if (strcmp(getprogname(), "Hearthstone") != 0) return; // Battle.net, Agent, helpers: stay inert
    const char *home = getenv("HOME");
    if (home) snprintf(g_logpath, sizeof g_logpath, "%s/Library/Logs/HearthstoneAccess/loader.log", home);
    struct stat st;
    if (stat(OVERLAY_DIR, &st) != 0 || !S_ISDIR(st.st_mode)) { hlog("overlay missing: %s, mod disabled", OVERLAY_DIR); return; }
    if (getenv("HSA_DISABLE")) { hlog("HSA_DISABLE set, mod disabled%s", ""); return; }
    // The overlay was built from one exact game build. After a Blizzard patch it
    // would feed the game stale code, so stay out until it is rebuilt.
    char want[65] = {0}, have[65] = {0};
    FILE *bf = fopen(HSA_DIR "built_for.sha256", "r");
    if (bf) { if (fscanf(bf, "%64s", want) != 1) want[0] = 0; fclose(bf); }
    if (sha256_file(GAME_ASM, have) != 0 || strcmp(want, have) != 0) {
        hlog("game Assembly-CSharp.dll changed (sha %s), mod disabled until rebuilt", have);
        announce_later("Hearthstone Access nie został włączony: gra została zaktualizowana. Mod trzeba przebudować.");
        return;
    }
    g_active = 1;
    hlog("active, overlay = %s", OVERLAY_DIR);
    // Mono's file IO for the sounds is not seen by the interposers, so give the
    // game a real Accessibility/ folder: a symlink into the overlay (an added file
    // next to the game, which Battle.net leaves alone).
    struct stat ls;
    if (lstat(GAME_DIR "Accessibility", &ls) != 0) {
        if (symlink(OVERLAY_DIR "Accessibility", GAME_DIR "Accessibility") == 0) hlog("created symlink %s", GAME_DIR "Accessibility");
        else hlog("could not create symlink %s", GAME_DIR "Accessibility");
    }
}

// Lexically normalise an absolute path ("." and ".." components, double slashes).
static void normalise(const char *in, char *out, size_t cap) {
    char tmp[PATH_MAX];
    if (in[0] == '/') snprintf(tmp, sizeof tmp, "%s", in);
    else { char cwd[PATH_MAX]; if (!getcwd(cwd, sizeof cwd)) { out[0] = 0; return; } snprintf(tmp, sizeof tmp, "%s/%s", cwd, in); }
    size_t n = 0; out[0] = 0;
    char *save = NULL;
    for (char *seg = strtok_r(tmp, "/", &save); seg; seg = strtok_r(NULL, "/", &save)) {
        if (!strcmp(seg, ".")) continue;
        if (!strcmp(seg, "..")) { while (n > 0 && out[n - 1] != '/') n--; if (n > 0) n--; out[n] = 0; continue; }
        size_t l = strlen(seg);
        if (n + l + 2 >= cap) { out[0] = 0; return; }
        out[n++] = '/'; memcpy(out + n, seg, l); n += l; out[n] = 0;
    }
    if (n == 0 && cap > 1) { out[0] = '/'; out[1] = 0; }
}

// Returns overlay path in buf if a read of `path` should be redirected, else NULL.
static const char *overlay(const char *path, int flags, char *buf, size_t cap) {
    if (!g_active || !path || (flags & O_ACCMODE) != O_RDONLY) return NULL;
    if (path[0] == '/' && strncmp(path, GAME_DIR, sizeof GAME_DIR - 1) != 0) return NULL; // fast path
    char abs[PATH_MAX];
    normalise(path, abs, sizeof abs);
    if (strncmp(abs, GAME_DIR, sizeof GAME_DIR - 1) != 0) return NULL;
    const char *rel = abs + sizeof GAME_DIR - 1;
    if (!strncmp(rel, "HearthstoneAccess/", 18) || !*rel) return NULL;
    snprintf(buf, cap, "%s%s", OVERLAY_DIR, rel);
    struct stat st;
    if (stat(buf, &st) != 0) return NULL;
    if (S_ISREG(st.st_mode)) return buf;
    // directories: only stand in for ones the game folder does not have
    // (e.g. Accessibility/), never shadow real game directories
    struct stat orig;
    if (S_ISDIR(st.st_mode) && stat(abs, &orig) != 0) return buf;
    return NULL;
}

static const char *pick(const char *path, int flags, char *buf, size_t cap, const char *what) {
    const char *o = overlay(path, flags, buf, cap);
    if (!o) return path;
    // log each Managed/ and Strings/ hit; sounds would be noisy but are few
    hlog(what, path);
    return o;
}

static int my_open(const char *path, int flags, ...) {
    mode_t mode = 0;
    if (flags & O_CREAT) { va_list ap; va_start(ap, flags); mode = (mode_t)va_arg(ap, int); va_end(ap); }
    char buf[PATH_MAX];
    return open(pick(path, flags, buf, sizeof buf, "open -> overlay: %s"), flags, mode);
}
static int my_openat(int fd, const char *path, int flags, ...) {
    mode_t mode = 0;
    if (flags & O_CREAT) { va_list ap; va_start(ap, flags); mode = (mode_t)va_arg(ap, int); va_end(ap); }
    char buf[PATH_MAX];
    // only redirect paths we can resolve without the fd (absolute, or cwd-relative)
    if (path && (path[0] == '/' || fd == AT_FDCWD)) return openat(fd, pick(path, flags, buf, sizeof buf, "openat -> overlay: %s"), flags, mode);
    return openat(fd, path, flags, mode);
}
static FILE *my_fopen(const char *path, const char *m) {
    int ro = m && m[0] == 'r' && !strchr(m, '+');
    char buf[PATH_MAX];
    return fopen(ro ? pick(path, O_RDONLY, buf, sizeof buf, "fopen -> overlay: %s") : path, m);
}
static int my_stat(const char *path, struct stat *b) {
    char buf[PATH_MAX]; const char *o = overlay(path, O_RDONLY, buf, sizeof buf);
    return stat(o ? o : path, b);
}
static int my_lstat(const char *path, struct stat *b) {
    char buf[PATH_MAX]; const char *o = overlay(path, O_RDONLY, buf, sizeof buf);
    return lstat(o ? o : path, b);
}
static int my_access(const char *path, int mode) {
    char buf[PATH_MAX]; const char *o = (mode & W_OK) ? NULL : overlay(path, O_RDONLY, buf, sizeof buf);
    return access(o ? o : path, mode);
}

// Mono may canonicalise assembly paths; for overlay-only files answer with the
// game-folder path as if the file were there, so Mono keeps using that path.
static char *my_realpath(const char *path, char *resolved) {
    char buf[PATH_MAX];
    if (overlay(path, O_RDONLY, buf, sizeof buf)) {
        char abs[PATH_MAX];
        normalise(path, abs, sizeof abs);
        struct stat orig;
        if (stat(abs, &orig) != 0) { // only for files that exist solely in the overlay
            char *out = resolved ? resolved : malloc(PATH_MAX);
            if (!out) return NULL;
            snprintf(out, PATH_MAX, "%s", abs);
            return out;
        }
    }
    return realpath(path, resolved);
}
static int my_fstatat(int fd, const char *path, struct stat *b, int flag) {
    char buf[PATH_MAX];
    const char *o = (path && (path[0] == '/' || fd == AT_FDCWD)) ? overlay(path, O_RDONLY, buf, sizeof buf) : NULL;
    return fstatat(o ? AT_FDCWD : fd, o ? o : path, b, flag);
}
static int my_faccessat(int fd, const char *path, int mode, int flag) {
    char buf[PATH_MAX];
    const char *o = (!(mode & W_OK) && path && (path[0] == '/' || fd == AT_FDCWD)) ? overlay(path, O_RDONLY, buf, sizeof buf) : NULL;
    return faccessat(o ? AT_FDCWD : fd, o ? o : path, mode, flag);
}

DYLD_INTERPOSE(my_realpath, realpath)
DYLD_INTERPOSE(my_fstatat, fstatat)
DYLD_INTERPOSE(my_faccessat, faccessat)
DYLD_INTERPOSE(my_open, open)
DYLD_INTERPOSE(my_openat, openat)
DYLD_INTERPOSE(my_fopen, fopen)
DYLD_INTERPOSE(my_stat, stat)
DYLD_INTERPOSE(my_lstat, lstat)
DYLD_INTERPOSE(my_access, access)

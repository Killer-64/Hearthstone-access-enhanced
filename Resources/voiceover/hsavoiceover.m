// Speech bridge for Hearthstone Access on macOS: the mod's Tolk output goes to
// the macOS speech synthesizer through Prism (github.com/ethindp/prism, MPL-2.0),
// using its AVSpeech backend. Same semantics as Tolk: interrupting output stops
// current speech, other output is spoken after it (the synthesizer's own queue).
//
// Voice and rate follow the macOS system voice (System Settings > Accessibility >
// Spoken Content), re-read before each message (at most once a second). If "Detect languages" is on there,
// each message's language is recognised and the voice chosen for that language
// in Spoken Content is used, like macOS does.
// Any key press in the game (including a lone modifier such as Ctrl) stops the
// current speech at once, like a screen reader; the mod's response to that key
// is then spoken fresh.
// Optional overrides in ~/Library/Application Support/HearthstoneAccess/:
//   voice       voice name (e.g. "Zosia") or language (e.g. "pl-PL")
//   speech_rate 0.0 .. 1.0 (Prism rate; 0.5 = normal)
#import <Cocoa/Cocoa.h>
#import <AVFoundation/AVFoundation.h>
#import <NaturalLanguage/NaturalLanguage.h>
#include "prism/prism.h"
#include <stdio.h>
#include <time.h>

static FILE *g_log;
static PrismContext *g_prism;
static PrismBackend *g_tts;
static dispatch_queue_t g_q;              // serial: init + all Prism calls
static NSMutableArray *g_early;           // output before Prism is ready: {text, interrupt}
static BOOL g_ready, g_failed, g_started;

static void logline(const char *kind, NSString *s) {
    if (!g_log) {
        NSString *p = [NSHomeDirectory() stringByAppendingPathComponent:@"Library/Logs/HearthstoneAccess/speech.log"];
        g_log = fopen(p.fileSystemRepresentation, "a");
        if (!g_log) return;
    }
    time_t t = time(NULL); char ts[32];
    strftime(ts, sizeof ts, "%H:%M:%S", localtime(&t));
    fprintf(g_log, "[%s] %s %s\n", ts, kind, s.UTF8String ?: "");
    fflush(g_log);
}

static NSString *setting(NSString *name) {
    NSString *p = [[NSHomeDirectory() stringByAppendingPathComponent:@"Library/Application Support/HearthstoneAccess"] stringByAppendingPathComponent:name];
    NSString *s = [NSString stringWithContentsOfFile:p encoding:NSUTF8StringEncoding error:nil];
    s = [s stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    return s.length ? s : nil;
}

// ---- voices (Prism index by name + language) ----
static NSArray<NSDictionary *> *g_voices;      // {name, lang}
static long g_systemVoice = -1, g_currentVoice = -1;
static BOOL g_detect;
static CFAbsoluteTime g_settingsAt;

static void load_voices(void) {
    size_t n = 0;
    if (prism_backend_count_voices(g_tts, &n) != PRISM_OK) n = 0;
    NSMutableArray *a = [NSMutableArray arrayWithCapacity:n];
    for (size_t i = 0; i < n; i++) {
        const char *nm = NULL, *lg = NULL;
        if (prism_backend_get_voice_name(g_tts, i, &nm) != PRISM_OK) nm = NULL;
        if (prism_backend_get_voice_language(g_tts, i, &lg) != PRISM_OK) lg = NULL;
        [a addObject:@{ @"name": nm ? @(nm) : @"", @"lang": lg ? @(lg) : @"" }];
    }
    g_voices = a;
}

static long index_of(NSString *name, NSString *lang) {
    long byName = -1;
    for (NSUInteger i = 0; i < g_voices.count; i++) {
        NSString *n = g_voices[i][@"name"], *l = g_voices[i][@"lang"];
        if (name && [n caseInsensitiveCompare:name] == NSOrderedSame) {
            if (!lang || [l hasPrefix:lang]) return (long)i;
            if (byName < 0) byName = (long)i;
        }
    }
    if (byName >= 0) return byName;
    if (!name && lang) for (NSUInteger i = 0; i < g_voices.count; i++) if ([g_voices[i][@"lang"] hasPrefix:lang]) return (long)i;
    return -1;
}

// a voice identifier as stored by macOS -> Prism index
static long index_of_identifier(NSString *ident) {
    if (!ident) return -1;
    AVSpeechSynthesisVoice *v = [AVSpeechSynthesisVoice voiceWithIdentifier:ident];
    if (v) return index_of(v.name, v.language);
    NSDictionary *attrs = [NSSpeechSynthesizer attributesForVoice:ident];   // legacy voices
    NSString *name = attrs[NSVoiceName];
    NSString *lang = [attrs[NSVoiceLocaleIdentifier] stringByReplacingOccurrencesOfString:@"_" withString:@"-"];
    return name ? index_of(name, lang) : -1;
}

// Spoken Content settings per language (com.apple.Accessibility
// SpokenContentDefaultVoiceSelectionsByLanguage = (lang, {voiceId, rate, volume}, ...)).
// rate is on the AVSpeech 0..1 scale, which is also Prism's scale.
static NSDictionary<NSString *, NSDictionary *> *g_sel;   // lang -> {voice: idx, rate, volume}
static NSString *g_sysLang = @"pl";

static id pref(CFStringRef key, CFStringRef domain) {
    return CFBridgingRelease(CFPreferencesCopyAppValue(key, domain));
}

static void refresh_settings(BOOL force) {
    CFAbsoluteTime now = CFAbsoluteTimeGetCurrent();
    if (!force && now - g_settingsAt < 1) return;
    g_settingsAt = now;
    CFPreferencesAppSynchronize(CFSTR("com.apple.Accessibility"));
    CFPreferencesAppSynchronize(CFSTR("com.apple.universalaccess"));
    CFPreferencesAppSynchronize(CFSTR("com.apple.speech.voice.prefs"));

    NSMutableDictionary *sel = [NSMutableDictionary dictionary];
    NSArray *arr = pref(CFSTR("SpokenContentDefaultVoiceSelectionsByLanguage"), CFSTR("com.apple.Accessibility"));
    if ([arr isKindOfClass:NSArray.class])
        for (NSUInteger i = 0; i + 1 < arr.count; i += 2) {
            NSString *lang = arr[i]; NSDictionary *d = arr[i + 1];
            if (![lang isKindOfClass:NSString.class] || ![d isKindOfClass:NSDictionary.class]) continue;
            NSMutableDictionary *e = [NSMutableDictionary dictionary];
            long v = [d[@"voiceId"] isKindOfClass:NSString.class] ? index_of_identifier(d[@"voiceId"]) : -1;
            if (v >= 0) e[@"voice"] = @(v);
            if ([d[@"rate"] respondsToSelector:@selector(floatValue)]) e[@"rate"] = @([d[@"rate"] floatValue]);
            if ([d[@"volume"] respondsToSelector:@selector(floatValue)]) e[@"volume"] = @([d[@"volume"] floatValue]);
            sel[lang] = e;
        }
    // older per-language voice list, used when a language has no selection above
    NSDictionary *per = pref(CFSTR("spokenContentPreferredVoiceForLanguage"), CFSTR("com.apple.universalaccess"));
    if ([per isKindOfClass:NSDictionary.class])
        for (NSString *lang in per)
            if (!sel[lang][@"voice"]) { long v = index_of_identifier(per[lang]); if (v >= 0) { NSMutableDictionary *e = [sel[lang] mutableCopy] ?: [NSMutableDictionary dictionary]; e[@"voice"] = @(v); sel[lang] = e; } }
    g_sel = sel;

    // system language and voice
    NSString *sl = pref(CFSTR("SystemTTSLanguage"), CFSTR("com.apple.speech.voice.prefs"));
    long sys = -1;
    NSString *want = setting(@"voice");
    if (want) { sys = index_of(want, nil); if (sys < 0) sys = index_of(nil, want); }
    if (sys < 0) sys = index_of_identifier([NSSpeechSynthesizer defaultVoice]);
    if (sys < 0) { NSString *n = pref(CFSTR("SelectedVoiceName"), CFSTR("com.apple.speech.voice.prefs")); if ([n isKindOfClass:NSString.class]) sys = index_of(n, nil); }
    if ([sl isKindOfClass:NSString.class] && sl.length) g_sysLang = sl;
    else if (sys >= 0) g_sysLang = [g_voices[sys][@"lang"] componentsSeparatedByString:@"-"].firstObject;
    if (!want && sel[g_sysLang][@"voice"]) sys = [sel[g_sysLang][@"voice"] longValue];
    if (sys >= 0 && sys != g_systemVoice) { g_systemVoice = sys; logline("system voice:", [NSString stringWithFormat:@"%@ (%@)", g_voices[sys][@"name"], g_sysLang]); }

    NSNumber *det = pref(CFSTR("detectLanguagesEnabled"), CFSTR("com.apple.universalaccess"));
    BOOL detect = ([det isKindOfClass:NSNumber.class] && det.boolValue) || getenv("HSA_VO_FORCE_DETECT");
    if (detect != g_detect || force) { g_detect = detect; logline("detect languages:", detect ? @"on" : @"off"); }
}

// language whose voice/rate/volume a message uses
static NSString *lang_for_text(NSString *text) {
    if (!g_detect || text.length < 12) return g_sysLang;
    NLLanguageRecognizer *rec = [[NLLanguageRecognizer alloc] init];
    [rec processString:text];
    NSDictionary<NLLanguage, NSNumber *> *h = [rec languageHypothesesWithMaximum:1];
    NLLanguage lang = h.allKeys.firstObject;
    if (!lang || h[lang].doubleValue < 0.8) return g_sysLang;
    return lang;
}

static long voice_for_lang(NSString *lang) {
    if ([lang isEqualToString:g_sysLang]) return g_systemVoice;
    NSNumber *i = g_sel[lang][@"voice"];
    if (i) return i.longValue;
    AVSpeechSynthesisVoice *v = [AVSpeechSynthesisVoice voiceWithLanguage:lang];   // macOS default for that language
    long j = v ? index_of(v.name, v.language) : index_of(nil, lang);
    return j >= 0 ? j : g_systemVoice;
}

static void speak_now(NSString *text, BOOL interrupt) {   // on g_q, Prism ready
    refresh_settings(NO);
    NSString *lang = lang_for_text(text);
    long v = voice_for_lang(lang);
    NSDictionary *p = g_sel[lang];
    NSString *ro = setting(@"speech_rate");
    float rate = (ro && ro.doubleValue >= 0 && ro.doubleValue <= 1) ? (float)ro.doubleValue : p[@"rate"] ? [p[@"rate"] floatValue] : 0.5f;
    float volume = p[@"volume"] ? [p[@"volume"] floatValue] : 1.0f;
    if (getenv("HSA_VO_DRYRUN")) { logline("=> (dry)", [NSString stringWithFormat:@"[%@ %@ rate %.2f vol %.2f] %@", v >= 0 ? g_voices[v][@"name"] : @"?", lang, rate, volume, text]); return; }
    static float lastRate = -1, lastVol = -1;
    if (v >= 0 && v != g_currentVoice && prism_backend_set_voice(g_tts, (size_t)v) == PRISM_OK) g_currentVoice = v;
    if (rate != lastRate && prism_backend_set_rate(g_tts, rate) == PRISM_OK) lastRate = rate;
    if (volume != lastVol && prism_backend_set_volume(g_tts, volume) == PRISM_OK) lastVol = volume;
    PrismError e = prism_backend_output(g_tts, text.UTF8String, interrupt);
    if (e != PRISM_OK) logline("!! Prism output failed, error", [NSString stringWithFormat:@"%d", (int)e]);
}

// Prism's AVSpeech init can wait (personal-voice authorisation), so it runs on
// our own queue and never blocks the game's main thread.
static void start(void) {
    @synchronized ([NSProcessInfo processInfo]) {
        if (g_started) return;
        g_started = YES;
        g_q = dispatch_queue_create("hsa.speech", DISPATCH_QUEUE_SERIAL);
        g_early = [NSMutableArray array];
    }
    dispatch_async(g_q, ^{
        PrismConfig cfg = prism_config_init();
        g_prism = prism_init(&cfg);
        g_tts = g_prism ? prism_registry_create(g_prism, PRISM_BACKEND_AV_SPEECH) : NULL;
        PrismError e = g_tts ? prism_backend_initialize(g_tts) : PRISM_ERROR_NOT_INITIALIZED;
        if (e != PRISM_OK) { logline("!! Prism AVSpeech backend unavailable, error", [NSString stringWithFormat:@"%d", (int)e]); g_failed = YES; return; }
        load_voices();
        refresh_settings(YES);
        // voices installed/removed while playing: reload the list and settings
        [[NSNotificationCenter defaultCenter] addObserverForName:AVSpeechSynthesisAvailableVoicesDidChangeNotification object:nil queue:nil
            usingBlock:^(NSNotification *n) {
                dispatch_async(g_q, ^{
                    (void)prism_backend_refresh_voices(g_tts);
                    load_voices(); g_currentVoice = -1; g_systemVoice = -1;
                    refresh_settings(YES);
                    logline("voices changed, reloaded", [NSString stringWithFormat:@"%lu", (unsigned long)g_voices.count]);
                });
            }];
        logline("Prism ready:", @(prism_backend_name(g_tts)));
        NSArray *early;
        @synchronized (g_early) { g_ready = YES; early = [g_early copy]; [g_early removeAllObjects]; }
        for (NSArray *m in early) speak_now(m[0], [m[1] boolValue]);
    });
}

static void stop_speech(const char *why) {
    @synchronized (g_early) { [g_early removeAllObjects]; }
    if (g_ready) dispatch_async(g_q, ^{ (void)prism_backend_stop(g_tts); });
    (void)why;
}

// key presses inside the game window interrupt speech
static void install_key_monitor(void) {
    static BOOL done;
    if (done) return;
    done = YES;
    dispatch_async(dispatch_get_main_queue(), ^{
        [NSEvent addLocalMonitorForEventsMatchingMask:(NSEventMaskKeyDown | NSEventMaskFlagsChanged) handler:^NSEvent *(NSEvent *e) {
            static NSEventModifierFlags last;
            if (e.type == NSEventTypeKeyDown) {
                if (!e.isARepeat) stop_speech("key");
            } else {
                NSEventModifierFlags now = e.modifierFlags & NSEventModifierFlagDeviceIndependentFlagsMask;
                if ((now & ~last) != 0) stop_speech("modifier");   // a modifier went down (not up)
                last = now;
            }
            return e;   // the game still gets the key
        }];
        logline("key monitor:", @"on");
    });
}

__attribute__((visibility("default"))) void hsa_vo_init(void) { @autoreleasepool { start(); install_key_monitor(); } }

__attribute__((visibility("default"))) int hsa_vo_output(const char *utf8, int interrupt) {
    @autoreleasepool {
        NSString *s = utf8 ? [NSString stringWithUTF8String:utf8] : @"";
        if (!s) return 0;
        logline(interrupt ? "!" : "+", s);
        start(); install_key_monitor();
        if (g_failed) return 0;
        if (s.length == 0) {   // Tolk: empty interrupting output = silence
            if (interrupt && g_ready) dispatch_async(g_q, ^{ (void)prism_backend_stop(g_tts); });
            return 1;
        }
        BOOL ready;
        @synchronized (g_early) {
            ready = g_ready;
            if (!ready) { if (interrupt) [g_early removeAllObjects]; [g_early addObject:@[s, @(interrupt != 0)]]; }
        }
        if (ready) dispatch_async(g_q, ^{ speak_now(s, interrupt != 0); });
        return 1;
    }
}

__attribute__((visibility("default"))) int hsa_vo_silence(void) {
    @autoreleasepool {
        logline("x", @"");
        start();
        @synchronized (g_early) { [g_early removeAllObjects]; }
        if (g_ready) dispatch_async(g_q, ^{ (void)prism_backend_stop(g_tts); });
        return 1;
    }
}

__attribute__((visibility("default"))) int hsa_vo_is_running(void) { return g_failed ? 0 : 1; }

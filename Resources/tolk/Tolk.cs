// Drop-in replacement for Tolk's .NET wrapper (DavyKager.Tolk) used by
// Hearthstone Access. On macOS all speech goes to VoiceOver through
// libHSAVoiceOver.dylib, which speaks through Prism's macOS TTS backend.
using System.Runtime.InteropServices;

namespace DavyKager
{
    public sealed class Tolk
    {
        const string Lib = "/Applications/Hearthstone/HearthstoneAccess/libHSAVoiceOver.dylib";

        [DllImport(Lib, CharSet = CharSet.Ansi)] static extern int hsa_vo_output([MarshalAs(UnmanagedType.LPStr)] string text, int interrupt);
        [DllImport(Lib)] static extern int hsa_vo_silence();
        [DllImport(Lib)] static extern int hsa_vo_is_running();
        [DllImport(Lib)] static extern void hsa_vo_init();

        static bool loaded;

        Tolk() { }

        public static void Load() { loaded = true; hsa_vo_init(); }
        public static bool IsLoaded() { return loaded; }
        public static void Unload() { loaded = false; }
        public static void TrySAPI(bool trySAPI) { }
        public static void PreferSAPI(bool preferSAPI) { }
        public static string DetectScreenReader() { return "Prism"; }
        public static bool HasSpeech() { return true; }
        public static bool HasBraille() { return false; }
        public static bool Output(string str, bool interrupt = false) { return hsa_vo_output(str ?? "", interrupt ? 1 : 0) != 0; }
        public static bool Speak(string str, bool interrupt = false) { return Output(str, interrupt); }
        public static bool Braille(string str) { return false; }
        public static bool IsSpeaking() { return false; }
        public static bool Silence() { return hsa_vo_silence() != 0; }
    }
}

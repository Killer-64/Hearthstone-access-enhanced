// Replacements for BCL members that the Mac build of Hearthstone strips but
// Hearthstone Access calls. Only members the Mac BCL keeps may be used here
// (rebuild.sh checks this against the game's own assemblies).
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HSACompat
{
    public static class Shim
    {
        // Regex.Split(input, pattern)
        public static string[] RegexSplit(string input, string pattern)
        {
            var parts = new List<string>();
            int pos = 0;
            foreach (Match m in Regex.Matches(input, pattern))
            {
                if (m.Length == 0) continue;
                parts.Add(input.Substring(pos, m.Index - pos));
                pos = m.Index + m.Length;
            }
            parts.Add(input.Substring(pos));
            return parts.ToArray();
        }
    }
}

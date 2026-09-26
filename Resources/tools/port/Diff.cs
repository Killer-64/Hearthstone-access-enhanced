using System.Text.RegularExpressions;
using Mono.Cecil;
static class DiffMethods
{
    static readonly Regex[] Norm = {
        new(@"<([^>]*)>d__\d+(_hsa)?"), new(@"<>c__DisplayClass\d+_\d+(_hsa)?"), new(@"<([^>]*)>b__\d+_\d+"),
        new(@"<([^>]*)>g__([^|]*)\|\d+_\d+"), new(@"<>c(_hsa)?\b"), new(@"<PrivateImplementationDetails>(_hsa)?::[^ ]*"), new(@"<>f__AnonymousType\d+"),
    };
    static string N(string s)
    {
        s = Norm[0].Replace(s, "<$1>d__N"); s = Norm[1].Replace(s, "<>c__DisplayClassN"); s = Norm[2].Replace(s, "<$1>b__N");
        s = Norm[3].Replace(s, "<$1>g__$2|N"); s = Norm[4].Replace(s, "<>c"); s = Norm[5].Replace(s, "<PID>"); s = Norm[6].Replace(s, "<>f__AnonN");
        return s;
    }
    // classes: file with one class full name per line; selected: W method full names already transplanted
    public static void Run(string wPath, string mPath, string classesFile, string selectedFile)
    {
        var W = ModuleDefinition.ReadModule(wPath); var M = ModuleDefinition.ReadModule(mPath);
        var classes = File.ReadAllLines(classesFile).ToHashSet();
        var selected = File.ReadAllLines(selectedFile).ToHashSet();
        var mTypes = Analyze.AllTypes(M).ToDictionary(t => t.FullName);
        int total = 0;
        foreach (var wt in Analyze.AllTypes(W).Where(t => { var x = t; while (x.DeclaringType != null) x = x.DeclaringType; return classes.Contains(x.FullName) && !Port.IsCompilerGenerated(t); }))
        {
            if (!mTypes.TryGetValue(wt.FullName, out var mt)) { Console.WriteLine($"CLASS ONLY IN W: {wt.FullName}"); continue; }
            var mm = mt.Methods.ToDictionary(m => m.FullName);
            foreach (var m in wt.Methods)
            {
                if (selected.Contains(m.FullName)) continue;
                if (!mm.TryGetValue(m.FullName, out var x)) { Console.WriteLine($"W-ONLY NOT PULLED: {m.FullName}"); total++; continue; }
                if (N(Analyze.BodyKey(m)) == N(Analyze.BodyKey(x))) continue;
                Console.WriteLine($"DIFFERS: {m.FullName}"); total++;
            }
        }
        Console.WriteLine($"candidates: {total}");
    }
}

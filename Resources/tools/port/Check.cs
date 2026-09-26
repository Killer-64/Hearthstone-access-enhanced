using Mono.Cecil;
using Mono.Cecil.Cil;
static class Check
{
    // Resolve every type/member reference of `path` against the given directories.
    public static int Run(string path, string[] dirs)
    {
        var res = new DefaultAssemblyResolver();
        foreach (var d in dirs) res.AddSearchDirectory(d);
        var m = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = res });
        var bad = new SortedDictionary<string, int>();
        void Note(string k) => bad[k] = bad.GetValueOrDefault(k) + 1;
        foreach (var tr in m.GetTypeReferences())
            try { if (tr.Resolve() == null) Note("type " + tr.FullName); } catch (AssemblyResolutionException e) { Note("assembly " + e.AssemblyReference.Name); }
        foreach (var mr in m.GetMemberReferences())
            try
            {
                var r = mr switch { MethodReference x => (object?)x.Resolve(), FieldReference f => f.Resolve(), _ => "" };
                if (r == null) Note("member " + mr.FullName);
            }
            catch (AssemblyResolutionException e) { Note("assembly " + e.AssemblyReference.Name); }
        foreach (var kv in bad) Console.WriteLine($"{kv.Value}\t{kv.Key}");
        Console.WriteLine($"unresolved: {bad.Count}");
        return bad.Count;
    }
}

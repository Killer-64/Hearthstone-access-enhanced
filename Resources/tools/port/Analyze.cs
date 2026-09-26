using Mono.Cecil;
using Mono.Cecil.Cil;

static class Analyze
{
    public static string Sig(MethodDefinition m) => m.FullName;

    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition mod)
    {
        foreach (var t in mod.Types)
            foreach (var x in Walk(t)) yield return x;
    }

    static IEnumerable<TypeDefinition> Walk(TypeDefinition t)
    {
        yield return t;
        foreach (var n in t.NestedTypes)
            foreach (var x in Walk(n)) yield return x;
    }

    public static string BodyKey(MethodDefinition m)
    {
        if (!m.HasBody) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var i in m.Body.Instructions)
        {
            sb.Append(i.OpCode.Name);
            switch (i.Operand)
            {
                case null: break;
                case Instruction target: sb.Append(' ').Append(m.Body.Instructions.IndexOf(target)); break;
                case Instruction[] targets: foreach (var t in targets) sb.Append(' ').Append(m.Body.Instructions.IndexOf(t)); break;
                case VariableDefinition v: sb.Append(' ').Append(v.Index); break;
                case ParameterDefinition p: sb.Append(' ').Append(p.Index); break;
                case MemberReference r: sb.Append(' ').Append(r.FullName); break;
                default: sb.Append(' ').Append(i.Operand); break;
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static void Run(string wPath, string mPath)
    {
        var w = ModuleDefinition.ReadModule(wPath);
        var m = ModuleDefinition.ReadModule(mPath);
        var mTypes = AllTypes(m).ToDictionary(t => t.FullName);
        var wTypes = AllTypes(w).ToList();

        var added = wTypes.Where(t => !mTypes.ContainsKey(t.FullName)).ToList();
        Console.WriteLine($"W types {wTypes.Count}, M types {mTypes.Count}, only in W {added.Count}, only in M {mTypes.Count - wTypes.Count(t => mTypes.ContainsKey(t.FullName))}");
        foreach (var g in added.GroupBy(t => t.Namespace == "" && t.DeclaringType != null ? "(nested)" : t.Namespace).OrderByDescending(g => g.Count()).Take(30))
            Console.WriteLine($"  ns '{g.Key}': {g.Count()}  e.g. {string.Join(", ", g.Take(4).Select(t => t.Name))}");

        var addedNames = new HashSet<string>(added.Select(t => t.FullName));
        int addedFields = 0, addedMethods = 0, changed = 0, changedHsa = 0;
        var changedHsaList = new List<string>();
        var addedMemberNames = new HashSet<string>();
        foreach (var wt in wTypes)
        {
            if (!mTypes.TryGetValue(wt.FullName, out var mt)) continue;
            var mf = new HashSet<string>(mt.Fields.Select(f => f.FullName));
            foreach (var f in wt.Fields) if (!mf.Contains(f.FullName)) { addedFields++; addedMemberNames.Add(f.FullName); }
            var mm = mt.Methods.ToDictionary(x => x.FullName);
            foreach (var wm in wt.Methods)
                if (!mm.ContainsKey(wm.FullName)) { addedMethods++; addedMemberNames.Add(wm.FullName); }
        }
        foreach (var wt in wTypes)
        {
            if (!mTypes.TryGetValue(wt.FullName, out var mt)) continue;
            var mm = mt.Methods.ToDictionary(x => x.FullName);
            foreach (var wm in wt.Methods)
            {
                if (!mm.TryGetValue(wm.FullName, out var mmeth)) continue;
                if (BodyKey(wm) == BodyKey(mmeth)) continue;
                changed++;
                bool hsa = wm.HasBody && wm.Body.Instructions.Any(i => i.Operand is MemberReference r &&
                    (addedNames.Contains(r.DeclaringType?.FullName ?? "") || addedNames.Contains(r.FullName) || addedMemberNames.Contains(r.FullName)));
                if (hsa) { changedHsa++; changedHsaList.Add(wm.FullName); }
            }
        }
        Console.WriteLine($"added fields on existing types {addedFields}, added methods {addedMethods}, changed bodies {changed}, of which touching added stuff {changedHsa}");
        File.WriteAllLines("changed_hsa.txt", changedHsaList);
        File.WriteAllLines("added_types.txt", added.Select(t => t.FullName));
        File.WriteAllLines("added_members.txt", addedMemberNames);
    }
}

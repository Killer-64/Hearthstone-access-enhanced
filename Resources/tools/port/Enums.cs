using Mono.Cecil;
static class Enums
{
    // enums present in both modules whose name->value tables differ
    public static Dictionary<string, (Dictionary<string, long> w, Dictionary<string, long> m)> Diff(ModuleDefinition W, ModuleDefinition M)
    {
        var mt = Analyze.AllTypes(M).Where(t => t.IsEnum).ToDictionary(t => t.FullName);
        var res = new Dictionary<string, (Dictionary<string, long>, Dictionary<string, long>)>();
        foreach (var t in Analyze.AllTypes(W).Where(t => t.IsEnum))
        {
            if (!mt.TryGetValue(t.FullName, out var m)) continue;
            var a = t.Fields.Where(f => f.HasConstant).ToDictionary(f => f.Name, f => Convert.ToInt64(f.Constant));
            var b = m.Fields.Where(f => f.HasConstant).ToDictionary(f => f.Name, f => Convert.ToInt64(f.Constant));
            bool differ = a.Any(kv => b.TryGetValue(kv.Key, out var v) && v != kv.Value) || a.Keys.Any(k => !b.ContainsKey(k));
            if (differ) res[t.FullName] = (a, b);
        }
        return res;
    }
}

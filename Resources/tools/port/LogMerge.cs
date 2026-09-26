using Mono.Cecil;
using Mono.Cecil.Cil;

// HSA registers its loggers ("Accessibility", "Accessibility_text") in Log..cctor:
// a List<string> of legacy logger names and a LogInfo[] of defaults. W's cctor
// also carries Windows-only loggers and lacks Mac/version ones, so instead of
// taking it we append HSA's entries to M's cctor.
static class LogMerge
{
    public static void Apply(ModuleDefinition W, ModuleDefinition M, ISet<string> hsaLoggerNames, TextWriter log)
    {
        var wc = W.GetType("Log").Methods.First(m => m.IsConstructor && m.IsStatic);
        var mc = M.GetType("Log").Methods.First(m => m.IsConstructor && m.IsStatic);
        var wi = wc.Body.Instructions; var mi = mc.Body.Instructions;
        var il = mc.Body.GetILProcessor();

        // 1. legacy names: insert "dup; ldstr; callvirt Add" before stsfld s_legacyHearthstoneLoggers
        var stNames = mi.First(i => i.OpCode == OpCodes.Stsfld && ((FieldReference)i.Operand).Name == "s_legacyHearthstoneLoggers");
        var add = mi.Take(mi.IndexOf(stNames)).Last(i => i.OpCode == OpCodes.Callvirt && ((MethodReference)i.Operand).Name == "Add");
        var mNames = mi.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToHashSet();
        foreach (var n in hsaLoggerNames.Where(n => !mNames.Contains(n)))
        {
            il.InsertBefore(stNames, il.Create(OpCodes.Dup));
            il.InsertBefore(stNames, il.Create(OpCodes.Ldstr, n));
            il.InsertBefore(stNames, il.Create(OpCodes.Callvirt, (MethodReference)add.Operand));
            log.WriteLine($"Log..cctor: added legacy logger {n}");
        }

        // 2. LogInfo defaults: find W element blocks for the names
        var stInfos = mi.First(i => i.OpCode == OpCodes.Stsfld && ((FieldReference)i.Operand).Name == "DEFAULT_LOG_INFOS");
        var newarr = mi.Take(mi.IndexOf(stInfos)).Last(i => i.OpCode == OpCodes.Newarr);
        var sizeIns = newarr.Previous;
        int size = GetInt(sizeIns);
        var wBlocks = new List<List<Instruction>>();
        for (int k = 0; k < wi.Count; k++)
        {
            if (wi[k].OpCode != OpCodes.Newobj || ((MethodReference)wi[k].Operand).DeclaringType.Name != "LogInfo") continue;
            // block: [dup, ldc idx] newobj ... stelem.ref
            int end = k; while (wi[end].OpCode != OpCodes.Stelem_Ref) end++;
            var nameIns = wi.Skip(k).Take(end - k).FirstOrDefault(i => i.OpCode == OpCodes.Ldstr);
            if (nameIns == null || !hsaLoggerNames.Contains((string)nameIns.Operand) || mNames.Contains((string)nameIns.Operand)) continue;
            wBlocks.Add(wi.Skip(k).Take(end - k + 1).ToList());
        }
        if (wBlocks.Count == 0) return;
        il.Replace(sizeIns, il.Create(OpCodes.Ldc_I4, size + wBlocks.Count));
        int idx = size;
        foreach (var b in wBlocks)
        {
            il.InsertBefore(stInfos, il.Create(OpCodes.Dup));
            il.InsertBefore(stInfos, il.Create(OpCodes.Ldc_I4, idx++));
            foreach (var i in b)
            {
                Instruction n = i.Operand switch
                {
                    MethodReference r => il.Create(i.OpCode, M.ImportReference(Resolve(r))),
                    FieldReference f => il.Create(i.OpCode, M.ImportReference(Resolve(f))),
                    string s => il.Create(i.OpCode, s),
                    int v => il.Create(i.OpCode, v),
                    sbyte v => il.Create(i.OpCode, v),
                    null => il.Create(i.OpCode),
                    _ => throw new Exception("unexpected operand in LogInfo block: " + i)
                };
                il.InsertBefore(stInfos, n);
            }
            log.WriteLine($"Log..cctor: added LogInfo {b.First(i => i.OpCode == OpCodes.Ldstr).Operand} at {idx - 1}");
        }
        mc.Body.MaxStackSize = Math.Max(mc.Body.MaxStackSize, 8);
    }

    static int GetInt(Instruction i) => i.OpCode.Code switch
    {
        Code.Ldc_I4 => (int)i.Operand, Code.Ldc_I4_S => (sbyte)i.Operand,
        >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8 => i.OpCode.Code - Code.Ldc_I4_0,
        _ => throw new Exception("array size is not a constant: " + i)
    };
    // W refs point at Blizzard.T5.Logging; import them by name against M's copy
    static MethodReference Resolve(MethodReference r) => r;
    static FieldReference Resolve(FieldReference f) => f;
}

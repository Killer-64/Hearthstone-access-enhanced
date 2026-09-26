using Mono.Cecil;
using Mono.Cecil.Cil;

// Enum constants are compiled into IL as plain integers. HSA inserted members
// into some game enums (e.g. ClientOption), shifting later values, and added
// members (EOE, ACCESSIBILITY*) that the Mac build lacks. This pass:
//  1. adds the W-only members to the M enums,
//  2. finds every integer constant in W code that is used as one of those enums
//     (small stack simulation) and rewrites it to the M value, in place in W,
//  3. reports methods that use HSA-added members (they must be transplanted).
class EnumFix
{
    readonly ModuleDefinition W, M;
    readonly TextWriter log;
    // W enum full name -> (W value -> M value)
    readonly Dictionary<string, Dictionary<long, long>> remap = new();
    readonly Dictionary<string, HashSet<long>> addedValuesW = new();
    public readonly HashSet<MethodDefinition> usesAddedMembers = new();
    public readonly List<(MethodDefinition m, string text)> warnings = new();
    public int rewritten;

    public EnumFix(ModuleDefinition w, ModuleDefinition m, TextWriter log) { W = w; M = m; this.log = log; }

    public void AddMembersAndBuildMaps()
    {
        var mEnums = Analyze.AllTypes(M).Where(t => t.IsEnum).ToDictionary(t => t.FullName);
        var wEnums = Analyze.AllTypes(W).Where(t => t.IsEnum).ToDictionary(t => t.FullName);
        foreach (var (name, (w, m)) in Enums.Diff(W, M))
        {
            var mt = mEnums[name];
            var under = mt.Fields.First(f => f.Name == "value__").FieldType;
            long next = m.Count == 0 ? 0 : m.Values.Max() + 1;
            var added = new HashSet<long>();
            // non-EOE first, EOE last so it stays "one past the end"
            foreach (var k in w.Keys.Where(k => !m.ContainsKey(k)).OrderBy(k => k == "EOE" ? 1 : 0).ThenBy(k => w[k]))
            {
                var f = new FieldDefinition(k, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, mt)
                { Constant = Convert.ChangeType(next, Type.GetType(under.FullName)!) };
                // copy attributes too: Option members carry [Description("optionName")],
                // which the game uses as the key in options.txt
                var wf = wEnums[name].Fields.First(x => x.Name == k);
                foreach (var a in wf.CustomAttributes)
                    f.CustomAttributes.Add(new CustomAttribute(M.ImportReference(a.Constructor), a.GetBlob()));
                mt.Fields.Add(f);
                m[k] = next; added.Add(w[k]);
                log.WriteLine($"enum {name}: added {k} = {next} (W {w[k]})");
                next++;
            }
            var map = new Dictionary<long, long>();
            foreach (var (k, v) in w) if (m.TryGetValue(k, out var mv)) map[v] = mv;
            remap[name] = map;
            addedValuesW[name] = added;
        }
    }

    // ---- stack simulation ----
    sealed class Slot { public Instruction? Producer; }

    static int Pops(Instruction i, MethodDefinition ctx, int depth)
    {
        switch (i.OpCode.Code)
        {
            case Code.Call: case Code.Callvirt:
                { var m = (MethodReference)i.Operand; return m.Parameters.Count + (m.HasThis ? 1 : 0); }
            case Code.Newobj: return ((MethodReference)i.Operand).Parameters.Count;
            case Code.Calli: { var cs = (CallSite)i.Operand; return cs.Parameters.Count + (cs.HasThis ? 1 : 0) + 1; }
            case Code.Ret: return ctx.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
        }
        return i.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4
                or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
            StackBehaviour.PopAll => depth,
            _ => 0
        };
    }

    static int Pushes(Instruction i)
    {
        switch (i.OpCode.Code)
        {
            case Code.Call: case Code.Callvirt: return ((MethodReference)i.Operand).ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
            case Code.Calli: return ((CallSite)i.Operand).ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
            case Code.Newobj: return 1;
        }
        return i.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush => 0,
            _ => 1
        };
    }

    static bool IsLdcInt(Instruction i) => i.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2
        or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4_M1 or Code.Ldc_I8;

    static long LdcValue(Instruction i) => i.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1, Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3, Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7, Code.Ldc_I4_8 => 8,
        Code.Ldc_I4_S => (sbyte)i.Operand, Code.Ldc_I4 => (int)i.Operand, Code.Ldc_I8 => (long)i.Operand, _ => 0
    };

    // resolve !0 / !!0 in a member signature against the reference's generic arguments
    static TypeReference Subst(TypeReference t, MemberReference owner)
    {
        if (t is GenericParameter gp)
        {
            if (gp.Type == GenericParameterType.Type && owner.DeclaringType is GenericInstanceType git && gp.Position < git.GenericArguments.Count)
                return git.GenericArguments[gp.Position];
            if (gp.Type == GenericParameterType.Method && owner is GenericInstanceMethod gim && gp.Position < gim.GenericArguments.Count)
                return gim.GenericArguments[gp.Position];
        }
        return t;
    }

    TypeReference? ProducedType(Instruction? p, MethodDefinition m)
    {
        if (p == null) return null;
        switch (p.OpCode.Code)
        {
            case Code.Ldloc_0: return m.Body.Variables[0].VariableType;
            case Code.Ldloc_1: return m.Body.Variables[1].VariableType;
            case Code.Ldloc_2: return m.Body.Variables[2].VariableType;
            case Code.Ldloc_3: return m.Body.Variables[3].VariableType;
            case Code.Ldloc: case Code.Ldloc_S: return ((VariableDefinition)p.Operand).VariableType;
            case Code.Ldarg: case Code.Ldarg_S: return ((ParameterDefinition)p.Operand).ParameterType;
            case Code.Ldarg_0: case Code.Ldarg_1: case Code.Ldarg_2: case Code.Ldarg_3:
                {
                    int idx = p.OpCode.Code - Code.Ldarg_0 - (m.HasThis ? 1 : 0);
                    return idx < 0 ? m.DeclaringType : m.Parameters[idx].ParameterType;
                }
            case Code.Ldfld: case Code.Ldsfld: { var f = (FieldReference)p.Operand; return Subst(f.FieldType, f); }
            case Code.Call: case Code.Callvirt: { var r = (MethodReference)p.Operand; return Subst(r.ReturnType, r); }
            case Code.Unbox_Any: return (TypeReference)p.Operand;
            case Code.Ldelem_Any: return (TypeReference)p.Operand;
        }
        return null;
    }

    string? EnumName(TypeReference? t)
    {
        if (t == null) return null;
        if (t is ByReferenceType br) t = br.ElementType;
        if (t is RequiredModifierType rm) t = rm.ElementType;
        if (t.Scope != W && !(t is TypeDefinition td && td.Module == W)) return null;
        return remap.ContainsKey(t.FullName) ? t.FullName : null;
    }

    public void Process(MethodDefinition m)
    {
        if (!m.HasBody || m.Body.Instructions.Count == 0) return;
        var ins = m.Body.Instructions;
        var entry = new Dictionary<Instruction, List<Instruction?>>();
        var work = new Stack<(int idx, List<Instruction?> st)>();
        work.Push((0, new()));
        foreach (var h in m.Body.ExceptionHandlers)
        {
            if (h.HandlerType == ExceptionHandlerType.Catch || h.HandlerType == ExceptionHandlerType.Filter)
                work.Push((ins.IndexOf(h.HandlerStart), new() { null }));
            else work.Push((ins.IndexOf(h.HandlerStart), new()));
            if (h.FilterStart != null) work.Push((ins.IndexOf(h.FilterStart), new() { null }));
        }
        // consumer -> list of (slot position from bottom of popped args, producer)
        var consumed = new List<(Instruction consumer, List<Instruction?> args)>();
        var seen = new HashSet<Instruction>();
        var subArgs = new Dictionary<Instruction, Instruction?>();
        while (work.Count > 0)
        {
            var (idx, st) = work.Pop();
            while (idx >= 0 && idx < ins.Count)
            {
                var i = ins[idx];
                if (!seen.Add(i)) break;
                int pop = Math.Min(Pops(i, m, st.Count), st.Count);
                var args = st.GetRange(st.Count - pop, pop);
                st.RemoveRange(st.Count - pop, pop);
                if (pop > 0) consumed.Add((i, args));
                if (i.OpCode.Code == Code.Sub && args.Count == 2) subArgs[i] = args[0];
                int push = Pushes(i);
                if (i.OpCode.Code == Code.Dup) { var top = args.Count > 0 ? args[0] : null; st.Add(top); st.Add(top); }
                else for (int k = 0; k < push; k++) st.Add(i);
                var fc = i.OpCode.FlowControl;
                if (i.Operand is Instruction t) work.Push((ins.IndexOf(t), new(st)));
                if (i.Operand is Instruction[] ts) foreach (var x in ts) work.Push((ins.IndexOf(x), new(st)));
                if (fc is FlowControl.Branch or FlowControl.Return or FlowControl.Throw) break;
                if (i.OpCode.Code is Code.Leave or Code.Leave_S or Code.Endfinally or Code.Endfilter) break;
                idx++;
            }
        }

        var typed = new Dictionary<Instruction, string>();
        foreach (var (c, args) in consumed)
        {
            void Tag(Instruction? p, TypeReference? want)
            {
                var en = EnumName(want);
                if (p == null || en == null || !IsLdcInt(p)) return;
                typed[p] = en;
            }
            switch (c.OpCode.Code)
            {
                case Code.Call: case Code.Callvirt: case Code.Newobj:
                    {
                        var r = (MethodReference)c.Operand;
                        int off = (r.HasThis && c.OpCode.Code != Code.Newobj) ? 1 : 0;
                        for (int k = 0; k < r.Parameters.Count && k + off < args.Count; k++)
                            Tag(args[k + off], Subst(r.Parameters[k].ParameterType, r));
                        break;
                    }
                case Code.Stfld: { var f = (FieldReference)c.Operand; Tag(args[^1], Subst(f.FieldType, f)); break; }
                case Code.Stsfld: { var f = (FieldReference)c.Operand; Tag(args[^1], Subst(f.FieldType, f)); break; }
                case Code.Stloc: case Code.Stloc_S: Tag(args[^1], ((VariableDefinition)c.Operand).VariableType); break;
                case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
                    Tag(args[^1], m.Body.Variables[c.OpCode.Code - Code.Stloc_0].VariableType); break;
                case Code.Starg: case Code.Starg_S: Tag(args[^1], ((ParameterDefinition)c.Operand).ParameterType); break;
                case Code.Ret: Tag(args[^1], m.ReturnType); break;
                case Code.Box: Tag(args[^1], (TypeReference)c.Operand); break;
                case Code.Stelem_Any: Tag(args[^1], (TypeReference)c.Operand); break;
                case Code.Stelem_I4:
                    {
                        var at = ProducedType(args[0], m) as ArrayType ?? (args[0] is { OpCode.Code: Code.Newarr } na ? new ArrayType((TypeReference)na.Operand) : null);
                        Tag(args[^1], at?.ElementType); break;
                    }
                case Code.Ceq: case Code.Cgt: case Code.Cgt_Un: case Code.Clt: case Code.Clt_Un:
                case Code.Beq: case Code.Beq_S: case Code.Bne_Un: case Code.Bne_Un_S:
                case Code.Bge: case Code.Bge_S: case Code.Bge_Un: case Code.Bge_Un_S:
                case Code.Bgt: case Code.Bgt_S: case Code.Bgt_Un: case Code.Bgt_Un_S:
                case Code.Ble: case Code.Ble_S: case Code.Ble_Un: case Code.Ble_Un_S:
                case Code.Blt: case Code.Blt_S: case Code.Blt_Un: case Code.Blt_Un_S:
                case Code.Sub:
                    if (args.Count == 2) { Tag(args[1], ProducedType(args[0], m)); Tag(args[0], ProducedType(args[1], m)); }
                    break;
                case Code.Switch:
                    if (args.Count == 1)
                    {
                        var src = args[0];
                        var en = EnumName(ProducedType(src, m));
                        if (en != null && NeedsRemap(en)) warnings.Add((m, $"switch on {en}"));
                        // "x - base; switch" pattern
                        if (src?.OpCode.Code == Code.Sub && subArgs.TryGetValue(src, out var sa))
                        {
                            var en2 = EnumName(ProducedType(sa, m));
                            if (en2 != null && NeedsRemap(en2)) warnings.Add((m, $"switch on ({en2} - base)"));
                        }
                    }
                    break;
            }
        }

        foreach (var (p, en) in typed)
        {
            long v = LdcValue(p);
            if (addedValuesW[en].Contains(v)) usesAddedMembers.Add(m);
            if (!remap[en].TryGetValue(v, out var nv))
            {
                if (NeedsRemap(en)) warnings.Add((m, $"unnamed {en} value {v}"));
                continue;
            }
            if (nv == v) continue;
            if (p.OpCode.Code == Code.Ldc_I8) p.Operand = nv;
            else { p.OpCode = OpCodes.Ldc_I4; p.Operand = (int)nv; }
            rewritten++;
        }
    }

    bool NeedsRemap(string en) => remap[en].Any(kv => kv.Key != kv.Value);
}

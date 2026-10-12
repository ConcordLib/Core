using System.Reflection;
using System.Reflection.Emit;
using OpCode = System.Reflection.Emit.OpCode;
using ReflectedBody = System.Reflection.MethodBody;

namespace Concord.Host.Adapter.Tests;

public static class RealBodies {
    internal const string GamePathVariable = "CONCORD_GAME_ASSEMBLY";

    internal const string RuntimeCorpus = "runtime";

    internal const string GameCorpus = "game";

    private const string InstalledGameAssembly = "/mnt/games/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll";

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<ushort, OpCode> ByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => (ushort)opcode.Value);

    private static int resolverInstalled;

    internal static Assembly[] Runtime => [
        typeof(object).Assembly,
        typeof(System.Linq.Expressions.Expression).Assembly,
        typeof(HashSet<>).Assembly,
        typeof(ComposedBody).Assembly,
    ];

    internal static string GameAssemblyPath => Environment.GetEnvironmentVariable(GamePathVariable) is { Length: > 0 } overridden
        ? overridden
        : InstalledGameAssembly;

    public static Xunit.TheoryData<string> Corpora => new Xunit.TheoryData<string> { RuntimeCorpus };

    internal static Assembly[] Of(string corpus) {
        if (corpus != GameCorpus) {
            return Runtime;
        }

        return [Game() ?? throw new InvalidOperationException($"no game assembly at {GameAssemblyPath}; point {GamePathVariable} at one")];
    }

    internal static Assembly? Game() {
        string path = GameAssemblyPath;
        if (!File.Exists(path)) {
            return null;
        }

        if (Interlocked.Exchange(ref resolverInstalled, 1) == 0) {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) => {
                string sibling = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(sibling) ? Assembly.LoadFrom(sibling) : null;
            };
        }

        return Assembly.LoadFrom(path);
    }

    internal static IEnumerable<MethodBase> Methods(Assembly assembly) {
        Type[] types;
        try {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial) {
            types = partial.Types.Where(type => type is not null).ToArray()!;
        }

        foreach (Type type in types) {
            MethodBase[] members;
            try {
                members = [.. type.GetMethods(Declared), .. type.GetConstructors(Declared).Cast<MethodBase>()];
            }
            catch (TypeLoadException) {
                continue;
            }
            catch (FileNotFoundException) {
                continue;
            }

            foreach (MethodBase member in members) {
                yield return member;
            }
        }
    }

    internal static ComposedBody? Read(MethodBase method, out string? skipped) {
        return Read(method, out skipped, out _);
    }

    internal static ComposedBody? Read(MethodBase method, out string? skipped, out ReflectedBody? header) {
        skipped = null;
        header = null;
        ReflectedBody? reflected;
        byte[]? il;
        try {
            reflected = method.GetMethodBody();
            il = reflected?.GetILAsByteArray();
        }
        catch (Exception unreadable) {
            skipped = unreadable.GetType().Name;
            return null;
        }

        if (reflected is null || il is null || il.Length == 0) {
            skipped = "no body";
            return null;
        }

        header = reflected;
        Type[]? typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        Type[]? methodArguments = method is MethodInfo { IsGenericMethodDefinition: true } generic ? generic.GetGenericArguments() : null;
        Walk walk = new Walk(il, method.Module, typeArguments, methodArguments);
        if (!walk.Decode(out skipped)) {
            return null;
        }

        return walk.Compose(method, reflected, out skipped);
    }

    private sealed class Walk {
        private readonly byte[] il;
        private readonly Module module;
        private readonly Type[]? typeArguments;
        private readonly Type[]? methodArguments;
        private readonly List<int> offsets = [];
        private readonly List<OpCode> opcodes = [];
        private readonly List<object?> operands = [];
        private readonly List<int[]?> branches = [];

        internal Walk(byte[] il, Module module, Type[]? typeArguments, Type[]? methodArguments) {
            this.il = il;
            this.module = module;
            this.typeArguments = typeArguments;
            this.methodArguments = methodArguments;
        }

        internal bool Decode(out string? skipped) {
            skipped = null;
            int at = 0;
            while (at < il.Length) {
                int start = at;
                ushort value = il[at] == 0xfe ? (ushort)(0xfe00 | il[at + 1]) : il[at];
                at += il[start] == 0xfe ? 2 : 1;
                if (!ByValue.TryGetValue(value, out OpCode opcode)) {
                    skipped = $"opcode 0x{value:x4}";
                    return false;
                }

                object? operand = null;
                int[]? targets = null;
                switch (opcode.OperandType) {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                        targets = [at + 1 + (sbyte)il[at]];
                        at += 1;
                        break;
                    case OperandType.InlineBrTarget:
                        targets = [at + 4 + BitConverter.ToInt32(il, at)];
                        at += 4;
                        break;
                    case OperandType.InlineSwitch: {
                        int cases = BitConverter.ToInt32(il, at);
                        int after = at + 4 + (cases * 4);
                        targets = new int[cases];
                        for (int index = 0; index < cases; index++) {
                            targets[index] = after + BitConverter.ToInt32(il, at + 4 + (index * 4));
                        }

                        at = after;
                        break;
                    }

                    case OperandType.ShortInlineI:
                        operand = opcode == OpCodes.Ldc_I4_S ? (sbyte)il[at] : il[at];
                        at += 1;
                        break;
                    case OperandType.InlineI:
                        operand = BitConverter.ToInt32(il, at);
                        at += 4;
                        break;
                    case OperandType.InlineI8:
                        operand = BitConverter.ToInt64(il, at);
                        at += 8;
                        break;
                    case OperandType.ShortInlineR:
                        operand = BitConverter.ToSingle(il, at);
                        at += 4;
                        break;
                    case OperandType.InlineR:
                        operand = BitConverter.ToDouble(il, at);
                        at += 8;
                        break;
                    case OperandType.ShortInlineVar:
                        operand = (int)il[at];
                        at += 1;
                        break;
                    case OperandType.InlineVar:
                        operand = (int)BitConverter.ToUInt16(il, at);
                        at += 2;
                        break;
                    case OperandType.InlineString:
                        try {
                            operand = module.ResolveString(BitConverter.ToInt32(il, at));
                        }
                        catch (Exception unresolved) {
                            skipped = "ResolveString " + unresolved.GetType().Name;
                            return false;
                        }

                        at += 4;
                        break;
                    case OperandType.InlineType:
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok: {
                        MemberInfo member;
                        try {
                            member = module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!;
                        }
                        catch (Exception unresolved) {
                            skipped = "ResolveMember " + unresolved.GetType().Name;
                            return false;
                        }

                        at += 4;
                        if (member is not (Type or FieldInfo or MethodBase)) {
                            skipped = "operand member " + member.GetType().Name;
                            return false;
                        }

                        operand = member;
                        break;
                    }

                    default:
                        skipped = opcode.OperandType.ToString();
                        return false;
                }

                offsets.Add(start);
                opcodes.Add(opcode);
                operands.Add(operand);
                branches.Add(targets);
            }

            return true;
        }

        internal ComposedBody? Compose(MethodBase method, ReflectedBody reflected, out string? skipped) {
            skipped = null;
            Dictionary<int, int> indexOf = [];
            for (int index = 0; index < offsets.Count; index++) {
                indexOf[offsets[index]] = index;
            }

            indexOf[il.Length] = offsets.Count;

            ITranspilerContext context = TranspilerContextFactory.For(method);
            Dictionary<int, Label> marks = [];
            Label MarkAt(int offset) {
                if (!marks.TryGetValue(offset, out Label mark)) {
                    mark = context.DefineLabel();
                    marks[offset] = mark;
                }

                return mark;
            }

            for (int index = 0; index < opcodes.Count; index++) {
                int[]? targets = branches[index];
                if (targets is null) {
                    continue;
                }

                foreach (int target in targets) {
                    if (!indexOf.TryGetValue(target, out int resolved) || resolved == offsets.Count) {
                        skipped = "branch to " + target;
                        return null;
                    }
                }

                operands[index] = opcodes[index].OperandType == OperandType.InlineSwitch
                    ? targets.Select(MarkAt).ToArray()
                    : MarkAt(targets[0]);
            }

            ComposedInstruction[] instructions = new ComposedInstruction[opcodes.Count];
            for (int index = 0; index < opcodes.Count; index++) {
                CodeInstruction instruction = new CodeInstruction(opcodes[index], operands[index]);
                if (marks.TryGetValue(offsets[index], out Label mark)) {
                    instruction.labels.Add(mark);
                }

                instructions[index] = new ComposedInstruction(instruction, offsets[index], null);
            }

            List<ComposedClause> clauses = [];
            foreach (ExceptionHandlingClause clause in reflected.ExceptionHandlingClauses) {
                if (!indexOf.TryGetValue(clause.TryOffset, out int tryStart)
                    || !indexOf.TryGetValue(clause.TryOffset + clause.TryLength, out int tryEnd)
                    || !indexOf.TryGetValue(clause.HandlerOffset, out int handlerStart)
                    || !indexOf.TryGetValue(clause.HandlerOffset + clause.HandlerLength, out int handlerEnd)) {
                    skipped = "clause boundary off an instruction";
                    return null;
                }

                int filterStart = -1;
                Type? caught = null;
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause) {
                    try {
                        caught = clause.CatchType;
                    }
                    catch (Exception unresolved) {
                        skipped = "CatchType " + unresolved.GetType().Name;
                        return null;
                    }

                    if (caught is null) {
                        skipped = "catch with no type";
                        return null;
                    }
                }
                else if (clause.Flags == ExceptionHandlingClauseOptions.Filter && !indexOf.TryGetValue(clause.FilterOffset, out filterStart)) {
                    skipped = "filter start off an instruction";
                    return null;
                }

                ClauseKind kind = clause.Flags switch {
                    ExceptionHandlingClauseOptions.Clause => ClauseKind.Catch,
                    ExceptionHandlingClauseOptions.Filter => ClauseKind.Filter,
                    ExceptionHandlingClauseOptions.Finally => ClauseKind.Finally,
                    _ => ClauseKind.Fault,
                };

                clauses.Add(new ComposedClause(kind, tryStart, tryEnd, handlerStart, handlerEnd, caught, filterStart));
            }

            ComposedLocal[] locals = reflected.LocalVariables
                .OrderBy(local => local.LocalIndex)
                .Select(local => new ComposedLocal(local.LocalType, local.IsPinned))
                .ToArray();

            return new ComposedBody(instructions, locals, clauses, reflected.InitLocals);
        }
    }
}

public sealed class GameCorpusFactAttribute : Xunit.FactAttribute {
    public GameCorpusFactAttribute() {
        if (!File.Exists(RealBodies.GameAssemblyPath)) {
            Skip = $"the '{RealBodies.GameCorpus}' corpus is absent: no game assembly at '{RealBodies.GameAssemblyPath}'. " +
                $"Set {RealBodies.GamePathVariable} to an existing Assembly-CSharp.dll to run this check over real game bodies.";
        }
    }
}

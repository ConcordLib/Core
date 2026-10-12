using System.Reflection;
using System.Reflection.Emit;
using dnlib.DotNet;
using DnBody = dnlib.DotNet.Emit.CilBody;
using DnHandler = dnlib.DotNet.Emit.ExceptionHandler;
using DnHandlerType = dnlib.DotNet.Emit.ExceptionHandlerType;
using DnInstruction = dnlib.DotNet.Emit.Instruction;
using DnLocal = dnlib.DotNet.Emit.Local;
using DnOpCode = dnlib.DotNet.Emit.OpCode;
using DnOpCodes = dnlib.DotNet.Emit.OpCodes;
using DnParameter = dnlib.DotNet.Parameter;
using DnWriter = dnlib.DotNet.Writer.MethodBodyWriter;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.Tests;

internal sealed class CaughtType : TypeRefUser {
    internal CaughtType(Type type) : base(null, "caught", type.Name) {
        Caught = type;
    }

    internal Type Caught { get; }
}

internal sealed class OracleTokens : dnlib.DotNet.Writer.ITokenProvider {
    private readonly ITokenSource tokens;
    private readonly uint localsToken;

    internal OracleTokens(ITokenSource tokens, uint localsToken) {
        this.tokens = tokens;
        this.localsToken = localsToken;
    }

    internal List<string> Complaints { get; } = [];

    public MDToken GetToken(object o) {
        return new MDToken((uint)(o switch {
            CaughtType caught => tokens.TokenFor(caught.Caught),
            Type type => tokens.TokenFor(type),
            FieldInfo field => tokens.TokenFor(field),
            MethodBase method => tokens.TokenFor(method),
            string literal => tokens.TokenFor(literal),
            _ => throw new NotSupportedException($"the oracle has no token for a {o?.GetType().ToString() ?? "null"} operand"),
        }));
    }

    public MDToken GetToken(IList<TypeSig> locals, uint original) {
        return new MDToken(locals.Count == 0 ? original : localsToken);
    }

    public void Error(string message) {
        Complaints.Add(message);
    }
}

internal static class DnlibOracle {
    internal const uint LocalsToken = 0x11000001;

    private static readonly Dictionary<ushort, DnOpCode> ByValue = typeof(DnOpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(DnOpCode))
        .Select(field => (DnOpCode)field.GetValue(null)!)
        .GroupBy(opcode => (ushort)opcode.Value)
        .ToDictionary(group => group.Key, group => group.First());

    internal static byte[] Encode(ComposedBody body, ITokenSource tokens, int maxStack, out List<string> complaints) {
        DnInstruction[] stream = new DnInstruction[body.Instructions.Count];
        for (int index = 0; index < stream.Length; index++) {
            stream[index] = new DnInstruction(ByValue[(ushort)body.Instructions[index].Instruction.opcode.Value]);
        }

        Dictionary<Label, int> targets = [];
        for (int index = 0; index < body.Instructions.Count; index++) {
            foreach (Label label in body.Instructions[index].Instruction.labels) {
                targets[label] = index;
            }
        }

        for (int index = 0; index < stream.Length; index++) {
            CodeInstruction instruction = body.Instructions[index].Instruction;
            OpCode opcode = instruction.opcode;
            object? operand = instruction.operand;
            stream[index].Operand = opcode.OperandType switch {
                OperandType.InlineNone => null,
                OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget => stream[targets[(Label)operand!]],
                OperandType.InlineSwitch => ((Label[])operand!).Select(label => stream[targets[label]]).ToList(),
                OperandType.ShortInlineVar or OperandType.InlineVar => new DnParameter(Slot(operand)),
                OperandType.ShortInlineI => opcode == OpCodes.Ldc_I4_S ? Convert.ToSByte(operand) : Convert.ToByte(operand),
                OperandType.InlineI => unchecked((int)Convert.ToInt64(operand)),
                OperandType.InlineI8 => Convert.ToInt64(operand),
                OperandType.ShortInlineR => Convert.ToSingle(operand),
                OperandType.InlineR => Convert.ToDouble(operand),
                _ => operand,
            };
        }

        DnBody dn = new DnBody {
            KeepOldMaxStack = true,
            MaxStack = (ushort)maxStack,
            InitLocals = body.InitLocals,
        };

        foreach (DnInstruction instruction in stream) {
            dn.Instructions.Add(instruction);
        }

        if (body.Locals.Count > 0) {
            dn.LocalVarSigTok = LocalsToken;
            for (int slot = 0; slot < body.Locals.Count; slot++) {
                dn.Variables.Add(new DnLocal(new CorLibTypeSig(new TypeRefUser(null, "System", "Object"), ElementType.Object)));
            }
        }

        foreach (int index in BodyEncoder.InnermostFirst(body.Clauses)) {
            ComposedClause clause = body.Clauses[index];
            DnHandler handler = new DnHandler(clause.Kind switch {
                ClauseKind.Catch => DnHandlerType.Catch,
                ClauseKind.Filter => DnHandlerType.Filter,
                ClauseKind.Finally => DnHandlerType.Finally,
                _ => DnHandlerType.Fault,
            }) {
                TryStart = Boundary(stream, clause.TryStart),
                TryEnd = Boundary(stream, clause.TryEnd),
                HandlerStart = Boundary(stream, clause.HandlerStart),
                HandlerEnd = Boundary(stream, clause.HandlerEnd),
            };

            if (clause.Kind == ClauseKind.Filter) {
                handler.FilterStart = Boundary(stream, clause.FilterStart);
            }

            if (clause.Kind == ClauseKind.Catch) {
                handler.CatchType = new CaughtType(clause.CatchType!);
            }

            dn.ExceptionHandlers.Add(handler);
        }

        dn.SimplifyBranches();
        OracleTokens provider = new OracleTokens(tokens, LocalsToken);
        DnWriter writer = new DnWriter(provider, dn, true);
        writer.Write();
        complaints = provider.Complaints;
        return writer.GetFullMethodBody();
    }

    private static DnInstruction? Boundary(DnInstruction[] stream, int index) {
        return index < stream.Length ? stream[index] : null;
    }

    private static int Slot(object? operand) {
        return operand switch {
            LocalRef local => local.Index,
            _ => Convert.ToInt32(operand),
        };
    }
}

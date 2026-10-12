using System.Reflection;
using System.Reflection.Emit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.Tests;

internal readonly record struct Region(
    ExceptionRegionKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    int FilterOffset,
    int CatchToken);

internal readonly record struct Decoded(
    int MaxStack,
    int CodeSize,
    bool InitLocals,
    int LocalSignatureToken,
    bool LocalSignatureIsNil,
    byte[] Il,
    Region[] Regions);

internal readonly record struct Step(int Offset, OpCode OpCode, int Size, long Operand);

internal static class ReadBack {
    private static readonly Dictionary<ushort, OpCode> ByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => (ushort)opcode.Value);

    internal static unsafe Decoded Body(byte[] bytes) {
        fixed (byte* start = bytes) {
            MethodBodyBlock block = MethodBodyBlock.Create(new BlobReader(start, bytes.Length));
            Region[] regions = block.ExceptionRegions.Select(region => new Region(
                region.Kind,
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : -1,
                region.Kind == ExceptionRegionKind.Catch ? MetadataTokens.GetToken(region.CatchType) : 0)).ToArray();

            return new Decoded(
                block.MaxStack,
                block.GetILBytes()!.Length,
                block.LocalVariablesInitialized,
                MetadataTokens.GetToken(block.LocalSignature),
                block.LocalSignature.IsNil,
                block.GetILBytes()!,
                regions);
        }
    }

    internal static unsafe int LocalCount(byte[] signature) {
        fixed (byte* start = signature) {
            BlobReader reader = new BlobReader(start, signature.Length);
            byte kind = reader.ReadByte();
            return kind == 0x07 ? reader.ReadCompressedInteger() : -1;
        }
    }

    internal static List<Step> Steps(byte[] il) {
        List<Step> steps = new List<Step>();
        int at = 0;
        while (at < il.Length) {
            int start = at;
            ushort value = il[at] == 0xfe ? (ushort)(0xfe00 | il[at + 1]) : il[at];
            at += il[start] == 0xfe ? 2 : 1;
            OpCode opcode = ByValue[value];
            int operandSize = SizeOf(opcode, il, at);
            long operand = operandSize switch {
                1 => (sbyte)il[at],
                2 => BitConverter.ToInt16(il, at),
                4 => BitConverter.ToInt32(il, at),
                8 => BitConverter.ToInt64(il, at),
                _ => 0,
            };

            at += operandSize;
            steps.Add(new Step(start, opcode, at - start, operand));
        }

        return steps;
    }

    private static int SizeOf(OpCode opcode, byte[] il, int at) {
        return opcode.OperandType switch {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineI => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
            _ => throw new NotSupportedException($"No operand size for '{opcode.Name}'."),
        };
    }
}

#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static partial class SupportMatrix
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodeTable();

        internal static string Validate(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo)
        {
            string hostReason = ValidateHost(target, added, patchInfo);
            if (hostReason != null)
            {
                return hostReason;
            }

            try
            {
                WrapperComposer.RejectSharedGenericInstantiation(target, added);
            }
            catch (ConcordEmitException ex)
            {
                return $"Target {target.Name} is a shared reference-type generic instantiation: {ex.Message}";
            }

            return null;
        }

        internal static bool BodyCalls(MethodBase method, Func<MethodBase, bool> predicate)
        {
            MethodBody body = method.GetMethodBody();
            if (body == null)
            {
                return false;
            }

            try
            {
                byte[] il = body.GetILAsByteArray();
                int position = 0;
                while (position < il.Length)
                {
                    short value = il[position];
                    position++;
                    if (value == 0xFE)
                    {
                        value = (short)(0xFE00 | il[position]);
                        position++;
                    }

                    if (!OpCodesByValue.TryGetValue(value, out OpCode opcode))
                    {
                        return true;
                    }

                    if (opcode.OperandType == OperandType.InlineMethod)
                    {
                        MethodBase called = ResolveCalled(method, BitConverter.ToInt32(il, position));
                        if (called != null && predicate(called))
                        {
                            return true;
                        }
                    }

                    position += OperandSize(opcode, il, position);
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static partial string ValidateHost(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo);

        private static MethodBase ResolveCalled(MethodBase method, int token)
        {
            try
            {
                return method.Module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(), method is MethodInfo genericSource ? genericSource.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static Dictionary<short, OpCode> BuildOpCodeTable()
        {
            Dictionary<short, OpCode> table = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode code)
                {
                    table[code.Value] = code;
                }
            }

            return table;
        }

        private static int OperandSize(OpCode opcode, byte[] il, int position)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return 4 + (BitConverter.ToInt32(il, position) * 4);
                default:
                    return 4;
            }
        }
    }
}

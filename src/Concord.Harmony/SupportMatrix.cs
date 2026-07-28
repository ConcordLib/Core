#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static class SupportMatrix
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodeTable();

        internal static string Validate(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo)
        {
            if (target.IsConstructor)
            {
                foreach (Injection injection in added)
                {
                    if (injection.At is InjectAt.Around)
                    {
                        return $"Constructor {target.Name} cannot receive whole-method Around patch (uninitialized this under Harmony)";
                    }
                }
            }

            if (HasInnerPatches(patchInfo))
            {
                return $"Target {target.Name} has Harmony 2.4 inner patches (not composable with Concord detours)";
            }

            // Harmony hands us the stream for the method it was asked to patch. That lines up with a
            // PatchBody.Declared injection, but a PatchBody.StateMachine one composes onto a MoveNext
            // Harmony never opened, so there is nothing to compose the two streams onto.
            foreach (Injection injection in added)
            {
                if (injection.Body == PatchBody.StateMachine &&
                    WrapperComposer.ResolveStateMachineTarget(target) != target)
                {
                    return $"Injection {injection.InjectionMethod.Name} selected PatchBody.StateMachine on async/iterator method {target.Name} (stream source is the declared method)";
                }
            }

            try
            {
                WrapperComposer.RejectSharedGenericInstantiation(target);
            }
            catch (ConcordEmitException ex)
            {
                return $"Target {target.Name} is a shared reference-type generic instantiation: {ex.Message}";
            }

            foreach (Injection injection in added)
            {
                if (CallsGetExecutingAssembly(injection.InjectionMethod))
                {
                    return $"Injection method {injection.InjectionMethod.Name} calls Assembly.GetExecutingAssembly (Harmony rewrites this to target's assembly, breaking observation)";
                }
            }

            return null;
        }

        internal static bool HasInnerPatches(Patches patchInfo)
        {
            if (patchInfo == null)
            {
                return false;
            }

            return patchInfo.InnerPrefixes.Count > 0 || patchInfo.InnerPostfixes.Count > 0;
        }

        internal static bool CallsGetExecutingAssembly(MethodBase method)
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

                    if (opcode.OperandType == OperandType.InlineMethod
                        && TargetsGetExecutingAssembly(method, BitConverter.ToInt32(il, position)))
                    {
                        return true;
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

        /// <summary>
        ///     Resolves an InlineMethod operand token and reports whether it names
        ///     <c>Assembly.GetExecutingAssembly</c>.
        /// </summary>
        /// <remarks>
        ///     A token that will not resolve is treated as "not a match" rather than propagating:
        ///     ResolveMethod throws ArgumentException for tokens outside this module's scope, which is
        ///     an ordinary outcome when walking a body that references another assembly.
        /// </remarks>
        private static bool TargetsGetExecutingAssembly(MethodBase method, int token)
        {
            MethodBase resolved;
            try
            {
                resolved = method.Module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(), method is MethodInfo genericSource ? genericSource.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                resolved = null;
            }

            return resolved != null
                && resolved.Name == "GetExecutingAssembly"
                && resolved.DeclaringType == typeof(System.Reflection.Assembly);
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

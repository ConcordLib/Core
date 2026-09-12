using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Resolves an injection's <c>BoundArguments</c> into the literal loads that replace its
///     <see cref="BoundAttribute" /> parameters while its body is copied.
/// </summary>
internal static class BoundConstants {
    private static MethodInfo TypeFromHandle =>
        typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!;

    internal static Dictionary<int, object?> Resolve(MethodBase injectionMethod, IReadOnlyDictionary<string, object?>? boundArguments) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;
        Dictionary<int, object?> resolved = new Dictionary<int, object?>();

        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].GetCustomAttribute<BoundAttribute>() is null) {
                continue;
            }

            string name = parameters[i].Name!;
            if (boundArguments is null || !boundArguments.TryGetValue(name, out object? value)) {
                throw new ConcordEmitException(
                    "CONC136",
                    "Injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name + "' declares a [Bound] parameter '" + name +
                    "' but the injection supplied no value for it. Set Injection.BoundArguments[\"" + name + "\"].");
            }

            RejectUnbindable(injectionMethod, parameters[i], value);
            resolved[i + offset] = value;
        }

        if (boundArguments is not null) {
            RejectUnknownNames(injectionMethod, parameters, boundArguments);
        }

        return resolved;
    }

    internal static Dictionary<int, VariableDefinition> SpillToLocals(
        Dictionary<int, object?> values,
        MethodBase injectionMethod,
        Mono.Cecil.Cil.MethodBody destination,
        ModuleDefinition module,
        List<Instruction> prologue) {
        Dictionary<int, VariableDefinition> locals = new Dictionary<int, VariableDefinition>(values.Count);
        if (values.Count == 0) {
            return locals;
        }

        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;

        foreach (KeyValuePair<int, object?> entry in values) {
            VariableDefinition local = new VariableDefinition(module.ImportReference(parameters[entry.Key - offset].ParameterType));
            destination.Variables.Add(local);
            destination.InitLocals = true;
            prologue.AddRange(Load(entry.Value, module));
            prologue.Add(Instruction.Create(OpCodes.Stloc, local));
            locals[entry.Key] = local;
        }

        return locals;
    }

    internal static void RejectDeclarations(MethodBase injectionMethod, string position) {
        foreach (ParameterInfo parameter in injectionMethod.GetParameters()) {
            if (parameter.GetCustomAttribute<BoundAttribute>() is not null) {
                throw new ConcordEmitException(
                    "CONC137",
                    "[Bound] parameter '" + parameter.Name + "' on '" + injectionMethod.Name + "' is not supported at " + position + ".");
            }
        }
    }

    internal static List<Instruction> Load(object? value, ModuleDefinition module) {
        if (value is null) {
            return [Instruction.Create(OpCodes.Ldnull)];
        }

        if (value is Type type) {
            return [
                Instruction.Create(OpCodes.Ldtoken, module.ImportReference(type)),
                Instruction.Create(OpCodes.Call, module.ImportReference(TypeFromHandle)),
            ];
        }

        if (value is string text) {
            return [Instruction.Create(OpCodes.Ldstr, text)];
        }

        object literal = value.GetType().IsEnum ? Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())) : value;

        return literal switch {
            bool flag => [Instruction.Create(OpCodes.Ldc_I4, flag ? 1 : 0)],
            char c => [Instruction.Create(OpCodes.Ldc_I4, c)],
            sbyte n => [Instruction.Create(OpCodes.Ldc_I4, n)],
            byte n => [Instruction.Create(OpCodes.Ldc_I4, n)],
            short n => [Instruction.Create(OpCodes.Ldc_I4, n)],
            ushort n => [Instruction.Create(OpCodes.Ldc_I4, n)],
            int n => [Instruction.Create(OpCodes.Ldc_I4, n)],
            uint n => [Instruction.Create(OpCodes.Ldc_I4, unchecked((int)n))],
            long n => [Instruction.Create(OpCodes.Ldc_I8, n)],
            ulong n => [Instruction.Create(OpCodes.Ldc_I8, unchecked((long)n))],
            float n => [Instruction.Create(OpCodes.Ldc_R4, n)],
            double n => [Instruction.Create(OpCodes.Ldc_R8, n)],
            _ => throw new ConcordEmitException("CONC137", "Bound value of type '" + literal.GetType().Name + "' cannot be emitted as a literal."),
        };
    }

    private static void RejectUnbindable(MethodBase injectionMethod, ParameterInfo parameter, object? value) {
        Type declared = parameter.ParameterType;
        if (declared.IsByRef) {
            throw new ConcordEmitException(
                "CONC137",
                "[Bound] parameter '" + parameter.Name + "' on '" + injectionMethod.Name + "' is byref. A bound value is emitted as a literal and has no address.");
        }

        if (value is null) {
            if (declared.IsValueType && Nullable.GetUnderlyingType(declared) is null) {
                throw new ConcordEmitException(
                    "CONC137",
                    "[Bound] parameter '" + parameter.Name + "' on '" + injectionMethod.Name + "' is a non-nullable value type and cannot be bound to null.");
            }

            return;
        }

        if (!declared.IsInstanceOfType(value)) {
            throw new ConcordEmitException(
                "CONC137",
                "[Bound] parameter '" + parameter.Name + "' on '" + injectionMethod.Name + "' is declared as '" + declared.Name + "' but was bound to a '" +
                value.GetType().Name + "'.");
        }
    }

    private static void RejectUnknownNames(MethodBase injectionMethod, ParameterInfo[] parameters, IReadOnlyDictionary<string, object?> boundArguments) {
        foreach (string name in boundArguments.Keys) {
            bool matched = false;
            foreach (ParameterInfo parameter in parameters) {
                if (parameter.Name == name && parameter.GetCustomAttribute<BoundAttribute>() is not null) {
                    matched = true;
                    break;
                }
            }

            if (!matched) {
                throw new ConcordEmitException(
                    "CONC136",
                    "Injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name + "' was given a bound value for '" + name +
                    "', which is not a [Bound] parameter on it.");
            }
        }
    }
}

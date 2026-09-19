using System.Reflection;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

internal static class LocalResolver {
    /// <summary>
    ///     Resolves every <see cref="LocalAttribute" /> parameter of the injection to a wrapper local and
    ///     folds the result into <paramref name="captureBinding" />, which has the same shape.
    /// </summary>
    internal static IReadOnlyDictionary<int, VariableDefinition>? Bind(
        InjectionCopyRequest request,
        ProtocolLocals locals,
        IReadOnlyDictionary<int, VariableDefinition>? captureBinding) {
        ParameterInfo[] parameters = request.InjectionMethod.GetParameters();
        int argOffset = request.InjectionMethod.IsStatic ? 0 : 1;
        Dictionary<int, VariableDefinition>? merged = null;

        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].GetCustomAttribute<LocalAttribute>() is null) {
                continue;
            }

            if (merged is null) {
                merged = new Dictionary<int, VariableDefinition>();
                if (captureBinding is not null) {
                    foreach (KeyValuePair<int, VariableDefinition> entry in captureBinding) {
                        merged[entry.Key] = entry.Value;
                    }
                }
            }

            merged[i + argOffset] = Resolve(
                request.Destination.Body,
                locals.RawLocalCount,
                locals.SearchLocalCount,
                parameters[i],
                request.InjectionMethod,
                request.Target);
        }

        return merged ?? captureBinding;
    }

    internal static VariableDefinition Resolve(
        MethodBody body,
        int rawCount,
        int searchCount,
        ParameterInfo parameter,
        MethodBase injectionMethod,
        MethodBase target) {
        LocalAttribute local = parameter.GetCustomAttribute<LocalAttribute>()!;
        Type wanted = parameter.ParameterType;

        if (local.Index >= 0) {
            return ByIndex(body, searchCount, local.Index, wanted, injectionMethod, target);
        }

        List<VariableDefinition> candidates = new List<VariableDefinition>();
        for (int slot = 0; slot < searchCount; slot++) {
            if (Matches(body.Variables[slot], wanted)) {
                candidates.Add(body.Variables[slot]);
            }
        }

        if (local.Ordinal > 0) {
            if (local.Ordinal > candidates.Count) {
                throw Fail("CONC148", injectionMethod, target,
                    $"selects occurrence {local.Ordinal} of '{wanted}', but the body holds {candidates.Count}.");
            }

            return candidates[(int)local.Ordinal - 1];
        }

        if (candidates.Count == 0) {
            throw Fail("CONC148", injectionMethod, target, $"matches no local of type '{wanted}'.");
        }

        if (candidates.Count > 1) {
            throw Fail("CONC147", injectionMethod, target,
                $"matches {candidates.Count} locals of type '{wanted}': {Describe(candidates, rawCount)}. " +
                "Set Ordinal to pick one.");
        }

        return candidates[0];
    }

    private static VariableDefinition ByIndex(
        MethodBody body, int searchCount, int index, Type wanted,
        MethodBase injectionMethod, MethodBase target) {
        if (index >= searchCount) {
            throw Fail("CONC149", injectionMethod, target,
                $"asks for slot {index}, but the body holds {searchCount} locals.");
        }

        VariableDefinition slot = body.Variables[index];
        if (!Matches(slot, wanted)) {
            throw Fail("CONC150", injectionMethod, target,
                $"declares slot {index} as '{wanted}', but that slot is '{slot.VariableType.FullName}'.");
        }

        return slot;
    }

    private static bool Matches(VariableDefinition slot, Type wanted) {
        return slot.VariableType.FullName == wanted.FullName;
    }

    private static string Describe(List<VariableDefinition> candidates, int rawCount) {
        return string.Join(", ", candidates.Select(c =>
            $"slot {c.Index} ({(c.Index < rawCount ? "target body" : "transpiler-added")})"));
    }

    private static ConcordEmitException Fail(string code, MethodBase injectionMethod, MethodBase target, string tail) {
        return new ConcordEmitException(code,
            $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on " +
            $"'{target.DeclaringType?.Name}.{target.Name}' {tail}");
    }
}

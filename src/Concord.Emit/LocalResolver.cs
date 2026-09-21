using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
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
            LocalAttribute? bound = BoundSelector(parameters[i]);
            if (bound is null) {
                continue;
            }

            // Both attributes are AttributeTargets.Parameter, so [Capture(1), Local] compiles. The
            // capture is already spilled by now, and folding the local in on the same key would throw
            // that spill away and read the local instead, with nothing said about it.
            if (captureBinding is not null && captureBinding.ContainsKey(i + argOffset)) {
                throw Fail("CONC165", request.InjectionMethod, request.Target, parameters[i].Name,
                    "carries both [Capture] and [Local]. One binds an argument of the matched call, the other a " +
                    "local of the target body; keep one and delete the other.");
            }

            merged ??= Seed(captureBinding);
            merged[i + argOffset] = Resolve(
                request.Destination.Body,
                locals,
                parameters[i],
                bound,
                request.InjectionMethod,
                request.Target);
        }

        return merged ?? captureBinding;
    }

    /// <summary>
    ///     The argument indices <see cref="Bind" /> resolves to a real target slot. Taking the address of
    ///     one reaches the target's own local, so the copier has to copy it first.
    /// </summary>
    /// <param name="injectionMethod">The injection method whose parameters are scanned.</param>
    internal static HashSet<int> BoundArgIndices(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int argOffset = injectionMethod.IsStatic ? 0 : 1;
        HashSet<int> indices = new HashSet<int>();

        for (int i = 0; i < parameters.Length; i++) {
            if (BoundSelector(parameters[i]) is not null) {
                indices.Add(i + argOffset);
            }
        }

        return indices;
    }

    internal static VariableDefinition Resolve(
        MethodBody body,
        ProtocolLocals locals,
        ParameterInfo parameter,
        LocalAttribute local,
        MethodBase injectionMethod,
        MethodBase target) {
        return Resolve(
            body,
            locals,
            new LocalSelector(parameter.ParameterType, local.Ordinal, local.Index, local.Name),
            injectionMethod,
            target,
            parameter.Name);
    }

    /// <summary>
    ///     Resolves the same selector triple a <see cref="LocalAttribute" /> carries, for callers that read
    ///     it from an <see cref="InjectAt.Local" /> position instead of a parameter.
    /// </summary>
    /// <param name="body">The wrapper body whose locals are being selected from.</param>
    /// <param name="locals">The wrapper's protocol locals, which bound the searchable slot range.</param>
    /// <param name="selector">The type and selector triple picking the local.</param>
    /// <param name="injectionMethod">The injection method, used for diagnostic messages.</param>
    /// <param name="target">The original method being patched, used for diagnostic messages.</param>
    /// <param name="parameterName">The injection parameter this selector came from, or null when it came from a position.</param>
    internal static VariableDefinition Resolve(
        MethodBody body,
        ProtocolLocals locals,
        LocalSelector selector,
        MethodBase injectionMethod,
        MethodBase target,
        string? parameterName = null) {
        Type wanted = selector.Wanted;
        uint ordinal = selector.Ordinal;
        int index = selector.Index;
        string? name = selector.Name;
        int searchCount = locals.SearchLocalCount;

        RejectConflictingSelectors(ordinal, index, name, injectionMethod, target, parameterName);

        if (index >= 0) {
            return Referenced(ByIndex(body, searchCount, index, wanted, injectionMethod, target, parameterName), body, locals, injectionMethod, target, parameterName);
        }

        if (name is not null) {
            return Referenced(ByName(body, searchCount, name, wanted, injectionMethod, target, parameterName), body, locals, injectionMethod, target, parameterName);
        }

        List<VariableDefinition> candidates = new List<VariableDefinition>();
        for (int slot = 0; slot < searchCount; slot++) {
            if (Matches(body.Variables[slot], wanted)) {
                candidates.Add(body.Variables[slot]);
            }
        }

        if (ordinal > 0) {
            if (ordinal > candidates.Count) {
                throw NoMatch(body, searchCount, injectionMethod, target, parameterName,
                    $"selects occurrence {ordinal} of '{wanted}', but the body holds {candidates.Count}.");
            }

            return Referenced(candidates[(int)ordinal - 1], body, locals, injectionMethod, target, parameterName);
        }

        if (candidates.Count == 0) {
            throw NoMatch(body, searchCount, injectionMethod, target, parameterName, $"matches no local of type '{wanted}'.");
        }

        if (candidates.Count > 1) {
            throw Fail("CONC147", injectionMethod, target, parameterName,
                $"matches {candidates.Count} locals of type '{wanted}': {Describe(candidates, locals.RawLocalCount)}. " +
                "Set Ordinal to pick one.",
                candidates.Any(candidate => candidate.Index >= locals.RawLocalCount));
        }

        return Referenced(candidates[0], body, locals, injectionMethod, target, parameterName);
    }

    // The three At.Local failures in WrapperComposer come from a position rather than a parameter, and
    // they share this opener so two mods on one target are still told apart.
    internal static string Opener(MethodBase injectionMethod, MethodBase target) {
        return $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on " +
            $"'{target.DeclaringType?.Name}.{target.Name}'";
    }

    private static Dictionary<int, VariableDefinition> Seed(IReadOnlyDictionary<int, VariableDefinition>? captureBinding) {
        Dictionary<int, VariableDefinition> seeded = new Dictionary<int, VariableDefinition>();
        if (captureBinding is not null) {
            foreach (KeyValuePair<int, VariableDefinition> entry in captureBinding) {
                seeded[entry.Key] = entry.Value;
            }
        }

        return seeded;
    }

    // Twin of the analyzer's CONCORD036. Index, Name and Ordinal each pick a slot a different way, so
    // two of them set leaves no single answer and the priority order below would drop the losers
    // silently. PatchBuilder.Local takes all three as runtime values the analyzer cannot read, so the
    // runtime half is the only gate on that route.
    private static void RejectConflictingSelectors(
        uint ordinal, int index, string? name,
        MethodBase injectionMethod, MethodBase target, string? parameterName) {
        List<string> set = new List<string>(3);
        if (ordinal > 0) {
            set.Add("Ordinal");
        }

        if (index >= 0) {
            set.Add("Index");
        }

        if (name is not null) {
            set.Add("Name");
        }

        if (set.Count < 2) {
            return;
        }

        string named = string.Join(", ", set.Take(set.Count - 1)) + " and " + set[set.Count - 1];
        throw Fail("CONC145", injectionMethod, target, parameterName,
            $"sets {named}; keep one and delete the rest.");
    }

    // A slot no instruction names has no live value. Under an Around it also gets no clone, so it
    // would read zero and stay silent about it.
    private static VariableDefinition Referenced(
        VariableDefinition slot, MethodBody body, ProtocolLocals locals,
        MethodBase injectionMethod, MethodBase target, string? parameterName) {
        int index = body.Variables.IndexOf(slot);
        if (locals.ReferencedSlots.Contains(index)) {
            return slot;
        }

        throw Fail("CONC162", injectionMethod, target, parameterName,
            $"selects slot {index}, but no instruction in the composed body reads or writes it.");
    }

    // The author asked for a type nothing in range carries. If an unbindable slot is why, say so:
    // "slot 2 is a pinned local" beats "matches no local". Both the plain and the Ordinal path come
    // through here so they report the same way.
    private static ConcordEmitException NoMatch(
        MethodBody body, int searchCount, MethodBase injectionMethod, MethodBase target, string? parameterName, string tail) {
        for (int slot = 0; slot < searchCount; slot++) {
            string? kind = ExcludedKind(body.Variables[slot]);
            if (kind is not null) {
                return Fail("CONC153", injectionMethod, target, parameterName,
                    $"{tail} Slot {slot} is a {kind} local and cannot be bound.");
            }
        }

        return Fail("CONC148", injectionMethod, target, parameterName, tail);
    }

    // A name is not a key in either direction. A Debug build gives two `for (int i ...)` loops one
    // name across two slots; a Release build shares one slot between same-typed locals in disjoint
    // scopes, so one slot carries two names. Only a single distinct slot is unambiguous.
    private static VariableDefinition ByName(
        MethodBody body, int searchCount, string name, Type wanted,
        MethodBase injectionMethod, MethodBase target, string? parameterName) {
        IReadOnlyList<(int Slot, string Name, int ScopeStart, int ScopeEnd)> entries =
            LocalNames.For(target, out string? reason);
        if (reason is not null) {
            throw Fail("CONC151", injectionMethod, target, parameterName,
                $"selects local '{name}' by name, but the target's symbols could not be read: {reason}.");
        }

        List<(int Slot, string Name, int ScopeStart, int ScopeEnd)> matched =
            entries.Where(entry => entry.Name == name).ToList();
        int[] slots = matched.Select(entry => entry.Slot).Distinct().OrderBy(slot => slot).ToArray();

        if (slots.Length == 0) {
            string known = entries.Count == 0
                ? "the symbols name no locals in it"
                : "the symbols name " + string.Join(", ", entries.Select(entry => "'" + entry.Name + "'").Distinct());
            throw Fail("CONC152", injectionMethod, target, parameterName, $"selects local '{name}', but {known}.");
        }

        if (slots.Length > 1) {
            throw Fail("CONC163", injectionMethod, target, parameterName,
                $"selects local '{name}', which the symbols spread across {slots.Length} slots: " +
                string.Join(", ", slots.Select(slot => $"slot {slot} ({ScopeOf(matched, slot)})")) +
                ". Set Ordinal or Index to pick one.");
        }

        return ByIndex(body, searchCount, slots[0], wanted, injectionMethod, target, parameterName);
    }

    // One slot can carry the name in more than one scope, so print every range rather than the
    // first: showing one understates how much of the body the ambiguity covers.
    private static string ScopeOf(List<(int Slot, string Name, int ScopeStart, int ScopeEnd)> matched, int slot) {
        return string.Join(" and ", matched
            .Where(candidate => candidate.Slot == slot)
            .Select(candidate => $"IL_{candidate.ScopeStart:X4} to " +
                (candidate.ScopeEnd == int.MaxValue ? "end of method" : "IL_" + candidate.ScopeEnd.ToString("X4"))));
    }

    private static VariableDefinition ByIndex(
        MethodBody body, int searchCount, int index, Type wanted,
        MethodBase injectionMethod, MethodBase target, string? parameterName) {
        if (index >= searchCount) {
            throw Fail("CONC149", injectionMethod, target, parameterName,
                $"asks for slot {index}, but the body holds {searchCount} locals.");
        }

        VariableDefinition slot = body.Variables[index];
        string? kind = ExcludedKind(slot);
        if (kind is not null) {
            throw Fail("CONC153", injectionMethod, target, parameterName,
                $"asks for slot {index}, which is a {kind} local and cannot be bound.");
        }

        Type found = SlotType(slot);
        if (found != wanted) {
            throw Fail("CONC150", injectionMethod, target, parameterName,
                $"declares slot {index} as '{wanted}', but that slot is '{found}'.");
        }

        return slot;
    }

    private static bool Matches(VariableDefinition slot, Type wanted) {
        return ExcludedKind(slot) is null && SlotType(slot) == wanted;
    }

    // A bare ldloc on a byref or pinned slot pushes an address the parameter would read as a value.
    private static string? ExcludedKind(VariableDefinition slot) {
        if (slot.IsPinned) {
            return "pinned";
        }

        return slot.VariableType is ByReferenceType ? "byref" : null;
    }

    // Cecil and reflection spell FullName differently for generics and nested types, so cross the
    // boundary once and compare canonical runtime types. No slot here is ever a generic parameter:
    // DynamicMethodDefinition refuses any method with ContainsGenericParameters, a closed
    // instantiation's locals come back already substituted, and ImportReference refuses a
    // generic-parameter Type, so DeclareLocal cannot add one either.
    private static Type SlotType(VariableDefinition slot) {
        return slot.VariableType.ResolveReflection();
    }

    private static string Describe(List<VariableDefinition> candidates, int rawCount) {
        return string.Join(", ", candidates.Select(c =>
            $"slot {c.Index} ({(c.Index < rawCount ? "target body" : "transpiler-added")})"));
    }

    // A LocalHandle parameter carries [Local] for its selectors, but its own type is the handle, not
    // the local's. LocalHandleLowering binds it from T instead.
    private static LocalAttribute? BoundSelector(ParameterInfo parameter) {
        LocalAttribute? local = parameter.GetCustomAttribute<LocalAttribute>();
        return local is not null && !LocalHandleLowering.IsLocalHandleType(parameter.ParameterType) ? local : null;
    }

    // An injection can carry two same-typed selectors, and without the parameter name every one of
    // these reads the same. Naming it costs a clause and says which declaration to go and fix.
    private static ConcordEmitException Fail(
        string code, MethodBase injectionMethod, MethodBase target, string? parameterName, string tail,
        bool transpilerAddedSlot = false) {
        string who = parameterName is null ? string.Empty : $" parameter '{parameterName}'";
        return new ConcordEmitException(code, Opener(injectionMethod, target) + $"{who} {tail}") {
            LocalBindingMethod = injectionMethod,
            BrokenByAForeignEdit = transpilerAddedSlot,
        };
    }
}

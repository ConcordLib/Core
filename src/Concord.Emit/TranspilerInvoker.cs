using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Invokes an author's transpiler method against a stream of <see cref="CodeInstruction" />s.
///     Each distinct method is validated and compiled to a cached delegate once, so repeated
///     composition - which re-runs on every patch and unpatch of a target - never falls back to
///     <see cref="MethodBase.Invoke(object, object[])" /> per call.
/// </summary>
internal static class TranspilerInvoker {
    private static readonly object CacheLock = new object();

    private static readonly Dictionary<MethodBase, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>> Cache =
        new Dictionary<MethodBase, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>>();

    /// <summary>
    ///     Runs <paramref name="transpiler" /> against <paramref name="input" />, fully materializing its
    ///     result before returning. This ensures an exception raised while enumerating a <c>yield</c>-based
    ///     result surfaces here as <c>CONC117</c> rather than escaping unwrapped at the caller's later
    ///     enumeration.
    /// </summary>
    /// <param name="transpiler">The author's transpiler method. Must be static and match one of the accepted signatures.</param>
    /// <param name="input">The instructions the transpiler rewrites.</param>
    /// <param name="context">Services the transpiler may use to mint labels and locals.</param>
    /// <returns>The transpiler's rewritten instruction sequence.</returns>
    /// <exception cref="ConcordEmitException">
    ///     Thrown with <c>CONC116</c> when <paramref name="transpiler" /> is not static or does not match an
    ///     accepted signature, or with <c>CONC117</c> when it throws or returns <see langword="null" />.
    /// </exception>
    internal static IEnumerable<CodeInstruction> Invoke(MethodBase transpiler, IEnumerable<CodeInstruction> input, TranspilerContext context) {
        Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>> compiled = GetOrCompile(transpiler);

        try {
            IEnumerable<CodeInstruction> result = compiled(input, context);
            if (result is null) {
                throw new ConcordEmitException(
                    "CONC117",
                    $"Transpiler '{transpiler.DeclaringType?.Name}.{transpiler.Name}' returned null instead of a sequence of instructions.");
            }

            return new List<CodeInstruction>(result);
        } catch (ConcordEmitException) {
            throw;
        } catch (Exception ex) {
            throw new ConcordEmitException(
                "CONC117",
                $"Transpiler '{transpiler.DeclaringType?.Name}.{transpiler.Name}' threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>> GetOrCompile(MethodBase transpiler) {
        lock (CacheLock) {
            if (Cache.TryGetValue(transpiler, out Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>? cached)) {
                return cached;
            }

            Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>> compiled = Compile(transpiler);
            Cache[transpiler] = compiled;
            return compiled;
        }
    }

    private static Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>> Compile(MethodBase transpiler) {
        if (!transpiler.IsStatic) {
            throw new ConcordEmitException(
                "CONC116",
                $"Transpiler method '{transpiler.DeclaringType?.Name}.{transpiler.Name}' must be static. A [Patch] declaration is abstract, so an instance transpiler can never be invoked.");
        }

        if (transpiler is not MethodInfo method || method.ReturnType != typeof(IEnumerable<CodeInstruction>)) {
            throw new ConcordEmitException(
                "CONC116",
                $"Transpiler method '{transpiler.DeclaringType?.Name}.{transpiler.Name}' must return IEnumerable<CodeInstruction>.");
        }

        ParameterInfo[] parameters = method.GetParameters();

        if (parameters.Length == 2
            && parameters[0].ParameterType == typeof(IEnumerable<CodeInstruction>)
            && parameters[1].ParameterType == typeof(ITranspilerContext)) {
            return (Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>)Delegate.CreateDelegate(
                typeof(Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>),
                method);
        }

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(IEnumerable<CodeInstruction>)) {
            Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> single =
                (Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>)Delegate.CreateDelegate(
                    typeof(Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>),
                    method);
            return (instructions, _) => single(instructions);
        }

        throw new ConcordEmitException(
            "CONC116",
            $"Transpiler method '{transpiler.DeclaringType?.Name}.{transpiler.Name}' must accept " +
            "(IEnumerable<CodeInstruction> instructions) or (IEnumerable<CodeInstruction> instructions, ITranspilerContext context).");
    }
}

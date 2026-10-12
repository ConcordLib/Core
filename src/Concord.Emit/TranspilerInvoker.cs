using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Invokes an author's transpiler method against a stream of <see cref="CodeInstruction" />s.
///     Each distinct method is validated and compiled to a cached delegate once, so repeated
///     composition - which re-runs on every patch and unpatch of a target - never falls back to
///     <see cref="MethodBase.Invoke(object, object[])" /> per call.
/// </summary>
public static class TranspilerInvoker {
    private static readonly object CacheLock = new object();

    private static readonly Dictionary<MethodBase, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>> Cache =
        new Dictionary<MethodBase, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>>();

    private static readonly List<Func<MethodInfo, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>?>> ForeignShapes =
        new List<Func<MethodInfo, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>?>>();

    /// <summary>
    ///     Registers a compiler for a transpiler signature Concord does not recognise itself, such as a
    ///     foreign patch library's. It returns <see langword="null" /> for a method it does not own.
    /// </summary>
    /// <param name="compiler">Maps a static method to a Concord-shaped delegate, or returns <see langword="null" /> to pass.</param>
    public static void AddShape(Func<MethodInfo, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>?> compiler) {
        if (compiler is null) {
            throw new ArgumentNullException(nameof(compiler));
        }

        lock (CacheLock) {
            ForeignShapes.Add(compiler);
        }
    }

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

        if (transpiler is MethodInfo method) {
            Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>? native = CompileNative(method);
            if (native is not null) {
                return native;
            }

            foreach (Func<MethodInfo, Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>?> shape in ForeignShapes) {
                Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>? foreign = shape(method);
                if (foreign is not null) {
                    return foreign;
                }
            }
        }

        throw new ConcordEmitException(
            "CONC116",
            $"Transpiler method '{transpiler.DeclaringType?.Name}.{transpiler.Name}' has signature {Describe(transpiler)}. " +
            "It must return IEnumerable<CodeInstruction> and accept (IEnumerable<CodeInstruction> instructions) or " +
            "(IEnumerable<CodeInstruction> instructions, ITranspilerContext context), or match a shape a foreign patch host registered.");
    }

    private static Func<IEnumerable<CodeInstruction>, ITranspilerContext, IEnumerable<CodeInstruction>>? CompileNative(MethodInfo method) {
        if (method.ReturnType != typeof(IEnumerable<CodeInstruction>)) {
            return null;
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

        return null;
    }

    private static string Describe(MethodBase method) {
        ParameterInfo[] parameters = method.GetParameters();
        string[] names = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            names[i] = TypeName(parameters[i].ParameterType);
        }

        string returnType = method is MethodInfo info ? TypeName(info.ReturnType) : "void";
        return returnType + " (" + string.Join(", ", names) + ")";
    }

    private static string TypeName(Type type) {
        if (!type.IsGenericType) {
            return type.Name;
        }

        Type[] arguments = type.GetGenericArguments();
        string[] names = new string[arguments.Length];
        for (int i = 0; i < arguments.Length; i++) {
            names[i] = TypeName(arguments[i]);
        }

        string bare = type.Name;
        int tick = bare.IndexOf('`');
        return (tick < 0 ? bare : bare.Substring(0, tick)) + "<" + string.Join(", ", names) + ">";
    }
}

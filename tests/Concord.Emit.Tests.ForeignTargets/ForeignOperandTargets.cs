namespace Concord.Emit.Tests.ForeignTargets;

/// <summary>
///     A parameterless void method defined in a separate assembly, for transpilers that INSERT a
///     direct <c>Call</c> operand referencing it - forcing Concord to import a fresh cross-module
///     <see cref="System.Reflection.MethodInfo" /> rather than one already baked into a copied body.
/// </summary>
public static class ForeignCallTarget {
    /// <summary>Gets or sets the number of times <see cref="Ping" /> ran.</summary>
    public static int Runs { get; set; }

    /// <summary>A parameterless void method a transpiler can insert a direct <c>Call</c> to.</summary>
    public static void Ping() {
        Runs++;
    }
}

/// <summary>A plain public static field in a separate assembly, for Ldsfld/Stsfld transpiler tests.</summary>
public static class ForeignFieldTarget {
    /// <summary>A real field (not a property) so inserted IL carries a raw FieldInfo operand.</summary>
    public static int Counter;
}

/// <summary>A constructible type in a separate assembly, for Newobj/Castclass transpiler tests.</summary>
public sealed class ForeignTypeTarget {
    /// <summary>Gets or sets the number of times a <see cref="ForeignTypeTarget" /> was constructed.</summary>
    public static int Constructed { get; set; }

    /// <summary>A plain instance field read back through Ldfld after construction.</summary>
    public int Marker = 7;

    /// <summary>Increments <see cref="Constructed" /> so an inserted Newobj is observable.</summary>
    public ForeignTypeTarget() {
        Constructed++;
    }
}

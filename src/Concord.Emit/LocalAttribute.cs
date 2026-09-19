namespace Concord;

/// <summary>
///     Binds an injection parameter to one of the target method's local variables.
/// </summary>
/// <remarks>
///     Set at most one selector. With none set, the parameter's own type selects the local, and a
///     target holding two locals of that type is rejected rather than guessed at.
///     <para>
///         A local in a loop holds whatever the last iteration left in it, or its default when the
///         collection was empty. Reading one from a late position such as <see cref="At.Return" />
///         gets that, not the value from any particular iteration. Under a Release build the same
///         slot may also have carried a different variable outside the selected one's scope.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LocalAttribute : Attribute {
    /// <summary>
    ///     The 1-based occurrence of the parameter's type among the target's locals, in slot order.
    ///     Zero means unset.
    /// </summary>
    public uint Ordinal { get; set; }

    /// <summary>
    ///     A raw local slot in the target body. Precise and brittle: any recompile of the target can
    ///     move it. Negative one means unset.
    /// </summary>
    public int Index { get; set; } = -1;

    /// <summary>
    ///     The local's source name, read from a pdb when one is present.
    /// </summary>
    /// <remarks>
    ///     The least portable selector, not the recommended one. It needs a pdb that matches the
    ///     loaded module, found next to the assembly's file on disk. A host that loads assemblies
    ///     from bytes leaves <c>Assembly.Location</c> empty, so nothing is found and the injection
    ///     is rejected with CONC151; an embedded pdb does not rescue that, because once the byte
    ///     array is consumed there is no way back to the PE debug directory. RimWorld is one such
    ///     host, and its <c>Assembly-CSharp</c> ships no pdb at all. There, <c>Name</c> only works
    ///     once an adapter registers a path resolver, and never for the game's own assembly.
    ///     <para>
    ///         A name also does not map to one slot on its own. A Debug build gives two
    ///         <c>for (int i ...)</c> loops one name across two slots, which is rejected with
    ///         CONC163; a Release build can share one slot between same-typed locals in disjoint
    ///         scopes, so a bound slot may have carried another variable elsewhere in the body.
    ///     </para>
    /// </remarks>
    public string? Name { get; set; }
}

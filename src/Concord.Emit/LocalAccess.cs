namespace Concord;

/// <summary>
///     Which kind of access to a local an <see cref="At.Local" /> injection targets.
/// </summary>
public enum LocalAccess {
    /// <summary>Match writes to the local. Matches <c>stloc</c> in all its forms and nothing else.</summary>
    Store,

    /// <summary>Match reads of the local. Matches <c>ldloc</c> in all its forms and nothing else.</summary>
    Load,
}

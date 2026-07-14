namespace Concord;

/// <summary>
///     Marks a patch declaration to append its composed wrapper IL to <c>Concord.PatchDebug.log</c>
///     on the current user's desktop when Concord applies it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PatchDebugAttribute : Attribute { }

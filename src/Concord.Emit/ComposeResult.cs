using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Contains the wrapper and original-body callable produced by <see cref="WrapperComposer" />.
/// </summary>
/// <param name="Wrapper">The composed wrapper method that should be applied as the detour replacement.</param>
/// <param name="OriginalBody">A copy of the original target body that bypasses Concord patches.</param>
public sealed record ComposeResult(MethodInfo Wrapper, MethodInfo OriginalBody);

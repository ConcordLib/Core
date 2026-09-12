using System;
using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Contains the wrapper and original-body callable produced by <see cref="WrapperComposer" />.
/// </summary>
public sealed class ComposeResult {
    private readonly Func<MethodInfo> originalBody;
    private MethodInfo? cachedOriginalBody;

    internal ComposeResult(MethodInfo wrapper, Func<MethodInfo> originalBody) {
        Wrapper = wrapper;
        this.originalBody = originalBody;
    }

    /// <summary>
    ///     The composed wrapper method that should be applied as the detour replacement.
    /// </summary>
    public MethodInfo Wrapper { get; }

    /// <summary>
    ///     A copy of the original target body that bypasses Concord patches. Generated on first read:
    ///     cloning a body is as expensive as composing one, and the detour path never needs it.
    /// </summary>
    public MethodInfo OriginalBody => cachedOriginalBody ??= originalBody();

    internal bool OriginalBodyMaterialized => cachedOriginalBody is not null;
}

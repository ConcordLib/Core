using System;
using System.Collections.Generic;
using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Contains the wrapper and original-body callable produced by <see cref="WrapperComposer" />.
/// </summary>
public sealed class ComposeResult {
    private readonly Func<MethodInfo> originalBody;
    private MethodInfo? cachedOriginalBody;

    internal ComposeResult(MethodInfo wrapper, Func<MethodInfo> originalBody, IReadOnlyList<RejectedInjection> rejected) {
        Wrapper = wrapper;
        this.originalBody = originalBody;
        Rejected = rejected;
    }

    /// <summary>
    ///     The composed wrapper method that should be applied as the detour replacement.
    /// </summary>
    public MethodInfo Wrapper { get; }

    /// <summary>
    ///     The injections composition dropped to keep the rest of the target working, each with the
    ///     diagnostic its owner needs to fix it. Empty on a clean compose.
    /// </summary>
    public IReadOnlyList<RejectedInjection> Rejected { get; }

    /// <summary>
    ///     A copy of the original target body that bypasses Concord patches. Generated on first read:
    ///     cloning a body is as expensive as composing one, and the detour path never needs it.
    /// </summary>
    public MethodInfo OriginalBody => cachedOriginalBody ??= originalBody();

    internal bool OriginalBodyMaterialized => cachedOriginalBody is not null;
}

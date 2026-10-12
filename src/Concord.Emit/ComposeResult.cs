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

    internal ComposeResult(
        MethodInfo wrapper,
        Func<MethodInfo> originalBody,
        IReadOnlyList<RejectedInjection> rejected,
        ComposedBody? body = null,
        string? bodyRejection = null) {
        Wrapper = wrapper;
        this.originalBody = originalBody;
        Rejected = rejected;
        Body = body;
        BodyRejection = bodyRejection;
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

    /// <summary>
    ///     The composed body with no Cecil type in it, for a detour backend in a separate assembly to encode.
    ///     Null unless <see cref="WrapperComposer.CaptureComposedBody" /> was on: Cecil disposes the body this describes.
    /// </summary>
    public ComposedBody? Body { get; }

    /// <summary>
    ///     Why <see cref="Body" /> is null, or null when capture was simply off. Non-null means install a detour
    ///     instead; refusing a body a detour already handles would turn a working patch into a crash.
    /// </summary>
    public string? BodyRejection { get; }

    internal bool OriginalBodyMaterialized => cachedOriginalBody is not null;
}

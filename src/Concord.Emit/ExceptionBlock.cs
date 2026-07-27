namespace Concord;

/// <summary>The kind of exception-handling boundary an <see cref="ExceptionBlock" /> marks.</summary>
public enum ExceptionBlockType {
    /// <summary>Opens a protected region.</summary>
    BeginExceptionBlock,

    /// <summary>Opens a typed catch handler.</summary>
    BeginCatchBlock,

    /// <summary>Opens a filter handler.</summary>
    BeginExceptFilterBlock,

    /// <summary>Opens a fault handler.</summary>
    BeginFaultBlock,

    /// <summary>Opens a finally handler.</summary>
    BeginFinallyBlock,

    /// <summary>Closes the innermost open region.</summary>
    EndExceptionBlock,
}

/// <summary>
///     An exception-handling boundary attached to a CodeInstruction. Member names are
///     lowercase to match Harmony so migrating transpiler bodies compile unchanged; do not rename them
///     to house style.
/// </summary>
public sealed class ExceptionBlock {
    /// <summary>Creates an exception block boundary.</summary>
    /// <param name="blockType">The kind of boundary.</param>
    /// <param name="catchType">The caught type, for <see cref="ExceptionBlockType.BeginCatchBlock" />.</param>
    public ExceptionBlock(ExceptionBlockType blockType, Type? catchType = null) {
        this.blockType = blockType;
        this.catchType = catchType;
    }

    /// <summary>The kind of boundary this marks.</summary>
#pragma warning disable SA1300
    public ExceptionBlockType blockType { get; set; }
#pragma warning restore SA1300

    /// <summary>The caught type, when this is a typed catch boundary.</summary>
#pragma warning disable SA1300
    public Type? catchType { get; set; }
#pragma warning restore SA1300
}

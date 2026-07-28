namespace Concord.Emit;

/// <summary>
/// Distinguishes the kind of region event in exception handling.
/// </summary>
public enum NeutralRegionEventKind {
    /// <summary>Marks the beginning of a try block.</summary>
    BeginTry,

    /// <summary>Marks the beginning of a catch block.</summary>
    BeginCatch,

    /// <summary>Marks the beginning of a finally block.</summary>
    BeginFinally,

    /// <summary>Marks the end of a try/catch/finally region.</summary>
    EndRegion,
}

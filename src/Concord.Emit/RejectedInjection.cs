namespace Concord.Emit;

/// <summary>
///     One injection that composition dropped instead of failing the whole target. Only a
///     <c>[Local]</c> or <c>At.Local</c> selector another mod broke lands here: one made ambiguous by
///     a slot a transpiler added, or one whose <c>By</c> held against the target's own IL and stopped
///     holding after a foreign edit. The owning injection is evicted, every other injection on the
///     target still applies. A selector that would have failed against the bare target body throws
///     instead, because that is the author's own mistake and dropping it would hide it.
/// </summary>
/// <param name="Owner">The stable owner id of the injection that was dropped.</param>
/// <param name="Code">The Concord diagnostic code that caused the eviction, such as <c>CONC147</c>.</param>
/// <param name="Message">The full diagnostic, naming every candidate slot the selector could have meant.</param>
public sealed record RejectedInjection(string Owner, string Code, string Message);

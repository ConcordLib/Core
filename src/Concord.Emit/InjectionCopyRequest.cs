using System.Reflection;
using Mono.Cecil;

namespace Concord.Emit;

/// <summary>
///     Bundles the injection identity and target shared by every call site that copies an injection method's body
///     into a destination method.
/// </summary>
/// <param name="InjectionDefinition">The Cecil definition of the decompiled injection method body.</param>
/// <param name="Destination">The wrapper method the lowered instructions are copied into.</param>
/// <param name="Target">The original target member being patched.</param>
/// <param name="InjectionMethod">The reflection handle for the injection method.</param>
/// <param name="InjectedMembers">Maps injected member declarations to the resolved target members.</param>
internal readonly record struct InjectionCopyRequest(
    MethodDefinition InjectionDefinition,
    MethodDefinition Destination,
    MethodBase Target,
    MethodBase InjectionMethod,
    InjectedMemberMap InjectedMembers) {
    /// <summary>
    ///     Values bound to the injection's <see cref="BoundAttribute" /> parameters, keyed by parameter name.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? BoundArguments { get; init; }

    /// <summary>
    ///     The position the injection was registered at, so lowering can refuse a call the position
    ///     does not support.
    /// </summary>
    public InjectAt? At { get; init; }

    /// <summary>
    ///     Whether the injection was translated from a foreign patch, so lowering can give a cancel
    ///     the narrower skip the foreign rulebook asks for.
    /// </summary>
    public bool ForeignOrigin { get; init; }

    /// <summary>
    ///     The injection parameter index that reads the target's return value, or null when there is none.
    ///     A by-ref parameter writes it too.
    /// </summary>
    public int? ResultArgIndex { get; init; }

    /// <summary>
    ///     The injection parameter index that reads the target instance, or null when there is none.
    /// </summary>
    public int? InstanceArgIndex { get; init; }

    /// <summary>
    ///     The injection parameter index that reads whether the target body still runs, or null when there
    ///     is none.
    /// </summary>
    public int? RunOriginalArgIndex { get; init; }

    /// <summary>
    ///     The injection parameter index that reads every target argument boxed into an object array, or
    ///     null when there is none.
    /// </summary>
    public int? ArgsArgIndex { get; init; }

    /// <summary>
    ///     Maps an injection parameter index to the field on the target's declaring type it reads.
    ///     A by-ref parameter writes it too.
    /// </summary>
    public IReadOnlyDictionary<int, FieldInfo>? FieldArgs { get; init; }

    /// <summary>
    ///     Injection parameter indexes the body writes but the target passes by value, so the write needs a
    ///     scratch local instead of the wrapper's own argument.
    /// </summary>
    public IReadOnlyCollection<int>? ScratchArgs { get; init; }

    /// <summary>
    ///     Whether the injection returns the target's new return value, so a return lowers to a store into
    ///     the wrapper's return-value local instead of a bare branch.
    /// </summary>
    public bool ReturnsIntoResult { get; init; }

    /// <summary>
    ///     Whether the injection returns a bool that cancels the target when it is false, so a return
    ///     lowers to a test on that bool ahead of the branch.
    /// </summary>
    public bool CancelsOnFalse { get; init; }

    /// <summary>
    ///     Parameter indexes declared by-ref against a target argument the target passes by value, so their
    ///     loads lower to the address of the wrapper's own argument.
    /// </summary>
    public IReadOnlyCollection<int>? AddressArgs { get; init; }

    /// <summary>
    ///     Parameter index bound to the shared state slot this injection's declaring type owns on the target.
    /// </summary>
    public int? StateArg { get; init; }

    /// <summary>
    ///     The injection parameter index bound to the patched method itself, or null when there is none.
    /// </summary>
    public int? OriginalMethodArg { get; init; }

    /// <summary>
    ///     The patch declaration whose state slot this injection reads, or null when it reads none.
    /// </summary>
    public Type? ForeignStateOwner { get; init; }

    /// <summary>
    ///     The injection parameter index that receives the foreign state slot, or null when there is none.
    /// </summary>
    public int? ForeignStateArg { get; init; }
}

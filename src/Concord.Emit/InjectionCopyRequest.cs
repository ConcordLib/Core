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
    InjectedMemberMap InjectedMembers);

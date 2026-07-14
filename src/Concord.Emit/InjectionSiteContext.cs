using System.Reflection;
using Mono.Cecil;

namespace Concord.Emit;

/// <summary>
///     Bundles the injection, wrapper method, target, and protocol locals shared by the
///     <c>WrapperComposer.Process*Injection</c> family as they assemble a wrapper body.
/// </summary>
/// <param name="Injection">The injection being processed.</param>
/// <param name="WrapperDefinition">The wrapper method the injection's body is assembled into.</param>
/// <param name="Target">The original target member being patched.</param>
/// <param name="Locals">The wrapper's protocol locals (cancel flag, return value, etc.).</param>
internal readonly record struct InjectionSiteContext(
    Injection Injection,
    MethodDefinition WrapperDefinition,
    MethodBase Target,
    ProtocolLocals Locals);

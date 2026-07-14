using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;

namespace Concord.Emit;

/// <summary>
///     Bundles the invariant state shared across <c>WrapperComposer.Assemble</c> and its dispatch
///     helpers while a single wrapper body is composed.
/// </summary>
/// <param name="WrapperDefinition">The wrapper method the composed body is assembled into.</param>
/// <param name="Target">The original target member being patched.</param>
/// <param name="Ordered">The injections being composed, ordered by their caller.</param>
/// <param name="Locals">The wrapper's protocol locals (cancel flag, return value, etc.).</param>
/// <param name="IsVoid">Whether the target's resolved return type is <c>void</c>.</param>
/// <param name="HasAround">Whether a whole-method Around injection participates in the composition.</param>
internal readonly record struct WrapperAssembly(
    MethodDefinition WrapperDefinition,
    MethodBase Target,
    IReadOnlyList<Injection> Ordered,
    ProtocolLocals Locals,
    bool IsVoid,
    bool HasAround);

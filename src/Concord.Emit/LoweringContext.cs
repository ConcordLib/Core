using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Concord.Emit;

/// <summary>
///     Bundles the shared mapping state threaded through the per-instruction lowering helpers.
/// </summary>
/// <param name="Module">The destination wrapper module.</param>
/// <param name="VariableMap">Maps injection method locals to their copies in the destination body.</param>
/// <param name="InjectionMethodLocals">The injection method body's local variables.</param>
/// <param name="InjectedMembers">Maps injected member declarations to the resolved target members.</param>
/// <param name="ArgRemap">Maps injection method argument indices to target argument indices.</param>
/// <param name="DestinationVariables">The destination wrapper body's variable collection.</param>
internal readonly record struct LoweringContext(
    ModuleDefinition Module,
    Dictionary<VariableDefinition, VariableDefinition> VariableMap,
    IList<VariableDefinition> InjectionMethodLocals,
    InjectedMemberMap InjectedMembers,
    Dictionary<int, int> ArgRemap,
    Collection<VariableDefinition> DestinationVariables);

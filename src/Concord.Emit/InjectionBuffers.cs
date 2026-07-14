using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Bundles the accumulator collections the wrapper-assembly dispatch loop fills as it classifies
///     each injection by position.
/// </summary>
/// <param name="HeadBodies">Copied head injection bodies, chained before the guard.</param>
/// <param name="TailBodies">Copied tail injection bodies, spliced before the last exit.</param>
/// <param name="AroundReturnInjections">Return injections deferred to the Around spine copies.</param>
/// <param name="AroundTailInjections">Tail injections deferred to the Around spine copies.</param>
internal readonly record struct InjectionBuffers(
    List<List<Instruction>> HeadBodies,
    List<List<Instruction>> TailBodies,
    List<(Injection Injection, InjectAt.Return ReturnSite)> AroundReturnInjections,
    List<Injection> AroundTailInjections);

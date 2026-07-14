using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Bundles the ordered instruction fragments concatenated into the final composed wrapper body.
/// </summary>
/// <param name="Heads">Chained head injection bodies emitted before the guard.</param>
/// <param name="HasHead">Whether any head injection contributed a body.</param>
/// <param name="GuardStart">The cancel-flag load beginning the head guard.</param>
/// <param name="GuardBranch">The branch that skips the spine when a head cancels.</param>
/// <param name="AroundBody">The composed Around body, or <c>null</c> when no Around participates.</param>
/// <param name="Spine">The composed original-body spine.</param>
/// <param name="AfterSpine">The sentinel instruction following the spine.</param>
/// <param name="Returns">Chained return injection bodies emitted before the epilogue.</param>
/// <param name="Epilogue">The wrapper epilogue.</param>
internal readonly record struct AssembledBodyParts(
    List<Instruction> Heads,
    bool HasHead,
    Instruction GuardStart,
    Instruction GuardBranch,
    List<Instruction>? AroundBody,
    List<Instruction> Spine,
    Instruction AfterSpine,
    List<Instruction> Returns,
    List<Instruction> Epilogue);

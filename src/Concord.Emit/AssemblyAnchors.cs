using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Bundles the working spine and instruction anchors shared by the wrapper-assembly dispatch
///     helpers as injection bodies are spliced into the composed method.
/// </summary>
/// <param name="Spine">The mutable copy of the original body instructions being composed.</param>
/// <param name="AfterSpine">The sentinel instruction that spine returns branch to.</param>
/// <param name="GuardStart">The cancel-flag load that begins the head guard.</param>
/// <param name="EpilogueStart">The first instruction of the epilogue.</param>
internal readonly record struct AssemblyAnchors(
    List<Instruction> Spine,
    Instruction AfterSpine,
    Instruction GuardStart,
    Instruction EpilogueStart);

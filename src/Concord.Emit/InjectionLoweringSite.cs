using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Bundles the per-call invariants that <see cref="BodyCopier.LowerInstruction" /> needs alongside the
///     per-instruction source and the shared <see cref="LoweringContext" />.
/// </summary>
/// <param name="ControlHandleArgIndex">Argument index of the injection method's control-handle parameter, or -1 if none.</param>
/// <param name="OperationArgIndex">Argument index of the injection method's operation parameter, or -1 if none.</param>
/// <param name="Locals">The wrapper's protocol locals (cancel flag, return value, etc.).</param>
/// <param name="ReturnBranchTarget">The instruction injection-method returns should branch to.</param>
/// <param name="InjectionMethod">The reflection handle for the injection method.</param>
/// <param name="Target">The original target member being patched, or null outside a spine-splicing context.</param>
/// <param name="SpineTemplate">The captured target spine to splice into an Around injection, or null outside one.</param>
/// <param name="Destination">The wrapper method the lowered instructions are copied into.</param>
/// <param name="SpineCopies">Accumulates spine copies spliced for an Around injection's invoke sites.</param>
/// <param name="InsideAround">Whether lowering occurs inside an Around injection's spliced spine copy.</param>
internal readonly record struct InjectionLoweringSite(
    int ControlHandleArgIndex,
    int OperationArgIndex,
    ProtocolLocals Locals,
    Instruction ReturnBranchTarget,
    MethodBase InjectionMethod,
    MethodBase? Target,
    SpineTemplate? SpineTemplate,
    MethodDefinition? Destination,
    List<SpineCopy>? SpineCopies,
    bool InsideAround);

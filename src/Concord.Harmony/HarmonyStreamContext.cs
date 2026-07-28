using System.Collections.Generic;
using System.Reflection;

#nullable enable

namespace Concord.Harmony;

using Label = System.Reflection.Emit.Label;

/// <summary>
///     Carries the SRE <see cref="Label" /> and local-variable identity a Harmony transpiler
///     stream needs that Concord's IL model cannot itself express, across one
///     <see cref="CodeInstructionConverter.ToConcord" />/<see cref="Emit.WrapperComposer.TransformStream" />/
///     <see cref="CodeInstructionConverter.FromConcord" /> round trip.
/// </summary>
internal sealed class HarmonyStreamContext {
    public HarmonyStreamContext() {
        HarmonyLabelByConcord = new Dictionary<Concord.Label, Label>();
        HarmonyLocalByRef = new Dictionary<Concord.LocalRef, LocalVariableInfo>();
    }

    /// <summary>Maps each minted Concord label back to the Harmony <see cref="Label" /> it stands in for.</summary>
    public Dictionary<Concord.Label, Label> HarmonyLabelByConcord { get; }

    /// <summary>Maps each densely declared Concord local back to Harmony's own local for that slot.</summary>
    public Dictionary<Concord.LocalRef, LocalVariableInfo> HarmonyLocalByRef { get; }

    /// <summary>Whether the incoming stream needed the sacrificial trailing Nop for end-of-body handlers.</summary>
    public bool AppendedSacrificialNop { get; set; }
}

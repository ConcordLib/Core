using System.Collections.Generic;
using System.Reflection.Emit;

#nullable enable

namespace Concord.Harmony;

using ExceptionBlock = HarmonyLib.ExceptionBlock;
using Label = System.Reflection.Emit.Label;

internal sealed class HarmonyStreamContext {
    public HarmonyStreamContext() {
        LabelById = new Dictionary<int, Label>();
        BuilderBySlot = new Dictionary<int, LocalBuilder>();
    }

    public Dictionary<int, Label> LabelById { get; }

    public Dictionary<int, LocalBuilder> BuilderBySlot { get; }

    public int IncomingLabelCount { get; set; }

    public int IncomingLocalCount { get; set; }
}

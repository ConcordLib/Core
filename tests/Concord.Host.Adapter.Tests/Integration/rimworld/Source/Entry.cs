using System;
using Verse;

namespace Concord.Waylith.RimProbe;

public class ProbeMod : Mod {
    public ProbeMod(ModContentPack content) : base(content) {
        try {
            Probe.Run(content.RootDir);
        } catch (Exception thrown) {
            Report.Threw("probe", thrown);
            Report.Line("done");
        }
    }
}

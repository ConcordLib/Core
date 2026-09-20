using Xunit;

namespace Concord.Orchestration.Tests;

// Patcher keeps its registries in statics, so these cannot run beside another test that registers.
[CollectionDefinition("PatcherStatics", DisableParallelization = true)]
public sealed class PatcherStaticsCollection {
}

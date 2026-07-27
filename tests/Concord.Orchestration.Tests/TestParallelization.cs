using Xunit;

// Concord's patching surface is process-wide: DetourBackend.Current is a static, and so are the
// registries behind it. A test that installs its own backend and then asserts nothing reached it
// is really asserting about global state, so any test patching concurrently in another collection
// lands in the first test's backend and fails it.
//
// That is what made CanonicalTargetTests.ConstructedGenericAsyncTarget_RawTargetIsRejected flaky:
// it caught ControlReturnPatchTests patching ControlGateTarget.Bump. It only showed up on CI
// because a slower, more contended runner widens the window.
//
// These assemblies each run in about a second, so serializing them costs nothing worth measuring.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public class CanonicalTargetTests {
    private sealed class RecordingBackend : IDetourBackend {
        public MethodBase? LastTarget;

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
            throw new System.NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
            LastTarget = target;
            return new FakeDetourHandle(target);
        }
    }

    private static async Task<int> AsyncTarget() {
        await Task.Yield();
        return 7;
    }

    private static void Head() {
    }

    private sealed class GenericAsyncContainer<T> {
        public async Task<int> FooAsync() {
            await Task.Yield();
            return 1;
        }
    }

    [Fact]
    public void ConstructedGenericAsyncTarget_RawTargetIsRejected() {
        RecordingBackend backend = new RecordingBackend();
        IDetourBackend previous = DetourBackend.Current;
        DetourBackend.Current = backend;
        try {
            CollectingPatchApplier applier = new CollectingPatchApplier();
            MethodBase entry = typeof(GenericAsyncContainer<string>).GetMethod(nameof(GenericAsyncContainer<string>.FooAsync))!;
            Injection head = new Injection(typeof(CanonicalTargetTests).GetMethod(nameof(Head), BindingFlags.Static | BindingFlags.NonPublic)!, new InjectAt.Head(), "test", 0);

            ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => applier.ApplyPatch(entry, head));

            Assert.Equal("CONC061", ex.Code);
            Assert.Null(backend.LastTarget);
        } finally {
            DetourBackend.Current = previous;
        }
    }

    [Fact]
    public void AsyncTargetReachesBackendAsMoveNext() {
        RecordingBackend backend = new RecordingBackend();
        IDetourBackend previous = DetourBackend.Current;
        DetourBackend.Current = backend;
        try {
            CollectingPatchApplier applier = new CollectingPatchApplier();
            MethodBase entry = typeof(CanonicalTargetTests).GetMethod(nameof(AsyncTarget), BindingFlags.Static | BindingFlags.NonPublic)!;
            Injection head = new Injection(typeof(CanonicalTargetTests).GetMethod(nameof(Head), BindingFlags.Static | BindingFlags.NonPublic)!, new InjectAt.Head(), "test", 0);
            applier.ApplyPatch(entry, head);
            Assert.NotNull(backend.LastTarget);
            Assert.Equal("MoveNext", backend.LastTarget!.Name);
        } finally {
            DetourBackend.Current = previous;
        }
    }
}

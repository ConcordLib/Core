using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public class PatchHandleRetryTests {
    private sealed class FailOnceDisposeBackend : IDetourBackend {
        public int DisposeCalls;

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
            throw new NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
            return new FailOnceHandle(this, target);
        }

        private sealed class FailOnceHandle(FailOnceDisposeBackend owner, MethodBase original) : IDetourHandle {
            private bool disposed;

            public MethodBase Original { get; } = original;

            public bool IsApplied => !disposed;

            public void Dispose() {
                if (disposed) {
                    return;
                }

                owner.DisposeCalls++;
                if (owner.DisposeCalls == 1) {
                    throw new InvalidOperationException("teardown failed once");
                }

                disposed = true;
            }
        }
    }

    public static int Target(int x) {
        return x;
    }

    private static void Head() {
    }

    [Fact]
    public void FailedDisposeLeavesHandleRetryable() {
        FailOnceDisposeBackend backend = new FailOnceDisposeBackend();
        IDetourBackend previous = DetourBackend.Current;
        DetourBackend.Current = backend;
        try {
            MethodBase target = typeof(PatchHandleRetryTests).GetMethod(nameof(Target))!;
            Injection head = new Injection(typeof(PatchHandleRetryTests).GetMethod(nameof(Head), BindingFlags.Static | BindingFlags.NonPublic)!, new InjectAt.Head(), "test", 0);
            IPatchHandle handle = Patcher.PatchInjection(target, head);
            Assert.True(handle.IsApplied);
            Assert.Throws<AggregateException>(() => handle.Dispose());
            Assert.True(handle.IsApplied);
            handle.Dispose();
            Assert.Equal(2, backend.DisposeCalls);
            Assert.False(handle.IsApplied);
        } finally {
            DetourBackend.Current = previous;
        }
    }
}

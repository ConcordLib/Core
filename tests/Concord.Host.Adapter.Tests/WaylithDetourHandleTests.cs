using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class WaylithDetourHandleTests {
    private static readonly MethodBase Target = typeof(Probe).GetMethod(nameof(Probe.One))!;

    private static readonly MethodBase Other = typeof(Probe).GetMethod(nameof(Probe.Two))!;

    [Fact]
    public void StackUnwindsToTheOriginalBodyNeverAnIntermediate() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        IDetourHandle first = stack.Add(Target, [Patch("a")]);
        IDetourHandle second = stack.Add(Target, [Patch("b")]);
        Assert.Equal("a,b", runtime.BodyOf(Target));

        first.Dispose();
        Assert.Equal("b", runtime.BodyOf(Target));

        second.Dispose();
        Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,b", "publish b", "restore"], runtime.Ops);
    }

    [Fact]
    public void UnwindingInReverseOrderAlsoLandsOnTheOriginal() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        IDetourHandle first = stack.Add(Target, [Patch("a")]);
        IDetourHandle second = stack.Add(Target, [Patch("b")]);

        second.Dispose();
        Assert.Equal("a", runtime.BodyOf(Target));

        first.Dispose();
        Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,b", "publish a", "restore"], runtime.Ops);
    }

    [Fact]
    public void TwoHundredCyclesEndOnTheOriginalEveryTime() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        for (int cycle = 0; cycle < 200; cycle++) {
            IDetourHandle first = stack.Add(Target, [Patch("a")]);
            IDetourHandle second = stack.Add(Target, [Patch("b")]);
            first.Dispose();
            Assert.Equal("b", runtime.BodyOf(Target));
            second.Dispose();
            Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));
        }

        Assert.Equal(800, runtime.Ops.Count);
    }

    [Fact]
    public void ATargetIsPatchableAgainAfterAFullUnwind() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        stack.Add(Target, [Patch("a")]).Dispose();
        stack.Add(Target, [Patch("c")]);

        Assert.Equal("c", runtime.BodyOf(Target));
        Assert.Equal(["publish a", "restore", "publish c"], runtime.Ops);
    }

    [Fact]
    public void OneCallCarryingTwoInjectionsPublishesOnceAndUndoesBoth() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        IDetourHandle handle = stack.Add(Target, [Patch("a"), Patch("b")]);
        Assert.Equal("a,b", runtime.BodyOf(Target));

        handle.Dispose();
        Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));
        Assert.Equal(["publish a,b", "restore"], runtime.Ops);
    }

    [Fact]
    public void ReadingIsAppliedNeverTouchesTheRuntime() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();
        IDetourHandle handle = stack.Add(Target, [Patch("a")]);

        int before = runtime.Ops.Count;
        Assert.True(handle.IsApplied);
        Assert.True(handle.IsApplied);
        Assert.Equal(before, runtime.Ops.Count);

        handle.Dispose();
        int after = runtime.Ops.Count;
        Assert.False(handle.IsApplied);
        Assert.Equal(after, runtime.Ops.Count);
    }

    [Fact]
    public void DisposingTwiceRemovesOnce() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        IDetourHandle first = stack.Add(Target, [Patch("a")]);
        stack.Add(Target, [Patch("b")]);

        first.Dispose();
        first.Dispose();

        Assert.Equal("b", runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,b", "publish b"], runtime.Ops);
    }

    [Fact]
    public void DisposeReportsARefusedRepublishInsteadOfThrowing() {
        Runtime runtime = new Runtime();
        List<string> logged = [];
        WaylithTargetStack stack = runtime.Stack(logged.Add);

        WaylithDetourHandle first = stack.Add(Target, [Patch("a")]);
        stack.Add(Target, [Patch("b")]);

        runtime.Refusal = new NotSupportedException("the runtime refused this target");
        first.Dispose();

        Assert.IsType<NotSupportedException>(first.UndoFailure);
        Assert.True(first.IsApplied);
        Assert.Equal("a,b", runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,b"], runtime.Ops);
        Assert.Equal(2, logged.Count);
        Assert.StartsWith(WaylithTargetStack.UndoFailedMarker, logged[0]);
        Assert.Contains("the runtime refused this target", logged[0]);
        Assert.StartsWith(WaylithTargetStack.RepublishFailedMarker, logged[1]);

        runtime.Refusal = null;
        first.Dispose();

        Assert.Null(first.UndoFailure);
        Assert.False(first.IsApplied);
        Assert.Equal("b", runtime.BodyOf(Target));
    }

    [Fact]
    public void DisposeDoesNotThrowWhenTheLogSinkThrows() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack(static _ => throw new InvalidOperationException("no sink"));

        WaylithDetourHandle handle = stack.Add(Target, [Patch("a")]);
        runtime.Refusal = new NotSupportedException("refused");
        handle.Dispose();

        Assert.IsType<InvalidOperationException>(handle.UndoFailure);
        Assert.True(handle.IsApplied);
    }

    [Fact]
    public void AnAddThatFailsLeavesNoTraceInTheStack() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        stack.Add(Target, [Patch("a")]);
        runtime.Refusal = new NotSupportedException("refused");
        Assert.Throws<NotSupportedException>(() => stack.Add(Target, [Patch("b")]));

        runtime.Refusal = null;
        stack.Add(Target, [Patch("c")]);

        Assert.Equal("a,c", runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,c"], runtime.Ops);
    }

    [Fact]
    public void AnAddThatFailsAfterTheBodyMovedRepublishesWhatSurvives() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        stack.Add(Target, [Patch("a")]);
        runtime.RefusalAfterMutating = new NotSupportedException("refused after writing the body");
        Assert.Throws<NotSupportedException>(() => stack.Add(Target, [Patch("b")]));

        Assert.Equal("a", runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,b", "publish a"], runtime.Ops);
    }

    [Fact]
    public void ARemoveThatFailsAfterTheBodyMovedRepublishesWhatWasThere() {
        Runtime runtime = new Runtime();
        List<string> logged = [];
        WaylithTargetStack stack = runtime.Stack(logged.Add);

        WaylithDetourHandle first = stack.Add(Target, [Patch("a")]);
        stack.Add(Target, [Patch("b")]);

        runtime.RefusalAfterMutating = new NotSupportedException("refused after writing the body");
        first.Dispose();

        Assert.IsType<NotSupportedException>(first.UndoFailure);
        Assert.True(first.IsApplied);
        Assert.Equal("a,b", runtime.BodyOf(Target));
        Assert.Equal(["publish a", "publish a,b", "publish b", "publish a,b"], runtime.Ops);
        Assert.Single(logged);
        Assert.StartsWith(WaylithTargetStack.UndoFailedMarker, logged[0]);
    }

    [Fact]
    public void ARollbackThatAlsoFailsIsReportedAndNeverThrown() {
        Runtime runtime = new Runtime();
        List<string> logged = [];
        WaylithTargetStack stack = runtime.Stack(logged.Add);

        stack.Add(Target, [Patch("a")]);
        runtime.Refusal = new NotSupportedException("refused every time");
        Assert.Throws<NotSupportedException>(() => stack.Add(Target, [Patch("b")]));

        Assert.Single(logged);
        Assert.StartsWith(WaylithTargetStack.RepublishFailedMarker, logged[0]);
        Assert.Contains("refused every time", logged[0]);
    }

    [Fact]
    public void AFirstAddThatFailsPublishesNothing() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        runtime.Refusal = new NotSupportedException("refused");
        Assert.Throws<NotSupportedException>(() => stack.Add(Target, [Patch("a")]));

        Assert.Empty(runtime.Ops);
        Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));

        runtime.Refusal = null;
        stack.Add(Target, [Patch("d")]);
        Assert.Equal(["publish d"], runtime.Ops);
    }

    [Fact]
    public void EveryInstantiationOneReplacementReachesSharesOneStack() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        MethodBase text = typeof(Box<string>).GetMethod(nameof(Box<string>.Ping))!;
        MethodBase version = typeof(Box<Version>).GetMethod(nameof(Box<Version>.Ping))!;
        MethodBase number = typeof(Box<int>).GetMethod(nameof(Box<int>.Ping))!;
        Assert.NotEqual(MethodIdentity.SharedBodyKey(text), MethodIdentity.SharedBodyKey(number));

        stack.Add(text, [Patch("a")]);
        stack.Add(version, [Patch("b")]);
        stack.Add(number, [Patch("c")]);

        Assert.Equal(["publish a", "publish a,b", "publish a,b,c"], runtime.Ops);
        Assert.Equal(1, runtime.Published);
    }

    [Fact]
    public void TheBodyOwnerIsHandedTheOpenDefinitionNotAnInstantiation() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        MethodBase text = typeof(Box<string>).GetMethod(nameof(Box<string>.Ping))!;
        MethodBase definition = typeof(Box<>).GetMethod(nameof(Box<string>.Ping))!;

        stack.Add(text, [Patch("a")]);

        Assert.Equal("a", runtime.BodyOf(definition));
    }

    [Fact]
    public void AValueInstantiationReportsPublishedOnceAReferenceOneWasAdded() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        MethodBase text = typeof(Box<string>).GetMethod(nameof(Box<string>.Ping))!;
        MethodBase number = typeof(Box<int>).GetMethod(nameof(Box<int>.Ping))!;

        stack.Add(text, [Patch("a")]);

        Assert.True(stack.IsPublished(number));
    }

    [Fact]
    public void OriginalReportsTheMethodTheCallerAskedFor() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        MethodBase text = typeof(Box<string>).GetMethod(nameof(Box<string>.Ping))!;
        IDetourHandle handle = stack.Add(text, [Patch("a")]);

        Assert.Same(text, handle.Original);
    }

    [Fact]
    public void ThePublishedOrderIsCompositionOrderNotInsertionOrder() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        stack.Add(Target, [Patch("early", 0)]);
        stack.Add(Target, [Patch("late", 5)]);

        Assert.Equal("late,early", runtime.BodyOf(Target));
    }

    [Fact]
    public void AHandleThatOwnsNothingTouchesNothing() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        IDetourHandle handle = stack.Add(Target, []);

        Assert.False(handle.IsApplied);
        handle.Dispose();
        Assert.Empty(runtime.Ops);
        Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));
    }

    [Fact]
    public void SeparateTargetsKeepSeparateStacks() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        IDetourHandle first = stack.Add(Target, [Patch("a")]);
        stack.Add(Other, [Patch("b")]);

        first.Dispose();

        Assert.Equal(Runtime.OriginalBody, runtime.BodyOf(Target));
        Assert.Equal("b", runtime.BodyOf(Other));
    }

    [Fact]
    public void ConcurrentAddsOnOneTargetAllReachThePublishedBody() {
        Runtime runtime = new Runtime();
        WaylithTargetStack stack = runtime.Stack();

        Parallel.For(0, 8, thread => {
            for (int i = 0; i < 50; i++) {
                stack.Add(Target, [Patch("t" + thread + "i" + i)]);
            }
        });

        Assert.Equal(400, runtime.Ops.Count);
        Assert.Equal(400, runtime.BodyOf(Target).Split(',').Length);
    }

    private static Injection Patch(string owner, int priority = 0) {
        return new Injection(typeof(Probe).GetMethod(nameof(Probe.Inject))!, new InjectAt.Head(), owner, priority);
    }

    private sealed class Runtime : ITargetBodyOwner {
        internal const string OriginalBody = "<original>";

        private readonly Dictionary<MethodBase, string> bodies = new Dictionary<MethodBase, string>();

        private readonly object gate = new object();

        internal List<string> Ops { get; } = [];

        internal Exception? Refusal { get; set; }

        internal Exception? RefusalAfterMutating { get; set; }

        internal int Published {
            get {
                lock (gate) {
                    return bodies.Count;
                }
            }
        }

        public void PublishComposed(MethodBase target, IReadOnlyList<Injection> ordered) {
            string body = Describe(ordered);
            lock (gate) {
                Refuse();
                bodies[target] = body;
                Ops.Add("publish " + body);
                RefuseAfterMutating();
            }
        }

        public void RevertToOriginal(MethodBase target) {
            lock (gate) {
                Refuse();
                bodies.Remove(target);
                Ops.Add("restore");
                RefuseAfterMutating();
            }
        }

        internal WaylithTargetStack Stack(Action<string>? log = null) {
            return new WaylithTargetStack(this, log ?? (static _ => { }));
        }

        internal string BodyOf(MethodBase target) {
            lock (gate) {
                return bodies.TryGetValue(target, out string? body) ? body : OriginalBody;
            }
        }

        private static string Describe(IReadOnlyList<Injection> ordered) {
            string[] owners = new string[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) {
                owners[i] = ordered[i].Owner;
            }

            return string.Join(',', owners);
        }

        private void Refuse() {
            if (Refusal is Exception refusal) {
                throw refusal;
            }
        }

        private void RefuseAfterMutating() {
            if (RefusalAfterMutating is not Exception refusal) {
                return;
            }

            RefusalAfterMutating = null;
            throw refusal;
        }
    }

    private static class Probe {
        public static int One() {
            return 1;
        }

        public static int Two() {
            return 2;
        }

        public static void Inject() {
        }
    }

    private sealed class Box<T> {
        public int Ping() {
            return 1;
        }
    }
}

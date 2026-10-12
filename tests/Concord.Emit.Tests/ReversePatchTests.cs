using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class ReversePatchTarget {
    public int Value() {
        return 7;
    }

    private int Secret() {
        return 42;
    }
}

public sealed class OwnedReversePatchTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Value() {
        return 7;
    }
}

public static class UnownedReversePatchTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement() {
        return 99;
    }
}

public sealed class OwnedReversePatchCtorTarget {
    public int Seed;

    public OwnedReversePatchCtorTarget(int seed) {
        Seed = seed;
        OwnedReversePatchCtorLog.Entries.Add("body:" + seed);
    }
}

public static class OwnedReversePatchCtorLog {
    public static List<string> Entries = new List<string>();
}

public static class ReversePatchInjectionMethods {
    public static void ReturnNinetyNineThenCancel(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }

    public static void WrapCtor(int seed, VoidOperation<int> original) {
        OwnedReversePatchCtorLog.Entries.Add("pre");
        original.Invoke(seed);
        OwnedReversePatchCtorLog.Entries.Add("post");
    }
}

public sealed class ReversePatchTests {
    [Fact]
    public void Bind_BypassesCancelingPatch_ReturnsOriginalValue() {
        MethodBase original = typeof(ReversePatchTarget).GetMethod(nameof(ReversePatchTarget.Value))!;
        MethodBase injectionMethod = typeof(ReversePatchInjectionMethods).GetMethod(nameof(ReversePatchInjectionMethods.ReturnNinetyNineThenCancel))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult composed = WrapperComposer.Compose(original, [head]);
        Func<ReversePatchTarget, int> wrapper = composed.Wrapper.CreateDelegate<Func<ReversePatchTarget, int>>();

        Func<ReversePatchTarget, int> reverse =
            (Func<ReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<ReversePatchTarget, int>));

        ReversePatchTarget instance = new ReversePatchTarget();
        Assert.Equal(99, wrapper(instance));
        Assert.Equal(7, reverse(instance));
    }

    [Fact]
    public void Bind_PrivateMethod_ReturnsCorrectValue() {
        MethodBase secret = typeof(ReversePatchTarget).GetMethod("Secret", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Func<ReversePatchTarget, int> reverse =
            (Func<ReversePatchTarget, int>)ReversePatchFactory.Bind(secret, typeof(Func<ReversePatchTarget, int>));

        ReversePatchTarget instance = new ReversePatchTarget();
        Assert.Equal(42, reverse(instance));
    }

    [Fact]
    public void Bind_ZeroAllocPerCall_OnWarmInvocation() {
        MethodBase original = typeof(ReversePatchTarget).GetMethod(nameof(ReversePatchTarget.Value))!;

        Func<ReversePatchTarget, int> reverse =
            (Func<ReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<ReversePatchTarget, int>));

        ReversePatchTarget instance = new ReversePatchTarget();
        reverse(instance);

        long before = TestPolyfills.GetAllocatedBytes();
        reverse(instance);
        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Bind_MethodConcordOwns_RunsTheBodyConcordRunsNow() {
        MethodBase original = typeof(OwnedReversePatchTarget).GetMethod(nameof(OwnedReversePatchTarget.Value))!;
        MonoModDetourBackend backend = new MonoModDetourBackend();
        OwnedReversePatchTarget instance = new OwnedReversePatchTarget();

        try {
            ReversePatchFactory.OwnsBody = backend.OwnsBody;
            using IDetourHandle handle = backend.ApplyComposed(original, [Head(nameof(ReversePatchInjectionMethods.ReturnNinetyNineThenCancel))]);
            Assert.Equal(99, instance.Value());

            Func<OwnedReversePatchTarget, int> reverse =
                (Func<OwnedReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<OwnedReversePatchTarget, int>));

            Assert.Equal(99, reverse(instance));
        } finally {
            ReversePatchFactory.OwnsBody = null;
        }
    }

    [Fact]
    public void Bind_OwnedMethod_WithoutTestSettingOwnsBody_RunsTheLiveBody() {
        MethodBase original = typeof(OwnedReversePatchTarget).GetMethod(nameof(OwnedReversePatchTarget.Value))!;
        MonoModDetourBackend backend = new MonoModDetourBackend();
        OwnedReversePatchTarget instance = new OwnedReversePatchTarget();

        try {
            using IDetourHandle handle = backend.ApplyComposed(original, [Head(nameof(ReversePatchInjectionMethods.ReturnNinetyNineThenCancel))]);
            Assert.Equal(99, instance.Value());

            Func<OwnedReversePatchTarget, int> reverse =
                (Func<OwnedReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<OwnedReversePatchTarget, int>));

            Assert.Equal(99, reverse(instance));
        } finally {
            ReversePatchFactory.OwnsBody = null;
        }
    }

    [Fact]
    public void Bind_AfterConcordLetsGo_ClonesTheOriginalAgain() {
        MethodBase original = typeof(OwnedReversePatchTarget).GetMethod(nameof(OwnedReversePatchTarget.Value))!;
        MonoModDetourBackend backend = new MonoModDetourBackend();
        OwnedReversePatchTarget instance = new OwnedReversePatchTarget();

        try {
            ReversePatchFactory.OwnsBody = backend.OwnsBody;
            IDetourHandle handle = backend.ApplyComposed(original, [Head(nameof(ReversePatchInjectionMethods.ReturnNinetyNineThenCancel))]);
            handle.Dispose();

            Func<OwnedReversePatchTarget, int> reverse =
                (Func<OwnedReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<OwnedReversePatchTarget, int>));

            Assert.Equal(7, reverse(instance));
        } finally {
            ReversePatchFactory.OwnsBody = null;
        }
    }

    [Fact]
    public void Bind_MethodConcordDoesNotOwn_BypassesTheDetourOnIt() {
        MethodInfo original = typeof(UnownedReversePatchTarget).GetMethod(nameof(UnownedReversePatchTarget.Value))!;
        MethodInfo replacement = typeof(UnownedReversePatchTarget).GetMethod(nameof(UnownedReversePatchTarget.Replacement))!;
        MonoModDetourBackend backend = new MonoModDetourBackend();

        try {
            ReversePatchFactory.OwnsBody = backend.OwnsBody;
            using IDetourHandle handle = backend.Apply(original, replacement);
            Assert.Equal(99, UnownedReversePatchTarget.Value());
            Assert.False(backend.OwnsBody(original));

            Func<int> reverse = (Func<int>)ReversePatchFactory.Bind(original, typeof(Func<int>));

            Assert.Equal(7, reverse());
        } finally {
            ReversePatchFactory.OwnsBody = null;
        }
    }

    [Fact]
    public void Bind_ConstructorConcordOwns_RunsTheWrappedConstructor() {
        ConstructorInfo ctor = typeof(OwnedReversePatchCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(ReversePatchInjectionMethods).GetMethod(nameof(ReversePatchInjectionMethods.WrapCtor))!;
        MonoModDetourBackend backend = new MonoModDetourBackend();

        try {
            ReversePatchFactory.OwnsBody = backend.OwnsBody;
            using IDetourHandle handle = backend.ApplyComposed(ctor, [new Injection(injectionMethod, new InjectAt.Around(), "test", 0)]);

            Action<OwnedReversePatchCtorTarget, int> reverse =
                (Action<OwnedReversePatchCtorTarget, int>)ReversePatchFactory.Bind(ctor, typeof(Action<OwnedReversePatchCtorTarget, int>));
            OwnedReversePatchCtorTarget built = new OwnedReversePatchCtorTarget(0);

            OwnedReversePatchCtorLog.Entries.Clear();
            reverse(built, 5);

            Assert.Equal(["pre", "body:5", "post"], OwnedReversePatchCtorLog.Entries);
            Assert.Equal(5, built.Seed);
        } finally {
            ReversePatchFactory.OwnsBody = null;
        }
    }

    private static Injection Head(string name) {
        return new Injection(typeof(ReversePatchInjectionMethods).GetMethod(name)!, new InjectAt.Head(), "test", 0);
    }
}

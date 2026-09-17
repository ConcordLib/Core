using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public class ProbeBox<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Ping() {
        return "orig";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string StaticPing() {
        return "orig";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Describe() {
        return typeof(T).Name;
    }
}

public sealed class ProbeSub : ProbeBox<string>;

public static class ProbeGenericMethod {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Identity<T>() {
        return "orig";
    }
}

public struct ProbeStructBox<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Ping() {
        return "orig";
    }
}

public static class ProbeThirdInjection {
    public static void AfterPing(ControlHandle<string> ch) {
        ch.ReturnValue = ch.ReturnValue + "+r";
    }
}

public static class ProbeOtherInjection {
    public static void AfterPing(ControlHandle<string> ch) {
        ch.ReturnValue = ch.ReturnValue + "+q";
    }
}

public static class ProbeInjection {
    public static void AfterPing(ControlHandle<string> ch) {
        ch.ReturnValue = ch.ReturnValue + "+p";
    }
}

public class SharedGenericProbeTests {
    private static IDetourHandle PatchRaw(MethodBase target) {
        MethodInfo injectionMethod = typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!;
        Injection injection = new Injection(injectionMethod, new InjectAt.Tail(), "probe", 0);
        return DetourBackend.Current.ApplyComposed(target, new[] { injection });
    }

    [Fact]
    public void Guard_InstanceMethodOnGenericType_IsolatesRequestedInstantiation() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.Ping))!;
        IDetourHandle handle = PatchRaw(target);
        try {
            Assert.Equal("orig+p", new ProbeBox<string>().Ping());
            Assert.Equal("orig", new ProbeBox<Version>().Ping());
            Assert.Equal("orig", new ProbeBox<int>().Ping());
            Assert.Equal("orig+p", new ProbeSub().Ping());
        } finally {
            handle.Dispose();
        }

        Assert.Equal("orig", new ProbeBox<string>().Ping());
    }

    [Fact]
    public void ContextTouchingBody_IsNotGuardable_AndStaysRejected() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.Describe))!;
        Injection[] ordered = [new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0)];

        Assert.False(WrapperComposer.IsSharedBodyGenericContextFree(target));
        Assert.False(WrapperComposer.CanGuardSharedInstantiation(target, ordered));
        Assert.Throws<ConcordEmitException>(() => WrapperComposer.RejectSharedGenericInstantiation(target, ordered));
    }

    [Fact]
    public void ContextFreeBody_IsGuardable() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.Ping))!;
        Injection[] ordered = [new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0)];

        Assert.True(WrapperComposer.IsSharedBodyGenericContextFree(target));
        Assert.True(WrapperComposer.CanGuardSharedInstantiation(target, ordered));
        WrapperComposer.RejectSharedGenericInstantiation(target, ordered);
    }

    [Fact]
    public void Unguardable_StaticMethodOnGenericType_StillLeaks() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.StaticPing))!;
        Assert.False(WrapperComposer.CanGuardSharedInstantiation(target, new[] {
            new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0),
        }));

        IDetourHandle handle = PatchRaw(target);
        try {
            Assert.Equal("orig+p", ProbeBox<string>.StaticPing());
            Assert.Equal("orig+p", ProbeBox<Version>.StaticPing());
        } finally {
            handle.Dispose();
        }
    }

    [Fact]
    public void Unguardable_GenericMethod_StillLeaks() {
        MethodBase target = typeof(ProbeGenericMethod).GetMethod(nameof(ProbeGenericMethod.Identity))!.MakeGenericMethod(typeof(string));
        Assert.False(WrapperComposer.CanGuardSharedInstantiation(target, new[] {
            new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0),
        }));

        IDetourHandle handle = PatchRaw(target);
        try {
            Assert.Equal("orig+p", ProbeGenericMethod.Identity<string>());
            Assert.Equal("orig+p", ProbeGenericMethod.Identity<Version>());
        } finally {
            handle.Dispose();
        }
    }
}

public class SharedGenericTwoInstantiationTests {
    private static IDetourHandle Patch(MethodBase target, MethodInfo injectionMethod, string owner) {
        return DetourBackend.Current.ApplyComposed(target, [new Injection(injectionMethod, new InjectAt.Tail(), owner, 0)]);
    }

    private static MethodInfo Ping<T>() {
        return typeof(ProbeBox<T>).GetMethod(nameof(ProbeBox<T>.Ping))!;
    }

    [Fact]
    public void SharedBodyKey_CollapsesReferenceTypesAndKeepsValueTypesApart() {
        MethodBase text = MethodIdentity.SharedBodyKey(Ping<string>());
        MethodBase version = MethodIdentity.SharedBodyKey(Ping<Version>());
        MethodBase number = MethodIdentity.SharedBodyKey(Ping<int>());

        Assert.Equal(text, version);
        Assert.NotEqual(text, number);
        Assert.Equal(typeof(ProbeBox<object>), text.DeclaringType);
    }

    [Fact]
    public void GenericStructReceiver_IsNotTreatedAsASharedBody() {
        MethodBase target = typeof(ProbeStructBox<string>).GetMethod(nameof(ProbeStructBox<string>.Ping))!;

        Assert.False(WrapperComposer.SharesGenericBody(target));
        Assert.Equal(target, MethodIdentity.SharedBodyKey(target));
    }

    [Fact]
    public void TwoInstantiationsOfTheSameSharedBody_BothKeepTheirPatches() {
        IDetourHandle a = Patch(Ping<string>(), typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, "probe.a");
        IDetourHandle b = Patch(Ping<Version>(), typeof(ProbeOtherInjection).GetMethod(nameof(ProbeOtherInjection.AfterPing))!, "probe.b");
        try {
            Assert.Equal("orig+p", new ProbeBox<string>().Ping());
            Assert.Equal("orig+q", new ProbeBox<Version>().Ping());
            Assert.Equal("orig", new ProbeBox<Guid>().Ping());
            Assert.Equal("orig", new ProbeBox<int>().Ping());
        } finally {
            b.Dispose();
            a.Dispose();
        }

        Assert.Equal("orig", new ProbeBox<string>().Ping());
        Assert.Equal("orig", new ProbeBox<Version>().Ping());
    }

    [Fact]
    public void RemovingOneInstantiationsPatch_RecomposesAndKeepsTheRest() {
        IDetourHandle a = Patch(Ping<string>(), typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, "probe.a");
        IDetourHandle b = Patch(Ping<Version>(), typeof(ProbeOtherInjection).GetMethod(nameof(ProbeOtherInjection.AfterPing))!, "probe.b");
        try {
            a.Dispose();

            Assert.Equal("orig", new ProbeBox<string>().Ping());
            Assert.Equal("orig+q", new ProbeBox<Version>().Ping());
        } finally {
            b.Dispose();
            a.Dispose();
        }

        Assert.Equal("orig", new ProbeBox<Version>().Ping());
    }

    [Fact]
    public void ThreeInstantiations_ValueTypeKeepsItsOwnEntry() {
        IDetourHandle a = Patch(Ping<string>(), typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, "probe.a");
        IDetourHandle b = Patch(Ping<Version>(), typeof(ProbeOtherInjection).GetMethod(nameof(ProbeOtherInjection.AfterPing))!, "probe.b");
        IDetourHandle c = Patch(Ping<long>(), typeof(ProbeThirdInjection).GetMethod(nameof(ProbeThirdInjection.AfterPing))!, "probe.c");
        try {
            Assert.Equal("orig+p", new ProbeBox<string>().Ping());
            Assert.Equal("orig+q", new ProbeBox<Version>().Ping());
            Assert.Equal("orig+r", new ProbeBox<long>().Ping());
            Assert.Equal("orig", new ProbeBox<int>().Ping());
        } finally {
            c.Dispose();
            b.Dispose();
            a.Dispose();
        }

        Assert.Equal("orig", new ProbeBox<long>().Ping());
    }
}

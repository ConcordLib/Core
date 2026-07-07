using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public class IsolationBox<T> {
    private readonly T value;

    public IsolationBox(T value) {
        this.value = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public T Get() {
        return value;
    }
}

public static class IsolationRegistry<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Describe() {
        return typeof(T).Name;
    }
}

public static class IsolationStringTailInjection {
    public static void AfterGet(ControlHandle<string> ch) {
        ch.ReturnValue = "patched";
    }
}

public static class IsolationIntTailInjection {
    public static void AfterGet(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
    }
}

public class GenericInstantiationIsolationTests {
    [Fact]
    public void Tail_RefTypeInstanceInstantiation_IsRejected() {
        MethodInfo injectionMethod = typeof(IsolationStringTailInjection).GetMethod(nameof(IsolationStringTailInjection.AfterGet))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Patcher.For(typeof(IsolationBox<string>), nameof(IsolationBox<string>.Get))
                .Tail(injectionMethod)
                .Apply());

        Assert.Equal("CONC061", ex.Code);
        Assert.Equal("hi", new IsolationBox<string>("hi").Get());
    }

    [Fact]
    public void Tail_RefTypeStaticInstantiation_IsRejected() {
        MethodInfo injectionMethod = typeof(IsolationStringTailInjection).GetMethod(nameof(IsolationStringTailInjection.AfterGet))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Patcher.For(typeof(IsolationRegistry<string>), nameof(IsolationRegistry<string>.Describe))
                .Tail(injectionMethod)
                .Apply());

        Assert.Equal("CONC061", ex.Code);
        Assert.Equal("String", IsolationRegistry<string>.Describe());
        Assert.Equal("Version", IsolationRegistry<Version>.Describe());
    }

    [Fact]
    public void Tail_ValueTypeInstantiation_IsAllowed_AndDoesNotLeakToOtherValueTypes() {
        Assert.Equal(5, new IsolationBox<int>(5).Get());
        Assert.Equal(7L, new IsolationBox<long>(7L).Get());

        MethodInfo injectionMethod = typeof(IsolationIntTailInjection).GetMethod(nameof(IsolationIntTailInjection.AfterGet))!;

        IPatchHandle handle = Patcher.For(typeof(IsolationBox<int>), nameof(IsolationBox<int>.Get))
            .Tail(injectionMethod)
            .Apply();
        try {
            Assert.Equal(99, new IsolationBox<int>(5).Get());
            Assert.Equal(7L, new IsolationBox<long>(7L).Get());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(5, new IsolationBox<int>(5).Get());
        Assert.Equal(7L, new IsolationBox<long>(7L).Get());
    }
}

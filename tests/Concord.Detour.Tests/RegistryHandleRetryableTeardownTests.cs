using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using MonoMod.Core;
using Xunit;

namespace Concord.Detour.Tests;

[CollectionDefinition(Name)]
public sealed class DetourFactorySwapCollection {
    public const string Name = "DetourFactorySwap";
}

public static class RetryableTeardownTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class RetryableTeardownInjection {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }

    public static void AddTen(ControlHandle<int> ch) {
        ch.ReturnValue += 10;
    }
}

internal sealed class FaultyDetourFactory : IDetourFactory {
    private readonly IDetourFactory inner;
    private readonly MethodBase armedSource;
    private bool armed;

    public FaultyDetourFactory(IDetourFactory inner, MethodBase armedSource) {
        this.inner = inner;
        this.armedSource = armedSource;
    }

    public bool SupportsNativeDetourOrigEntrypoint => inner.SupportsNativeDetourOrigEntrypoint;

    public void ArmNextCallForTarget() {
        armed = true;
    }

    public ICoreDetour CreateDetour(CreateDetourRequest request) {
        if (armed && request.Source == armedSource) {
            armed = false;
            throw new InvalidOperationException("Simulated CreateDetour failure.");
        }

        return inner.CreateDetour(request);
    }

    public ICoreNativeDetour CreateNativeDetour(CreateNativeDetourRequest request) {
        return inner.CreateNativeDetour(request);
    }
}

[Collection(DetourFactorySwapCollection.Name)]
public sealed class RegistryHandleRetryableTeardownTests {
    [Fact]
    public void Dispose_WhenRecomposeThrows_LeavesHandleRetryable() {
        MethodBase target = typeof(RetryableTeardownTarget).GetMethod(nameof(RetryableTeardownTarget.Value))!;
        MethodInfo addOne = typeof(RetryableTeardownInjection).GetMethod(nameof(RetryableTeardownInjection.AddOne))!;
        MethodInfo addTen = typeof(RetryableTeardownInjection).GetMethod(nameof(RetryableTeardownInjection.AddTen))!;
        Injection first = new Injection(addOne, new InjectAt.Tail(), "A", 0);
        Injection second = new Injection(addTen, new InjectAt.Tail(), "B", 0);

        IDetourFactory original = DetourFactory.Current;
        FaultyDetourFactory faulty = new FaultyDetourFactory(original, target);
        DetourFactory.SetCurrentFactory(_ => faulty);
        try {
            IDetourHandle handleA = TargetDetourRegistry.Add(target, [first]);
            IDetourHandle handleB = TargetDetourRegistry.Add(target, [second]);
            Assert.True(handleA.IsApplied);
            Assert.True(handleB.IsApplied);
            Assert.Equal(12, RetryableTeardownTarget.Value());

            faulty.ArmNextCallForTarget();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => handleA.Dispose());
            Assert.Equal("Simulated CreateDetour failure.", error.Message);

            handleA.Dispose();
            Assert.Equal(11, RetryableTeardownTarget.Value());

            handleB.Dispose();
            Assert.False(handleB.IsApplied);
            Assert.Equal(1, RetryableTeardownTarget.Value());
        } finally {
            DetourFactory.SetCurrentFactory(_ => original);
        }
    }
}

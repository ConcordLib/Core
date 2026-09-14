using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.AttachedData;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public class AttachedSeedTarget {
    public int observed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Compute() {
        return 1;
    }
}

#pragma warning disable CS0109, CS0414
public class AttachedUnmarkedInjectionMethod : AttachedSeedTarget {
    public int notOnTarget;

    public void Head(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        ch.Cancel();
    }
}

public class AttachedCounterInjectionMethod : AttachedSeedTarget {
    [Attached]
    public int rage;

    public void Head(ControlHandle<int> ch) {
        rage += 5;
        ch.ReturnValue = rage;
        ch.Cancel();
    }
}

public class AttachedByRefInjectionMethod : AttachedSeedTarget {
    [Attached]
    public int hits;

    public void Head(ControlHandle<int> ch) {
        Bump(ref hits);
        ch.ReturnValue = hits;
        ch.Cancel();
    }

    private static void Bump(ref int value) {
        value += 3;
    }
}

public class AttachedAlongsideShadowInjectionMethod : AttachedSeedTarget {
    [Attached]
    public int extra;

    private new int observed;

    public void Head(ControlHandle<int> ch) {
        observed = 7;
        extra += 2;
        ch.ReturnValue = extra;
        ch.Cancel();
    }
}

public class AttachedNeighbour {
    public int hits;
}

public class AttachedNeighbourInjectionMethod : AttachedSeedTarget {
    [Attached]
    public int hits;

    public static AttachedNeighbour Neighbour = new AttachedNeighbour();

    public void Head(ControlHandle<int> ch) {
        hits += 1;
        Neighbour.hits += 10;
        ch.ReturnValue = hits;
        ch.Cancel();
    }
}

public class AttachedAutoPropertyInjectionMethod : AttachedSeedTarget {
    public int Scratch { get; set; }

    public void Head(ControlHandle<int> ch) {
        Scratch += 4;
        ch.ReturnValue = Scratch;
        ch.Cancel();
    }
}

public struct AttachedPoint {
    public int X;
}

public class AttachedStructInjectionMethod : AttachedSeedTarget {
    [Attached]
    public AttachedPoint point;

    [Attached]
    public List<int>? seen;

    public void Head(ControlHandle<int> ch) {
        point.X += 2;
        seen ??= new List<int>();
        seen.Add(point.X);
        ch.ReturnValue = point.X + seen.Count;
        ch.Cancel();
    }
}

public class AttachedTailInjectionMethod : AttachedSeedTarget {
    [Attached]
    public int tailHits;

    public void Tail(ControlHandle<int> ch) {
        tailHits += 1;
        ch.ReturnValue = tailHits;
    }
}

public class AttachedMismatchShadowInjectionMethod : AttachedSeedTarget {
    private new long observed;

    public void Head(ControlHandle<int> ch) {
        observed = 7L;
        ch.ReturnValue = 5;
        ch.Cancel();
    }
}
#pragma warning restore CS0109, CS0414

public sealed class ShadowAttachedFieldTests {
    private static IDetourHandle Apply(Type declaration) {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = declaration.GetMethod("Head")!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        return DetourBackend.Current.Apply(target, result.Wrapper);
    }

    [Fact]
    public void Compose_UnmarkedFieldAbsentOnTarget_ThrowsCONC003() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedUnmarkedInjectionMethod).GetMethod(nameof(AttachedUnmarkedInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC003", ex.Code);
    }

    [Fact]
    public void Compose_AttachedField_ReadsAndWritesPerInstance() {
        using IDetourHandle handle = Apply(typeof(AttachedCounterInjectionMethod));

        AttachedSeedTarget first = new AttachedSeedTarget();
        AttachedSeedTarget second = new AttachedSeedTarget();

        Assert.Equal(5, first.Compute());
        Assert.Equal(10, first.Compute());
        Assert.Equal(5, second.Compute());
    }

    [Fact]
    public void Compose_AttachedField_IsReachableThroughItsSlot() {
        using IDetourHandle handle = Apply(typeof(AttachedCounterInjectionMethod));

        AttachedSeedTarget instance = new AttachedSeedTarget();
        instance.Compute();

        IAttachedSlot slot = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(AttachedCounterInjectionMethod), "rage", typeof(int)));

        Assert.Equal(5, slot.Get(instance));

        slot.Set(instance, 100);

        Assert.Equal(105, instance.Compute());
    }

    [Fact]
    public void Compose_AttachedFieldByRef_WritesThroughTheReference() {
        using IDetourHandle handle = Apply(typeof(AttachedByRefInjectionMethod));

        AttachedSeedTarget instance = new AttachedSeedTarget();

        Assert.Equal(3, instance.Compute());
        Assert.Equal(6, instance.Compute());
    }

    [Fact]
    public void Compose_AttachedAlongsideRealShadow_WritesBoth() {
        using IDetourHandle handle = Apply(typeof(AttachedAlongsideShadowInjectionMethod));

        AttachedSeedTarget instance = new AttachedSeedTarget();

        Assert.Equal(2, instance.Compute());
        Assert.Equal(7, instance.observed);
    }

    [Fact]
    public void Compose_SameNamedFieldOnAnotherObject_IsNotLowered() {
        using IDetourHandle handle = Apply(typeof(AttachedNeighbourInjectionMethod));

        AttachedSeedTarget instance = new AttachedSeedTarget();

        Assert.Equal(1, instance.Compute());
        Assert.Equal(10, AttachedNeighbourInjectionMethod.Neighbour.hits);
    }

    [Fact]
    public void Compose_AutoPropertyOnDeclaration_IsAttachedNotAnError() {
        using IDetourHandle handle = Apply(typeof(AttachedAutoPropertyInjectionMethod));

        AttachedSeedTarget first = new AttachedSeedTarget();
        AttachedSeedTarget second = new AttachedSeedTarget();

        Assert.Equal(4, first.Compute());
        Assert.Equal(8, first.Compute());
        Assert.Equal(4, second.Compute());
    }

    [Fact]
    public void SlotFor_SameNameDifferentValueType_Throws() {
        AttachedStorage.SlotFor(typeof(AttachedSeedTarget), "reallocated", typeof(int));

        Assert.Throws<InvalidOperationException>(() =>
            AttachedStorage.SlotFor(typeof(AttachedSeedTarget), "reallocated", typeof(long)));
    }

    [Fact]
    public void Compose_StructAndReferenceAttachedFields_RoundTrip() {
        using IDetourHandle handle = Apply(typeof(AttachedStructInjectionMethod));

        AttachedSeedTarget instance = new AttachedSeedTarget();

        Assert.Equal(3, instance.Compute());
        Assert.Equal(6, instance.Compute());
    }

    [Fact]
    public void Compose_AttachedFieldAtTail_Lowers() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedTailInjectionMethod).GetMethod(nameof(AttachedTailInjectionMethod.Tail))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        using IDetourHandle handle = DetourBackend.Current.Apply(target, result.Wrapper);

        AttachedSeedTarget instance = new AttachedSeedTarget();

        Assert.Equal(1, instance.Compute());
        Assert.Equal(2, instance.Compute());
    }

    [Fact]
    public void Compose_MismatchedShadow_ThrowsCONC002() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedMismatchShadowInjectionMethod).GetMethod(nameof(AttachedMismatchShadowInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC002", ex.Code);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class CatchSurfaceTarget {
    public static int Compute() {
        return 1;
    }
}

public static class CatchSurfaceInjection {
    public static void OnThrow(Exception error) {
    }
}

public sealed class CatchSurfaceTests {
    [Fact]
    public void InjectAttribute_Catch_ResolvedAt_ReturnsInjectAtCatch() {
        InjectAttribute attr = new InjectAttribute(At.Catch, "M");
        Assert.Equal(At.Catch, attr.At);
        Assert.Equal(new InjectAt.Catch(), attr.ResolvedAt);
    }

    [Fact]
    public void PatchBuilder_Inject_AtCatch_ResolvesToInjectAtCatch() {
        MethodInfo injectionMethod = typeof(CatchSurfaceInjection).GetMethod(nameof(CatchSurfaceInjection.OnThrow))!;
        PatchBuilder builder = Patcher.For(typeof(CatchSurfaceTarget), nameof(CatchSurfaceTarget.Compute));
        builder.Inject(At.Catch, injectionMethod);

        FieldInfo injectionsField = typeof(PatchBuilder).GetField("injections", BindingFlags.NonPublic | BindingFlags.Instance)!;
        List<Injection> recorded = (List<Injection>)injectionsField.GetValue(builder)!;
        Injection single = Assert.Single(recorded);
        Assert.Equal(new InjectAt.Catch(), single.At);
    }

    [Fact]
    public void Patcher_ToInjectAt_AtCatch_ResolvesToInjectAtCatch() {
        MethodInfo toInjectAt = typeof(Patcher).GetMethod("ToInjectAt", BindingFlags.NonPublic | BindingFlags.Static)!;
        InjectAt resolved = (InjectAt)toInjectAt.Invoke(null, new object[] { At.Catch })!;
        Assert.Equal(new InjectAt.Catch(), resolved);
    }

    [Fact]
    public void InjectAttribute_UnmappedPosition_StillThrowsCONC116() {
        InjectAttribute attr = new InjectAttribute((At)999, "M");
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => attr.ResolvedAt);
        Assert.Equal("CONC116", ex.Code);
    }

    [Fact]
    public void PatchBuilder_Inject_UnmappedPosition_StillThrowsCONC116() {
        MethodInfo injectionMethod = typeof(CatchSurfaceInjection).GetMethod(nameof(CatchSurfaceInjection.OnThrow))!;
        PatchBuilder builder = Patcher.For(typeof(CatchSurfaceTarget), nameof(CatchSurfaceTarget.Compute));

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => builder.Inject((At)999, injectionMethod));
        Assert.Equal("CONC116", ex.Code);
    }

    [Fact]
    public void Patcher_Patch_UnmappedPosition_StillThrowsCONC116() {
        MethodInfo injectionMethod = typeof(CatchSurfaceInjection).GetMethod(nameof(CatchSurfaceInjection.OnThrow))!;
        MethodInfo target = typeof(CatchSurfaceTarget).GetMethod(nameof(CatchSurfaceTarget.Compute))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => Patcher.Patch(target, injectionMethod, (At)999));
        Assert.Equal("CONC116", ex.Code);
    }

    [Fact]
    public void At_Catch_IsTheHighestDeclaredOrdinal() {
        int catchValue = (int)At.Catch;
        int maxValue = Enum.GetValues(typeof(At)).Cast<int>().Max();
        Assert.Equal(maxValue, catchValue);
    }

    [Fact]
    public void InjectAtCatch_Record_IsParameterless() {
        ConstructorInfo ctor = Assert.Single(typeof(InjectAt.Catch).GetConstructors());
        Assert.Empty(ctor.GetParameters());
    }
}

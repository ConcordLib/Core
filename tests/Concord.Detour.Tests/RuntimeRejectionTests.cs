using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using MonoMod.Core;
using Xunit;

namespace Concord.Detour.Tests;

public static class RuntimeRejectionTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class RuntimeRejectionInjection {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

internal sealed class RejectingDetourFactory : IDetourFactory {
    private readonly IDetourFactory inner;
    private readonly MethodBase source;

    public RejectingDetourFactory(IDetourFactory inner, MethodBase source) {
        this.inner = inner;
        this.source = source;
    }

    public bool SupportsNativeDetourOrigEntrypoint => inner.SupportsNativeDetourOrigEntrypoint;

    public ICoreDetour CreateDetour(CreateDetourRequest request) {
        if (request.Source == source) {
            throw new InvalidProgramException("Invalid IL code: IL_0000: stloc.1");
        }

        return inner.CreateDetour(request);
    }

    public ICoreNativeDetour CreateNativeDetour(CreateNativeDetourRequest request) {
        return inner.CreateNativeDetour(request);
    }
}

[Collection(DetourFactorySwapCollection.Name)]
public sealed class RuntimeRejectionTests {
    [Fact]
    public void Add_RuntimeRejectsBody_ThrowsCONC144WithAssemblyTable() {
        MethodBase target = typeof(RuntimeRejectionTarget).GetMethod(nameof(RuntimeRejectionTarget.Value))!;
        MethodInfo addOne = typeof(RuntimeRejectionInjection).GetMethod(nameof(RuntimeRejectionInjection.AddOne))!;
        Injection injection = new Injection(addOne, new InjectAt.Tail(), "A", 0);

        IDetourFactory original = DetourFactory.Current;
        DetourFactory.SetCurrentFactory(_ => new RejectingDetourFactory(original, target));
        try {
            ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => TargetDetourRegistry.Add(target, [injection]));

            Assert.Equal("CONC144", ex.Code);
            Assert.Contains("stloc.1", ex.Message);
            Assert.Contains("assemblies[", ex.Message);
            Assert.Contains(typeof(RuntimeRejectionTarget).Assembly.GetName().Name!, ex.Message);
            Assert.Equal(1, RuntimeRejectionTarget.Value());
        } finally {
            DetourFactory.SetCurrentFactory(_ => original);
        }
    }
}

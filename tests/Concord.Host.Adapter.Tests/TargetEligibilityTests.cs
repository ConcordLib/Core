using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public abstract class Bodyless {
    public abstract int Declared();

    public virtual int Implemented() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int Intrinsic();

    [DllImport("libc", EntryPoint = "getpid")]
    public static extern int Native();
}

public interface IDeclaredOnly {
    int Declared();
}

public delegate int Hop(int step);

public static class Initialized {
    public static readonly int Slot = 3;

    public static int Open<T>() {
        return 0;
    }
}

public class TargetEligibilityTests {
    private static MethodBase Of(string name) {
        return typeof(Bodyless).GetMethod(name)!;
    }

    private static DynamicMethod Minted() {
        DynamicMethod minted = new DynamicMethod("minted", typeof(int), Type.EmptyTypes);
        ILGenerator il = minted.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        return minted;
    }

    private static MethodBase InDynamicAssembly() {
        AssemblyBuilder built = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("EligibilityProbe"),
            AssemblyBuilderAccess.Run);
        TypeBuilder type = built.DefineDynamicModule("probe").DefineType("Probe", TypeAttributes.Public);
        MethodBuilder method = type.DefineMethod(
            "Answer",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Answer")!;
    }

    [Fact]
    public void AMethodWithABodyIsReplaced() {
        TargetVerdict verdict = TargetEligibility.Judge(Of(nameof(Bodyless.Implemented)));

        Assert.Equal(TargetHandling.Replace, verdict.Handling);
        Assert.Equal(string.Empty, verdict.Code);
        Assert.Equal(string.Empty, verdict.Message);
        verdict.ThrowIfRefused();
    }

    [Fact]
    public void ShapesTheSurveyMeasuredAsEligibleAreNotRefused() {
        MethodBase[] replaceable = [
            typeof(Initialized).TypeInitializer!,
            typeof(Initialized).GetMethod(nameof(Initialized.Open))!,
            typeof(Initialized).GetMethod(nameof(Initialized.Open))!.MakeGenericMethod(typeof(string)),
            typeof(Bodyless).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0],
            Of(nameof(Bodyless.Implemented)),
        ];

        foreach (MethodBase target in replaceable) {
            Assert.Equal(TargetHandling.Replace, TargetEligibility.Judge(target).Handling);
        }
    }

    [Fact]
    public void AnAbstractTargetIsRefusedWithTheNoBodyCode() {
        TargetVerdict verdict = TargetEligibility.Judge(Of(nameof(Bodyless.Declared)));

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal("CONC168", verdict.Code);
        Assert.Equal(TargetEligibility.NoBodyCode, verdict.Code);
        Assert.Contains("Concord.Host.Adapter.Tests.Bodyless.Declared", verdict.Message);
        Assert.Contains("is abstract", verdict.Message);
        Assert.Contains("Patch an override or an implementation instead.", verdict.Message);
        Assert.DoesNotContain(TargetEligibility.FallbackMarker, verdict.Message);

        ConcordEmitException thrown = Assert.Throws<ConcordEmitException>(verdict.ThrowIfRefused);
        Assert.Equal("CONC168", thrown.Code);
        Assert.StartsWith("CONC168: ", thrown.Message);
    }

    [Fact]
    public void AnInterfaceDeclarationIsRefused() {
        TargetVerdict verdict = TargetEligibility.Judge(typeof(IDeclaredOnly).GetMethod(nameof(IDeclaredOnly.Declared))!);

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal(TargetEligibility.NoBodyCode, verdict.Code);
        Assert.Contains("is abstract", verdict.Message);
    }

    [Fact]
    public void APInvokeTargetIsRefusedAndNamesTheDeclaration() {
        TargetVerdict verdict = TargetEligibility.Judge(Of(nameof(Bodyless.Native)));

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal(TargetEligibility.NoBodyCode, verdict.Code);
        Assert.Contains("is a p/invoke declaration", verdict.Message);
        Assert.Contains("Patch the managed code that calls it instead.", verdict.Message);
    }

    [Fact]
    public void AnInternalCallTargetIsRefused() {
        TargetVerdict verdict = TargetEligibility.Judge(Of(nameof(Bodyless.Intrinsic)));

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Contains("is an InternalCall intrinsic", verdict.Message);
    }

    [Fact]
    public void DelegateMembersAreRefusedAsRuntimeProvided() {
        MethodBase[] provided = [typeof(Hop).GetMethod(nameof(Hop.Invoke))!, typeof(Hop).GetConstructors()[0]];

        foreach (MethodBase target in provided) {
            TargetVerdict verdict = TargetEligibility.Judge(target);
            Assert.Equal(TargetHandling.Refuse, verdict.Handling);
            Assert.Equal(TargetEligibility.NoBodyCode, verdict.Code);
            Assert.Contains("is provided by the runtime, not by IL", verdict.Message);
        }
    }

    [Fact]
    public void AnArrayAccessorIsRefusedByTheCatchAllReason() {
        MethodBase accessor = typeof(int[]).GetMethod("Get")!;

        Assert.Equal(MethodImplAttributes.IL, accessor.MethodImplementationFlags);
        Assert.False(accessor.IsAbstract);

        TargetVerdict verdict = TargetEligibility.Judge(accessor);

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal(TargetEligibility.NoBodyCode, verdict.Code);
        Assert.Contains("has no IL body", verdict.Message);
    }

    [Fact]
    public void ADynamicMethodFallsBackBeforeAnythingReadsItsBody() {
        DynamicMethod minted = Minted();

        Assert.Throws<InvalidOperationException>(() => minted.GetMethodBody());

        TargetVerdict verdict = TargetEligibility.Judge(minted);

        Assert.Equal(TargetHandling.Detour, verdict.Handling);
        Assert.Equal(string.Empty, verdict.Code);
        Assert.StartsWith(TargetEligibility.FallbackMarker, verdict.Message);
        Assert.Contains("dynamic or collectible module", verdict.Message);
        verdict.ThrowIfRefused();
    }

    [Fact]
    public void AMethodInANonCollectibleDynamicAssemblyFallsBack() {
        MethodBase target = InDynamicAssembly();

        Assert.False(target.IsCollectible);
        Assert.True(target.Module.Assembly.IsDynamic);
        Assert.NotNull(target.GetMethodBody());

        TargetVerdict verdict = TargetEligibility.Judge(target);

        Assert.Equal(TargetHandling.Detour, verdict.Handling);
        Assert.StartsWith(TargetEligibility.FallbackMarker, verdict.Message);
    }

    [Fact]
    public void ARejectedComposedBodyFallsBackAndCarriesTheComposeReason() {
        TargetVerdict verdict = TargetEligibility.ForRejectedBody(
            Of(nameof(Bodyless.Implemented)),
            "instruction 4 loads a function pointer to a method with a vararg signature.");

        Assert.Equal(TargetHandling.Detour, verdict.Handling);
        Assert.StartsWith(TargetEligibility.FallbackMarker, verdict.Message);
        Assert.Contains("Concord.Host.Adapter.Tests.Bodyless.Implemented", verdict.Message);
        Assert.Contains("instruction 4 loads a function pointer to a method with a vararg signature", verdict.Message);
        Assert.Contains("still applies through a native detour", verdict.Message);
        verdict.ThrowIfRefused();
    }

    [Fact]
    public void ARuntimeRefusalAtPublishTimeFallsBack() {
        TargetVerdict verdict = TargetEligibility.ForRuntimeRefusal(
            Of(nameof(Bodyless.Implemented)),
            new NotSupportedException("Method body replacement is not supported for this method."));

        Assert.Equal(TargetHandling.Detour, verdict.Handling);
        Assert.StartsWith(TargetEligibility.FallbackMarker, verdict.Message);
        Assert.Contains("NotSupportedException", verdict.Message);
        Assert.Contains("Method body replacement is not supported for this method", verdict.Message);
    }

    [Fact]
    public void ATargetReportsItsFallbackOnceNoMatterHowOftenItRecomposes() {
        MethodBase first = Of(nameof(Bodyless.Implemented));
        MethodBase second = Of(nameof(Bodyless.Declared));

        Assert.True(TargetEligibility.ReportOnce(first));
        Assert.False(TargetEligibility.ReportOnce(first));
        Assert.False(TargetEligibility.ReportOnce(first));
        Assert.True(TargetEligibility.ReportOnce(second));
        Assert.False(TargetEligibility.ReportOnce(second));
        Assert.Throws<ArgumentNullException>(() => TargetEligibility.ReportOnce(null!));
    }

    [Fact]
    public void NoVerdictBuilderAcceptsAMissingArgument() {
        Assert.Throws<ArgumentNullException>(() => TargetEligibility.Judge(null!));
        Assert.Throws<ArgumentNullException>(() => TargetEligibility.ForRejectedBody(null!, "why"));
        Assert.Throws<ArgumentException>(() => TargetEligibility.ForRejectedBody(Of(nameof(Bodyless.Implemented)), " "));
        Assert.Throws<ArgumentNullException>(() => TargetEligibility.ForRuntimeRefusal(null!, new NotSupportedException()));
        Assert.Throws<ArgumentNullException>(
            () => TargetEligibility.ForRuntimeRefusal(Of(nameof(Bodyless.Implemented)), null!));
    }
}

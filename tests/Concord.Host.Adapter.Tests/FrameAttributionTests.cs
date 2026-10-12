using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Concord.Emit;
using Xunit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.Tests;

public static class Attributed {
    public static int Alpha(int value) {
        return value;
    }

    public static int Beta(int value) {
        return value;
    }

    public static int Gamma(int value) {
        return value;
    }

    public static int Target(int value) {
        return value + 1;
    }

    public static int Other(int value) {
        return value + 2;
    }
}

[Collection("AdapterStatics")]
public sealed class FrameAttributionTests {
    private static readonly MethodInfo Target = typeof(Attributed).GetMethod(nameof(Attributed.Target))!;

    private static readonly MethodInfo Other = typeof(Attributed).GetMethod(nameof(Attributed.Other))!;

    private static readonly MethodInfo Alpha = typeof(Attributed).GetMethod(nameof(Attributed.Alpha))!;

    private static readonly MethodInfo Beta = typeof(Attributed).GetMethod(nameof(Attributed.Beta))!;

    private static readonly MethodInfo Gamma = typeof(Attributed).GetMethod(nameof(Attributed.Gamma))!;

    [Fact]
    public void TwoOwnersInOneBodyEachGetSentinelsAndEachSentinelNamesItsOwnOwner() {
        int band = Len(Target);
        ComposedBody body = Body(
            Copied(OpCodes.Nop, 0),
            Injected(OpCodes.Nop, Alpha),
            Injected(OpCodes.Nop, Alpha),
            Copied(OpCodes.Nop, 1),
            Injected(OpCodes.Ret, Beta));

        FrameAttribution.Register(Target, body, Encoded(20, 0, 4, 8, 12, 16), [Owned(Alpha, "alpha"), Owned(Beta, "beta")]);

        Assert.Null(FrameAttribution.OwnerAt(Target, 0));
        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band + 4));
        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band + 8));
        Assert.Null(FrameAttribution.OwnerAt(Target, 1));
        Assert.Equal("beta", FrameAttribution.OwnerAt(Target, band + 16));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void AnOffsetInsideAnInstructionAttributesToTheInstructionItIsInsideWhenTheMapIsEmpty() {
        ComposedBody body = Body(Injected(OpCodes.Ldc_I4, Alpha), Copied(OpCodes.Ret, 0));
        WaylithDetourBackend.EmptyMapAlways = true;

        try {
            FrameAttribution.Register(Target, body, Encoded(6, 0, 5), [Owned(Alpha, "alpha"), Owned(Beta, "beta")]);

            Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, 0));
            Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, 4));
            Assert.Null(FrameAttribution.OwnerAt(Target, 5));
            FrameAttribution.Forget(Target);
        } finally {
            WaylithDetourBackend.EmptyMapAlways = false;
        }
    }

    [Fact]
    public void OneOwnerReachedTwiceAttributesInBothOfItsRuns() {
        int band = Len(Target);
        ComposedBody body = Body(
            Injected(OpCodes.Nop, Alpha),
            Copied(OpCodes.Nop, 0),
            Injected(OpCodes.Ret, Alpha));

        FrameAttribution.Register(Target, body, Encoded(3, 0, 1, 2), [Owned(Alpha, "alpha")]);

        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band));
        Assert.Null(FrameAttribution.OwnerAt(Target, 0));
        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band + 2));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void TwoInjectionMethodsOwnedByOneModGetTwoSentinelsAndBothNameThatOwner() {
        int band = Len(Target);
        ComposedBody body = Body(Injected(OpCodes.Nop, Alpha), Injected(OpCodes.Ret, Beta));

        FrameAttribution.Register(Target, body, Encoded(2, 0, 1), [Owned(Alpha, "one"), Owned(Beta, "one")]);

        Assert.Equal("one", FrameAttribution.OwnerAt(Target, band));
        Assert.Equal("one", FrameAttribution.OwnerAt(Target, band + 1));
        Assert.Equal(["one"], FrameAttribution.OwnersRegisteredFor(Target));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void TwoInjectionsFromOneOwnerInOneGapNameTheOwnerAtTwoDistinctSentinels() {
        int band = Len(Target);
        ComposedBody body = Body(
            Copied(OpCodes.Nop, 0),
            Injected(OpCodes.Nop, Alpha),
            Injected(OpCodes.Nop, Beta),
            Copied(OpCodes.Ret, 1));

        FrameAttribution.Register(Target, body, Encoded(4, 0, 1, 2, 3), [Owned(Alpha, "one"), Owned(Beta, "one")]);

        Assert.Equal("one", FrameAttribution.OwnerAt(Target, band + 1));
        Assert.Equal("one", FrameAttribution.OwnerAt(Target, band + 2));
        Assert.Null(FrameAttribution.OwnerAt(Target, 0));
        Assert.Null(FrameAttribution.OwnerAt(Target, 1));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void TranspilerIlWithNoOriginAttributesToNobody() {
        int band = Len(Target);
        ComposedBody body = Body(Copied(OpCodes.Nop, 0), Rewritten(OpCodes.Nop), Injected(OpCodes.Ret, Alpha));

        FrameAttribution.Register(Target, body, Encoded(3, 0, 1, 2), [Owned(Alpha, "alpha")]);

        Assert.Null(FrameAttribution.OwnerAt(Target, 0));
        Assert.Null(FrameAttribution.OwnerAt(Target, band + 1));
        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band + 2));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void AnInjectionMethodTheOrderedListNeverMentionsAttributesToNobody() {
        int band = Len(Target);
        ComposedBody body = Body(Injected(OpCodes.Nop, Gamma), Copied(OpCodes.Ret, 0));

        FrameAttribution.Register(Target, body, Encoded(2, 0, 1), [Owned(Alpha, "alpha")]);

        Assert.Null(FrameAttribution.OwnerAt(Target, band));
        Assert.Null(FrameAttribution.OwnerAt(Target, 0));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void AnOffsetPastTheEndOfTheBodyAttributesNothingEvenWhenTheLastRunIsOwned() {
        int band = Len(Target);
        ComposedBody body = Body(Copied(OpCodes.Nop, 0), Injected(OpCodes.Ret, Alpha));

        FrameAttribution.Register(Target, body, Encoded(2, 0, 1), [Owned(Alpha, "alpha")]);

        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band + 1));
        Assert.Null(FrameAttribution.OwnerAt(Target, band + 2));
        Assert.Null(FrameAttribution.OwnerAt(Target, band + 4000));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void ATranspilerReorderingTargetIlLeavesAttributionAlone() {
        int band = Len(Target);
        ComposedBody body = Body(
            Copied(OpCodes.Nop, band - 1),
            Injected(OpCodes.Nop, Alpha),
            Copied(OpCodes.Ret, 0));

        FrameAttribution.Register(Target, body, Encoded(3, 0, 1, 2), [Owned(Alpha, "alpha")]);

        Assert.Null(FrameAttribution.OwnerAt(Target, band - 1));
        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band + 1));
        Assert.Null(FrameAttribution.OwnerAt(Target, 0));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void TheTightestSentinelIsOnePastTheLastOffsetTheTargetCouldName() {
        int band = Len(Target);
        ComposedBody body = Body(Injected(OpCodes.Nop, Alpha), Copied(OpCodes.Ret, band - 1));

        FrameAttribution.Register(Target, body, Encoded(2, 0, 1), [Owned(Alpha, "alpha")]);

        Assert.Equal("alpha", FrameAttribution.OwnerAt(Target, band));
        Assert.Null(FrameAttribution.OwnerAt(Target, band - 1));
        FrameAttribution.Forget(Target);
    }

    [Fact]
    public void AttributionExposesNoOffsetMapBuilderSoTheMapHasExactlyOne() {
        MethodInfo[] builders = [
            .. typeof(FrameAttribution)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => typeof(IEnumerable<OffsetPair>).IsAssignableFrom(method.ReturnType)),
        ];

        Assert.Empty(builders);
        Assert.NotNull(typeof(WaylithDetourBackend).GetMethod(nameof(WaylithDetourBackend.OffsetMap)));
    }

    [Fact]
    public void AFrameInAValueInstantiationReadsTheTableRegisteredForAReferenceOne() {
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase number = typeof(Held<int>).GetMethod(nameof(Held<int>.Ping))!;
        Assert.NotEqual(MethodIdentity.SharedBodyKey(text), MethodIdentity.SharedBodyKey(number));
        int band = Len(text);
        ComposedBody body = Body(Injected(OpCodes.Ret, Alpha));

        FrameAttribution.Register(text, body, Encoded(1, 0), [Owned(Alpha, "alpha")]);

        Assert.Equal("alpha", FrameAttribution.OwnerAt(number, band));
        Assert.Equal(["alpha"], FrameAttribution.OwnersRegisteredFor(number));
        FrameAttribution.Forget(number);
        Assert.Null(FrameAttribution.OwnerAt(text, band));
    }

    [Fact]
    public void AnUnregisteredTargetAttributesNothing() {
        FrameAttribution.Forget(Other);

        Assert.Null(FrameAttribution.OwnerAt(Other, 0));
        Assert.Empty(FrameAttribution.OwnersRegisteredFor(Other));
    }

    [Fact]
    public void ForgettingATargetStopsItAttributing() {
        int band = Len(Other);
        ComposedBody body = Body(Injected(OpCodes.Ret, Alpha));
        FrameAttribution.Register(Other, body, Encoded(1, 0), [Owned(Alpha, "alpha")]);
        Assert.Equal("alpha", FrameAttribution.OwnerAt(Other, band));

        FrameAttribution.Forget(Other);

        Assert.Null(FrameAttribution.OwnerAt(Other, band));
    }

    [Fact]
    public void RepublishingReplacesTheOwnerTableRatherThanGrowingIt() {
        int band = Len(Other);
        ComposedBody first = Body(Injected(OpCodes.Ret, Alpha));
        FrameAttribution.Register(Other, first, Encoded(1, 0), [Owned(Alpha, "alpha")]);

        ComposedBody second = Body(Injected(OpCodes.Ret, Beta));
        FrameAttribution.Register(Other, second, Encoded(1, 0), [Owned(Beta, "beta")]);

        Assert.Equal(["beta"], FrameAttribution.OwnersRegisteredFor(Other));
        Assert.Equal("beta", FrameAttribution.OwnerAt(Other, band));
        FrameAttribution.Forget(Other);
    }

    [Fact]
    public void AnEncodedBodyWhoseOffsetCountDoesNotMatchIsRefused() {
        ComposedBody body = Body(Copied(OpCodes.Ret, 0));

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => FrameAttribution.Register(Target, body, Encoded(8, 0, 4), []));

        Assert.Contains("cannot be paired into an attribution table", failure.Message);
    }

    [Fact]
    public void AFrameReportingNoIlOffsetAttributesNothing() {
        ComposedBody body = Body(Injected(OpCodes.Ret, Alpha));
        FrameAttribution.Register(Target, body, Encoded(1, 0), [Owned(Alpha, "alpha")]);

        Assert.Null(FrameAttribution.OwnerAt(Target, -1));
        FrameAttribution.Forget(Target);
    }

    private static ComposedBody Body(params ComposedInstruction[] stream) {
        return new ComposedBody(stream, [], [], false);
    }

    private static EncodedBody Encoded(int codeSize, params int[] offsets) {
        return new EncodedBody([], offsets, 1, codeSize);
    }

    private static Injection Owned(MethodInfo injection, string owner) {
        return new Injection(injection, new InjectAt.Head(), owner, 0);
    }

    private static ComposedInstruction Copied(OpCode opcode, int origin) {
        return new ComposedInstruction(new CodeInstruction(opcode), origin, null);
    }

    private static ComposedInstruction Injected(OpCode opcode, MethodBase injection) {
        return new ComposedInstruction(new CodeInstruction(opcode), null, injection);
    }

    private static ComposedInstruction Rewritten(OpCode opcode) {
        return new ComposedInstruction(new CodeInstruction(opcode), null, null);
    }

    private static int Len(MethodBase target) {
        return target.GetMethodBody()!.GetILAsByteArray()!.Length;
    }
}

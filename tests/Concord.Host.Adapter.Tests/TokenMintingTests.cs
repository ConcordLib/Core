using System.Reflection;
using Xunit;

namespace Concord.Host.Adapter.Tests;

internal sealed class RecordingMinter : IMetadataMinter {
    internal List<(string Kind, object Operand, Module Target)> Calls { get; } = new();

    internal int Next { get; set; } = 0x0a000001;

    public int Token(Module target, MethodBase method) {
        return Record("method", method, target);
    }

    public int Token(Module target, FieldInfo field) {
        return Record("field", field, target);
    }

    public int Token(Module target, Type type) {
        return Record("type", type, target);
    }

    public int Token(Module target, string literal) {
        return Record("literal", literal, target);
    }

    public int LocalsToken(Module target, byte[] signature) {
        return Record("locals", signature, target);
    }

    private int Record(string kind, object operand, Module target) {
        Calls.Add((kind, operand, target));
        return Next++;
    }
}

public class TokenMintingTests {
    private static readonly Module Here = typeof(TokenMintingTests).Module;

    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public TokenMintingTests(Xunit.Abstractions.ITestOutputHelper output) {
        this.output = output;
    }

    [GameCorpusFact]
    public void TheMethodSpecRefusalFiresOnGameBodies() {
        TheMethodSpecRefusalFiresOnRealBodies(RealBodies.GameCorpus);
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void TheMethodSpecRefusalFiresOnRealBodies(string corpus) {
        CorpusResult result = Corpus.Run(RealBodies.Of(corpus), (_, _, _, _) => { });

        output.WriteLine($"corpus {corpus}: {result.Summary}");
        Assert.True(
            result.MethodSpecBodies > 0,
            $"no body in corpus {corpus} reached the MethodSpec refusal, so nothing measured it. {result.Summary}");
        Assert.Equal(0, result.UnreachableCalls);
    }

    private static (WaylithTokenSource Source, RecordingMinter Minter) Bound() {
        RecordingMinter minter = new RecordingMinter();
        return (new WaylithTokenSource(Here, minter), minter);
    }

    [Fact]
    public void EachOperandKindGoesToItsOwnMintAndReturnsThatToken() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();
        MethodBase method = typeof(Wide).GetMethod("Pub")!;
        FieldInfo field = typeof(Wide).GetField("Slot")!;
        byte[] signature = [0x07, 0x01, 0x08];

        Assert.Equal(0x0a000001, source.TokenFor(typeof(int)));
        Assert.Equal(0x0a000002, source.TokenFor(field));
        Assert.Equal(0x0a000003, source.TokenFor(method));
        Assert.Equal(0x0a000004, source.TokenFor("hello"));
        Assert.Equal(0x0a000005, source.TokenForLocals(signature));

        Assert.Equal(
            new[] { "type", "field", "method", "literal", "locals" },
            minter.Calls.Select(call => call.Kind).ToArray());
        Assert.All(minter.Calls, call => Assert.Same(Here, call.Target));
        Assert.Same(signature, minter.Calls[4].Operand);
    }

    [Fact]
    public void AConstructorGoesToTheMethodMint() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();
        ConstructorInfo constructor = typeof(Wide).GetConstructors()[0];

        source.TokenFor(constructor);

        Assert.Equal("method", minter.Calls[0].Kind);
        Assert.Same(constructor, minter.Calls[0].Operand);
    }

    [Fact]
    public void AConstructedGenericMethodIsRefused() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();
        MethodInfo definition = typeof(Wide).GetMethod("Generic", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo constructed = definition.MakeGenericMethod(typeof(int));

        TokenAccessException thrown = Assert.Throws<TokenAccessException>(() => source.TokenFor(constructed));
        TokenAccessException open = Assert.Throws<TokenAccessException>(() => source.TokenFor(definition));

        Assert.Contains("Concord.Host.Adapter.Tests.Wide.Generic", thrown.Message);
        Assert.Contains(Corpus.MethodSpecMarker, thrown.Message);
        Assert.Contains(Corpus.MethodSpecMarker, open.Message);
        Assert.Contains("Int32", thrown.Message);
        Assert.DoesNotContain("Int32", open.Message);
        Assert.Contains("Generic<T> (open generic method definition, not yet constructed)", open.Message);
        Assert.NotEqual(thrown.Message, open.Message);
        Assert.Empty(minter.Calls);
    }

    [Fact]
    public void ANonGenericMethodOnAGenericTypeStillReachesTheMint() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();
        MethodInfo onConstructedType = typeof(List<int>).GetMethod("Add")!;

        source.TokenFor(onConstructedType);

        Assert.Same(onConstructedType, Assert.Single(minter.Calls).Operand);
    }

    [Fact]
    public void AConstructedGenericTypeReachesTheMintStillConstructed() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();

        source.TokenFor(typeof(List<int>));

        Assert.Same(typeof(List<int>), minter.Calls[0].Operand);
    }

    [Fact]
    public void ArraysAndByrefsReachTheMintUnchanged() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();
        Type byref = typeof(int).MakeByRefType();

        source.TokenFor(typeof(int[]));
        source.TokenFor(byref);

        Assert.Same(typeof(int[]), minter.Calls[0].Operand);
        Assert.Same(byref, minter.Calls[1].Operand);
    }

    [Fact]
    public void AnUnreachableMemberThrowsBeforeAnyMintHappens() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();
        WaylithTokenSource outward = new WaylithTokenSource(typeof(object).Module, minter);
        MethodBase shut = typeof(Shut).GetMethod("Peek")!;

        TokenAccessException thrown = Assert.Throws<TokenAccessException>(() => outward.TokenFor(shut));

        Assert.Contains("Concord.Host.Adapter.Tests.Shut", thrown.Message);
        Assert.Empty(minter.Calls);
        Assert.Equal(0x0a000001, source.TokenFor(shut));
    }

    [Fact]
    public void AnUnreachableTypeAndFieldAlsoThrowBeforeMinting() {
        RecordingMinter minter = new RecordingMinter();
        WaylithTokenSource outward = new WaylithTokenSource(typeof(object).Module, minter);

        Assert.Throws<TokenAccessException>(() => outward.TokenFor(typeof(Shut)));
        Assert.Throws<TokenAccessException>(() => outward.TokenFor(typeof(Shut).GetField("Slot")!));
        Assert.Empty(minter.Calls);
    }

    [Fact]
    public void ALiteralIsNeverAccessibilityChecked() {
        RecordingMinter minter = new RecordingMinter();
        WaylithTokenSource outward = new WaylithTokenSource(typeof(object).Module, minter);

        outward.TokenFor("anything");

        Assert.Equal("literal", minter.Calls[0].Kind);
    }

    [Fact]
    public void NullOperandsAndNullConstructorArgumentsAreRejected() {
        (WaylithTokenSource source, RecordingMinter minter) = Bound();

        Assert.Throws<ArgumentNullException>(() => source.TokenFor((Type)null!));
        Assert.Throws<ArgumentNullException>(() => source.TokenFor((FieldInfo)null!));
        Assert.Throws<ArgumentNullException>(() => source.TokenFor((MethodBase)null!));
        Assert.Throws<ArgumentNullException>(() => source.TokenFor((string)null!));
        Assert.Throws<ArgumentNullException>(() => source.TokenForLocals(null!));
        Assert.Throws<ArgumentNullException>(() => new WaylithTokenSource(null!, minter));
        Assert.Throws<ArgumentNullException>(() => new WaylithTokenSource(Here, null!));
        Assert.Empty(minter.Calls);
    }

    [Fact]
    public void TheForkMinterReportsWhetherTheRuntimeHasTheApiAndSaysSoWhenItDoesNot() {
        bool present = Type.GetType("WaylithClr.MethodBodyUpdater, System.Private.CoreLib", throwOnError: false) is not null;

        Assert.Equal(present, ForkMinter.IsAvailable);

        if (present) {
            return;
        }

        PlatformNotSupportedException thrown = Assert.Throws<PlatformNotSupportedException>(
            () => new ForkMinter().Token(Here, typeof(int)));

        Assert.Contains("WaylithClr.MethodBodyUpdater", thrown.Message);
        Assert.Contains("GetTokenFor(Module, Type)", thrown.Message);
        Assert.Contains("detour backend", thrown.Message);

        PlatformNotSupportedException onAMethod = Assert.Throws<PlatformNotSupportedException>(
            () => new ForkMinter().Token(Here, typeof(object).GetMethod(nameof(ToString))!));

        Assert.Contains("GetTokenFor(Module, MethodBase)", onAMethod.Message);
    }
}

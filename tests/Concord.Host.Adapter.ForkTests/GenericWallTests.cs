using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using dnlib.DotNet;
using dnlib.DotNet.MD;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class GenericWallTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so the generic walls cannot be probed.";

    private static readonly MethodInfo IdentityDefinition = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.Identity))!;

    private static readonly MethodInfo WrapperDefinition = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.Wrapper))!;

    private readonly ITestOutputHelper output;

    private readonly List<MethodBase> written = [];

    private readonly List<string> log = [];

    public GenericWallTests(ITestOutputHelper output) {
        this.output = output;
    }

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall1ControlANonGenericOperandThroughTheSamePathRuns() {
        MethodInfo plain = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.Plain))!;
        MethodInfo host = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.CallsPlain))!;
        int token = new ForkMinter().Token(typeof(GenericWallHosts).Module, plain);
        Note($"token for Plain: 0x{token:X8} (table 0x{token >> 24:X2})");
        Assert.Equal(-1, GenericWallHosts.CallsPlain());
        Publish(host, Calling(plain, 7));
        string outcome = Attempt(() => GenericWallHosts.CallsPlain().ToString());
        Note($"CallsPlain() after publishing 'ldc.i4 7; call Plain; ret': {outcome}");

        Assert.True(outcome == "7", Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall1ConstructedGenericMethodOperandCompilesAndRuns() {
        Module module = typeof(GenericWallHosts).Module;
        MethodInfo constructed = IdentityDefinition.MakeGenericMethod(typeof(int));
        int token = new ForkMinter().Token(module, constructed);
        Note($"token for Identity<int>: 0x{token:X8} (table 0x{token >> 24:X2})");
        Note($"signature blob: {Attempt(() => Hex(ReadSignature(module, token)))}");
        Note($"ResolveMethod(token): {Attempt(() => module.ResolveMethod(token)?.ToString())}");
        Note($"same reader on the compiler's own MethodSpec row 1: {Attempt(() => CompilerMethodSpec(module))}");

        MethodInfo host = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.CallsConstructed))!;
        Assert.Equal(-1, GenericWallHosts.CallsConstructed());
        string publish = Attempt(() => {
            Publish(host, Calling(constructed, 7));
            return "accepted";
        });
        Note($"ReplaceMethodBody over CallsConstructed with 'ldc.i4 7; call Identity<int>; ret': {publish}");
        string outcome = Attempt(() => GenericWallHosts.CallsConstructed().ToString());
        Note($"CallsConstructed() after that publish: {outcome}");
        ForkRuntime.Restore(host);
        written.Clear();
        string reverted = Attempt(() => GenericWallHosts.CallsConstructed().ToString());
        Note($"CallsConstructed() after Revert: {reverted}");

        Assert.True(
            token >> 24 == 0x2B
            && publish == "accepted"
            && outcome == "7"
            && reverted == "-1",
            Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall1OpenDefinitionGetsAMethodDefAndAnMvarSpecRunsPerInstantiation() {
        Module module = typeof(GenericWallHosts).Module;
        int definitionToken = new ForkMinter().Token(module, IdentityDefinition);
        Note($"token for Identity<T> definition: 0x{definitionToken:X8} (table 0x{definitionToken >> 24:X2})");
        Note($"definition signature blob: {Attempt(() => Hex(ReadSignature(module, definitionToken)))}");

        MethodInfo host = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.CallsDefinition))!;
        Publish(host, Calling(IdentityDefinition, 7));
        Note($"CallsDefinition() after publishing a call on the bare definition token: {Attempt(() => GenericWallHosts.CallsDefinition().ToString())}");

        Type ownParameter = WrapperDefinition.GetGenericArguments()[0];
        MethodInfo overOwnParameter = IdentityDefinition.MakeGenericMethod(ownParameter);
        int mvarToken = new ForkMinter().Token(module, overOwnParameter);
        Note($"token for Identity<!!T of Wrapper>: 0x{mvarToken:X8} (table 0x{mvarToken >> 24:X2})");
        Note($"mvar signature blob: {Attempt(() => Hex(ReadSignature(module, mvarToken)))}");
        Note($"ResolveMethod(mvar token, Wrapper's T): {Attempt(() => module.ResolveMethod(mvarToken, null, [ownParameter])?.ToString())}");

        Assert.Equal(103, GenericWallHosts.Wrapper<int>(3));
        Publish(WrapperDefinition, Forwarding(overOwnParameter));
        string viaInt = Attempt(() => GenericWallHosts.Wrapper<int>(3).ToString());
        string viaString = Attempt(() => GenericWallHosts.Wrapper<string>(3).ToString());
        Note($"Wrapper<int>(3) after publishing 'ldarg.0; call Identity<!!T>; ret' over the definition: {viaInt}");
        Note($"Wrapper<string>(3) after the same publish: {viaString}");

        Assert.True(
            definitionToken >> 24 == 0x06
            && mvarToken >> 24 == 0x2B
            && viaInt == "3"
            && viaString == "3",
            Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall1AGenericMethodFromAnotherAssemblyRuns() {
        Module module = typeof(GenericWallHosts).Module;
        MethodInfo empty = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(int));
        int token = new ForkMinter().Token(module, empty);
        Note($"token for Array.Empty<int>: 0x{token:X8} (table 0x{token >> 24:X2})");

        MethodInfo host = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.CallsForeignConstructed))!;
        Assert.Equal(-1, GenericWallHosts.CallsForeignConstructed());
        Publish(host, Measuring(empty));
        string outcome = Attempt(() => GenericWallHosts.CallsForeignConstructed().ToString());
        Note($"CallsForeignConstructed() after publishing 'call Array.Empty<int>; ldlen; conv.i4; ret': {outcome}");

        Assert.True(token >> 24 == 0x2B && outcome == "0", Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall2ReplacingAClosedValueTypeInstantiationReachesEverySibling() {
        MethodInfo target = typeof(Box<int>).GetMethod(nameof(Box<int>.Bar))!;
        Note($"before: {BoxReadings()}");

        Publish(target, ForkRuntime.Returning(5));
        string after = BoxReadings();
        Note($"after publishing 'ldc.i4 5; ret' over Box<int>.Bar: {after}");
        string fresh = Attempt(() => Box<Guid>.Bar().ToString());
        Note($"Box<Guid>.Bar() first ever call, after the publish: {fresh}");

        ForkRuntime.Restore(target);
        written.Clear();
        string reverted = $"{BoxReadings()} Box<Guid>={Attempt(() => Box<Guid>.Bar().ToString())}";
        Note($"after revert: {reverted}");

        Assert.True(after == "Box<int>=5 Box<string>=5 Box<object>=5 Box<Pt>=5" && fresh == "5" && reverted == "Box<int>=1 Box<string>=1 Box<object>=1 Box<Pt>=1 Box<Guid>=1", Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall2ReplacingAClosedReferenceTypeInstantiationReachesEverySibling() {
        MethodInfo target = typeof(Crate<string>).GetMethod(nameof(Crate<string>.Bar))!;
        Note($"before: {CrateReadings()}");

        Publish(target, ForkRuntime.Returning(5));
        string after = CrateReadings();
        Note($"after publishing 'ldc.i4 5; ret' over Crate<string>.Bar: {after}");
        string fresh = Attempt(() => Crate<Uri>.Bar().ToString());
        Note($"Crate<Uri>.Bar() first ever call, after the publish: {fresh}");

        ForkRuntime.Restore(target);
        written.Clear();
        string reverted = $"{CrateReadings()} Crate<Uri>={Attempt(() => Crate<Uri>.Bar().ToString())}";
        Note($"after revert: {reverted}");

        Assert.True(after == "Crate<string>=5 Crate<object>=5 Crate<int>=5 Crate<Pt>=5" && fresh == "5" && reverted == "Crate<string>=1 Crate<object>=1 Crate<int>=1 Crate<Pt>=1 Crate<Uri>=1", Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall3PublishingOverAnOpenGenericTypeMethodIsAcceptedAndReachesInstantiations() {
        MethodInfo open = typeof(Open<>).GetMethod(nameof(Open<int>.Bar))!;
        Note($"target: {open.DeclaringType} ContainsGenericParameters={open.ContainsGenericParameters}");
        Note($"before: Open<int>={Attempt(() => Open<int>.Bar().ToString())} Open<string>={Attempt(() => Open<string>.Bar().ToString())}");

        string publish = Attempt(() => {
            Publish(open, ForkRuntime.Returning(5));
            return "accepted";
        });
        Note($"publish over Open<>.Bar: {publish}");
        string after = $"Open<int>={Attempt(() => Open<int>.Bar().ToString())} Open<string>={Attempt(() => Open<string>.Bar().ToString())} Open<Pt>(fresh)={Attempt(() => Open<Pt>.Bar().ToString())}";
        Note($"after: {after}");

        string revert = Attempt(() => {
            ForkRuntime.Restore(open);
            written.Clear();
            return "accepted";
        });
        Note($"revert on Open<>.Bar: {revert}");
        Note($"after revert: Open<int>={Attempt(() => Open<int>.Bar().ToString())} Open<string>={Attempt(() => Open<string>.Bar().ToString())} Open<Pt>={Attempt(() => Open<Pt>.Bar().ToString())}");

        Assert.True(publish == "accepted" && after == "Open<int>=5 Open<string>=5 Open<Pt>(fresh)=5", Report());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void Wall3PublishingOverAnOpenGenericMethodDefinitionIsAcceptedAndReachesInstantiations() {
        MethodInfo open = typeof(GenericWallHosts).GetMethod(nameof(GenericWallHosts.OpenMethod))!;
        Note($"target: {open} IsGenericMethodDefinition={open.IsGenericMethodDefinition}");
        Note($"before: OpenMethod<int>={Attempt(() => GenericWallHosts.OpenMethod<int>().ToString())} OpenMethod<string>={Attempt(() => GenericWallHosts.OpenMethod<string>().ToString())}");

        string publish = Attempt(() => {
            Publish(open, ForkRuntime.Returning(5));
            return "accepted";
        });
        Note($"publish over OpenMethod<T>: {publish}");
        string after = $"OpenMethod<int>={Attempt(() => GenericWallHosts.OpenMethod<int>().ToString())} OpenMethod<string>={Attempt(() => GenericWallHosts.OpenMethod<string>().ToString())} OpenMethod<Pt>(fresh)={Attempt(() => GenericWallHosts.OpenMethod<Pt>().ToString())}";
        Note($"after: {after}");

        string revert = Attempt(() => {
            ForkRuntime.Restore(open);
            written.Clear();
            return "accepted";
        });
        Note($"revert on OpenMethod<T>: {revert}");
        Note($"after revert: OpenMethod<int>={Attempt(() => GenericWallHosts.OpenMethod<int>().ToString())} OpenMethod<string>={Attempt(() => GenericWallHosts.OpenMethod<string>().ToString())} OpenMethod<Pt>={Attempt(() => GenericWallHosts.OpenMethod<Pt>().ToString())}");

        Assert.True(publish == "accepted" && after == "OpenMethod<int>=5 OpenMethod<string>=5 OpenMethod<Pt>(fresh)=5", Report());
    }

    private static string BoxReadings() {
        return $"Box<int>={Attempt(() => Box<int>.Bar().ToString())} Box<string>={Attempt(() => Box<string>.Bar().ToString())} Box<object>={Attempt(() => Box<object>.Bar().ToString())} Box<Pt>={Attempt(() => Box<Pt>.Bar().ToString())}";
    }

    private static string CrateReadings() {
        return $"Crate<string>={Attempt(() => Crate<string>.Bar().ToString())} Crate<object>={Attempt(() => Crate<object>.Bar().ToString())} Crate<int>={Attempt(() => Crate<int>.Bar().ToString())} Crate<Pt>={Attempt(() => Crate<Pt>.Bar().ToString())}";
    }

    private static byte[] Calling(MethodInfo callee, int argument) {
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldc_I4, argument), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Call, callee), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new RawTokens(typeof(GenericWallHosts).Module), new ComputedStack()).Bytes;
    }

    private static byte[] Measuring(MethodInfo callee) {
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Call, callee), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldlen), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Conv_I4), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new RawTokens(typeof(GenericWallHosts).Module), new ComputedStack()).Bytes;
    }

    private static byte[] Forwarding(MethodInfo callee) {
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldarg_0), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Call, callee), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new RawTokens(typeof(GenericWallHosts).Module), new ComputedStack()).Bytes;
    }

    private static byte[] ReadSignature(Module module, int token) {
        PropertyInfo import = module.GetType().GetProperty("MetadataImport", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RuntimeModule has no MetadataImport property on this runtime.");
        object metadata = import.GetValue(module)!;
        string reader = (token >> 24) switch {
            0x06 => "GetSigOfMethodDef",
            0x0A => "GetMemberRefProps",
            _ => "GetSignatureFromToken",
        };
        MethodInfo read = metadata.GetType().GetMethod(reader, [typeof(int)])
            ?? throw new InvalidOperationException($"MetadataImport has no {reader}(int) on this runtime.");
        object blob = read.Invoke(metadata, [token])!;
        int length = (int)blob.GetType().GetField("m_length", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(blob)!;
        nint pointer = (nint)blob.GetType().GetField("m_constArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(blob)!;
        byte[] bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return bytes;
    }

    private static string CompilerMethodSpec(Module module) {
        using ModuleDefMD file = ModuleDefMD.Load(module.Assembly.Location);
        if (file.TablesStream.MethodSpecTable.Rows == 0) {
            return "the file has no MethodSpec rows";
        }

        file.TablesStream.TryReadMethodSpecRow(1, out RawMethodSpecRow row);
        byte[] expected = file.BlobStream.Read(row.Instantiation)!;
        const int token = 0x2B000001;
        return $"file bytes {Hex(expected)}; runtime reader {Attempt(() => Hex(ReadSignature(module, token)))}; ResolveMethod {Attempt(() => module.ResolveMethod(token)?.ToString())}";
    }

    private static string Hex(byte[] bytes) {
        return bytes.Length == 0 ? "(empty)" : string.Join(" ", bytes.Select(value => value.ToString("X2")));
    }

    private static string Attempt(Func<string?> probe) {
        try {
            return probe() ?? "null";
        } catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null) {
            return Threw(wrapped.InnerException);
        } catch (Exception failure) {
            return Threw(failure);
        }
    }

    private static string Threw(Exception failure) {
        return $"threw {failure.GetType().Name}: {failure.Message}";
    }

    private void Publish(MethodBase target, byte[] body) {
        ForkBodyWriter.Shared.Write(target, body, []);
        written.Add(target);
    }

    private void Note(string line) {
        log.Add(line);
        output.WriteLine(line);
    }

    private string Report() {
        return "\n  " + string.Join("\n  ", log);
    }

    private sealed class RawTokens : ITokenSource {
        private readonly Module module;

        private readonly ForkMinter minter = new ForkMinter();

        public RawTokens(Module module) {
            this.module = module;
        }

        public int TokenFor(MethodBase method) => minter.Token(module, method);

        public int TokenFor(FieldInfo field) => minter.Token(module, field);

        public int TokenFor(Type type) => minter.Token(module, type);

        public int TokenFor(string literal) => minter.Token(module, literal);

        public int TokenForLocals(byte[] signature) => minter.LocalsToken(module, signature);
    }
}

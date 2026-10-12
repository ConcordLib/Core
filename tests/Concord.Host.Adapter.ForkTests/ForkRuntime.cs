using System.Reflection;
using System.Reflection.Emit;

namespace Concord.Host.Adapter.ForkTests;

public enum ForkGap {
    None,
    Runtime,
    Writer,
}

public static class ForkRuntime {
    public const string Runtime = "10.0.10-waylithclr.dev";

    private static readonly Type? Updater = RuntimeCapability.Updater();

    public static ForkGap Gap =>
        !RuntimeCapability.IsAvailable ? ForkGap.Runtime
        : !ForkBodyWriter.Shared.IsAvailable ? ForkGap.Writer
        : ForkGap.None;

    public static bool Unavailable => Gap != ForkGap.None;

    public static string Explain => Gap switch {
        ForkGap.Writer =>
            $"'{RuntimeCapability.UpdaterTypeName}' reports {nameof(RuntimeCapability.IsAvailable)} on '{RuntimeCapability.Runtime}' but 'Concord.Host.Adapter.ForkBodyWriter' could not bind ReplaceMethodBody/Revert, so no fork body can be written here",
        _ => RuntimeCapability.Explain(),
    };

    public static byte[] Returning(int value) {
        return Returning(value, 1);
    }

    public static byte[] Returning(int value, int maxStack) {
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldc_I4, value), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new NoTokens(), new FixedStack(maxStack)).Bytes;
    }

    public static byte[] Summing(int terms, int maxStack) {
        List<ComposedInstruction> stream = [];
        for (int i = 0; i < terms; i++) {
            stream.Add(new ComposedInstruction(new CodeInstruction(OpCodes.Ldc_I4_1), null, null));
        }

        for (int i = 1; i < terms; i++) {
            stream.Add(new ComposedInstruction(new CodeInstruction(OpCodes.Add), null, null));
        }

        stream.Add(new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null));
        ComposedBody body = new ComposedBody(stream, [], [], false);
        return BodyEncoder.Encode(body, new NoTokens(), new FixedStack(maxStack)).Bytes;
    }

    public static void WriteSum(MethodBase target, int terms, int maxStack) {
        ForkBodyWriter.Shared.Write(target, Summing(terms, maxStack), []);
    }

    public static void Write(MethodBase target, int value) {
        ForkBodyWriter.Shared.Write(target, Returning(value), []);
    }

    public static void Write(MethodBase target, int value, int maxStack) {
        ForkBodyWriter.Shared.Write(target, Returning(value, maxStack), []);
    }

    public static void Restore(MethodBase target) {
        ForkBodyWriter.Shared.Restore(target);
    }

    public static int MetadataUpdateVersion() {
        return (int)Member("MetadataUpdateVersion").GetValue(null)!;
    }

    public static int[] UpdatedMethodTokens(Module module) {
        MethodInfo tokens = Updater!.GetMethod(
            "GetUpdatedMethodTokens",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Module)])!;

        return (int[])tokens.Invoke(null, [module])!;
    }

    private static PropertyInfo Member(string name) {
        return Updater!.GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"'{RuntimeCapability.UpdaterTypeName}' has no static '{name}', so this test cannot read it on '{RuntimeCapability.Runtime}'.");
    }

    private sealed class NoTokens : ITokenSource {
        public int TokenFor(MethodBase method) => throw Refused(method);

        public int TokenFor(FieldInfo field) => throw Refused(field);

        public int TokenFor(Type type) => throw Refused(type);

        public int TokenFor(string literal) => throw Refused(literal);

        public int TokenForLocals(byte[] signature) => throw Refused(signature);

        private static InvalidOperationException Refused(object subject) {
            return new InvalidOperationException($"A body of 'ldc.i4 n; ret' asked for a token for '{subject}', so the encoder is minting something this test did not write.");
        }
    }

    internal sealed class FixedStack : IMaxStackSource {
        private readonly int depth;

        public FixedStack(int depth) {
            this.depth = depth;
        }

        public int MaxStackFor(ComposedBody body) => depth;
    }
}

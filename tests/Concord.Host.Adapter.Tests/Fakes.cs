using System.Reflection;

namespace Concord.Host.Adapter.Tests;

internal sealed class Tokens : ITokenSource {
    internal const int LocalsToken = 0x11000001;

    private readonly Dictionary<object, int> assigned = new Dictionary<object, int>();

    public byte[]? Locals { get; private set; }

    public int TokenFor(Type type) {
        return Assign(type, 0x01000000);
    }

    public int TokenFor(FieldInfo field) {
        return Assign(field, 0x04000000);
    }

    public int TokenFor(MethodBase method) {
        return Assign(method, 0x0a000000);
    }

    public int TokenFor(string literal) {
        return Assign(literal, 0x70000000);
    }

    public int TokenForLocals(byte[] signature) {
        Locals = signature;
        return LocalsToken;
    }

    private int Assign(object key, int table) {
        if (!assigned.TryGetValue(key, out int token)) {
            token = table | (assigned.Count + 1);
            assigned.Add(key, token);
        }

        return token;
    }
}

internal sealed class FixedStack : IMaxStackSource {
    private readonly int value;

    internal FixedStack(int value) {
        this.value = value;
    }

    internal ComposedBody? Asked { get; private set; }

    public int MaxStackFor(ComposedBody body) {
        Asked = body;
        return value;
    }
}

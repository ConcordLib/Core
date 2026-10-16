using Concord;

namespace Concord.Harmony;

internal delegate bool InfixPrefix(object? self, object? outer, object?[] args, ref object? result);

internal delegate object? InfixPostfix(object? self, object? outer, object?[] args, object? result);

internal sealed class Infix {
    private static readonly InfixPrefix PassThrough = Pass;

    private static readonly InfixPostfix KeepResult = Keep;

    internal InfixPrefix Prefix { get; set; } = PassThrough;

    internal InfixPostfix Postfix { get; set; } = KeepResult;

    private static bool Pass(object? self, object? outer, object?[] args, ref object? result) {
        return true;
    }

    private static object? Keep(object? self, object? outer, object?[] args, object? result) {
        return result;
    }
}

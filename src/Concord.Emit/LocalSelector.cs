namespace Concord.Emit;

internal readonly record struct LocalSelector(
    Type Wanted,
    uint Ordinal,
    int Index,
    string? Name);

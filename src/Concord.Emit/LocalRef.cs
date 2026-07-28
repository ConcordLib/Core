namespace Concord;

/// <summary>
///     An opaque local-variable handle inside a transpiled method body. Declare new locals via
///     the transpiler context.
/// </summary>
public readonly struct LocalRef : IEquatable<LocalRef> {
    internal LocalRef(int index, Type type) {
        Index = index;
        Type = type;
    }

    /// <summary>The declared type of the local.</summary>
    public Type Type { get; }

    internal int Index { get; }

    /// <summary>Compares two local references for equality.</summary>
    /// <param name="left">The first local reference.</param>
    /// <param name="right">The second local reference.</param>
    /// <returns><see langword="true" /> when both refer to the same local slot.</returns>
    public static bool operator ==(LocalRef left, LocalRef right) {
        return left.Equals(right);
    }

    /// <summary>Compares two local references for inequality.</summary>
    /// <param name="left">The first local reference.</param>
    /// <param name="right">The second local reference.</param>
    /// <returns><see langword="true" /> when they refer to different local slots.</returns>
    public static bool operator !=(LocalRef left, LocalRef right) {
        return !left.Equals(right);
    }

    /// <summary>Compares two local references for equality.</summary>
    /// <param name="other">The local reference to compare against.</param>
    /// <returns><see langword="true" /> when both refer to the same local slot.</returns>
    public bool Equals(LocalRef other) {
        return Index == other.Index;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        return obj is LocalRef other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return Index;
    }

    /// <inheritdoc />
    public override string ToString() {
        return "V_" + Index.ToString("X4");
    }
}

internal static class LocalRefFactory {
    internal static LocalRef Create(int index, Type type) {
        return new LocalRef(index, type);
    }
}

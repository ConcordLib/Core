namespace Concord;

/// <summary>
///     An opaque branch-target handle inside a transpiled method body. Mint new labels via
///     the transpiler context.
/// </summary>
public readonly struct Label : IEquatable<Label> {
    internal Label(int id) {
        Id = id;
    }

    internal int Id { get; }

    /// <summary>Compares two labels for equality.</summary>
    /// <param name="left">The first label.</param>
    /// <param name="right">The second label.</param>
    /// <returns><see langword="true" /> when both labels refer to the same target.</returns>
    public static bool operator ==(Label left, Label right) {
        return left.Equals(right);
    }

    /// <summary>Compares two labels for inequality.</summary>
    /// <param name="left">The first label.</param>
    /// <param name="right">The second label.</param>
    /// <returns><see langword="true" /> when the labels refer to different targets.</returns>
    public static bool operator !=(Label left, Label right) {
        return !left.Equals(right);
    }

    /// <summary>Compares two labels for equality.</summary>
    /// <param name="other">The label to compare against.</param>
    /// <returns><see langword="true" /> when both labels refer to the same target.</returns>
    public bool Equals(Label other) {
        return Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        return obj is Label other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return Id;
    }

    /// <inheritdoc />
    public override string ToString() {
        return "L_" + Id.ToString("X4");
    }
}

internal static class LabelFactory {
    internal static Label Create(int id) {
        return new Label(id);
    }
}

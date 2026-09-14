namespace Concord.AttachedData;

/// <summary>
///     Type-erased access to one attached-field slot, for hosts that work in <see cref="object" />
///     (save/load adapters, inspectors). The typed path is <see cref="AttachedField{TTarget,TVal}" />.
/// </summary>
public interface IAttachedSlot {
    /// <summary>Gets the value attached to <paramref name="target" />, boxed.</summary>
    /// <param name="target">The instance to read from.</param>
    /// <returns>The attached value, or the default of the slot's value type when absent.</returns>
    object? Get(object target);

    /// <summary>Sets the value attached to <paramref name="target" />, unboxing it into the slot's value type.</summary>
    /// <param name="target">The instance to attach to.</param>
    /// <param name="value">The value to store.</param>
    void Set(object target, object? value);
}

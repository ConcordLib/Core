namespace Concord;

/// <summary>
///     Reads and writes one of the target method's local variables from an injection.
/// </summary>
/// <typeparam name="T">The local's type.</typeparam>
/// <remarks>
///     Concord lowers property access on this type into wrapper locals while composing IL. Runtime
///     instances are not allocated on the patched method hot path, so every use must be a direct
///     property access on the parameter. Storing the handle, passing it on, capturing it in a
///     lambda, or returning it all break the lowering and are rejected.
/// </remarks>
public sealed class LocalHandle<T> {
    /// <summary>
    ///     The local's current value.
    /// </summary>
    public T Value { get; set; } = default!;
}

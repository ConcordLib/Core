using System.Runtime.CompilerServices;

namespace Concord.AttachedData;

/// <summary>
///     A weak-keyed side-table that attaches a strongly-typed value to instances of a reference type
///     without modifying the target type. Backed by a <see cref="ConditionalWeakTable{TKey,TValue}" />,
///     so an entry is collected when its target is collected: no leak on instance death.
/// </summary>
/// <typeparam name="TTarget">The reference type to attach values to.</typeparam>
/// <typeparam name="TVal">The attached value type.</typeparam>
public sealed class AttachedField<TTarget, TVal>
    where TTarget : class {
    private readonly ConditionalWeakTable<TTarget, StrongBox<TVal>> _table = new ConditionalWeakTable<TTarget, StrongBox<TVal>>();

    /// <summary>
    ///     Gets the value attached to <paramref name="target" />, or <c>default(TVal)</c> when none is set.
    /// </summary>
    /// <param name="target">The instance to read from.</param>
    /// <returns>The attached value, or the default of <typeparamref name="TVal" /> when absent.</returns>
    public TVal Get(TTarget target) {
        if (_table.TryGetValue(target, out StrongBox<TVal>? box)) {
            return box.Value!;
        }

        return default!;
    }

    /// <summary>
    ///     Sets the value attached to <paramref name="target" />, replacing any existing value.
    /// </summary>
    /// <param name="target">The instance to attach to.</param>
    /// <param name="value">The value to store.</param>
    public void Set(TTarget target, TVal value) {
        if (_table.TryGetValue(target, out StrongBox<TVal>? box)) {
            box.Value = value;
            return;
        }

        _table.Add(target, new StrongBox<TVal>(value));
    }

    /// <summary>
    ///     Attempts to read the value attached to <paramref name="target" />.
    /// </summary>
    /// <param name="target">The instance to read from.</param>
    /// <param name="value">The attached value when present; otherwise <c>default(TVal)</c>.</param>
    /// <returns><c>true</c> when a value is attached; otherwise <c>false</c>.</returns>
    public bool TryGet(TTarget target, out TVal value) {
        if (_table.TryGetValue(target, out StrongBox<TVal>? box)) {
            value = box.Value!;
            return true;
        }

        value = default!;
        return false;
    }
}

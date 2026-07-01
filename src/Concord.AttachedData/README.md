# Concord.AttachedData

Weak-reference side storage, one of the core libraries internal to Concord.

`Concord.AttachedData` lets a mod hang custom data on target instances without adding real fields to their types. It backs the attached-data feature.

## What's here

- `AttachedField<TTarget, TValue>` is a weak-reference table keyed by target instance. Reads and writes are O(1) amortized, and an entry is collected automatically when its target is.

The data lives in a side table rather than on the object, so target types are never modified. The trade-off is boxing for value types and undefined enumeration order; see [Attached Data](../../docs/attached-data.md) for the usage pattern and the determinism caveat.

## When to use this

You don't reference it directly. It compiles separately but merges into the Concord Assembly (`Concord.dll`). Mods reach attached data through `[Patch]` declarations, not this type directly. Work here if you are developing the storage layer.

See [Packages](../../docs/packages.md) for how this fits with the other projects.

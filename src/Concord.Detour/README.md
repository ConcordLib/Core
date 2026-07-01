# Concord.Detour

The detour backend, one of the core libraries internal to Concord.

`Concord.Detour` applies a composed wrapper over a live target method and takes it back down again. Where `Concord.Emit` builds the wrapper, this layer is what redirects calls to it.

## What's here

- `IDetourBackend` is the interface Concord uses to apply a wrapper as a method's runtime entry point.
- `MonoModDetourBackend` is the default backend, built on MonoMod.Core.
- `IDetourHandle.Dispose()` undoes and disposes the detour, which is the foundation for real unpatching.

## When to use this

You don't reference it directly. It compiles separately but merges into the Concord Assembly (`Concord.dll`). Work here if you are developing how Concord applies and removes detours, or adding an alternative backend.

Depends on MonoMod.Core.

See [Packages](../../docs/packages.md) for how this fits with the other projects.

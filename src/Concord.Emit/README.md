# Concord.Emit

The IL composition layer, one of the core libraries internal to Concord.

`Concord.Emit` builds the wrapper method that holds a target's original body plus every patch that applies to it. It does the IL work behind the patch model: copying the original body, splicing injection bodies in at the head, tail, return sites, around the whole body, or around a specific call, and lowering `ControlHandle`/`ControlHandle<T>` and `Operation<T>` calls into wrapper locals.

## What's here

- `WrapperComposer` composes the wrapper from a target and an ordered list of injections.
- `BodyCopier` owns the IL-copying mechanics: cloning instructions and handlers, remapping arguments, mapping shadow fields to real target fields, and splicing.
- `At` is the public injection-position enum. `InjectAt` is the low-level representation used by composition.
- `InjectAttribute` is the `[Inject]` attribute mods write.
- Invalid patch shapes fail with a `ConcordEmitException` carrying a stable `CONCxxx` diagnostic code.

## When to use this

You don't reference it directly. It compiles separately but merges into the Concord Assembly (`Concord.dll`), so mods and runtime adapters get it through that assembly. Work here if you are developing Concord's IL composition.

Depends on MonoMod.Utils for Cecil.

See [Packages](../../docs/packages.md) for how this fits with the other projects, and [Contributor Architecture](../../docs/architecture.md) for the composition flow.

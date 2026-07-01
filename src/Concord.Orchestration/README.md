# Concord.Orchestration

The author-facing API and the patch scanner, one of the core libraries internal to Concord.

`Concord.Orchestration` is the surface a mod calls. It applies and undoes patches, offers a fluent builder, and scans assemblies for `[Patch]` declarations.

## What's here

- `Patcher.Apply(assembly)` applies every `[Patch]` declaration in an assembly and returns an `IPatchHandle` you can dispose to take them down.
- `PatchBuilder` is the fluent builder returned by `Patcher.For(...)` (`.Head`, `.Tail`, `.Return`, `.Around`, `.Invoke`, `.Apply`).
- `PatchDeclarationScanner` reads a `[Patch]` subclass and forwards its `[Inject]` methods for patch application and its plain declared fields to attached-property registration, through `IPatchApplier` and `IAttachedPropertyRegistry`.

## When to use this

You don't reference it directly. It compiles separately but merges into the Concord Assembly (`Concord.dll`), and mods reach `Patcher` through that assembly (or, at compile time, through `Concord.Ref`). Work here if you are developing the apply/undo API or the declaration scanner.

Depends on Concord.Emit and Concord.Detour.

See [Packages](../../docs/packages.md) for how this fits with the other projects.

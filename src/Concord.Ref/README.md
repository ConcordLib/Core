# Concord.Ref

**Compile-time reference assembly for [Concord](https://concordlib.dev).**

`Concord.Ref` is a metadata-only package: it exposes Concord's public API so a mod
can be *authored and compiled* against it, but it ships **no runtime implementation**.
The target runtime supplies the Concord Assembly (`Concord.dll`) at runtime.

## When to use this

Reference `Concord.Ref` when you are **authoring a mod** for a target runtime that
already ships Concord as its patching runtime. You get the API surface (attributes,
`ControlHandle<T>`, `Patcher`, `AttachedField<,>`, and friends) for IntelliSense and
compilation, without taking a hard runtime dependency on the patching engine.

Don't reference `Concord.Ref` if you are the **runtime adapter** integrating Concord into a target runtime;
use the Concord Assembly directly.

## How it works

The package contains only a `ref/net10.0/Concord.dll` reference assembly. Every method
body is a metadata stub (`throw null`), and the assembly is stamped with
`ReferenceAssemblyAttribute`, so the runtime refuses to load it. NuGet resolves the
`ref/` folder for compilation and contributes nothing at runtime, so the mod never
ships a duplicate `Concord.dll`; the loaded Concord Assembly satisfies binding.

Because the reference assembly carries the same assembly identity (`Concord`) as the
Concord Assembly, a mod compiled against the ref binds to the target runtime's `Concord.dll`
with no redirection.

## Add To A Project

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="1.0.0" />
  <PackageReference Include="Concord.Analyzers" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

`Concord.Analyzers` is optional but recommended. It is build-time tooling only; keep
`PrivateAssets="all"` so it does not flow into mod output or downstream packages.

## Notes

- Targets `net10.0`, matching the Concord runtime.
- The reference assembly's public surface is clean of MonoMod types; mods compile against
  Concord's API only.
- Pair with `Concord.Analyzers` for IDE/build feedback while authoring patches.

See [Packages](../../docs/packages.md) for how this fits with the other projects.

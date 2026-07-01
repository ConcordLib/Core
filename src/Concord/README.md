# Concord Assembly

The shipped artifact: one `Concord.dll`.

This project doesn't hold source. It ILRepack-merges the four core libraries (`Concord.Emit`, `Concord.Detour`, `Concord.AttachedData`, `Concord.Orchestration`) into a single `Concord.dll`, with their MonoMod dependencies folded in. Build output lands in `Assemblies/`.

## When to use this

This is the Concord Assembly. A target runtime loads it to provide patching, and a mod binds to it at runtime to run its patches. Everything `Concord.Ref` declares at compile time is implemented here.

- Integrating Concord into a target runtime: ship and load this `Concord.dll`.
- Writing a mod: don't ship this yourself. Compile against [`Concord.Ref`](../Concord.Ref/README.md); the target runtime's Concord Assembly runs your patches.

The single-file merge matters because each mod dll is loaded into an isolated context, so a multi-dll Concord would fail to resolve its own dependencies. Merging to one file avoids that.

See [Packages](../../docs/packages.md) for the full project map.

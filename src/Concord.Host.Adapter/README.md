# Concord.Host.Adapter

A detour backend that replaces a method's IL body in place.

Concord normally patches a method by installing a native detour: the target's entry point jumps to a
wrapper Concord generated. This backend composes the same wrapper, encodes it as an ECMA-335 method
body, and asks the runtime to replace the target's body. No wrapper method exists, and the target
stays itself.

## Requirements

The runtime must support managed method-body replacement. Ludeon ships a CoreCLR fork with RimWorld
1.7 that does. On any other runtime, `RuntimeCapability.IsAvailable` is false and the backend
declines to install, so Concord keeps its default detour backend. `RuntimeCapability.Explain()`
returns the reason.

The adapter has no compile-time reference to the runtime. It resolves the type by name and calls it
through reflection.

## Use

Call the entry point from your mod's constructor, before applying patches:

```csharp
Selection choice = AdapterEntryPoint.Install(log: null, setting: null, assemblyDirectories: null);
```

`Install` returns a `Selection` that says whether the backend took over and which input decided it.
Pass `setting: false` to decline in code.

The `CONCORD_WAYLITH_BACKEND` environment variable overrides that setting. It accepts `1`, `true`,
`on`, `yes` or `enabled`, and `0`, `false`, `off`, `no` or `disabled`. Unreadable text is ignored
with a warning.

## What it gives you

A replaced body keeps the target's identity, so a stack trace names the real method, reflection on
the target returns the target, and nothing in the process sees a trampoline. Harmony patches applied
after a body replacement still work, because the entry point was never redirected.

## Tests

`Concord.Host.Adapter.Tests` runs anywhere and is part of the solution.

`Concord.Host.Adapter.ForkTests` needs a RimWorld 1.7 install and is deliberately outside both
solution files. Run it by path:

```bash
dotnet build tests/Concord.Host.Adapter.ForkTests -p:ForkRuntimeDir=<install>
./tests/Concord.Host.Adapter.ForkTests/bin/Debug/net10.0/linux-x64/Concord.Host.Adapter.ForkTests
```

Without that install the build fails on purpose. Off-runtime the facts would report as skipped and
nothing would say so.

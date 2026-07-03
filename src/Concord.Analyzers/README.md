# Concord.Analyzers

Build-time Roslyn analyzers for Concord projects.

Add this package next to `Concord.Ref` when authoring mods so Concord-specific mistakes can surface in the compiler and IDE instead of at runtime.

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="1.0.0" />
  <PackageReference Include="Concord.Analyzers" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

Analyzer packages are compile-time tooling only. Do not ship them with a mod.

The analyzer package also suppresses C# field-use warnings for `[InjectField]`
declarations, such as `CS0649` for fields that appear unassigned to the compiler
but are supplied by Concord lowering.

For patch declarations whose target can be resolved statically, the analyzer also
checks `[InjectField]`, `[InjectProperty]`, and `[InjectMethod]` declarations
against the target type. It validates `[Inject]` target methods and constructors,
overload disambiguation, injection method parameters, `ControlHandle<T>` return types,
static target usage, duplicate injection declarations, and unsupported declaration
forms such as generic `[Inject]` methods or invalid `[InjectInstance]`
properties. It also warns when a plain patch field mirrors a target
field, since that usually means `[InjectField]` was intended. Injection methods
may return `Control` only at the head position; a `Control` return anywhere else
is reported as CONCORD015.

Resolvable targets include `[Patch(typeof(Target))]`, inherited patch targets,
and string targets such as `[Patch("Game.Target, GameAssembly")]` when that type
is present in the project's source or referenced target assemblies. Unresolvable
string targets produce a warning because Concord cannot validate those declarations
until runtime.

The analyzer also nudges patch declarations toward compiler-checked names when
that is possible: use `typeof(Target)` instead of a resolvable string patch
target, `nameof(Target.Member)` instead of a string member target, and an
inherited `[Patch]` declaration instead of `[Patch(typeof(Target))]` when the
target class or record can be extended.

Runtime adapters and bootstrap assemblies that can run before the Concord Assembly is loadable can opt into bootstrap-reference checks:

```xml
<PropertyGroup>
  <ConcordBootstrapAssembly>true</ConcordBootstrapAssembly>
  <ConcordBootstrapAdapterAssemblies>MyRuntimeAdapter</ConcordBootstrapAdapterAssemblies>
</PropertyGroup>

<ItemGroup>
  <CompilerVisibleProperty Include="ConcordBootstrapAssembly" />
  <CompilerVisibleProperty Include="ConcordBootstrapAdapterAssemblies" />
</ItemGroup>
```

`ConcordBootstrapAdapterAssemblies` is optional and accepts a semicolon-separated list.

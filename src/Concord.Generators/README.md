# Concord.Generators

Roslyn source generators and refactorings for [Concord](https://concordlib.dev) patch declarations.

- **Patch registry**: emits an assembly-level registry of `[Patch]` declarations so
  `Patcher.Apply(assembly)` skips the reflection scan.
- **Shadow members**: `[Shadow("member")]` on a partial patch declaration generates the matching
  `[InjectField]` / `[InjectProperty]` / `[InjectMethod]` declaration from target metadata.
- **Scaffolding refactorings**: "Concord: add injection", "Concord: add shadow member",
  "Concord: convert to patch declaration", "Concord: create patch for X()" in any Roslyn IDE.

Reference alongside `Concord.Ref` and `Concord.Analyzers`:

```xml
<PackageReference Include="Concord.Generators" Version="x.y.z" PrivateAssets="all" />
```

; Unshipped analyzer release.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CONCORD001 | Concord.Bootstrap | Error | Bootstrap assemblies must not hard-reference the Concord Assembly or runtime adapter.
CONCORD002 | Concord.Patches | Error | Injected member declarations must name a target member that exists on the patch target type.
CONCORD003 | Concord.Patches | Error | Injected member declarations must match the target member type, static-ness, return type, and signature.
CONCORD004 | Concord.Patches | Warning | String patch targets should resolve from source or project references.
CONCORD005 | Concord.Patches | Error | Injections must name an existing target method or constructor.
CONCORD006 | Concord.Patches | Error | Overloaded injection targets must be disambiguated with parameterTypes.
CONCORD007 | Concord.Patches | Error | Injection method parameters and ControlHandle parameters must match the target method.
CONCORD008 | Concord.Patches | Error | Static target methods require compatible static declaration members.
CONCORD009 | Concord.Patches | Warning | Plain fields matching target fields should usually be marked with InjectField.
CONCORD010 | Concord.Patches | Warning | Duplicate injection declarations at the same target and position are usually accidental.
CONCORD011 | Concord.Patches | Error | Concord declaration members must use supported declaration shapes.
CONCORD012 | Concord.Patches | Warning | String patch targets should use typeof when the target type is available at compile time.
CONCORD013 | Concord.Patches | Warning | String member targets should use nameof when the target member is available at compile time.
CONCORD014 | Concord.Patches | Warning | Explicit patch targets should be inferred through inheritance when the target type can be inherited.

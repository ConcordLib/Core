# cancelling PatchTools.DetourMethod

one question: if concord stops harmony installing its native detour, does harmony's own api stay
honest about the patch?

**mostly yes, with two hard constraints, and one reading that is flatly dishonest.** harmony's
bookkeeping never touches the detour, so `GetPatchInfo`, `GetAllPatchedMethods`, `GetOriginalMethod`
and `Unpatch` all keep answering. but a blanket cancel kills reverse patches, and a cancel applied
to a method harmony already detoured leaves harmony reporting a patch that never runs.

run it with `./run-detour-cancel-probe.sh`. `--self-test` doctors a real run and confirms the checks
report FAIL. `--raw` prints the whole tsv.

## the seam, verified

against `/mnt/games/RimWorld/Mods/Harmony-net10/1.7/Assemblies/0Harmony.dll`, harmony `2.4.2.0`,
net10. decompile-server, not ilspycmd.

| claim | verified |
|---|---|
| `internal static void DetourMethod(MethodBase method, MethodBase replacement)` | yes, and re-read at runtime: `seam.signature`, `seam.is.void = 1` |
| exactly two callers | yes. `find_callers` returns 2: `PatchFunctions.UpdateWrapper`, `PatchFunctions.ReversePatch` |
| the void return means nothing has to be fabricated | yes. a cancelled `UpdateWrapper` would have to return a `MethodInfo` that `HarmonySharedState.UpdatePatchInfo` dereferences. `DetourMethod` returns nothing |
| the `Dispose()` of a prior detour is inside `DetourMethod` | yes. the whole body is `lock (detours)`, dispose the old entry if present, then `detours[method] = DetourFactory.Current.CreateDetour(...)` |
| `detours` is reachable | yes. `private static Dictionary<MethodBase, ICoreDetour>` on `PatchTools` |

**why the bookkeeping survives at all.** every one of `UpdateWrapper`'s five callers
(`PatchProcessor.Patch`, both `PatchProcessor.Unpatch` overloads,
`PatchClassProcessor.ProcessPatchJob`, `ProcessUnpatchJob`) calls
`HarmonySharedState.UpdatePatchInfo(original, replacement, patchInfo)` **after** `UpdateWrapper`
returns. `UpdateWrapper` itself only creates the replacement and hands it to `DetourMethod`. so a
cancel inside `DetourMethod` is downstream of nothing: the patch info, the
`originals[replacement] -> original` map and the version counter are all written afterwards
regardless.

### the two dll paths are not a stale mount

`/run/media/aaron/games` and `/mnt/games` are the same device and the same btrfs subvol
(`/dev/nvme0n1p1`, `subvol=/`). the md5s differ because the two paths name **different files**:
`Harmony-net10/Current/` is `2.4.1.0` and `LoadFolders.xml` maps it to rimworld 1.6, `1.7/` is
`2.4.2.0` and maps to 1.7. matching paths across the two mountpoints hash identically. the repo's
existing `run-rimworld-probe.sh` already points at `1.7/`, so this probe measures `2.4.2.0` too.

### harmony v3

not verified, and the brief's claim that v3 calls the same function is not checked here. there is no
harmony 3.x on this machine (`lib.harmony` has 2.3.3, 2.3.6, 2.4.2). harmonyx 2.10.2 **is** here and
has no `PatchTools.DetourMethod` at all: it detours through
`HarmonyLib.Public.Patching.NativeDetourMethodPatcher`. so the seam does not exist in that fork.

## how it was measured

a plain net10 console host, `detour-cancel/Source/`. no game, no container, no forked runtime. it
loads the real 0Harmony, patches `DetourMethod` with a harmony prefix, and reads harmony back
through harmony's own api.

the cancel is itself a harmony prefix because the mechanism cannot matter to the question: harmony's
bookkeeping has no way to know who stopped the detour. the cost is that `DetourMethod` shows up in
`GetAllPatchedMethods` and in `detours`, which check 3 counts explicitly rather than filtering out.

the hook runs one of four predicates, set per scenario:

| predicate | cancels |
|---|---|
| off | nothing. the control |
| all | every call |
| attribute | every call whose first argument has no `[HarmonyReversePatch]` |
| owned | only calls whose first argument is in a set the host pre-seeded |

every target is `[MethodImpl(MethodImplOptions.NoInlining)]`, so the jit cannot fold away the call
and make an uninstalled patch look like an installed one.

### the gate

checks 2-7 do not run unless the hook fires and the cancel changes behaviour. cancel off, a patched
method returns 42 and a detour exists for it. cancel on, the same shape of method returns 1 and no
detour exists. with a dead hook, every "stays honest" below would just be an unpatched harmony
answering normally, which is exactly the shape of a check that measures nothing.

## answers

### 1. does `GetPatchInfo` still report the patch: yes

| reading | cancel on | cancel off | control |
|---|---|---|---|
| `Prefixes.Count` | 1 | 1 | never-patched method: `GetPatchInfo` returns null |
| `Owners` | the patching id | same | |

### 2. does `GetAllPatchedMethods` still list the target: yes

listed under cancel, listed under the control, and a never-patched method is absent. the list also
holds `PatchTools.DetourMethod` itself, which is the hook's own target.

### 3. does `Unpatch` still work, and what does it think it is restoring: yes, and nothing specific

`Unpatch` does not restore anything. it rebuilds a wrapper from the reduced `PatchInfo` and sends it
through `DetourMethod` again, so with zero patches left the "restore" is a fresh native detour
pointing at a harmony-built copy of the original body.

| sequence | behaviour after unpatch | prefixes | detour present |
|---|---|---|---|
| patch and unpatch, cancel off | 1 | 0 | **yes, and it is a new detour object** |
| patch and unpatch, cancel on | 1 | 0 | no |
| patch cancelled, unpatch **not** cancelled | 1 | 0 | **yes, on a method that never had one** |

the third row is the constraint. a cancel that only covers the install lets `Unpatch` plant a native
detour over whatever body is live at that moment. on waylith that is the poisoning case the earlier
investigation already closed, so the cancel has to cover the unpatch path too. under cancel the
unpatch outcome is still right, because the method was never redirected in the first place.

both the cancel and the control leave the method **listed** by `GetAllPatchedMethods` with zero
patches after unpatch, so that is harmony 2.4 behaviour and not something the cancel caused. the
check compares the two instead of reporting it as a finding.

### 4. does `GetOriginalMethod` / `GetRealMethod` still answer: yes

`HarmonySharedState.GetRealMethod` returns **its own argument** when it finds no mapping, so "null
means not found" is not available and a null-return control is impossible to write. the reading is
"did the answer differ from the argument".

| reading | cancel on | cancel off | control |
|---|---|---|---|
| `GetOriginalMethod(replacement)` | the target method | the target method | an unmapped method reads back as itself |
| `GetRealMethod(replacement, true)` | the target method | the target method | same |

### 5. `ReversePatch` is the other caller: a blanket cancel kills it

| predicate | manual reverse patch (`CreateReversePatcher`) | attribute-driven (`PatchClassProcessor`) | ordinary patch |
|---|---|---|---|
| off (control) | 77, the source's body | not run | not run |
| all | **-1, dead. the standin runs its own body** | **-1, dead** | cancelled |
| attribute | 79 when the standin carries the attribute, **-1 when it does not** | not run | cancelled |
| owned | 81, survives | not run | cancelled, no detour left |

both standins in the attribute row went in through `CreateReversePatcher`. one carries
`[HarmonyReversePatch]` and one does not, which is the whole point: the attribute is a property of
the standin, not of the path it came through.

three ways to tell the callers apart at the cancel site, and only one of them held:

- **the `[HarmonyReversePatch]` attribute on argument 1.** unsound.
  `Harmony.CreateReversePatcher(original, new HarmonyMethod(standin))` needs no attribute anywhere,
  and `ReversePatcher.Patch` sets `standin.reversePatchType` itself. a bare standin measured dead at
  -1 under this predicate.
- **a stack walk for an `UpdateWrapper` or `ReversePatch` frame.** it resolved all 23 calls in this
  run, 17 `UpdateWrapper` and 6 `ReversePatch`, 0 unresolved, and check 6 requires that. but that is
  a this-run observation on coreclr, not a property: this phase exists because the jit and the aot
  compiler inline, and a stack walk is the first thing inlining takes away. untested on waylith and
  not something to build on.
- **an owned-set predicate on argument 1.** sound in both directions here: the reverse patch
  survived at 81 while the owned ordinary patch was cancelled and left no detour. the standin is a
  mod method concord has no body claim on, so it is never in the set.

so the two callers **can** be distinguished, but not by anything at the call site itself. the only
discriminator that held is concord's own ownership record, which means the cancel has to be a filter
over methods concord already owns, never a blanket hook.

### 6. the skipped `Dispose`: this is the dishonest reading

cancelling skips two things, not one: the `Dispose()` of any prior detour **and** the
`detours[method] = ...` assignment. so a method harmony already detoured keeps running its **old**
wrapper while harmony reports the new patch as installed.

| step | behaviour | prefixes | postfixes |
|---|---|---|---|
| control: prefix, then postfix, cancel off throughout | 42 then **142** | 1 | 1 |
| prefix cancel off, then postfix cancel on | 42 then **42** | 1 | 1 |

same bookkeeping, different behaviour. `Owners` on the second row names both harmony ids, so harmony
says two mods patched the method. one of them never runs and nothing reports that.

what leaks is not the skipped allocation. the new `ICoreDetour` is never created, so the skip
allocates nothing. what leaks is the **old** one staying live: its native trampoline and the
`DynamicMethod` it points at stay rooted by `PatchTools.detours` for the process's life, and the new
wrapper's `DynamicMethod` stays rooted by `HarmonySharedState.originals` for a wrapper that is never
entered.

it can be freed from outside. the dict value is an `IDisposable`:

| step | result |
|---|---|
| dispose the dict value by hand | the method runs its own body again, 42 back to 1 |
| harmony's bookkeeping after that | still 1 prefix and 1 postfix. now neither runs |
| `detours` after that | still holds the key, now a dead reference |
| a later uncancelled install | re-disposes the dead reference without throwing, and the method composes again at 142 |

so freeing it is possible but it does not fix the dishonesty, it moves it: harmony then reports two
patches on a method running none of them. the real constraint is ordering. concord must own the
method before harmony's first install, which is the case the earlier investigation already settled,
and this measurement does not reopen it.

## what this does not measure

not measured: the waylith runtime, the stack-walk discriminator under jit or aot inlining, harmony v3, what `GetMethodBody()` hands harmony for a body-replaced method, and concord's own code, which this probe never loads.

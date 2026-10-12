#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'TEXT'
run-rimworld-probe.sh [--keep-logs|--legacy|--help]

Boots RimWorld 1.7 twice in a gamecrate container with core + Harmony + the local Concord checkout
+ one probe mod: CONCORD_WAYLITH_BACKEND=1 (body replacement), then =0 (native detour). The off-run
is the control.

The backend arrives the way a player's install delivers it. The Concord mod is copied to a temp
directory and the built adapter goes in its Current/Backend/, which the engine does not scan and
DetourBackendLoader does. ConcordMod's constructor finds it, loads it and installs it before Concord
applies its own first patch. The probe installs nothing; it reads what the loader already chose.

Current/Backend/ is gitignored in that checkout, so it may already hold a backend from an earlier
staging build. The copy step never carries it across; this script is the only thing that puts a file
there, and it prints what it put.

Checks, most important first:
  0. setup: the staged Concord runtime, the Harmony bridge and the Concord the adapter compiled
     against all carry one git sha. A mismatched pair turns coexistence off, so nothing below
     would mean anything.
  1. positive control: Harmony alone changes a method, and Concord alone changes a different one,
     in the same process. Nothing below is judged unless this passes.
  2. Concord loads, the backend selects body replacement, and a patch applies to a mod method and
     to a real Assembly-CSharp method
  3. Harmony and Concord on DIFFERENT methods: both hold, neither breaks the other
  4. Harmony and Concord on the SAME method, Concord first then Harmony
  5. Harmony and Concord on the SAME method, Harmony first then Concord
  6. what the bridge did to the shared method, and whether a published body was left under Harmony
  7. every patch still holds once the game has finished loading
  8. a woven body stays PUBLISHED under a real Harmony patch on a real Assembly-CSharp method,
     and the frame running inside it attributes back to Concord's injection
  9. the hook was armed on every Harmony copy before anything could poison a method
 10. a caller already JIT-compiled before the weave runs the new body
 11. the heap survives a forced compacting gen2 GC taken with a woven body live

Exit: 0 all checks passed, 1 a check failed, 2 a check was inconclusive, 3 setup failed,
      4 a fixture run, which is never a measurement.

Flags:
  --keep-logs   leave the two collected game logs in /tmp
  --legacy      add a third run with a 1.6-era mod built against an older Concord.Ref, to see
                whether it still loads. Skipped by default because the mod is not part of this
                repo and its 1.7 incompatibilities are not Concord's. The check refuses to read
                an empty error count as a pass unless it first sees that mod's assembly in the
                AppDomain, and unless the same field reads false in a run without it.

Env:
  WAYLITH_RIMPROBE_TIMEOUT=420    seconds to wait for each run's report
  CONCORD_RIMWORLD_INSTALL=<dir>  the RimWorld 1.7 install to build against
  CONCORD_RIMWORLD_MOD=<dir>      the Concord RimWorld checkout to launch
  CONCORD_LEGACY_MOD=<dir>        the mod --legacy loads
  CONCORD_RIMPROBE_BACKEND=stage  what to put in the Concord mod's Current/Backend. The two
                                  non-default values are the negative controls for the loader:
                                    stage     the built adapter, correctly paired (default)
                                    none      nothing. The staged copy of the mod never includes a
                                              Current/Backend, whatever is in the checkout, and the
                                              script refuses to launch if one is there anyway.
                                              DetourBackendLoader must find no backend, say nothing,
                                              and leave the game booting on Concord's own detour
                                              backend. Every adapter reading then reads
                                              adapter-absent, so the run FAILS by design.
                                    mismatch  the built adapter with one hex digit of its
                                              ConcordBuild stamp flipped. The loader's pairing
                                              check must refuse it. Also FAILS by design.
  CONCORD_RIMPROBE_FIXTURE_ON=<log>   read these two logs instead of launching anything, so a
  CONCORD_RIMPROBE_FIXTURE_OFF=<log>  check can be shown failing and then passing without a game.
                                      Both must be set together. A fixture run always exits 4 and
                                      never prints RESULT: PASS, whatever the per-check lines say.
TEXT
}

KEEP_LOGS=0
LEGACY=0
for arg in "$@"; do
  case "$arg" in
    --help|-h) usage; exit 0 ;;
    --keep-logs) KEEP_LOGS=1 ;;
    --legacy) LEGACY=1 ;;
    *) echo "unknown argument '$arg'. --help lists them." >&2; exit 2 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/../../.." && pwd)"
WORKSPACE="$(cd "$REPO/../.." && pwd)"
CORE_ROOT="$REPO"
RIM_MOD="${CONCORD_RIMWORLD_MOD:-$WORKSPACE/Concord/RimWorld}"
GAME_DIR="${CONCORD_RIMWORLD_INSTALL:-/mnt/games/SteamLibrary/steamapps/common/RimWorld}"
HARMONY_DIR=/mnt/games/RimWorld/Mods/Harmony-net10/1.7/Assemblies
LEGACY_MOD="${CONCORD_LEGACY_MOD:-/mnt/games/SteamLibrary/steamapps/workshop/content/294100/3706851018}"

PROBE_SRC="$SCRIPT_DIR/rimworld"
HARMONY_PROBE_ID=cryptiklemur.waylithrimprobe
PROFILE=v17
INSTANCE=rprobe
CONTAINER="gamecrate-rimworld-$PROFILE-$INSTANCE"
RUNS="$HOME/.local/share/gamecrate/rimworld/$PROFILE/instances/$INSTANCE/logs/runs"
ON_LOG=/tmp/concord-waylith-rimworld-on.log
OFF_LOG=/tmp/concord-waylith-rimworld-off.log
LEGACY_LOG=/tmp/concord-waylith-rimworld-legacy.log
TIMEOUT="${WAYLITH_RIMPROBE_TIMEOUT:-420}"
STAGE="$(mktemp -d /tmp/concord-waylith-rimprobe.XXXX)"
BACKEND_MODE="${CONCORD_RIMPROBE_BACKEND:-stage}"
FIXTURE_ON="${CONCORD_RIMPROBE_FIXTURE_ON:-}"
FIXTURE_OFF="${CONCORD_RIMPROBE_FIXTURE_OFF:-}"
FIXTURE=0
if [ -n "$FIXTURE_ON$FIXTURE_OFF" ]; then FIXTURE=1; fi

setup_fail() { echo "SETUP-FAIL: $1" >&2; exit 3; }

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  (cd /tmp && gamecrate stop rimworld "$PROFILE" --instance "$INSTANCE" >/dev/null 2>&1) || true
  rm -rf "$STAGE"
}
trap cleanup EXIT

case "$BACKEND_MODE" in
  stage|none|mismatch) ;;
  *) setup_fail "CONCORD_RIMPROBE_BACKEND='$BACKEND_MODE' is not stage, none or mismatch" ;;
esac

if [ "$FIXTURE" = 1 ]; then
  test -n "$FIXTURE_ON" || setup_fail "CONCORD_RIMPROBE_FIXTURE_OFF is set without CONCORD_RIMPROBE_FIXTURE_ON. A fixture run needs both logs."
  test -n "$FIXTURE_OFF" || setup_fail "CONCORD_RIMPROBE_FIXTURE_ON is set without CONCORD_RIMPROBE_FIXTURE_OFF. A fixture run needs both logs."
  test -f "$FIXTURE_ON" || setup_fail "no fixture log at $FIXTURE_ON"
  test -f "$FIXTURE_OFF" || setup_fail "no fixture log at $FIXTURE_OFF"
  ON_LOG="$FIXTURE_ON"
  OFF_LOG="$FIXTURE_OFF"
  LEGACY=0
  echo "== FIXTURE RUN. No game launched, no backend staged, nothing measured. The checks below are"
  echo "== reading $ON_LOG and $OFF_LOG, which a person wrote. This exits 4 and cannot print RESULT: PASS."
else
command -v docker >/dev/null || setup_fail "docker is not on PATH"
command -v gamecrate >/dev/null || setup_fail "gamecrate is not on PATH"
test -f "$GAME_DIR/Assembly-CSharp.dll" || setup_fail "no Assembly-CSharp.dll under $GAME_DIR"
test -f "$HARMONY_DIR/0Harmony.dll" || setup_fail "no 0Harmony.dll under $HARMONY_DIR"
test -f "$RIM_MOD/About/About.xml" || setup_fail "no Concord RimWorld mod at $RIM_MOD"

STAGED_RUNTIME="$RIM_MOD/Current/Assemblies/0Concord.dll"
STAGED_BRIDGE="$RIM_MOD/Current/Bridge/Concord.Harmony.dll"
ADAPTER_CONCORD="$CORE_ROOT/Assemblies/net10.0/Concord.dll"
test -f "$STAGED_RUNTIME" || setup_fail "$STAGED_RUNTIME missing. Build the Concord RimWorld mod first."
test -f "$STAGED_BRIDGE" || setup_fail "$STAGED_BRIDGE missing. Build the Concord RimWorld mod first."
test -f "$ADAPTER_CONCORD" || setup_fail "$ADAPTER_CONCORD missing. Build Concord Core first."

sha_of() {
  strings -a "$1" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+[-A-Za-z0-9.]*\+[0-9a-f]{40}' | head -1 | cut -d+ -f2 || true
}

RUNTIME_SHA="$(sha_of "$STAGED_RUNTIME")"
BRIDGE_SHA="$(sha_of "$STAGED_BRIDGE")"
ADAPTER_SHA="$(sha_of "$ADAPTER_CONCORD")"
echo "== sha runtime=${RUNTIME_SHA:0:12} bridge=${BRIDGE_SHA:0:12} adapter-compiled-against=${ADAPTER_SHA:0:12}"
[ -n "$RUNTIME_SHA" ] || setup_fail "no build sha in $STAGED_RUNTIME, so nothing can be paired"
[ "$RUNTIME_SHA" = "$BRIDGE_SHA" ] \
  || setup_fail "$STAGED_BRIDGE was built from $BRIDGE_SHA and $STAGED_RUNTIME from $RUNTIME_SHA. HarmonyProbe refuses a mismatched pair, so coexistence would be off and checks 3-5 would measure nothing."
[ "$RUNTIME_SHA" = "$ADAPTER_SHA" ] \
  || setup_fail "the adapter compiles against Concord $ADAPTER_SHA but the game will load $RUNTIME_SHA. Rebuild both from one Core checkout."

echo "== game $(cat "$GAME_DIR/Version.txt" 2>/dev/null || echo unknown), harmony $(strings -a "$HARMONY_DIR/0Harmony.dll" | grep -oE '^2\.[0-9]+\.[0-9]+\.[0-9]+$' | head -1)"

dotnet build "$REPO/Concord.Host.Adapter.slnx" -c Release --verbosity quiet \
  || setup_fail "the adapter did not build"
ADAPTER="$REPO/src/Concord.Host.Adapter/bin/Release/net10.0/Concord.Host.Adapter.dll"
test -f "$ADAPTER" || setup_fail "$ADAPTER missing after the adapter build"

dotnet build "$PROBE_SRC/Source/ConcordWaylithRimProbe.csproj" -c Release --verbosity quiet \
  -p:GameDir="$GAME_DIR" -p:HarmonyDir="$HARMONY_DIR" -p:ConcordDir="$CORE_ROOT/Assemblies/net10.0" \
  || setup_fail "the probe mod did not build"
PROBE_DLL="$PROBE_SRC/Source/bin/probe/ConcordWaylithRimProbe.dll"
test -f "$PROBE_DLL" || setup_fail "$PROBE_DLL is missing after the probe build"

mkdir -p "$STAGE/probe/About" "$STAGE/probe/Assemblies"
cp "$PROBE_SRC/About/About.xml" "$STAGE/probe/About/"
cp "$PROBE_DLL" "$STAGE/probe/Assemblies/"
PROBE_STAGE="$STAGE/probe"

CONCORD_STAGE="$STAGE/concord"
mkdir -p "$CONCORD_STAGE/Current"
cp -a "$RIM_MOD/About" "$CONCORD_STAGE/"
find "$RIM_MOD/Current" -mindepth 1 -maxdepth 1 ! -name Backend -exec cp -a {} "$CONCORD_STAGE/Current/" \;
cp -a "$RIM_MOD/LoadFolders.xml" "$RIM_MOD/.gamecrate.yml" "$CONCORD_STAGE/"
BACKEND_DEST="$CONCORD_STAGE/Current/Backend/Concord.Host.Adapter.dll"

case "$BACKEND_MODE" in
  stage)
    mkdir -p "$(dirname "$BACKEND_DEST")"
    cp "$ADAPTER" "$BACKEND_DEST"
    echo "== backend staged at Current/Backend/, the only path DetourBackendLoader probes"
    ;;
  none)
    echo "== no backend staged, which is every player's ordinary case"
    ;;
  mismatch)
    mkdir -p "$(dirname "$BACKEND_DEST")"
    cp "$ADAPTER" "$BACKEND_DEST"
    WRONG_SHA="${ADAPTER_SHA:0:39}$(if [ "${ADAPTER_SHA: -1}" = 0 ]; then echo 1; else echo 0; fi)"
    python3 - "$BACKEND_DEST" "$ADAPTER_SHA" "$WRONG_SHA" <<'PY' || setup_fail "could not flip the ConcordBuild stamp"
import sys
path, old, new = sys.argv[1], sys.argv[2].encode(), sys.argv[3].encode()
blob = open(path, 'rb').read()
found = blob.count(old)
if found != 1:
    sys.exit("expected exactly one copy of the sha in the adapter, found %d" % found)
open(path, 'wb').write(blob.replace(old, new))
PY
    echo "== backend staged with its ConcordBuild stamp flipped to $WRONG_SHA, runtime is $RUNTIME_SHA"
    ;;
esac

echo "== staged Current/ holds: $(/usr/bin/ls -1 "$CONCORD_STAGE/Current" | tr '\n' ' ')"
if [ -d "$CONCORD_STAGE/Current/Backend" ]; then
  echo "== staged Current/Backend/ holds: $(/usr/bin/ls -1 "$CONCORD_STAGE/Current/Backend" | tr '\n' ' ')"
else
  echo "== staged Current/Backend/ does not exist, so DetourBackendLoader has nothing to find"
fi
if [ "$BACKEND_MODE" = none ] && [ -e "$CONCORD_STAGE/Current/Backend" ]; then
  setup_fail "CONCORD_RIMPROBE_BACKEND=none staged a Current/Backend anyway, so this run is not a negative control and every adapter reading below could be a real backend's"
fi
echo "== probe staged at $PROBE_STAGE, Concord mod staged at $CONCORD_STAGE"

run_mode() {
  local mode="$1" out="$2" extra_mod="$3" rc=0 dir="" before=""
  echo "== run CONCORD_WAYLITH_BACKEND=$mode${extra_mod:+ plus $extra_mod}"
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  before="$(/usr/bin/ls -1 "$RUNS" 2>/dev/null | sort | tail -1)"

  local args=(run "$PROFILE" --instance "$INSTANCE"
    --only concordlib.concord --only brrainz.harmony --only CryptikLemur.WaylithRimProbe
    --use CryptikLemur.WaylithRimProbe="$PROBE_STAGE"
    --use concordlib.concord="$CONCORD_STAGE"
    --no-build --no-stale-check --replace
    --mode headless --marker '[RPROBE] done' --timeout "$TIMEOUT"
    --docker-arg -e --docker-arg "CONCORD_WAYLITH_BACKEND=$mode"
    --docker-arg -e --docker-arg "CONCORD_RIMPROBE_WATCH=${WATCH:-}")
  if [ -n "$extra_mod" ]; then
    args+=(--only "$extra_mod" --use "$extra_mod=$LEGACY_MOD" --only RimWorks.RimLogging)
  fi

  (cd /tmp && gamecrate "${args[@]}") >/dev/null 2>&1 || rc=$?
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true

  dir="$(/usr/bin/ls -1 "$RUNS" 2>/dev/null | sort | tail -1)"
  if [ -z "$dir" ] || [ ! -f "$RUNS/$dir/stdout.log" ]; then
    echo "SETUP-FAIL: gamecrate exited $rc and left no stdout.log under $RUNS. The game never started."
    exit 3
  fi
  if [ "$dir" = "$before" ]; then
    echo "SETUP-FAIL: gamecrate exited $rc and wrote no new run directory. The newest one under $RUNS is still $dir, which this launch did not produce. Reading it would report an older run's numbers as this one's."
    exit 3
  fi

  cp -f "$RUNS/$dir/stdout.log" "$out"
  echo "== run CONCORD_WAYLITH_BACKEND=$mode ended, gamecrate exit $rc, log at $out"
}

run_mode 1 "$ON_LOG" ""
run_mode 0 "$OFF_LOG" ""
fi

if [ "$LEGACY" = 1 ]; then
  test -f "$LEGACY_MOD/About/About.xml" || setup_fail "--legacy was asked for but there is no mod at $LEGACY_MOD"
  LEGACY_ID="$(sed -nE 's:.*<packageId>(.*)</packageId>.*:\1:p' "$LEGACY_MOD/About/About.xml" | head -1)"
  LEGACY_DLL="$(find "$LEGACY_MOD/Assemblies" -maxdepth 1 -name '*.dll' 2>/dev/null | sort | sed -n 1p)"
  [ -n "$LEGACY_DLL" ] || LEGACY_DLL="$(find "$LEGACY_MOD" -name '*.dll' -path '*Assemblies*' | sort | sed -n 1p)"
  LEGACY_ASM="$(basename "${LEGACY_DLL:-}" .dll)"
  [ -n "$LEGACY_ASM" ] || setup_fail "$LEGACY_MOD ships no assembly, so 'does it still load' has nothing to answer"
  echo "== legacy mod $LEGACY_ID ships $LEGACY_ASM.dll"
  WATCH="$LEGACY_ASM" run_mode 1 "$LEGACY_LOG" "$LEGACY_ID"
fi

field() {
  awk -v p="$2" -v k="$3" '
    index($0, "[RPROBE] " p) {
      for (i = 1; i <= NF; i++) {
        n = index($i, "=")
        if (n > 0 && substr($i, 1, n - 1) == k) { print substr($i, n + 1); exit }
      }
    }' "$1"
}

PASSED=0
FAILED=0
INCONCLUSIVE=0
pass() { echo "PASS  $1"; PASSED=$((PASSED + 1)); }
fail() { echo "FAIL  $1"; FAILED=$((FAILED + 1)); }
unsure() { echo "INCON $1"; INCONCLUSIVE=$((INCONCLUSIVE + 1)); }

legacy_binding_errors() {
  grep -aE 'Could not load type|TypeLoadException|MissingMethodException|MissingFieldException|BadImageFormatException' "$1" \
    | grep -av 'backend-fallback' \
    | grep -ai -- "$LEGACY_ASM" || true
}

dump() {
  echo "--- last 40 lines of $1 ---"
  tail -40 "$1" 2>/dev/null || echo "(no log)"
}

for pair in "on:$ON_LOG" "off:$OFF_LOG"; do
  mode="${pair%%:*}"; log="${pair#*:}"
  if ! grep -q '\[RPROBE\] start' "$log" 2>/dev/null; then
    echo "SETUP-FAIL: the $mode run never reached the probe mod. An empty report is not a clean run."
    dump "$log"
    exit 3
  fi
  if ! grep -q '\[RPROBE\] measured' "$log" 2>/dev/null; then
    echo "SETUP-FAIL: the $mode run started the probe but never finished measuring, so it died mid-measurement."
    dump "$log"
    exit 3
  fi
  if ! grep -q '\[RPROBE\] done' "$log" 2>/dev/null; then
    echo "SETUP-FAIL: the $mode run measured but never reached the deferred report, so the game did not survive mod loading."
    dump "$log"
    exit 3
  fi
done

echo
echo "concord   $(grep -m1 '\[RPROBE\] env concord=' "$ON_LOG" | sed 's/.*env //')"
echo "harmony   $(grep -m1 '\[RPROBE\] env harmony=' "$ON_LOG" | sed 's/.*env //')"
echo "adapter   $(grep -m1 '\[RPROBE\] env adapter ' "$ON_LOG" | sed 's/.*env adapter //')"
echo "backend   before=$(field "$ON_LOG" env backend-before) after=$(field "$ON_LOG" install backend-after)"

ON_SPLIT_H="$(field "$ON_LOG" split harmony-target)"
ON_SPLIT_C="$(field "$ON_LOG" split concord-target)"
OFF_SPLIT_H="$(field "$OFF_LOG" split harmony-target)"
OFF_SPLIT_C="$(field "$OFF_LOG" split concord-target)"

echo
echo "positive control on: harmony-only-method=$ON_SPLIT_H (want 21) concord-only-method=$ON_SPLIT_C (want 11)"
echo "positive control off: harmony-only-method=$OFF_SPLIT_H (want 21) concord-only-method=$OFF_SPLIT_C (want 11)"

CONTROL_OK=0
if [ "$ON_SPLIT_H" = "21" ] && [ "$ON_SPLIT_C" = "11" ] && [ "$OFF_SPLIT_H" = "21" ] && [ "$OFF_SPLIT_C" = "11" ]; then
  CONTROL_OK=1
  pass "positive control: in both runs Harmony changed one method and Concord changed another, so the reader below can see either patcher working"
else
  fail "positive control: Harmony and Concord did not both take effect on their own methods, so a later 'someone lost' reading cannot be trusted"
  dump "$ON_LOG"
fi

ON_BACKEND="$(field "$ON_LOG" install selected)"
OFF_BACKEND="$(field "$OFF_LOG" install selected)"
ON_PATCHED_BEFORE="$(field "$ON_LOG" install patched-before)"

echo
echo "loader on:  $(grep -m1 -a 'optional detour backend' "$ON_LOG" | sed 's/^.*\[Concord.RimWorld\] //' | cut -c1-200)"
echo "loader off: $(grep -m1 -a 'optional detour backend' "$OFF_LOG" | sed 's/^.*\[Concord.RimWorld\] //' | cut -c1-200)"
echo "install  $ON_PATCHED_BEFORE Concord target(s) were already patched when the probe looked"
echo "install  on-run=$ON_BACKEND"
echo "install  off-run=$OFF_BACKEND"
echo "capability $(grep -m1 '\[RPROBE\] install capability=' "$ON_LOG" | sed 's/.*capability=//')"

if [ "$ON_BACKEND" = "body-replacement" ]; then
  pass "the on-run selected body replacement, so every result below was measured through it"
else
  fail "the on-run selected '$ON_BACKEND', not body replacement, so nothing below tests this backend"
  grep -E '\[RPROBE\] (install|adapter-log)' "$ON_LOG" | head -12 || true
fi

if [ "$OFF_BACKEND" = "native-detour" ]; then
  pass "the off-run selected the native detour, so it is a real control"
else
  fail "the off-run selected '$OFF_BACKEND', not the native detour, so there is no control"
fi

ON_SOLO_BEFORE="$(field "$ON_LOG" solo before)"
ON_SOLO_AFTER="$(field "$ON_LOG" solo after)"
ON_SOLO_REPLACED="$(field "$ON_LOG" solo replaced)"
ON_GAME_BEFORE="$(field "$ON_LOG" game before)"
ON_GAME_AFTER="$(field "$ON_LOG" game after)"
ON_GAME_REVERTED="$(field "$ON_LOG" game reverted)"
ON_GAME_REPLACED="$(field "$ON_LOG" game replaced)"
OFF_GAME_AFTER="$(field "$OFF_LOG" game after)"

echo
echo "mod method   on: $ON_SOLO_BEFORE -> $ON_SOLO_AFTER (want 11), published-body=$ON_SOLO_REPLACED"
echo "game method  on: $ON_GAME_BEFORE -> $ON_GAME_AFTER (want 42) -> $ON_GAME_REVERTED after revert (want 2), published-body=$ON_GAME_REPLACED"
echo "game method off: -> $OFF_GAME_AFTER"

if [ "$ON_SOLO_AFTER" = "11" ] && [ "$ON_GAME_AFTER" = "42" ]; then
  pass "a Concord patch changed a mod method and a real Assembly-CSharp method under body replacement"
else
  fail "a Concord patch did not take (mod=$ON_SOLO_AFTER want 11, game=$ON_GAME_AFTER want 42)"
fi

OFF_SOLO_REPLACED="$(field "$OFF_LOG" solo replaced)"
OFF_GAME_REPLACED="$(field "$OFF_LOG" game replaced)"
echo "published-body oracle: on-run says mod=$ON_SOLO_REPLACED game=$ON_GAME_REPLACED, off-run says mod=$OFF_SOLO_REPLACED game=$OFF_GAME_REPLACED"

if [ "$OFF_SOLO_REPLACED" != "false" ] || [ "$OFF_GAME_REPLACED" != "false" ]; then
  fail "FrameAttribution.OwnersRegisteredFor still reports a published body in the detour run (mod=$OFF_SOLO_REPLACED game=$OFF_GAME_REPLACED), so it does not tell a published body from a detour and every 'published-body' reading here is worthless"
elif [ "$ON_SOLO_REPLACED" = "true" ] && [ "$ON_GAME_REPLACED" = "true" ]; then
  pass "both targets carry a body the backend published, not a detour it handed off. The same oracle reads false for both in the detour run, so it discriminates."
else
  fail "a target was handed to the detour instead of published (mod=$ON_SOLO_REPLACED game=$ON_GAME_REPLACED)"
  grep -aE 'Concord.Waylith|CONC[0-9]{3}' "$ON_LOG" | cut -c1-200 | head -12 || true
fi

if [ "$ON_GAME_REVERTED" = "2" ]; then
  pass "reverting the game-method patch put GenMath.PositiveMod back, so the probe did not leave the game broken"
else
  fail "GenMath.PositiveMod(7,5) reads $ON_GAME_REVERTED after revert, not 2, so a revert under this backend does not restore a game method"
fi

if [ "$CONTROL_OK" = 1 ]; then
  if [ "$(field "$ON_LOG" split harmony-replaced)" = "false" ]; then
    pass "Harmony and Concord held different methods in one process: neither patcher touched the other's target"
  else
    fail "the backend published a body for the method only Harmony patched, so the two patchers are not on separate paths"
  fi
else
  unsure "the positive control failed, so 'different methods' cannot be judged"
fi

ON_CF_CONCORD="$(field "$ON_LOG" concord-first after-concord)"
ON_CF_HARMONY="$(field "$ON_LOG" concord-first after-harmony)"
OFF_CF_CONCORD="$(field "$OFF_LOG" concord-first after-concord)"
OFF_CF_HARMONY="$(field "$OFF_LOG" concord-first after-harmony)"
ON_CF_OWNERS="$(field "$ON_LOG" concord-first concord-owners)"

echo
echo "concord then harmony  on: $ON_CF_CONCORD then $ON_CF_HARMONY   (3 none, 33 concord, 103 harmony, 133 both), owners=$ON_CF_OWNERS"
echo "concord then harmony off: $OFF_CF_CONCORD then $OFF_CF_HARMONY"

judge_shared() {
  local label="$1" got="$2" both="$3" concord="$4" harmony="$5" original="$6"
  case "$got" in
    "$both") pass "$label: both patchers still run on the shared method ($got)" ;;
    "$concord") fail "$label: Harmony's patch is not running on the shared method ($got). The second mod to patch lost." ;;
    "$harmony") fail "$label: Concord's injection is not running on the shared method ($got). The first mod to patch lost." ;;
    "$original") fail "$label: neither patcher runs on the shared method ($got). Both mods lost." ;;
    *) unsure "$label: the shared method returned '$got', which is none of $both/$concord/$harmony/$original" ;;
  esac
}

if [ "$CONTROL_OK" = 1 ]; then
  if [ "$ON_CF_CONCORD" != "33" ]; then
    fail "concord-first: Concord's own patch read $ON_CF_CONCORD, not 33, before Harmony was involved at all, so the order case never got set up"
  else
    judge_shared "concord-first" "$ON_CF_HARMONY" 133 33 103 3
  fi
else
  unsure "the positive control failed, so concord-first cannot be judged"
fi

ON_HF_HARMONY="$(field "$ON_LOG" harmony-first after-harmony)"
ON_HF_CONCORD="$(field "$ON_LOG" harmony-first after-concord)"
OFF_HF_HARMONY="$(field "$OFF_LOG" harmony-first after-harmony)"
OFF_HF_CONCORD="$(field "$OFF_LOG" harmony-first after-concord)"
ON_HF_REPLACED="$(field "$ON_LOG" harmony-first replaced)"
ON_HF_OWNERS="$(field "$ON_LOG" harmony-first concord-owners)"

echo
echo "harmony then concord  on: $ON_HF_HARMONY then $ON_HF_CONCORD   (4 none, 44 concord, 204 harmony, 244 both), published-body=$ON_HF_REPLACED owners=$ON_HF_OWNERS"
echo "harmony then concord off: $OFF_HF_HARMONY then $OFF_HF_CONCORD"

if [ "$CONTROL_OK" = 1 ]; then
  if [ "$ON_HF_HARMONY" != "204" ]; then
    fail "harmony-first: Harmony's own patch read $ON_HF_HARMONY, not 204, before Concord was involved at all, so the order case never got set up"
  else
    judge_shared "harmony-first" "$ON_HF_CONCORD" 244 44 204 4
  fi
else
  unsure "the positive control failed, so harmony-first cannot be judged"
fi

ON_REPLACED_AT_CONCORD="$(field "$ON_LOG" concord-first replaced-at-concord)"
ON_REPLACED_AFTER_HARMONY="$(field "$ON_LOG" concord-first replaced-after-harmony)"

echo
echo "bridge on:  $(grep -ao '\[Concord.Coex\].*' "$ON_LOG" | cut -c1-60 | sort -u | tr '\n' '|')"
echo "bridge off: $(grep -ao '\[Concord.Coex\].*' "$OFF_LOG" | cut -c1-60 | sort -u | tr '\n' '|')"
echo "shared-method body on: published when Concord was alone=$ON_REPLACED_AT_CONCORD, still published once Harmony arrived=$ON_REPLACED_AFTER_HARMONY, published when Harmony was first=$ON_HF_REPLACED"

SHARED_PROMOTED=0
SHARED_SUPPRESSED=0
if grep -q 'Concord.Coex\] promoted' "$ON_LOG" && grep -q 'Concord.Coex\] routed-contested' "$ON_LOG"; then SHARED_PROMOTED=1; fi
if grep -q 'Concord.Coex\] hook-installed' "$ON_LOG"; then SHARED_SUPPRESSED=1; fi
SHARED_REFUSED="$(grep -a -e 'Concord.Coex\] compose-refused' -e 'Concord.Coex\] compose-unowned' -e 'Concord.Coex\] suppress-failed' "$ON_LOG" | cut -c1-240 | head -3 || true)"

if [ "$SHARED_PROMOTED" = 0 ] && [ "$SHARED_SUPPRESSED" = 0 ]; then
  unsure "both patchers ran on the shared method, but the bridge logged neither a promoted/routed-contested marker nor a hook-installed one, so what made that work was not measured"
elif [ "$ON_REPLACED_AFTER_HARMONY" = "false" ] && [ "$ON_HF_REPLACED" = "false" ] && [ "$SHARED_PROMOTED" = 1 ]; then
  pass "on a shared method the backend hands the method back and the Harmony bridge composes both, in both orders. Harmony's permanent poisoning of a body-replaced method never gets a chance to fire because no body is left under it."
elif [ "$ON_REPLACED_AFTER_HARMONY" = "true" ] && [ -z "$SHARED_REFUSED" ]; then
  pass "on a shared method Concord keeps its published body and Harmony's own build is cancelled at the UpdateWrapper head, so both patchers run out of one woven body (concord-first=$ON_REPLACED_AFTER_HARMONY harmony-first=$ON_HF_REPLACED). Whether that body is live rather than dead is check 9's poison count, not this line."
elif [ -n "$SHARED_REFUSED" ]; then
  fail "the shared method was neither promoted nor composed: $SHARED_REFUSED"
else
  fail "the shared method's body state is neither of the two mechanisms this build has (concord-first=$ON_REPLACED_AFTER_HARMONY harmony-first=$ON_HF_REPLACED, promoted=$SHARED_PROMOTED hook=$SHARED_SUPPRESSED), so one order gave the body up and the other kept it and nothing says why"
fi

ON_LATE_SOLO="$(field "$ON_LOG" late solo)"
ON_LATE_HARMONY="$(field "$ON_LOG" late harmony-only)"
ON_LATE_CF="$(field "$ON_LOG" late concord-first)"
ON_LATE_HF="$(field "$ON_LOG" late harmony-first)"
ON_LATE_REPLACED="$(field "$ON_LOG" late solo-replaced)"
ON_LATE_WATCH="$(field "$ON_LOG" late loaded)"

echo
echo "after the rest of boot on: solo=$ON_LATE_SOLO (want 11) harmony-only=$ON_LATE_HARMONY (want 21) concord-first=$ON_LATE_CF (want 133) harmony-first=$ON_LATE_HF (want 244) still-published=$ON_LATE_REPLACED"

if [ "$ON_LATE_SOLO" = "11" ] && [ "$ON_LATE_HARMONY" = "21" ] && [ "$ON_LATE_CF" = "133" ] && [ "$ON_LATE_HF" = "244" ] && [ "$ON_LATE_REPLACED" = "true" ]; then
  pass "every patch still held once the game finished loading defs and reached the deferred queue, with the published body still in place"
else
  fail "a patch stopped holding between the mod ctor and the end of loading (solo=$ON_LATE_SOLO harmony-only=$ON_LATE_HARMONY concord-first=$ON_LATE_CF harmony-first=$ON_LATE_HF published=$ON_LATE_REPLACED)"
fi

ON_W_VALUE="$(field "$ON_LOG" woven one-call)"
ON_W_PUB_AT_C="$(field "$ON_LOG" woven published-at-concord)"
ON_W_PUB="$(field "$ON_LOG" woven published)"
ON_W_CLAIMED="$(field "$ON_LOG" woven claimed)"
ON_W_POISONED="$(field "$ON_LOG" woven poisoned)"
ON_W_FINDING="$(field "$ON_LOG" woven finding)"
ON_W_CD="$(field "$ON_LOG" woven concord-delta)"
ON_W_HD="$(field "$ON_LOG" woven harmony-delta)"
ON_W_RUNS="$(field "$ON_LOG" woven concord-runs)"
ON_W_OWNERS="$(field "$ON_LOG" woven owners)"
ON_W_FRAME_OWNER="$(field "$ON_LOG" wovenframe owner)"
ON_W_FRAME_METHOD="$(field "$ON_LOG" wovenframe method)"
ON_W_FRAME_OFFSET="$(field "$ON_LOG" wovenframe offset)"
ON_W_FRAME_STACK="$(field "$ON_LOG" wovenframe stack)"
ON_W_HELD_VALUE="$(field "$ON_LOG" wovenheld value)"
ON_W_HELD_PUB="$(field "$ON_LOG" wovenheld published)"
ON_W_HELD_POISONED="$(field "$ON_LOG" wovenheld poisoned)"
OFF_W_PUB="$(field "$OFF_LOG" woven published)"
OFF_W_FRAME_OWNER="$(field "$OFF_LOG" wovenframe owner)"
OFF_W_FRAME_METHOD="$(field "$OFF_LOG" wovenframe method)"
OFF_W_CD="$(field "$OFF_LOG" woven concord-delta)"
OFF_W_HD="$(field "$OFF_LOG" woven harmony-delta)"

echo
echo "woven real game method, real mod harmony patch, left in place:"
echo "  on:  GenMath.PositiveMod(9,4)=$ON_W_VALUE (want 1) published-at-concord=$ON_W_PUB_AT_C published-under-harmony=$ON_W_PUB claimed=$ON_W_CLAIMED poisoned=$ON_W_POISONED finding=$ON_W_FINDING"
echo "  on:  one call ran concord +$ON_W_CD and harmony +$ON_W_HD, $ON_W_RUNS concord runs so far, owners=$ON_W_OWNERS"
echo "  on:  frame inside the body method=$ON_W_FRAME_METHOD offset=$ON_W_FRAME_OFFSET owner=$ON_W_FRAME_OWNER stack=$ON_W_FRAME_STACK"
echo "  off: published=$OFF_W_PUB frame-method=$OFF_W_FRAME_METHOD frame-owner=$OFF_W_FRAME_OWNER concord+$OFF_W_CD harmony+$OFF_W_HD"
echo "  on:  after loading finished value=$ON_W_HELD_VALUE published=$ON_W_HELD_PUB poisoned=$ON_W_HELD_POISONED"

W_REFUSAL="$(grep -a 'Concord.Coex\] compose-refused' "$ON_LOG" | grep -a 'PositiveMod' | cut -c1-240 | head -1 || true)"

if [ -z "$ON_W_VALUE" ] || [ -z "$ON_W_FRAME_OWNER" ]; then
  unsure "the woven step did not report, so a published body under Harmony was not measured at all"
  grep -a '\[RPROBE\] woven' "$ON_LOG" | cut -c1-240 | head -4 || true
elif [ "$ON_W_POISONED" != "true" ] && [ "$ON_W_POISONED" != "false" ]; then
  unsure "the contention oracle read '$ON_W_POISONED' rather than true or false, so whether the body survived Harmony is unanswered"
elif [ "$OFF_W_FRAME_OWNER" != "<null>" ] && [ "$OFF_W_FRAME_OWNER" != "<nomatch>" ] && [ "$OFF_W_FRAME_OWNER" != "adapter-absent" ]; then
  fail "FrameAttribution.OwnerOfFrame answered '$OFF_W_FRAME_OWNER' in the detour run, where no body was published, so it does not tell a woven frame from a wrapper frame and the on-run reading means nothing"
elif [ -n "$W_REFUSAL" ]; then
  fail "the Harmony patch on GenMath.PositiveMod was refused, so Concord handed the method back instead of keeping its body: $W_REFUSAL"
elif [ "$ON_W_PUB" != "true" ] || [ "$ON_W_CLAIMED" != "true" ]; then
  fail "the body was disowned once Harmony patched the same game method (published=$ON_W_PUB claimed=$ON_W_CLAIMED, and it read published-at-concord=$ON_W_PUB_AT_C before Harmony arrived)"
elif [ "$ON_W_POISONED" = "true" ]; then
  fail "WriterRegistry reports GenMath.PositiveMod poisoned (finding=$ON_W_FINDING), so a detour runs in front of Concord's body and that body is dead code"
elif [ "$ON_W_FRAME_OWNER" = "<null>" ] || [ "$ON_W_FRAME_METHOD" != "GenMath.PositiveMod" ]; then
  fail "the frame that ran the injection is '$ON_W_FRAME_METHOD' owned by '$ON_W_FRAME_OWNER', not an offset inside GenMath.PositiveMod's published body, so what executed was a wrapper (stack=$ON_W_FRAME_STACK)"
elif [ "${ON_W_CD:-0}" -lt 1 ] || [ "${ON_W_HD:-0}" -lt 1 ]; then
  fail "one call to the shared game method ran concord +$ON_W_CD and harmony +$ON_W_HD; both have to run, so one patcher's code is not in the body"
elif [ "$ON_W_VALUE" != "1" ] || [ "$ON_W_HELD_VALUE" != "1" ]; then
  fail "GenMath.PositiveMod(9,4) reads $ON_W_VALUE with both patches live and $ON_W_HELD_VALUE after loading, not 1, so the woven body returns the wrong answer to the game"
elif ! printf '%s' "$ON_W_OWNERS" | grep -q 'WovenPatch'; then
  fail "the published body's owner list is '$ON_W_OWNERS' and carries no Concord injection owner, so the body in effect is not the one Concord composed"
elif ! printf '%s' "$ON_W_OWNERS" | grep -q "$HARMONY_PROBE_ID"; then
  fail "the published body's owner list is '$ON_W_OWNERS' and does not name the Harmony owner $HARMONY_PROBE_ID, so Harmony's patch was not folded into the body that runs"
elif [ "$ON_W_HELD_PUB" != "true" ] || [ "$ON_W_HELD_POISONED" != "false" ]; then
  fail "the body was published while the probe watched and gone by the end of loading (published=$ON_W_HELD_PUB poisoned=$ON_W_HELD_POISONED)"
else
  pass "a real Assembly-CSharp method stayed PUBLISHED with a real mod's Harmony patch on it: both patchers' code ran from inside GenMath.PositiveMod's own frame at IL offset $ON_W_FRAME_OFFSET, owners $ON_W_OWNERS, still published after loading finished. The same frame oracle reads '$OFF_W_FRAME_OWNER' in the detour run, so it discriminates."
fi

hashes_of() { printf '%s' "$1" | tr '/ ' '\n\n' | sed -n 's/.*#\(-*[0-9]\+\).*/\1/p' | sort -u | tr '\n' ' '; }

ON_COPIES="$(field "$ON_LOG" coex harmony-copies)"
ON_COPY_COUNT="$(field "$ON_LOG" coex count)"
ON_POISON_COUNT="$(field "$ON_LOG" coex-registry poisoned)"
ON_CLAIM_COUNT="$(field "$ON_LOG" coex-registry claims)"
ON_PUBLISHED_COUNT="$(field "$ON_LOG" coex-registry published-bodies)"
ON_CLAIM_TARGETS="$(field "$ON_LOG" coex-claims targets)"
PROBE_HASHES="$(hashes_of "$ON_COPIES")"
ARMED_HASHES="$(grep -ao 'arrival-armed #-*[0-9]*' "$ON_LOG" | sed 's/.*#//' | sort -u | tr '\n' ' ' || true)"
HOOKED_HASHES="$(grep -ao 'hook-installed [^ ]* #-*[0-9]*' "$ON_LOG" | sed 's/.*#//' | sort -u | tr '\n' ' ' || true)"
ARM_FAILED="$(grep -a -e 'Concord.Coex\] arrival-failed' -e 'Concord.Coex\] hook-unavailable' "$ON_LOG" | cut -c1-240 | head -3 || true)"
PROBE_MISSING="$(grep -a 'Concord.Coex\] arrival-probe-missing' "$ON_LOG" | cut -c1-240 | head -2 || true)"
LATE_HOOK="$(grep -a 'already built a wrapper' "$ON_LOG" | cut -c1-240 | head -3 || true)"
UNARMED=""
for copy_hash in $PROBE_HASHES; do
  case " $ARMED_HASHES " in
    *" $copy_hash "*) ;;
    *) UNARMED="$UNARMED $copy_hash" ;;
  esac
done

echo
echo "arrival on: harmony copies in the process=$ON_COPIES (count=$ON_COPY_COUNT)"
echo "arrival on: armed=[$ARMED_HASHES] hooked=[$HOOKED_HASHES] from the probe=[$PROBE_HASHES]"
echo "registry on: $ON_CLAIM_COUNT target(s) claimed, $ON_PUBLISHED_COUNT body/bodies published, $ON_POISON_COUNT poisoned"
echo "registry on: claimed targets=$ON_CLAIM_TARGETS"

if [ -z "$ARMED_HASHES" ]; then
  unsure "no arrival-armed marker in the log, so HarmonyArrival never armed in this build and whether the hook beat the first patch cannot be judged"
  grep -a 'Concord.Coex\]' "$ON_LOG" | cut -c1-200 | sort -u | head -8 || true
elif [ -z "$PROBE_HASHES" ]; then
  unsure "the probe reported no 0Harmony copy ('$ON_COPIES'), so there is nothing to compare the armed set against"
elif [ -n "$PROBE_MISSING" ]; then
  fail "the host armed the hook without one of the two probes SuppressionGate needs, so suppression is off everywhere and this host can never keep a body under Harmony: $PROBE_MISSING"
elif [ -n "$ARM_FAILED" ]; then
  fail "a Harmony copy was not hooked, so patches through it land unseen and a body written under them is dead: $ARM_FAILED"
elif [ "$ARMED_HASHES" != "$HOOKED_HASHES" ]; then
  fail "the armed set [$ARMED_HASHES] and the hooked set [$HOOKED_HASHES] name different Harmony copies, so arrival-armed is stamped with a copy it did not hook"
elif [ -n "$UNARMED" ]; then
  fail "the probe sees Harmony copies [$PROBE_HASHES] and [$UNARMED] were never armed, so a copy in this process can patch without the hook seeing it"
elif [ -n "$LATE_HOOK" ]; then
  fail "the gate refused a target because Harmony had already built a wrapper for it, so the hook arrived after that method was taken: $LATE_HOOK"
elif [ "$ON_POISON_COUNT" != "0" ]; then
  fail "WriterRegistry reports $ON_POISON_COUNT claimed target(s) permanently poisoned, so the hook did not get in front of every detour"
else
  pass "every 0Harmony copy the probe can see was armed and hooked under the same hash, no refusal says Harmony got to a method first, and none of the $ON_CLAIM_COUNT claimed targets is poisoned"
fi

ON_HOT_BEFORE="$(field "$ON_LOG" hot through-before)"
ON_HOT_AFTER="$(field "$ON_LOG" hot through-after)"
ON_HOT_LATER="$(field "$ON_LOG" hot through-later)"
ON_HOT_DIRECT="$(field "$ON_LOG" hot direct-after)"
ON_HOT_REVERTED="$(field "$ON_LOG" hot through-reverted)"
ON_HOT_WARMED="$(field "$ON_LOG" hot warmed)"
ON_HOT_INLINING="$(field "$ON_LOG" hot caller-inlining)"
OFF_HOT_AFTER="$(field "$OFF_LOG" hot through-after)"

echo
echo "hot caller on: $ON_HOT_WARMED calls before the weave, inlining into the caller=$ON_HOT_INLINING"
echo "hot caller on: through-before=$ON_HOT_BEFORE through-after=$ON_HOT_AFTER (want 42) through-later=$ON_HOT_LATER direct-after=$ON_HOT_DIRECT through-reverted=$ON_HOT_REVERTED (want 2)"
echo "hot caller off: through-after=$OFF_HOT_AFTER"

if [ -z "$ON_HOT_AFTER" ] || [ "$ON_HOT_BEFORE" != "2" ]; then
  unsure "the hot-caller step did not report a compiled-before-the-weave caller (through-before=$ON_HOT_BEFORE), so nothing was asked of one"
elif [ "$ON_HOT_DIRECT" != "42" ]; then
  unsure "the patch itself did not take on this run (direct-after=$ON_HOT_DIRECT, want 42), so what a pre-compiled caller sees is unanswered"
elif [ "$ON_HOT_AFTER" = "42" ]; then
  pass "a caller JIT-compiled $ON_HOT_WARMED calls before the weave picked the new body up on its very next call, with inlining of the target left $ON_HOT_INLINING"
elif [ "$ON_HOT_LATER" = "42" ]; then
  fail "a caller compiled before the weave kept running the old body ($ON_HOT_AFTER) and only picked the new one up after $ON_HOT_WARMED more calls ($ON_HOT_LATER), so a weave is invisible until the caller is recompiled"
else
  fail "a caller compiled before the weave never saw it: through-after=$ON_HOT_AFTER through-later=$ON_HOT_LATER while a fresh direct call reads $ON_HOT_DIRECT. The target is $ON_HOT_INLINING to inline, so the caller is probably running an inlined copy of the original body"
fi

if [ "$ON_HOT_REVERTED" != "2" ] && [ -n "$ON_HOT_REVERTED" ]; then
  fail "the hot caller reads $ON_HOT_REVERTED after the patch was reverted, not 2, so the probe left a hot game method wrong"
fi

ON_HEAP_BAD="$(field "$ON_LOG" heap bad)"
ON_HEAP_BAD_PRE="$(field "$ON_LOG" heap bad-before-gc)"
ON_HEAP_BLOCKS="$(field "$ON_LOG" heap blocks)"
ON_HEAP_GEN2="$(field "$ON_LOG" heap gen2-collections)"
ON_HEAP_LATE="$(field "$ON_LOG" wovenheld heap-bad)"
OFF_HEAP_BAD="$(field "$OFF_LOG" heap bad)"
FAULTS="$(grep -anE 'SIGSEGV|SIGBUS|SIGILL|SIGABRT|Segmentation fault|Fatal error\. Internal CLR error|double free|heap corruption|corrupted double-linked list|Assertion failed' "$ON_LOG" "$OFF_LOG" 2>/dev/null | head -5 || true)"

echo
echo "heap watch: what is watched is $ON_HEAP_BLOCKS sentinel blocks written before any patch and re-checksummed after a forced blocking compacting gen2 GC taken with a woven body live on a hot game method, plus the two logs for a native fault signature."
echo "heap watch on: bad-before-gc=$ON_HEAP_BAD_PRE bad-after-gc=$ON_HEAP_BAD gen2-collections=$ON_HEAP_GEN2 bad-at-end-of-loading=$ON_HEAP_LATE"
echo "heap watch off: bad-after-gc=$OFF_HEAP_BAD"

if [ -n "$FAULTS" ]; then
  fail "a run died on a native fault, which is what a trip looks like. ConcordLib/Core issue 1 is a heap corruption in the coexistence bridge that has reproduced 4 times in 24 CI runs and 0 times in 103 local runs, so a local hit is worth keeping."
  printf '%s\n' "$FAULTS"
elif [ -z "$ON_HEAP_BAD" ] || [ -z "$OFF_HEAP_BAD" ]; then
  unsure "no heap sentinel reading in one of the runs (on='$ON_HEAP_BAD' off='$OFF_HEAP_BAD'), so the only thing watched was the fault grep and a quiet log is not an answer"
elif [ "$ON_HEAP_BAD" != "0" ] || [ "$OFF_HEAP_BAD" != "0" ] || [ "$ON_HEAP_BAD_PRE" != "0" ] || [ "$ON_HEAP_LATE" != "0" ]; then
  fail "a sentinel block came back changed (on before-gc=$ON_HEAP_BAD_PRE after-gc=$ON_HEAP_BAD end-of-loading=$ON_HEAP_LATE, off after-gc=$OFF_HEAP_BAD). Nothing in this probe writes to those blocks, so something else did."
else
  pass "$ON_HEAP_BLOCKS sentinel blocks survived a forced compacting gen2 GC with a woven body live, in both runs, and no native fault signature is in either log. ConcordLib/Core issue 1 has never reproduced locally (0 of 103), so this is the expected local outcome and is NOT evidence the bug is gone."
fi

if [ "$LEGACY" = 1 ]; then
  echo
  LEGACY_LOADED="$(field "$LEGACY_LOG" late loaded)"
  LEGACY_ERRORS="$(legacy_binding_errors "$LEGACY_LOG" | grep -ac . || true)"
  echo "legacy mod $LEGACY_ID ($LEGACY_ASM.dll): in-AppDomain=$LEGACY_LOADED, binding errors naming $LEGACY_ASM=$LEGACY_ERRORS"
  echo "scoping: a line has to carry a binding exception AND name $LEGACY_ASM, and must not be one of Concord's own backend-fallback lines, which quote BadImageFormatException while explaining what the runtime would have thrown. That misses a failure whose line names neither the assembly nor a type in it."
  echo "control: the same field reads '$ON_LATE_WATCH' in the on-run, which loaded no such mod"

  if ! grep -q '\[RPROBE\] done' "$LEGACY_LOG" 2>/dev/null; then
    unsure "the --legacy run never reached the deferred report, so whether the old mod loads is unanswered"
  elif [ "$ON_LATE_WATCH" != "false" ]; then
    unsure "the watch field reads '$ON_LATE_WATCH' even in a run with no legacy mod, so it cannot tell a loaded mod from an absent one and its answer means nothing"
  elif [ "$LEGACY_LOADED" != "true" ]; then
    fail "$LEGACY_ASM.dll is not in the AppDomain after loading finished, so the 1.6-era mod did NOT load. An empty error count here proves nothing."
    grep -aiE "$LEGACY_ASM" "$LEGACY_LOG" | cut -c1-200 | head -5 || true
  elif [ "${LEGACY_ERRORS:-0}" = 0 ]; then
    pass "the 1.6-era mod built against an older Concord.Ref is loaded in the AppDomain with no binding error"
  else
    unsure "the 1.6-era mod loaded but logged $LEGACY_ERRORS binding error(s) that name $LEGACY_ASM. Read $LEGACY_LOG before blaming Concord: this mod does not declare 1.7 support, so a missing game member is not a Concord result."
    legacy_binding_errors "$LEGACY_LOG" | cut -c1-200 | head -5 || true
  fi
else
  echo
  echo "note  the 1.6-era-mod question was not run. Pass --legacy to run it."
fi

echo
echo "== $PASSED passed, $FAILED failed, $INCONCLUSIVE inconclusive"
if [ "$KEEP_LOGS" = 1 ]; then
  echo "== logs kept at $ON_LOG and $OFF_LOG"
fi

if [ "$FIXTURE" = 1 ]; then
  echo "RESULT: FIXTURE, $PASSED passed and $FAILED failed against logs nobody measured. Not a run."
  exit 4
fi
if [ "$FAILED" -gt 0 ]; then
  echo "RESULT: FAIL"
  exit 1
fi
if [ "$INCONCLUSIVE" -gt 0 ]; then
  echo "RESULT: INCONCLUSIVE"
  exit 2
fi
echo "RESULT: PASS"

#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'TEXT'
run-reach-census.sh [--census-only|--controls-only|--help]

Measures what fraction of the Harmony patch methods in a real mod set Concord's MemberReach would
refuse to mint a token for. Reads metadata only. Launches no game and starts no container.

MemberReach lives in src/Concord.Host.Adapter/MemberReach.cs. It refuses in two places:
  line 19  the member's declaring type, or an enclosing type, is not visible outside its assembly
  line 27  the member itself is private, internal or private protected
Both are escapable only by an InternalsVisibleTo grant from the owning assembly to the consumer,
and the consumer here is the game module the replaced body is written into.

The census reimplements that predicate over dnlib metadata, because a reflection load of a 1.6-era
mod assembly in net10 cannot resolve its references and a MetadataLoadContext cannot instantiate
attributes, which is the path Reaches() takes. Check 1 is what makes the reimplementation worth
trusting: it runs the real MemberReach.Explain against the census verdict, method for method, on a
fixture that populates every cell.

One version is picked per mod, the one holding the highest-numbered Assemblies directory, and then
EVERY dll under that version folder is read, not only the ones under Assemblies. Three mods ship
their real code in <version>/Lunar/Components with a loader stub in Assemblies; reading Assemblies
alone missed 195 patch methods, all of them refused, and reported 35.3% instead of 38.2%.

Checks, most important first:
  1. equivalence: real MemberReach.Explain and the census classifier agree on every fixture method,
     and the fixture produces every verdict rather than one repeated. A run where the fixture only
     ever produced "Allowed" would prove nothing, so a single-verdict fixture FAILS here.
  2. negative control, detector: the same fixture source compiled with the attributes moved to a
     namespace that is not HarmonyLib must yield zero patch methods. Same types, same modifiers,
     same method names: only the attribute namespace differs.
  3. negative control, real assemblies: three shipped assemblies with no Harmony in them must yield
     zero. The check refuses to read zero as a pass unless it also sees one assembly scanned, so an
     excluded or unreadable file cannot pass by scanning nothing.
  4. positive control, real assembly: CommonSense must yield patch methods in more than one cell.
  5. per-class control: on the fixture, the refused fraction of patch classes (whole type hidden)
     must differ from the refused fraction of patch methods. A class is refused on the type check
     alone, a method is refused on the type check or its own modifier, so the two fractions are
     different questions and must come out as different numbers.
  6. the census itself over the workshop set and the local Mods set.

Exit: 0 all checks passed, 1 a check failed, 3 setup failed.

Flags:
  --census-only    skip checks 1-5 and just recount
  --controls-only  run checks 1-5 and stop

Env:
  CONCORD_RIMWORLD_WORKSHOP=<dir>   directory of workshop mod directories
  CONCORD_RIMWORLD_MODS=<dir>       the install's Mods directory
  CONCORD_RIMWORLD_INSTALL=<dir>    install root, for the check 3 assemblies
TEXT
}

MODE=all
for arg in "$@"; do
  case "$arg" in
    --help|-h) usage; exit 0 ;;
    --census-only) MODE=census ;;
    --controls-only) MODE=controls ;;
    *) echo "unknown argument '$arg'. --help lists them." >&2; exit 3 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CENSUS="$SCRIPT_DIR/reach-census"
OUT="$CENSUS/out"
INSTALL="${CONCORD_RIMWORLD_INSTALL:-/mnt/games/SteamLibrary/steamapps/common/RimWorld}"
WORKSHOP="${CONCORD_RIMWORLD_WORKSHOP:-/mnt/games/SteamLibrary/steamapps/workshop/content/294100}"
MODS="${CONCORD_RIMWORLD_MODS:-$INSTALL/Mods}"
TOOL="$CENSUS/Source/bin/census/ReachCensus.dll"
FIXTURE="$CENSUS/Fixture/bin/fixture/ReachCensusFixture.dll"
BLANK="$CENSUS/Fixture/bin/blank/ReachCensusFixtureBlank.dll"
POSITIVE="$WORKSHOP/1561769193/1.6/Assemblies/CommonSense.dll"

FAILED=0
note() { printf '%s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILED=1; }
pass() { printf 'pass  %s\n' "$*"; }

mkdir -p "$OUT"

note "== setup"
for dir in "$INSTALL" "$WORKSHOP" "$MODS"; do
  if [ ! -d "$dir" ]; then
    echo "not a directory: $dir" >&2
    exit 3
  fi
done

if ! dotnet build "$CENSUS/Fixture/ReachCensusFixture.csproj" -v:q --nologo > "$OUT/build-fixture.log" 2>&1; then
  tail -20 "$OUT/build-fixture.log" >&2
  exit 3
fi
if ! dotnet build "$CENSUS/Fixture/ReachCensusFixture.csproj" -p:FixtureAttributeNamespace=NotHarmonyLib \
    -v:q --nologo > "$OUT/build-blank.log" 2>&1; then
  tail -20 "$OUT/build-blank.log" >&2
  exit 3
fi
if ! dotnet build "$CENSUS/Source/ReachCensus.csproj" -v:q --nologo > "$OUT/build-census.log" 2>&1; then
  tail -20 "$OUT/build-census.log" >&2
  exit 3
fi
note "built the census tool, the fixture and the blank fixture"

field() { awk -F'\t' -v k="$1" '$1==k{print $2; exit}' "$2"; }

if [ "$MODE" != census ]; then
  note ""
  note "== 1. equivalence against the real MemberReach"
  if dotnet "$TOOL" verify "$FIXTURE" > "$OUT/verify.log" 2>&1; then
    cat "$OUT/verify.log"
    pass "every fixture method agrees, and the fixture produced $(field 'distinct verdicts produced by the fixture' "$OUT/verify.log") distinct verdicts"
  else
    cat "$OUT/verify.log"
    fail "the census classifier and MemberReach.Explain disagree, or the fixture does not discriminate"
  fi

  note ""
  note "== 2. negative control: the same source with the attributes renamed"
  dotnet "$TOOL" files "$BLANK" --expect-none > "$OUT/blank.log" 2>&1 || true
  BLANK_SCANNED="$(field 'assemblies scanned' "$OUT/blank.log")"
  BLANK_PATCHES="$(field 'patch methods' "$OUT/blank.log")"
  if [ "${BLANK_SCANNED:-0}" = 1 ] && [ "${BLANK_PATCHES:-x}" = 0 ]; then
    pass "scanned 1 assembly, found 0 patch methods"
  else
    fail "expected 1 scanned and 0 patch methods, got scanned=${BLANK_SCANNED:-none} patches=${BLANK_PATCHES:-none}"
  fi

  note ""
  note "== 3. negative control: three shipped assemblies with no Harmony in them"
  for name in RimWorld.dll System.Text.Json.dll Waylith.dll; do
    if [ ! -f "$INSTALL/$name" ]; then
      fail "$name is not in $INSTALL, so this control did not run"
      continue
    fi
    dotnet "$TOOL" files "$INSTALL/$name" --expect-none > "$OUT/none-$name.log" 2>&1 || true
    SCANNED="$(field 'assemblies scanned' "$OUT/none-$name.log")"
    PATCHES="$(field 'patch methods' "$OUT/none-$name.log")"
    if [ "${SCANNED:-0}" = 1 ] && [ "${PATCHES:-x}" = 0 ]; then
      pass "$name: scanned 1, found 0"
    else
      fail "$name: expected 1 scanned and 0 patch methods, got scanned=${SCANNED:-none} patches=${PATCHES:-none}"
    fi
  done

  note ""
  note "== 4. positive control: a real mod assembly"
  if [ ! -f "$POSITIVE" ]; then
    fail "CommonSense is not at $POSITIVE, so this control did not run"
  else
    dotnet "$TOOL" files "$POSITIVE" --out "$OUT/positive.tsv" > "$OUT/positive.log" 2>&1 || true
    POS_PATCHES="$(field 'patch methods' "$OUT/positive.log")"
    POS_CELLS="$(awk -F'\t' 'NR>1{print $6"/"$8}' "$OUT/positive.tsv" | sort -u | wc -l)"
    if [ "${POS_PATCHES:-0}" -gt 0 ] && [ "$POS_CELLS" -gt 1 ]; then
      pass "found $POS_PATCHES patch methods across $POS_CELLS visibility cells"
    else
      fail "expected patch methods in more than one cell, got patches=${POS_PATCHES:-none} cells=$POS_CELLS"
    fi
  fi

  note ""
  note "== 5. per-class control: a known-different answer on the fixture"
  CLASS_COUNT="$(field 'fixture patch classes, mintable roles' "$OUT/verify.log")"
  CLASS_HIDDEN="$(field 'fixture patch classes type-hidden, mintable roles' "$OUT/verify.log")"
  CLASS_FRACTION="$(field 'fixture patch classes refused fraction, mintable roles' "$OUT/verify.log")"
  METHOD_FRACTION="$(field 'fixture patch methods refused fraction, mintable roles' "$OUT/verify.log")"
  if [ "${CLASS_COUNT:-0}" = 12 ] && [ "${CLASS_HIDDEN:-0}" = 3 ] \
      && [ "${CLASS_FRACTION:-x}" != "${METHOD_FRACTION:-y}" ]; then
    pass "classes refused $CLASS_FRACTION of $CLASS_COUNT, methods refused $METHOD_FRACTION: different populations, different numbers"
  else
    fail "expected 12 classes, 3 of them type-hidden, and a class fraction different from the method fraction; got classes=${CLASS_COUNT:-none} hidden=${CLASS_HIDDEN:-none} classFraction=${CLASS_FRACTION:-none} methodFraction=${METHOD_FRACTION:-none}"
  fi
fi

if [ "$MODE" != controls ]; then
  note ""
  note "== 6. census: the workshop set"
  dotnet "$TOOL" mods "$WORKSHOP" --out "$OUT/workshop.tsv" | tee "$OUT/workshop.log"

  note ""
  note "== 6. census: the install Mods set"
  dotnet "$TOOL" mods "$MODS" --out "$OUT/local.tsv" | tee "$OUT/local.log"
fi

note ""
if [ "$FAILED" = 0 ]; then
  note "all checks passed"
  exit 0
fi

note "a check failed; the numbers above are not trustworthy"
exit 1

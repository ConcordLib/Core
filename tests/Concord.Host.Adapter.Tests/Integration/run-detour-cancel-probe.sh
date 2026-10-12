#!/usr/bin/env bash
set -uo pipefail

usage() {
  cat <<'TEXT'
run-detour-cancel-probe.sh [--self-test|--raw|--help]

Answers one question: if something cancels HarmonyLib.PatchTools.DetourMethod for a patch install,
does Harmony's own api stay honest about that patch? Launches no game and starts no container. A
plain net10 console host loads the real net10 Harmony 2.4.2 that RimWorld 1.7 ships, patches
DetourMethod with a prefix that returns false, and then reads Harmony back through its own api.

The cancel is a Harmony prefix on DetourMethod because the mechanism does not matter to the
question: Harmony's bookkeeping cannot tell who stopped the detour. That also means DetourMethod
itself shows up in GetAllPatchedMethods and in PatchTools.detours, which check 2 accounts for.

Every answer is paired with the same reading taken on a known-different input, so a check that
cannot say DISHONEST never gets to say honest:

  1. gate: the hook fires and the cancel does something. Cancel off, a patched method returns the
     patched value and a detour exists; cancel on, the same shape of method returns its original
     value and no detour exists. Checks 2-7 are not evaluated unless this passes, because with a
     dead hook every "stays honest" below is just an unpatched Harmony answering normally.
  2. GetPatchInfo (question 1). Control: a method nothing ever patched reads back null.
  3. GetAllPatchedMethods (question 2). Control: a never-patched method is absent. Unpatch leaves
     a method listed with zero patches whether the cancel is on or off, so the two are compared
     rather than blamed on the cancel.
  4. GetOriginalMethod / HarmonySharedState.GetRealMethod (question 4). Those return their own
     argument when they find no mapping, so the reading is "did the answer differ from the
     argument". Control: an unmapped method reads back as itself.
  5. Unpatch (question 3), and what it installs. Control: the same unpatch with the cancel off.
  6. ReversePatch (question 5), the other caller, against three cancel-site predicates:
     cancel everything, cancel only what carries no [HarmonyReversePatch], cancel only methods in
     an owned set. Control: the same reverse patch with the cancel off.
  7. the skipped Dispose (question 6). Patch once with the cancel off, again with it on, and read
     what the method actually runs against what Harmony says is patched. Control: the identical
     two-patch sequence with the cancel off all the way through.

Exit: 0 all checks passed, 1 a check failed, 3 setup failed.

Flags:
  --self-test  run the checks against two doctored copies of a real run and confirm they FAIL the
               checks that were doctored. A checker that cannot fail has measured nothing.
  --raw        print the probe's whole tsv after the checks

Env:
  CONCORD_HARMONY_DIR=<dir>  directory holding the net10 0Harmony.dll to measure
TEXT
}

MODE=all
RAW=0
for arg in "$@"; do
  case "$arg" in
    --help|-h) usage; exit 0 ;;
    --self-test) MODE=self-test ;;
    --raw) RAW=1 ;;
    *) echo "unknown argument '$arg'. --help lists them." >&2; exit 3 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROBE="$SCRIPT_DIR/detour-cancel"
OUT="$PROBE/out"
PROJ="$PROBE/Source/DetourCancelProbe.csproj"
TOOL="$PROBE/Source/bin/probe/DetourCancelProbe.dll"
HARMONY_DIR="${CONCORD_HARMONY_DIR:-/mnt/games/RimWorld/Mods/Harmony-net10/1.7/Assemblies}"

FAILED=0
TSV=""
note() { printf '%s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILED=1; }
pass() { printf 'pass  %s\n' "$*"; }
SELF=0
selffail() { printf 'FAIL  %s\n' "$*"; SELF=1; }

field() { awk -F'\t' -v k="$1" '$1==k{print $2; exit}' "$TSV"; }

expect() {
  local key="$1" want="$2" why="$3" got
  got="$(field "$key")"
  if [ -z "$got" ]; then
    fail "$key is not in the probe output, so this check did not run ($why)"
    return
  fi
  if [ "$got" = "$want" ]; then
    pass "$key = $got  ($why)"
  else
    fail "$key = $got, expected $want  ($why)"
  fi
}

expect_same() {
  local a="$1" b="$2" why="$3" ga gb
  ga="$(field "$a")"
  gb="$(field "$b")"
  if [ -z "$ga" ] || [ -z "$gb" ]; then
    fail "$a or $b is not in the probe output, so this check did not run ($why)"
    return
  fi
  if [ "$ga" = "$gb" ]; then
    pass "$a = $b = $ga  ($why)"
  else
    fail "$a = $ga but $b = $gb  ($why)"
  fi
}

gate_ok() {
  [ "$(field gate.control.after)" = 42 ] &&
  [ "$(field gate.cancel.after)" = 1 ] &&
  [ "$(field gate.control.detour.present)" = 1 ] &&
  [ "$(field gate.cancel.detour.present)" = 0 ] &&
  [ "$(field gate.control.hook.hits.delta)" = 1 ] &&
  [ "$(field gate.cancel.hook.cancels.delta)" = 1 ]
}

evaluate() {
  TSV="$1"
  FAILED=0

  note "== 1. gate: the hook fires and the cancel does something"
  expect seam.is.void 1 "DetourMethod returns void, so a cancel has no return value to fabricate"
  expect probe.complete 1 "the host reached the end"
  expect gate.control.before 1 "unpatched value"
  expect gate.control.after 42 "cancel off: the patch takes effect"
  expect gate.control.detour.present 1 "cancel off: a detour exists for the target"
  expect gate.control.hook.hits.delta 1 "the hook saw exactly one DetourMethod call per install"
  expect gate.cancel.before 1 "unpatched value"
  expect gate.cancel.after 1 "cancel on: the patch does NOT take effect"
  expect gate.cancel.detour.present 0 "cancel on: no detour exists for the target"
  expect gate.cancel.hook.cancels.delta 1 "the hook cancelled exactly one install"

  if ! gate_ok; then
    note ""
    note "the gate failed, so checks 2-7 were not evaluated: with a dead hook or a cancel that"
    note "does nothing, every api reading below would be an ordinary Harmony answer."
    return 1
  fi

  note ""
  note "== 2. question 1: GetPatchInfo"
  expect q1.cancel.prefixes 1 "cancelled install: the prefix is still reported"
  expect q1.control.prefixes 1 "cancel off: same reading"
  expect_same q1.cancel.owners q1.control.owners "the owning harmony id is reported either way"
  expect q1.neverpatched.patchinfo null "control: a never-patched method has no patch info"

  note ""
  note "== 3. question 2: GetAllPatchedMethods"
  expect q2.cancel.listed 1 "cancelled install: the target is still listed"
  expect q2.control.listed 1 "cancel off: same reading"
  expect q2.neverpatched.listed 0 "control: a never-patched method is not listed"
  expect q2.detourmethod.listed 1 "the hook's own target is in the list and is counted here"

  note ""
  note "== 4. question 4: GetOriginalMethod and GetRealMethod"
  expect q4.cancel.getoriginal.is.target 1 "cancelled install: the replacement still maps to the target"
  expect q4.cancel.getoriginal.is.identity 0 "cancelled install: the answer is a real mapping, not the argument"
  expect q4.control.getoriginal.is.identity 0 "cancel off: same reading"
  expect q4.unrelated.getoriginal.is.identity 1 "control: an unmapped method reads back as itself"
  expect q4.cancel.getrealmethod.is.identity 0 "cancelled install: GetRealMethod has a mapping too"
  expect q4.unrelated.getrealmethod.is.identity 1 "control: GetRealMethod falls back to its argument"

  note ""
  note "== 5. question 3: Unpatch, and what it installs"
  expect q3.cancel.patched.detour 0 "cancelled install left no detour to undo"
  expect q3.cancel.unpatch.cancels.delta 1 "unpatch calls DetourMethod again and that call was cancelled too"
  expect q3.cancel.unpatched.prefixes 0 "the patch is removed from the bookkeeping"
  expect q3.cancel.unpatched.behaviour 1 "the method runs its own body, which is what unpatch wanted"
  expect q3.cancel.unpatched.detour 0 "nothing was installed, because the call was cancelled"
  expect q3.control.unpatched.detour 1 "cancel off: unpatch INSTALLS a detour to a patch-free wrapper"
  expect q3.control.unpatched.detour.replaced 1 "cancel off: and it is a different detour than the patched one"
  expect q3.thenoff.unpatched.detour 1 "cancel on for the patch, off for the unpatch: a detour appears on a method that never had one"
  expect_same q3.cancel.unpatched.listed q3.control.unpatched.listed \
    "after unpatch a method stays listed with zero patches either way, so that is Harmony, not the cancel"

  note ""
  note "== 6. question 5: ReversePatch, the other caller"
  expect q5.control.standin 77 "control: cancel off, the standin runs the source's body"
  expect q5.blanket.standin.bare -1 "cancel everything: a manual reverse patch is DEAD, the standin runs its own body"
  expect q5.blanket.standin.attrdriven -1 "cancel everything: an attribute-driven reverse patch is DEAD too"
  expect q5.attronly.standin.attributed 79 "cancel unless [HarmonyReversePatch]: an attributed standin survives"
  expect q5.attronly.standin.bare -1 "cancel unless [HarmonyReversePatch]: a standin without the attribute still DIES"
  expect q5.attronly.updatewrapper.target 1 "cancel unless [HarmonyReversePatch]: an ordinary patch is still cancelled"
  expect q5.owned.standin 81 "cancel only an owned set: the reverse patch survives"
  expect q5.owned.updatewrapper.target 1 "cancel only an owned set: the owned ordinary patch is cancelled"
  expect q5.owned.target.detour 0 "cancel only an owned set: and it left no detour"
  expect q5.frames.unresolved 0 "every cancel-site call resolved an UpdateWrapper or ReversePatch stack frame"

  note ""
  note "== 7. question 6: the Dispose inside DetourMethod that a cancel skips"
  expect q6.control.p1.behaviour 42 "control: prefix only"
  expect q6.control.p2.behaviour 142 "control: cancel off, the second patch composes onto the first"
  expect q6.p1.behaviour 42 "cancel off for the first patch, so it takes effect"
  expect q6.p2.prefixes 1 "bookkeeping after the cancelled second install"
  expect q6.p2.postfixes 1 "bookkeeping says the second patch is there"
  expect q6.p2.behaviour 42 "but the method still runs the FIRST wrapper only: bookkeeping and behaviour disagree"
  expect q6.p2.detour.id.unchanged 1 "the old detour object is still the one in PatchTools.detours, undisposed"
  expect q6.stale.is.disposable 1 "that object is an IDisposable, so it can be freed from outside"
  expect q6.after.dispose.behaviour 1 "disposing it by hand puts the method back on its own body"
  expect q6.after.dispose.postfixes 1 "while Harmony still reports both patches: now nothing runs"
  expect q6.after.dispose.detour.key 1 "and PatchTools.detours still holds the dead reference"
  expect q6.repatch.threw none "a later uncancelled install re-disposes that dead detour without throwing"
  expect q6.repatch.behaviour 142 "and the method composes again"

  return $FAILED
}

if [ "$MODE" != self-test ]; then
  note "== setup"
  if [ ! -f "$HARMONY_DIR/0Harmony.dll" ]; then
    echo "0Harmony.dll not found in $HARMONY_DIR. Set CONCORD_HARMONY_DIR." >&2
    exit 3
  fi
  mkdir -p "$OUT"
  if ! dotnet build "$PROJ" -p:HarmonyDir="$HARMONY_DIR" -v:q --nologo > "$OUT/build.log" 2>&1; then
    tail -20 "$OUT/build.log" >&2
    exit 3
  fi
  note "built the probe host against $HARMONY_DIR/0Harmony.dll"
fi

mkdir -p "$OUT"
if [ ! -f "$TOOL" ]; then
  echo "probe host not built at $TOOL" >&2
  exit 3
fi

dotnet "$TOOL" > "$OUT/run.tsv" 2> "$OUT/run.err"
RUN_STATUS=$?
if [ "$RUN_STATUS" != 0 ]; then
  echo "the probe host exited $RUN_STATUS" >&2
  tail -20 "$OUT/run.err" >&2
  tail -20 "$OUT/run.tsv" >&2
  exit 3
fi
note "harmony $(awk -F'\t' '$1=="harmony.version"{print $2}' "$OUT/run.tsv") at $HARMONY_DIR"
note ""

if [ "$MODE" = self-test ]; then
  note "== self-test: doctor the gate, the checks must refuse to evaluate anything"
  sed 's/^gate\.cancel\.after\t1$/gate.cancel.after\t42/' "$OUT/run.tsv" > "$OUT/doctored-gate.tsv"
  evaluate "$OUT/doctored-gate.tsv" > "$OUT/self-gate.log" 2>&1
  GATE_STATUS=$?
  if [ "$GATE_STATUS" = 0 ]; then
    selffail "a doctored gate still passed"
  elif ! grep -q 'checks 2-7 were not evaluated' "$OUT/self-gate.log"; then
    selffail "a doctored gate failed but the checks still evaluated"
  else
    pass "doctored gate.cancel.after: $(grep -c '^FAIL' "$OUT/self-gate.log") failure(s), checks 2-7 refused"
  fi

  note ""
  note "== self-test: doctor one reading per question, each must be reported FAIL"
  sed -e 's/^q1\.neverpatched\.patchinfo\tnull$/q1.neverpatched.patchinfo\tpresent/' \
      -e 's/^q2\.neverpatched\.listed\t0$/q2.neverpatched.listed\t1/' \
      -e 's/^q3\.cancel\.unpatched\.detour\t0$/q3.cancel.unpatched.detour\t1/' \
      -e 's/^q4\.unrelated\.getoriginal\.is\.identity\t1$/q4.unrelated.getoriginal.is.identity\t0/' \
      -e 's/^q5\.blanket\.standin\.bare\t-1$/q5.blanket.standin.bare\t78/' \
      -e 's/^q6\.p2\.behaviour\t42$/q6.p2.behaviour\t142/' \
      "$OUT/run.tsv" > "$OUT/doctored-answers.tsv"
  evaluate "$OUT/doctored-answers.tsv" > "$OUT/self-answers.log" 2>&1
  for key in q1.neverpatched.patchinfo q2.neverpatched.listed q3.cancel.unpatched.detour \
             q4.unrelated.getoriginal.is.identity q5.blanket.standin.bare q6.p2.behaviour; do
    if grep -q "^FAIL  $key = " "$OUT/self-answers.log"; then
      pass "$key reported FAIL on the doctored input"
    else
      selffail "$key did not report FAIL on the doctored input, so that check cannot discriminate"
    fi
  done

  note ""
  note "== self-test: a missing reading must fail, not pass by reading empty"
  grep -v '^q6\.p2\.behaviour' "$OUT/run.tsv" > "$OUT/doctored-missing.tsv"
  evaluate "$OUT/doctored-missing.tsv" > "$OUT/self-missing.log" 2>&1
  if grep -q 'q6.p2.behaviour is not in the probe output' "$OUT/self-missing.log"; then
    pass "a deleted reading is reported as not run"
  else
    selffail "a deleted reading did not fail the check"
  fi

  note ""
  if [ "$SELF" = 0 ]; then
    note "the checks can report DISHONEST, so a pass from them means something"
    exit 0
  fi
  note "the checks cannot discriminate; the numbers they print are worthless"
  exit 1
fi

evaluate "$OUT/run.tsv"
RESULT=$?

if [ "$RAW" = 1 ]; then
  note ""
  note "== raw"
  cat "$OUT/run.tsv"
fi

note ""
if [ "$RESULT" = 0 ]; then
  note "all checks passed. run --self-test to see the same checks report FAIL on doctored input."
  exit 0
fi
note "a check failed; the answers above are not trustworthy"
exit 1

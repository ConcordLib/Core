#!/usr/bin/env bash
set -uo pipefail

CONFIG="${CONFIG:-Debug}"
cd "$(dirname "$0")/.."

OUT="${OUT:-artifacts/bodyreader-probe}"
PROJ=tests/Concord.BodyReader.Probe
CORE_EXE="$PROJ/bin/$CONFIG/net10.0/Concord.BodyReader.Probe"
MONO_EXE="$PROJ/bin/$CONFIG/net472/Concord.BodyReader.Probe.exe"

dotnet build "$PROJ/Concord.BodyReader.Probe.csproj" -c "$CONFIG" -v q --nologo >/dev/null \
    || { echo "the probe would have run against a stale Concord.Emit; build failed" >&2; exit 1; }

SHAPES=(
    selftest-ok selftest-throw selftest-abort selftest-neverran
    Plain
    FnPtrLocal FnPtrParam FnPtrReturn FnPtrArrayLocal FnPtrPointerLocal FnPtrPinnedLocal
    UnmanagedCallConvFnPtr CalliNoFnPtrLocal FnPtrField
    FnPtrStructByValParam FnPtrStructReturn FnPtrStructLocal NestedFnPtrStructLocal FnPtrClassFieldLocal
    PointerLocal PointerParam PointerReturn VoidPointerLocal
    ByRefLocal RefStructLocal PinnedLocal Stackalloc
    TypedReferenceLocal Arglist ArgIteratorLocal
    ModreqInParam ModreqRefReadonlyReturn VolatileModreq
    GenericPointerLocal GenericPointerLocalClosed GenericFnPtrLocal GenericFnPtrLocalClosed
    GenericValueTupleTryCatch DynamicAssemblyLocal
)
PROBES=(resolve getbody localcount body locals params sigkind localvt sig cecil cecilops dmdctor dmd dmddef dmdgen clone)

HARMONY_DLL="${HARMONY_DLL:-$HOME/.nuget/packages/lib.harmony/2.4.2/lib/net472/0Harmony.dll}"
[[ -f "$HARMONY_DLL" ]] && SHAPES+=("ext:$HARMONY_DLL|HarmonyLib.PatchFunctions|UpdateWrapper")

legs=(core mono)
[[ $# -gt 0 ]] && legs=("$@")

rm -rf "$OUT"
mkdir -p "$OUT"
LEDGER="$OUT/ledger.tsv"
RESULTS="$OUT/results.tsv"
: >"$LEDGER"
: >"$RESULTS"

run_leg() {
    local leg="$1"
    local logs="$OUT/$leg"
    mkdir -p "$logs"

    for shape in "${SHAPES[@]}"; do
        for probe in "${PROBES[@]}"; do
            local cell="$logs/${shape//[^A-Za-z0-9:.-]/_}.$probe"
            printf 'START\t%s\t%s\t%s\n' "$leg" "$shape" "$probe" >>"$LEDGER"

            if [[ "$leg" == mono ]]; then
                MONO_CRASH_NOFILE=1 timeout 60 mono "$MONO_EXE" "$shape" "$probe" "$cell.marker" >"$cell.log" 2>&1
            else
                timeout 60 "$CORE_EXE" "$shape" "$probe" "$cell.marker" >"$cell.log" 2>&1
            fi
            local rc=$?

            local entered=no
            [[ -f "$cell.marker" ]] && grep -q $'^ENTER\t' "$cell.marker" && entered=yes
            local result
            result=$(grep $'^RESULT\t' "$cell.marker" 2>/dev/null | head -1)

            local verdict detail
            if [[ -n "$result" ]]; then
                verdict=$(cut -f2 <<<"$result")
                detail=$(cut -f3- <<<"$result")
            elif [[ "$entered" == no ]]; then
                verdict=NEVER_RAN
                detail="rc=$rc; $(head -c 200 "$cell.log" | tr '\n\t' '  ')"
            elif [[ "$rc" -eq 124 ]]; then
                verdict=TIMEOUT
                detail="rc=124 after 60s"
            elif [[ "$rc" -gt 128 ]]; then
                verdict=SIGNAL
                detail="signal $((rc - 128)); $(head -c 200 "$cell.log" | tr '\n\t' '  ')"
            elif [[ "$rc" -ne 0 ]]; then
                verdict=DIED
                detail="rc=$rc; $(head -c 200 "$cell.log" | tr '\n\t' '  ')"
            else
                verdict=SILENT
                detail="rc=0, entered, no RESULT"
            fi

            printf 'END\t%s\t%s\t%s\t%s\trc=%s\n' "$leg" "$shape" "$probe" "$verdict" "$rc" >>"$LEDGER"
            printf '%s\t%s\t%s\t%s\t%s\n' "$leg" "$shape" "$probe" "$verdict" "$detail" >>"$RESULTS"
            printf '%-5s %-26s %-8s %s\n' "$leg" "$shape" "$probe" "$verdict"
        done
    done
}

for leg in "${legs[@]}"; do
    case "$leg" in
        mono) command -v mono >/dev/null || { echo "mono is not installed" >&2; exit 1; }
              [[ -f "$MONO_EXE" ]] || { echo "build $PROJ for net472 first" >&2; exit 1; } ;;
        core) [[ -x "$CORE_EXE" ]] || { echo "build $PROJ for net10.0 first" >&2; exit 1; } ;;
        *) echo "unknown leg '$leg' (want: core, mono)" >&2; exit 1 ;;
    esac
    run_leg "$leg"
done

expected=$(( ${#SHAPES[@]} * ${#PROBES[@]} * ${#legs[@]} ))
started=$(grep -c $'^START\t' "$LEDGER" || true)
ended=$(grep -c $'^END\t' "$LEDGER" || true)
echo
echo "cells expected: $expected"
echo "cells started:  $started"
echo "cells ended:    $ended"
echo "ledger: $LEDGER"
echo "results: $RESULTS"

status=0
if [[ "$started" -ne "$expected" ]]; then
    echo "MISSING: $((expected - started)) cells never started" >&2
    status=1
fi
if [[ "$ended" -ne "$started" ]]; then
    echo "MISSING: $((started - ended)) cells started and never ended" >&2
    grep $'^START\t' "$LEDGER" | cut -f2-4 | sort >"$OUT/.started"
    grep $'^END\t' "$LEDGER" | cut -f2-4 | sort >"$OUT/.ended"
    comm -23 "$OUT/.started" "$OUT/.ended" >&2
    rm -f "$OUT/.started" "$OUT/.ended"
    status=1
fi
exit "$status"

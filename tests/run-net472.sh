#!/usr/bin/env bash
# Run the net472 test assemblies on Linux.
#
# `dotnet test` cannot do this: its net472 path wants Extensions/TestHostNetFramework/
# testhost.net472.exe, which the Linux SDK does not ship, so every net472 cell aborts with
# "Cannot open assembly ... testhost.net472.exe". That is a missing host, not a real failure,
# and it means the net472 surface is otherwise only ever exercised in CI.
#
# xunit's own net4x console runner under mono has no such dependency.
#
#   ./tests/run-net472.sh            # every net472 test assembly
#   ./tests/run-net472.sh Detour     # only assemblies matching "Detour"
#   CONFIG=Release ./tests/run-net472.sh
set -uo pipefail

CONFIG="${CONFIG:-Debug}"

cd "$(dirname "$0")/.."

command -v mono >/dev/null || { echo "mono is not installed" >&2; exit 1; }

RUNNER=$(find ~/.nuget/packages/xunit.runner.console -path '*net4*' -name 'xunit.console.exe' 2>/dev/null | sort | tail -1)
[[ -n "$RUNNER" ]] || { echo "xunit.runner.console net4x runner not restored; run a net472 build first" >&2; exit 1; }

filter="${1:-}"
total=0
failed=0
ran=0

# Pinned to one configuration: globbing bin/*/net472 picks up stale builds from the other one
# and silently runs the same assembly twice with different results.
for dir in tests/*/bin/"$CONFIG"/net472; do
    dll=$(ls "$dir"/*.Tests.dll 2>/dev/null | head -1) || continue
    [[ -n "$dll" ]] || continue
    name=$(basename "$dll")
    [[ -n "$filter" ]] && [[ "$name" != *"$filter"* ]] && continue

    line=$( (cd "$dir" && mono "$RUNNER" "$name" -parallel none 2>&1) | grep -oE "Total: [0-9]+, Errors: [0-9]+, Failed: [0-9]+" | tail -1)

    if [[ -z "$line" ]]; then
        echo "FAIL  $name (runner produced no summary)"
        failed=$((failed + 1))
        continue
    fi

    ran=$((ran + 1))
    t=$(echo "$line" | sed -E 's/Total: ([0-9]+).*/\1/')
    f=$(echo "$line" | sed -E 's/.*Failed: ([0-9]+)/\1/')
    e=$(echo "$line" | sed -E 's/.*Errors: ([0-9]+),.*/\1/')
    total=$((total + t))
    failed=$((failed + f + e))

    printf '%-6s %-42s %s\n' "$([[ "$((f + e))" -eq 0 ]] && echo ok || echo FAIL)" "$name" "$line"
done

echo
echo "net472: $total tests across $ran assemblies, $failed failed"
[[ "$failed" -eq 0 ]]

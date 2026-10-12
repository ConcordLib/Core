#!/usr/bin/env bash
set -u

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$script_dir/Concord.Host.Adapter.ForkTests.csproj"
configuration="${CONFIGURATION:-Release}"
tfm="net10.0"
rid="linux-x64"

dotnet build "$project" --nologo -c "$configuration" -tl:false "$@"
build_status=$?
if [ "$build_status" -ne 0 ]; then
    echo "run-fork-tests: build failed with exit $build_status" >&2
    exit "$build_status"
fi

exe="$script_dir/bin/$configuration/$tfm/$rid/Concord.Host.Adapter.ForkTests"
if [ ! -x "$exe" ]; then
    echo "run-fork-tests: build succeeded but no exe at $exe" >&2
    exit 1
fi

output="$("$exe")"
run_status=$?
printf '%s\n' "$output"

summary="$(printf '%s\n' "$output" | sed 's/\x1b\[[0-9;]*m//g' | grep -o 'Total: [0-9]*, Errors: [0-9]*, Failed: [0-9]*, Skipped: [0-9]*' | tail -1)"
if [ -z "$summary" ]; then
    echo "run-fork-tests: no test summary line in the run output" >&2
    exit 1
fi

total=$(printf '%s' "$summary" | sed -n 's/.*Total: \([0-9]*\).*/\1/p')
errors=$(printf '%s' "$summary" | sed -n 's/.*Errors: \([0-9]*\).*/\1/p')
failed=$(printf '%s' "$summary" | sed -n 's/.*Failed: \([0-9]*\).*/\1/p')
skipped=$(printf '%s' "$summary" | sed -n 's/.*Skipped: \([0-9]*\).*/\1/p')
passed=$((total - errors - failed - skipped))

echo "run-fork-tests: passed=$passed failed=$((errors + failed)) skipped=$skipped total=$total"

if [ "$run_status" -ne 0 ]; then
    echo "run-fork-tests: exe exited $run_status" >&2
    exit "$run_status"
fi

if [ "$total" -eq 0 ] || [ "$skipped" -eq "$total" ]; then
    echo "run-fork-tests: every fact skipped, the fork overlay did not take" >&2
    exit 1
fi

exit 0

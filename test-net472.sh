#!/usr/bin/env bash
set -uo pipefail

config="${1:-Debug}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v mono >/dev/null; then
    echo "mono is not installed. the net472 lane cannot run without it." >&2
    exit 1
fi

runner="$(ls -1d "$HOME"/.nuget/packages/xunit.runner.console/*/tools/net472/xunit.console.exe 2>/dev/null | sort -V | tail -1)"

if [ -z "$runner" ]; then
    echo "xunit.console.exe not found. run: dotnet restore, or nuget install xunit.runner.console" >&2
    exit 1
fi

mapfile -t suites < <(ls -1 "$root"/tests/*/bin/"$config"/net472/*.Tests.dll 2>/dev/null)

if [ "${#suites[@]}" -eq 0 ]; then
    echo "no net472 test assemblies under tests/*/bin/$config/net472. build first: dotnet build Concord.slnx -c $config" >&2
    exit 1
fi

failed=()

for suite in "${suites[@]}"; do
    name="$(basename "$suite" .dll)"
    echo "== $name"
    (cd "$(dirname "$suite")" && mono "$runner" "$(basename "$suite")" -parallel none) || failed+=("$name")
done

if [ "${#failed[@]}" -gt 0 ]; then
    echo
    echo "net472 failures: ${failed[*]}" >&2
    exit 1
fi

echo
echo "net472: ${#suites[@]} suites passed"

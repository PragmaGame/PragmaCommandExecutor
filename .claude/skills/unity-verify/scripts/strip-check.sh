#!/bin/sh
# After a player build, compares every Pragma.CommandExecutor* player assembly with its stripped copy.
# Usage: strip-check.sh [platform]   (default: Android, the folder under Library/Bee/artifacts)
# Exit code 1 when the linker removed anything the original assembly declares.
cd "$(git rev-parse --show-toplevel)" || exit 2
PLATFORM="${1:-Android}"
SCRIPT="$(pwd)/.claude/skills/unity-verify/scripts/strip-check.cs"
FAILED=0

for original in Library/Bee/PlayerScriptAssemblies/Pragma.CommandExecutor*.dll; do
  stripped="Library/Bee/artifacts/$PLATFORM/ManagedStripped/$(basename "$original")"
  if [ ! -f "$stripped" ]; then
    echo "$(basename "$original"): no stripped copy for $PLATFORM"
    continue
  fi
  output=$(dotnet run --file "$SCRIPT" -- "$original" "$stripped" 2>&1)
  echo "$output"
  echo "$output" | grep -q ", missing 0$" || FAILED=1
done

exit $FAILED

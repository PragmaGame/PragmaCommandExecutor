#!/bin/sh
# Imports changed files into the running Editor of this project, recompiles and prints the result.
# Exit code 0 when the scripts compile. The errors come from recompile_status: the console keeps old entries.
cd "$(git rev-parse --show-toplevel)" || exit 2
UNITY="${UNITY:-$LOCALAPPDATA/Unity/bin/unity}"

"$UNITY" --no-banner --json command menu --path "Assets/Refresh" > /dev/null 2>&1
"$UNITY" --no-banner --json command recompile > /dev/null 2>&1

for _ in $(seq 1 100); do
  result=$("$UNITY" --no-banner --json command recompile_status 2>&1 | python -c "
import json, sys
try:
    data = json.load(sys.stdin)
    result = data['data']['result']
    result = json.loads(result) if isinstance(result, str) else result
    print(result.get('status'), result.get('compilationFailed'))
    for error in result.get('errors') or []:
        print(error)
except Exception as exception:
    print('unreachable', exception)
")
  case "$result" in
    completed*|up_to_date*|idle*|failed*) break ;;
  esac
  sleep 3
done

echo "$result"
case "$result" in
  *True*|unreachable*) exit 1 ;;
esac

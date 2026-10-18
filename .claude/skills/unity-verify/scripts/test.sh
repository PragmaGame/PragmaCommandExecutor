#!/bin/sh
# Runs the tests of this project in the running Editor and prints the summary and every failure.
# Usage: test.sh [editor|graph|playmode|all]   (default: all)
# Exit code 0 when every selected test passed.
cd "$(git rev-parse --show-toplevel)" || exit 2
UNITY="${UNITY:-$LOCALAPPDATA/Unity/bin/unity}"
SUITE="${1:-all}"
FAILED=0

report() {
  python -c "
import json, sys
raw = sys.stdin.read()
try:
    data = json.loads(raw)
    result = data['data']['result']
    result = json.loads(result) if isinstance(result, str) else result
except Exception:
    print(raw[:3000]); sys.exit(1)
summary = result.get('Summary') or result.get('summary')
print('$1:', summary)
failed = False
for test in result.get('Results') or result.get('results') or []:
    status = test.get('Status') or test.get('status')
    if status != 'Passed':
        failed = True
        print('-', test.get('FullName') or test.get('fullName'), status, (test.get('Message') or test.get('message') or '')[:800])
total = (summary or {}).get('Total', (summary or {}).get('total', 0))
sys.exit(1 if failed or not total else 0)
"
}

run_editor() {
  # A pipeline runs its last command in a subshell: the failure is recorded here, not inside report.
  "$UNITY" --no-banner --json command --timeout 900 run_tests --mode editor --filter_type assembly --filter "$1" | report "$1" || FAILED=1
}

run_playmode() {
  # Entering play mode reloads the domain and drops a synchronous request: start async and poll the status file.
  rm -f Temp/pipeline_test_status.json
  "$UNITY" --no-banner --json command run_tests --mode playmode --filter_type assembly --filter "$1" --async_tests true > /dev/null
  for _ in $(seq 1 120); do
    sleep 5
    if [ -f Temp/pipeline_test_status.json ] && python -c "
import json, sys
status = json.load(open('Temp/pipeline_test_status.json', encoding='utf-8-sig')).get('status', '')
sys.exit(0 if status in ('completed', 'failed', 'error') else 1)" 2>/dev/null; then
      break
    fi
  done
  python -c "
import json
data = json.load(open('Temp/pipeline_test_status.json', encoding='utf-8-sig'))
print(json.dumps({'data': {'result': data}}))" | report "$1" || FAILED=1
}

case "$SUITE" in
  editor) run_editor Pragma.CommandExecutor.Tests ;;
  graph) run_editor Pragma.CommandExecutor.GraphToolkit.Editor.Tests ;;
  playmode) run_playmode Pragma.CommandExecutor.PlayModeTests ;;
  all)
    run_editor Pragma.CommandExecutor.Tests
    run_editor Pragma.CommandExecutor.GraphToolkit.Editor.Tests
    run_playmode Pragma.CommandExecutor.PlayModeTests
    ;;
  *) echo "Unknown suite $SUITE"; exit 2 ;;
esac

exit $FAILED

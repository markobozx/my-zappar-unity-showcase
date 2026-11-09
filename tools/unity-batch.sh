#!/bin/bash
# Runs one Unity batch-mode method on this project, waiting for exclusive access first, because
# only one Unity process may open a project at a time (several agents share this project).
#
#   tools/unity-batch.sh <ExecuteMethod> <log-name> [--graphics] [extra Unity args...]
#
#   --graphics   keep the GPU (needed for preview renders and play tests); default adds -nographics
#   --no-quit    don't pass -quit (the method exits Unity itself, e.g. the play test)
# Unity is killed if it runs longer than 15 minutes.
# Prints the tagged log lines ([SHOWCASE*], [FLATKIT], errors) and exits with Unity's exit code,
# or 2 if the project has C# compile errors.
set -u
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="/Volumes/ExtSSD/Applications/UnityThings/6000.5.9f1/Unity.app/Contents/MacOS/Unity"
LOCK="/tmp/zappar-showcase-unity.lock"
METHOD="$1"; NAME="$2"; shift 2
GRAPHICS="-nographics"; QUIT="-quit"
while [ $# -gt 0 ]; do
  case "$1" in
    --graphics) GRAPHICS=""; shift ;;
    --no-quit) QUIT=""; shift ;;
    *) break ;;
  esac
done
LOG="$PROJECT/Logs/batch-$NAME.log"

# An open Editor holds the project too; never kill it, just report.
if pgrep -f "Unity.app/Contents/MacOS/Unity -projectPath $PROJECT" >/dev/null; then
  echo "BLOCKED: the Unity Editor has this project open. Close it first (do not kill it)."; exit 3
fi

waited=0
until mkdir "$LOCK" 2>/dev/null; do
  holder=$(cat "$LOCK/pid" 2>/dev/null || echo "")
  if [ -n "$holder" ] && ! kill -0 "$holder" 2>/dev/null; then
    echo "Removing stale lock from dead pid $holder"; rm -rf "$LOCK"; continue
  fi
  if [ $((waited % 30)) -eq 0 ]; then echo "Waiting for Unity lock (held by pid ${holder:-?}, ${waited}s)"; fi
  sleep 5; waited=$((waited + 5))
  if [ $waited -ge 1800 ]; then echo "TIMEOUT waiting for Unity lock"; exit 4; fi
done
echo $$ > "$LOCK/pid"
trap 'rm -rf "$LOCK"' EXIT

"$UNITY" -batchmode $GRAPHICS -projectPath "$PROJECT" -executeMethod "$METHOD" $QUIT -logFile "$LOG" "$@" >/dev/null 2>&1 &
upid=$!
# Detached from our stdout so a leftover sleep can never hold a caller's pipe open.
( sleep 900; kill -TERM $upid 2>/dev/null ) >/dev/null 2>&1 &
watchdog=$!
wait $upid; code=$?
pkill -P $watchdog 2>/dev/null; kill $watchdog 2>/dev/null; wait $watchdog 2>/dev/null
if [ $code -eq 143 ]; then echo "KILLED: Unity ran longer than 15 minutes"; fi

grep -E "error CS[0-9]+" "$LOG" | sed 's/^.*Assets/Assets/' | sort -u | head -30
if grep -qE "error CS[0-9]+" "$LOG"; then echo "COMPILE ERRORS (see $LOG)"; exit 2; fi
grep -E "\[SHOWCASE[A-Z-]*\]|\[SELFTEST\]|\[FLATKIT\]|NullReferenceException|Exception:|ERROR" "$LOG" | grep -v "ErrorCheck\|Couldn't open" | cut -c1-300 | head -60
echo "Unity exit code $code (log: $LOG)"
exit $code

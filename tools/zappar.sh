#!/bin/bash
# One entry point for the Zappar/Showcase commands, for people and agents:
#
#   tools/zappar.sh <command> [-arg value ...]
#   tools/zappar.sh zappar_status
#   tools/zappar.sh zappar_add_target -image ~/Desktop/poster.png
#   tools/zappar.sh showcase_set_target_experience -target poster -experience room
#   tools/zappar.sh list                      # every command with its arguments (needs the open Editor)
#
# Editor open  -> runs in it through the Unity CLI (unity command <command> --arg value).
# Editor closed -> runs headless through tools/unity-batch.sh (ZapparCliTools.ZapparBatch.Run), with the project lock.
# Exit code 0 = the command succeeded.
set -u
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
CMD="${1:-}"; shift || true
if [ -z "$CMD" ]; then sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 2; fi

if pgrep -f "Unity.app/Contents/MacOS/Unity -projectPath $PROJECT" >/dev/null; then
  if [ "$CMD" = "list" ]; then exec unity command --project-path "$PROJECT" --no-banner --no-pager --detail compact; fi
  args=()
  while [ $# -gt 0 ]; do case "$1" in -*) args+=("-$1") ;; *) args+=("$1") ;; esac; shift; done
  exec unity command "$CMD" --project-path "$PROJECT" --no-banner ${args[@]+"${args[@]}"}
fi

if [ "$CMD" = "list" ]; then echo "Open the Unity Editor for the full list, or read Assets/editor/ZapparCli/ZapparCliCommands.cs and Assets/editor/Showcase/ShowcaseCliCommands.cs"; exit 0; fi
exec "$PROJECT/tools/unity-batch.sh" ZapparCliTools.ZapparBatch.Run "zappar-$CMD" -zapparCommand "$CMD" "$@"

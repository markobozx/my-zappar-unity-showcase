#!/bin/bash
# Fast C# compile check of this project's own scripts (Assets/Scripts, Assets/editor) with Unity's
# Roslyn compiler, without opening Unity (no lock needed). References the last Unity-compiled
# assemblies, so pack/plugin types resolve. Prints errors only; exit 0 = compiles.
#
#   tools/compile-check.sh
set -u
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
S="/Volumes/ExtSSD/Applications/UnityThings/6000.5.9f1/Unity.app/Contents/Resources/Scripting"
TMP="$(mktemp -d)"
RSP="$TMP/csc.rsp"
cd "$PROJECT"
{
  echo "-nologo -nostdlib -target:library -langversion:9.0 -nowarn:0169,0414,0649,0618,0436,1701,1702 -out:$TMP/check.dll"
  echo "-define:UNITY_EDITOR;UNITY_WEBGL;UNITY_2020_1_OR_NEWER;UNITY_2019_1_OR_NEWER;ZAPPAR_SRP"
  echo "\"-r:$S/NetStandard/ref/2.1.0/netstandard.dll\""
  for f in "$S"/Managed/UnityEngine/*.dll Library/ScriptAssemblies/*.dll; do echo "\"-r:$f\""; done
  find Assets/Scripts Assets/editor -name "*.cs" | sed 's/.*/"&"/'
} > "$RSP"
out=$("$S/DotNetSdk/dotnet" "$S/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll" @"$RSP" 2>&1 | grep -E "error CS" | sed "s|$PROJECT/||" | sort -u)
rm -rf "$TMP"
if [ -n "$out" ]; then echo "$out"; exit 1; fi
echo "OK: compiles"

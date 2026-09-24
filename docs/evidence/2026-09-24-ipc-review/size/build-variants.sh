#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# IPC review, 2026-09-24: publish a copy of the REAL server (git archive of HEAD) six times,
# each with one IPC probe wired into Main, and record the NativeAOT executable size.
# Scratch only; the repository itself is only read (git archive).
set -u
REPO=/c/Source/SixFive7/BrowserAI
HERE=$REPO/.work/ipc-review/size
HEAD_SHA=$(git -C "$REPO" rev-parse HEAD)
echo "HEAD=$HEAD_SHA" > "$HERE/sizes.txt"
for V in V0-baseline V1-netpipe-sync V2-netpipe-async V3-rawpipe V4-record-crc V5-stop-event; do
  T="$HERE/tree-$V"
  rm -rf "$T"
  mkdir -p "$T"
  git -C "$REPO" archive --format=tar HEAD src build third-party assets Directory.Build.props Directory.Packages.props global.json .editorconfig tool-verdicts.json LICENSE THIRD-PARTY-NOTICES.txt | tar -x -C "$T"
  if [ "$V" = "V0-baseline" ]; then
    printf '// IPC review size probe: baseline, the same hook with nothing behind it.\nnamespace BrowserAI;\n\ninternal static class IpcProbe\n{\n    public static void Start()\n    {\n    }\n}\n' > "$T/src/BrowserAI/IpcProbe.cs"
  else
    cp "$HERE/probes/$V.cs" "$T/src/BrowserAI/IpcProbe.cs"
  fi
  python3 - "$T/src/BrowserAI/Program.cs" <<'PY'
import sys
p = sys.argv[1]
s = open(p, encoding='utf-8').read()
anchor = "    private static async Task<int> Main(string[] args)\n    {\n"
assert anchor in s, "anchor"
s = s.replace(anchor, anchor + "        if (System.Environment.GetEnvironmentVariable(\"IPCPROBE\") == \"1\")\n        {\n            IpcProbe.Start();\n        }\n\n", 1)
open(p, 'w', encoding='utf-8').write(s)
PY
  echo "=== $V $(date -u +%H:%M:%SZ)" >> "$HERE/build.log"
  ( cd "$T" && timeout 1200 dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64 --self-contained -p:MinVerVersionOverride=1.1.1-alpha.0.99 -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o "$T/out" >> "$HERE/build.log" 2>&1 )
  rc=$?
  size=$(stat -c %s "$T/out/BrowserAI.Server.exe" 2>/dev/null || echo missing)
  echo "$V exit=$rc BrowserAI.Server.exe=$size" | tee -a "$HERE/sizes.txt"
done
echo DONE >> "$HERE/sizes.txt"

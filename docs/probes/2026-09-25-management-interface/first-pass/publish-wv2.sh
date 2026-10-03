#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# wv2probe in three builds: window only, WebView2 with the loader DLL beside it, WebView2 with the loader linked in.
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1
pub(){ name=$1; shift; rm -rf probes/wv2probe/obj probes/wv2probe/bin; s=$(date +%s); dotnet publish probes/wv2probe/wv2probe.csproj -c Release -r win-x64 -o "out/$name" "$@" --disable-build-servers -nodeReuse:false > "logs/publish-$name.log" 2>&1; c=$?; e=$(date +%s); echo "$name exit=$c seconds=$((e-s)) exe=$(stat -c %s out/$name/wv2probe.exe 2>/dev/null) loader=$(stat -c %s out/$name/WebView2Loader.dll 2>/dev/null)" >> logs/publish-wv2-summary.txt; }
pub wv2-window -p:NoWebView=true
pub wv2-dll
pub wv2-static -p:StaticLoader=true
echo DONE >> logs/publish-wv2-summary.txt

#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Publishes every probe NativeAOT win-x64 Release, one after another, logging each.
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1
for p in "$@"; do
  start=$(date +%s)
  dotnet publish "probes/$p/$p.csproj" -c Release -r win-x64 -o "out/$p" --disable-build-servers -nodeReuse:false > "logs/publish-$p.log" 2>&1
  code=$?
  end=$(date +%s)
  echo "$p exit=$code seconds=$((end-start))" >> logs/publish-summary.txt
done
echo DONE >> logs/publish-summary.txt

#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Builds the b2 driver (JIT) and publishes the named probes NativeAOT win-x64 Release. Output goes to logs/, never to the caller's pipe.
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1
: > logs/build-summary.txt
for target in "$@"; do
  s=$(date +%s)
  if [ "$target" = "driver" ]; then
    dotnet build driver/driver.csproj -c Release -o out/driver --disable-build-servers -nodeReuse:false > logs/build-driver.log 2>&1
  else
    dotnet publish "probes/$target/$target.csproj" -c Release -r win-x64 -o "out/$target" --disable-build-servers -nodeReuse:false > "logs/publish-$target.log" 2>&1
  fi
  c=$?
  e=$(date +%s)
  echo "$target exit=$c seconds=$((e-s)) bytes=$(stat -c %s out/$target/$target.exe 2>/dev/null)" >> logs/build-summary.txt
done
echo DONE >> logs/build-summary.txt

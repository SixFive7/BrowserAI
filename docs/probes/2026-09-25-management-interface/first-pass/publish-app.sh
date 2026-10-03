#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Publishes the app copy once per variant ("" = unchanged baseline), NativeAOT win-x64 Release.
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1
for v in "$@"; do
  name=${v:-BASE}
  rm -rf appcopy/src/BrowserAI.App/obj appcopy/src/BrowserAI.App/bin
  start=$(date +%s)
  dotnet publish appcopy/src/BrowserAI.App/BrowserAI.App.csproj -c Release -r win-x64 --self-contained -o "out/app-$name" \
    -p:WebUiVariant="$v" -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false -p:EnforceCodeStyleInBuild=false \
    --disable-build-servers -nodeReuse:false > "logs/publish-app-$name.log" 2>&1
  code=$?
  end=$(date +%s)
  echo "$name exit=$code seconds=$((end-start)) bytes=$(stat -c %s out/app-$name/BrowserAI.exe 2>/dev/null)" >> logs/publish-app-summary.txt
done
echo DONE >> logs/publish-app-summary.txt

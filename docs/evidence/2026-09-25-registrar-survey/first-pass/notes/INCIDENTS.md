<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Incidents during track A (2026-09-25)

## 1. `dotnet package search` appended a PATH entry to HKCU\Environment\Path

- When: between the 00-baseline hash (03:47:33Z) and the 02-after-native hash (~04:02Z).
- Cause: `dotnet package search` run with DOTNET_CLI_HOME pointed at .work/zoomout/a/dotnet-home.
  The .NET SDK's first-use experience in a fresh DOTNET_CLI_HOME appends its global-tools
  folder to the user PATH (DOTNET_ADD_GLOBAL_TOOLS_TO_PATH was not set to false).
- What changed: the REG_EXPAND_SZ value gained the literal suffix
  `C:/Source/SixFive7/BrowserAI/.work/zoomout/a/dotnet-home\.dotnet\tools` (the original ended with `;`).
- Found by: the hash comparison in hash-real.sh (f59bf491... -> 40d2fad3...).
- Remedy: the suffix was stripped (exactly one occurrence, kind kept as ExpandString) and
  WM_SETTINGCHANGE "Environment" was broadcast. The restored value was verified against the
  baseline hash before writing (reconstruction hashed to f59bf491...) and after.
- Residual: any process that started between the SDK's change and the restore inherited the
  extra PATH entry; it points into a gitignored scratch folder that holds no executables.

## 2. APM 0.31.0 opened two "Connect to GitHub" sign-in windows (Git Credential Manager)

- When: 06:07:15 local (04:07:15Z) and 06:12:54 local (04:12:54Z), both closed at about 06:15 local.
- Cause: `apm install -g` (user scope, one self-defined MCP dependency, no packages) ran
  `git credential-manager get` through sh.exe/git.exe; Git Credential Manager 2.9.1 showed its
  "Connect to GitHub" window. The first run hung and was stopped (pid 100216, verified by path);
  the second was cut by a 100 s timeout. GCM (pids 96660 and 106036) survived both and were stopped
  after being verified by pid, creation time and path under C:\Program Files\Git; their sh/git
  parents had already exited.
- Evidence: Win32_Process listing (creation times 06:07:15 and 06:12:54, parent chain
  sh -> git credential-manager get -> git-credential-manager.exe), MainWindowTitle 'Connect to GitHub',
  and a GCM dotnet-suggest sentinel plus an NVIDIA DXCache file written under the sandbox TEMP/LOCALAPPDATA.
- Why it matters for the report: an MCP registrar that shells out to git for GitHub auth cannot
  run inside a Velopack hook or a background coordinator without risking a modal sign-in window.

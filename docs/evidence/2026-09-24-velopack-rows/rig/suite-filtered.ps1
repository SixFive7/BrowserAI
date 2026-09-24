# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch: ONE filtered suite run (iteration, never verification), PowerShell half: forces C:\ and declares upper.
# The run's own output goes straight to the file through cmd redirection, so the log is live and nothing
# is held by a pipeline in this shell.
param([Parameter(Mandatory)][string]$Out, [Parameter(Mandatory)][string]$Filter)
$ErrorActionPreference = 'Continue'
$root = 'C:\Source\SixFive7\BrowserAI'
Set-Location $root
"$((Get-Date).ToString('HH:mm:ss.fff')) starting filtered run filter=$Filter" | Set-Content -LiteralPath $Out
$env:BROWSERAI_DRIVE_CASE = 'upper'
$before = if (Test-Path "$root\.work\suite-coverage.txt") { (Get-Item "$root\.work\suite-coverage.txt").LastWriteTimeUtc } else { $null }
& "$env:SystemRoot\System32\cmd.exe" /d /c "dotnet test `"$root\BrowserAI.slnx`" --treenode-filter `"$Filter`" >> `"$Out`" 2>&1"
"$((Get-Date).ToString('HH:mm:ss.fff')) dotnet test exit=$LASTEXITCODE" | Add-Content -LiteralPath $Out
$after = if (Test-Path "$root\.work\suite-coverage.txt") { (Get-Item "$root\.work\suite-coverage.txt").LastWriteTimeUtc } else { $null }
"--- coverage block (file mtime before=$before after=$after, so it is this run's only if it moved):" | Add-Content -LiteralPath $Out
Get-Content "$root\.work\suite-coverage.txt" -ErrorAction SilentlyContinue | Add-Content -LiteralPath $Out
"$((Get-Date).ToString('HH:mm:ss.fff')) DONE" | Add-Content -LiteralPath $Out

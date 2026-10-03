# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# launch.ps1 -- starts a node script of the rig detached, output to files, isolated environment.
param([Parameter(Mandatory)][string]$Script, [string[]]$ScriptArgs = @(), [Parameter(Mandatory)][string]$Tag)
$root = 'C:\Source\SixFive7\BrowserAI\.work\state-across-close'
$env:LOCALAPPDATA = "$root\localappdata"
$env:TEMP = "$root\temp"
$env:TMP = "$root\temp"
$env:PWTEST_SERVER_REGISTRY = "$root\registry"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
$node = 'C:\Source\SixFive7\BrowserAI\payload\node\node.exe'
$allArgs = @("$root\rig\$Script") + $ScriptArgs
$p = Start-Process -FilePath $node -ArgumentList $allArgs -WorkingDirectory "$root\rig" -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput "$root\runs\_launch-$Tag.out.txt" -RedirectStandardError "$root\runs\_launch-$Tag.err.txt"
"{0}`t{1}`t{2}`t{3}" -f (Get-Date).ToUniversalTime().ToString('o'), $p.Id, $p.StartTime.ToUniversalTime().ToString('o'), ($allArgs -join ' ') | Add-Content "$root\launched-pids.tsv"
"pid=$($p.Id) start=$($p.StartTime.ToUniversalTime().ToString('o'))"

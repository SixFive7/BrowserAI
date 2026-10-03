# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Runs one node script under a kill-on-close job
# object this script creates, with the BrowserAI child environment, and ends anything still alive
# by closing that job when the script exits or the timeout passes. Output goes to files.
param(
    [Parameter(Mandatory)] [string] $Script,
    [string[]] $ScriptArgs = @(),
    [Parameter(Mandatory)] [string] $OutPrefix,
    [int] $TimeoutSec = 300
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\durability'
Add-Type -Path (Join-Path $root 'rig\hk.cs')
. (Join-Path $root 'rig\childenv.ps1')
$node = Join-Path $root 'payload\node\node.exe'
$scratch = "$OutPrefix-env"
foreach ($d in @("$scratch\temp", "$scratch\local", "$scratch\reg")) { [void][System.IO.Directory]::CreateDirectory($d) }
$envC = New-ChildEnv "$scratch\temp" "$scratch\local" "$scratch\reg" @{ HK_PW = (Join-Path $root 'payload\mcp\node_modules\playwright-core') }
$c = [HkChild]::Start($node, [string[]] (@($Script) + $ScriptArgs), $root, $envC)
$job = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job, $c.P)
$exited = $c.P.WaitForExit($TimeoutSec * 1000)
$pids = [HkWin]::JobPids($job)
$io = [HkWin]::JobIo($job)
[void][HkWin]::CloseHandle($job)
if ($exited) { $c.P.WaitForExit() }
[System.IO.File]::WriteAllText("$OutPrefix.out.txt", $c.OutText())
[System.IO.File]::WriteAllText("$OutPrefix.err.txt", $c.ErrText())
"exited=$exited exit=$(if ($exited) { $c.P.ExitCode } else { 'killed' }) stillInJobAtEnd=$($pids.Count) writeOps=$($io[1]) writeBytes=$($io[4])"

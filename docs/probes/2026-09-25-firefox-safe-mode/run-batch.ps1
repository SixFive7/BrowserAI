# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig (Q302): launches headless Firefox through the payload's
# playwright-core, each node+Firefox tree in its own KILL_ON_JOB_CLOSE job,
# serially or in concurrent batches, optionally with a commit limit or a CPU
# cap on that job alone. Results go to <run>\results.jsonl.
param(
    [Parameter(Mandatory)] [string] $RunName,
    [int] $Count = 10,
    [int] $Batch = 1,
    [int] $TimeoutMs = 180000,
    [int] $SuspectMs = 30000,
    [long] $JobMemoryMB = 0,
    [int] $CpuRate = 0,              # 1/100 of a percent of the whole machine; 0 = uncapped
    [int] $PauseMs = 1500,
    [switch] $WaitForQuiet,          # hold each batch until no suite run is active
    [string] $DebugNs = 'pw:protocol',
    [switch] $KeepProfiles,
    [switch] $ReuseProfile           # every launch in this run on one profile directory
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI'
$rigDir = Join-Path $root '.work\firefox-launch\rig'
$runDir = Join-Path $root ".work\firefox-launch\runs\$RunName"
New-Item -ItemType Directory -Force $runDir | Out-Null
$results = Join-Path $runDir 'results.jsonl'
$events = Join-Path $runDir 'events.log'
$node = Join-Path $root 'payload\node\node.exe'
$exe = Join-Path $root '.work\firefox-launch\firefox-1549\firefox\firefox.exe'
$pw = Join-Path $root 'payload\mcp\node_modules\playwright-core'

Add-Type -Path (Join-Path $rigDir 'Rig.cs')

function Log([string] $s) { Add-Content -LiteralPath $events -Value ("{0:o} {1}" -f [DateTime]::UtcNow, $s) }

function SuiteActive {
    if (Test-Path (Join-Path $root '.work\installer.lock')) { return 'installer.lock present' }
    $cut = [DateTime]::Now.AddSeconds(-20)
    $growing = Get-ChildItem (Join-Path $root '.work\suite') -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $cut }
    if ($growing) { return 'growing: ' + (($growing | Select-Object -ExpandProperty Name) -join ',') }
    return $null
}

# The product's child environment: an allow-list, plus the forced pair.
$allow = 'SystemRoot','windir','SystemDrive','COMSPEC','PATH','PATHEXT','NUMBER_OF_PROCESSORS','PROCESSOR_ARCHITECTURE','PROCESSOR_IDENTIFIER','OS','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA','HOMEDRIVE','HOMEPATH','PUBLIC','ProgramData','ALLUSERSPROFILE','ProgramFiles','ProgramFiles(x86)','ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432','USERNAME','USERDOMAIN','COMPUTERNAME','SESSIONNAME'
$envBlock = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($k in $allow) { $v = [Environment]::GetEnvironmentVariable($k); if ($v) { $envBlock[$k] = $v } }
$envBlock['PLAYWRIGHT_SKIP_BROWSER_GC'] = '1'
$envBlock['PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD'] = '1'
$envBlock['PLAYWRIGHT_BROWSERS_PATH'] = Join-Path $root '.work\firefox-launch'

function Inspect([int] $ffPid, [string] $outFile) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine(("inspect at {0:o} firefoxPid={1}" -f [DateTime]::UtcNow, $ffPid))
    try {
        $p = [System.Diagnostics.Process]::GetProcessById($ffPid)
        $a = @{}; foreach ($t in $p.Threads) { $a[$t.Id] = $t.TotalProcessorTime.TotalMilliseconds }
        $cpu0 = $p.TotalProcessorTime.TotalMilliseconds
        Start-Sleep -Milliseconds 2000
        $p.Refresh()
        [void]$sb.AppendLine(("process cpu {0:F0} ms total, +{1:F0} ms over 2 s; threads={2}; handles={3}; privateMB={4:F1} wsMB={5:F1}" -f $p.TotalProcessorTime.TotalMilliseconds, ($p.TotalProcessorTime.TotalMilliseconds - $cpu0), $p.Threads.Count, $p.HandleCount, ($p.PrivateMemorySize64/1MB), ($p.WorkingSet64/1MB)))
        $rows = foreach ($t in $p.Threads) {
            $wr = ''; try { if ($t.ThreadState -eq 'Wait') { $wr = $t.WaitReason } } catch {}
            $before = if ($a.ContainsKey($t.Id)) { $a[$t.Id] } else { 0 }
            [pscustomobject]@{ Tid = $t.Id; Start = $t.StartTime.ToString('HH:mm:ss.fff'); State = $t.ThreadState; Wait = $wr; CpuMs = [math]::Round($t.TotalProcessorTime.TotalMilliseconds); DeltaMs = [math]::Round($t.TotalProcessorTime.TotalMilliseconds - $before) }
        }
        [void]$sb.AppendLine(($rows | Sort-Object Start | Format-Table -AutoSize | Out-String -Width 200))
    } catch { [void]$sb.AppendLine("process read failed: $_") }
    try { [void]$sb.AppendLine("windows of the pid:"); [void]$sb.AppendLine([Rig]::WindowsOf($ffPid)) } catch { [void]$sb.AppendLine("window read failed: $_") }
    try {
        $kids = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ffPid"
        foreach ($k in $kids) {
            $kp = $null; try { $kp = [System.Diagnostics.Process]::GetProcessById([int]$k.ProcessId) } catch {}
            $cpu = if ($kp) { '{0:F0}' -f $kp.TotalProcessorTime.TotalMilliseconds } else { '?' }
            [void]$sb.AppendLine(("child pid={0} cpuMs={1} cmd={2}" -f $k.ProcessId, $cpu, ($k.CommandLine -replace '^.*?firefox\.exe"?\s*','')))
        }
    } catch { [void]$sb.AppendLine("child read failed: $_") }
    Set-Content -LiteralPath $outFile -Value $sb.ToString()
}

Log "run=$RunName count=$Count batch=$Batch timeoutMs=$TimeoutMs suspectMs=$SuspectMs jobMemoryMB=$JobMemoryMB cpuRate=$CpuRate debug=$DebugNs"
$launched = 0
$batchNo = 0
while ($launched -lt $Count) {
    if ($WaitForQuiet) {
        $waitStart = [DateTime]::UtcNow
        while ($why = SuiteActive) {
            if (([DateTime]::UtcNow - $waitStart).TotalSeconds -lt 1) { Log "waiting for quiet: $why" }
            Start-Sleep -Seconds 15
        }
        $waited = ([DateTime]::UtcNow - $waitStart).TotalSeconds
        if ($waited -gt 1) { Log ("quiet after {0:F0} s" -f $waited) }
    }
    $suiteNow = SuiteActive
    $batchNo++
    $n = [Math]::Min($Batch, $Count - $launched)
    $members = @()
    for ($i = 0; $i -lt $n; $i++) {
        $launched++
        $id = '{0}-{1:D4}' -f $RunName, $launched
        $dir = Join-Path $runDir $id
        New-Item -ItemType Directory -Force $dir | Out-Null
        $argsFile = Join-Path $dir 'args.json'
        $a = [ordered]@{
            id = $id; batch = $batchNo; mode = "b$Batch" + $(if ($JobMemoryMB) { "-mem$JobMemoryMB" } else { '' }) + $(if ($CpuRate) { "-cpu$CpuRate" } else { '' })
            playwrightCore = $pw; exe = $exe
            profile = $(if ($ReuseProfile) { Join-Path $runDir 'shared-profile' } else { Join-Path $dir 'firefox\profile' }); downloads = Join-Path $dir 'downloads'; artifacts = Join-Path $dir 'artifacts'
            log = Join-Path $dir 'probe.log'; results = $results
            timeoutMs = $TimeoutMs; suspectMs = $SuspectMs; debug = $DebugNs
        }
        ($a | ConvertTo-Json) | Set-Content -LiteralPath $argsFile
        $cmd = '"{0}" "{1}" "{2}"' -f $node, (Join-Path $rigDir 'ffprobe.js'), $argsFile
        $l = [Rig]::Launch($node, $cmd, $dir, $envBlock, [uint64]($JobMemoryMB * 1MB), [uint32]$CpuRate)
        $members += [pscustomobject]@{ Id = $id; Dir = $dir; L = $l; Inspected = $false; Start = [DateTime]::UtcNow }
        Log "launched $id nodePid=$($l.Pid) batch=$batchNo suiteActive=$([bool]$suiteNow) $suiteNow"
    }
    $bound = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs + 90000)
    while ($true) {
        $alive = @($members | Where-Object { -not [Rig]::HasExited($_.L) })
        foreach ($m in $members) {
            $sus = Join-Path $m.Dir 'probe.suspect'
            if (-not $m.Inspected -and (Test-Path $sus)) {
                $m.Inspected = $true
                $ffPid = [int](Get-Content -LiteralPath $sus -Raw)
                Log "SUSPECT $($m.Id) firefoxPid=$ffPid; inspecting"
                if ($ffPid -gt 0) {
                    # The pid Playwright spawned is Firefox's launcher; the browser
                    # is its child. Inspect both, keyed by pid and parent pid only.
                    $targets = @($ffPid) + @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ffPid" | ForEach-Object { [int]$_.ProcessId })
                    $k = 0
                    foreach ($t in $targets) { $k++; Inspect $t (Join-Path $m.Dir "inspect-1-$k-$t.txt") }
                    Start-Sleep -Seconds 20
                    if (-not [Rig]::HasExited($m.L)) { $k = 0; foreach ($t in $targets) { $k++; Inspect $t (Join-Path $m.Dir "inspect-2-$k-$t.txt") } }
                }
            }
        }
        if ($alive.Count -eq 0) { break }
        if ([DateTime]::UtcNow -gt $bound) { Log "BOUND reached with $($alive.Count) alive: $(($alive | ForEach-Object Id) -join ',')"; break }
        Start-Sleep -Milliseconds 250
    }
    foreach ($m in $members) {
        $rep = [Rig]::JobReport($m.L)
        $exited = [Rig]::HasExited($m.L)
        [Rig]::Close($m.L)
        Log "closed $($m.Id) exited=$exited $rep"
        Add-Content -LiteralPath (Join-Path $runDir 'jobs.log') -Value "$($m.Id) exited=$exited $rep"
        if (-not $KeepProfiles -and -not (Test-Path (Join-Path $m.Dir 'probe.suspect'))) {
            # Keep the listing, drop the bulk: a healthy profile is ~36 MB.
            $prof = Join-Path $m.Dir 'firefox'
            if (Test-Path $prof) { Remove-Item -LiteralPath $prof -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
    Start-Sleep -Milliseconds $PauseMs
}
Log "run $RunName done: $launched launched"

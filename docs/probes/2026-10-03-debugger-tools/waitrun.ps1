# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Blocks until the rig process of the newest run exits (by the pid its supervisor
# recorded, start time verified), at most -Seconds; then prints the batch tail.
# When the newest recorded rig has already exited, it first waits (file-system
# event, bounded) for the next run's supervisor.log to appear.
# Read-only: it waits on a process and never stops one.
param([string]$Batch = 'main2', [int]$Seconds = 110)
$s = 'C:\Source\SixFive7\BrowserAI\.work\debugger-tools'
function Newest { Get-ChildItem -LiteralPath "$s\runs" -Recurse -Filter supervisor.log | Sort-Object CreationTime | Select-Object -Last 1 }
function RigOf($sup) {
    $line = Get-Content -LiteralPath $sup.FullName | Where-Object { $_ -match 'rig pid=(\d+) start=(\S+)' } | Select-Object -First 1
    if ($line -match 'rig pid=(\d+) start=(\S+)') {
        $rp = [int]$Matches[1]; $st = [datetime]::Parse($Matches[2]).ToUniversalTime()
        $p = Get-Process -Id $rp -ErrorAction SilentlyContinue
        if ($p -and [Math]::Abs(($p.StartTime.ToUniversalTime() - $st).TotalSeconds) -lt 1) { return $rp }
    }
    return $null
}
$sup = Newest
$rp = RigOf $sup
if (-not $rp) {
    $w = [System.IO.FileSystemWatcher]::new("$s\runs", 'supervisor.log')
    $w.IncludeSubdirectories = $true
    [void]$w.WaitForChanged([System.IO.WatcherChangeTypes]::Created, 30000)
    $w.Dispose()
    for ($i = 0; $i -lt 20 -and -not $rp; $i++) { $sup = Newest; $rp = RigOf $sup; if (-not $rp) { [void][System.Threading.Tasks.Task]::Delay(100).Wait() } }
}
if ($rp) {
    "waiting on rig pid $rp ($($sup.Directory.FullName.Substring($s.Length + 6)))"
    Wait-Process -Id $rp -Timeout $Seconds -ErrorAction SilentlyContinue
} else { 'no running rig found' }
Get-Date -Format 'HH:mm:ss'
Get-Content -LiteralPath "$s\logs\batch-$Batch.log" | Select-Object -Last 1

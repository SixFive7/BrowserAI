# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03), grown from .work\hard-kill\rig\orchestrate.ps1.
# Not product code. Runs a plan (JSON array of run specs) one browser at a time and appends one
# JSON line per run to the results file. Resumable: a run id already in the results file without
# an error is skipped. The only kill it performs is closing a kill-on-close job object it created.
param(
    [Parameter(Mandatory)] [string] $PlanFile,
    [Parameter(Mandatory)] [string] $ResultsFile,
    [int] $Port = 47911
)

$ErrorActionPreference = 'Stop'
$root   = 'C:\Source\SixFive7\BrowserAI\.work\durability'
$rig    = Join-Path $root 'rig'
$node   = Join-Path $root 'payload\node\node.exe'
$pwCore = Join-Path $root 'payload\mcp\node_modules\playwright-core'

Add-Type -Path (Join-Path $rig 'hk.cs')
. (Join-Path $rig 'childenv.ps1')

function Now() { [HkWin]::NowMs() }

$http = [System.Net.Http.HttpClient]::new([System.Net.Http.HttpClientHandler]@{ UseProxy = $false })
$http.Timeout = [TimeSpan]::FromSeconds(10)
function Get-Status([string] $run) {
    $s = $http.GetStringAsync("http://127.0.0.1:$Port/status?run=$run").GetAwaiter().GetResult()
    return ($s | ConvertFrom-Json -AsHashtable)
}
function Set-Phase([string] $run, [string] $p) {
    [void]$http.GetStringAsync("http://127.0.0.1:$Port/phase?run=$run&p=$p").GetAwaiter().GetResult()
}

function Read-Tagged([HkChild] $c, [string[]] $want, [int] $timeoutMs) {
    $deadline = (Now) + $timeoutMs
    while ((Now) -lt $deadline) {
        $line = $c.ReadLine([int][Math]::Max(1, $deadline - (Now)))
        if ($null -eq $line) { if ($c.P.HasExited) { break }; continue }
        $sp = $line.IndexOf(' ')
        $tag = if ($sp -gt 0) { $line.Substring(0, $sp) } else { $line }
        $body = if ($sp -gt 0) { $line.Substring($sp + 1) } else { '{}' }
        if ($want -contains $tag -or $tag -in @('ERR', 'LAUNCH-FAILED')) {
            return @{ tag = $tag; body = ($body | ConvertFrom-Json -AsHashtable); line = $line }
        }
    }
    return @{ tag = 'TIMEOUT'; body = @{}; line = ''; err = $c.ErrText() }
}

function Get-WatchDirs([string] $browser, [string] $profile) {
    if ($browser -eq 'chromium') {
        return @(
            @{ dir = (Join-Path $profile 'Default\Network'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default\Local Storage\leveldb'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default\Session Storage'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default\IndexedDB'); recurse = $true },
            @{ dir = (Join-Path $profile 'Default\Service Worker\CacheStorage'); recurse = $true },
            @{ dir = (Join-Path $profile 'Default\Sessions'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default'); recurse = $false; filter = 'Preferences' }
        )
    }
    return @(
        @{ dir = $profile; recurse = $false; filter = 'cookies.sqlite*' },
        @{ dir = $profile; recurse = $false; filter = 'prefs.js' },
        @{ dir = (Join-Path $profile 'storage\default'); recurse = $true },
        @{ dir = (Join-Path $profile 'sessionstore-backups'); recurse = $false }
    )
}

function Get-WatchSnap($dirs, [string] $profile) {
    $snap = @{}
    foreach ($w in $dirs) {
        if (-not [System.IO.Directory]::Exists($w.dir)) { continue }
        $opt = if ($w.recurse) { [System.IO.SearchOption]::AllDirectories } else { [System.IO.SearchOption]::TopDirectoryOnly }
        $flt = if ($w.filter) { $w.filter } else { '*' }
        try {
            foreach ($f in [System.IO.Directory]::EnumerateFiles($w.dir, $flt, $opt)) {
                $fi = [System.IO.FileInfo]::new($f)
                try { $snap[$f.Substring($profile.Length + 1)] = '{0}|{1}' -f $fi.Length, $fi.LastWriteTimeUtc.Ticks } catch { }
            }
        } catch { }
    }
    return $snap
}

function Step-Watch($dirs, [string] $profile, [long] $zeroMs, $events, [hashtable] $prev) {
    $now = Now
    $snap = Get-WatchSnap $dirs $profile
    foreach ($k in $snap.Keys) {
        if (-not $prev.ContainsKey($k) -or $prev[$k] -ne $snap[$k]) {
            [void]$events.Add(('{0}|{1}|{2}' -f ($now - $zeroMs), $k, $snap[$k].Split('|')[0]))
        }
    }
    foreach ($k in @($prev.Keys)) { if (-not $snap.ContainsKey($k)) { [void]$events.Add(('{0}|{1}|gone' -f ($now - $zeroMs), $k)) } }
    $prev.Clear(); foreach ($k in $snap.Keys) { $prev[$k] = $snap[$k] }
}

function Watch-Until([long] $untilMs, $dirs, [string] $profile, [long] $zeroMs, $events, [hashtable] $prev) {
    while ($true) {
        Step-Watch $dirs $profile $zeroMs $events $prev
        $left = $untilMs - (Now)
        if ($left -le 0) { break }
        Start-Sleep -Milliseconds ([int][Math]::Min(100, $left))
    }
}

function Start-Driver([string] $runDir, [hashtable] $spec, [string] $mode, [string] $profile, [string] $pfx, [hashtable] $more = @{}) {
    $a = [ordered]@{ browser = $spec.browser; profile = $profile; mode = $mode; run = $spec.id; port = $Port; downloads = (Join-Path $runDir 'downloads'); tag = 'a' }
    foreach ($k in @('extraArgs', 'dropArgs', 'extraPrefs', 'tabs', 'prefsChange', 'settleMs', 'preIdleMs', 'syncToSave')) { if ($spec.ContainsKey($k)) { $a[$k] = $spec[$k] } }
    foreach ($k in $more.Keys) { $a[$k] = $more[$k] }
    $json = $a | ConvertTo-Json -Compress -Depth 10
    foreach ($d in @("$pfx-temp", "$pfx-local", "$pfx-reg")) { [void][System.IO.Directory]::CreateDirectory((Join-Path $runDir $d)) }
    $envC = New-ChildEnv (Join-Path $runDir "$pfx-temp") (Join-Path $runDir "$pfx-local") (Join-Path $runDir "$pfx-reg") @{ HK_PW = $pwCore }
    $c = [HkChild]::Start($node, [string[]]@((Join-Path $rig 'driver.js'), $json), $runDir, $envC)
    $job = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job, $c.P)
    return @{ c = $c; job = $job }
}

# Kill the whole tree by closing the job; returns the kill timestamp. Waits for every member to exit.
function Stop-Tree($d, $r, [string] $prefix) {
    $pids = [HkWin]::JobPids($d.job)
    $handles = [System.Collections.Generic.List[HkWin+ProcHandle]]::new()
    foreach ($p in $pids) { $h = [HkWin]::Open($p); if ($h) { $handles.Add($h) } }
    $r["${prefix}ProcessesBeforeKill"] = $pids.Count
    $tK = Now
    [void][HkWin]::CloseHandle($d.job); $d.job = [IntPtr]::Zero
    $alive = [HkWin]::WaitAll($handles, 30000)
    $r["${prefix}KillToAllExitedMs"] = (Now) - $tK
    $r["${prefix}AliveAfterKill"] = $alive.Count
    [HkWin]::CloseAll($handles)
    [void]$d.c.P.WaitForExit(10000)
    return $tK
}

function Close-Clean($d, [int] $timeoutMs = 60000) {
    $d.c.WriteLine('CLOSE')
    $l = Read-Tagged $d.c @('CLOSED') $timeoutMs
    [void]$d.c.P.WaitForExit(30000)
    if ($d.job -ne [IntPtr]::Zero) { [void][HkWin]::CloseHandle($d.job); $d.job = [IntPtr]::Zero }
    return $l.body.ms
}

function Invoke-Inspect([hashtable] $spec, [string] $profile, [switch] $Patch) {
    $args2 = @((Join-Path $rig 'inspect.js'), $spec.browser, $profile, $spec.id)
    if ($Patch) { $args2 += '--patch-exit-type' }
    $o = & $node @args2 2>&1
    try { return (($o | Select-Object -Last 1) | ConvertFrom-Json -AsHashtable) } catch { return @{ unparsable = "$o" } }
}

function Get-Survival([hashtable] $spec, $status, [string] $tag, [string] $phase) {
    $id = $spec.id
    $cookieHeader = [string]$status.readCookieHeader
    $rep = $status.report
    $ls = 0
    if ($rep -and $rep.localStorage) {
        for ($i = 0; $i -lt 40; $i++) {
            $k = 'hk_ls_{0}_{1:00}' -f $tag, $i
            if ($rep.localStorage.ContainsKey($k) -and $rep.localStorage[$k] -eq "${id}_$i") { $ls++ }
        }
    }
    $idb = $false
    if ($rep -and $rep.idb -is [System.Collections.IDictionary] -and $rep.idb.ContainsKey("k_$tag")) { $idb = $rep.idb["k_$tag"] -eq $id }
    $cache = $false
    if ($rep -and $rep.cache -is [System.Collections.IDictionary] -and $rep.cache.ContainsKey($tag)) { $cache = $rep.cache[$tag] -eq "${id}_cache" }
    $tabs = @(); $tabsSs = @()
    if ($status.tabs -and $status.tabs.ContainsKey($phase)) {
        foreach ($k in ($status.tabs[$phase].Keys | Sort-Object)) {
            $tabs += $k
            if ($status.tabs[$phase][$k].ss) { $tabsSs += $k }
        }
    }
    return [ordered]@{
        cookieHttpOnly = $cookieHeader.Contains("hk_srv_$tag=$id")
        cookieJs = $cookieHeader.Contains("hk_js_$tag=$id")
        localStorageKeys = $ls
        indexedDb = $idb
        cacheStorage = $cache
        tabsRestored = ($tabs -join ',')
        tabsWithSessionStorage = ($tabsSs -join ',')
        writePageRestored = [int] ($status.writeHits[$phase])
    }
}

function Invoke-Survival([hashtable] $spec) {
    $id = $spec.id
    $runDir = Join-Path $root "runs\$id"
    if (Test-Path -LiteralPath $runDir) { Remove-Item -LiteralPath $runDir -Recurse -Force }
    [void][System.IO.Directory]::CreateDirectory($runDir)
    $profile = Join-Path $runDir 'profile'
    $r = [ordered]@{ id = $id; kind = 'survival'; browser = $spec.browser; lever = $spec.lever; D = $spec.D; rep = $spec.rep; tabs = $spec.tabs; extraArgs = $spec.extraArgs; dropArgs = $spec.dropArgs; extraPrefs = $spec.extraPrefs; flush = $spec.flush; patchExitType = [bool]$spec.patchExitType; cycle = $spec.cycle; warm = [bool]$spec.warm; preIdleMs = $spec.preIdleMs; syncToSave = [bool]$spec.syncToSave; startedUtc = (Get-Date).ToUniversalTime().ToString('o') }
    $w = $null; $m = $null; $rd = $null; $u = $null
    try {
        if ($spec.warm) {
            Set-Phase $id 'warm'
            $u = Start-Driver $runDir $spec 'warm' $profile 'u'
            $l = Read-Tagged $u.c @('READY') 30000; if ($l.tag -ne 'READY') { throw "warm-up not ready: $($l.line)" }
            $u.c.WriteLine('GO')
            $l = Read-Tagged $u.c @('LAUNCHED') 240000; if ($l.tag -ne 'LAUNCHED') { throw "warm-up launch: $($l.line) $($l.err)" }
            $r.warmLaunchMs = $l.body.ms; $r.warmPages = $l.body.pages
            Start-Sleep -Milliseconds 1500
            $r.warmCloseMs = Close-Clean $u
            $r.inspectAfterWarm = Invoke-Inspect $spec $profile
            Set-Phase $id 'write'
        }
        $w = Start-Driver $runDir $spec 'write' $profile 'w'
        $l = Read-Tagged $w.c @('READY') 30000; if ($l.tag -ne 'READY') { throw "writer not ready: $($l.line) $($l.err)" }
        $w.c.WriteLine('GO')
        $l = Read-Tagged $w.c @('LAUNCHED') 240000; if ($l.tag -ne 'LAUNCHED') { throw "writer launch: $($l.line) $($l.err)" }
        $r.writerLaunchMs = $l.body.ms; $r.browserVersion = $l.body.version; $r.writerPagesAtLaunch = $l.body.pages
        $l = Read-Tagged $w.c @('WRITTEN') 120000; if ($l.tag -ne 'WRITTEN') { throw "writer write: $($l.line) $($l.err)" }
        $r.written = $l.body
        $tW = [long]$l.body.tDone
        $tZero = $tW
        $dirs = Get-WatchDirs $spec.browser $profile
        $events = [System.Collections.ArrayList]::new()
        $prev = Get-WatchSnap $dirs $profile
        [void]$events.Add(('{0}|baseline|{1}' -f ((Now) - $tW), $prev.Count))
        if ($spec.flush) {
            $w.c.WriteLine("FLUSH $($spec.flush)")
            $l = Read-Tagged $w.c @('FLUSHED') 60000; if ($l.tag -ne 'FLUSHED') { throw "flush: $($l.line) $($l.err)" }
            $r.flushed = $l.body
            $tZero = [long]$l.body.t
            [void]$events.Add(('{0}|flush-answered|' -f ($tZero - $tW)))
        }
        $r.idleMsAtWrite = [HkWin]::IdleMs()
        Watch-Until ($tZero + [long](1000 * [double]$spec.D)) $dirs $profile $tW $events $prev
        $r.idleMsAtKill = [HkWin]::IdleMs()
        $tK = Stop-Tree $w $r 'writer'
        $r.actualD = [Math]::Round(($tK - $tZero) / 1000.0, 3)
        $r.tKill = $tK
        $marks = $r.written.marks
        $r.ageAtKill = [ordered]@{
            cookie = if ($marks) { [Math]::Round(($tK - [long]$marks.cookie) / 1000.0, 3) } else { $null }
            localStorage = if ($marks) { [Math]::Round(($tK - [long]$marks.localStorage) / 1000.0, 3) } else { $null }
            indexedDb = if ($marks) { [Math]::Round(($tK - [long]$marks.indexedDb) / 1000.0, 3) } else { $null }
            cacheStorage = if ($marks) { [Math]::Round(($tK - [long]$marks.cacheStorage) / 1000.0, 3) } else { $null }
            tabs = if ($r.written.tTabs) { [Math]::Round(($tK - [long]$r.written.tTabs) / 1000.0, 3) } else { $null }
            pref = if ($r.written.tPref) { [Math]::Round(($tK - [long]$r.written.tPref) / 1000.0, 3) } else { $null }
        }
        $r.writerStderrTail = (($w.c.ErrText() -split "`n") | Select-Object -Last 5) -join "`n"
        $r.flushEvents = @($events)
        $r.inspectAfterKill = Invoke-Inspect $spec $profile -Patch:([bool]$spec.patchExitType)
        $integ = & $node (Join-Path $rig 'integrity.js') $profile (Join-Path $runDir 'dbcopy') 2>&1
        try { $r.integrity = ($integ | Select-Object -Last 1) | ConvertFrom-Json -AsHashtable; $r.integrity.Remove('all') } catch { $r.integrity = "unparsable: $integ" }

        if ($spec.cycle) {
            # The session after a hard kill: relaunch, record what came back, open one more tab,
            # wait, and kill again.
            Set-Phase $id 'mid'
            $m = Start-Driver $runDir $spec 'mid' $profile 'm' @{ phase = 'mid'; expectTabs = [int]$spec.tabs; midTab = 9 }
            $l = Read-Tagged $m.c @('READY') 30000; if ($l.tag -ne 'READY') { throw "mid not ready: $($l.line)" }
            $m.c.WriteLine('GO')
            $l = Read-Tagged $m.c @('LAUNCHED') 240000; if ($l.tag -ne 'LAUNCHED') { throw "mid launch: $($l.line) $($l.err)" }
            $r.midLaunchMs = $l.body.ms; $r.midPagesAtLaunch = $l.body.pages
            $l = Read-Tagged $m.c @('MID') 120000; if ($l.tag -ne 'MID') { throw "mid: $($l.line) $($l.err)" }
            $r.mid = $l.body
            $tM = [long]$l.body.tMidTab
            $ev2 = [System.Collections.ArrayList]::new(); $prev2 = Get-WatchSnap $dirs $profile
            Watch-Until ($tM + [long](1000 * [double]$spec.cycle)) $dirs $profile $tM $ev2 $prev2
            $tK2 = Stop-Tree $m $r 'mid'
            $r.midActualD = [Math]::Round(($tK2 - $tM) / 1000.0, 3)
            $r.midEvents = @($ev2)
            $st = Get-Status $id
            $r.midSurvival = Get-Survival $spec $st 'a' 'mid'
            $r.inspectAfterMidKill = Invoke-Inspect $spec $profile -Patch:([bool]$spec.patchExitType)
        }

        Set-Phase $id 'read'
        $rd = Start-Driver $runDir $spec 'read' $profile 'r' @{ phase = 'read'; expectTabs = ([int]$spec.tabs + $(if ($spec.cycle) { 1 } else { 0 })) }
        $l = Read-Tagged $rd.c @('READY') 30000; if ($l.tag -ne 'READY') { throw "reader not ready: $($l.line)" }
        $rd.c.WriteLine('GO')
        $l = Read-Tagged $rd.c @('LAUNCHED') 240000
        if ($l.tag -ne 'LAUNCHED') { $r.readerError = "$($l.tag) $($l.line) $($l.err)" }
        else {
            $r.readerLaunchMs = $l.body.ms; $r.readerPagesAtLaunch = $l.body.pages
            $l = Read-Tagged $rd.c @('READ') 120000
            if ($l.tag -ne 'READ') { $r.readerError = "$($l.tag) $($l.line)" } else {
                $r.readerSettleMs = $l.body.settleMs; $r.readerUrls = $l.body.urlsAfterSettle; $r.readerCookies = $l.body.cookies
            }
            $l = Read-Tagged $rd.c @('CLOSED') 60000
            $r.readerCloseMs = $l.body.ms
        }
        [void]$rd.c.P.WaitForExit(30000)
        $status = Get-Status $id
        $r.readCookieHeader = $status.readCookieHeader
        $r.survived = Get-Survival $spec $status 'a' 'read'
        $r.report = $status.report
        if ($r.report) { $r.report.Remove('localStorage') }
        $r.readerStderrTail = (($rd.c.ErrText() -split "`n") | Select-Object -Last 5) -join "`n"
    }
    catch {
        $r.error = "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        foreach ($pair in @(@('writer', $w), @('mid', $m), @('reader', $rd))) { if ($pair[1]) { $r["$($pair[0])StderrTail"] = (($pair[1].c.ErrText() -split "`n") | Select-Object -Last 15) -join "`n" } }
    }
    finally {
        foreach ($d in @($u, $w, $m, $rd)) {
            if ($d -and $d.job -ne [IntPtr]::Zero) { [void][HkWin]::CloseHandle($d.job); $d.job = [IntPtr]::Zero }
            if ($d) { [void]$d.c.P.WaitForExit(15000) }
        }
        $r.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
        if (-not $spec.keep) {
            foreach ($dn in @('profile', 'dbcopy', 'downloads')) { $p = Join-Path $runDir $dn; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue } }
        }
    }
    return $r
}

function Invoke-Work([hashtable] $spec) {
    $id = $spec.id
    $runDir = Join-Path $root "runs\$id"
    if (Test-Path -LiteralPath $runDir) { Remove-Item -LiteralPath $runDir -Recurse -Force }
    [void][System.IO.Directory]::CreateDirectory($runDir)
    $profile = Join-Path $runDir 'profile'
    $r = [ordered]@{ id = $id; kind = 'work'; browser = $spec.browser; lever = $spec.lever; rep = $spec.rep; secs = $spec.secs; extraArgs = $spec.extraArgs; dropArgs = $spec.dropArgs; extraPrefs = $spec.extraPrefs; flushKind = $spec.flushKind; flushEveryMs = $spec.flushEveryMs; startedUtc = (Get-Date).ToUniversalTime().ToString('o') }
    $w = $null
    try {
        $w = Start-Driver $runDir $spec 'work' $profile 'w'
        $l = Read-Tagged $w.c @('READY') 30000; if ($l.tag -ne 'READY') { throw "worker not ready: $($l.line)" }
        $w.c.WriteLine('GO')
        $l = Read-Tagged $w.c @('LAUNCHED') 240000; if ($l.tag -ne 'LAUNCHED') { throw "worker launch: $($l.line) $($l.err)" }
        $r.launchMs = $l.body.ms; $r.browserVersion = $l.body.version
        $l = Read-Tagged $w.c @('WRITTEN') 60000; if ($l.tag -ne 'WRITTEN') { throw "worker ready: $($l.line)" }
        Start-Sleep -Seconds 12   # let startup writes settle so the window measures the workload
        $dirs = Get-WatchDirs $spec.browser $profile
        $events = [System.Collections.ArrayList]::new()
        $prev = Get-WatchSnap $dirs $profile
        $io0 = [HkWin]::JobIo($w.job)
        $r.idleMsAtStart = [HkWin]::IdleMs()
        $t0 = Now
        $fk = if ($spec.flushKind) { $spec.flushKind } else { 'none' }
        $fe = if ($spec.flushEveryMs) { $spec.flushEveryMs } else { 0 }
        $w.c.WriteLine("WORK $($spec.secs) $fk $fe")
        $res = $null
        $deadline = $t0 + 1000 * ([int]$spec.secs + 90)
        while ((Now) -lt $deadline) {
            $line = $w.c.ReadLine(400)
            if ($line -and $line.StartsWith('WORKED ')) { $res = $line.Substring(7) | ConvertFrom-Json -AsHashtable; break }
            if ($line -and $line.StartsWith('ERR ')) { throw "work: $line" }
            Step-Watch $dirs $profile $t0 $events $prev
        }
        if (-not $res) { throw 'work timed out' }
        $io1 = [HkWin]::JobIo($w.job)
        $r.idleMsAtEnd = [HkWin]::IdleMs()
        $r.windowMs = (Now) - $t0
        $r.io = [ordered]@{ readOps = $io1[0] - $io0[0]; writeOps = $io1[1] - $io0[1]; otherOps = $io1[2] - $io0[2]; readBytes = $io1[3] - $io0[3]; writeBytes = $io1[4] - $io0[4]; otherBytes = $io1[5] - $io0[5]; cpuUserMs = [Math]::Round(($io1[6] - $io0[6]) / 10000.0); cpuKernelMs = [Math]::Round(($io1[7] - $io0[7]) / 10000.0) }
        $r.page = $res
        $r.events = @($events)
        $r.closeMs = Close-Clean $w
        $r.profileBytes = (Get-ChildItem -LiteralPath $profile -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    }
    catch {
        $r.error = "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        if ($w) { $r.stderrTail = (($w.c.ErrText() -split "`n") | Select-Object -Last 15) -join "`n" }
    }
    finally {
        if ($w -and $w.job -ne [IntPtr]::Zero) { [void][HkWin]::CloseHandle($w.job); $w.job = [IntPtr]::Zero }
        if ($w) { [void]$w.c.P.WaitForExit(15000) }
        $r.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
        if (-not $spec.keep) { $p = Join-Path $runDir 'profile'; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue } }
    }
    return $r
}

# ---------------- main ----------------
$plan = Get-Content -LiteralPath $PlanFile -Raw | ConvertFrom-Json -AsHashtable
$done = @{}
if (Test-Path -LiteralPath $ResultsFile) {
    foreach ($line in Get-Content -LiteralPath $ResultsFile) {
        try { $o = $line | ConvertFrom-Json -AsHashtable; if (-not $o.error) { $done[$o.id] = $true } } catch { }
    }
}
[void][System.IO.Directory]::CreateDirectory((Join-Path $root 'runs'))
$serverLog = Join-Path $root ("results\server-{0}.jsonl" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmss'))
$srvEnv = New-ChildEnv (Join-Path $root 'runs\srv-temp') (Join-Path $root 'runs\srv-local') (Join-Path $root 'runs\srv-reg') @{}
foreach ($d in @('runs\srv-temp', 'runs\srv-local', 'runs\srv-reg')) { [void][System.IO.Directory]::CreateDirectory((Join-Path $root $d)) }
$srv = [HkChild]::Start($node, [string[]]@((Join-Path $rig 'server.js'), [string]$Port, $serverLog), $root, $srvEnv)
$srvJob = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($srvJob, $srv.P)
$hello = $srv.ReadLine(15000)
if (-not $hello) { throw "server did not start: $($srv.ErrText())" }
Write-Output "server: $hello"
try {
    $n = 0
    foreach ($spec in $plan) {
        $n++
        if ($done.ContainsKey($spec.id)) { Write-Output "skip $($spec.id)"; continue }
        $t0 = Now
        $res = if ($spec.kind -eq 'work') { Invoke-Work $spec } else { Invoke-Survival $spec }
        Add-Content -LiteralPath $ResultsFile -Value ($res | ConvertTo-Json -Depth 30 -Compress) -Encoding utf8
        if ($spec.kind -eq 'work') {
            $sum = if ($res.io) { 'writeBytes={0} writeOps={1} lagP99={2} ls={3}' -f $res.io.writeBytes, $res.io.writeOps, $res.page.lagP99, $res.page.ops.ls } else { 'no io' }
        } else {
            $s = $res.survived
            $sum = if ($s) { 'ck={0}/{1} ls={2} idb={3} cache={4} tabs=[{5}] ss=[{6}] pref={7} exit={8}' -f $s.cookieHttpOnly, $s.cookieJs, $s.localStorageKeys, $s.indexedDb, $s.cacheStorage, $s.tabsRestored, $s.tabsWithSessionStorage, $res.inspectAfterKill.bookmarkBarShowOnAllTabs, $res.inspectAfterKill.exitType } else { 'no survival' }
        }
        Write-Output ('[{0}/{1}] {2} D={3} actualD={4} {5} err={6} ({7}s)' -f $n, $plan.Count, $spec.id, $spec.D, $res.actualD, $sum, $res.error, [Math]::Round(((Now) - $t0) / 1000.0, 1))
    }
}
finally {
    [void][HkWin]::CloseHandle($srvJob)
    [void]$srv.P.WaitForExit(10000)
    Write-Output "server exited: $($srv.P.HasExited)"
}

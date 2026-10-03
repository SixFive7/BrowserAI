# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig for the hard-kill survival measurement. Not product code.
# Runs a plan (JSON array of run specs) sequentially -- one browser alive at a time --
# and appends one JSON line per run to the results file. Resumable: a run id already
# present in the results file without an error is skipped.
param(
    [Parameter(Mandatory)] [string] $PlanFile,
    [Parameter(Mandatory)] [string] $ResultsFile,
    [int] $Port = 47811
)

$ErrorActionPreference = 'Stop'
$root   = 'C:\Source\SixFive7\BrowserAI\.work\hard-kill'
$rig    = Join-Path $root 'rig'
$repo   = 'C:\Source\SixFive7\BrowserAI'
$node   = Join-Path $repo 'payload\node\node.exe'
$pwCore = Join-Path $repo 'payload\mcp\node_modules\playwright-core'
$mcpCli = Join-Path $repo 'payload\mcp\node_modules\@playwright\mcp\cli.js'
$cache  = Join-Path $repo '.work\browsers-cache'
$taskkillExe = Join-Path $env:SystemRoot 'System32\taskkill.exe'

Add-Type -Path (Join-Path $rig 'hk.cs')

# ChildEnvironment.InheritedWhenSet, verbatim, so every child gets BrowserAI's allowlist.
$inherit = @('SystemRoot','windir','SystemDrive','COMSPEC','PATH','PATHEXT','NUMBER_OF_PROCESSORS',
    'PROCESSOR_ARCHITECTURE','PROCESSOR_IDENTIFIER','OS','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA',
    'HOMEDRIVE','HOMEPATH','PUBLIC','ProgramData','ALLUSERSPROFILE','ProgramFiles','ProgramFiles(x86)',
    'ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432','USERNAME','USERDOMAIN',
    'COMPUTERNAME','SESSIONNAME','HTTP_PROXY','HTTPS_PROXY','NO_PROXY','ALL_PROXY','NODE_EXTRA_CA_CERTS',
    'PWTEST_SERVER_REGISTRY')

function New-ChildEnv([string] $temp, [string] $local, [string] $reg, [hashtable] $extra) {
    $d = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($n in $inherit) { $v = [Environment]::GetEnvironmentVariable($n); if ($null -ne $v) { $d[$n] = $v } }
    $d['PLAYWRIGHT_SKIP_BROWSER_GC'] = '1'
    $d['PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD'] = '1'
    $d['PLAYWRIGHT_BROWSERS_PATH'] = $cache
    $d['TEMP'] = $temp; $d['TMP'] = $temp; $d['LOCALAPPDATA'] = $local; $d['PWTEST_SERVER_REGISTRY'] = $reg
    $d['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    if ($extra) { foreach ($k in $extra.Keys) { $d[$k] = [string]$extra[$k] } }
    return $d
}

function Get-FullEnv([hashtable] $extra) {
    $d = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($e in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $d[[string]$e.Key] = [string]$e.Value }
    if ($extra) { foreach ($k in $extra.Keys) { $d[$k] = [string]$extra[$k] } }
    return $d
}

function Now() { [HkWin]::NowMs() }

$http = [System.Net.Http.HttpClient]::new([System.Net.Http.HttpClientHandler]@{ UseProxy = $false })
$http.Timeout = [TimeSpan]::FromSeconds(10)
function Get-Status([string] $run) {
    $s = $http.GetStringAsync("http://127.0.0.1:$Port/status?run=$run").GetAwaiter().GetResult()
    return ($s | ConvertFrom-Json -AsHashtable)
}

function Read-Tagged([HkChild] $c, [string[]] $want, [int] $timeoutMs) {
    $deadline = (Now) + $timeoutMs
    while ((Now) -lt $deadline) {
        $line = $c.ReadLine([int][Math]::Max(1, $deadline - (Now)))
        if ($null -eq $line) {
            if ($c.P.HasExited) { break }
            continue
        }
        $sp = $line.IndexOf(' ')
        $tag = if ($sp -gt 0) { $line.Substring(0, $sp) } else { $line }
        $body = if ($sp -gt 0) { $line.Substring($sp + 1) } else { '{}' }
        if ($want -contains $tag -or $tag -in @('ERR', 'LAUNCH-FAILED')) {
            return @{ tag = $tag; body = ($body | ConvertFrom-Json -AsHashtable); line = $line }
        }
    }
    return @{ tag = 'TIMEOUT'; body = @{}; line = ''; err = $c.ErrText() }
}

function Invoke-Rpc([HkChild] $c, [ref] $nextId, [string] $method, $params, [int] $timeoutMs) {
    $id = $nextId.Value
    $nextId.Value = $id + 1
    $msg = [ordered]@{ jsonrpc = '2.0'; id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 30 -Compress
    $c.WriteLine($msg)
    $deadline = (Now) + $timeoutMs
    while ((Now) -lt $deadline) {
        $line = $c.ReadLine([int][Math]::Max(1, $deadline - (Now)))
        if ($null -eq $line) { if ($c.P.HasExited) { break }; continue }
        try { $o = $line | ConvertFrom-Json -AsHashtable } catch { continue }
        if ($o.ContainsKey('id') -and $o.id -eq $id) { return $o }
    }
    throw "rpc $method timed out after $timeoutMs ms; stderr: $($c.ErrText())"
}

function Send-Notify([HkChild] $c, [string] $method) {
    $c.WriteLine(([ordered]@{ jsonrpc = '2.0'; method = $method; params = @{} } | ConvertTo-Json -Compress))
}

function Get-Listing([string] $dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    $base = (Resolve-Path -LiteralPath $dir).Path.TrimEnd('\') + '\'
    $items = @()
    foreach ($f in [System.IO.Directory]::EnumerateFileSystemEntries($dir, '*', [System.IO.SearchOption]::AllDirectories)) {
        $isDir = [System.IO.Directory]::Exists($f)
        $len = if ($isDir) { -1 } else { ([System.IO.FileInfo]::new($f)).Length }
        $items += ('{0}|{1}' -f $f.Substring($base.Length), $len)
    }
    return $items
}

function Get-WatchDirs([string] $browser, [string] $profile) {
    if ($browser -eq 'chromium') {
        return @(
            @{ dir = (Join-Path $profile 'Default\Network'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default\Local Storage\leveldb'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default\Session Storage'); recurse = $false },
            @{ dir = (Join-Path $profile 'Default\IndexedDB'); recurse = $true },
            @{ dir = (Join-Path $profile 'Default'); recurse = $false; filter = 'Preferences' }
        )
    }
    return @(
        @{ dir = $profile; recurse = $false; filter = 'cookies.sqlite*' },
        @{ dir = $profile; recurse = $false; filter = 'webappsstore.sqlite*' },
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

function Watch-Until([long] $untilMs, $dirs, [string] $profile, [long] $zeroMs, $events, [hashtable] $prev) {
    while ($true) {
        $now = Now
        $snap = Get-WatchSnap $dirs $profile
        foreach ($k in $snap.Keys) {
            if (-not $prev.ContainsKey($k) -or $prev[$k] -ne $snap[$k]) {
                $parts = $snap[$k].Split('|')
                [void]$events.Add(('{0}|{1}|{2}' -f ($now - $zeroMs), $k, $parts[0]))
            }
        }
        foreach ($k in @($prev.Keys)) { if (-not $snap.ContainsKey($k)) { [void]$events.Add(('{0}|{1}|gone' -f ($now - $zeroMs), $k)) } }
        $prev.Clear(); foreach ($k in $snap.Keys) { $prev[$k] = $snap[$k] }
        $left = $untilMs - (Now)
        if ($left -le 0) { break }
        Start-Sleep -Milliseconds ([int][Math]::Min(100, $left))
    }
}

function Find-Keys($node, [string] $prefix, $acc) {
    if ($node -is [System.Collections.IDictionary]) {
        foreach ($k in $node.Keys) {
            $p = if ($prefix) { "$prefix.$k" } else { [string]$k }
            if ($k -match 'exit|clean|crash' -and -not ($node[$k] -is [System.Collections.IDictionary])) { $acc[$p] = $node[$k] }
            Find-Keys $node[$k] $p $acc
        }
    }
}

function Get-ProfileState([string] $browser, [string] $profile) {
    $s = [ordered]@{}
    $s.topLevel = @(Get-ChildItem -LiteralPath $profile -Force -ErrorAction SilentlyContinue | ForEach-Object { if ($_.PSIsContainer) { "$($_.Name)/" } else { "$($_.Name)|$($_.Length)" } })
    # What SessionInventory.CookieStoreIn (SessionInventory.cs:179-209) would find: a file named
    # Cookies or cookies.sqlite anywhere under the profile.
    $s.cookieStoreFiles = @(foreach ($nm in 'Cookies', 'cookies.sqlite') { if ([System.IO.Directory]::Exists($profile)) { foreach ($f in [System.IO.Directory]::EnumerateFiles($profile, $nm, [System.IO.SearchOption]::AllDirectories)) { '{0}|{1}' -f $f.Substring($profile.Length + 1), ([System.IO.FileInfo]::new($f)).Length } } })
    if ($browser -eq 'chromium') {
        $s.lockfile = [HkWin]::TryOpenRw((Join-Path $profile 'lockfile'))
        foreach ($pair in @(@('preferences', 'Default\Preferences'), @('localState', 'Local State'))) {
            $p = Join-Path $profile $pair[1]
            if (Test-Path -LiteralPath $p) {
                $raw = [System.IO.File]::ReadAllText($p)
                $s[$pair[0]] = @([regex]::Matches($raw, '"([A-Za-z_]*(?:exit|clean|crash)[A-Za-z_]*)"\s*:\s*("[^"]*"|true|false|-?\d+)') | ForEach-Object { "$($_.Groups[1].Value)=$($_.Groups[2].Value)" })
            } else { $s[$pair[0]] = 'absent' }
        }
    } else {
        $s.parentLock = [HkWin]::TryOpenRw((Join-Path $profile 'parent.lock'))
        $s.sessionstore = @(@('sessionstore.jsonlz4', 'sessionstore-backups\recovery.jsonlz4', 'sessionstore-backups\recovery.baklz4', 'sessionstore-backups\previous.jsonlz4') | Where-Object { Test-Path -LiteralPath (Join-Path $profile $_) })
        $prefs = Join-Path $profile 'prefs.js'
        $s.prefs = if (Test-Path -LiteralPath $prefs) { @(Select-String -LiteralPath $prefs -Pattern 'recent_crashes|last_success|resume_session_once|sessionstore' | ForEach-Object { $_.Line }) } else { 'absent' }
    }
    return $s
}

function Get-LevelDbLogHits([string] $profile) {
    $hits = @()
    foreach ($f in [System.IO.Directory]::EnumerateFiles($profile, 'LOG*', [System.IO.SearchOption]::AllDirectories)) {
        try {
            foreach ($m in (Select-String -LiteralPath $f -Pattern 'orrupt|Recovering log|Repair|error' -ErrorAction SilentlyContinue)) {
                $hits += ('{0}: {1}' -f $f.Substring($profile.Length + 1), $m.Line.Trim())
            }
        } catch { }
    }
    return $hits
}

function Start-Driver([string] $runDir, [hashtable] $spec, [string] $mode, [string] $profile, [string] $pfx, [string] $tag = 'a') {
    $xa = if ($spec.extraArgs) { @($spec.extraArgs) } else { @() }
    $a = [ordered]@{ browser = $spec.browser; profile = $profile; mode = $mode; run = $spec.id; port = $Port; downloads = (Join-Path $runDir 'downloads'); locale = 'nl-NL'; tz = 'Europe/Berlin'; tag = $tag; extraArgs = $xa } | ConvertTo-Json -Compress
    foreach ($d in @("$pfx-temp", "$pfx-local", "$pfx-reg")) { [void][System.IO.Directory]::CreateDirectory((Join-Path $runDir $d)) }
    $envC = New-ChildEnv (Join-Path $runDir "$pfx-temp") (Join-Path $runDir "$pfx-local") (Join-Path $runDir "$pfx-reg") @{ HK_PW = $pwCore }
    return [HkChild]::Start($node, [string[]]@((Join-Path $rig 'driver3.js'), $a), $runDir, $envC)
}

function Write-McpConfig([string] $file, [hashtable] $spec, [string] $sess, [string] $profile) {
    $isFx = $spec.browser -eq 'firefox'
    $launch = [ordered]@{}
    if ($isFx) {
        $launch.firefoxUserPrefs = [ordered]@{ 'toolkit.winRegisterApplicationRestart' = $false; 'signon.rememberSignons' = $false }
    } else {
        $launch.channel = 'chrome-for-testing'
        $launch.args = @('--enable-automation', '--disable-blink-features=AutomationControlled') + @($(if ($spec.extraArgs) { $spec.extraArgs } else { @() }))
    }
    $launch.headless = $true
    $launch.downloadsPath = (Join-Path $sess 'downloads')
    $ctxo = [ordered]@{ viewport = [ordered]@{ width = 1920; height = 1080 }; locale = 'nl-NL'; timezoneId = 'Europe/Berlin'; ignoreHTTPSErrors = $false }
    if (-not $isFx) { $ctxo.permissions = @('clipboard-read') }
    $cfg = [ordered]@{
        browser = [ordered]@{ browserName = $spec.browser; userDataDir = $profile; launchOptions = $launch; contextOptions = $ctxo }
        capabilities = @('config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing')
        outputDir = (Join-Path $sess 'output')
        saveSession = $false
        allowUnrestrictedFileAccess = $false
        console = [ordered]@{ level = 'debug' }
        snapshot = [ordered]@{ boxes = $true }
        codegen = 'none'
        filePaths = 'absolute'
        timeouts = [ordered]@{ idle = 3600000 }
        webmcp = $true
    }
    foreach ($d in @($profile, (Join-Path $sess 'output'), (Join-Path $sess 'downloads'))) { [void][System.IO.Directory]::CreateDirectory($d) }
    [System.IO.File]::WriteAllText($file, ($cfg | ConvertTo-Json -Depth 20), [System.Text.UTF8Encoding]::new($false))
}

function Start-Mcp([string] $runDir, [string] $cfgFile, [string] $sess, [string] $pfx, [hashtable] $extraEnv = @{}) {
    $envC = New-ChildEnv (Join-Path $runDir "$pfx-temp") (Join-Path $runDir "$pfx-local") (Join-Path $runDir "$pfx-reg") $extraEnv
    # BrowserAI's session child runs in <session>\output (SessionManager.cs:2348).
    return [HkChild]::Start($node, [string[]]@($mcpCli, '--config', $cfgFile, '--sandbox'), (Join-Path $sess 'output'), $envC)
}

function Wait-ServerField([string] $run, [scriptblock] $pick, [int] $timeoutMs) {
    $deadline = (Now) + $timeoutMs
    while ((Now) -lt $deadline) {
        $st = Get-Status $run
        $v = & $pick $st
        if ($null -ne $v) { return $v }
        Start-Sleep -Milliseconds 50
    }
    return $null
}

function Get-Survival([hashtable] $spec, $status, $readBody, [string] $tag) {
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
    $ss = $false
    if ($rep -and $rep.sessionStorage -is [System.Collections.IDictionary] -and $rep.sessionStorage.ContainsKey("hk_ss_$tag")) { $ss = $rep.sessionStorage["hk_ss_$tag"] -eq $id }
    return [ordered]@{
        cookieHttpOnly = $cookieHeader.Contains("hk_srv_$tag=$id")
        cookieJs = $cookieHeader.Contains("hk_js_$tag=$id")
        localStorageKeys = $ls
        indexedDb = $idb
        sessionStorage = $ss
    }
}


function Invoke-TreeKill([HkChild] $w, $rootId, [uint32[]] $pids, $r, [string] $runDir, [IntPtr] $jobNow = [IntPtr]::Zero) {
    # Membership is read again at the moment of the kill: a member may have started a child since.
    if ($jobNow -ne [IntPtr]::Zero) { $pids = [HkWin]::JobPids($jobNow) }
    $check = [HkWin]::Open([uint32]$w.P.Id)
    $same = $check -and $check.Created -eq $rootId.Created -and [string]::Equals($check.Image, $rootId.Image, [StringComparison]::OrdinalIgnoreCase) -and [string]::Equals($check.Image, $node, [StringComparison]::OrdinalIgnoreCase)
    if ($check) { [void][HkWin]::CloseHandle($check.H) }
    if (-not $same) { throw "identity check failed for pid $($w.P.Id): refusing to taskkill" }
    $tree = [HkWin]::Descendants([uint32]$w.P.Id)
    $pm = [HkWin]::ParentMap()
    $inJob = [System.Collections.Generic.HashSet[uint32]]::new([uint32[]]$pids)
    $strangers = @(); $outside = @()
    foreach ($x in $tree) {
        if ($inJob.Contains([uint32]$x)) { continue }
        $px = $pm[[uint32]$x]
        $hx = [HkWin]::Open([uint32]$x); $hp = if ($null -ne $px) { [HkWin]::Open([uint32]$px) } else { $null }
        $real = $hx -and $hp -and $inJob.Contains([uint32]$px) -and $hx.Created -ge $hp.Created
        $outside += ('{0} parent={1} image={2} createdAfterParentMs={3} real={4}' -f $x, $px, $(if ($hx) { [System.IO.Path]::GetFileName($hx.Image) } else { '?' }), $(if ($hx -and $hp) { [Math]::Round(($hx.Created - $hp.Created) / 10000.0, 1) } else { '?' }), $real)
        if (-not $real) { $strangers += $x }
        if ($hx) { [void][HkWin]::CloseHandle($hx.H) }; if ($hp) { [void][HkWin]::CloseHandle($hp.H) }
    }
    $r.taskkillTree = $tree.Count
    $r.taskkillTreeOutsideJob = $outside
    if ($strangers.Count) { throw "taskkill tree holds $($strangers.Count) pid(s) that are not provably ours ($($outside -join '; ')): refusing to taskkill" }
    $r.identityVerified = "pid=$($w.P.Id) created=$($rootId.Created) image=$($rootId.Image)"
    $tStart = Now
    $tkProc = [HkChild]::Start($taskkillExe, [string[]]@('/T', '/F', '/PID', [string]$w.P.Id), $runDir, (Get-FullEnv @{}))
    [void]$tkProc.P.WaitForExit(60000)
    $r.taskkillMs = (Now) - $tStart
    $r.taskkillExit = $tkProc.P.ExitCode
    $r.taskkillOut = ($tkProc.OutText() + $tkProc.ErrText()).Trim()
    return $tStart
}

function Invoke-Run([hashtable] $spec) {
    $id = $spec.id
    $runDir = Join-Path $root "runs\$id"
    if (Test-Path -LiteralPath $runDir) { Remove-Item -LiteralPath $runDir -Recurse -Force }
    $isMcp = $spec.path -eq 'mcp'
    $sess = Join-Path $runDir 'session'
    $profile = if ($isMcp) { Join-Path $sess 'profile' } else { Join-Path $runDir 'profile' }
    foreach ($d in @('w-temp', 'w-local', 'w-reg', 'r-temp', 'r-local', 'r-reg', 'downloads')) { [void][System.IO.Directory]::CreateDirectory((Join-Path $runDir $d)) }
    $r = [ordered]@{ id = $id; browser = $spec.browser; path = $spec.path; D = $spec.D; kill = $spec.kill; rep = $spec.rep; second = $spec.second; fast = [bool]$spec.fast; warm = [bool]$spec.warm; extraArgs = $spec.extraArgs; startedUtc = (Get-Date).ToUniversalTime().ToString('o') }
    $job = [IntPtr]::Zero; $job2 = [IntPtr]::Zero
    $handles = [System.Collections.Generic.List[HkWin+ProcHandle]]::new()
    $w = $null; $rd = $null
    try {
        # ---------------- writer ----------------
        $nextId = 1
        if ($isMcp) {
            $cfgFile = Join-Path $runDir 'playwright-mcp.config.json'
            Write-McpConfig $cfgFile $spec $sess $profile
            $w = Start-Mcp $runDir $cfgFile $sess 'w' $(if ($spec.debug) { @{ DEBUG = 'pw:browser*,pw:mcp*' } } else { @{} })
            $job = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job, $w.P)
        } else {
            if ($spec.warm) {
                # A first, cleanly closed session in the same profile, so the measured launch
                # is a resumed profile and not a fresh one.
                $u = Start-Driver $runDir $spec 'write' $profile 'u' 'w0'
                $jobU = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($jobU, $u.P)
                try {
                    $l = Read-Tagged $u @('READY') 30000
                    if ($l.tag -ne 'READY') { throw "warm-up not ready: $($l.line)" }
                    $u.WriteLine('GO')
                    $l = Read-Tagged $u @('LAUNCHED') 240000; if ($l.tag -ne 'LAUNCHED') { throw "warm-up launch: $($l.line)" }
                    $r.warmLaunchMs = $l.body.ms
                    $l = Read-Tagged $u @('WRITTEN') 120000; if ($l.tag -ne 'WRITTEN') { throw "warm-up write: $($l.line)" }
                    if ($spec.warmHold) { Start-Sleep -Seconds ([int]$spec.warmHold) }
                    $u.WriteLine('CLOSE')
                    $l = Read-Tagged $u @('CLOSED') 60000; $r.warmCloseMs = $l.body.ms
                    [void]$u.P.WaitForExit(30000)
                } finally { [void][HkWin]::CloseHandle($jobU) }
                $r.warmState = Get-ProfileState $spec.browser $profile
            }
            $w = Start-Driver $runDir $spec 'write' $profile 'w'
            $job = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job, $w.P)
            $l = Read-Tagged $w @('READY') 30000
            if ($l.tag -ne 'READY') { throw "writer not ready: $($l.line) $($l.err)" }
        }
        $r.jobLimitFlags = '0x{0:X}' -f [HkWin]::JobLimitFlags($job)
        $rootId = [HkWin]::Open([uint32]$w.P.Id)
        $r.rootPid = $w.P.Id; $r.rootCreated = $rootId.Created; $r.rootImage = $rootId.Image
        $tL0 = Now
        if ($isMcp) {
            $nid = [ref]$nextId
            $init = Invoke-Rpc $w $nid 'initialize' ([ordered]@{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = [ordered]@{ name = 'hk-probe'; version = '2026-10-03' } }) 60000
            $r.mcpServer = "$($init.result.serverInfo.name) $($init.result.serverInfo.version)"
            Send-Notify $w 'notifications/initialized'
            $nav = Invoke-Rpc $w $nid 'tools/call' ([ordered]@{ name = 'browser_navigate'; arguments = [ordered]@{ url = "http://127.0.0.1:$Port/write?run=$id&tag=a" } }) 240000
            $r.writerNavigateMs = (Now) - $tL0
            if ($nav.result.isError) { throw "navigate failed: $(($nav.result.content | ForEach-Object { $_.text }) -join ' ')" }
            $done = Wait-ServerField $id { param($st) if ($st.done -and $st.done.a) { $st.done.a } else { $null } } 60000
            if (-not $done) { throw 'write never reported done' }
            $tW = [long]$done.t
            $r.writtenEcho = $done
        } else {
            $w.WriteLine('GO')
            $l = Read-Tagged $w @('LAUNCHED') 240000
            if ($l.tag -ne 'LAUNCHED') { throw "writer launch: $($l.line) $($l.err)" }
            $r.writerLaunchMs = $l.body.ms; $r.browserVersion = $l.body.version
            $l = Read-Tagged $w @('WRITTEN') 120000
            if ($l.tag -ne 'WRITTEN') { throw "writer write: $($l.line) $($l.err)" }
            $tW = [long]$l.body.tDone
            $r.writtenEcho = $l.body
        }
        $r.tWritten = $tW
        $dirs = Get-WatchDirs $spec.browser $profile
        $events = [System.Collections.ArrayList]::new()
        $prev = Get-WatchSnap $dirs $profile
        [void]$events.Add(('{0}|baseline|{1}' -f ((Now) - $tW), $prev.Count))
        $tZero = $tW
        if ($spec.second) {
            Watch-Until ($tW + 1000 * [double]$spec.second) $dirs $profile $tW $events $prev
            $w.WriteLine('WRITE b')
            $l = Read-Tagged $w @('WRITTEN') 60000
            if ($l.tag -ne 'WRITTEN') { throw "second write: $($l.line)" }
            $tZero = [long]$l.body.tDone
            $r.tWrittenB = $tZero
            [void]$events.Add(('{0}|second-write-done|' -f ($tZero - $tW)))
        }
        if ($spec.kill -ne 'clean' -or $spec.D -gt 0) {
            Watch-Until ($tZero + [long](1000 * [double]$spec.D)) $dirs $profile $tW $events $prev
        }

        # ---------------- kill ----------------
        $pids = [HkWin]::JobPids($job)
        foreach ($p in $pids) { $h = [HkWin]::Open($p); if ($h) { $handles.Add($h) } }
        $r.processesBeforeKill = $pids.Count
        $r.imagesBeforeKill = (($handles | ForEach-Object { [System.IO.Path]::GetFileName($_.Image) } | Group-Object | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ',')
        switch ($spec.kill) {
            'job' {
                $tK = Now
                [void][HkWin]::CloseHandle($job); $job = [IntPtr]::Zero
            }
            'taskkill' {
                $check = [HkWin]::Open([uint32]$w.P.Id)
                $same = $check -and $check.Created -eq $rootId.Created -and [string]::Equals($check.Image, $rootId.Image, [StringComparison]::OrdinalIgnoreCase) -and [string]::Equals($check.Image, $node, [StringComparison]::OrdinalIgnoreCase)
                if ($check) { [void][HkWin]::CloseHandle($check.H) }
                if (-not $same) { throw "identity check failed for pid $($w.P.Id): refusing to taskkill" }
                # The tree taskkill /T will walk, by parent pid. Every member must be in our own job,
                # or a stale parent pid could hand taskkill a stranger: refuse, and do not risk it.
                $tree = [HkWin]::Descendants([uint32]$w.P.Id)
                $pm = [HkWin]::ParentMap()
                $inJob = [System.Collections.Generic.HashSet[uint32]]::new([uint32[]]$pids)
                $strangers = @(); $outside = @()
                foreach ($x in $tree) {
                    if ($inJob.Contains([uint32]$x)) { continue }
                    # Outside our job but in the tree. A REAL child was created after its parent;
                    # a pid-reuse stranger (stale parent pid) was created before it. Ids and times only.
                    $px = $pm[[uint32]$x]
                    $hx = [HkWin]::Open([uint32]$x); $hp = if ($null -ne $px) { [HkWin]::Open([uint32]$px) } else { $null }
                    $real = $hx -and $hp -and $inJob.Contains([uint32]$px) -and $hx.Created -ge $hp.Created
                    $outside += ('{0} parent={1} image={2} createdAfterParentMs={3} real={4}' -f $x, $px, $(if ($hx) { [System.IO.Path]::GetFileName($hx.Image) } else { '?' }), $(if ($hx -and $hp) { [Math]::Round(($hx.Created - $hp.Created) / 10000.0, 1) } else { '?' }), $real)
                    if (-not $real) { $strangers += $x }
                    if ($hx) { [void][HkWin]::CloseHandle($hx.H) }; if ($hp) { [void][HkWin]::CloseHandle($hp.H) }
                }
                $r.taskkillTree = $tree.Count
                $r.taskkillTreeOutsideJob = $outside
                if ($strangers.Count) { throw "taskkill tree holds $($strangers.Count) pid(s) that are not provably ours ($($outside -join '; ')): refusing to taskkill" }
                $r.identityVerified = "pid=$($w.P.Id) created=$($rootId.Created) image=$($rootId.Image)"
                $tK = Now
                $tkProc = [HkChild]::Start($taskkillExe, [string[]]@('/T', '/F', '/PID', [string]$w.P.Id), $runDir, (Get-FullEnv @{}))
                [void]$tkProc.P.WaitForExit(60000)
                $r.taskkillMs = (Now) - $tK
                $r.taskkillExit = $tkProc.P.ExitCode
                $r.taskkillOut = ($tkProc.OutText() + $tkProc.ErrText()).Trim()
            }
            'toolclose' {
                # BrowserAI's own idle close, used as a shutdown step: browser_close, then a hard
                # tree kill the moment it answers. Survival means "safe once the tool answered".
                $tTc = Now
                $ans = Invoke-Rpc $w $nid 'tools/call' ([ordered]@{ name = 'browser_close'; arguments = @{} }) 60000
                $r.toolCloseAnswerMs = (Now) - $tTc
                $r.toolCloseIsError = [bool]$ans.result.isError
                [void]$events.Add(('{0}|browser_close-answered|' -f ((Now) - $tW)))
                $tEof = $tTc
                if ($w.P.HasExited) { $tK = Now } else { $tK = Invoke-TreeKill $w $rootId $pids $r $runDir $job }
            }
            'eof' {
                # The client's graceful exit: the child's stdin closes, then after a grace the
                # tree is killed hard (taskkill /T /F, leaf first). Measures how much grace the
                # browser needs to save.
                $tEof = Now
                $w.CloseStdin()
                [void]$events.Add(('{0}|stdin-closed|' -f ($tEof - $tW)))
                $g = [double]$spec.grace
                # Trace who joins the job during the grace (observed by id; nothing is selected by name).
                $known = [System.Collections.Generic.HashSet[uint32]]::new([uint32[]]$pids)
                $joined = [System.Collections.ArrayList]::new()
                $deadline = $tEof + [long](1000 * $g)
                do {
                    try {
                        $pm = $null
                        foreach ($p in [HkWin]::JobPids($job)) {
                            if ($known.Add([uint32]$p)) {
                                if (-not $pm) { $pm = [HkWin]::ParentMap() }
                                $hx = [HkWin]::Open([uint32]$p)
                                $pp = $pm[[uint32]$p]
                                $hp = if ($null -ne $pp) { [HkWin]::Open([uint32]$pp) } else { $null }
                                [void]$joined.Add(('+{0}ms pid={1} image={2} parent={3} parentImage={4}' -f ((Now) - $tEof), $p, $(if ($hx) { [System.IO.Path]::GetFileName($hx.Image) } else { '?' }), $pp, $(if ($hp) { [System.IO.Path]::GetFileName($hp.Image) } else { '?' })))
                                if ($hx) { [void][HkWin]::CloseHandle($hx.H) }; if ($hp) { [void][HkWin]::CloseHandle($hp.H) }
                            }
                        }
                    } catch { }
                    if ($g -gt 0) { Watch-Until ([Math]::Min($deadline, (Now) + 50)) $dirs $profile $tW $events $prev }
                } while ((Now) -lt $deadline)
                $r.joinedDuringGrace = @($joined)
                $r.rootExitedBeforeKill = $w.P.HasExited
                if ($w.P.HasExited) {
                    $r.rootExitAfterEofMs = [long] (($w.P.ExitTime.ToUniversalTime() - [DateTime]::UnixEpoch).TotalMilliseconds) - $tEof
                    $tK = Now
                } else {
                    $tK = Invoke-TreeKill $w $rootId $pids $r $runDir $job
                }
                $r.graceActual = [Math]::Round(($tK - $tEof) / 1000.0, 3)
            }
            'clean' {
                $tK = Now
                if ($isMcp) {
                    $w.CloseStdin()
                } else {
                    $w.WriteLine('CLOSE')
                    $l = Read-Tagged $w @('CLOSED') 60000
                    $r.cleanCloseMs = $l.body.ms
                }
                [void]$w.P.WaitForExit(60000)
            }
        }
        $r.actualD = [Math]::Round(($tK - $tZero) / 1000.0, 3)
        if ($spec.kill -in @('eof', 'toolclose')) { $r.actualD = [Math]::Round(($tEof - $tZero) / 1000.0, 3) }
        $alive = [HkWin]::WaitAll($handles, 30000)
        $r.killToAllExitedMs = (Now) - $tK
        $r.aliveAfterKill = $alive.Count
        if ($job -ne [IntPtr]::Zero) {
            $acc = [HkWin]::JobAccounting($job)
            $r.jobActiveBeforeBackstop = $acc[1]
            [void][HkWin]::CloseHandle($job); $job = [IntPtr]::Zero
            $alive2 = [HkWin]::WaitAll($handles, 10000)
            $r.aliveAfterBackstop = $alive2.Count
        }
        $r.writerExitCode = if ($w.P.HasExited) { $w.P.ExitCode } else { $null }
        [HkWin]::CloseAll($handles); $handles.Clear()
        $r.flushEvents = @($events)

        # ---------------- post-kill inspection ----------------
        $tI0 = Now
        $r.postKill = Get-ProfileState $spec.browser $profile
        if (-not $spec.fast) {
            $integ = & $node (Join-Path $rig 'integrity.js') $profile (Join-Path $runDir 'dbcopy') 2>&1
            try { $r.integrity = ($integ | Select-Object -Last 1) | ConvertFrom-Json -AsHashtable } catch { $r.integrity = "unparsable: $integ" }
            if ($r.integrity -is [System.Collections.IDictionary]) { $r.integrity.Remove('all') }
        }
        $r.leftWriter = [ordered]@{ temp = @(Get-Listing (Join-Path $runDir 'w-temp')); local = @(Get-Listing (Join-Path $runDir 'w-local')); reg = @(Get-Listing (Join-Path $runDir 'w-reg')) }
        if ($isMcp) { $r.leftWriter.sessionOutput = @(Get-Listing (Join-Path $sess 'output')) }
        $r.inspectMs = (Now) - $tI0

        # ---------------- reader ----------------
        $tR0 = Now
        if ($isMcp) {
            $rd = Start-Mcp $runDir $cfgFile $sess 'r'
            $job2 = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job2, $rd.P)
            $nid2 = [ref]1
            [void](Invoke-Rpc $rd $nid2 'initialize' ([ordered]@{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = [ordered]@{ name = 'hk-probe'; version = '2026-10-03' } }) 60000)
            Send-Notify $rd 'notifications/initialized'
            $nav2 = Invoke-Rpc $rd $nid2 'tools/call' ([ordered]@{ name = 'browser_navigate'; arguments = [ordered]@{ url = "http://127.0.0.1:$Port/read?run=$id" } }) 240000
            $r.readerNavigateMs = (Now) - $tR0
            if ($nav2.result.isError) { $r.readerError = (($nav2.result.content | ForEach-Object { $_.text }) -join ' ') }
            $rep = Wait-ServerField $id { param($st) $st.report } 60000
            $tC = Now
            $rd.CloseStdin()
            [void]$rd.P.WaitForExit(60000)
            $r.readerCloseMs = (Now) - $tC
        } else {
            $rd = Start-Driver $runDir $spec 'read' $profile 'r'
            $job2 = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job2, $rd.P)
            $l = Read-Tagged $rd @('READY') 30000
            if ($l.tag -ne 'READY') { throw "reader not ready: $($l.line)" }
            $rd.WriteLine('GO')
            $l = Read-Tagged $rd @('LAUNCHED') 240000
            if ($l.tag -ne 'LAUNCHED') { $r.readerError = "$($l.tag) $($l.line) $($l.err)" }
            else {
                $r.readerLaunchMs = $l.body.ms
                $l = Read-Tagged $rd @('READ') 120000
                if ($l.tag -ne 'READ') { $r.readerError = "$($l.tag) $($l.line)" } else { $r.readerCookies = $l.body.cookies }
                $l = Read-Tagged $rd @('CLOSED') 60000
                $r.readerCloseMs = $l.body.ms
            }
            [void]$rd.P.WaitForExit(60000)
        }
        $r.readerExitCode = if ($rd.P.HasExited) { $rd.P.ExitCode } else { $null }
        $acc2 = [HkWin]::JobAccounting($job2)
        $r.readerJobActiveAtEnd = $acc2[1]
        [void][HkWin]::CloseHandle($job2); $job2 = [IntPtr]::Zero
        $status = Get-Status $id
        $r.readCookieHeader = $status.readCookieHeader
        $r.survivedA = Get-Survival $spec $status $null 'a'
        if ($spec.second) { $r.survivedB = Get-Survival $spec $status $null 'b' }
        if ($spec.warm) { $r.survivedW0 = Get-Survival $spec $status $null 'w0' }
        $r.report = $status.report
        if ($r.report) { $r.report.Remove('localStorage') }
        $r.postRead = Get-ProfileState $spec.browser $profile
        $r.levelDbLogHits = @(Get-LevelDbLogHits $profile)
        $r.leftReader = [ordered]@{ temp = @(Get-Listing (Join-Path $runDir 'r-temp')); local = @(Get-Listing (Join-Path $runDir 'r-local')); reg = @(Get-Listing (Join-Path $runDir 'r-reg')) }
        $r.writerStderrTail = (($w.ErrText() -split "`n") | Select-Object -Last 5) -join "`n"
        if ($spec.debug) { $r.writerStderrFull = $w.ErrText() }
        $r.readerStderrTail = (($rd.ErrText() -split "`n") | Select-Object -Last 5) -join "`n"
    }
    catch {
        $r.error = "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        if ($w) { $r.writerStderrTail = (($w.ErrText() -split "`n") | Select-Object -Last 15) -join "`n" }
        if ($rd) { $r.readerStderrTail = (($rd.ErrText() -split "`n") | Select-Object -Last 15) -join "`n" }
    }
    finally {
        # Backstops: closing our own kill-on-close jobs ends anything of ours still alive.
        if ($job -ne [IntPtr]::Zero) { [void][HkWin]::CloseHandle($job) }
        if ($job2 -ne [IntPtr]::Zero) { [void][HkWin]::CloseHandle($job2) }
        if ($w) { [void]$w.P.WaitForExit(15000) }
        if ($rd) { [void]$rd.P.WaitForExit(15000) }
        if ($handles.Count) { [void][HkWin]::WaitAll($handles, 15000); [HkWin]::CloseAll($handles) }
        $r.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
        $r.runDirBytes = (Get-ChildItem -LiteralPath $runDir -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        if (-not $spec.keep) {
            foreach ($d in @('profile', 'session', 'dbcopy', 'downloads')) { $p = Join-Path $runDir $d; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue } }
        }
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
$serverLog = Join-Path $root ("server-{0}.jsonl" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmss'))
$srvEnv = Get-FullEnv @{}
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
        $attempt = 0
        while ($true) {
            $attempt++
            $res = Invoke-Run $spec
            $res.attempt = $attempt
            $json = $res | ConvertTo-Json -Depth 30 -Compress
            Add-Content -LiteralPath $ResultsFile -Value $json -Encoding utf8
            # A tree that holds a process we cannot prove is ours is never killed; the run is
            # repeated with fresh pids instead, and the refusal stays in the results file.
            if ($res.error -and $res.error -match 'refusing to taskkill' -and $attempt -lt 4) { Write-Output "retry $($spec.id) after: $($res.error)"; continue }
            break
        }
        $s = $res.survivedA
        $sum = if ($s) { 'cookieHttpOnly={0} cookieJs={1} ls={2} idb={3} ss={4}' -f $s.cookieHttpOnly, $s.cookieJs, $s.localStorageKeys, $s.indexedDb, $s.sessionStorage } else { 'no survival' }
        if ($res.survivedB) { $b = $res.survivedB; $sum += ' | B: cookieHttpOnly={0} cookieJs={1} ls={2} idb={3}' -f $b.cookieHttpOnly, $b.cookieJs, $b.localStorageKeys, $b.indexedDb }
        Write-Output ('[{0}/{1}] {2} {3}s actualD={4} procs={5} alive={6} {7} err={8} ({9}s)' -f $n, $plan.Count, $spec.id, $spec.D, $res.actualD, $res.processesBeforeKill, $res.aliveAfterKill, $sum, $res.error, [Math]::Round(((Now) - $t0) / 1000.0, 1))
    }
}
finally {
    [void][HkWin]::CloseHandle($srvJob)
    [void]$srv.P.WaitForExit(10000)
    Write-Output "server exited: $($srv.P.HasExited)"
}

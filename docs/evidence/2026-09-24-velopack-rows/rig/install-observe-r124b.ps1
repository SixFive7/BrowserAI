# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch driver: runs the suite's TEST-PACK installer once, from a windowless (DETACHED) parent,
# in the sandbox, and records the process tree (kernel timestamps), command lines and windows.
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Tag,
    [switch]$Silent,
    [switch]$CloseApp,
    [switch]$WatchTerminals,
    [int]$Seconds = 120,
    [int]$SettleMs = 4000,
    [string]$Setup = 'C:\Source\SixFive7\BrowserAI\Releases\test-pack\BrowserAI.test-installer.exe'
)
$ErrorActionPreference = 'Stop'
$base = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
Add-Type -Path "$base\Rig.cs"
$logs = "$base\logs"; $null = New-Item -ItemType Directory -Force -Path $logs
$sandbox = "$base\sandbox"
$ov = [System.Collections.Generic.Dictionary[string,string]]::new()
$ov['CLAUDE_CONFIG_DIR'] = "$sandbox\client"
$ov['CODEX_HOME'] = "$sandbox\codex"
$ov['BROWSERAI_ROOT'] = "$sandbox\data"
$ov['VELOPACK_FIRSTRUN'] = $null
$ov['VELOPACK_RESTART'] = $null
foreach ($k in 'CLAUDE_CONFIG_DIR','CODEX_HOME','BROWSERAI_ROOT') { if (-not (Test-Path $ov[$k])) { throw "sandbox $k missing: $($ov[$k])" } }
if (-not (Test-Path "$sandbox\client\.claude.json")) { throw 'client sandbox not seeded' }

$out = "$logs\$Tag-observe.jsonl"
$w = [IO.StreamWriter]::new($out, $false, [Text.UTF8Encoding]::new($false))
$sw = [Diagnostics.Stopwatch]::StartNew()
function Emit($kind, [hashtable]$h) {
    $o = [ordered]@{ t = [math]::Round($sw.Elapsed.TotalMilliseconds, 1); wall = (Get-Date).ToString('HH:mm:ss.fff'); kind = $kind }
    foreach ($k in $h.Keys) { $o[$k] = $h[$k] }
    $w.WriteLine(($o | ConvertTo-Json -Compress -Depth 5)); $w.Flush()
}

$setupLog = "$logs\$Tag-setup.log"
$argline = "--verbose --log `"$setupLog`" --installto `"$Root`"" + $(if ($Silent) { ' --silent' } else { '' })
$setupProc = [VR.Rig]::StartDetached($Setup, $argline, (Split-Path $Setup), $ov)
Emit 'launch' @{ pid_ = $setupProc.Pid; created = [VR.Rig]::Ft($setupProc.Created); args = $argline; flags = 'DETACHED_PROCESS|CREATE_UNICODE_ENVIRONMENT, bInheritHandles=false' }

$tracked = @{ $setupProc.Pid = $setupProc }
$pre = @{}; foreach ($q in [VR.Rig]::Snapshot()) { $pre[$q.Pid] = 1 }; $baseWins = @{}; foreach ($w0 in [VR.Rig]::Windows()) { $baseWins["$($w0.Hwnd)"] = 1 }; $exeOf = @{}
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$shots = 0
$reported = @{}
$winSeen = @{}
$setupGoneAt = $null
$appWindowAt = $null
$appPid = 0
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    foreach ($p in [VR.Rig]::Snapshot()) {
        if ($tracked.ContainsKey($p.Pid)) { continue }
        $isTerm = $WatchTerminals -and ($p.Exe -in @('WindowsTerminal.exe','OpenConsole.exe')) -and -not $pre.ContainsKey($p.Pid)
        if (-not $tracked.ContainsKey($p.Ppid) -and -not $isTerm) { continue }
        $parent = if ($tracked.ContainsKey($p.Ppid)) { $tracked[$p.Ppid] } else { $setupProc }
        if (-not [VR.Rig]::Attach($p)) { Emit 'attach-failed' @{ pid_ = $p.Pid; ppid = $p.Ppid; exe = $p.Exe }; $tracked[$p.Pid] = $p; continue }
        if ($parent.Created -ne 0 -and $p.Created -lt $parent.Created) { [void][VR.Native]::CloseHandle($p.Handle); continue } # pid reuse guard
        $tracked[$p.Pid] = $p
        Emit 'proc+' @{ pid_ = $p.Pid; ppid = $p.Ppid; exe = $p.Exe; path = $p.Path; created = [VR.Rig]::Ft($p.Created); cmd = [VR.Rig]::CommandLine($p.Handle); sinceSetupStartMs = [VR.Rig]::Ms($setupProc.Created, $p.Created) }
    }
    foreach ($p in @($tracked.Values)) {
        if ($reported.ContainsKey($p.Pid)) { continue }
        [VR.Rig]::Refresh($p)
        if ($p.Exited -ne 0) {
            $reported[$p.Pid] = $true
            Emit 'proc-' @{ pid_ = $p.Pid; exe = $p.Exe; exited = [VR.Rig]::Ft($p.Exited); exitCode = $p.ExitCode; lifeMs = [VR.Rig]::Ms($p.Created, $p.Exited) }
            if ($p.Pid -eq $setupProc.Pid) { $setupGoneAt = $sw.Elapsed.TotalMilliseconds }
        }
    }
    foreach ($win in [VR.Rig]::Windows()) {
        $newConsole = $WatchTerminals -and -not $baseWins.ContainsKey("$($win.Hwnd)") -and $win.Visible -and ($win.Class -in @('CASCADIA_HOSTING_WINDOW_CLASS','ConsoleWindowClass','PseudoConsoleWindow'))
        if (-not $tracked.ContainsKey($win.Pid) -and -not $newConsole) { continue }
        if (-not $tracked.ContainsKey($win.Pid)) { $tracked[$win.Pid] = [VR.Proc]::new(); $tracked[$win.Pid].Pid = $win.Pid; $tracked[$win.Pid].Exe = 'untracked-owner:' + ((Get-Process -Id $win.Pid -ErrorAction SilentlyContinue).ProcessName); $reported[$win.Pid] = $true }
        $key = "$($win.Hwnd)"
        $state = "$($win.Visible)|$($win.Title)"
        if ($winSeen[$key] -ne $state) {
            $winSeen[$key] = $state
            Emit 'window' @{ hwnd = ('0x{0:X}' -f $win.Hwnd.ToInt64()); pid_ = $win.Pid; exe = $tracked[$win.Pid].Exe; class = $win.Class; title = $win.Title; visible = $win.Visible; rect = "$($win.L),$($win.T)-$($win.R),$($win.B)"; w = $win.R - $win.L; h = $win.B - $win.T }
            if ($win.Visible -and $win.Class -in @('ConsoleWindowClass','CASCADIA_HOSTING_WINDOW_CLASS','PseudoConsoleWindow') -and $shots -lt 3) {
                $shots++; $bmp = [System.Drawing.Bitmap]::new([Math]::Max(1,$win.R-$win.L), [Math]::Max(1,$win.B-$win.T)); $g = [System.Drawing.Graphics]::FromImage($bmp)
                try { $g.CopyFromScreen($win.L, $win.T, 0, 0, $bmp.Size); $bmp.Save("$logs\$Tag-console-$shots.png", [System.Drawing.Imaging.ImageFormat]::Png); Emit 'screenshot' @{ file = "$Tag-console-$shots.png"; hwnd = ('0x{0:X}' -f $win.Hwnd.ToInt64()) } } catch { Emit 'screenshot-failed' @{ err = "$_" } } finally { $g.Dispose(); $bmp.Dispose() }
            }
            if ($win.Visible -and $win.Class -eq '#32770' -and $tracked[$win.Pid].Path -like "$Root\current\*" -and -not $appWindowAt) { $appWindowAt = $sw.Elapsed.TotalMilliseconds; $appPid = $win.Pid }
        }
    }
    if ($setupGoneAt) {
        if ($Silent) { if ($sw.Elapsed.TotalMilliseconds - $setupGoneAt -gt $SettleMs) { break } }
        elseif ($appWindowAt -and ($sw.Elapsed.TotalMilliseconds - $appWindowAt -gt $SettleMs)) { break }
    }
    Start-Sleep -Milliseconds 5
}
Emit 'loop-end' @{ setupGoneAtMs = $setupGoneAt; appWindowAtMs = $appWindowAt; appPid = $appPid }

# Summary with kernel timestamps.
foreach ($p in @($tracked.Values)) { [VR.Rig]::Refresh($p) }
$summary = foreach ($p in ($tracked.Values | Sort-Object Created)) {
    [ordered]@{ pid_ = $p.Pid; ppid = $p.Ppid; exe = $p.Exe; path = $p.Path; created = [VR.Rig]::Ft($p.Created); exited = [VR.Rig]::Ft($p.Exited); exitCode = $p.ExitCode }
}
Emit 'tree' @{ procs = @($summary) }

if ($CloseApp -and $appPid) {
    $app = $tracked[$appPid]
    $dlg = [VR.Rig]::Windows() | Where-Object { $_.Pid -eq $appPid -and $_.Visible -and $_.Class -eq '#32770' } | Select-Object -First 1
    if ($dlg) {
        $posted = [VR.Rig]::PostClose($dlg.Hwnd)
        Emit 'close-posted' @{ hwnd = ('0x{0:X}' -f $dlg.Hwnd.ToInt64()); pid_ = $appPid; posted = $posted }
        $t0 = $sw.Elapsed.TotalSeconds
        while ($sw.Elapsed.TotalSeconds - $t0 -lt 30) { [VR.Rig]::Refresh($app); if ($app.Exited -ne 0) { break }; Start-Sleep -Milliseconds 20 }
        Emit 'app-exit' @{ pid_ = $appPid; exited = [VR.Rig]::Ft($app.Exited); exitCode = $app.ExitCode }
    }
}
$w.Close()
Get-Content -LiteralPath $out

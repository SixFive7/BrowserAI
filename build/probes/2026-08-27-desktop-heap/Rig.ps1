# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig for QUESTIONS.md section 8 -- the silent Chromium death.
# Consumes a DEDICATED desktop's heap and launches the provisioned Chromium
# onto that same desktop. The interactive desktop is never touched.
[CmdletBinding()]
param(
    [ValidateSet('Controller', 'Agent')][string] $Mode = 'Controller',

    # Agent
    [string] $Ipc = '',
    [int] $Id = 0,
    [int] $TitleChars = 0,
    [int] $Cap = 0,

    # Controller
    [string] $Station = '',
    [string] $Desktop = 'BaiHeapRig',
    [int] $HeapKb = 0,
    [int] $MaxFillers = 40,
    [int] $FixedCap = 0,
    [switch] $UseDefaultDesktop,
    [int] $Reps = 5,
    [int] $Controls = 5,
    [string] $Out = '',
    [string] $Chromium = '',
    [int] $BrowserWaitMs = 12000,
    [int] $ReleaseAfterMs = 0,
    [switch] $NoFill,

    # Added 2026-08-29 for QUESTIONS.md section 8 direction (e). The 2026-08-27
    # rig could only exhaust the heap BEFORE the browser started, or hand it back
    # DURING startup. This is the arm nobody had run: pre-fill to a level the
    # browser survives, then take the last few windows -- crossing the cliff
    # while it is mid-startup, which is what a real desktop does all day.
    [int] $TripCount = 0,
    [int] $TripAfterMs = 0
)

# One kernel object, so the trip lands within a millisecond of where it was
# aimed rather than within a file-poll's 100.
$TripName = 'Local\BaiHeapRigTrip'

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -Path (Join-Path $here 'Rig.cs') -ErrorAction Stop

function Now { (Get-Date).ToUniversalTime().ToString('HH:mm:ss.fff') }

# Straight to the standard output handle, never down the pipeline: a Write-Output
# inside a function is captured as that function's return value, and the first
# smoke run silently returned its own commentary instead of its results.
function Say([string] $text) {
    [Console]::Out.WriteLine("[{0}] {1}", (Now), $text)
    [Console]::Out.Flush()
}

# ---------------------------------------------------------------- Agent mode
if ($Mode -eq 'Agent') {
    $log = Join-Path $Ipc ("a{0}.log" -f $Id)
    $keep = [System.Collections.Generic.List[System.IntPtr]]::new()
    try {
        $station = [HeapRig.Native]::GetProcessWindowStation()
        $desktop = [HeapRig.Native]::GetThreadDesktop([HeapRig.Native]::GetCurrentThreadId())
        $stationName = [HeapRig.Rig]::NameOf($station)
        $desktopName = [HeapRig.Rig]::NameOf($desktop)
        $heapKb = [HeapRig.Rig]::HeapSizeKb($desktop)

        "pid=$PID station=$stationName desktop=$desktopName heapKb=$heapKb" |
            Set-Content -Path (Join-Path $Ipc ("a{0}.ready" -f $Id)) -Encoding utf8

        $report = [HeapRig.Rig]::Consume($TitleChars, $Cap, $keep)
        "$report heapKb=$heapKb station=$stationName desktop=$desktopName" |
            Set-Content -Path (Join-Path $Ipc ("a{0}.filled" -f $Id)) -Encoding utf8

        # The trip: block on the event and, the instant it is set, take the last
        # few windows. This runs BEFORE the poll loop below, so the agent is
        # parked on a kernel object rather than spinning a file check.
        if ($TripCount -gt 0) {
            $gate = [System.Threading.EventWaitHandle]::new($false, [System.Threading.EventResetMode]::ManualReset, $TripName)
            [void]$gate.WaitOne(120000)
            $tripped = [HeapRig.Rig]::Consume($TitleChars, $TripCount, $keep)
            "$tripped" | Set-Content -Path (Join-Path $Ipc ("a{0}.tripped" -f $Id)) -Encoding utf8
            $gate.Dispose()
        }

        $deadline = (Get-Date).AddMinutes(45)
        while ((Get-Date) -lt $deadline) {
            if (Test-Path (Join-Path $Ipc 'stop')) { break }

            if ($Id -eq 0) {
                foreach ($request in @(Get-ChildItem -Path $Ipc -Filter 'probe.*' -File -ErrorAction SilentlyContinue)) {
                    $token = $request.Name.Substring(6)
                    $answer = Join-Path $Ipc ("probed.{0}" -f $token)
                    if (-not (Test-Path $answer)) {
                        $walk = [HeapRig.Rig]::ProbeMessageWindows('Chrome_MessageWindow')
                        $any = [HeapRig.Rig]::ProbeMessageWindows('STATIC')
                        "$walk`nstatic=$any" | Set-Content -Path $answer -Encoding utf8
                    }
                }
            }

            if ((Test-Path (Join-Path $Ipc 'release')) -and $keep.Count -gt 0) {
                foreach ($window in $keep) { [void][HeapRig.Native]::DestroyWindow($window) }
                $keep.Clear()
                "released" | Set-Content -Path (Join-Path $Ipc ("a{0}.released" -f $Id)) -Encoding utf8
            }

            Start-Sleep -Milliseconds 100
        }

        "exiting cleanly" | Add-Content -Path $log -Encoding utf8
    }
    catch {
        "AGENT $Id FAILED: $_`n$($_.ScriptStackTrace)" | Add-Content -Path $log -Encoding utf8
        exit 3
    }
    exit 0
}

# ----------------------------------------------------------- Controller mode
if ([string]::IsNullOrEmpty($Out)) { $Out = Join-Path $here ('run-' + (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')) }
$null = New-Item -ItemType Directory -Path $Out -Force
$ipcDir = Join-Path $Out 'ipc'
$null = New-Item -ItemType Directory -Path $ipcDir -Force

if ([string]::IsNullOrEmpty($Chromium)) {
    $Chromium = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers\chromium-1237\chrome-win64\chrome.exe'
}
if (-not (Test-Path $Chromium)) { throw "no provisioned chromium at $Chromium" }

$pwshPath = (Get-Process -Id $PID).Path
Say "controller pid=$PID out=$Out"
Say "chromium=$Chromium"
Say "host=$pwshPath"
Say ("machine before: " + [HeapRig.Rig]::PerformanceLine())

$job = [HeapRig.Rig]::CreateKillOnCloseJob()
Say "job object created (KILL_ON_JOB_CLOSE); every rig process is assigned to it"

$startedPids = [System.Collections.Generic.List[int]]::new()
$stationHandle = [System.IntPtr]::Zero
$desktopHandle = [System.IntPtr]::Zero
$previousStation = [System.IntPtr]::Zero

try {
    if (-not [string]::IsNullOrEmpty($Station)) {
        $stationHandle = [HeapRig.Native]::CreateWindowStationW($Station, 0, [HeapRig.Native]::WINSTA_ALL, [System.IntPtr]::Zero)
        if ($stationHandle -eq [System.IntPtr]::Zero) { throw "CreateWindowStation failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
        Say "created window station '$Station'"
        $previousStation = [HeapRig.Native]::GetProcessWindowStation()
        if (-not [HeapRig.Native]::SetProcessWindowStation($stationHandle)) {
            throw "SetProcessWindowStation failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())"
        }
    }

    if ($UseDefaultDesktop) {
        # The control that says which of this rig's findings belong to the rig:
        # the same command line, on the desktop the suite itself uses. Nothing
        # is consumed here, ever.
        $target = ''
        $currentDesktop = [HeapRig.Native]::GetThreadDesktop([HeapRig.Native]::GetCurrentThreadId())
        $heapKb = [HeapRig.Rig]::HeapSizeKb($currentDesktop)
        Say ("running on the INHERITED desktop '" + [HeapRig.Rig]::NameOf($currentDesktop) + "' -- UOI_HEAPSIZE = $heapKb KB. Nothing is consumed.")
    }
    else {
        $desktopHandle = [HeapRig.Native]::MakeDesktop($Desktop, [HeapRig.Native]::DESKTOP_ALL, [uint32] $HeapKb)
        if ($desktopHandle -eq [System.IntPtr]::Zero) { throw "CreateDesktop failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())" }

        if ($previousStation -ne [System.IntPtr]::Zero) {
            [void][HeapRig.Native]::SetProcessWindowStation($previousStation)
        }

        $heapKb = [HeapRig.Rig]::HeapSizeKb($desktopHandle)
        $stationLabel = if ([string]::IsNullOrEmpty($Station)) { [HeapRig.Rig]::NameOf([HeapRig.Native]::GetProcessWindowStation()) } else { $Station }
        $target = "$stationLabel\$Desktop"
        Say "created desktop '$target' -- UOI_HEAPSIZE = $heapKb KB"
    }

    function Start-Agent([int] $agentId, [int] $agentCap) {
        # Only the filler trips; the prober (id 0) creates nothing and must stay
        # answerable while the heap is gone.
        $trip = if ($agentId -eq 0) { 0 } else { $TripCount }
        $command = '"{0}" -NoProfile -NonInteractive -File "{1}" -Mode Agent -Ipc "{2}" -Id {3} -TitleChars {4} -Cap {5} -TripCount {6}' -f `
            $pwshPath, (Join-Path $here 'Rig.ps1'), $ipcDir, $agentId, $TitleChars, $agentCap, $trip
        $err = 0
        $agentPid = [HeapRig.Rig]::Start($pwshPath, $command, $here, $target, $job, [ref] $err)
        if ($agentPid -eq 0) { return @{ Pid = 0; Error = $err; Filled = '<not started>' } }
        $startedPids.Add($agentPid)

        $deadline = (Get-Date).AddSeconds(90)
        $filledPath = Join-Path $ipcDir ("a{0}.filled" -f $agentId)
        while ((Get-Date) -lt $deadline -and -not (Test-Path $filledPath)) { Start-Sleep -Milliseconds 150 }
        $filled = if (Test-Path $filledPath) { (Get-Content $filledPath -Raw).Trim() } else { '<timed out>' }
        return @{ Pid = $agentPid; Error = 0; Filled = $filled }
    }

    # Agent 0 is the prober. It creates no windows, so it exists before the
    # heap is gone and can still answer afterwards.
    $prober = Start-Agent 0 0
    Say ("prober: pid={0} error={1} :: {2}" -f $prober.Pid, $prober.Error, $prober.Filled)
    if ($prober.Pid -eq 0) { throw "the prober could not be started on $target" }

    function Invoke-Probe([string] $token) {
        $answer = Join-Path $ipcDir ("probed.{0}" -f $token)
        if (Test-Path $answer) { Remove-Item $answer -Force }
        Set-Content -Path (Join-Path $ipcDir ("probe.{0}" -f $token)) -Value 'go' -Encoding utf8
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline -and -not (Test-Path $answer)) { Start-Sleep -Milliseconds 100 }
        if (Test-Path $answer) { return (Get-Content $answer -Raw).Trim() }
        return '<probe timed out>'
    }

    function Invoke-Browser([string] $label, [int] $rep) {
        $profileDir = Join-Path $Out ("profile-{0}-{1}" -f $label, $rep)
        $logPath = Join-Path $Out ("chrome-{0}-{1}.log" -f $label, $rep)
        $null = New-Item -ItemType Directory -Path $profileDir -Force

        $arguments = '"{0}" --headless=new --user-data-dir="{1}" --no-first-run --no-default-browser-check --disable-component-update --enable-logging --log-file="{2}" --v=1 about:blank' -f `
            $Chromium, $profileDir, $logPath

        $launched = [HeapRig.CapturedProcess]::Start($Chromium, $arguments, $Out, $target, $job)
        if (-not $launched.Created) {
            Say ("{0}#{1}: CreateProcessW REFUSED, error={2}" -f $label, $rep, $launched.CreateError)
            return [pscustomobject]@{
                Label = $label; Rep = $rep; Created = $false; CreateError = $launched.CreateError
                Pid = 0; ExitCode = $null; Alive = $false; ElapsedMs = 0; StdOut = ''; StdErr = ''
                Log = ''; LogLines = 0; LastLog = ''; MessageWindows = ''; Performance = ''
            }
        }

        # The transient arm: a real desktop's heap is spent by other processes'
        # windows, which come and go. Hand the heap back while this browser is
        # still starting and see whether a recovered heap changes how it dies.
        if ($ReleaseAfterMs -gt 0) {
            Start-Sleep -Milliseconds $ReleaseAfterMs
            Set-Content -Path (Join-Path $ipcDir 'release') -Value 'go' -Encoding utf8
        }

        # The 2026-08-29 arm: cross the cliff WHILE it is starting. Spun rather
        # than slept -- Start-Sleep's granularity is ~15 ms and the window this
        # is aiming at is 10 to 30.
        if ($TripCount -gt 0) {
            $spin = [System.Diagnostics.Stopwatch]::StartNew()
            while ($spin.Elapsed.TotalMilliseconds -lt $TripAfterMs) { [System.Threading.Thread]::Sleep(0) }
            $gate = [System.Threading.EventWaitHandle]::new($false, [System.Threading.EventResetMode]::ManualReset, $TripName)
            [void]$gate.Set()
            $gate.Dispose()
        }

        # Give it long enough to die the way the measured shape dies (26 ms),
        # then long enough to prove it did not.
        $died = $launched.WaitFor($BrowserWaitMs)
        $performance = [HeapRig.Rig]::PerformanceLine()
        $windows = Invoke-Probe ("{0}-{1}" -f $label, $rep)

        if (-not $died) {
            $launched.Kill()
            [void]$launched.WaitFor(10000)
        }

        $launched.Finish(15000)
        $exitCode = $launched.ExitCode
        $out = $launched.StdOut
        $err = $launched.StdErr
        $elapsed = $launched.ElapsedMs
        $browserPid = $launched.Pid
        $launched.Dispose()

        Start-Sleep -Milliseconds 300
        $logText = if (Test-Path $logPath) { Get-Content $logPath -Raw } else { '<no log file>' }
        $logLines = if (Test-Path $logPath) { @(Get-Content $logPath).Count } else { 0 }
        $lastLine = if ($logLines -gt 0) { (@(Get-Content $logPath))[-1] } else { '' }

        # Seed hypothesis 1, answered with data rather than argument: was a
        # crashpad handler up when this died, and did anything reach the
        # database? A dump in reports\ means the handler was connected AND the
        # death went through it.
        $crashpad = Join-Path $profileDir 'Crashpad'
        $dumps = if (Test-Path (Join-Path $crashpad 'reports')) { @(Get-ChildItem (Join-Path $crashpad 'reports') -File -ErrorAction SilentlyContinue).Count } else { -1 }
        $pending = if (Test-Path (Join-Path $crashpad 'pending')) { @(Get-ChildItem (Join-Path $crashpad 'pending') -File -ErrorAction SilentlyContinue).Count } else { -1 }
        $settings = Test-Path (Join-Path $crashpad 'settings.dat')

        Say ("{0}#{1}: pid={2} died={3} exit={4} elapsedMs={5} stdout={6}B stderr={7}B logLines={8}" -f `
            $label, $rep, $browserPid, $died, $exitCode, [int]$elapsed, $out.Length, $err.Length, $logLines)
        Say ("{0}#{1}: crashpad -> dumps={2} pending={3} settings.dat={4}" -f $label, $rep, $dumps, $pending, $settings)
        Say ("{0}#{1}: message windows -> {2}" -f $label, $rep, ($windows -replace "`r?`n", ' | '))

        return [pscustomobject]@{
            Label = $label; Rep = $rep; Created = $true; CreateError = 0
            Pid = $browserPid; ExitCode = $exitCode; Alive = (-not $died); ElapsedMs = $elapsed
            StdOut = $out; StdErr = $err; Log = $logText; LogLines = $logLines; LastLog = $lastLine
            MessageWindows = $windows; Performance = $performance
            TripCount = $TripCount; TripAfterMs = $TripAfterMs
            CrashpadDumps = $dumps; CrashpadPending = $pending; CrashpadSettings = $settings
        }
    }

    $results = [System.Collections.Generic.List[object]]::new()

    # -------- control BEFORE anything is consumed
    for ($rep = 1; $rep -le $Controls; $rep++) {
        $results.Add((Invoke-Browser 'healthy-before' $rep))
    }

    $fillers = 0
    $totalWindows = 0
    if (-not $NoFill -and $FixedCap -gt 0) {
        # The gradient arm: consume an EXACT number of windows, so the free heap
        # left behind is a number rather than "none".
        Say "filling $FixedCap windows of $TitleChars-character title onto $target ($heapKb KB) ..."
        $filler = Start-Agent 1 $FixedCap
        $fillers = 1
        if ($filler.Filled -match 'created=(\d+)') { $totalWindows = [int]$Matches[1] }
        Say ("filler 1: pid={0} error={1} :: {2}" -f $filler.Pid, $filler.Error, $filler.Filled)
        Say ("machine after fill: " + [HeapRig.Rig]::PerformanceLine())

        for ($rep = 1; $rep -le $Reps; $rep++) {
            $results.Add((Invoke-Browser ("cap" + $FixedCap) $rep))
        }

        Say "releasing the windows"
        Set-Content -Path (Join-Path $ipcDir 'release') -Value 'go' -Encoding utf8
        Start-Sleep -Seconds 3
    }
    elseif (-not $NoFill) {
        Say "filling the desktop heap of $target ($heapKb KB) ..."
        for ($n = 1; $n -le $MaxFillers; $n++) {
            $filler = Start-Agent $n 60000
            $fillers++
            Say ("filler {0}: pid={1} error={2} :: {3}" -f $n, $filler.Pid, $filler.Error, $filler.Filled)
            if ($filler.Pid -eq 0) {
                Say "filler $n could not be STARTED on the desktop at all -- that is itself the ceiling"
                break
            }
            if ($filler.Filled -match 'created=(\d+)') {
                $made = [int]$Matches[1]
                $totalWindows += $made
                if ($made -lt 50) {
                    Say "filler $n created only $made windows -- the desktop heap is spent"
                    break
                }
            }
            else {
                Say "filler $n did not report; stopping the fill"
                break
            }
        }
        Say "fill complete: $fillers filler processes, $totalWindows windows on $target"
        Say ("machine after fill: " + [HeapRig.Rig]::PerformanceLine())

        for ($rep = 1; $rep -le $Reps; $rep++) {
            $results.Add((Invoke-Browser 'exhausted' $rep))
        }

        Say "releasing the windows"
        Set-Content -Path (Join-Path $ipcDir 'release') -Value 'go' -Encoding utf8
        Start-Sleep -Seconds 3

        for ($rep = 1; $rep -le $Controls; $rep++) {
            $results.Add((Invoke-Browser 'healthy-after' $rep))
        }
    }

    $results | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $Out 'results.json') -Encoding utf8
    Say ("machine at end: " + [HeapRig.Rig]::PerformanceLine())
    Say "RESULTS $Out\results.json"
}
finally {
    Say "teardown"
    Set-Content -Path (Join-Path $ipcDir 'stop') -Value 'go' -Encoding utf8
    Start-Sleep -Seconds 2

    foreach ($rigPid in $startedPids) {
        try {
            $survivor = Get-Process -Id $rigPid -ErrorAction SilentlyContinue
            if ($null -ne $survivor) {
                Say "agent pid $rigPid still up; stopping it by pid"
                Stop-Process -Id $rigPid -Force -ErrorAction SilentlyContinue
            }
        }
        catch { Say "could not stop pid $rigPid : $_" }
    }

    if ($desktopHandle -ne [System.IntPtr]::Zero) {
        $closed = [HeapRig.Native]::CloseDesktop($desktopHandle)
        Say "CloseDesktop -> $closed"
    }
    if ($stationHandle -ne [System.IntPtr]::Zero) {
        $closed = [HeapRig.Native]::CloseWindowStation($stationHandle)
        Say "CloseWindowStation -> $closed"
    }

    # Closing the job kills anything the rig started that is somehow still up.
    [void][HeapRig.Native]::CloseHandle($job)
    Say "job closed"

    Say ("window stations now: " + ([HeapRig.Rig]::WindowStations() -join ', '))
    $current = [HeapRig.Native]::GetProcessWindowStation()
    Say ("desktops on " + [HeapRig.Rig]::NameOf($current) + ": " + ([HeapRig.Rig]::Desktops($current) -join ', '))
    Say "done"
}

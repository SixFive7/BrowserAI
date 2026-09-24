# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: the suite's OrphanedConsoleStart shape (LauncherCorpse.Freed), reproduced by hand.
# cmd.exe started CreateNoWindow (a console with no window), two `start /b`s so the product's parent
# is a pid nothing holds, NOTHING redirected, and VELOPACK_FIRSTRUN stated explicitly.
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$Out,
    [switch]$FirstRun,
    [int]$Seconds = 60
)
$ErrorActionPreference = 'Stop'
$lines = [System.Collections.Generic.List[string]]::new()
function Say($s) { $lines.Add("$((Get-Date).ToString('HH:mm:ss.fff')) $s"); [IO.File]::WriteAllLines($Out, $lines) }
try {
    Add-Type -Path 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\Rig.cs'
    $null = New-Item -ItemType Directory -Force -Path $DataRoot
    $psi = [Diagnostics.ProcessStartInfo]::new((Join-Path ([Environment]::SystemDirectory) 'cmd.exe'))
    $psi.Arguments = "/c start /b `"`" cmd.exe /c start /b `"`" `"$Exe`""
    $psi.WorkingDirectory = $DataRoot
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.Environment['BROWSERAI_ROOT'] = $DataRoot
    $psi.Environment['CLAUDE_CONFIG_DIR'] = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\sandbox\client'
    $psi.Environment['CODEX_HOME'] = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\sandbox\codex'
    [void]$psi.Environment.Remove('VELOPACK_RESTART')
    if ($FirstRun) { $psi.Environment['VELOPACK_FIRSTRUN'] = 'true' } else { [void]$psi.Environment.Remove('VELOPACK_FIRSTRUN') }
    Say "launch exe=$Exe firstRun=$FirstRun dataRoot=$DataRoot"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $launcher = [Diagnostics.Process]::Start($psi)
    $launcherStart = $launcher.StartTime
    $launcher.WaitForExit()
    Say "outer cmd pid=$($launcher.Id) exited code=$($launcher.ExitCode) at +$([math]::Round($sw.Elapsed.TotalMilliseconds,1))ms"
    $launcher.Dispose()

    $logs = Join-Path $DataRoot 'logs'
    $identity = $null; $decision = $null
    while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
        if (Test-Path $logs) {
            $text = ''
            foreach ($f in Get-ChildItem $logs -Filter 'browserai-*.log') {
                $fs = [IO.FileStream]::new($f.FullName, 'Open', 'Read', 'ReadWrite,Delete')
                $text += [IO.StreamReader]::new($fs).ReadToEnd(); $fs.Dispose()
            }
            if (-not $identity) {
                $m = [regex]::Match($text, '  pid=(\d+)@(\d+)  BrowserAI\.Startup\[1\][^\n]*image=' + [regex]::Escape($Exe))
                if ($m.Success) { $identity = @([int]$m.Groups[1].Value, [long]$m.Groups[2].Value); Say "identity pid=$($identity[0]) created=$([VR.Rig]::Ft($identity[1]))" }
            }
            if ($identity -and -not $decision) {
                foreach ($id in 'BrowserAI.Startup[8]', 'BrowserAI.Startup[9]', 'Watching the MCP client') {
                    if ($text.Contains($id)) { $decision = $id; Say "decision=$id at +$([math]::Round($sw.Elapsed.TotalMilliseconds,1))ms"; break }
                }
            }
            if ($decision) { break }
        }
        Start-Sleep -Milliseconds 20
    }
    if ($identity) {
        $p = [VR.Proc]::new(); $p.Pid = $identity[0]
        if ([VR.Rig]::Attach($p) -and $p.Created -eq $identity[1]) {
            while ($sw.Elapsed.TotalSeconds -lt $Seconds) { [VR.Rig]::Refresh($p); if ($p.Exited -ne 0) { break }; Start-Sleep -Milliseconds 5 }
            Say "product exited=$([VR.Rig]::Ft($p.Exited)) code=$($p.ExitCode) lifeMs=$([VR.Rig]::Ms($p.Created, $p.Exited))"
        } else { Say 'product already gone before attach (or pid reused): no handle, exit code unread' }
    }
    Say 'RECORDS:'
    foreach ($f in Get-ChildItem $logs -Filter 'browserai-*.log' -ErrorAction SilentlyContinue) {
        $fs = [IO.FileStream]::new($f.FullName, 'Open', 'Read', 'ReadWrite,Delete')
        foreach ($l in ([IO.StreamReader]::new($fs).ReadToEnd() -split "`n")) { if ($l.Trim()) { $lines.Add('  ' + $l.TrimEnd()) } }
        $fs.Dispose()
    }
    Say 'DONE'
} catch { Say "ERROR $_"; Say 'DONE' }

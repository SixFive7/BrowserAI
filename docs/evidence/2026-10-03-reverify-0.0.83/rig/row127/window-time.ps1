# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Row 127's window-time half, 2026-10-03. Opens the configuration app's dialog on
# the screen three times, closing each with WM_CLOSE as soon as it has been seen and
# measured, with the maintainer's approval for this row. The app is the lane's own
# publish of origin/next. CLAUDE_CONFIG_DIR and CODEX_HOME point at empty scratch
# folders, because opening the window reads every client's registration and that
# starts the Codex CLI.
param([int]$Runs = 3)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\stale-scratch'
$out = Join-Path $S 'out\row127'
$claudeDir = Join-Path $S 'clients\claude'; $codexDir = Join-Path $S 'clients\codex'
New-Item -ItemType Directory -Force $out, $claudeDir, $codexDir | Out-Null
Add-Type -Path (Join-Path $S 'rigs\row127\Win.cs')
$exe = 'C:\Source\SixFive7\BrowserAI\.work\wt\stale\src\BrowserAI.App\bin\Release\net10.0-windows\win-x64\publish\BrowserAI.exe'
$log = Join-Path $out 'window-time.log'
function Say($m) { $l = ('[{0:yyyy-MM-ddTHH:mm:ss.fffZ}] {1}' -f (Get-Date).ToUniversalTime(), $m); Add-Content -Path $log -Value $l; Write-Host $l }
Say "exe $exe ($((Get-Item $exe).Length) bytes, $((Get-FileHash $exe).Hash))"

for ($n = 1; $n -le $Runs; $n++) {
  $psi = [System.Diagnostics.ProcessStartInfo]::new($exe)
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.WorkingDirectory = $out
  $psi.Environment['CLAUDE_CONFIG_DIR'] = $claudeDir
  $psi.Environment['CODEX_HOME'] = $codexDir
  $appPid = 0; $dialog = [IntPtr]::Zero; $detail = ''
  $ms = [Win]::Run($psi, 60000, [ref]$appPid, [ref]$dialog, [ref]$detail)
  $seenAt = [DateTime]::UtcNow
  if ($dialog -ne [IntPtr]::Zero) { [void][Win]::Close($dialog) }
  $closedAt = [DateTime]::UtcNow
  Say "run $n pid=$appPid dialogAfterMs=$([Math]::Round($ms,1)) $detail"
  $proc = [Win]::Last
  $exited = $true
  if ($proc) { $exited = $proc.WaitForExit(30000) }
  if (-not $exited) { Say "run $n did NOT exit within 30 s of WM_CLOSE; ending pid $appPid, which this script started"; Stop-Process -Id $appPid -Force }
  $code = if ($proc -and $exited) { $proc.ExitCode } elseif ($proc) { 'ended by the rig' } else { 'gone before the wait' }
  Say "run $n WM_CLOSE posted $([Math]::Round(($closedAt - $seenAt).TotalMilliseconds,1)) ms after the dialog was seen; exit code $code; still alive: $([bool](Get-Process -Id $appPid -ErrorAction SilentlyContinue))"
  Start-Sleep -Milliseconds 1500
}
Say "scratch CODEX_HOME now holds: $((Get-ChildItem -Recurse -Force $codexDir | Measure-Object).Count) entries; scratch CLAUDE_CONFIG_DIR: $((Get-ChildItem -Recurse -Force $claudeDir | Measure-Object).Count)"
Say 'done'

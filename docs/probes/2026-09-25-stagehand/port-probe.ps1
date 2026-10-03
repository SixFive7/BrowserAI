# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param([int]$ParentPid)
$all = Get-CimInstance Win32_Process
$b = $all | Where-Object { $_.ParentProcessId -eq $ParentPid -and $_.CommandLine -notmatch '--type=' -and $_.ExecutablePath -like '*chrome.exe' } | Select-Object -First 1
$cmd = $b.CommandLine
$port = [regex]::Match($cmd, '--remote-debugging-port=(\d+)').Groups[1].Value
$l = Get-NetTCPConnection -OwningProcess $b.ProcessId -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort
[pscustomobject]@{
  browserPid = $b.ProcessId
  port = $port
  listeners = @($l)
  remoteAllowOrigins = [regex]::Match($cmd, '--remote-allow-origins=\S+').Value
  unsafeExtensionDebugging = ($cmd -match '--enable-unsafe-extension-debugging')
  remoteDebuggingPipe = ($cmd -match '--remote-debugging-pipe')
  headless = [regex]::Match($cmd, '--headless\S*').Value
} | ConvertTo-Json -Depth 4 -Compress

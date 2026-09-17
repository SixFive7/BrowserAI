# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
<#
.SYNOPSIS
  Re-establishes the first-run provisioning figures in
  kb/playwright/provisioning-and-timings.md (re-verification row 21): one family
  into an empty PLAYWRIGHT_BROWSERS_PATH, timed end to end, then summed on disk.

.NOTES
  Nothing here touches %LocalAppData%\BrowserAI or %LocalAppData%\BrowserAI.app.
  The browsers root is the -Root argument and nothing else. TEMP is left alone,
  which is the predicate both earlier measurements were taken under; the product
  additionally redirects TEMP/TMP into the root, which the 2026-08-19 run proved
  produces a byte-identical tree.
#>
param(
  [Parameter(Mandatory)][ValidateSet('chromium', 'firefox')][string]$Family,
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$Payload,
  [Parameter(Mandatory)][string]$OutJson
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $Root) { Remove-Item -LiteralPath $Root -Recurse -Force }
New-Item -ItemType Directory -Path $Root -Force | Out-Null

$node = Join-Path $Payload 'node\node.exe'
$cli = Join-Path $Payload 'mcp\node_modules\@playwright\mcp\cli.js'
$log = "$OutJson.log"

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $node
foreach ($a in @($cli, 'install-browser', $Family, '--no-shell', '--no-progress')) { $psi.ArgumentList.Add($a) }
$psi.WorkingDirectory = $Root
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true          # house rule: every launch suppresses the console window
$psi.EnvironmentVariables['PLAYWRIGHT_BROWSERS_PATH'] = $Root

$out = [System.Text.StringBuilder]::new()
$err = [System.Text.StringBuilder]::new()
$p = [System.Diagnostics.Process]::new()
$p.StartInfo = $psi
$p.EnableRaisingEvents = $true
$null = Register-ObjectEvent -InputObject $p -EventName OutputDataReceived -MessageData $out -Action {
  if ($null -ne $EventArgs.Data) { [void]$Event.MessageData.AppendLine("$([DateTime]::UtcNow.ToString('o'))  $($EventArgs.Data)") } }
$null = Register-ObjectEvent -InputObject $p -EventName ErrorDataReceived -MessageData $err -Action {
  if ($null -ne $EventArgs.Data) { [void]$Event.MessageData.AppendLine("$([DateTime]::UtcNow.ToString('o'))  $($EventArgs.Data)") } }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
[void]$p.Start()
$p.BeginOutputReadLine(); $p.BeginErrorReadLine()
$p.WaitForExit()          # bare, so the async readers drain
$sw.Stop()
Start-Sleep -Milliseconds 400
$exit = $p.ExitCode

"$($out.ToString())`n--- stderr ---`n$($err.ToString())" | Set-Content -LiteralPath $log -Encoding utf8

$sep = [System.IO.Path]::DirectorySeparatorChar
$files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force)
$byDir = @($files | Group-Object { $_.FullName.Substring($Root.Length).TrimStart($sep).Split($sep)[0] } |
  ForEach-Object {
    [pscustomobject]@{ name = $_.Name; bytes = ($_.Group | Measure-Object Length -Sum).Sum; files = $_.Count }
  })

[pscustomobject]@{
  family     = $Family
  root       = $Root
  exitCode   = $exit
  seconds    = [math]::Round($sw.Elapsed.TotalSeconds, 2)
  totalBytes = ($files | Measure-Object Length -Sum).Sum
  totalFiles = $files.Count
  topLevel   = @(Get-ChildItem -LiteralPath $Root -Force | ForEach-Object { $_.Name }) -join ','
  components = @($byDir | Sort-Object name)
  utc        = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutJson -Encoding utf8

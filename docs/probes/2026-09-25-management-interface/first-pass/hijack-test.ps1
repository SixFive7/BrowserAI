# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Same-user test of http.sys routing: process A (our probe) registers http://127.0.0.1:P/,
# this script registers the LONGER prefix http://127.0.0.1:P/api/ on the same port,
# and a request to /api/state is sent to see which of the two receives it.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$portFile = Join-Path $root 'run\hijack-A.port'
Remove-Item -LiteralPath $portFile -ErrorAction SilentlyContinue
$psi = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $root 'out\httplistener\httplistener.exe'))
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.ArgumentList.Add($portFile); $psi.ArgumentList.Add('127.0.0.1')
$a = [System.Diagnostics.Process]::Start($psi)
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while (-not (Test-Path -LiteralPath $portFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 20 }
$port = [int]((Get-Content -LiteralPath $portFile -Raw).Split("`t")[0])
"A (probe pid $($a.Id)) registered http://127.0.0.1:$port/"
$b = [System.Net.HttpListener]::new()
$b.Prefixes.Add("http://127.0.0.1:$port/api/")
try { $b.Start(); "B (this PowerShell, pid $PID) registered http://127.0.0.1:$port/api/ : OK" } catch { "B registration refused: $($_.Exception.Message)" }
$client = [System.Net.Http.HttpClient]::new()
$task = $client.GetAsync("http://127.0.0.1:$port/api/state")
if ($b.IsListening) {
  $ctxTask = $b.GetContextAsync()
  if ($ctxTask.Wait(3000)) {
    $ctx = $ctxTask.Result
    "B RECEIVED: $($ctx.Request.HttpMethod) $($ctx.Request.Url.AbsolutePath)"
    $bytes = [Text.Encoding]::UTF8.GetBytes('{"answeredBy":"B, the second registration"}')
    $ctx.Response.ContentLength64 = $bytes.Length; $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length); $ctx.Response.Close()
  } else { "B received nothing within 3 s" }
}
if ($task.Wait(5000)) { $body = $task.Result.Content.ReadAsStringAsync().Result; "client got $([int]$task.Result.StatusCode): $($body.Substring(0, [Math]::Min(60, $body.Length)))" } else { 'client: no answer' }
if ($b.IsListening) { $b.Stop() }; $b.Close()
$quit = $client.GetAsync("http://127.0.0.1:$port/quit"); [void]$quit.Wait(3000)
if (-not $a.WaitForExit(5000)) { $a.Kill(); $a.WaitForExit() }
"A exited $($a.ExitCode)"

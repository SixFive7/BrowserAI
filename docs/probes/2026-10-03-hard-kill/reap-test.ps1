# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: runs the exact script ServerRegistryReap.ScriptFor builds
# (src/BrowserAI/Runtime/ServerRegistryReap.cs:155-162) against the registry directories the
# MCP-path runs left behind, with PWTEST_SERVER_REGISTRY pointing at each one in turn.
# Counts descriptors before and after. Touches only directories under this scratch.
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\hard-kill'
$node = 'C:\Source\SixFive7\BrowserAI\payload\node\node.exe'
$module = 'C:\Source\SixFive7\BrowserAI\payload\mcp\node_modules\playwright-core\lib\serverRegistry.js'
$script = "require('" + $module.Replace('\', '/').Replace("'", "\'") + "').serverRegistry.list().then(() => process.exit(0), () => process.exit(1))"
$rows = @()
foreach ($run in Get-ChildItem -LiteralPath (Join-Path $root 'runs') -Directory | Where-Object { $_.Name -like '*-mcp-*' }) {
    foreach ($sub in 'w-reg', 'r-reg') {
        $dir = Join-Path $run.FullName $sub
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $before = @(Get-ChildItem -LiteralPath $dir -File -Force).Count
        if ($before -eq 0) { $rows += "{0}`t{1}`t0`t0`t-`t-" -f $run.Name, $sub; continue }
        $psi = [System.Diagnostics.ProcessStartInfo]::new($node)
        $psi.ArgumentList.Add('-e'); $psi.ArgumentList.Add($script)
        $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
        $psi.Environment['PWTEST_SERVER_REGISTRY'] = $dir
        $psi.WorkingDirectory = $root
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $p = [System.Diagnostics.Process]::Start($psi)
        $err = $p.StandardError.ReadToEndAsync(); $o = $p.StandardOutput.ReadToEndAsync()
        if (-not $p.WaitForExit(60000)) { $rows += "{0}`t{1}`t{2}`t?`ttimeout`t-" -f $run.Name, $sub, $before; continue }
        $p.WaitForExit()
        $after = @(Get-ChildItem -LiteralPath $dir -File -Force).Count
        $rows += "{0}`t{1}`t{2}`t{3}`texit={4}`t{5}ms" -f $run.Name, $sub, $before, $after, $p.ExitCode, $sw.ElapsedMilliseconds
    }
}
$hdr = "run`tdir`tdescriptorsBefore`tdescriptorsAfter`treaper`telapsed"
@($hdr) + $rows | Set-Content -LiteralPath (Join-Path $root 'results\reap-test.tsv') -Encoding utf8
@($hdr) + $rows

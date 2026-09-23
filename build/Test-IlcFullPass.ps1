# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Refuses a publish log that shows no full ILC pass.

.DESCRIPTION
    HALT-A -- the ILC-output scan in New-Release.ps1 -- reads ILC's own console
    output and fails the release on anything it complains about. That check has a
    premise: that ILC ran. `IlcCompile` is an MSBuild target with Inputs and
    Outputs, so a publish whose managed assemblies have not moved SKIPS it and
    relinks last run's native object file. The publish then succeeds, the binary
    is fine, and the scan sweeps a log ILC never wrote -- a check that cannot
    fail, reporting clean.

    Measured 2026-09-15 on Windows 11 Pro 26200, SDK 10.0.400, ILC 10.0.12, at
    `-v:normal`, over `dotnet publish src/BrowserAI/BrowserAI.csproj -c Release
    -r win-x64 --self-contained`:

      full pass          95 lines   `IlcCompile:` + `Generating native code` + the ilc invocation
      incremental        75 lines   `Skipping target "IlcCompile" because all output files are up-to-date`

    The MARKER is asserted and not the line count. A count is a property of
    the verbosity, the project and the SDK all at once, and the one thing it
    would not survive is the thing this check exists for: a future publish that
    legitimately prints more. `Generating native code` is ILC's own line and is
    printed when, and only when, the compilation happens.

.PARAMETER Log
    The publish log to read.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Log
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'
$ErrorView = 'NormalView'

if (-not (Test-Path -LiteralPath $Log)) {
    Write-Error "There is no publish log at $Log, so whether ILC ran cannot be established."
    exit 1
}

$text = Get-Content -LiteralPath $Log -Raw

# ILC's own line, and the target header that carries it. Both, because the
# header alone appears in a skip message too.
$compiled = ($text -match '(?m)^\s*Generating native code\s*$') -and ($text -match '(?m)^\s*IlcCompile:\s*$')
$skipped = $text -match 'Skipping target "IlcCompile"'

if ($compiled) {
    Write-Host "ILC ran a full pass ($((Get-Content -LiteralPath $Log).Count) lines read)."
    exit 0
}

$why = if ($skipped) {
    'the log says "Skipping target ""IlcCompile"" because all output files are up-to-date", so the native object was relinked from a previous run'
} else {
    'the log carries neither "IlcCompile:" nor "Generating native code"'
}

Write-Error ("This publish did not compile: $why. HALT-A reads ILC's console output, so it would be scanning a " +
    "compilation that did not happen and could not fail. Remove src\BrowserAI\obj\Release\*\win-x64\native and publish again. Log: $Log")
exit 1

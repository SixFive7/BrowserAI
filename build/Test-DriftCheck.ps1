# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    The release rule that every dependency is the latest: the drift check was
    taken today, and no row of it records a drift.

.DESCRIPTION
    The maintainer's rule, 2026-10-10, verbatim: "Before we cut any realease all
    dependencies should always be checked if they are on the latest version.
    Part of the upstream checks we already do."

    The check itself is the daily drift check AGENTS.md describes: a person or an
    agent resolves the six upstreams and the vendored SQLite row the way the
    build resolves them and writes what came back into drift-check.json. That
    reading needs the network and judgement, so it is not repeated here. What is
    here is the refusal: a release is cut only from a tree whose drift check

      * is stamped today, by the local clock the check is stamped by, and
      * has no row, in `resolved` or in `vendored`, that records a drift.

    A drift is not adopted by this script. A row that drifted goes through
    UPSTREAM-REVIEW.md, or for the vendored SQLite through the steps the row's
    own `how` names, and the check is then taken again.

    It is a separate script from New-Release.ps1 for the reason
    Test-ReleaseVersion.ps1 is: the suite has to be able to drive it.

.PARAMETER DriftCheck
    The file to read. Defaults to drift-check.json at the repository root.

.PARAMETER Today
    The date lastChecked has to be, as yyyy-MM-dd. Defaults to today by the local
    clock. The suite passes one so that its arms do not depend on the day.

.OUTPUTS
    `current` when the release may go ahead. A refusal is a non-zero exit and a
    message naming every reason.
#>
[CmdletBinding()]
param(
    [string] $DriftCheck,
    [string] $Today
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'
$ErrorView = 'NormalView'

if (-not $DriftCheck) {
    $DriftCheck = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' 'drift-check.json'))
}

if (-not $Today) {
    $Today = (Get-Date).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
}

if (-not (Test-Path -LiteralPath $DriftCheck -PathType Leaf)) {
    Write-Error "There is no drift check at '$DriftCheck', so nothing says the dependencies are the latest. Take the daily drift check AGENTS.md describes, write it there, and cut again."
    exit 1
}

try {
    $record = Get-Content -LiteralPath $DriftCheck -Raw | ConvertFrom-Json -DateKind String
}
catch {
    Write-Error "'$DriftCheck' could not be read as JSON ($($_.Exception.Message)), so nothing says the dependencies are the latest."
    exit 1
}

$reasons = [System.Collections.Generic.List[string]]::new()
$names = $record.PSObject.Properties.Name

$stamped = if ($names -contains 'lastChecked') { [string] $record.lastChecked } else { '' }

if ($stamped -ne $Today) {
    $said = if ($stamped) { "was last taken on $stamped" } else { 'carries no lastChecked' }
    $reasons.Add("The drift check $said and today is $Today. Take it again today, every upstream and the vendored row, before cutting.")
}

$rows = 0

foreach ($section in @('resolved', 'vendored')) {
    if ($names -notcontains $section) {
        $reasons.Add("drift-check.json has no '$section' section, so the rows it should hold were never read.")
        continue
    }

    foreach ($row in $record.$section.PSObject.Properties) {
        $rows++
        $fields = $row.Value.PSObject.Properties.Name

        if ($fields -notcontains 'drift') {
            $reasons.Add("The $section row '$($row.Name)' does not say whether it drifted, so it says nothing about being the latest.")
            continue
        }

        if ($row.Value.drift -ne $false) {
            $now = if ($fields -contains 'resolved') { " upstream is at $($row.Value.resolved)" } else { '' }
            $ours = if ($fields -contains 'reviewed') { ", the tree is reviewed at $($row.Value.reviewed)" } elseif ($fields -contains 'pinned') { ", the tree is pinned at $($row.Value.pinned)" } else { '' }
            $reasons.Add("The $section row '$($row.Name)' records a drift:$now$ours. Adopt it first: UPSTREAM-REVIEW.md for an upstream, the row's own 'how' for the vendored source.")
        }
    }
}

if ($rows -eq 0) {
    $reasons.Add('drift-check.json holds no rows at all, so nothing was checked.')
}

if ($reasons.Count -gt 0) {
    Write-Error ("A release takes only the latest of every dependency, and '$DriftCheck' does not say that it has them:`n  " + ($reasons -join "`n  "))
    exit 1
}

'current'

# ⚠️ AND AN EXIT CODE, ADDED 2026-10-10 BY LANE FINAL. New-Release.ps1 calls this
# before any native command has run, then reads $LASTEXITCODE under StrictMode
# Latest. A script that ends without `exit` sets no exit code, so every release
# and every dev pack over a CURRENT check died at that line with "The variable
# '$LASTEXITCODE' cannot be retrieved because it has not been set." Only the
# refusal had been run end to end. Test-ReleaseVersion.ps1 ends the same way.
exit 0

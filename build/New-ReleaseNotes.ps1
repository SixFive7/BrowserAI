# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Turns one version's CHANGELOG.md section into the body of a GitHub release.

.DESCRIPTION
    A release body is not a changelog section. The section is the record -- every
    fact, every stamp, every link, written as the work landed -- and the body is
    what somebody reads once, on the release page, to find out whether they want
    this version. Until 2026-09-15 the body WAS the section, cut at a heading
    boundary because 236,567 characters do not fit in a field that holds 125,000,
    and what a reader met first was seventy screens of the middle of an argument.

    This reads the shape CHANGELOG.md is written in --

        - <icon> **One-sentence headline.** the whole of what happened

    -- and emits the headline as the line and the rest behind a `read more`
    fold. The icon is the entry's own; nothing here chooses one.

    THE SIZE GUARD IS NOT AN ESTIMATE. GitHub's release body limit is 125,000
    characters ([FLOATS]: it is their field, not ours, and nothing here can make
    them keep it). A body over the limit falls back to headlines alone plus the
    footer, which is an order of magnitude smaller, and the script SAYS WHICH
    SHAPE IT PRODUCED. A generator that silently produced a different document
    on a long release is the failure this whole change exists to stop.

    THE ANCHOR IS COMPUTED THE WAY GITHUB COMPUTES IT, by the same rule
    DocumentationLinkTests applies to every relative link in this repository --
    a link becomes its own text, inline HTML disappears, code and emphasis
    markers are dropped, then lower-case; letters, digits, hyphens and
    underscores survive, spaces become hyphens, everything else is dropped.
    `ChangelogTests` holds the two implementations against each other, because a
    footer link nobody checks is exactly the link that rots.

.PARAMETER Path
    The changelog. Defaults to CHANGELOG.md at the repository root.

.PARAMETER Version
    The version whose section becomes the body, bare and without the tag's `v`.

.PARAMETER Destination
    Where the body is written. The file is UTF-8 without a BOM and LF, which is
    what `gh release create --notes-file` sends.

.PARAMETER Limit
    The size at which the folded shape is abandoned for headlines alone.
    Defaults to GitHub's documented 125,000 characters; a parameter so a test
    can drive the fallback without a 125,000-character fixture.

.PARAMETER Repository
    The repository the footer link points into.

.EXAMPLE
    pwsh -File build/New-ReleaseNotes.ps1 -Version 1.0.0 -Destination .work/body.md
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path $PSScriptRoot '..' 'CHANGELOG.md'),
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Destination,
    [int] $Limit = 125000,
    [string] $Repository = 'https://github.com/SixFive7/BrowserAI'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# The same two lines every script in build/ carries: a redirected stream gets no
# ANSI colour, and a refusal is the sentence rather than caret art around it.
$PSStyle.OutputRendering = 'PlainText'
$ErrorView = 'NormalView'

$Path = [System.IO.Path]::GetFullPath($Path)

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "There is no changelog at '$Path', so there is nothing to make a release body out of."
    exit 1
}

$content = (Get-Content -LiteralPath $Path -Raw) -replace "`r`n", "`n"

# --- The version's section -----------------------------------------------------
$heading = [regex]::Match(
    $content,
    '(?m)^\#\#[ \t]+\[' + [regex]::Escape($Version) + '\][^\n]*$')

if (-not $heading.Success) {
    Write-Error "'$Path' has no '## [$Version]' section. The body is generated from the section the release is cut from, so a missing section is a missing release note rather than an empty one."
    exit 1
}

$rest = $content.Substring($heading.Index + $heading.Length)
$next = [regex]::Match($rest, '(?m)^\#\#[ \t]')
$section = if ($next.Success) { $rest.Substring(0, $next.Index) } else { $rest }

# --- The legend, read out of the changelog rather than written here ------------
# Two copies of a palette would eventually disagree, and the one in the file is
# the one a reader of the changelog sees.
$head = $content.Substring(0, [regex]::Match($content, '(?m)^\#\#[ \t]').Index)
$legendBlock = ($head -split "`n`n" | Where-Object { $_ -match '·' } | Select-Object -Last 1)

if (-not $legendBlock) {
    Write-Error "'$Path' carries no palette legend before its first version heading, so the release body has no legend to end with. The legend is the paragraph of icon descriptions separated by '·'."
    exit 1
}

$legend = (($legendBlock -split "`n" | ForEach-Object { $_.Trim() }) -join ' ').Trim()

# --- The anchor, by GitHub's rule ----------------------------------------------
function Get-GitHubAnchor {
    param([Parameter(Mandatory)] [string] $Heading)

    # CODE SPANS FIRST, emptied of their angle brackets: to a renderer
    # `<args>` inside a span is four literal characters, to an HTML stripper it
    # is a tag, and stripping tags first makes the two rules disagree.
    $text = [regex]::Replace($Heading, '`([^`]*)`', {
        param($match)
        $match.Groups[1].Value -replace '[<>]', ''
    })

    $text = [regex]::Replace($text, '\[([^\]]*)\]\([^)]*\)', '$1')   # a link is its own text
    $text = [regex]::Replace($text, '<[^>]*>', '')                   # inline HTML disappears
    # ONLY the backtick and the asterisk, which is exactly what the C# side
    # strips: GitHub KEEPS an underscore in a slug, so removing one here would
    # be a rule that is nearly right and disagrees on one heading in a hundred.
    $text = [regex]::Replace($text, '[`*]', '')                      # emphasis and code markers

    $slug = [System.Text.StringBuilder]::new()

    foreach ($character in $text.Trim().ToCharArray()) {
        if ([char]::IsLetterOrDigit($character) -or $character -eq '-' -or $character -eq '_') {
            $null = $slug.Append([char]::ToLowerInvariant($character))
        }
        elseif ($character -eq ' ' -or $character -eq "`t") {
            $null = $slug.Append('-')
        }
        else {
            $category = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($character)
            if ($category -eq [System.Globalization.UnicodeCategory]::NonSpacingMark -or
                $category -eq [System.Globalization.UnicodeCategory]::SpacingCombiningMark -or
                $category -eq [System.Globalization.UnicodeCategory]::EnclosingMark) {
                $null = $slug.Append($character)
            }
        }
    }

    return $slug.ToString()
}

$anchor = Get-GitHubAnchor -Heading ($heading.Value -replace '^\#\#[ \t]+', '')
$permalink = "$Repository/blob/v$Version/CHANGELOG.md#$anchor"

# --- The section's own parts ---------------------------------------------------
$lines = $section -split "`n"
$preamble = [System.Collections.Generic.List[string]]::new()
$groups = [System.Collections.Generic.List[object]]::new()
$group = $null
$entry = $null

function Complete-Entry {
    if ($null -eq $script:entry) { return }

    # WARNING: AN ENTRY BEFORE THE FIRST '### ' HEADING -- 2026-09-16. $group is
    # $null here, and `$null.Entries.Add(...)` under Set-StrictMode throws
    # "You cannot call a method on a null-valued expression", which names a
    # variable nobody reading a changelog has heard of. The body is generated
    # per group, so an entry outside one has nowhere to go; that is the sentence
    # to print.
    if ($null -eq $script:group) {
        Write-Error "The [$Version] section has an entry before its first '### ' group heading: '$($script:entry.Lines[0].Trim())'. Every entry belongs to a Keep a Changelog group -- Added, Changed, Deprecated, Removed, Fixed or Security -- because the release body is generated group by group, so an entry above the first heading has nowhere to be written."
        exit 1
    }

    $script:group.Entries.Add($script:entry)
    $script:entry = $null
}

foreach ($line in $lines) {
    if ($line -match '^\#\#\#[ \t]+(?<name>.+?)[ \t]*$') {
        Complete-Entry
        $group = [pscustomobject]@{ Name = $Matches['name']; Entries = [System.Collections.Generic.List[object]]::new() }
        $groups.Add($group)
        continue
    }

    if ($line -match '^-[ \t]') {
        Complete-Entry
        $entry = [pscustomobject]@{ Lines = [System.Collections.Generic.List[string]]::new() }
        $entry.Lines.Add($line)
        continue
    }

    if ($null -ne $entry) {
        if ($line.Trim().Length -eq 0 -or $line -match '^[ \t]') {
            $entry.Lines.Add($line)
            continue
        }
        Complete-Entry
        continue
    }

    if ($null -eq $group) {
        $preamble.Add($line)
        continue
    }

    # WARNING: A PARAGRAPH UNDER A GROUP HEADING -- 2026-09-16. It is not a
    # preamble (that is above the first heading), it is not an entry, and there
    # is nowhere in a folded body for it: every group renders as its heading and
    # its entries. It used to be DROPPED here, silently, and a release body that
    # quietly omits a paragraph somebody wrote is worse than one that refuses.
    # Refusing rather than carrying is the choice: inventing a rendering for a
    # shape nothing else in this repository reads would make the changelog's
    # format wider than the one ChangelogTests holds it to.
    if ($line.Trim().Length -gt 0) {
        Write-Error "The [$Version] section has a paragraph under the '### $($group.Name)' heading that is not an entry: '$($line.Trim())'. A group holds entries and nothing else -- prose belongs in the section's preamble, above the first '### ' heading, which is where the body renders it."
        exit 1
    }
}

Complete-Entry

# --- Each entry, split at its headline -----------------------------------------
# The shape is the changelog's own and is asserted there; a line that does not
# carry it is a defect in the changelog rather than something to paper over.
$rendered = [System.Collections.Generic.List[object]]::new()

foreach ($g in $groups) {
    $items = [System.Collections.Generic.List[object]]::new()

    foreach ($e in $g.Entries) {
        $whole = ($e.Lines -join "`n")
        $match = [regex]::Match($whole, '(?s)^-[ \t]+(?<icon>\S+)[ \t]+\*\*(?<headline>.+?)\*\*[ \t]*(?<detail>.*)$')

        if (-not $match.Success) {
            $first = ($e.Lines | Select-Object -First 1)
            Write-Error "An entry in '## [$Version]' is not written in the shape a release body is generated from -- '- <icon> **One-sentence headline.** the rest'. The line is: $first"
            exit 1
        }

        # Paragraphs, unwrapped: the changelog is hard-wrapped at 80 columns for
        # a reader of the file, and a release body is read in a browser column.
        $detail = ($match.Groups['detail'].Value -split "`n[ \t]*`n" |
            ForEach-Object { ($_ -replace '\s+', ' ').Trim() } |
            Where-Object { $_.Length -gt 0 })

        $items.Add([pscustomobject]@{
            Icon     = $match.Groups['icon'].Value
            Headline = ($match.Groups['headline'].Value -replace '\s+', ' ').Trim()
            Detail   = @($detail)
        })
    }

    $rendered.Add([pscustomobject]@{ Name = $g.Name; Items = $items })
}

# --- The body ------------------------------------------------------------------
function New-Body {
    param([bool] $Folded)

    $out = [System.Collections.Generic.List[string]]::new()

    # Unwrapped, like every detail below: the changelog is hard-wrapped at 80
    # columns for whoever reads the file, and a release body is read in a
    # browser column that is not 80 characters wide.
    $intro = (($preamble -join "`n") -split "`n[ 	]*`n" |
        ForEach-Object { ($_ -replace '\s+', ' ').Trim() } |
        Where-Object { $_.Length -gt 0 })

    foreach ($paragraph in $intro) {
        $out.Add($paragraph)
        $out.Add('')
    }

    foreach ($g in $rendered) {
        $out.Add("### $($g.Name)")
        $out.Add('')

        foreach ($item in $g.Items) {
            $out.Add("- $($item.Icon) **$($item.Headline)**")

            if ($Folded -and $item.Detail.Count -gt 0) {
                # Two spaces, so the block belongs to the list ITEM rather than
                # ending the list; a blank line on each side, so what is between
                # <summary> and </summary> is parsed as Markdown rather than as
                # the inside of an HTML block.
                $out.Add('')
                $out.Add('  <details><summary>read more</summary>')
                $out.Add('')
                foreach ($paragraph in $item.Detail) {
                    $out.Add("  $paragraph")
                    $out.Add('')
                }
                $out.Add('  </details>')
            }

            $out.Add('')
        }
    }

    $out.Add('---')
    $out.Add('')
    $out.Add($legend)
    $out.Add('')
    # ⚠️ The two halves of the link are never adjacent in this SOURCE, and that
    # is deliberate rather than fussy: DocumentationLinkTests reads every file in
    # the tree as text, a `](` in a script is a relative link to it, and
    # `$permalink` is not a path that exists.
    $out.Add('Every entry in full, with its evidence: [CHANGELOG.md]' + '(' + $permalink + ')')

    return (($out -join "`n") -replace "`n{3,}", "`n`n").Trim() + "`n"
}

$body = New-Body -Folded $true
$shape = 'folded'

if ($body.Length -gt $Limit) {
    $body = New-Body -Folded $false
    $shape = 'headlines'
}

$Destination = [System.IO.Path]::GetFullPath($Destination)
$directory = Split-Path -Parent $Destination

if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    $null = New-Item -ItemType Directory -Force -Path $directory
}

# LF and no BOM: `gh release create --notes-file` sends the bytes as they are.
[System.IO.File]::WriteAllText($Destination, $body, (New-Object System.Text.UTF8Encoding($false)))

$entries = ($rendered | ForEach-Object { $_.Items.Count } | Measure-Object -Sum).Sum

if ($shape -eq 'headlines') {
    Write-Host "The folded body for $Version did not fit in $Limit characters, so this one is HEADLINES ONLY: $($body.Length) characters, $entries entries, $($rendered.Count) groups. The detail is not in the release body at all -- the footer link is how a reader reaches it."
}
else {
    Write-Host "Release body for $Version is FOLDED: $($body.Length) characters against a limit of $Limit, $entries entries, $($rendered.Count) groups, each detail behind a 'read more'."
}

Write-Host "Wrote $Destination."

# Last, and to stdout: the shape and the size, for whoever is scripting this.
"$shape $($body.Length)"

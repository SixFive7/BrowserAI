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

    -- and emits ONE SHAPE for every release: the headline as the line, and a
    `read more` link beside it carrying the entry's own LINE RANGE in the tagged
    changelog. The icon is the entry's own; nothing here chooses one.

    ⚠️ ONE SHAPE, AND THE FOLD IS GONE -- 2026-09-16, the maintainer's choice
    (Q197 b). Previously each detail was emitted behind an HTML `<details>` fold
    and the whole body fell back to headlines-only over the size limit, so a
    reader met one of two documents depending on how much had happened that
    release. The fold was also the thing that made the body long: the detail was
    in it twice over, once in the release and once in the file it was copied
    from. A line range points AT the record instead of copying it, so the body
    stays a page whatever the release holds, and `?plain=1#L<first>-L<last>`
    lands a reader on the exact lines with the source view's own highlight.

    ⚠️ THE LINE NUMBERS ARE ONLY TRUE OF ONE FILE, which is why this refuses
    rather than guesses. They are computed from the changelog on disk and read
    against the changelog the tag carries, so the two must be the same document:
    the working copy must match HEAD, and a tag `v<version>`, if it exists, must
    be at HEAD. Either failing is a refusal naming both. A dirty tree ELSEWHERE
    is reported rather than refused -- this runs inside `New-Release.ps1` after a
    publish that can leave restore artifacts behind, and none of those can move a
    line number in a file that matches HEAD.

    THE SIZE GUARD IS NOT AN ESTIMATE. GitHub's release body limit is 125,000
    characters ([FLOATS]: it is their field, not ours, and nothing here can make
    them keep it). A body over the limit falls back to the headlines with no
    per-entry links at all, leaving the footer's section link as the only way in,
    and the script SAYS WHICH SHAPE IT PRODUCED. It is a pathological fallback
    rather than a second design: the linked shape is roughly a tenth of the size
    the folded one was, so reaching the limit now takes a release of a size this
    project has never cut.

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
    The size at which the per-entry links are abandoned for headlines alone.
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

# --- The line numbers have to be the tag's ------------------------------------
# ⚠️ EVERY ENTRY LINK IS A LINE RANGE INTO A TAGGED FILE, so a body generated
# from a changelog that differs from the one the tag carries points at the wrong
# lines -- silently, and in a document nobody re-reads. The link still resolves,
# still highlights, and highlights something else.
#
# What is asserted is the property itself rather than a proxy for it: the file
# this reads is byte-for-byte what HEAD holds, and when a tag `v<version>` exists
# it is at HEAD. A dirty tree ELSEWHERE is reported rather than refused -- this
# script runs inside `New-Release.ps1` step 9, after a publish that can leave
# restore artifacts in the working tree, and refusing on those would stop a
# release for something that cannot move one line number.
#
# Outside a repository there is nothing to check and nothing to claim. It says
# so, in the same sentence it says what it produced, rather than passing
# silently.
function Invoke-Git {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $output = & git @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (($output | Out-String).Trim()) }
}

$directory = Split-Path -Parent $Path
$inRepository = $false
$provenance = "'$Path' is not in a git repository, so nothing here pins these line numbers to a tag."

if (Get-Command git -CommandType Application -ErrorAction SilentlyContinue) {
    $inside = Invoke-Git @('-C', $directory, 'rev-parse', '--is-inside-work-tree')
    $inRepository = ($inside.ExitCode -eq 0) -and ($inside.Output -eq 'true')
}

if ($inRepository) {
    $relative = (Invoke-Git @('-C', $directory, 'ls-files', '--full-name', '--error-unmatch', $Path))

    # ⚠️ TRACKED OR NOT IS A STATE, NOT A FAILURE. An untracked changelog has no
    # committed version for anything to be compared against, so there is nothing
    # to claim and nothing to refuse -- which is exactly a fixture under a
    # gitignored scratch directory. It is announced rather than passed over: the
    # provenance line says the numbers are pinned to nothing.
    if ($relative.ExitCode -ne 0) {
        $inRepository = $false
        $provenance = "'$Path' is not tracked by the repository it sits in, so nothing here pins these line numbers to a tag."
    }
}

if ($inRepository) {

    $head = (Invoke-Git @('-C', $directory, 'rev-parse', 'HEAD')).Output
    $tagged = Invoke-Git @('-C', $directory, 'rev-parse', '--verify', '--quiet', "refs/tags/v$Version^{commit}")
    $modified = Invoke-Git @('-C', $directory, 'diff', '--quiet', 'HEAD', '--', $Path)
    $dirty = (Invoke-Git @('-C', $directory, 'status', '--porcelain')).Output

    if ($modified.ExitCode -ne 0) {
        Write-Error ("Every entry link in this body is a line range into '$($relative.Output)' as the tag carries it, and the working copy of that file differs from HEAD ($head)." +
            " The numbers would be computed from one document and read against another -- the links would resolve and highlight the wrong lines." +
            " Commit the changelog first." +
            " The tag 'v$Version' " + $(if ($tagged.ExitCode -eq 0) { "is at $($tagged.Output)." } else { 'does not exist yet.' }))
        exit 1
    }

    if (($tagged.ExitCode -eq 0) -and ($tagged.Output -ne $head)) {
        Write-Error ("The tag 'v$Version' points at $($tagged.Output) and HEAD is $head, so the changelog this reads is not the changelog that tag carries." +
            " Every entry link would be a line range into a file this run never saw." +
            " Move the tag onto the commit being released -- 'git tag -f v$Version <sha>' -- and generate the body after it lands, which is the order RELEASING.md gives.")
        exit 1
    }

    $provenance = if ($tagged.ExitCode -eq 0) {
        "The line ranges are '$($relative.Output)' at v$Version ($head), which is HEAD."
    }
    else {
        "The line ranges are '$($relative.Output)' at HEAD ($head); the tag v$Version does not exist yet, so nothing has pinned them."
    }

    if ($dirty) {
        $provenance += ' The working tree has uncommitted changes elsewhere, which cannot move a line number in a file that matches HEAD: ' +
            (($dirty -split "`n" | ForEach-Object { $_.Trim() }) -join '; ') + '.'
    }
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

# The heading's own 1-based line number, which every entry range is measured
# from.
$headingLine = ([regex]::Matches($content.Substring(0, $heading.Index), "`n")).Count + 1

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

# The name the links use. The body always points at the repository's own
# changelog, whatever file this run was handed -- a fixture is a stand-in for
# that document rather than a different one.
$ChangelogName = 'CHANGELOG.md'

$anchor = Get-GitHubAnchor -Heading ($heading.Value -replace '^\#\#[ \t]+', '')
$permalink = "$Repository/blob/v$Version/$ChangelogName#$anchor"

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

for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = $lines[$index]

    # ⚠️ ABSOLUTE, AND THAT IS THE WHOLE POINT OF THE for LOOP. $lines[0] is
    # whatever followed the heading on its own line -- nothing -- so $lines[$i]
    # is line ($headingLine + $i) of the file itself. A range computed against
    # the SECTION would be right about a document nobody can open.
    $number = $headingLine + $index

    if ($line -match '^\#\#\#[ \t]+(?<name>.+?)[ \t]*$') {
        Complete-Entry
        $group = [pscustomobject]@{ Name = $Matches['name']; Entries = [System.Collections.Generic.List[object]]::new() }
        $groups.Add($group)
        continue
    }

    if ($line -match '^-[ \t]') {
        Complete-Entry
        $entry = [pscustomobject]@{ Lines = [System.Collections.Generic.List[string]]::new(); First = $number; Last = $number }
        $entry.Lines.Add($line)
        continue
    }

    if ($null -ne $entry) {
        if ($line.Trim().Length -eq 0 -or $line -match '^[ \t]') {
            $entry.Lines.Add($line)

            # The blank line before the next entry belongs to nobody, so the
            # range ends at the last line that carries text. A range ending on a
            # blank line highlights one line of somebody else's entry.
            if ($line.Trim().Length -gt 0) { $entry.Last = $number }

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
            First    = $e.First
            Last     = $e.Last
        })
    }

    $rendered.Add([pscustomobject]@{ Name = $g.Name; Items = $items })
}

# --- The body ------------------------------------------------------------------
function New-Body {
    param([bool] $Linked)

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
            # ONE SHAPE, and the link is part of the line rather than a fold
            # under it. The two halves are never adjacent in this SOURCE for the
            # reason the footer's own line gives.
            $line = "- $($item.Icon) **$($item.Headline)**"

            if ($Linked) {
                $line += ' [read more]' + '(' + $Repository + '/blob/v' + $Version + '/' +
                    $ChangelogName + '?plain=1#L' + $item.First + '-L' + $item.Last + ')'
            }

            $out.Add($line)
        }

        $out.Add('')
    }

    $out.Add('---')
    $out.Add('')
    $out.Add($legend)
    $out.Add('')
    # ⚠️ The two halves of the link are never adjacent in this SOURCE, and that
    # is deliberate rather than fussy: DocumentationLinkTests reads every file in
    # the tree as text, a `](` in a script is a relative link to it, and
    # `$permalink` is not a path that exists.
    $out.Add('Every entry in full, with its evidence: [' + $ChangelogName + ']' + '(' + $permalink + ')')

    return (($out -join "`n") -replace "`n{3,}", "`n`n").Trim() + "`n"
}

$body = New-Body -Linked $true
$shape = 'linked'

if ($body.Length -gt $Limit) {
    $body = New-Body -Linked $false
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
    Write-Host "The linked body for $Version did not fit in $Limit characters, so this one is HEADLINES ONLY, with no per-entry links at all: $($body.Length) characters, $entries entries, $($rendered.Count) groups. The footer's section link is the only way into the detail."
}
else {
    Write-Host "Release body for $Version is LINKED: $($body.Length) characters against a limit of $Limit, $entries entries, $($rendered.Count) groups, each a headline and a line range into the tagged changelog."
}

Write-Host $provenance

Write-Host "Wrote $Destination."

# Last, and to stdout: the shape and the size, for whoever is scripting this.
"$shape $($body.Length)"

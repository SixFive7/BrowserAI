# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# PreToolUse reminder on Sessions\ and Interop\.
#
# Fires on Edit/Write, ignores every file but a .cs under those two directories, and
# injects five lines as additionalContext. It decides nothing and blocks nothing --
# see the note above the output block. The second EDIT-time context channel in this
# repository; upstream-review-gate.ps1 is the first, and this one is modelled on it
# deliberately, including the reason its message is a GIST rather than a copy.
#
# Fails OPEN by design: any parse error, missing field or unexpected shape exits 0
# with no decision. A hook that blocks work because of its own bug would be the same
# class of defect this repository exists to eliminate.
#
# This file MUST be saved with a UTF-8 BOM. Windows PowerShell 5.x reads a BOM-less
# .ps1 as Windows-1252 and mangles every byte above 0x7F at parse time; the JSON
# output then fails to parse and is dropped silently while the hook still logs
# success. Keep the body ASCII-only as a second line of defence.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# In-script guard, and it is the authoritative one. The settings.json `if` rules
# already scope this to a .cs under either directory, but `if` semantics are fiddly
# on Windows -- the field is a hand-rolled regex translator, not a glob library --
# and a wrong pattern fails silently in whichever direction happens to be worse.
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $path = ($raw | ConvertFrom-Json).tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
    if ($path -notmatch '\.cs$') { exit 0 }

    # Two matches rather than one alternation, and that is not a style choice.
    # A bracket-alternation spelled the way it wants to be spelled puts a closing
    # bracket immediately before an opening parenthesis, which is a Markdown link
    # to DocumentationLinkTests -- with the alternation as a target that does not
    # resolve. Caught by that test on 2026-08-18, which is the same trap its own
    # remarks record falling into.
    if ($path -notmatch '[\\/]Sessions[\\/]' -and $path -notmatch '[\\/]Interop[\\/]') { exit 0 }
}
catch {
    exit 0
}

# GIST + POINTER, not a copy. The directory's own CLAUDE.md is five lines away on
# disk and is the authority; restating it here would put a second copy of the rules
# in a file nobody diffs, and the last project to try that had its hook's summary
# drift across four repositories while still reading as authoritative. So: enough to
# make the reader stop, and where to read the rest. Nothing below is authoritative
# wording, so nothing below can fall out of sync.
$reason = @'
You are editing under Sessions\ or Interop\. Two invariants here are ones no
mechanism in this repository can fully catch: a process is (pid, creationFileTime)
and never a bare pid, and a browser is found by full image path and never by image
name -- the banned-symbol analyzer stops the framework calls and cannot see a new
native declaration. This directory has its own CLAUDE.md, beside the code. Read it.
'@

# No permissionDecision. This hook decides nothing, blocks nothing, prompts nobody,
# and that is not a softening -- it is what an EDIT-time channel can honestly be
# here. Measured 2026-08-15: under permission_mode 'bypassPermissions' an 'ask'
# returned to a SUB-AGENT is silently downgraded to allow, so a gate would be inert
# against precisely the caller most likely to trip it, and against a human it would
# only ever prove a click. Enforcement lives in the suite, where it is evidence.
# What is left here is worth keeping: whoever edits these files gets the two rules
# and the pointer in front of them at the moment they are relevant.

@{
    hookSpecificOutput = @{
        hookEventName     = 'PreToolUse'
        additionalContext = $reason
    }
} | ConvertTo-Json -Depth 5 -Compress | Write-Output

exit 0

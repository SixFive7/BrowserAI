# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# PreToolUse reminder on upstream-review.json.
#
# Fires on Edit/Write, ignores every file but the marker, and injects a pointer to
# the review procedure as additionalContext -- a pointer, never the procedure itself;
# see the note above $reason. It decides nothing and blocks nothing -- see the note
# above the output block for why it stopped being a gate, and plan/testing.md
# "The upstream-review gate" for what replaced it.
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

# In-script guard. The settings.json `if` rule already scopes this to the marker,
# but the authoritative check lives here: `if` semantics are fiddly on Windows and
# a wrong pattern fails silently in whichever direction happens to be worse.
$isSubAgent = $false
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json

    $path = $payload.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
    if ((Split-Path -Path $path -Leaf) -ne 'upstream-review.json') { exit 0 }

    # Who is calling? Measured 2026-08-15 by capturing real payloads from both:
    # a sub-agent's differs from the main session's by exactly two added keys,
    # `agent_id` and `agent_type`. session_id, transcript_path and prompt_id are
    # inherited verbatim from the spawning session and are useless here.
    #
    # This matters because `ask` does NOT gate a sub-agent. Measured the same day:
    # under permission_mode `bypassPermissions`, an `ask` returned to a sub-agent
    # is silently downgraded to allow, and the edit lands unprompted. The gate was
    # inert against precisely the caller most likely to trip it. `deny` is honoured,
    # and reaches the agent as a readable tool error it can report upward.
    #
    # StrictMode is on, so probe for the property rather than dereferencing it.
    $isSubAgent = [bool]($payload.PSObject.Properties.Name -contains 'agent_id')
}
catch {
    exit 0
}

# The content is a GIST + POINTER, not a copy of the procedure. This block used to
# restate UPSTREAM-REVIEW.md's five steps verbatim, which put a second copy of them
# in a file nobody diffs -- and the last project to try that had its hook's summary
# drift across four repositories while still reading as authoritative. So: enough to
# make the reader stop, and the path to the authority. Nothing below is authoritative
# wording, so nothing below can fall out of sync with UPSTREAM-REVIEW.md.
$reason = @'
This edits upstream-review.json, the marker that gates adoption of a new upstream
version.

A red marker test is NOT a stale file to fix. It means the review has not happened yet,
and editing this file to make the test pass defeats the only mechanism that catches what
a green suite cannot: upstream changing its behaviour rather than its surface.

There is a written procedure for that review, and it is not optional: UPSTREAM-REVIEW.md
in the repository root. Read it now and follow it there. It is the single source of truth
for what a review consists of and for what the entry has to record, and it is deliberately
not restated here, because a copy of it would drift.

Nothing here blocks this edit. The gate is the RELEASE, and it is mechanical: the build
diffs the snapshots against the resolved payload, runs the full suite, and fails if this
file does not adjudicate what moved. See plan/testing.md, "The upstream-review gate".
'@

# No permissionDecision. This hook decides nothing, blocks nothing, prompts nobody.
#
# It used to return 'ask'. That was abandoned on 2026-08-15 after measurement:
# under permission_mode 'bypassPermissions', an 'ask' returned to a SUB-AGENT is
# silently downgraded to allow, so the gate was inert against precisely the caller
# most likely to trip it -- and against a human it only ever proved a click, not a
# review. Enforcement moved to the suite, where it is evidence rather than assent:
# four snapshots diffed against the resolved payload, and a marker entry that must
# adjudicate whatever actually moved. See plan/testing.md, "The upstream-review gate".
#
# What is left here is worth keeping: whoever touches this file gets the procedure
# in front of them at the moment it is relevant. That is a reminder, and it is now
# honestly labelled as one.
#
# ($isSubAgent is computed above and deliberately unused. It is the discriminator
# that would be needed if this ever became a gate again -- agent_id is present for
# a sub-agent and absent for the main session -- and the measurement is cheaper to
# keep than to rediscover.)

@{
    hookSpecificOutput = @{
        hookEventName     = 'PreToolUse'
        additionalContext = $reason
    }
} | ConvertTo-Json -Depth 5 -Compress | Write-Output

exit 0

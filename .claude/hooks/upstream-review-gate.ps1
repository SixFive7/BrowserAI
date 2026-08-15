# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# PreToolUse gate on upstream-review.json.
#
# Fires on Edit/Write, ignores every file but the marker, and returns
# permissionDecision "ask" with the review procedure as its reason, so an agent is
# told what the edit means and a human approves it before the marker moves.
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

$reason = @'
This edits upstream-review.json, which gates adoption of a new upstream version.

A red marker test is NOT a stale file to fix. It means a review has not happened yet.
Editing this file to make a test pass defeats the only mechanism that catches upstream
behaviour changes, renamed config keys and changed defaults. None of those are visible
to the golden tools/list snapshot, and loadConfig is a bare JSON.parse with no schema
validation, so a renamed key is discarded in silence.

Before approving this edit, the procedure in UPSTREAM-REVIEW.md must have been run:

  1. git diff <old>..<new> -- tests/       in the upstream repo. What upstream now
                                           asserts. Better signal than the changelog.
  2. git diff <old>..<new> -- config.d.ts  Renamed or removed config keys. Highest
                                           value, because this failure is silent.
  3. browsers.json                         A moved browser revision changes the payload.
  4. --help on old vs new                  Flags that vanished (the --output-mode class).
  5. Release notes                         Intent the diffs do not carry.

Then record in the entry: the version actually reviewed, today's date, and notes saying
what changed, what was adopted, AND what was declined and why. A decline with a reason
is worth as much as an adoption. An empty or unchanged note is a review that did not
happen, and it is visible as such in the diff.

If a change breaks us: fix forward. Never pin back.
'@

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'ask'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 5 -Compress | Write-Output

exit 0

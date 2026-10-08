<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 - lane S1's gate at 187cf52, the first after the caller was pinned to 2025-11-25, and its two reds

**What this is.** The two-shell ordinary gate lane S1 ran after it pushed
`187cf52d` to `master`, under the push rule of 2026-10-04 (push first, gate after,
fix forward). It ran from the lane's worktree, `.work\wt\s1`, checked out at the
pushed commit with nothing uncommitted. `187cf52d` is the commit that offers a
caller MCP revision `2025-11-25` and no other.

| Run | When | Build | Result |
|---|---|---|---|
| `187cf52-ps/` | 2026-10-08, 15:00:15Z to 15:06:09Z | `187cf52d`, 1.1.1-alpha.0.224, PowerShell half, `FULL RUN`, publish `FRESH` | **1006 of 1007** |
| `187cf52-bash/` | 15:06:09Z to 15:17:35Z | the same commit and binary, Git Bash half, `FULL RUN`, publish `FRESH` | **1006 of 1007** |
| `187cf52-again/` | 16:21:45Z to 16:23:07Z | the same commit and binary, the two red arms and nothing else, three runs, `FILTERED` | **2 of 2, three times** |

**The PowerShell half's red is the Codex arm's positive control.**
`ClientReconnectTests.ABrowserAiRegisteredInCodexServesACallAndLeavesNothingThatHoldsAnUpdate`
registers the published server in a real Codex under a scratch home, has Codex's
app-server call `browserai_list` through it, and, once the driver log shows the
answer to that call, asks the product's own reclaim pass about the arm's app root.
The pass found no held marker: *"a live server's marker must read as held, or the
check below proves nothing"*, and 0 received. The driver log, which says what Codex
answered and when the app-server and the server ended, was in the arm's scratch
directory, and that directory was gone when the red was read. The Git Bash half ran
the same arm on the same binary and passed it, and so did the three runs below.

**The Git Bash half's red is the coordinator wake's recheck arm**, with the
sentence the open row on that arm has recorded ten times before:
*"did not answer 'recheck' inside 500 ms"*. The PowerShell half passed it, and so
did the three runs below.

**The three runs below are not the gate's instruments.** They ran the two arms and
nothing else, so a red that needs a full suite's load could not have shown in them,
and all three received the upper-case drive letter, which is the PowerShell half's
spelling and not the Git Bash half's.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The open row on the Codex arm's red, and the eleventh red in the open row on the coordinator wake's recheck arm |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in the files that held it,
which are stored under `.trimmed.` names with the originals' digests in
`originals.sha256`. `cut_gate_187cf52.py` is the script that cut them. Nothing
else was changed.

## Privacy

Nothing here names the user, the user profile or the machine.

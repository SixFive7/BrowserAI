<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 -- which BrowserAI servers were running, whose they were, and what the index said

**What this is.** The read-only census the maintainer asked for (Q240 and Q244)
when a staged `1.1.0` would not apply: every live `BrowserAI.Server.exe` on this
machine, the client session that owns each one, how long since each was used, and
what `browserai_list` reported against what the session index held.
**4 files, 46,887 bytes.** Taken 2026-09-23 on Windows 10.0.26200, at BrowserAI
**1.1.0** staged over **1.0.0** running.

⚠️ **Read-only, at the maintainer's instruction** -- his words were _"do not touch
it. I want to see if it works as expected"_. Nothing was terminated, no directory
was removed, and the one write in the whole census was performed by the product
itself: `browserai_list` pruned three stale index entries as it read them, which is
the finding.

## Cited by

| Record | What it takes from here |
|---|---|
| [`DECISIONS.md`](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client) | That 22 live servers, 14 of them never used, held the staged update -- and that nothing on disk did |
| [kb: processes](../../../kb/windows/processes.md) | The pid-reuse trap in the process log: identity is the pid **and** the process creation time |
| [kb: detection](../../../kb/windows/detection.md) | That the index self-heals on the read path |

## What is here

| File | What it holds |
|---|---|
| [`servers.md`](servers.md) | The census: 22 servers, each with its owning client process, its folder, the conversation that opened it, when it was last used, and whether it ever opened a BrowserAI session. 20 belonged to one VS Code window; 2 were CLI sessions; 14 had never been used at all |
| `server-identities.txt` | The pid-and-creation-time identity of each server, which is what a pid alone cannot give. **A pid-only grep over the process log misattributed four of them** |
| `chromium-processes.txt` | The eight Chromium processes alive at the start: one tree, one attributed browser and seven helpers the stray sweep spares by design, belonging to a session that ran `browserai_destroy` at 18:14:42 local |

## What was cut

**Nothing was trimmed.** What is *not* here is the process log and the session
index themselves: both are live machine state under
`%LOCALAPPDATA%\BrowserAI`, both are rewritten by every run, and copying either
one would be a snapshot of the maintainer's own working data. `servers.md` records
what was read out of them, entry by entry.

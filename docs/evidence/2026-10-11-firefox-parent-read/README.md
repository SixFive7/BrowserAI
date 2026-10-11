<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- the Firefox preflight arm read the parent of a Firefox that had gone

**The run that showed the hazard.** Cited by the hazard index's row of 2026-10-11 for
`FirefoxTests.ThePreflightRefusesAHeldProfileBeforeAnyFirefoxOrWindowExists`. Taken on
this machine, Windows 11 Pro 10.0.26300, .NET 10.0.12, TUnit 1.73.19.

`rel3-ps-1.log` is the first PowerShell run of the release gate at `1a0c0cf9`, as
`build/Invoke-ReleaseGate.ps1` wrote it from 13:17:21Z: 1334 of 1335 in 13m 34.9s, every
capability `PRESENT`, `FULL RUN`, publish-`FRESH`, its `windows` and `firefox safe mode`
rows `CLEAN`. The one red is the arm above, 1.97 s in, on *"Win32Exception: Could not
open process 98796 to read its parent."* from the harness's parent read, called at
`FirefoxTests.cs:270`. The coverage block appears twice, once inside `dotnet test`'s report
of the failed run and once as the driver appended it.

What process 98796 was is not known: it was gone when the arm read it, and nothing that
ran then recorded its image or its parent. Read from the arm and not reproduced: the arm
lists every Firefox that runs from the provisioned browsers root and then reads each
one's parent, and a Firefox another arm had started exited in between.

## Files

| Path | What it is |
|---|---|
| `rel3-ps-1.log` | The run's log, 99 lines |
| `originals.sha256` | The SHA-256 of the log as it was taken, before this repository's line endings |

Line endings are this repository's, LF. The log was taken with CRLF.

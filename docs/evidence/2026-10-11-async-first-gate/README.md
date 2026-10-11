<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- the first gate after decisions 54 and 63, and what it found

**The first full run of the suite over the tree the maintainer's decisions 54 and 63
converted, its sixteen reds, the run in which they were green, and the measurement and
the two runs behind one of them.** Cited by the hazard index's rows of 2026-10-11 for a
session's log scope across an asynchronous acquire, for a rename the compiler does not
check, for an exited process's restart registration and for the real Codex arm's
RegisterAI run. Taken on this machine, Windows 11 Pro 10.0.26300, .NET 10.0.12, TUnit
1.73.19.

`async-ps-1.log` is the PowerShell half of the ordinary gate at `7e2f8516`, with the
waits kept for the maintainer's review suppressed and uncommitted, as
`build/Invoke-OrdinaryGate.ps1` wrote it from 20:56:28Z: 1345 cases, 16 failed, in
13m 07.6s, `FULL RUN`, with its coverage block. Six reds are scans that read a member by
its name before the conversion; five came from the conversion's own changes, three of
them in the product and two arms that read a record's asynchronous write as done; two
are the arms of the two flakes the hazard rows are about; and three are a count, a
harness double and a harness claim disposed twice.

`fix-1.log` is the eleven classes those sixteen are in, run alone at 21:15Z against the
fixes of `aca79551` and `59b4f621` before they were committed: 160 of 160, the sixteen
among them.

`restart-probe.txt` is the output of the program in
[the resurrection article's entry of that day](../../../kb/chromium/resurrection.md):
what `GetApplicationRestartSettings` answers for a process that has exited and for one
that runs. `red-restart-registration.log` is the arm of `d8b2e708` against the reader as
it was, red on the exited probe, and `green-restart-registration.log` is that arm and both
of the reader's callers' classes with the fix of `3c69e627`, 7 of 7.

## Files

| Path | What it is |
|---|---|
| `async-ps-1.log` | The gate's PowerShell half, 291 lines |
| `fix-1.log` | The sixteen reds' eleven classes, green |
| `restart-probe.txt` | The three answers of the restart registration measurement |
| `red-restart-registration.log` | `RestartRegistrationTests` before the reader's fix |
| `green-restart-registration.log` | `RestartRegistrationTests`, `BrowserContainmentTests` and `FirefoxTests` after it |
| `originals.sha256` | The SHA-256 of the five files as they were taken, before this repository's line endings |

Line endings are this repository's, LF. The four logs were taken with CRLF and some LF
among them, and `restart-probe.txt` with CRLF.

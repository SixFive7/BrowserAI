<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- the first gates after decisions 54 and 63, and what they found

**The first full run of the suite over the tree the maintainer's decisions 54 and 63
converted, its sixteen reds, the run in which they were green, and the measurement and
the two runs behind one of them; and the second gate's PowerShell half, its one red and
the run in which it was green.** Cited by the hazard index's rows of 2026-10-11 for a
session's log scope across an asynchronous acquire, for a rename the compiler does not
check, for an exited process's restart registration, for the real Codex arm's RegisterAI
run, for a value kept for a thread and read after an await, and, by addition, for the
Chromium containment arm's open row. Taken on this machine, Windows 11 Pro 10.0.26300, .NET 10.0.12, TUnit
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

`async-ps-4.log` is the PowerShell half of the third, at `d5fd9da6`, from 21:50:54Z:
1346 cases, 2 failed, in 13m 00.5s, the Chromium containment arm on a walked row whose
`inJobProcessIdList` was false and the real Codex arm's RegisterAI run past its 14 s
again. A first attempt at that commit, async-ps-3, stopped at its test pack, whose publish
was two commits behind the tree, and ran no test.

`async-ps-2.log` is the PowerShell half of the second ordinary gate, at `784173f3`, from
21:30:08Z: 1346 cases, 1 failed, in 13m 06.5s, the coverage arm reading a thread-static
value on a thread the asynchronous summary had not printed on. `green-coverage-block.log`
is that arm and the three other classes that read the summary, with the fix of
`cab4d514`, 36 of 36.

## Files

| Path | What it is |
|---|---|
| `async-ps-1.log` | The gate's PowerShell half, 291 lines |
| `fix-1.log` | The sixteen reds' eleven classes, green |
| `restart-probe.txt` | The three answers of the restart registration measurement |
| `red-restart-registration.log` | `RestartRegistrationTests` before the reader's fix |
| `green-restart-registration.log` | `RestartRegistrationTests`, `BrowserContainmentTests` and `FirefoxTests` after it |
| `async-ps-2.log` | The second gate's PowerShell half, 102 lines |
| `async-ps-4.log` | The third gate's PowerShell half, 112 lines |
| `green-coverage-block.log` | `SuiteCoverageTests`, `FirstRunCacheTests`, `ForegroundLockTests` and `SafeModeWatchTests` after the fix |
| `originals.sha256` | The SHA-256 of the eight files as they were taken, before this repository's line endings |

Line endings are this repository's, LF. The seven logs were taken with CRLF and some LF
among them, and `restart-probe.txt` with CRLF.

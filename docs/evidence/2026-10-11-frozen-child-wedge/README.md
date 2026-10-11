<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- a frozen child of a test host held the pipe a gate driver read its run through

**The two times it held a gate, and the runs in which the two arms that hold the fix were
watched red and then green.** Cited by the hazard index's row of 2026-10-11 for the
frozen-child wedge. Taken on this machine, Windows 11 Pro 10.0.26300, .NET 10.0.12, TUnit
1.73.19.

`wedge-rel2-ps-1.txt` is what was read while the second release gate's PowerShell driver,
at `d1bae44f`, waited in its `Tee-Object` after a green first run, from 12:28Z to 12:41Z,
and how it ended: a suspended `git.exe`, pid 102292, created at 12:05:20Z while
`ClientReconnectTests.ARealClaudeCodeMeetsTheRefusalOnceAndItsRetryGoesThrough` ran, one
thread, no CPU time, its parent gone, was ended through one handle at 12:42:19.52Z, and the
driver went on in the same second.

`wedge-rel2-ps-2.txt` is the same gate's second run, whose test host crashed on the poll
tick of [`2026-10-11-poll-tick-crash`](../2026-10-11-poll-tick-crash/README.md): a suspended
`cmd.exe /d /c "exit 0"`, pid 23920, the command line of
`InstallerLockTests.ExitedChildAsync`, its parent the crashed test host, held the driver's
tee until the driver and then that child were ended through one handle each, at 12:53:36Z
and 12:53:58Z.

`red-64c.log` is `GateDriverTests` and `ClientRunTests` run alone against the tree at
`50d58c6d`, which added them and no fix, from 20:05:00Z: all five failed at the hang
detector, ten minutes, the four drivers each still waiting for the end of a pipe that the
stand-in run's holder kept open, and the client's child still running after the arm had
ended. `green-64c.log` is the same run against `39cddc6c`, which carries both fixes, from
20:15:35Z: five of five in 14.8 s.

## Files

| Path | What it is |
|---|---|
| `wedge-rel2-ps-1.txt` | The record of the first hold, the suspended git, 32 lines |
| `wedge-rel2-ps-2.txt` | The record of the second hold, the suspended `cmd.exe`, 21 lines |
| `red-64c.log` | The red run of `GateDriverTests.ADriverFinishesWhileAChildItsRunLeftBehindStillHoldsTheRunsOutput`, four drivers, and `ClientRunTests.AClientAnArmStartedLeavesNoProcessBehindOnceTheArmEnds`, with its coverage block |
| `green-64c.log` | The green run of the same five, with its coverage block |
| `originals.sha256` | The SHA-256 of the four files as they were taken, before this repository's line endings |

Line endings are this repository's, LF. The two runs' logs and the second record were taken
with CRLF and some LF among them, the first record with LF.

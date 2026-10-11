<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- a poll tick that ran after its ticker was disposed ended the test host

**The run that showed the hazard, and the run in which the two arms that hold its fix
were watched red.** Cited by the hazard index's two rows of 2026-10-11, for
`BrowserProvisioner.PollTicker` and for `PageTabs`. Taken on this machine, Windows 11 Pro
10.0.26300, .NET 10.0.12, TUnit 1.73.19.

`rel2-ps-2.log` is the second PowerShell run of the release gate at `d1bae44f`, as
`build/Invoke-ReleaseGate.ps1` wrote it from 12:42:21Z. The test host ended 58.9 s in,
with 652 tests reported and none of them failed, on an `ObjectDisposedException` out of
the ticker's timer callback, and its last line is `dotnet test`'s summary of 12:43:41Z. No
TUnit report was written for the run, and the coverage block a finished run appends is not
in it: the driver never got past the run, and was stopped at 12:53:36Z.

`red-timers.log` is the two arms run alone against the tree at `01167b7c`, which added them
and no fix, at 13:05Z: both failed with an `ObjectDisposedException` out of the tick, the
first at the crash's own frame, `BrowserProvisioner.cs:2026`, and the second from
`CoordinatorInbox.Wake`.

## Files

| Path | What it is |
|---|---|
| `rel2-ps-2.log` | The crashed run's log, 25 lines |
| `red-timers.log` | The red run of `ProvisioningTests.APollTickThatArrivesAfterItsTickerIsDisposedDoesNothing` and `PageServiceTests.ALingerTickThatArrivesAfterTheTabsAreDisposedWakesNothing`, with its coverage block |
| `originals.sha256` | The SHA-256 of both logs as they were taken, before this repository's line endings |

Line endings are this repository's, LF. Both logs were taken with CRLF, and `red-timers.log` with some LF among them.

<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - the graceful-stop arm, red once in option c's gate

**What this is.** The full suite run in which
`ServerPipeTests.AStopThroughThePipeEndsAPublishedServerTheGracefulWay` went red,
the server's own log lines from that run, and the runs beside it. The arm starts
the published server, opens a session, sends a stop through the server's pipe,
and then requires the server to have exited 0, its instance directory to be gone,
the session's lock to be released and every browser of the session to end. The
red was the second of those: *"Expected to be false but found True at
Assert.That(Directory.Exists(instance)).IsFalse()"*, `ServerPipeTests.cs:638`,
after 15 s.

| Run | When | Build | Result |
|---|---|---|---|
| `a32bf62-ps/` | 2026-10-03, 23:21:32Z to 23:30:46Z | lane c's gate at `a32bf62`, 1.1.1-alpha.0.191, PowerShell half, `FULL RUN`, publish `FRESH` | **958 of 959**, this arm the one red |
| `a32bf62-bash/` | the same gate, 23:30:46Z to 23:36:51Z | the same commit and the same published binary, Git Bash half, `FULL RUN` | 959 of 959 |
| `a32bf62-gate-again/` | 2026-10-03, 23:39:01Z to 23:50:16Z | the same commit, published again, which left the binary as it was (`exe 2026-10-03T23:20:57.818Z` in all four coverage blocks), both halves | 959 of 959 and 959 of 959 |

**What the server said.** `a32bf62-ps/c2-ps-red-server-26992.trimmed.log` is lane
c's excerpt of the process log, the lines written by the red run's server, pid
26992, with the line numbers the search gave them. The stop arrived and was
acknowledged at 23:27:08.820Z; the server started Playwright's registry reap,
detached, at 23:27:11.988Z; its surface child, pid 106332, exited 0 at
23:27:12.040Z; and **5 ms later the server wrote** *"The instance directory
...\instances\26992-05f664423a964399b98bac3e98032389 was not fully removed: 3
node(s) would not go. The next run's sweep tries again."* The surface child's
working directory was that instance directory. **What held the three nodes is
not established**: the excerpt has no line naming them, and nothing was measured
after the run.

**How far the search for another red went.** On 2026-10-04 every `.log` under
the main checkout's `.work`, the lane worktrees' included, was searched for this
arm failing: 837 files report a run's totals. The assertion on the instance
directory failed in this run and in one other, the plant of 2026-09-24 that
watched the arm red on purpose when it was written. The other hits are the arm's
one earlier red in a gate, the same day, on a different assertion (a browser
still alive the instant the server was gone, fixed by waiting on each browser's
handle), and runs refused by a stale published binary before any assertion
ran.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The open row on this arm leaving the instance directory behind after a clean stop |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in the six files that held
it, which are stored under `.trimmed.` names with the originals' digests in
`originals.sha256`. `cut_pipe_stop_red.py` is the script that cut them, reading
lane c's scratch and changing nothing there. Nothing else was changed: the logs
are the gate driver's and the test host's whole output for those runs, and the
server excerpt is lane c's, whole.

## Privacy

Nothing here names the user, the user profile or the machine.

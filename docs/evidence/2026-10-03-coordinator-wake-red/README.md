<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - the coordinator wake's recheck arm, red under a full run

**What this is.** The three full suite runs in which
`CoordinatorWakeTests.AServingCoordinatorIsAskedToLookAgainAndNoTaskIsStarted`
went red, each with the same message, and the runs beside the two newer ones. The
arm opens a real coordinator pipe in the test process and calls the product's
wake against it; every red reads *"the coordinator's pipe is there and did not take the
recheck, so no second coordinator was started: '...' did not answer 'recheck'
inside 500 ms"*.

| Run | When | Build | Result |
|---|---|---|---|
| `dffe9d4-bash/` | 2026-10-03, about 18:45Z to 18:50Z | lane registry's gate at `dffe9d4`, 1.1.1-alpha.0.171, Git Bash half, `FULL RUN` | **942 of 943**, this arm the one red, after 3,004 ms |
| `dffe9d4-ps/` | the same gate, just before it | the same commit, PowerShell half | 943 of 943 |
| `dffe9d4-bash-again/` | the same gate, 2026-10-03, about 18:52Z to 18:56Z | the same commit, the Git Bash half run again | 943 of 943 |
| `6e6388d-ps/` | 2026-10-03, 22:07Z to 22:11Z | lane stale's two-shell gate at `6e6388d`, 1.1.1-alpha.0.186, PowerShell half, `FULL RUN` | 912 of 912 |
| `6e6388d-bash/` | the same gate, 22:11Z to 22:15Z | the same commit, Git Bash half, `FULL RUN` | **911 of 912**, this arm the one red, after 1,838 ms |
| `6e6388d-bash-again/` | the same gate, 22:16Z to 22:20Z | the same commit, the Git Bash half run again | 912 of 912 |
| `2026-09-29-ps/` | 2026-09-29, about 21:04Z to 21:08Z | 1.1.1-alpha.0.137, a PowerShell run in the main checkout, `FULL RUN` | **893 of 895**, this arm red after 941 ms, beside one unrelated red in `ChangelogTests` |

**How the 2026-09-29 run was found.** Lane registry reported no other red of this
arm in the 166 suite logs it read. A search of every log under `.work` that
reports a run's totals, 495 files counting drivers' copies and filtered runs, found
this one as well, in the main checkout's own `.work\suite`; the only other hits are
the two plant logs of 2026-09-25, in which the arm was watched red on purpose with
a different message. The third red came later that evening, in the gate of the
lane that recorded the first two, after the search.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The open row on this arm going red under load, and the 500 ms bound it rests on |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in all nine logs, which are
stored under `.trimmed.` names with the originals' digests in `originals.sha256`.
`cut_wake_red.py` is the script that cut them. Nothing else was changed and
nothing was left out: the logs are the gate driver's and the test host's whole
output for those runs.

## Privacy

Nothing here names the user, the user profile or the machine.

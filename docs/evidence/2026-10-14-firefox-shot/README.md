<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-14-firefox-shot

**What the open row of [the hazard index](../../../HAZARDS.md#hazard-index) on
`FirefoxSessionTests.AFirefoxSessionRunsFromInitThroughAnArtifactToDestroy` was
investigated from, on 2026-10-14**, after that arm's screenshot was answered with
`isError: true` once, in the second PowerShell half of the gate at `df12ab85` on
2026-10-13, with neither the answer's text nor the session's folder kept. It is cited
by that row and by
[the kb entry](../../../kb/playwright/tools-and-artifacts.md#a-firefox-screenshot-gets-upstreams-five-seconds-and-a-loaded-machine-takes-most-of-them----measured-2026-10-14).
Every file is kept whole, as taken, with the line endings this repository stores.

## `red/` -- what the red left

- `red-process-log.txt`: every record of the machine's process log,
  `browserai-20261013-002.log`, that names the red's background (pid 88660), its relay
  (17444), its Firefox (95748) or its scratch folder; every record of the stray sweep
  that attributed that Firefox; and every record above `INFO` of the whole log in the
  arm's 40 seconds. No record says what the screenshot was answered.
- `red-application-log.txt`: the Windows Application log from 22:03Z to 22:06Z. No event
  names `firefox.exe`, `node.exe` or `BrowserAI.exe`; the same match finds the suite's
  own crash probe 3 times, which is the positive control.
- `arm-durations.txt`: every run of the arm the process log files of 2026-10-12 and
  2026-10-13 hold, from the first BrowserAI start in its scratch folder to its session's
  destroy: 51 with a destroy, 48 of them at 13.6 to 51.3 s (median 30.8 s) and 3 alone
  at 3.8 to 4.4 s, and the red with none. Which of the 48 were full runs is not read here.

## `plant/` -- the arm explaining itself

`plant-red.log` is a filtered run of the arm with its screenshot planted to fail,
`fullPage` beside a `target`, which upstream refuses: the failure quotes the answer's
text and its time and says where the rig was kept.

## `probe/` -- the arm's steps, timed

`FirefoxShotProbe.cs.txt` is the probe, a test class compiled into the suite for these
runs only and never committed as code: each iteration starts the published BrowserAI the
way the arm does, opens a Firefox session, navigates, takes the screenshot, destroys
the session, and appends one JSON row with every call's time and every error's text.
This is its last version. `stall1` ran an earlier one, which suspended the browser 300
ms after the screenshot was asked for and so after it had been answered; its 7.3 s are
the probe's own wait and say nothing about the screenshot.

| File | What ran | Iterations | Screenshot answered in | Errors |
|---|---|--:|---|--:|
| `base1.jsonl` | nothing beside it | 5 | 26 to 51 ms | 0 |
| `stall1.jsonl` | the earlier hold, after the answer | 2 | not measured | 0 |
| `stall2.jsonl` | every Firefox process of the session suspended before the screenshot was asked for, let go when the answer came | 2 | 5,011 and 5,004 ms, then 61 and 32 ms for the next one | 2 |
| `load128.jsonl` | 128 spinning threads at normal priority, commit charge held at 85.5 % | 10 | 647 to 1,729 ms | 0 |
| `load320.jsonl` | 320 spinning threads at normal priority, commit charge held at 85.5 % | 10 | 30 to 4,165 ms | 0 |

Both answers of `stall2` were `isError: true` with the text `### Error`,
`TimeoutError: browserBackend.callTool: Timeout 5000ms exceeded.` and a call log of one
line, `taking page screenshot`, between the escape sequences that dim it.

## `load/` and `counters/` -- the two load batches, and what they did to the machine

⚠️ **The two load batches are kept for what they measured and what they cost, and
nothing here is to be run again.** `load-128-threads.log` and `load-320-threads.log`
are the scratch load generator's own ticks: it spun the threads and held the commit
charge at its target by committing memory nobody touched, topping it up as other
programs freed theirs, and it held 51.75 GiB by the end of the second batch.
`load-320-threads.csv` is the machine's counters through most of the second batch,
sampled every second: processor time at 100 % for its median, a run queue of 539 ready
threads at its median, 661 at its 90th percentile and 1,227 at most, on 32 processors.
Windows logged a leak warning for a PowerShell process at 00:44:03Z and closed a hung
Explorer at 01:00:33Z, and VS Code had to be restarted. No load of any kind is generated
on this machine since: a failure is reproduced by a forced order or an injected fault,
which is what `stall2` is.

`gate-opening.csv` is the same counters, sampled every second while another branch's
ordinary gate ran, from 00:07:09Z, 10.5 minutes into its PowerShell half, to 00:32:09Z;
its Git Bash half's test host was created at 00:12:47.49Z, which is where its opening
is counted from. In its first 45 s processor time read 100 % at the median, the run queue
248 ready threads at the median and 395 at most, on 32 processors, and the commit charge
91.5 % at the median and 96.1 % at most, while that run's own coverage block, which reads
the commit charge at the run's two ends, gave 84.1 % and 82.6 %. From 85 s on, processor
time read 29 % at the median and the run queue 0. The red's arm at `df12ab85` started
6.7 s after its own test host was created, at 22:04:09.82Z, and failed about 40 s after
it.

<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 - the resume wedge, measured

**What the 2026-09-16 run recorded and could not keep.** That run found the wedge
while re-establishing [re-verification row 38](../../../kb/re-verification.md) and
its transcript was never persisted, so
[the hazard row](../../../HAZARDS.md#hazard-index) stood on three numbers and a
rig. This is the re-run, bounded and instrumented, and this time the transcript
is here.

Cited by [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#the-resume-wedge-measured----2026-09-17),
the hazard row, and the question it raised in
[`QUESTIONS.md`](../../../QUESTIONS.md).

## What is here

| File | What it is |
|---|---|
| `wedge-probe-transcript.log` | The probe's own stdout, stamped in UTC, from the first launch to the destroy |
| `wedge-probe-report.json` | The same run as data, including both servers' stderr tails |
| `process-log-across-the-hang.log` | Every line the machine-wide process log carried for either server between `13:08` and `13:24` UTC, plus every stray-sweep line in that window |

The rig is
[`docs/probes/2026-09-16-resume/wedge-probe.js`](../../probes/2026-09-16-resume/README.md),
which is `resume-probe.js` with a clock on the call that hangs: the browser call
is fired and polled, not awaited, and the wait is bounded on the command
line so *it never returned* is a measurement and not the probe giving up at a
number nobody chose.

## The cut, and what it was cut from

`process-log-across-the-hang.log` is a **cut**, not a capture. It was taken from
the machine-wide process log at
`%LocalAppData%\BrowserAI\logs\browserai-20260917-000.log`, whose SHA-256 at the
moment of the cut was
`bca8c6bc2e24fb16499532dc95049f7d571bc88720631c07fa7f07a0a6a7e9ae` and which was
3,989,650 bytes when this batch began. **What was cut away** is every line
belonging to another process: that file is machine-wide and thirty-day, and it
carries the whole day's suite runs. What was kept is every line whose `pid=`
names either server the probe started (`61684`, `43804`) and every stray-sweep
line in the same window, because a sweep is the other thing that can end a
browser on this machine and its absence here is part of the finding.

The other two files are whole.

## What it touched

The session directory it was given, under `.work/`, and the product's shared data
root - the session index and the live markers - which is the same state the suite
drives. **Do not run it beside a suite run.** It destroyed its session through
`browserai_destroy` on the way out, from a third server process, and the run
ended with no live marker, no index entry, no session directory and no browser
under the browsers root. It never touched `%LocalAppData%\BrowserAI.app`.

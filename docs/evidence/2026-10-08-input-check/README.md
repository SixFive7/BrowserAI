<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- what the visible-input check costs

What the kb entry
[What the visible-input check costs, and the timer it runs on](../../../kb/windows/processes.md#what-the-visible-input-check-costs-and-the-timer-it-runs-on----measured-2026-10-08)
was read from, the root's earlier measurement of the bare reads it cites beside its
own, and the planted runs `VisibleInputWatchTests` and `InteropLayoutTests` were
watched red in. The rig is
[`docs/probes/2026-10-08-input-check`](../../probes/2026-10-08-input-check/README.md).

| Path | What it is |
|---|---|
| `rig/summary.txt`, `rig/summary.json` | The rig's whole run of 2026-10-08, 15:21Z to 15:33Z, at the product's tree of that day with the shipped interval of 2 s and tolerance of 1 s: each read and the product's check in tight loops, a million hot checks, and the four timed processes' twelve minutes. The text is the summary; the JSON carries every number it prints and the four processes' own records |
| `rig/timer.json`, `rig/exact.json`, `rig/wake.json`, `rig/control.json` | What each timed process wrote about its own window: cycles, kernel and user time, ticks, every spacing between two ticks, and each tick's cold reads |
| `rig-long/summary.txt`, `rig-long/summary.json` | The second run, 15:38Z, the same loops a hundred million calls each and no timed processes, for the thread's kernel time |
| `step0/m5/`, `step0/m5-task/` | The root's step-0 measurement 5 of the same day, cut from its scratch folder `.work/step0-tasks/runs/20261008T1411/`: the per-run summaries of the tight loops (`bench-*`), of five processes ticking every 2 s for 300 s (`ticker-*`, with each tick's raw nanoseconds) and of two that woke every 2 s and called nothing (`noop-*`), and the same loop started by a task (`m5-task/`) |
| `step0/findings-section-5.txt` | That measurement's own account, lines 8 to 13 and 83 to 100 of the step-0 `FINDINGS.md` as they stood, whose SHA-256 was `6abbcd474fd669b9795a4d87f7142ff014eb6950a78ea02eed8ad78f50d30281` (17,999 bytes) |
| `step0/left-out.sha256` | The eleven files of raw nanoseconds behind the tight loops, about 500 KB each, left out, with their digests |
| `plants/` | The filtered runs that watched each new arm red, one per planted stub, and the plant of `InteropLayoutTests` without the server's assembly in its search |

## How these bytes depart from the ones taken

- **The plant logs are trimmed.** Each is stored under a `.trimmed.` name with the
  user profile path, which the run's coverage block prints for the provisioned
  browsers, replaced by `C:\Users\<user>`; `plants/originals.sha256` carries each
  original's digest and size. Nothing else in them was changed.
- **The step-0 files are copies**, byte for byte, of the ones in the root's
  scratch folder; the raw files left out are named with their digests in
  `step0/left-out.sha256`. `step0/findings-section-5.txt` is lines cut from a
  longer file and says so above.
- The rig's outputs are as written.

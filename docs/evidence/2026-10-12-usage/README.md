<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-12 -- what a reading of BrowserAI's CPU and memory costs

Cited by
[What a reading of BrowserAI's CPU and memory costs](../../../kb/windows/processes.md#what-a-reading-of-browserais-cpu-and-memory-costs----measured-2026-10-12)
and by the decisions of
[the home, its CPU and memory, and the after-update notice](../../../DECISIONS.md#the-home-its-cpu-and-memory-and-the-after-update-notice-decided-2026-10-11).
The rig is [`docs/probes/2026-10-12-usage`](../../probes/2026-10-12-usage/README.md).

| Path | What it holds |
|---|---|
| `explore/shared-blank.txt`, `explore/shared-page.txt` | The two outputs of `shared-probe.ps1`, taken 2026-10-12 at 13:54Z under the suite's lock: one headless Chromium 1247 in a job of its own, each of its twelve processes' working set, private working set, shared working set, shared commit, private commit and CPU, the job's accounting, and a thousand readings of the job timed. `shared-blank.txt` loaded `about:blank`; `shared-page.txt` was asked for a page on the web and shows the same twelve processes, so whether that page loaded is not established |
| `rig/1-session/summary.txt` | The rig's run of 15:55Z to 15:59Z, one session: each process of the job, the first reading, a thousand hot readings and the 120 s paced against the 120 s without, with the product's `ProcessUsage` and `JobObject` compiled in |
| `rig/4-sessions/summary.txt` | The same for four sessions, 15:59Z to 16:03Z |
| `plants/usage-model-red.log`, `plants/usage-model-green.log` | The model's six arms in `UsageTests`, against planted stubs and against the code |
| `plants/home-red.log`, `plants/home-red-dashboard.log` | The first planted build of the rest, every behaviour switched off at once: the new arms of `UsageTests`, `HomeTests`, `BackgroundPageUsageTests`, `InteropLayoutTests` and `HomeBrowserTests`, and the dashboard arm for the update page's caption and the home's *Held by* line |
| `plants/home-red3.log`, `plants/home-red3-dashboard.log` | The second, each behaviour the first masked planted alone where its arm reaches it, with the suite's two hang bounds shortened in that build only, which the head of `home-red3.log` says |
| `plants/home-red4-no-post.log`, `plants/home-red4-no-hold.log`, `plants/home-red4-no-usage.log` | The third, one build of the browser arm as it stands, a variable of the test host's environment choosing the page script's plant: the dismiss folds the notice and posts nothing; a state that arrives while the notice folds is drawn at once; or no reading is written where its figure stands. The same two bounds, which each log's head says. The first run of its two plants, at 16:59Z, failed before either plant was reached, at a state the page's watch sends in its first second, which the arm now waits out; its logs were overwritten by the run at 17:10Z and are not here |
| `plants/home-green.log`, `plants/home-green-recheck.log` | The filtered run of every arm above, the classes around what changed and the house rules, against the code: 281 of 284, the three reds a row of the numbers index for the number the home no longer has, an arm of `UpdatePageTests` that still asserted the old caption, and the fragment count `AGENTS.md` publishes; then those classes and the tree scans after the three were corrected, 106 of 106 |

**Every byte is as taken**, but for the line endings `.gitattributes` normalises, and every
path in them names this machine's profile and the tree the runs were taken in, uncut, as
206 files of the batches before it do against 19 that cut the profile to
`C:\Users\<user>`, counted on 2026-10-12. Left out by directory: the Chromium profiles the
rig wrote beside each summary under `--out`.

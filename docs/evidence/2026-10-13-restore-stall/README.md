<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 -- a resumed Firefox session's first start, after its last page's server ended

Cited by
[A Firefox session resumed onto a page that can neither load nor come back from the cache waits out the 180 s launch](../../../kb/playwright/provisioning-and-timings.md#a-firefox-session-resumed-onto-a-page-that-can-neither-load-nor-come-back-from-the-cache-waits-out-the-180-s-launch----measured-2026-10-13),
by [the hazard index](../../../HAZARDS.md), the row the maintainer's 125 b of 2026-10-12 opened in
his words, *"125 your recommendation"*, and by re-verification row 221. The rig is
[`docs/probes/2026-10-13-restore-stall`](../../probes/2026-10-13-restore-stall/README.md), and
the two sightings that asked for it are
[`2026-10-12-firefox-restore-stall`](../2026-10-12-firefox-restore-stall/README.md).

Everything was taken on 2026-10-13 under the suite's lock, through BrowserAI 2.0.1-alpha.0.1
published from `02ad1585`, with `@playwright/mcp` 0.0.83, `playwright-core`
1.64.0-alpha-1790635538000, node v24.21.0, Firefox 156.0 (`firefox-1553`) and Chrome for
Testing 155.0.8059.12 (`chromium-1247`), each browser a copy of the provisioned one in a
scratch data root. One folder per batch:

| Folder | What it holds |
|---|---|
| `first/` | The first batch, 05:43Z to 06:23Z: two warm-ups and five interleaved rounds of `ff-dead`, `ff-alive`, `ff-dead-plain` and `cr-dead` through the published BrowserAI. `summary.txt` is the table `summarize.mjs` printed |
| `replay/` | The replays, 06:31Z to 06:47Z: the first three stalled profiles of `ff-dead` replayed through the payload's own child as they were and with their address answering again, and the first of them with the restore off, with Playwright's blank page kept and with the restore made lazy, both of those last through `firefoxUserPrefs` |
| `headers/` | The second batch, 11:38Z to 12:14Z: five interleaved rounds of `ff-dead-nostore`, the page with the dashboard's `Cache-Control: no-store` and no other header, and `ff-dead-cacheable`, the page with every other header the dashboard sends; and two of `ff-dead-again`, the dashboard's page resumed again at once after the stall and its second call |
| `again/` | The third batch, 12:15Z to 12:24Z: two rounds of `ff-dead-again-wait`, the same with the resume asked again every five seconds while it was refused |
| `replay2/` | The replays of 12:24Z to 12:28Z: the first stalled profile of `ff-dead` with the restore off and with the restore made lazy, each written into the profile's copy as a `user.js`. `replay-03-ff-dead-1-none` is a step the launcher's `-DirectionVariants 'none'` asked for, which `replay.mjs` refused before it started anything, as a variant it does not have; it is kept for that refusal, in `node.stderr.txt` |

**In each batch folder:** `build.txt`, the build and the arguments; `launcher.log`, the lock's
take and release and each step's start and end; `browsers.txt`, the copies; `registry-*.txt`,
what Firefox added under `HKCU\Software\Mozilla\Firefox` and what the launcher took out again;
`summary.txt`; `cuts.txt`, what was done to each file; and `originals.sha256`, the SHA-256 and
size of every file a copy here was made from.

**In `measure/`:** `results.jsonl`, one row per resume with every call's start, answer and
duration, the session files decoded before the resume and after the calls, and the profile's
growth during the first call; `events.jsonl`, the rig's own record; `background.stderr.txt`, the
records of BrowserAI's background, where every session's own records go; `relay.stderr.txt`;
`census.jsonl`, the members of the step's job every five seconds; and `configs/`, the config
BrowserAI wrote for each resumed child.

**In each `replay-*` folder:** `result.json`, the replay's outcome, its environment's names and
the call's answer; `config.json`, the config it launched from; `child.stderr.txt`, the child's
standard error with Playwright's `pw:protocol`, `pw:browser` and `pw:api` log on; for the
`as-is` replays, `moz/`, Firefox's own logs of the `DocLoader`, `nsDocShell`,
`SessionHistory`, `BrowsingContext` and `ProcessIsolation` modules; and
`profile/sessionstore-logs/`, Firefox's session store log of the restore. A `user.js`
replay's `result.json` carries the lines it wrote into the profile, under `userJs`.

**Not here: the profiles and the snapshots they were replayed from**, which hold a browser's
whole state, about 48 MB each; and the full registry before each batch, which lists every value
Firefox keeps for every install on this machine. Each one's digest is in `originals.sha256`
where a copy was cut from it, and the profiles' file counts and sizes are in the rows.

**How these bytes depart from the ones taken**, file by file in `cuts.txt`: the user's profile
path is `%USERPROFILE%` and terminal colour escapes are removed, under a `.trimmed.` name; no
file was cut down to a part of itself. Line endings are what `.gitattributes` normalises.

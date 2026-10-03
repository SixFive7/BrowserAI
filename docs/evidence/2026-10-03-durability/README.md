<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- committing to disk sooner, and session restore after a hard kill

**What this is.** What the durability rig recorded on 2026-10-03 between 13:43Z
and 18:50Z, for the maintainer's ask to find out whether both browsers can be
told to commit everything to disk at once. A page in a persistent profile writes
cookies, `localStorage`, IndexedDB, Cache Storage and `sessionStorage` after its
browser opened tabs and changed a preference; the browser tree is killed by
closing its job a chosen time later, with or without a lever; the profile is
launched again and read back. Plus two workloads that measure what the levers
cost, and a check of what a job's I/O counters count. Taken at Chrome for
Testing **155.0.8059.12** (`chromium-1247`), Firefox **156.0** (`firefox-1553`),
`playwright-core` **1.64.0-alpha-1790635538000** and `@playwright/mcp`
**0.0.83**, from a copy of the payload and on its node v24.21.0, on Windows 11
build 26300.9550. **60 files beside this README, 1,203,454 bytes as cut.** The
rig is a probe record at
[`docs/probes/2026-10-03-durability`](../../probes/2026-10-03-durability/README.md).

**Two agents took it.** The first planned the work and ran `main-cr`, `main-fx`
and `cycle`, and its chain went on to run `idle` and `work` after a usage limit
stopped the agent at 15:07Z; the second, which replaced it, ran `supp`,
`smoke-form` and the job-counter check, and wrote the summaries the table below
names as its own.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#committing-to-disk-sooner-and-session-restore-after-a-hard-kill----measured-2026-10-03) | Every number in the entry, and the corrections by addition to the two entries before it |
| [kb: job objects](../../../kb/windows/job-objects.md) | The pipe and file runs of `results/iocount.txt` |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 182 to 185, and the notes on rows 170 and 171 |
| [kb: what is not established](../../../kb/not-established.md) | The closed row on a restore after a kill, the notes on the two rows after it, and the six rows added below them |

## What is here

| Path | What it is |
|---|---|
| `results/table.txt` | The result: per browser, lever and store, how many runs kept and lost it, with the youngest age kept and the oldest lost, and when each store's files first changed after its own write. The `cycle-*` levers are pooled with their own lever. The second agent's |
| `results/<plan>.jsonl`, `.log` | One JSON line per run of each plan, with every timing, the file changes after the write, what an inspection of the killed profile found, and what was read back; and the orchestrator's log. The plans are `main-cr`, `main-fx`, `cycle`, `idle`, `work` and `supp`, and the rig's smoke runs `smoke`, `smoke2` and `smoke-form`, which no record cites |
| `results/summary-*.txt`, `ages-*.txt` | Per plan: runs kept per cell and the first file change per store; and the age of each store's write split by kept and lost. `summary-main-cr`, `summary-main-fx`, `ages-main-cr` and `ages-main-fx` are the first agent's, and the other summaries the second's |
| `results/cyclecheck.txt` | The Chromium crash cycles: what the relaunch after a kill showed, whether that session wrote its session file after a new tab, and what the files held after the second kill. The second agent's |
| `results/firstsave.txt` | Every Firefox run's first session write after a launch that restored, by setting, with the machine's idle time at the write. The second agent's |
| `results/fxsess.txt` | Every Firefox survival run's session-file writes after the page's own, with the idle time at the write and at the kill. The second agent's |
| `results/formcost.txt` | The form workload, per run and per setting. The second agent's |
| `results/cells.txt` | Every cell with its run count and errors, and which agent ran it. The second agent's |
| `results/iocount.txt` | Nine runs of a node process in a job writing 32 MiB to a pipe, 32 MiB to a file, or nothing, with the job's write counters before and after |
| `results/chain.log`, `chain-supp.log` | The order the plans ran in, with start and end times |
| `results/copy.log`, `browser-copies.sha256` | The copy of the browsers and of the payload into the scratch, and the SHA-256 of six of the browsers' files, each equal to the shared cache's |
| `results/load-supp.trimmed.csv` | The machine's total CPU load every 5 s from 18:31Z to 19:01Z, across the `supp` runs |
| `results/registry-removed.trimmed.tsv`, `registry-removed-dryrun-by-predecessor-1357Z.trimmed.tsv` | The seven values the scratch Firefox left under the three Firefox keys, which the cleanup removed at 18:51Z, and the first agent's dry run of the same at 13:57Z |
| `server-logs/` | The page server's log, one per sitting: every write page served, every tab that reported in, and every phase change |
| `plans/` | Every plan the orchestrator ran |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out, with digests |

**Levers**, by the names the plans and results use:

| Name | What it is |
|---|---|
| `base` | BrowserAI's launch options as they stand on `next` |
| `noaggr` | Chromium without `--enable-aggressive-domstorage-flushing` |
| `hide` | Chromium with `--hide-crash-restore-bubble` added |
| `patch` | Chromium with `profile.exit_type` rewritten from `Crashed` to `Normal` in `Default/Preferences` after the kill |
| `cdpflush`, `cdpflush1s` | A DevTools `Storage.clearDataForOrigin` for `cookies` on `https://durability-flush.invalid` after the write, `D` counted from its answer; or the same once a second through a workload |
| `cdpsetdel` | `Network.setCookie` then `Network.deleteCookies` on that origin |
| `batch512` | 512 throwaway cookies on that origin through Playwright's `addCookies` |
| `pageclose`, `pageclose-noaggr`, `noclose`, `noclose-noaggr` | Every page of the test origin closed after the write, and the controls that close nothing |
| `ss1000`, `ss0` | `browser.sessionstore.interval` and `browser.sessionstore.interval.idle` both at 1000, or both at 0 |
| `active15` | `browser.sessionstore.idleDelay` at 86,400 s |
| `idle1s-default`, `idle1s-ss1000` | `browser.sessionstore.idleDelay` at 1 s, at the default intervals or at 1000 |
| `cycle-*` | Kill, launch again, open one more tab, kill again that many seconds later, launch and read |
| `work`, `form` | The storage workload and the form workload, 60 s each |

## What was cut, and what was left out

- **The per-run directories under `runs\`**: 2,414 files, 273,575,034 bytes,
  each run's temp, local-app-data and registry scratch. What was read back is in
  the results.
- **The research's copies of the browsers and of the payload**: 370 files,
  820,045,696 bytes, and 199 files, 150,109,509 bytes. No binary is here.
- **The source read**: the Chromium and Firefox files fetched at the revisions
  the kb entry names (58 files, 5,186,782 bytes), the session-store and Juggler
  files extracted from `firefox-1553`'s two `omni.ja` (72 files, 1,468,440
  bytes), and `results/citations.txt`, which quotes the lines read. The kb entry
  names file and line, and the probe record's `cite.py` prints the same from the
  sources.
- **`baseline\`**: read-only snapshots, taken before the work, of the real
  Firefox and Chrome for Testing registry keys and of the real Mozilla and
  `ms-playwright` folders. They name the user's own Firefox profiles.
- **Three empty marker files** of a progress watcher.
- **The profile path** in the two registry records, replaced by
  `%USERPROFILE%`, and **the machine's name** in the CPU load's counter path,
  replaced by `<machine>`, each kept under a `.trimmed.` name.

The digests are in `left-out.sha256` and `originals.sha256`; a tree left out by
directory carries its file count and byte total there and no digest per file.

## Privacy

No file here names the user, the user profile or the machine. The privacy scan
found nothing in the 60 files, and its positive control found all nineteen
planted needles; before the cut it found five hits in three files, the three
trimmed above.

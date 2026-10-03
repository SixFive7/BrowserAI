<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- why a headless Firefox launch stops at 180 seconds (Q302)

**What this is.** What the Q302 rig wrote while it launched headless Firefox 341
times between 2026-09-24T23:01Z and 2026-09-25T00:02Z, one launch at a time and
in batches, with and without `MOZ_DISABLE_SAFE_MODE_KEY`, under a commit limit
and under a CPU cap, and the researcher's report. Taken at Firefox **156.0**
(`firefox-1549`, build 20260917210045, copied out of the browsers root into
scratch), `playwright-core` **1.64.0-alpha-1789764292000** and node
**v24.21.0**, both from the payload, on Windows 11 Pro 26200. **134 files
beside this README, 4,293,895 bytes**, of which `runs.zip` is 3,398,478. The rig
is a probe record at
[`docs/probes/2026-09-25-firefox-safe-mode`](../../probes/2026-09-25-firefox-safe-mode/README.md).

The finding: a Firefox browser process that starts while Shift is held enters
safe mode, opens a modal window nobody can see, and never answers
`Browser.enable`. The kb entry says it with the numbers.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#a-headless-firefox-that-starts-while-shift-is-held-never-finishes-launching----measured-2026-09-25) | Every number in the safe-mode entry, and the note that explains the cost-ratio round that produced no browser |
| [kb: re-verification](../../../kb/re-verification.md) | Row 168, and the addition to row 34 |
| [kb: what is not established](../../../kb/not-established.md) | The rows on who held Shift and on a headed launch |
| [microsoft/playwright#43089](https://github.com/microsoft/playwright/issues/43089) | The issue posted on 2026-10-03 quotes the 4 in 133, the 3 of 3 and the 0 in 287 and 155 |

## What is here

Times are UTC. `runs/<arm>/` is one invocation of the rig.

| Path | What it is |
|---|---|
| `REPORT.txt` | The researcher's report: the answer, the evidence chain with file and line for each link, the ranked hypotheses, the table, what was not established, the directions and the cleanup |
| `runs/<arm>/results.jsonl` | One row per launch, all twelve arms: id, start, launch and navigate time or the failure and its duration, profile size, and the process tree read for `-safeMode`. `rig/summary.py` reads these and nothing else |
| `runs/<arm>/events.log`, `jobs.log` | The runner's own log and its job-object records, all twelve arms |
| `runs/<arm>/<run>/` | Nineteen launches whole: all nine stalls (`smoke-0002`, `serial1-0003`, `serial1-0016`, `ab1-0109`, `forced1-0001` to `-0003`, `forced2-0001` and `-0002`), the three fast failures under a commit limit, and seven healthy launches to read them against. Each holds `args.json`, `probe.log` and the profile listings; a stall also holds `probe.suspect-tree.txt` or `inspect-*.txt`, which is where its `-safeMode` content process is |
| `runs.zip` | **Every file the rig wrote under `runs/`**, 1,341 of them, byte for byte, SHA-256 `c72c7264b8d3df09fd91b8a8417ccb533f410f2fc71a25c67cae581eb11138ea`. The 287 healthy process trees behind *0 of 287* and all 329 `probe.log` files are in it and in nothing else here |
| `originals.sha256` | The SHA-256 and size of every file stored here in a changed form, and what changed |
| `left-out.sha256` | The SHA-256 and size of every file left out whole, and why |

**The arms.** `smoke` (2) and `serial1` (30) are one launch at a time without
the override. `abtest0` (2) and `ab1` (200) interleave the arms launch by
launch, odd numbers without the variable and even numbers with it. `par3` and
`par3k` are batches of three without and with it, `par6k` batches of six with
it. `forced1` sets `MOZ_SAFE_MODE_RESTART=1` and `forced2` sets both. `mem450`,
`mem300` and `cpu100` put a commit limit or a CPU cap on the launch's own job.
The launch timeout was 180 s in `smoke`, 60 s in `serial1` and 30 s from
`abtest0` on, so a stall's duration says which arm it came from and not how long
Firefox would have waited.

## What was cut, and what was left out

- **Twelve `probe.log` files are stored under a `.trimmed.` name with their
  terminal escapes removed**: the nine stalls and the three memory failures,
  whose call log Playwright prints with dim and reset sequences. 16 to 56
  sequences each and nothing else; the tree refuses a C0 control byte in text.
  `originals.sha256` lists each original's digest, and `runs.zip` holds each
  original byte for byte.
- **The extracted startup scripts are not here.** The researcher unpacked
  `browser/omni.ja` and the juggler component of `firefox-1549`: 23 files,
  599,358 bytes, Mozilla's and Playwright's source. The kb entry quotes the lines
  it rests on: `BrowserGlue.sys.mjs:395-407` and `:205-206`, `browser-init.js`
  `:411-412`, `:728-737` and `:1052`, `Juggler.js:48`, `:83-87`, `:89-91` and
  `:149`, `protocol/BrowserHandler.js:30-33`. Re-establish them by unpacking the
  same build's `omni.ja`; `left-out.sha256` carries the SHA-256 and size of each
  of the 23 files, so a fresh extraction can be checked against what was read.
- **Twelve empty files**, `runs-*.out`, the stdout of each batch runner, which
  wrote everything to its own logs.
- **The researcher's cleanup left no profiles to cut.** Nine profile
  directories, 7,973,199 bytes, and the 346 MB copy of `firefox-1549` were
  deleted before this batch was taken, as `REPORT.txt` says under CLEANUP. The
  profile listings in each run are what is left of them.

## What it does not show

- **The keyboard.** Nothing recorded who or what held Shift. The rig did not
  read input, by design.
- **The suite's own two stalls of 2026-09-24**, which `REPORT.txt` classifies
  by duration and liveness from the suite log and the product's global log.
  Neither log is here.
- **What the stray sweep logged during those windows** (20 sweeps,
  `terminated=0`), which was read in the product's global log and not kept.

## Privacy

Every path in these files is under this repository's scratch directory or
names the payload. No file names the user profile, the user or the machine;
the privacy scan run before the commit found nothing, and its positive control
found all nineteen planted needles. The process trees list Firefox command
lines, which carry only scratch profile paths.

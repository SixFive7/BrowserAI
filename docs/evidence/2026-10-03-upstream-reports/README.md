<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- the measurements behind the three upstream posts, and the texts as posted (Q313, Q357)

**What this is.** What the drafting research measured on 2026-10-03 between about
00:30Z and 02:56Z before three posts went out under SixFive7 on the maintainer's
word, and the posts as they went out. The Firefox safe-mode reproduction at two
more builds, behind
[microsoft/playwright#43089](https://github.com/microsoft/playwright/issues/43089);
closed shadow roots in snapshots, behind the comment on
[microsoft/playwright#23047](https://github.com/microsoft/playwright/issues/23047);
add-mcp's handling of `CLAUDE_CONFIG_DIR`, behind
[neon-solutions/add-mcp#130](https://github.com/neon-solutions/add-mcp/issues/130);
and a probe of `@playwright/mcp` 0.0.83 that a dropped draft rested on. **113 files
beside this README, 188,265 bytes as cut.** The rigs are a probe record at
[`docs/probes/2026-10-03-upstream-reports`](../../probes/2026-10-03-upstream-reports/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#a-headless-firefox-that-starts-while-shift-is-held-never-finishes-launching----measured-2026-09-25) | The safe-mode check at Firefox 155.0 and 156.0 build 1553 |
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#content-in-a-closed-shadow-root-is-missing-from-the-snapshot----measured-2026-10-03) | The closed shadow roots, and the attach by `--endpoint` measured at 0.0.83 |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 168 and 174 |

## What is here

| Path | What it is |
|---|---|
| `posted/` | The three bodies as posted and the public gist's three files, stored with `.txt` added to each name so that nothing had to be added to them |
| `runs/ff-pw163/`, `runs/ff-mcp083/`, `runs/ff-pw163-run1-inherited-key/` | The safe-mode arms at `playwright-core` 1.63.0 with `firefox-1543` and at 1.64.0-alpha-1790635538000 with `firefox-1553`: per arm a log, the process tree and a `results.jsonl` row. The third is a first run in which `MOZ_DISABLE_SAFE_MODE_KEY` leaked in from the parent environment into every arm, kept because the forced arm hung there too |
| `runs/mcp083*/` | The probe of 0.0.83: two children, the attach by name, a pause armed in code, and `--isolated`. Its log is `mcp-probe.log`; `snapshot-while-parked.txt` is what a snapshot answered while a call was parked |
| `runs/resume-idle/`, `runs/shadow/` | `browser_resume` against upstream's idle timer; and the closed shadow root page, what each reader returned, and the MCP snapshot |
| `runs/registry-after-run2/` | The descriptor a run left in its scratch registry |
| `addmcp-sandbox/out/` | The add-mcp 2.4.1 runs behind the add-mcp issue, against Claude Code 2.1.288 under a scratch home and a scratch `CLAUDE_CONFIG_DIR` |
| `logs/` | The installs, the npm logs, and each probe's exit code |
| `baseline/` | The real Playwright browsers folder and registry, listed by name before and after |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out whole, with digests |

## What was cut, and what was left out

- **The drafts.** Six were written; the bodies as posted are here, and the drafts
  are not: two were revised before posting, one went out as written, one was not
  posted and two were dropped by the maintainer. Their digests are in
  `left-out.sha256`.
- **The profile path**, replaced by `%USERPROFILE%` in nine files stored under
  `.trimmed.` names, with the originals' digests in `originals.sha256`.
- **`logs/envinfo-pw163.txt`**, which describes this machine's hardware and
  software; the posted issue cut its environment block to *Windows 11* for the
  same reason.
- **Listings of the real Mozilla folders**, which name the user's own Firefox
  profiles.
- **The pages and sources read for the drafts**, under `web\`: issues, pull
  requests, release feeds, Mozilla's and mozregression's launch code. Cited by
  address in the posts.
- **The browsers, the packages and the source checkouts**: about 1.7 GB of
  scratch Chromium and Firefox builds, the `playwright` and `@playwright/mcp`
  installs and checkout, add-mcp's package and source, the npm cache, and every
  browser profile the runs wrote.

## Privacy

No file here names the user, the user profile or the machine. The privacy scan,
with a positive control, found nothing.

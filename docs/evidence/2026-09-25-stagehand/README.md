<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- Stagehand 4 against Playwright, and what a snapshot and a click cost BrowserAI (zoom-out track D)

**What this is.** The record of the zoom-out's track D, which asked whether
Stagehand 4 should replace Playwright: the researcher's report, the 80-row map of
BrowserAI's tool surface onto Stagehand's, and the bench that measured both
side by side on one Chrome for Testing binary. **Stagehand was dropped on
2026-10-01 (Q318)**; the bench's Playwright half is a measurement of BrowserAI's
own configuration and is what this batch is kept for. Taken at `@playwright/mcp`
**0.0.82** with `playwright-core` 1.64.0-alpha-1789764292000 and
`@browserbasehq/stagehand` **4.1.0**, Chrome for Testing **154.0.8037.0**
headless, node v26.7.0, on Windows 11. **140 files beside this README,
1,141,178 bytes as cut.** The bench is a probe record at
[`docs/probes/2026-09-25-stagehand`](../../probes/2026-09-25-stagehand/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#what-a-snapshot-and-a-click-cost-through-browserais-configuration----measured-2026-09-25) | Every number in the entry, and the reasons Stagehand was dropped |
| [kb: re-verification](../../../kb/re-verification.md) | Row 175 |

## What is here

| Path | What it is |
|---|---|
| `REPORT.md` | The researcher's report: the answer, the HN claims checked, the 80-tool map in prose, the measurements, the downsides and the directions |
| `feature-map.tsv` | Every one of BrowserAI's 80 tools against Stagehand: has, partial or lacks, and how it was established |
| `upstream-72-tools.txt`, `commits-90d.tsv`, `pw-commits-90d.tsv`, `clone.log` | The 72 upstream tools as listed, both projects' last 90 days of commits, and the clone of Stagehand's source |
| `bench/out/*.json`, `*.log`, `*.err` | One summary, log and error stream per bench: snapshots in four configurations, the settle knob, launch, close and profile, memory, actions, dialogs, fingerprint and the debugging port. `snap-summary.json` is the snapshot table |
| `bench/out/snap-*-sample.txt.json` | One snapshot sample per local page and configuration |
| `bench/out/pw-*-config.json`, `pw-*-output/` | The config each Playwright child was given and the console logs it wrote |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out whole, with digests |

## What was cut, and what was left out

- **The page snapshots the children wrote**, 164 `.yml` files, and the samples
  of the three public sites' pages, 12 files: those are the sites' own text. The
  summaries keep their token counts.
- **The two browser network logs**, which carry this machine's network
  addresses.
- **Eight third-party pages and records**: the Hacker News item and its comments,
  the npm and OSV records and Stagehand's README and versions. `REPORT.md` cites
  each by address.
- **The browsers, profiles, packages and Stagehand's source tree**, about 1.2 GB.
- **One install log's terminal escapes**, removed under a `.trimmed.` name, and
  the SPDX lines added to `REPORT.md`; `originals.sha256` has both originals'
  digests.

## Privacy

No file here names the user, the user profile or the machine. The privacy scan,
with a positive control, found nothing.

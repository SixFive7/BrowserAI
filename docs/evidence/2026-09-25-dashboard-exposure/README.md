<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- Playwright's dashboard against browsers it did not launch

**What this is.** What four rigs printed on 2026-09-25, between 03:57Z and 04:12Z
by the runs' own clocks, when they pointed Playwright's dashboard at browsers
they had launched themselves, and the research report written from it. Taken at
`@playwright/mcp` **0.0.82**, `playwright-core` **1.64.0-alpha-1789764292000**
and node **v24.21.0**, in headless Chromium **154.0.8037.0**, on Windows 11 Pro
10.0.26200. **34 files beside this README, 171,322 bytes.** The rigs are a probe
record at
[`docs/probes/2026-09-25-dashboard-exposure`](../../probes/2026-09-25-dashboard-exposure/README.md).

⚠️ **The real registry was read by name and never written.** Every rig ran with
`PWTEST_SERVER_REGISTRY` and `PWTEST_SOCKETS_DIR` pointing into scratch.
`%LOCALAPPDATA%\ms-playwright\b` was listed five times, before the first rig and
before and after each of the first two, and all five listings are here: 8 names
each, and one SHA-256 for all five,
`2fb761372deede4af40430763a5b7d045a52d5d5295f06d6067b6f95835f76d2`.

⚠️ **`REPORT.md` is the researcher's whole report, recommendations included.**
The facts are what [the kb](../../../kb/playwright/tools-and-artifacts.md#what-playwrights-dashboard-does-to-a-browser-it-did-not-launch----measured-2026-09-25)
records, each against a file here. The recommendations are one researcher's.
What the maintainer decided is
[the dashboard row in DECISIONS](../../../DECISIONS.md#processes-browsers-and-session-modes),
and the three questions that row leaves open are sections 9 and 13 of the report.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#what-playwrights-dashboard-does-to-a-browser-it-did-not-launch----measured-2026-09-25) | Every measured number in the dashboard section, and the correction of the older sentence that the dashboard cannot be reloaded |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 163 to 166, one for each rig |
| [kb: what is not established](../../../kb/not-established.md) | The rows under *Playwright's dashboard* |
| [`DECISIONS.md`](../../../DECISIONS.md#processes-browsers-and-session-modes) | The risks the decision names |

## What is here

Times are UTC, read from the files' modification times before they were copied,
because git keeps none.

| File | Bytes | What it is |
|---|--:|---|
| `REPORT.md` | 35,381 | The research report. Its first paragraph says how it reached disk |
| `probe.log` | 4,859 | The first rig's log, E0 to E14, 03:57Z to 03:58Z |
| `probe-results.json` | 4,810 | The same run as data: every status, header, count and timing the log quotes |
| `owner-descriptor.json` | 900 | The descriptor the owner's browser wrote when it was bound |
| `owner-trace.zip` | 3,994 | The trace the owner recorded across eight dashboard calls. It is also the file the fourth rig served |
| `owner-trace/` | 13,483 | That zip, extracted: `trace.trace`, `trace.network`, `trace.stacks` and three resources, one of them an empty file |
| `screencast-frame.jpg` | 7,093 | The first screencast frame the dashboard sent after `selectTab` |
| `ui-first-load.png` | 24,002 | The dashboard's own page at its first load in a headless tab |
| `ui-second-tab-after-first-closed.png` | 24,002 | A second tab, ten seconds after the first tab was closed and a third browser was bound. **Byte-identical to the file above**, SHA-256 `8e76b12a89f5fd00e0c9100d5938a598b06e538f3e0f7a8d7e1cd0d0fa8fd33e` for both, which is the finding: the tab that stayed never showed the new browser |
| `probe2.log` | 3,101 | The second rig's log, E20 to E25, 04:01Z to 04:02Z |
| `probe2-results.json` | 3,669 | The same run as data, with all ten reload trials |
| `probe2-console-after-listing.txt` | 667 | What `browser_console_messages` returned after the dashboard had listed the session |
| `probe2-snapshot-while-parked.txt` | 511 | What `browser_snapshot` returned while `browser_navigate` was parked, with its `### Paused` section |
| `mcp/config.json` | 504 | The config the second rig wrote for the `@playwright/mcp` child |
| `mcp/out/` | 1,303 | What the child wrote into its output directory: two console logs and two page snapshots |
| `reg/` | 2,009 | The two descriptors the second rig left in the scratch registry: the reload browser's, and the one titled `zoomout-c-mcp (2)` that the child's second browser wrote |
| `probe3.log` | 562 | The third rig's log, 04:06Z |
| `probe4.log` | 1,301 | The fourth rig's log, 04:11Z |
| `trace-viewer.png` | 37,510 | The trace viewer's page, loaded in a headless tab |
| `real-registry-before.txt`, `real-registry-before.time` | 328, 21 | The real registry's names at 03:52:35Z, before any rig ran, and that time |
| `real-registry-before-probe.txt`, `real-registry-after-probe.txt` | 328, 328 | The same listing at 03:57:10Z and 03:58:06Z, either side of the first rig |
| `real-registry-before-probe2.txt`, `real-registry-after-probe2.txt` | 328, 328 | The same at 04:01:41Z and 04:02:30Z, either side of the second |

**What the logs carry that looks like a secret and is not.** `probe.log` and
`probe-results.json` print the dashboard's websocket guid, because handing it
out is what `GET /` was measured doing; the process it belonged to was stopped
at the end of that run. Pipe names carry `e52b5188`, the eight characters
upstream derives from the Windows user name, which
[`2026-09-23-server-registry`](../2026-09-23-server-registry/README.md) already
carries. Every path is under this repository's own scratch directory. No file
here names the user profile, and none holds a cookie or a credential.

## What was left out

- **Four stdout captures**, each byte-identical to the log of the same rig, which
  writes every line to both. `probe-stdout.txt`, 4,859 bytes, SHA-256
  `cf876d90a6538c1321f035224e483b8de80901101a4388a0beb87f03c4f33c7d`;
  `probe2-stdout.txt`, 3,101 bytes,
  `3f0a79cb02efd7f87252c6ad9abc26fd961eb0cdc7fd5a8bcd410c36127fb87e`;
  `probe3-stdout.txt`, 562 bytes,
  `ca55308e4b4aa79d11b701853af1d993f616dc2ad2e327f6af0b3e9311270398`;
  `probe4-stdout.txt`, 1,301 bytes,
  `3056190e96ae35b1ee03d3c3b0862c641693fa662879889d1a256a4435446741`. Each
  digest is also the digest of the log that is here.
- **`probe2-mcp-stderr.txt`, which is empty.** The `@playwright/mcp` child wrote
  nothing to stderr in the whole run, and that is recorded here in place of a
  file of 0 bytes.
- **Five browser profiles**, 273 files and 10,699,302 bytes between them:
  `profile-owner`, `profile-owner2`, `profile-owner3`, `profile-live` and
  `mcp/profile`. They are the rigs' working state and no finding reads them.
- **Three empty directories**: `tmp`, `sockets` and `reg3`.

## Two departures from the bytes as taken

- **`REPORT.md` gained the repository's two-line SPDX header and the blank line
  under it**, because this tree requires one on every `.md` it holds. As written
  it was 35,258 bytes, SHA-256
  `fab6bc98813dbc3e511f591213966a73f6ce82bac2a9eaa610178d6acbb69900`. Nothing
  else in it moved, and it still ends without a newline.
- **The rigs are not here.** They are in the probe record, each with the same
  header added, so the line numbers `owner-trace/trace.stacks` gives are three
  lower than the stored rig's.

Every other file is byte for byte what the rig wrote. All of the text was
written with LF, so [the directory's own note](../README.md) about line endings
changes nothing here.

## What the report says that no file here shows

- **A last reading of the real registry at 04:15:11Z**, 8 names and none
  missing; **an enumeration of named pipes** that found 1,195 and none named
  `pw-*`, before and after; and **a check that no rig process was left alive**.
  The report states all three and kept no output for any of them.
- **Everything it read upstream**: issues, pull requests, release notes and
  `main`. Its section 14 lists the sources and the day they were read. The kb
  entry says which of those were read again on 2026-10-01.
- **Which binary the browser was.** The report names
  `chromium_headless_shell-1246` under `%LOCALAPPDATA%\ms-playwright`. The trace
  carries `HeadlessChrome/154.0.8037.0`, which is that revision's version, and
  nothing here records the path.

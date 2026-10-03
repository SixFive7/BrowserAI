<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- a picture of a headless session, taken beside an agent's call

**What this is.** The two runs of the look rig, one per browser family, taken on
2026-10-03 between 19:45Z and 19:49Z on Windows 11 Pro 10.0.26300, through
BrowserAI's published server 1.1.1-alpha.0.173 over `@playwright/mcp` 0.0.83
(`playwright-core` 1.64.0-alpha-1790635538000): Q317 c's three measurements of a
view-only picture of a headless session, taken with `browser_take_screenshot`
and one file name, on the session the agent is using. **Four files beside this
README.** The rig is a probe record at
[`docs/probes/2026-10-03-look`](../../probes/2026-10-03-look/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#a-picture-of-a-headless-session-beside-an-agents-call----measured-2026-10-03) | Every number in the entry |
| [`DECISIONS.md`](../../../DECISIONS.md#the-management-interface-is-a-tab-in-the-system-browser) | Why the look is not built |

## What is here

| File | What it is |
|---|---|
| `results-chromium.json` | Chrome for Testing 155.0.8059.12 (`HeadlessChrome/155.0.0.0` in its user agent): every call the rig made, its time, whether it erred, the sections its answer carried and which of the page, console and download reports were among them, and the session folder's listing before the pictures, after them and at the end |
| `results-firefox.json` | The same for Firefox 156.0 |
| `look-chromium.log`, `look-firefox.log` | The rig's own summary of each run, one line per case and round |

## What was cut, and what was left out

Nothing was cut. **Left out**: the server's own standard error for each run,
because it names this machine's profile folder in the data root's paths:
`server-stderr-chromium.log`, 17,776 bytes, SHA-256
`c46a8a4afd89bc406016a055c974763d1ae3c30c1ad43c2c9d954d2446849af9`, and
`server-stderr-firefox.log`, 17,760 bytes, SHA-256
`d0712e39f2ea9fa5c71d117b62a574d88823bb4fdb88ab879b076317205fe0a2`. The paths
the four files carry are the lane's scratch folder under the repository. The
Firefox Launcher key held the same value names before and after the two runs,
because the build the rig ran had run from the same path before.

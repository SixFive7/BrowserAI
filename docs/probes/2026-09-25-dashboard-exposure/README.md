<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- what Playwright's dashboard does to a browser it did not launch

Establishes
[What Playwright's dashboard does to a browser it did not launch](../../../kb/playwright/tools-and-artifacts.md#what-playwrights-dashboard-does-to-a-browser-it-did-not-launch----measured-2026-09-25)
and re-verification rows 163 to 166. Evidence:
[`docs/evidence/2026-09-25-dashboard-exposure/`](../../evidence/2026-09-25-dashboard-exposure/README.md).
What was decided from it is
[the dashboard row in DECISIONS](../../../DECISIONS.md#processes-browsers-and-session-modes).

**Why it exists.** The maintainer asked whether Playwright's dashboard could be
shown inside BrowserAI's own interface, and what that would cost a design in
which a session's directory is its lock. The dashboard is upstream's page over
every browser in its registry, so the question became what that page does to a
browser somebody else owns. These four rigs answer it against browsers they
launch themselves. No rig starts BrowserAI.

## Four rigs

| File | What it drives | What it logs |
|---|---|---|
| `dashboard-probe.cjs` | A headless persistent Chromium, bound the way `@playwright/mcp` binds one; the dashboard on a port; a raw websocket client speaking the frames the page speaks; the real page in a headless tab | E0 to E14, one line per finding, and `probe-results.json` |
| `mcp-pause-probe.cjs` | The payload's own `@playwright/mcp/cli.js` over stdio, which is the child BrowserAI forwards to; the dashboard; the real page reloaded ten times | E20 to E25, and `probe2-results.json` |
| `singleton-probe.cjs` | Two dashboards started with `--port` and a third process started with `--kill` | Five lines |
| `trace-viewer-probe.cjs` | `playwright-core/cli.js show-trace --host 127.0.0.1 --port 0` on the trace the first rig wrote, and a headless page that loads it | Six lines and a screenshot |

Nothing here decides anything. The classification is in the kb entry, taken from
what these printed.

## What keeps them off the rest of the machine

⚠️ **`PWTEST_SERVER_REGISTRY` and `PWTEST_SOCKETS_DIR` must both be set, and both
must point under `.work/zoomout/c`.** The first three rigs check that and exit 2
otherwise. The fourth sets both itself. Without the first variable a rig's
browsers land in the real `%LOCALAPPDATA%\ms-playwright\b` and the dashboard it
starts lists, connects to and reaps whatever else is there. Without the second,
the dashboard a rig starts takes the user's one singleton pipe, or loses to a
dashboard the user already runs.

⚠️ **`singleton-probe.cjs` sends a real `--kill`.** It reaches whichever
dashboard holds the singleton pipe, and only `PWTEST_SOCKETS_DIR` makes that the
rig's own.

The rest is in the rigs' own header comments and was checked against their code:

- Every browser is headless. The second rig writes `launchOptions.headless: true`
  and `browserName: "chromium"` into the MCP config it generates. Its comment
  gives the reasons: upstream's default on Windows is a headed browser, and its
  default browser is the machine's own Chrome.
- The dashboard is always started with `--port=0 --host=127.0.0.1`, so it serves
  over HTTP and opens no window.
- `reveal` is never called. It would open Explorer.
- `TEMP` and `TMP` point at `.work/zoomout/c/tmp`, so the dashboard's recording
  directory lands in scratch.
- The first two rigs list the real registry by name at their start and at their
  end, and write nothing to it.
- Every process a rig starts is stopped through the handle the rig holds on it.
  The first two also stop everything and exit 3 after eight minutes.

## Running them

Nobody ran them when this record was written; what follows is read from the rigs
and from their logs.

```
set PWTEST_SERVER_REGISTRY=C:\Source\SixFive7\BrowserAI\.work\zoomout\c\reg
set PWTEST_SOCKETS_DIR=C:\Source\SixFive7\BrowserAI\.work\zoomout\c\sockets
payload\node\node.exe docs\probes\2026-09-25-dashboard-exposure\dashboard-probe.cjs
payload\node\node.exe docs\probes\2026-09-25-dashboard-exposure\mcp-pause-probe.cjs
payload\node\node.exe docs\probes\2026-09-25-dashboard-exposure\singleton-probe.cjs
payload\node\node.exe docs\probes\2026-09-25-dashboard-exposure\trace-viewer-probe.cjs
```

**The paths are compiled in.** Each rig names the repository as
`C:/Source/SixFive7/BrowserAI`, reads `playwright-core` from
`payload/mcp/node_modules`, and writes its logs to `.work/zoomout/c/evidence`,
which is where the research ran. A run from another checkout needs the first
constant changed, and a run that changes it is a new measurement with a date of
its own.

**The order matters once.** The fourth rig serves `owner-trace.zip`, which the
first one writes.

**The node.** `probe-results.json` records `v24.21.0`, which is the version of
`payload/node/node.exe`. The logs do not record which binary that was.

⚠️ **The browser has to be there first.** The rigs launch `chromium` with
`headless: true` and name no browsers path, so `playwright-core` takes its
default. The research report says that was `chromium_headless_shell-1246` under
`%LOCALAPPDATA%\ms-playwright`, and the first rig's trace carries
`HeadlessChrome/154.0.8037.0`, which is that revision's version. On 2026-10-01
that directory holds headless shell revisions 1200, 1223, 1234 and 1247 and not
1246, so a run today fails at its first launch until that revision is installed
or `PLAYWRIGHT_BROWSERS_PATH` names a root that holds one.

## What they leave behind

Five browser profiles (`profile-owner`, `profile-owner2`, `profile-owner3`,
`profile-live` and `mcp/profile`), the scratch registry, and an empty `tmp`, all
under `.work/zoomout/c`. The first rig removes `profile-owner` and the scratch
registry before it starts; the second removes the registry and the whole `mcp`
directory.

## How the stored copies differ from the ones that ran

⚠️ **The two SPDX lines and the blank line under them were added when the rigs
were stored**, which is what every script this repository holds carries. Nothing
else in them moved. So a line number a rig's own output gives is three lower
than the stored file's: `owner-trace/trace.stacks` names lines 282, 286 and 304
of `dashboard-probe.cjs`, which are 285, 289 and 307 here.

None of the four selects a process by image name, or enumerates processes at
all: a search of each for the eight spellings
[`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
keys on found none, and the same search finds one in
`2026-09-14-firstrun/observe.ps1`.

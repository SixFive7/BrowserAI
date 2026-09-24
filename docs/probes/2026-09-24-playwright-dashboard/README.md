<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- Playwright's own dashboard, opened against a scratch registry

**What this re-establishes.** That the descriptor directory behind T7 is read by
something a person can look at, what that something shows, and that reloading its
page hangs the session list. Written for the maintainer's question -- can I see
it? -- and kept because the answer to *why does the growth matter* is *this*: the
dashboard is what degrades.

**Cited by** [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#every-launched-browser-leaves-a-descriptor-in-localappdatams-playwrightb-and-nothing-reaps-it----measured-2026-09-16)
and by [`DECISIONS.md`](../../../DECISIONS.md#processes-browsers-and-session-modes).
What it produced is at
[`docs/evidence/2026-09-23-server-registry`](../../evidence/2026-09-23-server-registry/README.md).

## What it does

`dashboard-demo.cjs` starts Playwright's dashboard application out of the
repository's own payload `playwright-core`, opens one demo browser with two tabs
so the list has something in it, prints the URL it bound, and stays up until a
`STOP-DASHBOARD` file appears beside it or three hours pass.
`dashboard-shot.cjs` drives a second browser to the printed URL and captures the
page, once as served and once after a reload.

⚠️ **`PWTEST_SERVER_REGISTRY` must be set before either is run.** Both were run
against a **scratch** registry directory, and the real
`%LOCALAPPDATA%\ms-playwright\b` was read and never written -- the demo log prints
the real directory's count at the start and at the end, and both readings were
`3815`. Running these without that variable would put demo descriptors in the
maintainer's own registry, which is the one thing the T7 measurement was careful
not to do.

⚠️ **The reload hang is a property of the dashboard and not of the rig.** One
`SessionProvider` is shared per server, and a closing connection's `dispose()`
removes **every** listener including the new connection's, so a reloaded tab never
receives `SessionsChanged` and its session list never fills. A fresh connection
against the same server lists normally. Open the bare URL in a new tab; do not
reload.

## Running it

```
node dashboard-demo.cjs          # prints the URL, stays up
node dashboard-shot.cjs <url>    # captures the page
```

Both need `PWTEST_SERVER_REGISTRY` pointed at a scratch directory, and both expect
the payload to be assembled at `payload/mcp/node_modules` -- they read
`playwright-core` from there and not from a global install. Taken at
`playwright-core` **1.64.0-alpha-1789764292000**, node **v24.21.0**, Chromium
**154.0.8037.0**.

⚠️ **The two SPDX lines at the head of each file were added when they were
persisted**, which is what every script this repository holds carries; nothing
else about either file moved.

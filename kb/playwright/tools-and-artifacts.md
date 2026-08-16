<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The tool surface and the artifacts it writes

## The tool surface and the package shape

Read from the shipped tree during the 2026-08-13 feasibility research unless a
later date is given. `@playwright/mcp` 0.0.79.

**`@playwright/mcp` is a 20-line shim.** The whole package is `cli.js`,
`index.js` and type definitions. `index.js` in full:

```js
const { tools } = require('playwright-core/lib/coreBundle');
module.exports = { createConnection: tools.createConnection };
```

The implementation is `playwright-core/lib/coreBundle.js` — **3.4 MB**,
esbuild-bundled. `[FLOATS]`

**Three tool counts, and a golden test written against the wrong one fails on
day one.** **78** entries in the internal registry array; **69** the maximum ever
exposed over MCP (9 are `skillOnly` and always stripped); **24** the default with
no `capabilities` set. The founding-bug reproduction saw the 24 over a real
`tools/list`. `Verified 2026-08-16 @ @playwright/mcp 0.0.79 / playwright-core
1.63.0-alpha-2026-08-05`: all three re-measured, the 78 and the 9 by reading
`browserTools` in-process and the 24 and 69 over a real `tools/list`. They now
regenerate into
[`upstream-snapshots/tools-list.json`](../../upstream-snapshots/tools-list.json)
on every build, so a move is a diff rather than a memory. `[FLOATS]`

**The `storage` capability is 17 tools** — the cookie / localStorage /
`storageState` set. The legacy `interactive` server ran without it, so in that
process they did not exist at all.

### The per-capability breakdown, counted

**Measured 2026-08-16 @ `@playwright/mcp` 0.0.79 / `playwright-core`
1.63.0-alpha-2026-08-05.** Re-establish it by regenerating the snapshot:
`pwsh -File build/Update-UpstreamSnapshots.ps1 -Accept`, which reads
`browserTools` from the resolved bundle and cross-checks every number against a
real `tools/list`. `[FLOATS]`

| Capability | Tools it carries | Of those, `skillOnly` | Surface with it alone |
|---|---|---|---|
| `core` | 21 | 2 | unconditional |
| `core-input` | 7 | 5 | unconditional |
| `core-navigation` | 4 | 2 | unconditional |
| `core-tabs` | 1 | 0 | unconditional |
| `core-install` | **0** | — | unconditional, and carries nothing |
| `config` | 1 | 0 | 25 |
| `network` | 4 | 0 | 28 |
| `pdf` | 1 | 0 | 25 |
| `storage` | 17 | 0 | 41 |
| `testing` | 5 | 0 | 29 |
| `vision` | 6 | 0 | 30 |
| `devtools` | 11 | 0 | 35 |
| **all twelve** | **78** | **9** | **69** |

**The `core` family is unconditional, and that is why every column above starts
at 24.** `filteredTools(config)` is
`browserTools.filter(t => t.capability.startsWith("core") || config.capabilities?.includes(t.capability)).filter(t => !t.skillOnly)`,
so the five `core*` capabilities are on whatever `capabilities` says — setting
`capabilities: ["config"]` yields **25** tools, not 1. Naming a `core*`
capability explicitly therefore does nothing, and **no configuration can reduce
the surface below the base 24**. `[FLOATS]`

**The nine `skillOnly` tools, by name:** `browser_console_clear`,
`browser_network_clear` (`core`); `browser_press_sequentially`,
`browser_keydown`, `browser_keyup`, `browser_check`, `browser_uncheck`
(`core-input`); `browser_navigate_forward`, `browser_reload`
(`core-navigation`). They are in the registry, they are never in a `tools/list`,
and the property is `tool.skillOnly` on the registry entry rather than anything
on the schema. `[FLOATS]`

**What BrowserAI's own modes expose, measured over the wire rather than added
up:** `config` + `vision` + `devtools` gives **42**, and `persistent` adding
`storage` gives **59** ([§C](../../plan/C-sessions.md#three-modes-and-tracing-as-a-modifier)).
Those are the same two numbers the `createConnection` experiment below produced
from two connections in one process, which is a second, independent route to
them. `[FLOATS]`

> ⚠️ **Corrected 2026-08-16 (previously: "A per-capability breakdown is not
> recorded anywhere in this repository … `[UNVERIFIED]` — the numbers were never
> observed, not merely lost. Count them from the resolved bundle at the next
> review rather than from memory.")** They have now been counted from the
> resolved bundle, which is what build-order step 4 was told to expect. The
> `[UNVERIFIED]` marker is gone because the numbers were observed, not because
> anybody reasoned about them.

**One node process can serve several configurations.** Verified: two connections
built through the programmatic `createConnection` API produced correctly
divergent surfaces — **42 vs 59 tools** — with no module-global browser state and
browsers created lazily on first tool call. It is reachable only through that
API, which is why the charter rejects it on scope rather than on capability.
`[FLOATS]`

**`playwright-core` whitelists `"./lib/coreBundle"` in its `exports` map**, so
`require('playwright-core/lib/coreBundle')` is a supported import, not a blocked
deep path. It exposes `browserTools` (a flat array of plain, inert objects),
`filteredTools`, `createConnection` and `BrowserBackend`. `defineTool` is the
identity function — no class, no registry, no side effect. No type definitions
and no semver guarantee attach to it. `[FLOATS]`

**The `playwright` package (4.85 MB) is a declared dependency that is never
loaded.** Prunable, but `npm ls` then calls the tree broken. `[FLOATS]`

**`core-install` is declared in `config.d.ts` but no tool carries it** in 0.0.79 —
a dead capability string; setting it does nothing. `[FLOATS]`

**Upstream publishes daily alpha builds of `playwright-core`.**
`@playwright/mcp@latest` is the released dist-tag; the `playwright-core` alpha
beneath it arrives as that package's own **exact** dependency (no `^`, no `~`),
which is what makes the browser revision pinned while the package is not.
`[FLOATS]`

## Tools that reach credentials

**`browser_run_code_unsafe` returns an `httpOnly` cookie against the default
surface.** Demonstrated 2026-08-14: with the default **24-tool** surface and zero
`browser_cookie_*` tools exposed, `async (page) => page.context().cookies()`
returned an `httpOnly` bearer token. The tool is in `core`, so **no capability
setting removes it**. It was the *only* hole — `browser_evaluate` →
`document.cookie` returns `""`, and `browser_network_request` strips `Cookie` and
`Set-Cookie`. `[FLOATS]`

**`browser_storage_state` and the cookie tools return `httpOnly` cookies** —
session bearer tokens JavaScript cannot read. Any mode permitted to call them is
credential-bearing. `[FLOATS]`

**`browser_storage_state` never captures IndexedDB.** It calls `storageState()`
with no options, so `{indexedDB: true}` is never passed. A persistent profile
carries IndexedDB, so a "saved" session silently omits it and the tool is
*weaker* than doing nothing. `[FLOATS]`

**`browser_get_config` does not redact.** Its handler is
`JSON.stringify(context.config, null, 2)` with no filtering, so it emits
`config.secrets` in plaintext if that key is ever set. It is not set today.
`[FLOATS]`

**`browser_annotate` opens a dashboard window and blocks until a human finishes
drawing** — and the window appears in headless too. `[FLOATS]`

## Artifacts and output-directory behaviour

All read from the shipped bundle or observed against a real child. `[FLOATS]`

**Playwright writes every artifact flat into one directory with a generated
name**, mixing machine churn with hand-named work. **Nine fixed generator
prefixes** make classification exact rather than heuristic: `console`,
`download`, `network`, `page`, `request`, `response`, `result`, `storage-state`,
`video`.

**The generated name format is `page-2026-08-14T04-11-50-882Z.png`** — a
timestamp, which is precisely what made 346 accumulated session directories
untriageable.

**A relative `filename` resolves against the child's cwd.** That is the whole
reason ten repositories currently run a `deny` hook on `browser_take_screenshot`,
and it is closed by setting the child's `WorkingDirectory` instead of by a hook.

**`_meta.json`, `_meta.cwd` and `_meta.raw` are read by the child before zod
parsing** and stripped before the tool sees them. Undocumented but real, and
available for a proxy to inject (JSON error format, relative-path base).

**Killed children leak `browser@<guid>` descriptors.** Each is a JSON file in the
browsers-registry root holding the absolute `userDataDir` and `workspaceDir`;
`BrowserServer.stop()` removes them only when there is **no** `userDataDir`. **28
were observed and removed on 2026-08-14** (`[MACHINE]` for the count). The
registry root sits at `%LocalAppData%\BrowserAI\browsers\`, outside `current\` under the current design — a tree
that should be read-only and is wiped on update.

**Real screenshots are not byte-stable across runs**, so passthrough-fidelity
assertions need a canned blob from a fake child rather than a live capture.

<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-21 - what a page's WebMCP tools reach

Establishes
[A page can add tools to the child's `tools/list`, and its own text reaches a caller](../../../kb/playwright/tools-and-artifacts.md#a-page-can-add-tools-to-the-childs-toolslist-and-its-own-text-reaches-a-caller--measured-2026-09-21),
taken during the `@playwright/mcp` 0.0.81 -> 0.0.82 review. Evidence:
[`docs/evidence/2026-09-21-webmcp/`](../../evidence/2026-09-21-webmcp/README.md).

**Why it exists.** 0.0.82 moved `browser_webmcp_list` and `browser_webmcp_call`
to `skillOnly`, so both left the wire - and in the same release the page's own
tool names, descriptions and schemas started arriving in the snapshot every
snapshot-bearing tool result carries, and the page's tools started appearing in
the child's own `tools/list`. Those two changes are easy to read as one and they
point in opposite directions, so the second is measured rather than inferred
from the first.

## Two rigs, and both are needed

| File | What it establishes |
|---|---|
| `probe.mjs` | What UPSTREAM does. Drives the payload's own `cli.js` over stdio against a page that registers two WebMCP tools, once with upstream's default and once with `webmcp: false`. **Run it twice and diff**: a line present in both is a line the key does not reach |
| `through-browserai.mjs` | What a CALLER of BrowserAI gets, which is a different question with a different answer for each half. BrowserAI answers `tools/list` from the run's own child, which never navigates, and forwards `tools/call` results verbatim - so reasoning either half from the other is the thing this rig exists to stop |

Neither decides anything. The classification is in the kb entry, taken from what
these printed.

## Running them

```
node docs/probes/2026-09-21-webmcp/probe.mjs payload .work/webmcp-probe on
node docs/probes/2026-09-21-webmcp/probe.mjs payload .work/webmcp-probe off
node docs/probes/2026-09-21-webmcp/through-browserai.mjs \
  src/BrowserAI/bin/Release/net10.0-windows/win-x64/publish/BrowserAI.Server.exe \
  .work/webmcp-e2e
```

Run them under the payload's own `node.exe`, which is what the product runs.

⚠️ **`through-browserai.mjs` creates a real session in the app root's index and
destroys it at the end.** Point its directory inside `.work/` and nowhere else.
It does not set `BROWSERAI_ROOT`, because
[setting it does not isolate a run](../../../CLAUDE.md) and would provoke a
provisioning download into an empty tree.

Both rigs reach the shared provisioned browsers root through
`PLAYWRIGHT_BROWSERS_PATH`, and both set `PLAYWRIGHT_SKIP_BROWSER_GC=1` for the
reason [`build/Build-Payload.ps1`](../../../build/Build-Payload.ps1) sets it:
upstream's stale-browser collector deletes any registry directory no `.links`
entry references, and this root holds more than any one rig put there.

## The hung-call shape, and the titled tool

Two later measurements used the same two rigs with the page changed, and both are
recorded here rather than given files of their own, because the change is one
line of the page each time.

**A page tool that never answers** is
`invokeTool: () => new Promise(() => {})`. Call it through `probe.mjs`'s own
request loop and keep asking the SAME child for `browser_snapshot` while it
pends: that is the whole of
[the no-block measurement](../../../kb/playwright/tools-and-artifacts.md#a-hung-page-tool-does-not-block-the-child-and-is-released-by-navigating-away--measured-2026-09-21),
and the release is timed by navigating the tab away afterwards. ⚠️ **The call
never completes on its own**, so a rig that waits for it has no end condition
but its own clock.

**A tool with a display title** is `{ name: "raw_name_here", title: "Human
Title" }`. `probe.mjs`'s `probe_tool_alpha` already carries one, which is what
makes the difference visible without changing anything: upstream builds the
entry with `title: tool.title || tool.name`, so the annotations carry the TITLE
while the snapshot block prints the NAME. That is
[the name rule](../../../kb/playwright/tools-and-artifacts.md#a-page-tools-wire-name-is-built-from-the-pages-tool-name-and-annotationstitle-is-not-that-name--measured-2026-09-21),
and it is the fact `browserai_page_tool` resolves on.

**Both are also driven from the suite now**, against the published binary, by
`PageToolTests` — which is a different instrument and not a replacement: it
asserts what BrowserAI does with these facts, and these rigs are how the facts
themselves are re-established.

## The page's contract, and why it is spelled the way it is

`collectToolsInPage` in the resolved bundle reads
`document.modelContext ?? navigator.modelContext` and calls `getTools()`, so the
page sets `document.modelContext`. The two tools' descriptions are deliberately
unmistakable strings - `PAGE-AUTHORED-DESCRIPTION-ALPHA`,
`PAGE-AUTHORED-PARAM-DESCRIPTION` - because the question is whether text a PAGE
wrote reaches a model, and a string nobody could mistake for Playwright's own is
what makes the answer readable rather than argued.

One tool carries `readOnlyHint` and the other `consequentialHint`, which is how
the `[readOnly]` and `[consequential]` markers in the output are shown to be the
page's own annotations passing through rather than something upstream decides.

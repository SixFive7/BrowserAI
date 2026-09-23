<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-21 - what a page's WebMCP tools reach

What
[kb: a page can add tools to the child's `tools/list`, and its text reaches a
caller](../../../kb/playwright/tools-and-artifacts.md#a-page-can-add-tools-to-the-childs-toolslist-and-its-own-text-reaches-a-caller----measured-2026-09-21)
was cut from. The rig is
[`docs/probes/2026-09-21-webmcp`](../../probes/2026-09-21-webmcp/README.md).

Taken during the `@playwright/mcp` **0.0.81 -> 0.0.82** review, at
`playwright-core` **1.64.0-alpha-1789764292000**, node **v24.21.0**, Chromium
**154.0.8037.0** (revision **1246**), Windows 11 Pro 26200. This is the first
`playwright-core` to reach this tree through the wrapper's own pin since
2026-09-17; the [dated override](../../../DECISIONS.md#the-two-exceptions-to-the-versioning-policy)
was retired in the same batch.

## What is here

| File | Bytes | What it is |
|---|--:|---|
| `child-webmcp-default.log` | 2,012 | The payload's own `cli.js`, driven over stdio with **no `webmcp` key written**, which is upstream's default and the state BrowserAI ships. `tools/list` **72 -> 74**, three `notifications/tools/list_changed`, and the page's own descriptions and schemas inside the snapshot |
| `child-webmcp-false.log` | 1,333 | The same drive with `webmcp: false`. **The control**: `tools/list` 72 -> 72, no notification, no tab-header line, no snapshot block. Every difference between these two files is what the key reaches |
| `through-browserai.log` | 8,784 | The same page through the published `BrowserAI.Server.exe`. `TOOLS BEFORE` and `TOOLS AFTER` are **both 78**, a call naming a page-supplied name is refused at the door, and the snapshot text arrives anyway. Includes the server's stderr, which is where the child's `"version":"1.64.0-alpha-1789764292000"` and its `{"tools":{"listChanged":true}}` are visible |

## What was cut, and what it was cut from

Nothing was trimmed: each file is the probe's whole stdout for one run, the
`=====` banners included. The two child logs differ only in the one config key
and in the loopback port and timestamps the run generates, which is what makes
the diff between them the measurement.

The page is the same in all three runs and is in the rig, not here: two
tools whose descriptions are deliberately unmistakable strings
(`PAGE-AUTHORED-DESCRIPTION-ALPHA`, `PAGE-AUTHORED-PARAM-DESCRIPTION`), so a
reader can see at a glance whether page-authored text reached a model instead
of having to trust a summary of it.

`through-browserai.log` ends with a real `browserai_destroy`, so the session it
created is gone and the paths in it name a directory under `.work/` that no
longer exists.

## What is not here

**No timing.** The collection runs an `evaluate` in every frame of the current
tab on every snapshot-bearing call, bounded at 5 s per frame by upstream's own
`kFrameTimeout`; what that costs on a real page was not measured and no number
in the kb entry claims it was.

**No second browser family.** Every run is Chromium. Nothing in the mechanism is
Chromium-specific as far as the bundle shows, and nothing here establishes that.

**No page that refuses to answer.** The 45 s hang that the 2026-09-15
`browser_webmcp_call` deny rests on was not re-taken, because the tool it was
about is no longer on the wire in any configuration. The bundle read that says
the call path is still unbounded is in the review record, not here.

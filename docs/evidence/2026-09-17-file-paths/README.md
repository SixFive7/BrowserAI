<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 - the `filePaths` pointer measurement

What the pointer table in
[kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#every-artifact-pointer-a-tool-result-carries-is-absolute----measured-2026-09-17)
was cut from. The rig is
[`docs/probes/2026-09-17-file-paths`](../../probes/2026-09-17-file-paths/README.md).

Taken at `@playwright/mcp` **0.0.81** / `playwright-core`
**1.64.0-alpha-2026-09-17**, node **v24.21.0**, Chromium **154.0.8037.0**
(revision **1245**), Windows 11 Pro 26200. The `playwright-core` version is
reached through the
[dated override](../../../DECISIONS.md#the-two-exceptions-to-the-versioning-policy);
no released `@playwright/mcp` pins it.

## What is here

| File | Bytes | What it is |
|---|--:|---|
| `child-relative.log` | 4,656 | The payload's own `cli.js`, driven over stdio with `filePaths: "relative"`. **The before half of the control** |
| `child-absolute.log` | 6,395 | The same drive with `filePaths: "absolute"`. Every pointer in the table is a line in these two files, side by side |
| `through-browserai.log` | 13,656 | The same drive through the published `BrowserAI.Server.exe`, with BrowserAI's own generated config. Includes the server's stderr, which is how the child's `"version":"1.64.0-alpha-2026-09-17"` is visible |

## What was cut, and what it was cut from

Nothing was trimmed: each file is the probe's whole stdout for one run,
including the results that carry no pointer at all. The two child logs differ
only in the `filePaths` value passed and in the timestamps upstream generates,
which is what makes the diff between them the measurement.

`through-browserai.log` ends with a real `browserai_destroy`, so the session it
created is gone; the paths in it name a directory under `.work/` that no longer
exists. **That is the point of keeping the log rather than the tree** - the
pointers are what was being measured, not the files they named.

## What is not here

No run drove a **paused-debugger location**, which the pull request's own body
named. The kb entry records that shape as a reading of the bundle rather than a
measurement, and this directory holds nothing that would support it either way.

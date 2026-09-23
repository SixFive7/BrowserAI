<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-08-18 - what the client silently truncates

Re-establishes the **2,048 UTF-16 characters per model-facing string** budget in
[kb](../../../kb/mcp/protocol.md#what-2kb-each-means----measured-2026-08-18--claude-code-21234),
which `ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget`
holds the whole published surface against. `probe-server*.js` publish strings of
stepped lengths as a fake MCP server, `capture.js` proxies the client's HTTP
traffic to disk, and `analyse.js` diffs what arrived against what was published.
[`RECIPE.md`](RECIPE.md) is the whole procedure, written for a project that has
never heard of BrowserAI.

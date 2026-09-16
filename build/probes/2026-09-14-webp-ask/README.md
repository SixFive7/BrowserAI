<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-14 - a WebP screenshot past 16,383 px is zero bytes

Re-establishes
[A WebP screenshot past 16,383 px comes back as zero bytes](../../../kb/playwright/tools-and-artifacts.md#a-webp-screenshot-past-16383-px-comes-back-as-zero-bytes-with-iserror-false--measured-2026-09-14),
with **no BrowserAI process on the path**: `raw-child.js` drives
`@playwright/mcp/cli.js` over stdio, `rpc.js` is the JSON-RPC client, and
`which-chromium.js` walks the live pid tree to record which binary each run
actually drove - without that walk the provisioned Chromium and the machine's
own Google Chrome are indistinguishable. Evidence:
[`docs/evidence/2026-09-14-webp-ask/`](../../../docs/evidence/2026-09-14-webp-ask/README.md).

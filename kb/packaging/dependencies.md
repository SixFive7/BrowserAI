<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Dependencies: provenance, cost and payload licensing

## Package provenance, as looked up

**`ModelContextProtocol` 2.2.0 was latest as of 2026-08-13**, Apache-2.0, 23.6M
downloads, the **Tier 1** SDK under the MCP project — which Anthropic donated to
the Linux Foundation's Agentic AI Foundation on **2025-12-09**. It began as
`PederHP/mcpdotnet`, now archived. The main package's hosting dependency is
abstractions-only and does **not** drag in ASP.NET; `ModelContextProtocol.Core`
alone is a viable smaller surface (`McpServer.Create` + `StdioServerTransport`,
and the `[McpServerTool]` attributes already live there). Verified 2026-08-14.
`[FLOATS]`

**A correctly stamped version comment went stale in three weeks.**
`SixFive7/OutlookAI` pins `ModelContextProtocol` 1.4.1 with a csproj comment
reading *"1.4.1 = latest stable on nuget.org as of 2026-07-23 (2.0.0 is still
preview)."* Re-checked against nuget.org's flat-container index on **2026-08-14**:
2.0.0, 2.1.0 and 2.2.0 have all shipped stable, so the comment's central claim is
now false and nothing in that build says so. **The date stamp is the only reason
the staleness is detectable at all.** `[FLOATS]`

**Other versions looked up, with their stamps:** Velopack and `vpk` **1.2.0**
(MIT); TUnit **1.65.0** as of 2026-08-13 (MIT, source-generated, reflection-free,
MTP-native; 1.0 shipped 2025-11-05; ~623K downloads/mo, growing 2.24× YoY);
`Verify.TUnit` **31.28.0** as of 2026-07-31, same monorepo and release as
`Verify.XunitV3`, with *more* test projects covering the TUnit integration;
`@modelcontextprotocol/inspector` **2.2.0**. **FluentAssertions relicensed at
exactly 8.0.0** to a bespoke non-SPDX licence with a commercial tier. TUnit is
**MTP-only and conflicts with `Microsoft.NET.Test.Sdk`**; Coverlet does not work
under MTP (`Microsoft.Testing.Extensions.CodeCoverage` instead). `[FLOATS]`

## Token cost of the tool surface

Measured **2026-08-13** with `tiktoken` `cl100k_base` against live `tools/list`
payloads from `@playwright/mcp` 0.0.79. `[FLOATS]`

| | Eager clients | Claude Code, deferred loading |
|---|---:|---:|
| Four servers as registered today | ~23,000 tok | ~985 tok |
| One perfectly-curated proxy | ~11,600 tok | ~330 tok |

**The entire achievable saving under deferred loading is ~650 tokens**, about
0.3% of a 200k window. Without deferred loading a consolidated surface saves
~65%. Recorded because the charter's *Non-reasons* section rests on it: this is
not why the project exists.

## Third-party payload, as shipped

Verified **2026-08-14** against the versions in the payload table
([component sizes](../playwright/provisioning-and-timings.md#component-sizes)), by reading the shipped trees and binaries.
`[FLOATS]` — every row moves when its component does.

| Component | Terms as shipped | What is in the tree |
|---|---|---|
| `@playwright/mcp`, `playwright-core` 0.0.79 | Apache-2.0 | The vendored `node_modules` tree carries the package `LICENSE`. **No `NOTICE` file is published upstream**, so §4(d) has nothing to propagate |
| `ModelContextProtocol` 2.2.0 | Apache-2.0 | Mid-transition from MIT; unrelicensed contributions remain MIT |
| Velopack 1.2.0 | MIT | Notice only |
| Node.js v24 | MIT **plus aggregate terms** for OpenSSL, ICU, V8, zlib and c-ares | Shipping "a single `node.exe`, nothing else" drops Node's `LICENSE`, which is not optional |
| `chromium-headless-shell` 1237 | BSD-3-Clause | `LICENSE.headless_shell` plus a **40,178-line** credits file. Binary is unbranded |
| `ffmpeg` 1011 | LGPL-2.1 | `COPYING.LGPLv2.1` already ships in the directory. Spawned by `playwright-core` as an unmodified separate executable, so §6's relink requirement does not bite |
| `winldd` 1007 | **no license file shipped at all** | Nothing in the tree to ship |
| full `chromium` 1237 | **Google-branded, no OSS license file anywhere in the tree** | `chrome.exe` reports CompanyName **"Google LLC"** and **"Copyright 2026 Google LLC. All rights reserved."**; its `ABOUT` points at Google's Chrome Terms of Service |

**The only on-point public statement on redistributing Chrome for Testing is
adverse.** A Google engineer, 2023: *"Chrome for Testing is a flavor of Google
Chrome, so google.com/chrome/terms applies"* — which forbids redistribution. This
is a citation, not a measurement, and it is not legal advice; it is recorded
because it is the single piece of evidence the provisioning decision rests on.

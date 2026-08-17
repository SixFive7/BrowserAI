<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Dependencies: provenance, cost and payload licensing

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · `ModelContextProtocol` 2.2.0 · Velopack 1.2.0 · Node v24.19.0 LTS · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · `chromium-headless-shell` 1237 · `ffmpeg` 1011 · `winldd` 1007.
Measured on [the reference machine](../README.md#the-reference-machine).

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
| `ModelContextProtocol`, `ModelContextProtocol.Core` 2.2.0 | Apache-2.0 | **Corrected 2026-08-16 (previously "Mid-transition from MIT; unrelicensed contributions remain MIT")** — that describes the transition and not what is in the tree. Nothing is in the tree: both are compiled into `BrowserAI.exe` and a NuGet package's licence stays in the machine's package cache. Upstream's `LICENSE` is **12,227 bytes and grants three licences** — Apache-2.0, MIT for contributions never relicensed (`Copyright (c) 2024-2026 Model Context Protocol a Series of LF Projects, LLC.`), and CC-BY-4.0 for documentation — and its Apache half carries §1–9, ending at *END OF TERMS AND CONDITIONS* with **no appendix**, although its own §4 says *"an example is provided in the Appendix below"*. It is reproduced whole in `THIRD-PARTY-NOTICES.txt`. Re-establish: read `<pkg-cache>\modelcontextprotocol\<v>\modelcontextprotocol.nuspec` for the `repository commit`, then fetch `LICENSE` from that commit |
| `Microsoft.Extensions.*` — **17 packages** | MIT | Measured 2026-08-16 by reading every `.nuspec` in the resolved closure of `src/BrowserAI/packages.lock.json`. All 17 carry `<license type="expression">MIT</license>` and the copyright field `© Microsoft Corporation. All rights reserved.`; two are direct references (`Logging`, `Logging.Console`) and 15 transitive. **Two source repositories and two different licence copyright lines**: 16 from `dotnet/dotnet` (`Copyright (c) .NET Foundation and Contributors`; commits `e2f47b01…` and `f7d90799…` carry byte-identical `LICENSE.TXT`) and `Microsoft.Extensions.AI.Abstractions` 10.8.3 from `dotnet/extensions` (`Copyright (c) .NET Foundation. All rights reserved.`). Nothing is in the tree; all of it is reproduced in `THIRD-PARTY-NOTICES.txt`, and the package list there is derived from the lock file by `ThirdPartyNoticeTests` so a new arrival is a red build |
| Velopack 1.2.0 | MIT | Notice only |
| Node.js v24 | MIT **plus aggregate terms** for OpenSSL, ICU, V8, zlib and c-ares | Shipping "a single `node.exe`, nothing else" drops Node's `LICENSE`, which is not optional. **The file is not downloadable beside `node.exe`** — measured 2026-08-16, `dist/v24.19.0/win-x64/` publishes only `node.exe`, `node.lib` and the two `node_pdb` archives, so the licence has to come out of `node-v24.19.0-win-x64.zip` ([kb](../playwright/provisioning-and-timings.md#component-sizes)). `build/Build-Payload.ps1` extracts both entries from the verified archive and fails if either is missing |
| `chromium-headless-shell` 1237 | BSD-3-Clause | `LICENSE.headless_shell` plus a **40,178-line** credits file. Binary is unbranded |
| `ffmpeg` 1011 | LGPL-2.1 | `COPYING.LGPLv2.1` already ships in the directory. Spawned by `playwright-core` as an unmodified separate executable, so §6's relink requirement does not bite |
| `winldd` 1007 | **no license file shipped at all** | Nothing in the tree to ship |
| full `chromium` 1237 | **Google-branded, no OSS license file anywhere in the tree** | `chrome.exe` reports CompanyName **"Google LLC"** and **"Copyright 2026 Google LLC. All rights reserved."**; its `ABOUT` points at Google's Chrome Terms of Service |

**The only on-point public statement on redistributing Chrome for Testing is
adverse.** A Google engineer, 2023: *"Chrome for Testing is a flavor of Google
Chrome, so google.com/chrome/terms applies"* — which forbids redistribution. This
is a citation, not a measurement, and it is not legal advice; it is recorded
because it is the single piece of evidence the provisioning decision rests on.

### What vendoring a runtime actually costs — two in-house cases

Measured **2026-08-16** by reading the repositories and their git history, on this
machine. `[MACHINE]` throughout — true of two repositories, not of the world. The
charter's provision-don't-bundle position currently argues from **licensing alone**;
this is the empirical half, and it is also the evidence behind the statement that a
bundled browser makes CVE response a release obligation.

**Vendoring a runtime fails by silence, not by breakage.**
`C:\Source\ExoFabric\Netwerkplek` vendored eDEX-UI — an Electron app, so a full
Chromium — **twice**: `Netwerkplek/eDEXUI/x64` at 162 MB and
`Netwerkplek/eDEXUI/x86` at 148 MB, with `icudtl.dat` **byte-identical across both**
(10,218,000 bytes, md5 `8cda0911…`) and never deduplicated. That is in a repository
whose `.git` is **931 MB** for **38 tracked `.vb` files**.

The history is the finding: `git log -- Netwerkplek/eDEXUI` returns **exactly six
commits**, 2019-01-27 through 2019-04-28 — **91 days** — and then nothing, ever.
Meanwhile the application itself was maintained until **2024-07-07** (the last
substantive commit; the only later one is a 2026 `.gitattributes` housekeeping
change). **That is 1,897 days — five years two months — of an unpatched 2019-era
Chromium in production**, across a period in which the surrounding VB was edited
freely. Nothing failed. Nothing warned. The vendored tree simply stopped being
something anyone thought about, which is the entire mechanism: **a bundled runtime
does not decay visibly, so nothing ever prompts the update.** Re-establish with
`git log --format="%ad" --date=short -- Netwerkplek/eDEXUI` and `du -sh` on the two
architecture directories.

**A vendored binary can outlive its distributor, leaving the recorded build ID as
the only identification.** `C:\Source\ExoFabric\Mill` commits ffmpeg at ~63 MB per
executable — `ffmpeg.exe` 65,870,336 b, `ffplay.exe` 65,759,232 b, `ffprobe.exe`
65,784,832 b — for a **33-`.cs`** project totalling **204,715,911 tracked bytes**.
Its vendored `Tools/FFMpeg/README.txt` records
`Build: ffmpeg-20190704-43e0ddd-win64-static` from Zeranoe, committed **2019-11-12**.
**The Zeranoe build service shut down in September 2020.** Recording the exact build
was the right call and is why the binary is identifiable at all — but the build is
frozen at 2019 with **no update path from the source it came from**, so replacing it
means re-sourcing from a different distributor and re-establishing provenance from
scratch. Re-establish by reading that `README.txt` and
`git log -- Tools/FFMpeg`. *(The Zeranoe shutdown date is a citation carried
forward, not something re-checked here.)*

**Why both belong in this article rather than in the charter:** they are the
measured cost of the alternative the charter rejected. `winldd` shipping with no
license file is a licensing gap someone can close in an afternoon; a 2019 Chromium
still running in 2024 is not a gap anyone can close, because nothing in the system
was ever going to raise it.

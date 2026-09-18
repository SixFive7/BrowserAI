<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Dependencies: provenance, cost and payload licensing

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · `ModelContextProtocol` 2.2.0 · Velopack 1.2.0 · Node v24.19.0 LTS · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · ~~`chromium-headless-shell` 1237~~ · `ffmpeg` 1011 · `winldd` 1007.
⚠️ **`chromium-headless-shell` was never in force and this line said it was** — *corrected 2026-09-18 by addition, the header half of [the struck row below](#third-party-payload-as-shipped)*. `BrowserProvisioner` has passed `--no-shell` since `a45749b` on 2026-08-16, **two days after these versions were read**, so no install has ever held that tree and no entry here can be about one. **Nothing is re-measured to say this**: the row below already records the browsers root holding no `chromium_headless_shell-*` directory at any revision, and this sentence only stops a reader meeting the number in the header and taking it for a component. The rest of the line is left exactly as written — it is what was in force on the day, and every entry that has moved since carries its own stamp.
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

**A correctly stamped version comment went stale in three weeks.** A sibling MCP
server of the author's pins `ModelContextProtocol` 1.4.1 with a csproj comment
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

**Re-read 2026-09-17** @ `@playwright/mcp` 0.0.81 · `playwright-core`
1.64.0-alpha-2026-09-17 · `playwright` 1.64.0-alpha-2026-09-14 · Node v24.21.0 ·
full `chromium` 1245 (154.0.8037.0) · `firefox` 1548 (155.0) · `ffmpeg` 1011 ·
`winldd` 1007 — by reading the `payload\mcp` and `payload\node` trees out of a
real publish and the provisioned trees out of the browsers root, at full depth
and file by file. `[FLOATS]` — every row moves when its component does.
*Corrected 2026-09-17 (previously "Verified **2026-08-14** against the versions
in the payload table … by reading the shipped trees and binaries")* — **that pass
was real and four of its rows are unmoved. What it got wrong is one cell that
covered two packages and was false of the second, one tree that stopped being
provisioned two days after it was read, and two components it never listed at
all.**

> ⚠️ **A search that comes back empty needs a positive control, and this one has
> two.** The same full-depth, case-insensitive sweep for `*licen*`, `*notice*`,
> `*copying*`, `*credit*`, `*legal*`, `*about*` and `*copyright*` that reports
> **nothing** under `firefox-1548` and `winldd-1007` returns
> `ffmpeg-1011/COPYING.LGPLv2.1` and `chromium-1245/chrome-win64/ABOUT` out of
> the same run — so it can find a licence when one is there. The two empty trees
> were then enumerated file by file as well, 61 files and 3, because an
> exhaustive listing is a stronger claim than a search that matched nothing.

> ⚠️ **"As shipped" means two different things in this table, and the difference
> is the whole of this project's licensing position.** The first seven rows are
> what travels inside the artifact; the last five are **downloaded to the user's
> machine on first run, and no copy of any of them ships** —
> `THIRD-PARTY-NOTICES.txt` says exactly that, in those words, and it is
> [the reason for first-run provisioning](../../DECISIONS.md) rather than a side
> benefit of it. A licence gap in the second group is a gap in what a *download*
> carries; one in the first group is a gap in what BrowserAI hands over.

| Component | Terms as shipped | What is in the tree |
|---|---|---|
| `@playwright/mcp` **0.0.81** | Apache-2.0 | `LICENSE` **11,552 B**, and **no `NOTICE` of its own** — which is true of this package and was never true of the other two. *Corrected 2026-09-17 @ `@playwright/mcp` 0.0.81 · `playwright-core` 1.64.0-alpha-2026-09-17 (previously "`@playwright/mcp`, `playwright-core` 0.0.79 | Apache-2.0 | The vendored `node_modules` tree carries the package `LICENSE`. **No `NOTICE` file is published upstream**, so §4(d) has nothing to propagate")* — **one cell covered two packages and the NOTICE half was false of the second.** It is three rows now because three Playwright packages ship |
| `playwright-core` **1.64.0-alpha-2026-09-17** | Apache-2.0 | `LICENSE` 11,601 B, **`NOTICE` 254 B**, `ThirdPartyNotices.txt` 676 B, **and three per-bundle sidecars** that the notices file points at rather than lists: `lib/serverRegistry.js.LICENSE` 2,982 B, `lib/utilsBundle.js.LICENSE` **106,591 B** and `lib/webp_codec.LICENSE` 8,841 B. **So §4(d) does bite, and the artifact has always satisfied it** — `THIRD-PARTY-NOTICES.txt` names `payload\mcp\node_modules\playwright-core\NOTICE` and has since `f18b657`, which is why this correction changes a sentence in the knowledge base and nothing about what ships |
| `playwright` **1.64.0-alpha-2026-09-14** | Apache-2.0 | **A third Playwright package ships and this table never listed it.** It is `@playwright/mcp`'s other exact dependency and has been in `build/payload/package-lock.json` since the first payload build (`9410876`). `LICENSE` 11,601 B and `NOTICE` 254 B, both **byte-identical to `playwright-core`'s** (SHA-256 `45873d00…` and `6d602191…`), `ThirdPartyNotices.txt` 705 B, and three sidecars: `lib/matchers/expect.js.LICENSE` 36,528 B, `lib/transform/babelBundle.js.LICENSE` **123,004 B** and `lib/transform/esmLoader.js.LICENSE` 17,406 B. ⚠️ **`THIRD-PARTY-NOTICES.txt` says "the two Playwright packages" and three ship.** Because the terms and the NOTICE text are identical to `playwright-core`'s, what is missing is a **name** rather than a licence — **reported and not fixed**: what ships beside the binary is the maintainer's call, not a sweep's. ✅ **FIXED 2026-09-18 (Q214 = a), and the call was taken rather than assumed**: the notices name `playwright` in the table with its three paths, say *"3 Playwright packages ship in the payload"*, and state the resolved versions of `playwright` and `playwright-core` because an override separated them. **The list is no longer typed**: `ThirdPartyNoticeTests.TheNoticesNameEveryPackageThatShipsAndEveryFamilyThatIsProvisioned` enumerates `build/payload/package-lock.json` and fails on a package the notices do not name, so the state this row found cannot recur. It was watched red on exactly that, naming the file's own gap: *"the payload ships 'playwright' and THIRD-PARTY-NOTICES.txt does not name it"* |
| `ModelContextProtocol`, `ModelContextProtocol.Core` 2.2.0 | Apache-2.0 | **Corrected 2026-08-16 (previously "Mid-transition from MIT; unrelicensed contributions remain MIT")** — that describes the transition and not what is in the tree. Nothing is in the tree: both are compiled into `BrowserAI.exe` and a NuGet package's licence stays in the machine's package cache. Upstream's `LICENSE` is **12,227 bytes and grants three licences** — Apache-2.0, MIT for contributions never relicensed (`Copyright (c) 2024-2026 Model Context Protocol a Series of LF Projects, LLC.`), and CC-BY-4.0 for documentation — and its Apache half carries §1–9, ending at *END OF TERMS AND CONDITIONS* with **no appendix**, although its own §4 says *"an example is provided in the Appendix below"*. It is reproduced whole in `THIRD-PARTY-NOTICES.txt`. Re-establish: read `<pkg-cache>\modelcontextprotocol\<v>\modelcontextprotocol.nuspec` for the `repository commit`, then fetch `LICENSE` from that commit |
| `Microsoft.Extensions.*` — **17 packages** | MIT | Measured 2026-08-16 by reading every `.nuspec` in the resolved closure of `src/BrowserAI/packages.lock.json`. All 17 carry `<license type="expression">MIT</license>` and the copyright field `© Microsoft Corporation. All rights reserved.`; two are direct references (`Logging`, `Logging.Console`) and 15 transitive. **Two source repositories and two different licence copyright lines**: 16 from `dotnet/dotnet` (`Copyright (c) .NET Foundation and Contributors`; commits `e2f47b01…` and `f7d90799…` carry byte-identical `LICENSE.TXT`) and `Microsoft.Extensions.AI.Abstractions` 10.8.3 from `dotnet/extensions` (`Copyright (c) .NET Foundation. All rights reserved.`). Nothing is in the tree; all of it is reproduced in `THIRD-PARTY-NOTICES.txt`, and the package list there is derived from the lock file by `ThirdPartyNoticeTests` so a new arrival is a red build |
| Velopack 1.2.0 | MIT | Notice only |
| Node.js v24 | MIT **plus aggregate terms** for OpenSSL, ICU, V8, zlib and c-ares | Shipping "a single `node.exe`, nothing else" drops Node's `LICENSE`, which is not optional. **The file is not downloadable beside `node.exe`** — measured 2026-08-16, `dist/v24.19.0/win-x64/` publishes only `node.exe`, `node.lib` and the two `node_pdb` archives, so the licence has to come out of `node-v24.19.0-win-x64.zip` ([kb](../playwright/provisioning-and-timings.md#component-sizes)). `build/Build-Payload.ps1` extracts both entries from the verified archive and fails if either is missing . **Verified 2026-09-17 @ v24.21.0**: the published payload carries `payload\node\LICENSE` at **160,555 B** beside a `node.exe` whose version resource reads ProductVersion **24.21.0**, so the extraction still happens and the file still lands. The 2026-08-16 reading of what `dist/` itself publishes was **not** re-taken and keeps its own date |
| ~~`chromium-headless-shell` 1237~~ | — | **Corrected 2026-09-17: this tree is never provisioned, so there is nothing in it to read.** *(previously "`LICENSE.headless_shell` plus a **40,178-line** credits file. Binary is unbranded", which was true of a tree — just not of one any install has.)* `BrowserProvisioner` passes **`--no-shell`** to upstream's own `install-browser` and has since `a45749b` on 2026-08-16, **two days after this row was read**; the browsers root confirms it. On 2026-09-17 it held `chromium-1244`, `chromium-1245`, `ffmpeg-1011`, `firefox-1544`, `firefox-1548` and `winldd-1007`, and **no `chromium_headless_shell-*` directory at any revision**. The row is struck rather than deleted because [the ~806 MB bundled total](../playwright/provisioning-and-timings.md#component-sizes) still counts its 268.49 MB, and already says so |
| `ffmpeg` 1011 | LGPL-2.1 | `COPYING.LGPLv2.1` already ships in the directory. Spawned by `playwright-core` as an unmodified separate executable, so §6's relink requirement does not bite . **Verified 2026-09-17, unmoved at revision 1011**: the directory holds **4 files**, and `COPYING.LGPLv2.1` is **26,526 B** of them |
| `winldd` 1007 | **no license file shipped at all** | Nothing in the tree to ship. **Verified 2026-09-17, unmoved at revision 1007**, and the tree is small enough to state exhaustively rather than search: **3 files** — `DEPENDENCIES_VALIDATED`, `INSTALLATION_COMPLETE` and `PrintDeps.exe` |
| full `chromium` **1245** (154.0.8037.0) | **Google-branded, no OSS license file anywhere in the tree** | `chrome.exe` reports CompanyName **"Google LLC"** and **"Copyright 2026 Google LLC. All rights reserved."**; its `ABOUT` points at Google's Chrome Terms of Service **Verified 2026-09-17 at revision 1245, and the reading could not have moved because the tree did not:** `chromium-1245` and `chromium-1244` hold the **same 308 files at the same 308 sizes**, and `chrome.exe` is SHA-256 `e3390ab4…` in both. That is the licensing half of [row 21](../re-verification.md)'s finding — `playwright-core` builds the URL with `cftUrl()`, keyed on `browserVersion`, and 154.0.8037.0 did not move with the revision, so 1245 **is** 1244's archive. **It was still run rather than reasoned:** the only licence-adjacent file among the 308 is `ABOUT` at **257 B**, SHA-256 `122b32f3…` identical across both revisions, and `chrome.exe`'s version resource reads CompanyName **Google LLC**, ProductName **Google Chrome for Testing**, FileVersion 154.0.8037.0 and LegalCopyright *"Copyright 2026 Google LLC. All rights reserved."* |
| `firefox` **1548** (155.0) | **MPL-2.0 headline, with Apache and BSD terms besides** | **A second browser family is provisioned and this table never listed it** — Firefox has shipped as a family since 2026-08-19, and this row is its first licensing read, taken 2026-09-17. ✅ **AND `THIRD-PARTY-NOTICES.txt` NOW LISTS IT, 2026-09-18 (Q214 = a)**: a *Browsers provisioned on first run* block names each family, what it is, where it is fetched to, and where its own terms are — for Firefox, inside `omni.ja` as `license.html`, which is what `about:license` renders. That block is checked against `ProvisionedBrowsers.Families` rather than typed, so a third family is a red build; it was watched red on *"'firefox' is a provisioned family and THIRD-PARTY-NOTICES.txt has no entry for it"* and on the same sentence for `chromium`, which was not listed that way either. **There is no standalone licence file anywhere in the tree**: 61 files, enumerated rather than searched. **The terms are in the tree, inside `omni.ja`** — `chrome/toolkit/content/global/license.html` at **320,171 B** inside `firefox/omni.ja`, and `chrome/browser/content/browser/license.html` at **320,613 B** inside `firefox/browser/omni.ja`, which is what `about:license` renders. It opens *"most of the source code is available under the Mozilla Public License 2.0 (MPL)"* and carries 6 further `Apache License` and 13 `BSD` mentions. **Both are byte-identical across 1544 and 1548** (SHA-256 `91ecd8f1…` and `9307479a…`), so the revision move cannot have changed the reading. ⚠️ **The entire 1544 → 1548 move is five entries inside `firefox/omni.ja`** — 2,544 entries, **0 added, 0 removed, 5 changed**, and four of the five are Playwright's own `chrome/juggler/` protocol files (`NetworkObserver.js`, `Runtime.js`, `PageHandler.js`, `Protocol.js`); the fifth, `modules/AppConstants.sys.mjs`, is the same size it was. That is the whole of the **+902 B** [row 21](../re-verification.md) measured on disk — so *Firefox* did not move on this roll, Playwright's instrumentation of it did |

**The only on-point public statement on redistributing Chrome for Testing is
adverse.** A Google engineer, 2023: *"Chrome for Testing is a flavor of Google
Chrome, so google.com/chrome/terms applies"* — which forbids redistribution. This
is a citation, not a measurement, and it is not legal advice; it is recorded
because it is the single piece of evidence the provisioning decision rests on.

### What vendoring a runtime actually costs — two long-lived cases

Measured **2026-08-16** by reading two unpublished repositories and their git
history. `[MACHINE]` throughout — true of two repositories, not of the world, and
**not reproducible from here**. What makes them worth keeping is that the route
to re-establish them is generic: anyone can run it against any repository that
vendors a runtime, including their own. The charter's provision-don't-bundle
position argues from **licensing alone**; this is the empirical half, and it is
the evidence behind the claim that a bundled browser makes CVE response a release
obligation.

**Vendoring a runtime fails by silence, not by breakage.** One repository vendored
an Electron desktop application — so a full Chromium — **twice**, once per
architecture, at **162 MB** and **148 MB**, with `icudtl.dat` **byte-identical
across both** (10,218,000 bytes) and never deduplicated. That is in a repository
whose `.git` is **931 MB** for **38 tracked source files**.

The history is the finding: `git log` over the vendored directory returns
**exactly six commits**, 2019-01-27 through 2019-04-28 — **91 days** — and then
nothing, ever.
Meanwhile the application itself was maintained until **2024-07-07** (the last
substantive commit; the only later one is a 2026 `.gitattributes` housekeeping
change). **That is 1,897 days — five years two months — of an unpatched 2019-era
Chromium in production**, across a period in which the surrounding VB was edited
freely. Nothing failed. Nothing warned. The vendored tree simply stopped being
something anyone thought about, which is the entire mechanism: **a bundled runtime
does not decay visibly, so nothing ever prompts the update.** **Run this on
anything, including your own tree:** `git log --format="%ad" --date=short --
<vendored dir>` beside the same command over the source directory, and read the
gap between the two last dates. That gap is the number this entry is about.

**A vendored binary can outlive its distributor, leaving the recorded build ID as
the only identification.** A second repository commits ffmpeg at ~63 MB per
executable — 65,870,336 b, 65,759,232 b and 65,784,832 b for the three tools —
inside a **33-source-file** project totalling **204,715,911 tracked bytes**. Its
vendored `README.txt` records `Build: ffmpeg-20190704-43e0ddd-win64-static` from
Zeranoe, committed **2019-11-12**. **The Zeranoe build service shut down in
September 2020.** Recording the exact build was the right call and is why the
binary is identifiable at all — but it is frozen at 2019 with **no update path
from the source it came from**, so replacing it means re-sourcing from a different
distributor and re-establishing provenance from scratch. **The transferable
check** is to take any vendored binary's recorded build ID and try to fetch that
exact build today; if you cannot, the recorded ID is an epitaph rather than a
provenance. *(The Zeranoe shutdown date is a citation carried forward, not
something re-checked here.)*

**Why both belong in this article rather than in the charter:** they are the
measured cost of the alternative the charter rejected. `winldd` shipping with no
license file is a licensing gap someone can close in an afternoon; a 2019 Chromium
still running in 2024 is not a gap anyone can close, because nothing in the system
was ever going to raise it.

**Kept rather than cut, and the reasoning is worth stating**, because both rest
entirely on repositories a reader cannot open. They survive because the claim is a
measurement — git-log spans, byte counts, a shutdown date — rather than an
impression; because the route to re-establish it is generic and stated; and
because the conclusion is the strongest single argument for this project's
provisioning design. What was cut is everything that served only to identify the
repositories.

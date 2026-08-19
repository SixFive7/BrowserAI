<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# BrowserAI

A self-contained, system-installed MCP server that fronts a **pinned** `@playwright/mcp` runtime and exposes browser automation to AI agents through a small, opinionated, centrally-updatable surface.

BrowserAI is a **proxy**. It ships the runtime, owns the lifecycle, and rewrites the tool surface. It does **not** reimplement Playwright, and it does not reimplement Playwright's MCP tool layer. That boundary is the single most important design constraint in this project and is spelled out in [Scope](#scope-proxy-not-implementation) below.

**Windows only, and there is nothing to install alongside it.** No Node, no .NET and no Chrome on the host: the installer carries a NativeAOT single-file binary, `node.exe` and the vendored `@playwright/mcp` tree, and provisions its own Chromium on first run. One MCP registration, at user scope, available in every repository — no `.mcp.json`, no hooks, no per-repository files.

Why it exists, and every settled decision with the argument that settled it, is in [`DECISIONS.md`](DECISIONS.md).

---

## Install

1. Download **`BrowserAI-win-Setup.exe`** from [the latest release](https://github.com/SixFive7/BrowserAI/releases/latest) and run it. It installs **per user** into `%LocalAppData%\BrowserAI` and needs no elevation.
2. That is the whole installation. The installer's own hook registers BrowserAI with Claude Code by running the client's supported command, `claude mcp add --scope user`, so it is available in every repository on the machine. The uninstaller removes the registration again.
3. Restart the client so it picks up the new server.

**If registration did not happen** — the client was not on `PATH`, or it is not Claude Code — BrowserAI writes `mcp-registration.json` beside the install root carrying the exact command to run by hand. It is this:

```
claude mcp add browserai --scope user -- "<install root>\current\BrowserAI.exe"
```

Registration is never allowed to fail an install, and never allowed to fail silently: every outcome writes a log record *and* that file.

A **`BrowserAI-win-Portable.zip`** is published beside the installer, by the same packaging run, for anyone who would rather unpack than install. There is no installer in it to run the registration hook, so registering it is the command above against wherever it was unpacked.

**Updates are automatic and there is one track.** No beta channel. BrowserAI checks its own feed and applies an update only when no other instance is live.

**The first session downloads a browser.** Chromium is provisioned once per machine, not once per update — measured at 203.8 MB down, 430.48 MiB on disk and about 12.6 s. Nothing is downloaded at spawn after that, and nothing resolves from a registry at runtime: the client runs exactly the bytes the build froze into the artifact.

---

## Using it

BrowserAI presents **upstream's `browser_*` tools under upstream's own names, byte-for-byte**, plus six tools of its own. A **session is a directory**, and that is the only handle there is: every upstream tool has a required **`session`** parameter injected into its schema, carrying the absolute path of the session the call belongs to. A call that names no session is refused rather than reaching a browser.

**Six authored tools**, all prefixed `browserai_`:

| Tool | What it does |
|---|---|
| `browserai_init` | Creates a session. `directory`, `purpose` and `mode` are all required — no defaults, no fallback, and an empty, relative or unusable path is refused rather than turned into one that happens to work |
| `browserai_resume` | Takes over a directory that is already a session, and replays what it was. Mode and browser are **not** arguments; they were bound at `init` and are read back out of the session's own record |
| `browserai_list` | Every session beneath a directory you name — mode, browser, purpose, created and last-used stamps, and size on disk. There is no unscoped form: breadth is stated rather than assumed |
| `browserai_destroy` | Closes the browser and deletes the whole directory. Refuses anything that does not hold a valid session record, which is what stops it being aimed at `Documents` |
| `browserai_set_purpose` | Rewrites what a session is for. The previous purpose is kept in the session's history rather than lost |
| `browserai_reinstall_browser` | Deletes and re-provisions **one** browser tree. `browser` is required and has no default — with two families on disk, a defaulted one would re-download a healthy tree and report success while the broken one stayed broken. It **refuses** — naming what is running — while any session has a browser of *that* family open. There is deliberately no force option. A third value, **`shared`**, rebuilds `ffmpeg` and `winldd`: both families download them into one root and neither family's reinstall touches them, so a corrupted `ffmpeg` — which recording video needs — was otherwise unrepairable from here. `shared` refuses while **any** session is open, of either family, because a browser starts the codec only at the moment it records |

**Three modes**, bound at `init` and recorded in the session's `lock.json`. A mode is two switches on the browser BrowserAI launches for that session — whether a window appears, and whether the profile keeps cookies and logins between runs:

| Mode | Window | Stored credentials |
|---|---|---|
| `headless` | No | No — the workhorse |
| `interactive` | Yes | No — a human may type a password the agent must never capture |
| `persistent` | Yes | Yes — logged-in agent work |

A session without stored credentials has its child launched **without upstream's `storage` capability**, so the 17 cookie, `localStorage` and `storageState` tools do not exist in that process at all.

`tracing` is a boolean on any of the three, not a mode of its own. Headless-with-storage is deliberately not offered.

⚠️ **Corrected 2026-08-18 (previously "bound at `init`, recorded in the session's `lock.json`, and *enforced server-side on every call*").** There was a `(tool, mode)` permission policy behind that phrase — five tool classes, deny-by-default in both directions, plus a guard that refused a `browser_get_config` answer carrying `"secrets"` — and it has been **removed**. It was described as a security boundary and was never one **against the caller**: the calling agent chooses the session directory, the profile and its cookie database are created inside it, and the agent runs as the same Windows user, so DPAPI decrypts for it. Any file tool the agent holds reads what the policy declined to return. Prompt injection is real and is not solved at this layer. **That sentence was measured on 2026-08-18, after the removal rather than before it** — a second process as the same user recovered a cookie from a session BrowserAI configured with `CryptUnprotectData` and AES-GCM alone, and App-Bound Encryption is not in force for the provisioned Chromium ([kb](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18)).

Change control moved to [the release gate](TESTING.md#the-upstream-review-gate), which covers more: four golden snapshots — including `tools-list.json` with every tool's `inputSchema` — are diffed against the resolved payload on every build, and `upstream-review.json` holds a release until a human adjudicates what moved.

**One tool is withheld and one argument is mandatory; neither is a permission.** `session` is mandatory, because that is *routing*. And `browser_annotate` is **not in `tools/list` at all** — filtering the surface is in scope where renaming is not — because it blocks with no self-timeout until a human draws, and its window belongs to a second, non-headless browser under a daemon that writes into `%TEMP%` and outlives the session. A caller that names it anyway is refused rather than forwarded. *Corrected 2026-08-18 (previously "Two refusals survive … `browser_annotate` is refused on a mode that opens no window").* The measurement is in [kb](kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18); what it would take to bring the tool back is in [DECISIONS](DECISIONS.md#licence-release-policy-and-the-tool-surface).

**The session directory is the identity.** One directory holds `lock.json` at its root and `profile/`, `output/` and `downloads/` beneath it. There is no handle to keep, no token to store and no expiry: a session stays resumable against its recorded directory for as long as the directory exists, and a resume costs about 515 ms and loses only `sessionStorage`. Artifacts are routed into typed subfolders on the way in, and every result carries the resolved absolute path.

**Two browser families**, `chromium` and `firefox`, bound at `init` alongside the mode and equally permanent: a profile belongs to the browser that made it, so `resume` reads the family back out of the record and refuses to be told a different one. `chromium` is the default. Each is downloaded once per machine on first use — **chromium 203.8 MB, firefox 127.2 MB**, both measured from the CDN's own `content-length` ([kb](kb/playwright/provisioning-and-timings.md#first-run-provisioning)) — and the first browser call on a family that is still downloading is refused with the size rather than blocked. *Corrected 2026-08-19 (previously "**Chromium only, today.** `browserai_init` refuses `browser: "firefox"`").* What that sentence was waiting for was a measured Firefox download size and a decision about what `browserai_reinstall_browser` reinstalls when there are two trees; both are done.

**The advertised tool surface does not depend on which family a session runs.** Measured 2026-08-19 against real children of the resolved payload at both capability sets: 42 tools without `storage` and 59 with it, identical names, identical order, identical schemas under `chromium` and `firefox` ([kb](kb/playwright/tools-and-artifacts.md#does-the-surface-differ-by-browser-family--measured-2026-08-19)). Every tool-surface number in this repository is therefore a claim about both.

**Any path is accepted, deliberately.** BrowserAI does not constrain a session directory to a sanctioned root, so an agent may point one at a real browser profile and read live browser state. Correct use is the calling agent's responsibility — BrowserAI logs the resolved absolute paths rather than enforcing a boundary. The reasoning is in [`DECISIONS.md`](DECISIONS.md#shape-and-packaging).

---

## Scope: proxy, not implementation

**BrowserAI spawns `@playwright/mcp` as a child process and forwards JSON-RPC to it.**

This is a hard boundary. It exists because the temptation to cross it is real and will present itself as a reasonable next step.

### In scope

- Spawning and supervising a pinned `@playwright/mcp` child over stdio
- Forwarding `tools/call` to that child and returning its response verbatim
- Fetching `tools/list` **from the child at runtime** and rewriting it — filtering, re-describing, adding parameters. **Not renaming:** upstream names pass through byte-for-byte ([Tool naming](DECISIONS.md#licence-release-policy-and-the-tool-surface))
- Generating the child's `--config` JSON at launch time from BrowserAI's own session state
- Everything *around* the protocol: locking, lifecycle, directories, artifacts, diagnostics, updates

### Out of scope — explicitly forbidden

- **Driving Playwright directly** (via `Microsoft.Playwright` / Playwright for .NET or any other binding)
- **Hand-writing tool schemas in C#.** Every schema must originate from the child's `tools/list` response. If a tool definition is being typed into a `.cs` file, the boundary has been crossed.
- **Reimplementing the snapshot/ref system**, the accessibility-tree serialization, response formatting, or error shaping

### Why the boundary sits exactly there

`@playwright/mcp` 0.0.79 is a **20-line shim**. The entire package is `cli.js`, `index.js`, and type definitions:

```js
// node_modules/@playwright/mcp/index.js
const { tools } = require('playwright-core/lib/coreBundle');
module.exports = { createConnection: tools.createConnection };
```

The implementation lives in `playwright-core/lib/coreBundle.js` — 3.4 MB, esbuild-bundled, containing a 78-entry tool array. Note the numbers, because a golden test written against the wrong one fails on day one: **78 is the internal registry; 69 is the maximum ever exposed over MCP** (9 are `skillOnly` and always stripped), and **24 is the default** with no `capabilities` set — all three in [kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape), which also records that the per-capability breakdown was never counted. Its value is not browser control; Playwright for .NET does browser control perfectly well. Its value is the **ref-based accessibility snapshot system**, the response formatting, and the error handling — the layer that turns a browser into something a language model can operate. That layer is large, subtle, actively developed upstream, and would drift permanently the day it is forked.

"We don't want to reimplement Playwright" is the easy half of this rule. The half that matters: **reimplementing the MCP tool layer is also reimplementation**, even though it never touches a browser API.

### The one sanctioned exception, if it is ever needed

`playwright-core` explicitly whitelists `"./lib/coreBundle"` in its `exports` map, so `require('playwright-core/lib/coreBundle')` is a supported import, not a blocked deep path. It exposes `browserTools` (a flat array of plain, inert objects), `filteredTools`, `createConnection`, and `BrowserBackend`. `defineTool` is literally the identity function — there is no class, no registry, no side effect ([kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)).

This means in-process tool manipulation is *available* if the proxy approach ever proves insufficient. It is **not** the plan, it carries no type definitions and no semver guarantee, and taking it requires pinning `@playwright/mcp` and `playwright-core` together and re-verifying the tool array on every bump. Documented here so it is a considered decision rather than a discovery.

---

## Where things are written down

| File | Holds | Changes when |
|---|---|---|
| **`README.md`** (this file) | What BrowserAI **is**: what it does, how to install it, how to use it, the scope boundary, the licence | The product's surface moves |
| **[`DECISIONS.md`](DECISIONS.md)** | What we **decided**, and why — the founding argument, the trade-offs taken, every settled decision with the reasoning attached | We change our minds |
| **[`ARCHITECTURE.md`](ARCHITECTURE.md)** | How the product is **put together**: each area, what it guarantees, and the code that implements it | The code moves |
| **[`HAZARDS.md`](HAZARDS.md)** | Every known failure mode, in one checkable list, with the evidence that closed each one. Maintained by addition; rows are never deleted | A new failure mode is found, or an open one is closed |
| **[`RELEASING.md`](RELEASING.md)** | The checklist a release must pass, and [the release gate](RELEASING.md#the-release-gate) it enforces. The only gate that exists | An item proves unevidenceable, or the gate moves into automation |
| **[`TESTING.md`](TESTING.md)** | Why the suite is the only thing between a floating dependency and a shipped regression: the five layers, [what the build itself must fail on](TESTING.md#what-the-build-itself-must-fail-on), [why the upstream-review gate is the suite and not a hook](TESTING.md#the-upstream-review-gate), and why the harness is ours | The suite's shape changes, or a gate moves |
| **[`STACK.md`](STACK.md)** | Which component was chosen and why, the MSVC prerequisite, where the version comes from, [the build configuration](STACK.md#the-build-configuration), and [the nine SDK deviations](STACK.md#nine-places-where-the-sdk-must-be-deviated-from) | A component is replaced, or a deviation stops being needed |
| **[`kb/`](kb/README.md)** | What we **measured** — about Chromium, Firefox, Playwright, Node and Windows, one article per topic, with provenance and a re-verification hook | Upstream ships, and a re-measurement says something different |
| **[`TODO.md`](TODO.md)** | Work settled in intent but not yet done | Something gets decided, or gets done |
| **[`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md)** | The procedure for adopting a new upstream version | The procedure proves insufficient |

**Different half-lives, which is the whole reason for the split.** A decision stays true until we revisit it. An architecture stays true until the code moves. A measurement stays true until upstream ships, which is a clock nobody here controls. Mixing them means the whole document reads as equally settled, and the parts with the shortest half-life are exactly the ones that quietly stop being true. When a document here states a measured fact, it is a summary — the article under [`kb/`](kb/README.md) carries the number, the date, the versions it held under, and how to re-establish it.

> **There was also an implementation plan**, one file per section under `plan/`, plus a `TODO.md` full of closed items and a log of the first release run. All three were **consumed**: a plan section was spent the day the code it described existed, and the whole set was deleted on 2026-08-17 once every section was built and audited. What survived it is the documents above that were never part of it, plus everything the sections had put into `kb/` and into the doc comments as they went. `git log` is the record of the build itself.

---

## Status

**Shipped. `v1.0.0`.**

493 executed test cases, 0 failed, 0 skipped — measured from a full `dotnet test` run on 2026-08-19 (*previously "491"*, and "478", "476", "461", "458", "436" and "419" before that; re-measured each time rather than adjusted). **The +2 are the probe-before-gate race**: one plants a peer's probe handle by hand and drives the product's post-write re-open against it, both the arm that clears and the arm that does not; the other holds *which* opens may wait such a handle out, which is the half no interleaving can be raced for reliably. **Of the +13, only 7 are new tests: the published figure was stale by 6**, because the tree already held 484 at `cc45900` and this sentence was last stamped at 478 — drift again, in a number nothing asserts, which is why the paragraph below says so twice. **Six of the seven are one class run twice**: `SessionDirectoryGuardTests` is now parameterised over `DriveLetterCase`, so every path those six arms compose is spelled `C:\…` **and** `c:\…` on every run — the mechanism against an assertion comparing a composed path against one Windows re-spelled, which was red from Git Bash and green from PowerShell on the same commit and which CI, running `pwsh` end to end, cannot see at all. The seventh is `PayloadTests`', asserting that upstream still serialises every install on one lock over the browsers root, because a corrected remark now rests on it. **The +2 before that, and both are arms a fast machine never reached** — one provokes `browserai_destroy`'s survivor answer deterministically, with a file the test holds open rather than a browser that has not let go; the other provokes a durable write that landed and could not be re-opened, with an ACL denying the one right the re-open needs and the write does not. The +15 before it was `FirefoxSessionTests`, six arms covering Firefox as a family a caller may ask for, one of which drives a real Firefox through the front door end to end, plus 9 that arrived with the boundary refusals on 2026-08-19 and did not re-stamp this sentence — which is what a number nothing asserts looks like when it drifts. **The predicate is executed cases, not `[Test]` methods**: every `[Arguments]` and `[MethodDataSource]` expansion counts as one, which is why no test in the suite asserts this number — reflecting over attributed methods would answer a different question, and a count that reads like a measurement and is not is the thing this repository spends the most effort avoiding. The publish emits a NativeAOT binary with ILC reporting nothing, and the update feed resolves over real HTTP with a manifest whose SHA-256 matches the package byte for byte.

*Corrected 2026-08-17 (previously "Design phase. Nothing is built."). That sentence outlived the design phase by a full build and was the first thing a reader of the public repository met. It is recorded rather than quietly replaced because the failure is worth keeping: a status line is written once, at the moment it is true, and nothing anywhere goes red when it stops being true.*

What is still owed is in [`TODO.md`](TODO.md); what is still undecided is under [Still open](DECISIONS.md#still-open).

---

## License

BrowserAI is **source-available** under a **bespoke variant of the Functional Source License 1.1 (MIT Future License)**, modified so the Change Date is the **fifth** anniversary of each release rather than the canonical second. On that date the release additionally becomes available under the **MIT License**. In spirit: read it, run it, modify it, deploy it inside your organisation — but do not ship a commercial product or service that competes with it, for five years, after which it becomes MIT.

This is **not** the canonical FSL and must not be referred to by, or distributed under, the SPDX identifier `FSL-1.1-MIT`. Where an SPDX expression is required, use `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr`. The authoritative terms are in [`LICENSE`](LICENSE) and prevail over this summary.

Copyright 2026 Jori Huisman.

**Source files carry the two-line SPDX header** — `SPDX-FileCopyrightText` plus `SPDX-License-Identifier`, always in the `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr` form, as [`CLAUDE.md`](CLAUDE.md) requires and as every article under [`kb/`](kb/README.md), `CLAUDE.md` and `UPSTREAM-REVIEW.md` already do.

The licence does not demand it — [`LICENSE`](LICENSE) is the notice and shipping it satisfies the Redistribution clause — so this is a house rule, kept for a different reason: **a file that names its own licence cannot be copied out of the repository and quietly become unlicensed**, which is exactly what happened to the launcher this project replaces, thirteen times.

Formats with no comment syntax carry it as data where they can ([`upstream-review.json`](upstream-review.json) has a `_license` key) and not at all where they cannot. Vendored third-party files keep their upstream headers instead, which Apache-2.0 §4 requires.

### Third-party components

The license above covers **BrowserAI's own code and its documentation**. It does not cover the bundled payload, which keeps its own terms. Shipping that payload creates obligations that attach at first installer handoff, independent of BrowserAI's own license. Verified 2026-08-14 against the versions pinned in [the runtime](ARCHITECTURE.md#the-runtime-it-ships); what is actually present in each shipped tree is recorded in [kb: payload licensing](kb/packaging/dependencies.md#third-party-payload-as-shipped):

**Two columns of this table used to be one, and merging them was the error.** What BrowserAI *redistributes* is the installer payload, and only that creates obligations for us. What the user's machine *downloads on first run* comes from Playwright's CDN, direct to that machine, and **carries no redistribution duty for us at all** — we ship no copy of it, so there is nothing to accompany with a licence. That is not a side benefit of [first-run provisioning](ARCHITECTURE.md#the-runtime-it-ships); it is the reason for it.

**What we redistribute — obligations are ours:**

| Component | Terms | Obligation on redistribution |
|---|---|---|
| `@playwright/mcp`, `playwright-core` 0.0.79 | Apache-2.0 | Keeping the vendored `node_modules` tree intact ships the package's `LICENSE` and satisfies §4. Upstream publishes no `NOTICE` file, so §4(d) has nothing to propagate. [Scope](#scope-proxy-not-implementation) forbids modification, so §4(b) is clean by construction. |
| `ModelContextProtocol`, `ModelContextProtocol.Core` 2.2.0 | Apache-2.0 | **§4(a): a copy of the licence must reach every recipient.** Compiled *into* `BrowserAI.exe`, so nothing carries it unless we do — it ships in `THIRD-PARTY-NOTICES.txt`. Upstream's own `LICENSE` grants three licences, not one: Apache-2.0, MIT for contributions whose authors have not consented to relicensing, and CC-BY-4.0 for documentation. It is reproduced whole for that reason, and because upstream's copy ends at *END OF TERMS AND CONDITIONS* and omits the appendix its own §4 refers to, which is upstream's file as published and is not completed here. Vendored fixture files keep their upstream headers. |
| `Microsoft.Extensions.*` — 17 assemblies | MIT | Notice, same as Velopack's and for the same reason. Two referenced directly (`Logging`, `Logging.Console`), the rest transitive; **the list is derived from `src/BrowserAI/packages.lock.json` by `ThirdPartyNoticeTests` rather than typed**, so a package entering the closure on a later bump is a red build. Two copyright lines, because sixteen come from `dotnet/dotnet` and `Microsoft.Extensions.AI.Abstractions` from `dotnet/extensions`. |
| Velopack 1.2.0 | MIT | Notice. |
| Node.js v24 | MIT, plus aggregate terms for OpenSSL, ICU, V8, zlib, c-ares | **Ship Node's full `LICENSE`.** "A single `node.exe`, nothing else" drops it. Not optional. |

**What the user's machine downloads — no obligation on us:**

| Component | Terms | Position |
|---|---|---|
| **full `chromium` 1237** | Google Chrome for Testing — Google-branded, no OSS license file anywhere in the tree | `chrome.exe` reports CompanyName "Google LLC" and "Copyright 2026 Google LLC. All rights reserved."; its `ABOUT` points at Google's Chrome Terms of Service, and the only on-point public statement — a Google engineer, 2023 — reads those terms as forbidding redistribution. **We do not redistribute it**, which is precisely why the provisioning decision was taken; the row that used to read *"Unresolved"* described a blocker that decision already closed. What remains open is only the *bundled-build fallback*, and it stays closed until someone gets a different answer from Google. |
| `chromium-headless-shell` 1237 | BSD-3-Clause + 40,178-line credits file | Not shipped **and not provisioned** — [full Chromium in every mode](DECISIONS.md#processes-browsers-and-session-modes). `LICENSE.headless_shell` and the credits file would have to accompany a bundled build; neither is our problem today. |
| `ffmpeg` 1011 | LGPL-2.1 | Arrives beside Chromium from the CDN. `COPYING.LGPLv2.1` ships in that directory as Playwright lays it down. Spawned as an unmodified separate executable by `playwright-core`, so §6's relink requirement does not bite and it never reaches BrowserAI's own code. A bundled build would owe version identification and an offer of corresponding source. |
| `winldd` 1007 | **no license file shipped** | Same: downloaded, never redistributed. A bundled build would have to source one from `microsoft/playwright` first — which is a reason not to bundle, not an outstanding task. |

Under a bundled build all four rows above move into the first table and their obligations become live. **Nothing in the second table blocks a release today.**

Playwright is a trademark of Microsoft Corporation. Chrome and Chromium are trademarks of Google LLC. BrowserAI is not affiliated with, endorsed by, or sponsored by either. Apache-2.0 §6 grants no trademark rights, and the inherited `browser_*` tool names surface upstream branding directly in BrowserAI's own API — ship a short disclaimer in the installed artifact.

✅ **It ships, since 2026-08-16, in `THIRD-PARTY-NOTICES.txt` beside the binary**, together with Velopack's MIT licence — two of the obligations with no upstream file of their own, and the two that the first run of [the release checklist](RELEASING.md) found absent from an otherwise releasable package. `ThirdPartyNoticeTests` asserts them against the repository file, the publish output and the packed `.nupkg`'s entry list, so an obligation added to the table above is a red build rather than a discovery at the next release.

**Corrected 2026-08-16 at the plan's final audit: four such obligations, not two (previously "the two obligations with no upstream file of their own" and "asserts all four").** The MCP SDK and the `Microsoft.Extensions.*` family are compiled into `BrowserAI.exe` on exactly Velopack's terms, and a NuGet package's licence stays in the machine's package cache — it is never copied to a publish output, so *linked in* and *its notice ships* are independent facts and the second was false for both. Apache-2.0 §4(a) is the stricter of the two clauses, not the looser, and this product is publicly distributed. The `Microsoft.Extensions.*` list is derived from `src/BrowserAI/packages.lock.json` rather than typed, so a package entering the closure on a later bump is red here rather than a licence nobody noticed had arrived.

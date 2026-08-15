<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# A. Ship and own the runtime

| Component | Requirement | Measured size |
|---|---|---|
| Node.js | Bundled — a **single `node.exe`**, nothing else. Verified driving the full MCP protocol with no npm, no `node_modules`, no `.cmd` shims. | v24.19.0 LTS, **88.53 MB** |
| `@playwright/mcp` + `playwright-core` | Resolved to latest at **build** time, then vendored into the artifact as an exact tree — never `@latest` at **spawn** time. Zero native binaries; the tree is portable JS. See [Versioning policy](../README.md#versioning-policy-everything-floats-the-build-freezes-it). | 0.0.79, **18.11 MB** |
| BrowserAI itself | NativeAOT, single file, no .NET runtime on the host. | **9.76 MiB**, measured by the [AOT spike](stack.md#nine-places-where-the-sdk-must-be-deviated-from) |
| System Chrome | **Not used.** See below. | — |
| Browsers | **Not in the installer.** [Provisioned on first run](#first-run-browser-provisioning) — the redistribution position for Chrome for Testing is unresolved and the only on-point public statement is adverse. | 203.8 MB on first run |

**Installer payload: ~117 MB installed** — `node.exe` 88.53 MB + the JS tree 18.11 MB + BrowserAI 9.76 MiB. **Browsers add 203.8 MB down / 433 MiB on disk on first run**, once per machine rather than once per update, so **disk after first run is ≈ 570 MB**. Per-component provenance in [kb: component sizes](../kb/playwright/provisioning-and-timings.md#component-sizes).

> ⚠️ **This table previously listed the browsers as bundled, totalling ~806 MB installed / ~239 MB compressed.** That was the design before 2026-08-14, when provisioning moved to first run; the row survived the decision and contradicted it for a day. The old figures are retained in [kb: component sizes](../kb/playwright/provisioning-and-timings.md#component-sizes) because a bundled build is the fallback if the redistribution question is ever resolved favourably. **They are not the numbers for *disk after first run***, which this note previously claimed: the ~806 MB total counts `chrome-headless-shell` (268.49 MB), which [is not provisioned at all](../README.md#settled-2026-08-15). Disk after first run is the ~117 MB payload plus 433 MiB of browsers — **≈ 570 MB**, not 806.

> ## Decided 2026-08-13: batteries included, zero host dependencies
>
> BrowserAI depends on **nothing** from the host — no Node, no .NET runtime, no Google Chrome. All three modes set `browserName: "chromium"` **and an explicit chromium-alias channel**, never `channel: "chrome"`, so every mode runs the Chromium BrowserAI provisioned rather than the user's Chrome. Dropping the channel *entirely* is not the same thing and does not work — see [config generation](C-sessions.md#config-generation-and-validating-it-against-the-runtime-we-ship). Ship as NativeAOT single-file.

Two consequences worth taking deliberately, because both are real costs of that decision:

- **You now own the browser's CVE response.** Google Chrome self-updates; a bundled Chromium updates only when BrowserAI cuts a release. When a Chromium RCE lands, every install stays vulnerable until you ship. Browser-security patching becomes a release obligation, not someone else's problem — and it is the one part of this payload where "update on our schedule" cuts the wrong way.
- **Headed behaviour changes.** Chromium is not Chrome: no proprietary codecs, different UA and fingerprint surface, no Widevine. Sites that behave differently under Chromium — DRM video, some enterprise SSO, anything fingerprinting the build — will behave differently from the system-Chrome setup this replaces. Verify the portals actually in use before cutting over.

"Single file" means BrowserAI.exe carries no .NET runtime dependency. The *payload* — `node.exe` and the vendored packages — remains a directory tree beside it, which is what Velopack's per-file delta needs to stay cheap. **The browsers are not in that tree**: they are provisioned on first run and live outside `current\` altogether, which is the arrangement that stops every update re-downloading them.

**NativeAOT is not free.** AOT forbids runtime reflection-based serialization, so `System.Text.Json` needs source-generated contexts (`JsonSerializerContext`). Velopack declares `IsAotCompatible=true` for net8.0+, and **so does `ModelContextProtocol`** — verified in-source on 2026-08-14, set on every target except `netstandard2.0`, at both `v1.4.1` and `v2.2.0` ([kb: SDK behaviours](../kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)). That is a meaningful signal and **not a proof for our usage**: the `JsonElement` passthrough at the heart of the proxy is AOT-friendly, but a declaration is the author's claim about their code, not about how we drive it. Publish AOT and run the suite against it before committing. The fallback is self-contained trimmed (~70 MB), which against the payload is noise. **AOT buys cold-start latency and the dropped runtime dependency here, not size.**

**The security boundary is identical in every build configuration.** Settled 2026-08-16: there is **no conditional compilation anywhere around session-type enforcement** — no `#if DEBUG` that relaxes a refusal, no `[Conditional]` attribute on a check, no environment variable or launch flag that turns one off for convenience. The `(tool, mode) → allow/deny` decision from [§H's one table](H-model-surface.md#h1-the-one-table) compiles to the same code in Debug and in Release.

This belongs beside the packaging decisions rather than beside the enforcement rules, because it is a statement about **the artifact**: a check that exists in one configuration and not the other means the build that was tested is not the build that ships, and every test asserting the boundary holds was run against a binary that is not the product. The gap is invisible from either side — the Debug suite is green because the check ran, and the Release artifact is silent because there is nothing to report.

The convenience this forbids is real and should be answered directly rather than by exception: the thing a developer actually wants is to *see* why a call was refused, not to have it permitted. That is [`debug` on `init` and `resume`](C-sessions.md#the-session-directory-is-the-identity), which raises the log level and changes no decision. A boundary that can be relaxed by rebuilding is not a boundary; the whole value of `interactive` is that a human relies on it, and a human cannot inspect which configuration produced the binary they are typing a password into.

> **Superseded 2026-08-14.** This paragraph argued that shipping the browser deletes a whole subsystem — the preflight, the install mutex, the staleness timeout, the detached installer, the LLM-readable retry instructions — because that machinery exists only when the browser arrives on demand. The argument was sound and we did not take it: [the redistribution position is unresolved](#first-run-browser-provisioning), so the browser *does* arrive on demand and the subsystem is specified thirty lines below. Kept because it correctly prices what first-run provisioning costs us, and that price should stay visible.

> ## `browserName: "chromium"` must be set explicitly or none of this runs
>
> `@playwright/mcp` 0.0.79 defaults to **`channel: "chrome"`** — system Chrome — and `headless: false` on Windows. Verified empirically: with an **empty** browsers directory, `initialize`, `tools/list` *and* `browser_navigate` all succeeded, because the bundled tree was never consulted ([kb: upstream config](../kb/playwright/configuration.md)).
>
> Omit `browserName` and the entire shipping-Chromium premise is silently dead code. This is the same failure shape as every bug in [the README's opening table](../README.md#read-this-before-designing-anything).

> ## The sandbox is off, and the config key that should enable it does nothing
>
> Measured 2026-08-15: with `"launchOptions": { "chromiumSandbox": true }` set explicitly in a config file, the browser and **every** child still ran with `--no-sandbox`. Only the CLI flag `--sandbox` actually enabled it. `validateBrowserConfig` intends `chromiumSandbox = true` on non-Linux, so this is upstream behaviour contradicting upstream intent ([kb: upstream config](../kb/playwright/configuration.md)).
>
> Two consequences. **BrowserAI must pass `--sandbox` on the command line, never the config key** — and must assert the absence of `--no-sandbox` from the child's resolved browser command line, because the key reads fine and has no effect. **And the configuration measured above runs Chromium unsandboxed** — verified 2026-08-15 @ `@playwright/mcp` 0.0.79 — which is a security posture nobody chose. This is [§why-3](../README.md#3-two-failure-classes-exist-that-no-configuration-can-fix) with a live instance attached: a config key that parses, validates, and is discarded. Raise it upstream.

`PLAYWRIGHT_BROWSERS_PATH` must be **absolute** — a relative value resolves against `INIT_CWD` (inherited from any npm ancestor) before `cwd`. The expected layout, verified by execution:

```
<browsers-root>\
  chromium-1237\chrome-win64\chrome.exe
  ffmpeg-1011\ffmpeg-win64.exe
  winldd-1007\PrintDeps.exe
```

> `chromium_headless_shell-1237\` appears in this layout only if something asks for it. **We do not provision it** — [full Chromium in every mode](../README.md#settled-2026-08-15) — so `--no-shell` above is load-bearing, not tidiness.

Note the asymmetry: the outer directory uses **underscores**, the inner one **dashes**. No sentinel files (`INSTALLATION_COMPLETE`, `DEPENDENCIES_VALIDATED`) are needed to launch — the only launch-time check is file accessibility of the executable. Strip `.links/` from the shipped tree; it contains the build machine's absolute paths. The layout, the `INIT_CWD` hazard and the `DEPENDENCIES_VALIDATED` write are in [kb: first-run provisioning](../kb/playwright/provisioning-and-timings.md#first-run-provisioning).

Build the browser payload with the pinned package itself, so the revision comes from `browsers.json` rather than a hand-typed URL:

```
set PLAYWRIGHT_BROWSERS_PATH=<staging>
node.exe <staging>\node_modules\@playwright\mcp\cli.js install-browser chromium --no-shell --no-progress
```

## First-run browser provisioning

**BrowserAI does not ship the browser inside the installer. It provisions it on first run**, as Playwright itself does. The redistribution position for Chrome for Testing is unresolved and the only on-point public statement is adverse — a Google engineer, 2023: *"Chrome for Testing is a flavor of Google Chrome, so google.com/chrome/terms applies"*, which forbids redistribution. This removes ≈ **699 MB** from the payload — full Chromium 426.88 MB, `chrome-headless-shell` 268.49 MB, `ffmpeg` 3.35 MB and `winldd` 0.25 MB, out of a bundled total of ~806 MB, leaving the ~117 MB above. (An earlier draft said ~427 MB, which is the full-Chromium term alone.) Note the phrase "our own Chrome for Testing" elsewhere in this document means **the build BrowserAI manages**, never one we ship.

**The version is pinned for free.** `playwright-core/browsers.json` carries the revision and `browserVersion`; the URL is built by substituting that version into a template that 307s to Google's bucket. That file is inside the artifact, never looked up online, and no "latest" lookup exists anywhere in the registry code. A release therefore knows forever exactly which browser it wants. Old builds resolve back to Chrome 115 (Jul 2023) — ~3 years of evidence, and **Google documents no retention policy**, so it is not a guarantee ([kb: first-run provisioning](../kb/playwright/provisioning-and-timings.md#first-run-provisioning)).

**Measured 2026-08-15, exact `content-length` from the CDN:** `chrome-win64.zip` 202,283,919 B + `ffmpeg-win64.zip` 1,411,741 B + `winldd-win64.zip` 128,684 B = **203.8 MB down, 433 MiB on disk**. Arithmetic for slower links: 2 m 43 s at 10 Mbps, 27 m 11 s at 1 Mbps.

> **This supersedes the 323.5 MB / ~700 MiB figures**, which were measured on 2026-08-14 and included `chrome-headless-shell` (119.7 MB down, 269 MiB on disk). [Settled 2026-08-15](../README.md#settled-2026-08-15), the shell is not provisioned — full Chromium in every mode — so dropping it took ~120 MB off the download and ~269 MiB off disk. The three-way discrepancy this section previously flagged is resolved: 202.3 MB was one term of the old sum, and the sum itself no longer applies.

**`init` must not block, and must say what it is doing.** Return immediately with `browserProvisioning: "downloading"` and an error on browser-needing calls that states the fact rather than hiding it:

> *First use of this browser version on this machine. The download has started. Wait ~10 seconds and retry.*

A long unexplained delay corrupts whatever timing the calling agent is managing; a stated fact lets it decide what a wait means for its own work. In-session recovery is proven — the same child navigates successfully once the install lands, with no restart.

**Strip upstream's remediation string.** It says `Run npx @playwright/mcp install-browser chromium`, which BrowserAI does not ship and which would resolve a different package at a different revision. A model will act on it. Replace it with ours: *delete `<path>` and call `init` again to re-download.*

**Environment.** Strip `PLAYWRIGHT_DOWNLOAD_HOST` and its three per-browser variants — they replace the mirror list with a single host, and since retries rotate through that list (`retryCount = 5`), setting one turns five attempts into five attempts at the same dead server. Pass through `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`/`ALL_PROXY` and **`NODE_EXTRA_CA_CERTS`**, needed under TLS inspection. SOCKS is unsupported on the download path. Egress needs `cdn.playwright.dev`, `storage.googleapis.com` and `playwright.download.prss.microsoft.com` ([kb: first-run provisioning](../kb/playwright/provisioning-and-timings.md#first-run-provisioning)).

**Browsers live at `%LocalAppData%\BrowserAI\browsers\`**, resolved from `VelopackLocator.Current.RootAppDir` — **never** inside `current\`, or every update re-downloads. `PLAYWRIGHT_SKIP_BROWSER_GC=1` is mandated, so pruning old revisions becomes BrowserAI's job.

**What upstream's integrity check does not cover.** Playwright validates only `content-length`, and upstream closed and locked the request for checksums ([#39559](https://github.com/microsoft/playwright/issues/39559)). `INSTALLATION_COMPLETE` is written last, so an *interrupted* install self-heals — but a tree corrupted **after** a successful install never re-downloads, because the marker short-circuits without validating anything. [Settled 2026-08-15](../README.md#settled-2026-08-15): we do not add our own health checks; the recovery is manual and the error text above is what makes it discoverable. **The consequence is stated plainly so it is a known limit rather than a surprise:** antivirus quarantining one DLL leaves a permanently broken install that only a human deleting the directory will fix.

**Node SEA, `pkg` and `nexe` are all dead ends** — do not spend time on them. `playwright-core` violates SEA's "no filesystem module loading" constraint in **five verified ways**, enumerated in [kb: first-run provisioning](../kb/playwright/provisioning-and-timings.md#first-run-provisioning), and SEA would save nothing regardless — the output *is* a copy of `node.exe` plus your blob. `vercel/pkg` was archived 2024-01-13; Bun and Deno both have open issues on precisely the Playwright browser-launch path.

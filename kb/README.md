<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Knowledge base — what we measured

[`../README.md`](../README.md) records what we **decided** and [`../PLAN.md`](../PLAN.md)
records what we are going to **build**. This directory records what we
**measured**, and it exists because those are different things with different
half-lives. A decision stays true until we change our minds. A plan is consumed
as the code gets written. A measurement stays true until upstream ships.

**What belongs here:** a fact about Chromium, Firefox, Playwright, Node or
Windows that we established by running something, reading a shipped binary, or
reading upstream source — together with enough provenance to re-establish it.

**What does not:** design decisions (`../README.md`), the implementation plan
(`../PLAN.md`), work items (`../TODO.md`), or the review procedure
([`../UPSTREAM-REVIEW.md`](../UPSTREAM-REVIEW.md)).

## The articles

| Article | Holds |
|---|---|
| [`windows/processes.md`](windows/processes.md) | Job objects and containment; stdio, exit codes and process startup — **including third-party code that writes to stdout's *handle* from a type initializer**; durable writes; recursive-enumeration and interop traps |
| [`windows/detection.md`](windows/detection.md) | Finding a browser that is already running — message windows, cross-process title reads, image-path enumeration, lock files, Windows object-name scoping, and **named-mutex / lock-file semantics from the C# prior art** |
| [`chromium/resurrection.md`](chromium/resurrection.md) | `RegisterApplicationRestart`, the 1023-character limit, and what actually resurrected the maintainer's browsers |
| [`chromium/profiles.md`](chromium/profiles.md) | What Chrome does with an unusable `--user-data-dir`: fallback, exit code 21, and the dialog that blocks startup |
| [`chromium/fingerprinting.md`](chromium/fingerprinting.md) | Whether `--browser-test` is web-detectable, and the baseline exposure that makes the question near-moot |
| [`playwright/configuration.md`](playwright/configuration.md) | Config keys that are silently discarded, defaults that are not what they look like, provisioning, environment and merge order, policy, shutdown |
| [`playwright/tools-and-artifacts.md`](playwright/tools-and-artifacts.md) | Tool counts, the package shape, tools that reach credentials, and how artifacts land on disk |
| [`playwright/provisioning-and-timings.md`](playwright/provisioning-and-timings.md) | Component sizes, first-run download, and every measured latency |
| [`mcp/protocol.md`](mcp/protocol.md) | The protocol split, the client at the other end, and the tooling around both |
| [`mcp/sdk.md`](mcp/sdk.md) | `ModelContextProtocol` as a proxy has to drive it, including the 2026-08-15 spike and the 2026-08-16 NativeAOT/ILC additions |
| [`packaging/velopack.md`](packaging/velopack.md) | The update path, its nine landmines, the install/update/rollback verification of each, and the restart/mutex handover race |
| [`packaging/dependencies.md`](packaging/dependencies.md) | Package provenance with date stamps, token cost, the licence terms of everything shipped, and **what vendoring a runtime cost two in-house repositories** |
| [`history.md`](history.md) | The legacy setup this project replaces, and corrections applied after the fact |

## Conventions

Every entry carries a marker and a date:

| Marker | Meaning |
|---|---|
| **`[FLOATS]`** | Depends on a version this project floats. **Re-verify at every upstream review.** Listed in [Re-verification index](#re-verification-index). |
| **`[STABLE]`** | A Windows or protocol fact that upstream cannot move. Re-verify only on a Windows major version. |
| **`[MACHINE]`** | True of the maintainer's machine, not of the world. Never generalise; never act on it as if universal. |
| **`[UNVERIFIED]`** | Inferred, not observed. Says so, and says why it was not observed. |

**Never edit a result without re-running the measurement.** An entry whose number
was updated by reasoning rather than by running something is worse than no entry,
because it reads identically to one that was measured. If a re-check is owed and
has not happened, mark it `[STALE]` rather than guessing.

Versions in force for everything below unless stated otherwise:
`@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 ·
Node 26.7.0 (libuv 1.52.1) · Chrome for Testing 152.0.7977.8 (`chromium-1237`) ·
system Google Chrome 151.0.7922.138 · Firefox `firefox-1539` · Windows 11 Pro
26200.

> ⚠️ **The Node above is not the Node we ship.** Measurements here ran on
> **26.7.0**, the Current line; the bundled runtime is **v24.19.0 LTS**
> ([§A](../plan/A-runtime.md#a-ship-and-own-the-runtime)). That gap matters in one place
> specifically: **libuv's permissive job object**
> ([kb](windows/processes.md#job-objects-and-process-containment)) sits in the
> chain between BrowserAI's job and the browser, and the containment guarantee is
> stated as holding *through* it. That was established against libuv 1.52.1 as
> shipped in 26.7.0 and **has not been re-checked on v24.19.0** — not "checked and
> found identical". Re-run `.work/jobtest/` under the bundled `node.exe` before
> the guarantee is claimed for a shipped build; nothing else here is known to
> depend on the Node version.

## Re-verification index

Every `[FLOATS]` fact is *meant* to be re-checked at upstream review, and this
table is how that happens. **It does not yet cover all of them**: **97**
`[FLOATS]` markers stand across the articles against the **41** numbered rows
below (43 lines, counting 4a and 4b),
because one row often stands for a cluster of related entries and because rows
have simply been missed — two whole articles carried none until 2026-08-15. Read a
missing row as a gap in this table, never as permission to skip the fact.

> **The 97 is a marker count, not an entry count** — counted 2026-08-16 with
> `grep -ro "\[FLOATS\]" --include=*.md .`, excluding this file's own four
> occurrences. Some entries carry a split marker (`[FLOATS]` for the numbers,
> `[STABLE]` for the mechanism) and are counted once each, so the true number of
> distinct floating *facts* is somewhat lower. It is recorded this way because a
> reproducible command beats a hand tally that silently drifts — which is what
> the previous "roughly 93" had become.

**The `Automated by` column is what makes this a gate rather than a checklist.** A row naming a test is answered by the suite and needs nobody. A row marked *manual* **must be answered by name in the [`upstream-review.json`](../upstream-review.json) entry**, with an outcome — whether an upstream change touches one of our abstractions is judgement, and judgement cannot be asserted mechanically. **A row that is neither automated nor answered fails [the gate](../plan/testing.md#the-upstream-review-gate).** Automating a manual row is always an improvement; naming a test here that does not exist is worse than leaving it manual, because it reads as covered. In
priority order — the first three would each silently invalidate a design
decision:

| # | Fact | Breaks if | Check | Automated by |
|---|---|---|---|---|
| 1 | Playwright's restart command line overshoots 1023 by 531+ ([kb](chromium/resurrection.md)) | Playwright trims its arg list | `GetApplicationRestartSettings` on a live browser returns `ERROR_NOT_FOUND` | `RestartRegistrationTests` |
| 2 | Job containment holds end to end ([kb](windows/processes.md#job-objects-and-process-containment)) | Playwright, Chromium or Firefox changes spawn flags | Run `.work/jobtest/` against both browsers | `JobContainmentTests` |
| 3 | `chromiumSandbox` config key still discarded ([kb](playwright/configuration.md#silent-config-failures)) | Upstream fixes it | Assert `--no-sandbox` absent from the child's browser command line | `SandboxFlagTests` |
| 4 | `Chrome_MessageWindow` title format ([kb](windows/detection.md)) | Chromium changes `ProcessSingleton` | Exact-title lookup against a launched browser | `MessageWindowTests` |
| 4a | **Cross-process `GetWindowTextW` bypasses `WM_GETTEXT`** — undocumented behaviour of a documented function, and the sweep rests on it ([kb](windows/detection.md#cross-process-title-reads--settled-by-two-independent-agents)) | A Windows change routes the read through the message queue | Child process with a WndProc that suppresses `WM_GETTEXT`; assert the parent still reads the kernel name. **No browser needed — runs in milliseconds on every build** | `WindowTextBypassTests` |
| 4b | A headless launch resolves to full `chrome.exe`, not `chrome-headless-shell` — **because we set a chromium-alias channel**; with no channel `getExecutableName` picks the shell ([selector](playwright/configuration.md#defaults-that-are-not-what-they-look-like), and the [unreconciled 0.0.79 observation](windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)) | Upstream changes binary selection, or our channel stops reaching the child. Not silent — the shell is never provisioned, so the launch **fails loudly** — but the failure would be baffling without this note | Launch through the real path, assert the walk yields a titled window owned by that PID, **and assert the resolved channel via `browser_get_config`** — which is also what closes the open disagreement between the two entries | `HeadlessBinaryTests` |
| 5 | Chromium/Firefox request no breakaway on browser paths ([kb](windows/processes.md#job-objects-and-process-containment)) | Either adds one | Source search for `CREATE_BREAKAWAY_FROM_JOB` | — *manual* |
| 6 | `--browser-test` call-site inventory (11 files) ([kb](chromium/fingerprinting.md)) | Chromium adds a web-facing site | Source search for `switches::kBrowserTest` | — *manual* |
| 7 | `browserName`/`channel`/binary-selection defaults ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | `validateBrowserConfig` or `getExecutableName` changes | Config round-trip via `browser_get_config` | `ConfigRoundTripTests` |
| 8 | `outputMaxSize` has no default ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | `defaultConfig` gains one | Assert unset in the resolved config | `OutputMaxSizeTests` |
| 9 | Firefox honours `toolkit.winRegisterApplicationRestart` ([kb](chromium/resurrection.md#the-mechanism-and-what-is-still-unproven)) | Mozilla removes the pref | Source check in `nsAppRunner.cpp` | — *manual* |
| 10 | `winldd` no-op for Chromium ([kb](playwright/configuration.md#browser-provisioning)) | Upstream fixes `chrome-win` → `chrome-win64` | Cold-start latency; source check | — *manual* |
| 11 | Tool counts — 78 internal, 69 exposed, 24 default ([kb](playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)) | Upstream adds, removes or reclassifies a tool | Golden `tools/list` snapshot; count `skillOnly` in the resolved bundle | — *manual* |
| 12 | `storage` is 17 tools — **and the other capabilities were never counted** ([kb](playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)) | Any capability's membership changes | Count every capability's tools from the resolved bundle. Never from memory | — *manual* |
| 13 | `browser_run_code_unsafe` reaches `httpOnly` cookies from the **default** surface ([kb](playwright/tools-and-artifacts.md#tools-that-reach-credentials)) | Upstream sandboxes it or moves it out of `core` | Run the probe: default caps, `page.context().cookies()` | — *manual* |
| 14 | `browser_storage_state` omits IndexedDB ([kb](playwright/tools-and-artifacts.md#tools-that-reach-credentials)) | Upstream passes `{indexedDB:true}` | Source check at the `storageState()` call site | — *manual* |
| 15 | The child's protocol ceiling is `2025-11-25`, and it never rejects — it caps or echoes ([kb](mcp/protocol.md#the-protocol-split)) | Upstream adopts a newer revision | Assert the negotiated version at startup | — *manual* |
| 16 | `DiscoverProbeTimeout` is 5 s, and the client pin is what skips the probe ([kb](mcp/protocol.md#the-protocol-split)) | The SDK changes the default or the probe | Assert the pin in every test client; time a spawn against the ~300 ms baseline | — *manual* |
| 17 | `PLAYWRIGHT_MCP_*` count is **42**, two of them outside the config mapping ([kb](playwright/configuration.md#environment-merge-order-and-startup-output)) | Upstream adds a variable | Derive the count from the resolved bundle; the allowlist test must not carry a literal | — *manual* |
| 18 | `--caps` and `PLAYWRIGHT_MCP_CAPS` replace rather than merge ([kb](playwright/configuration.md#environment-merge-order-and-startup-output)) | `mergeConfig` changes | Config round-trip via `browser_get_config` | — *manual* |
| 19 | Nine artifact generator prefixes ([kb](playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) | Upstream adds an artifact type | Enumerate prefixes in the resolved bundle; an unknown prefix must fail the sort test | — *manual* |
| 20 | Killed children leak `browser@<guid>` descriptors ([kb](playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) | `BrowserServer.stop()` learns to clean up with a `userDataDir` set | Kill a child, list the browsers-registry root | — *manual* |
| 21 | Payload sizes, the 203.8 MB first-run download and the 20.3 s provisioning time ([kb](playwright/provisioning-and-timings.md)) | Any browser or Node revision bump | Re-measure at each bump. The 202.3 / 323 MB / ~300 MB discrepancy is **settled** — 203.8 MB by exact CDN `content-length`, 2026-08-15 — so what is owed now is the 20.3 s figure, which was timed on a run that also fetched the shell, and the **compressed** payload size, which has never been measured at all | — *manual* |
| 22 | Full Chromium refuses a second instance; `chrome-headless-shell` does not notice one ([kb](windows/detection.md#process-image-path--the-fully-documented-detection-path)) | Upstream changes the singleton, or the shell is ever shipped | Launch twice against one profile directory, on both binaries | — *manual* |
| 23 | SDK behaviours — `cmd.exe /c` prefix, `ListToolsAsync` filtering, `ContentBlock` drop-and-throw ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | Any `ModelContextProtocol` bump | The fake-child passthrough tests are written against exactly these | — *manual* |
| 24 | Velopack landmines — feed-URL composition, `SetAutoApplyOnStartup`, the stub, `force_stop_package` ([kb](packaging/velopack.md#the-nine-landmines-claim-and-verdict)) | Any Velopack bump | The update lane: real feed URL, real N→N+1, real delta | — *manual* |
| 25 | Claude Code truncates `instructions` and tool descriptions at 2 KB, defers schemas, and **now handles** `tools/list_changed` ([kb](mcp/protocol.md#the-client-claude-code)) | Any client release | Measure both strings at build time; re-stamp the client version the claim was checked at | — *manual* |
| 26 | Payload licensing as shipped — `winldd` has no license file, full Chromium has no OSS license ([kb](packaging/dependencies.md#third-party-payload-as-shipped)) | Upstream adds one, or the payload composition changes | Re-read the shipped trees at each revision bump | — *manual* |
| 27 | **NativeAOT publishes clean and the proxy runs** ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | Any SDK bump | `PublishAot`, zero warnings, run the published binary against a real child — **and grep the publish output for `will always throw`**. Amended 2026-08-16: ILC reports an always-throwing method as [neither a warning nor an error](mcp/sdk.md#added-2026-08-16--not-part-of-the-2026-08-15-spike), so exit 0 plus zero warnings does not cover it, and the check as originally written would have passed a binary with a dead code path in it | — *manual* |
| 28 | The SDK still never relays `notifications/cancelled` ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | The SDK fixes it — our hand-rolled path would then double-send | Cancel a call, assert exactly one downstream notification | — *manual* |
| 29 | `Filters.Message.IncomingFilters` still exposes `Result` as raw `JsonNode?` ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | Any SDK bump; this is the whole proxy hook | Short-circuit a `tools/call` and compare bytes | — *manual* |
| 30 | `ListToolsAsync(RequestOptions?, ct)` still drops silently ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | SDK fixes or changes it | Fake child with an invalid `x-mcp-header`; compare both overloads | — *manual* |
| 31 | `StdioClientTransport` still wraps in `cmd.exe` ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | SDK fixes it — the custom transport stays correct either way, but the rationale changes | Probe `process.ppid` from a node child | — *manual* |
| 32 | An unusable `--user-data-dir` still falls back invisibly, a deny-all DACL still exits 21, and the "failed to create data directory" dialog still blocks startup ([kb](chromium/profiles.md)) | Chromium changes `RecursiveDirectoryCreate`, `ProcessSingleton` or its error dialogs | Launch against a file-occupied path and against a DACL'd directory; assert fallback, exit 21 and the dialog behave as recorded. **That article is the whole basis for [validate every path before launch](../README.md#settled-2026-08-15)** and had no row here until 2026-08-15 | — *manual* |
| 33 | A healthy start still prints `Session: <path>` to stderr, every time ([kb](history.md#the-legacy-setup-and-this-machine)) | Upstream changes startup output — in either direction: a new benign line trips the classifier, a removed one turns it into dead code | Classify a real start's stderr. [§E](../plan/E-lifecycle.md#e-lifecycle-and-observability) ports the two regexes **verbatim**, so this is behaviour our code copies rather than merely observes | — *manual* |
| 34 | Firefox's cost ratios against Chromium — ~2× RAM, ~10× first navigate, ~24× idle CPU, ~20× profile disk ([kb](history.md#the-legacy-setup-and-this-machine)) | Any Firefox revision bump | Re-measure. The original harness was not preserved, so these are order-of-magnitude guidance; re-establish them properly before any decision turns on them again | — *manual* |
| 35 | Floating NuGet still needs **two** restores — `--force-evaluate` to resolve, locked-mode to verify ([kb](windows/processes.md#interop-and-the-toolchain)) | NuGet changes lock-file semantics, or the SDK's default. A one-step `--locked-mode` build looks green with the float silently dead | Resolve, then `git diff --exit-code -- "**/packages.lock.json"`. The whole floating-dependency policy rests on this being two commands | — *manual* |
| 36 | `setupExitWatchdog` still hooks stdin close / `SIGINT` / `SIGTERM` → `gracefullyCloseAll()`, hard-exiting after **15 s** ([kb](playwright/configuration.md#shutdown)) | Upstream changes its shutdown path or that ceiling | Close stdin on a real child; assert graceful close, and that nothing survives the ceiling. Teardown is built on it — [no killing is involved in the normal path](../plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever) | — *manual* |
| 37 | `--console-level` still defaults to `info`, silently dropping `debug` ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | Upstream changes the default | Config round-trip via `browser_get_config`. The default is [exposed on `init`](../README.md#settled-2026-08-15), so an upstream change silently changes what a caller is choosing between | — *manual* |
| 38 | Resume costs **515 ms** and loses only `sessionStorage`; browser-idle close recovers 329 → 110 MB and relaunch costs 186 ms ([kb](playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)) | Any browser or Playwright bump that changes profile durability or launch cost | Kill the node child, resume against the directory, assert cookies, localStorage, IndexedDB, service workers and CacheStorage all survive. **[Reclaim is forever](../plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever) rests on this**: if a resume stopped preserving the profile the no-expiry decision would be wrong, and nothing else in the design would say so | — *manual* |

| 39 | A console logging sink's **type initializer** calls `SetConsoleMode` on `STD_OUTPUT_HANDLE` before any log line, and no-ops silently when stdout is a pipe ([kb](windows/processes.md#stdio-exit-codes-and-process-startup)) | We adopt `Serilog.Sinks.Console` or any sink shaped like it — or the referenced version moves. Upstream `main` has **already dropped** the `#if PINVOKE` guard that leaves 3.1.2 inert on `netstandard2.0`, so a version bump alone can arm it | Read `ConsoleSink.cs` and `Platform/WindowsConsole.cs` **at the version actually referenced**, not at whichever copy is on disk. **Conditional row:** nothing today depends on Serilog, so this is owed only once a console sink reaches the dependency list — but it must be answered *then*, because the write is invisible under MCP and shows up only in interactive diagnostics | — *manual* |
| 40 | A legitimately **empty** channel 404s exactly like a misconfigured feed URL ([kb](packaging/velopack.md#1-the-channel-must-not-go-in-the-feed-url)) | Velopack returns an empty release list instead, or changes the status code | Request a channel with nothing published, assert the status, and confirm the health check does **not** alarm. Row 24's update lane only ever exercises a populated channel, so this is not covered there | — *manual* |
| 41 | `UpdateExe.Start`'s first positional parameter is the **locator**, not `waitPid`; `ApplyUpdatesAndRestart` already restarts on its own ([kb](packaging/velopack.md#the-restart-handover-race-and-why-updateexe-is-the-answer)) | Any Velopack bump changes either signature | Compile a call against the shipped assembly, and assert exactly **one** relaunch on the update path. The double-restart failure is silent — two working launches look like one slow one | — *manual* |

Add a row whenever a new `[FLOATS]` entry lands. An entry with no row is an entry
nobody will re-check.

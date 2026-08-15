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
| [`windows/processes.md`](windows/processes.md) | Job objects and containment; stdio, exit codes and process startup; interop and the toolchain |
| [`windows/detection.md`](windows/detection.md) | Finding a browser that is already running — message windows, cross-process title reads, image-path enumeration, lock files, and Windows object-name scoping |
| [`chromium/resurrection.md`](chromium/resurrection.md) | `RegisterApplicationRestart`, the 1023-character limit, and what actually resurrected the maintainer's browsers |
| [`chromium/profiles.md`](chromium/profiles.md) | What Chrome does with an unusable `--user-data-dir`: fallback, exit code 21, and the dialog that blocks startup |
| [`chromium/fingerprinting.md`](chromium/fingerprinting.md) | Whether `--browser-test` is web-detectable, and the baseline exposure that makes the question near-moot |
| [`playwright/configuration.md`](playwright/configuration.md) | Config keys that are silently discarded, defaults that are not what they look like, provisioning, environment and merge order, policy, shutdown |
| [`playwright/tools-and-artifacts.md`](playwright/tools-and-artifacts.md) | Tool counts, the package shape, tools that reach credentials, and how artifacts land on disk |
| [`playwright/provisioning-and-timings.md`](playwright/provisioning-and-timings.md) | Component sizes, first-run download, and every measured latency |
| [`mcp/protocol.md`](mcp/protocol.md) | The protocol split, the client at the other end, and the tooling around both |
| [`mcp/sdk.md`](mcp/sdk.md) | `ModelContextProtocol` as a proxy has to drive it, including the 2026-08-15 spike |
| [`packaging/velopack.md`](packaging/velopack.md) | The update path, its nine landmines, and the install/update/rollback verification of each |
| [`packaging/dependencies.md`](packaging/dependencies.md) | Package provenance with date stamps, token cost, and the licence terms of everything shipped |
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

## Re-verification index

Everything marked `[FLOATS]` is re-checked at upstream review. In priority order —
the first three would each silently invalidate a design decision:

| # | Fact | Breaks if | Check |
|---|---|---|---|
| 1 | Playwright's restart command line overshoots 1023 by 531+ ([kb](chromium/resurrection.md)) | Playwright trims its arg list | `GetApplicationRestartSettings` on a live browser returns `ERROR_NOT_FOUND` |
| 2 | Job containment holds end to end ([kb](windows/processes.md#job-objects-and-process-containment)) | Playwright, Chromium or Firefox changes spawn flags | Run `.work/jobtest/` against both browsers |
| 3 | `chromiumSandbox` config key still discarded ([kb](playwright/configuration.md#silent-config-failures)) | Upstream fixes it | Assert `--no-sandbox` absent from the child's browser command line |
| 4 | `Chrome_MessageWindow` title format ([kb](windows/detection.md)) | Chromium changes `ProcessSingleton` | Exact-title lookup against a launched browser |
| 4a | **Cross-process `GetWindowTextW` bypasses `WM_GETTEXT`** — undocumented behaviour of a documented function, and the sweep rests on it ([kb](windows/detection.md#cross-process-title-reads--settled-by-two-independent-agents)) | A Windows change routes the read through the message queue | Child process with a WndProc that suppresses `WM_GETTEXT`; assert the parent still reads the kernel name. **No browser needed — runs in milliseconds on every build** |
| 4b | Playwright's headless path still spawns full `chrome.exe`, not `chrome-headless-shell` ([kb](windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)) | Upstream switches binaries. Not silent — the shell is never provisioned, so the launch **fails loudly** — but the failure would be baffling without this note | Launch through the real path, assert the walk yields a titled window owned by that PID |
| 5 | Chromium/Firefox request no breakaway on browser paths ([kb](windows/processes.md#job-objects-and-process-containment)) | Either adds one | Source search for `CREATE_BREAKAWAY_FROM_JOB` |
| 6 | `--browser-test` call-site inventory (11 files) ([kb](chromium/fingerprinting.md)) | Chromium adds a web-facing site | Source search for `switches::kBrowserTest` |
| 7 | `browserName`/`channel`/binary-selection defaults ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | `validateBrowserConfig` or `getExecutableName` changes | Config round-trip via `browser_get_config` |
| 8 | `outputMaxSize` has no default ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | `defaultConfig` gains one | Assert unset in the resolved config |
| 9 | Firefox honours `toolkit.winRegisterApplicationRestart` ([kb](chromium/resurrection.md#the-mechanism-and-what-is-still-unproven)) | Mozilla removes the pref | Source check in `nsAppRunner.cpp` |
| 10 | `winldd` no-op for Chromium ([kb](playwright/configuration.md#browser-provisioning)) | Upstream fixes `chrome-win` → `chrome-win64` | Cold-start latency; source check |
| 11 | Tool counts — 78 internal, 69 exposed, 24 default ([kb](playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)) | Upstream adds, removes or reclassifies a tool | Golden `tools/list` snapshot; count `skillOnly` in the resolved bundle |
| 12 | `storage` is 17 tools — **and the other capabilities were never counted** ([kb](playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)) | Any capability's membership changes | Count every capability's tools from the resolved bundle. Never from memory |
| 13 | `browser_run_code_unsafe` reaches `httpOnly` cookies from the **default** surface ([kb](playwright/tools-and-artifacts.md#tools-that-reach-credentials)) | Upstream sandboxes it or moves it out of `core` | Run the probe: default caps, `page.context().cookies()` |
| 14 | `browser_storage_state` omits IndexedDB ([kb](playwright/tools-and-artifacts.md#tools-that-reach-credentials)) | Upstream passes `{indexedDB:true}` | Source check at the `storageState()` call site |
| 15 | The child's protocol ceiling is `2025-11-25`, and it never rejects — it caps or echoes ([kb](mcp/protocol.md#the-protocol-split)) | Upstream adopts a newer revision | Assert the negotiated version at startup |
| 16 | `DiscoverProbeTimeout` is 5 s, and the client pin is what skips the probe ([kb](mcp/protocol.md#the-protocol-split)) | The SDK changes the default or the probe | Assert the pin in every test client; time a spawn against the ~300 ms baseline |
| 17 | `PLAYWRIGHT_MCP_*` count is **42**, two of them outside the config mapping ([kb](playwright/configuration.md#environment-merge-order-and-startup-output)) | Upstream adds a variable | Derive the count from the resolved bundle; the allowlist test must not carry a literal |
| 18 | `--caps` and `PLAYWRIGHT_MCP_CAPS` replace rather than merge ([kb](playwright/configuration.md#environment-merge-order-and-startup-output)) | `mergeConfig` changes | Config round-trip via `browser_get_config` |
| 19 | Nine artifact generator prefixes ([kb](playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) | Upstream adds an artifact type | Enumerate prefixes in the resolved bundle; an unknown prefix must fail the sort test |
| 20 | Killed children leak `browser@<guid>` descriptors ([kb](playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) | `BrowserServer.stop()` learns to clean up with a `userDataDir` set | Kill a child, list the browsers-registry root |
| 21 | Payload sizes and the 20.3 s provisioning time ([kb](playwright/provisioning-and-timings.md)) | Any browser or Node revision bump | Re-measure at each bump — **and settle the 202.3 / 323 MB / ~300 MB discrepancy while doing it** |
| 22 | Full Chromium refuses a second instance; `chrome-headless-shell` does not notice one ([kb](windows/detection.md#process-image-path--the-fully-documented-detection-path)) | Upstream changes the singleton, or the shell is ever shipped | Launch twice against one profile directory, on both binaries |
| 23 | SDK behaviours — `cmd.exe /c` prefix, `ListToolsAsync` filtering, `ContentBlock` drop-and-throw ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | Any `ModelContextProtocol` bump | The fake-child passthrough tests are written against exactly these |
| 24 | Velopack landmines — feed-URL composition, `SetAutoApplyOnStartup`, the stub, `force_stop_package` ([kb](packaging/velopack.md#the-nine-landmines-claim-and-verdict)) | Any Velopack bump | The update lane: real feed URL, real N→N+1, real delta |
| 25 | Claude Code truncates `instructions` and tool descriptions at 2 KB, defers schemas, and **now handles** `tools/list_changed` ([kb](mcp/protocol.md#the-client-claude-code)) | Any client release | Measure both strings at build time; re-stamp the client version the claim was checked at |
| 26 | Payload licensing as shipped — `winldd` has no license file, full Chromium has no OSS license ([kb](packaging/dependencies.md#third-party-payload-as-shipped)) | Upstream adds one, or the payload composition changes | Re-read the shipped trees at each revision bump |
| 27 | **NativeAOT publishes clean and the proxy runs** ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | Any SDK bump | `PublishAot`, zero warnings, run the published binary against a real child |
| 28 | The SDK still never relays `notifications/cancelled` ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | The SDK fixes it — our hand-rolled path would then double-send | Cancel a call, assert exactly one downstream notification |
| 29 | `Filters.Message.IncomingFilters` still exposes `Result` as raw `JsonNode?` ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | Any SDK bump; this is the whole proxy hook | Short-circuit a `tools/call` and compare bytes |
| 30 | `ListToolsAsync(RequestOptions?, ct)` still drops silently ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | SDK fixes or changes it | Fake child with an invalid `x-mcp-header`; compare both overloads |
| 31 | `StdioClientTransport` still wraps in `cmd.exe` ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | SDK fixes it — the custom transport stays correct either way, but the rationale changes | Probe `process.ppid` from a node child |

Add a row whenever a new `[FLOATS]` entry lands. An entry with no row is an entry
nobody will re-check.

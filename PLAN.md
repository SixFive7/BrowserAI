# BrowserAI — implementation plan

What to build, and what is known to go wrong while building it.

This document is **consumed as the code is written**. Every section here is a
requirement waiting to become a file in this repository; when it has, the section
has done its job. That is what separates it from [`README.md`](README.md), which
carries the architecture, the scope boundary and the reasoning behind each
decision and stays true for as long as the decision does, and from
[`kb/`](kb/README.md), which carries the measurements and goes stale when
upstream ships rather than when we change our minds.

Nothing here is built yet. Work settled in intent but not yet done is tracked in
[`TODO.md`](TODO.md).

---

## What BrowserAI must do

### A. Ship and own the runtime

| Component | Requirement | Measured size |
|---|---|---|
| Node.js | Bundled — a **single `node.exe`**, nothing else. Verified driving the full MCP protocol with no npm, no `node_modules`, no `.cmd` shims. | v24.19.0 LTS, **88.53 MB** |
| `@playwright/mcp` + `playwright-core` | Resolved to latest at **build** time, then vendored into the artifact as an exact tree — never `@latest` at **spawn** time. Zero native binaries; the tree is portable JS. See [Versioning policy](README.md#versioning-policy-everything-floats-the-build-freezes-it). | 0.0.79, **18.11 MB** |
| BrowserAI itself | NativeAOT, single file, no .NET runtime on the host. | ~10–15 MB |
| System Chrome | **Not used.** See below. | — |
| Browsers | **Not in the installer.** [Provisioned on first run](#first-run-browser-provisioning) — the redistribution position for Chrome for Testing is unresolved and the only on-point public statement is adverse. | 203.8 MB on first run |

**Installer payload: ~117 MB installed** — `node.exe` 88.53 MB + the JS tree 18.11 MB + BrowserAI ~10–15 MB. **Browsers add 203.8 MB down / 433 MiB on disk on first run**, once per machine rather than once per update. Per-component provenance in [kb: component sizes](kb/playwright/provisioning-and-timings.md#component-sizes).

> ⚠️ **This table previously listed the browsers as bundled, totalling ~806 MB installed / ~239 MB compressed.** That was the design before 2026-08-14, when provisioning moved to first run; the row survived the decision and contradicted it for a day. The old figures are retained in [kb: component sizes](kb/playwright/provisioning-and-timings.md#component-sizes) because they are still the right numbers for *disk after first run*, and because a bundled build is the fallback if the redistribution question is ever resolved favourably.

> ### Decided 2026-08-13: batteries included, zero host dependencies
>
> BrowserAI depends on **nothing** from the host — no Node, no .NET runtime, no Google Chrome. All four modes set `browserName: "chromium"` and **drop `channel: "chrome"` entirely**, so headed modes run Playwright's bundled Chromium rather than the user's Chrome. Ship as NativeAOT single-file.

Two consequences worth taking deliberately, because both are real costs of that decision:

- **You now own the browser's CVE response.** Google Chrome self-updates; a bundled Chromium updates only when BrowserAI cuts a release. When a Chromium RCE lands, every install stays vulnerable until you ship. Browser-security patching becomes a release obligation, not someone else's problem — and it is the one part of this payload where "update on our schedule" cuts the wrong way.
- **Headed behaviour changes.** Chromium is not Chrome: no proprietary codecs, different UA and fingerprint surface, no Widevine. Sites that behave differently under Chromium — DRM video, some enterprise SSO, anything fingerprinting the build — will differ from today. Verify the portals actually in use before cutting over.

"Single file" means BrowserAI.exe carries no .NET runtime dependency. The *payload* — `node.exe`, the vendored packages, both browsers — remains a directory tree beside it, which is what Velopack's per-file delta needs to stay cheap.

**NativeAOT is not free.** AOT forbids runtime reflection-based serialization, so `System.Text.Json` needs source-generated contexts (`JsonSerializerContext`). Velopack declares `IsAotCompatible=true` for net8.0+, and **so does `ModelContextProtocol`** — verified in-source on 2026-08-14, set on every target except `netstandard2.0`, at both `v1.4.1` and `v2.2.0` ([kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)). That is a meaningful signal and **not a proof for our usage**: the `JsonElement` passthrough at the heart of the proxy is AOT-friendly, but a declaration is the author's claim about their code, not about how we drive it. Publish AOT and run the suite against it before committing. The fallback is self-contained trimmed (~70 MB), which against the payload is noise. **AOT buys cold-start latency and the dropped runtime dependency here, not size.**

Shipping the browser deletes an entire subsystem from the current design: the preflight, the install mutex, the staleness timeout, the detached installer, and the retry instructions written for an LLM to read. That machinery exists solely because the browser arrives on demand.

> ### `browserName: "chromium"` must be set explicitly or none of this runs
>
> `@playwright/mcp` 0.0.79 defaults to **`channel: "chrome"`** — system Chrome — and `headless: false` on Windows. Verified empirically: with an **empty** browsers directory, `initialize`, `tools/list` *and* `browser_navigate` all succeeded, because the bundled tree was never consulted ([kb: upstream config](kb/playwright/configuration.md)).
>
> Omit `browserName` and the entire shipping-Chromium premise is silently dead code. This is the same failure shape as every bug in [the README's opening table](README.md#read-this-before-designing-anything).

> ### The sandbox is off, and the config key that should enable it does nothing
>
> Measured 2026-08-15: with `"launchOptions": { "chromiumSandbox": true }` set explicitly in a config file, the browser and **every** child still ran with `--no-sandbox`. Only the CLI flag `--sandbox` actually enabled it. `validateBrowserConfig` intends `chromiumSandbox = true` on non-Linux, so this is upstream behaviour contradicting upstream intent ([kb: upstream config](kb/playwright/configuration.md)).
>
> Two consequences. **BrowserAI must pass `--sandbox` on the command line, never the config key** — and must assert the absence of `--no-sandbox` from the child's resolved browser command line, because the key reads fine and has no effect. **And today's setup runs Chromium unsandboxed**, which is a security posture nobody chose. This is [§why-3](README.md#3-two-failure-classes-exist-that-no-configuration-can-fix) with a live instance attached: a config key that parses, validates, and is discarded. Raise it upstream.

`PLAYWRIGHT_BROWSERS_PATH` must be **absolute** — a relative value resolves against `INIT_CWD` (inherited from any npm ancestor) before `cwd`. The expected layout, verified by execution:

```
<browsers-root>\
  chromium_headless_shell-1237\chrome-headless-shell-win64\chrome-headless-shell.exe
  chromium-1237\chrome-win64\chrome.exe
  ffmpeg-1011\ffmpeg-win64.exe
```

Note the asymmetry: the outer directory uses **underscores**, the inner one **dashes**. No sentinel files (`INSTALLATION_COMPLETE`, `DEPENDENCIES_VALIDATED`) are needed to launch — the only launch-time check is file accessibility of the executable. Strip `.links/` from the shipped tree; it contains the build machine's absolute paths. The layout, the `INIT_CWD` hazard and the `DEPENDENCIES_VALIDATED` write are in [kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning).

Build the browser payload with the pinned package itself, so the revision comes from `browsers.json` rather than a hand-typed URL:

```
set PLAYWRIGHT_BROWSERS_PATH=<staging>
node.exe <staging>\node_modules\@playwright\mcp\cli.js install-browser chromium --only-shell --no-progress
```

#### First-run browser provisioning

**BrowserAI does not ship the browser inside the installer. It provisions it on first run**, as Playwright itself does. The redistribution position for Chrome for Testing is unresolved and the only on-point public statement is adverse — a Google engineer, 2023: *"Chrome for Testing is a flavor of Google Chrome, so google.com/chrome/terms applies"*, which forbids redistribution. This removes ~427 MB from the payload. Note the phrase "our own Chrome for Testing" elsewhere in this document means **the build BrowserAI manages**, never one we ship.

**The version is pinned for free.** `playwright-core/browsers.json` carries the revision and `browserVersion`; the URL is built by substituting that version into a template that 307s to Google's bucket. That file is inside the artifact, never looked up online, and no "latest" lookup exists anywhere in the registry code. A release therefore knows forever exactly which browser it wants. Old builds resolve back to Chrome 115 (Jul 2023) — ~3 years of evidence, and **Google documents no retention policy**, so it is not a guarantee ([kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning)).

**Measured 2026-08-15, exact `content-length` from the CDN:** `chrome-win64.zip` 202,283,919 B + `ffmpeg-win64.zip` 1,411,741 B + `winldd-win64.zip` 128,684 B = **203.8 MB down, 433 MiB on disk**. Arithmetic for slower links: 2 m 43 s at 10 Mbps, 27 m 11 s at 1 Mbps.

> **This supersedes the 323.5 MB / ~700 MiB figures**, which were measured on 2026-08-14 and included `chrome-headless-shell` (119.7 MB down, 269 MiB on disk). [Settled 2026-08-15](README.md#settled-2026-08-15), the shell is not provisioned — full Chromium in every mode — so dropping it took ~120 MB off the download and ~269 MiB off disk. The three-way discrepancy this section previously flagged is resolved: 202.3 MB was one term of the old sum, and the sum itself no longer applies.

**`init` must not block, and must say what it is doing.** Return immediately with `browserProvisioning: "downloading"` and an error on browser-needing calls that states the fact rather than hiding it:

> *First use of this browser version on this machine. The download has started. Wait ~10 seconds and retry.*

A long unexplained delay corrupts whatever timing the calling agent is managing; a stated fact lets it decide what a wait means for its own work. In-session recovery is proven — the same child navigates successfully once the install lands, with no restart.

**Strip upstream's remediation string.** It says `Run npx @playwright/mcp install-browser chromium`, which BrowserAI does not ship and which would resolve a different package at a different revision. A model will act on it. Replace it with ours: *delete `<path>` and call `init` again to re-download.*

**Environment.** Strip `PLAYWRIGHT_DOWNLOAD_HOST` and its three per-browser variants — they replace the mirror list with a single host, and since retries rotate through that list (`retryCount = 5`), setting one turns five attempts into five attempts at the same dead server. Pass through `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`/`ALL_PROXY` and **`NODE_EXTRA_CA_CERTS`**, needed under TLS inspection. SOCKS is unsupported on the download path. Egress needs `cdn.playwright.dev`, `storage.googleapis.com` and `playwright.download.prss.microsoft.com` ([kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning)).

**Browsers live at `%LocalAppData%\BrowserAI\browsers\`**, resolved from `VelopackLocator.Current.RootAppDir` — **never** inside `current\`, or every update re-downloads. `PLAYWRIGHT_SKIP_BROWSER_GC=1` is mandated, so pruning old revisions becomes BrowserAI's job.

**What upstream's integrity check does not cover.** Playwright validates only `content-length`, and upstream closed and locked the request for checksums ([#39559](https://github.com/microsoft/playwright/issues/39559)). `INSTALLATION_COMPLETE` is written last, so an *interrupted* install self-heals — but a tree corrupted **after** a successful install never re-downloads, because the marker short-circuits without validating anything. [Settled 2026-08-15](README.md#settled-2026-08-15): we do not add our own health checks; the recovery is manual and the error text above is what makes it discoverable. **The consequence is stated plainly so it is a known limit rather than a surprise:** antivirus quarantining one DLL leaves a permanently broken install that only a human deleting the directory will fix.

**Node SEA, `pkg` and `nexe` are all dead ends** — do not spend time on them. `playwright-core` violates SEA's "no filesystem module loading" constraint in **five verified ways**, enumerated in [kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning), and SEA would save nothing regardless — the output *is* a copy of `node.exe` plus your blob. `vercel/pkg` was archived 2024-01-13; Bun and Deno both have open issues on precisely the Playwright browser-launch path.

### B. Be the MCP server

stdio transport. Registered once at system or user scope, available in every repository, with no per-repo files.

**The protocol split is solved by configuration, not code.** `@playwright/mcp` 0.0.79 caps at `2025-11-25`; the current spec is `2026-07-28`, a breaking rewrite (removes `initialize`/`notifications/initialized`, adds `server/discover`, replaces server→client requests with the MRTR retry pattern, deprecates Roots/Sampling/Logging). The .NET SDK implements every revision from `2024-11-05` through `2026-07-28` and shipped 2026-07-28 support **on the spec's release date**. So the newer-upward/older-downward split is two properties:

```csharp
McpServerOptions.ProtocolVersion = null;          // upward: accept 2024-11-05 … 2026-07-28
McpClientOptions.ProtocolVersion = "2025-11-25";  // downward: pin to the child's ceiling
```

**The second line is not optional.** Left at `null`, the client probes the child with `server/discover` first, bounded by `DiscoverProbeTimeout` — **5 seconds by default**. If the child silently drops the unknown method instead of returning an error, *every child spawn costs a flat 5 s* against a ~300 ms baseline. It would present as "browser automation got slow," with no error anywhere. Pinning the client version skips the probe entirely ([kb: the protocol split](kb/mcp/protocol.md#the-protocol-split)).

Assert on the negotiated version at startup. The child never *rejects* a version — it caps or echoes silently (verified: offering `1999-01-01` returns `2025-11-25` with no error), so a mis-negotiation produces nothing to catch. The child's ceiling, the shape of the `2026-07-28` rewrite and the SDK's revision coverage are in [kb: the protocol split](kb/mcp/protocol.md#the-protocol-split).

### C. The `init` tool and instance handles

> **Decided 2026-08-13: one MCP registration, handle-based instance routing.**

The client calls `init` first, passing the session type and all target-directory information. BrowserAI resolves the directories, takes the appropriate locks, generates the child config, spawns a Playwright runtime, and returns a **short unique handle**. Every subsequent tool call carries that handle, which routes it to the right instance.

The point of the handle is separation: **the MCP server's lifetime is decoupled from the Playwright instances it supervises.** One BrowserAI process may own several children, and instance creation, lifetime and cleanup belong entirely to BrowserAI — not to the client, and not to Playwright.

This also replaces four static configurations with one dynamic one, and eliminates the relative-path hazard by making the directory an explicit argument rather than an implicit consequence of cwd.

#### The `init` contract

**Takes:** the mode; the browser; a required `purpose`; and **a required session directory. There is no default and there is no fallback.** An empty, relative, malformed or unusable path is rejected outright — never normalised into something that happens to work.

**Requiring the path is the design, not a validation detail.** A default is a decision made on the caller's behalf that the caller never notices making, and the whole failure class this project exists to eliminate is decisions nobody observed. Forcing every `init` and `resume` to name a location makes an agent state where this session's data lives — which is precisely the thought the founding stray-file problem shows nobody was having. It also removes the only path by which two agents could land in the same place without either choosing it.

The label exists because `checkout-flow-bug\` is navigable and `2026-08-14T04-11-50-882Z\` is not. Three months of the current setup produced 346 session directories and 1.5 GB, and the reason nobody pruned them is that nobody could tell what any of them had been ([kb: the legacy setup](kb/history.md#the-legacy-setup-and-this-machine)).

**Returns:** the handle, **and the resolved absolute paths.** [§settled](README.md#settled-2026-08-13) already requires logging those at instance creation; returning them costs nothing extra and puts them where the agent can act on them — it can tell the user where the screenshot is instead of guessing, and the paths become auditable from the transcript rather than only from a log file nobody opens.

**Per-instance paths are unconstrained; per-call filenames are not.** These look like the same decision and are not. `init`'s directory arguments are deliberately unrestricted — [§settled](README.md#settled-2026-08-13) accepts any path, because the caller is declaring where its data lives. A per-call `filename` names a file *within* a workspace already declared, so normalising it into that workspace is not a restriction on the caller's choice; it is honouring the choice already made. Record this distinction, because the two rules read as contradictory to anyone meeting them cold.

**Reject traversal rather than normalising it.** A `filename` of `..\..\..\foo.png` must resolve, be recognised as an escape, and be refused with an LLM-readable error — never silently collapsed into a path that happens to land somewhere.

**Check free disk space at `init`, and only with an O(1) query.** First-run browser provisioning needs **203.8 MB down and 433 MiB extracted**, so peak usage is ~640 MiB while both the archive and the tree exist; a session then grows unbounded. A refusal at `init` that names the number is recoverable in one turn; a failure partway through the download is the `spawn EFTYPE` shape — success-shaped, stderr empty, discovered at first navigation. **This must be a volume free-space query, never a directory walk**: `init` sits on the hot path of every session, and a check that scans the output tree would make the fix slower than the failure it prevents.

#### Three modes, and tracing as a modifier

The legacy setup had four modes — `headless`, `interactive`, `tracing`, `persistent`. With `--isolated` dropped they are exactly four of the eight combinations of three independent switches: **headed?**, **storage?**, **tracing?**

| # | headed | storage | tracing | Legacy name | |
|---|:---:|:---:|:---:|---|---|
| 1 | ✗ | ✗ | ✗ | `headless` | the workhorse |
| 2 | ✗ | ✗ | ✓ | — | gap |
| 3 | ✗ | ✓ | ✗ | — | credentialed and invisible |
| 4 | ✗ | ✓ | ✓ | — | as 3 |
| 5 | ✓ | ✗ | ✗ | `interactive` | a human types credentials the agent must never capture |
| 6 | ✓ | ✗ | ✓ | `tracing` | interactive, plus a trace |
| 7 | ✓ | ✓ | ✗ | `persistent` | logged-in agent work |
| 8 | ✓ | ✓ | ✓ | — | gap |

**Settled: three modes — `headless`, `interactive`, `persistent` — with `tracing` a boolean on any of them.** `tracing` was never a mode; row 6 is `interactive` with a flag, which is why it was the odd one out. Promoting it to a modifier *removes* a mode while *adding* capability: rows 2 and 8 arrive for free, the classification matrix shrinks from four rows to three, and rows 3–4 stay closed.

Rows 3 and 4 are deliberately not offered. They would be genuinely useful — routine work in a logged-in profile has no need to put a window on screen — but they are the only combination that grants full credential access with no visible signal that anything is driving the session. A window is not a security control; it is the sole cue a human gets. Opening that should be a decision taken on its own merits, never a side effect of making the switches orthogonal.

> **The mode is bound at `init` and carried by the handle**, exactly like the browser choice. `resume` reads it from `lock.json` and never accepts one, so a session cannot change what it is. Note the older argument in this section — that a named mode is harder to forge than a flag — is **weaker than it reads**: a flag bound at `init` and carried by the handle is equally unforgeable. The real reasons to keep names are the size of the classification matrix and the fact that a name carries intent to whoever reads it later.

**Discoverability is a requirement, not a nicety.** A mode nobody knows about is a mode nobody picks correctly, and the failure is silent — an agent that does not know `persistent` exists just fails to log in and reports the site as broken. All three channels below carry part of it:

- **Server `instructions`** — one compact line naming the three modes and the one-sentence rule for choosing. This is the only channel that reaches the model *before* it calls anything, so it must contain the choice, not a pointer to it. Costs perhaps 150 of the 2 KB.
- **`init`'s description** — the full table: what each mode grants, what it refuses, that `tracing` is a boolean orthogonal to all three, and that the choice is permanent for the directory's life.
- **`resume`'s description and result** — the recorded mode, played back, alongside the recorded purpose. An agent meeting an existing directory learns what it is without guessing.
- **Refusal text is the fourth channel and the most effective one.** A storage tool called on a `headless` handle must not fail with "not permitted"; it must name the mode that would permit it and what to do — a mode error is a teaching moment arriving exactly when the model is ready to learn. This channel has no budget, so it carries the detail the capped ones cannot.

Pin all of it with tests, as `SixFive7/OutlookAI` does for its instructions string: the mode list in `instructions`, in `init`'s description, and in the refusal text must all be generated from **one** table, so a fourth mode cannot ever be added in one place and missed in the other three.

#### Where guidance lives: three channels, two of them capped

An MCP server can address the model in three places, and they are seen at different times. Putting the wrong content in the wrong one is why agents forget handles.

| Channel | Seen | Budget | Carries |
|---|---|---|---|
| **Server `instructions`** (on `initialize`) | Always, at session start — Claude Code loads tool *names* and server instructions eagerly and defers schemas | **2 KB, truncated silently** | That `init` comes first, and why. The fact that is useless once it is needed |
| **`init`'s description** | When the model reaches for `init` | **2 KB, truncated silently** | Argument meanings, the real-Chrome-profile warning, and the retention policy — the spec requires retention to be stated *here*, in the creation tool's description |
| **`init`'s result** | Immediately after the call | none | Resolved absolute paths, the layout, the session label |

The server instructions exist to pre-empt the cold-start failure named above: **the first call after a restart will forget the handle.** Only an eagerly-loaded string can reach the model before it makes that mistake. Detail belongs in the result, where there is no budget and the paths are concrete — spending a third of a 2 KB allowance on a directory diagram every agent re-reads at the wrong moment is the wrong trade. The client behaviour these budgets rest on — eager names, deferred schemas, silent truncation at 2 KB — is in [kb: the client](kb/mcp/protocol.md#the-client-claude-code).

`SixFive7/OutlookAI` is the in-house precedent for treating this as a contract rather than prose: its instructions string lives in `ServerMetadata.cs` and is pinned by tests.

**Why a handle beats a `mode` enum.** A handle is *minted by the server*. The model cannot invent one for a session type it never created, so `browser_cookie_list` cannot be aimed at an interactive session by choosing the wrong string — an interactive handle simply does not permit storage tools. A `mode` parameter, by contrast, is a value the model composes fresh on every call and can compose wrongly. The handle converts a model-authored assertion into a server-issued capability reference, which is a materially stronger position.

It does not close the gap entirely: a connection holding both an interactive and a persistent handle can still route a call to the persistent one. But that grants nothing new — an agent holding a persistent handle was already entitled to those cookies. **The interactive guarantee holds**, and that is the one that matters.

**Critical constraint — read before designing `init`:** the MCP spec (2026-07-28, *Tools § Capabilities*) states the tool set "**MAY** change over time … but **MUST NOT** vary per-connection or as a side effect of other requests on the connection." SEP-2567 removed protocol-level sessions outright. Separately, `notifications/tools/list_changed` is unreliable in practice — Claude Code registers no handler for it (issues [#13646](https://github.com/anthropics/claude-code/issues/13646), [#4118](https://github.com/anthropics/claude-code/issues/4118)). **That citation is stale** — it held at client 2.0.65 and is false at 2.1.231 ([kb: the client](kb/mcp/protocol.md#the-client-claude-code)). The conclusion below is unchanged, because it rests on SEP-2567 rather than on the client.

**Therefore `init` cannot shrink the tool list.** There is one static list; session-inappropriate calls must be **rejected at runtime by BrowserAI**, keyed on the handle's type. Storage tools remain *visible* in every session and are refused at call time. See [Trade-offs](README.md#known-trade-offs) for what this costs.

**Obligations the handle design creates:**

- **Every tool schema gains a required `handle` parameter**, injected by BrowserAI into the raw `inputSchema` of all ~69 tools. Do this on the `JsonElement`; never materialise a typed schema to do it. Keep the injection order-stable — the spec SHOULDs deterministic tool ordering for prompt-cache hit rates.
- **Missing, unknown and expired handles need three distinct, LLM-readable errors.** That text is read by a model deciding what to do next, not by a human tailing a console — the same principle as the current launcher's browser-preflight message, which exists precisely because "the server is stuck" was the wrong conclusion to invite.
- **The first call after a cold start will forget the handle.** Design for it: the error must name `init`, state what it needs, and be recoverable in one turn.
- **Instance lifetime is BrowserAI's to define.** At minimum: explicit teardown, an idle timeout, and **stdin EOF as the backstop** that reaps everything — EOF fires instantly when the parent holding the pipe is `TerminateProcess`d ([measured](kb/windows/processes.md#stdio-exit-codes-and-process-startup)), and the SDK already treats it as shutdown.
- **N children per process.** One BrowserAI now supervises several `node` children, each with its own config, stderr stream and directory locks. **One job object per child, never one shared job** — a shared job fuses every instance's tree together, so tearing down one handle would kill them all, and assigning BrowserAI itself would make it a casualty too. See [the job object contract](#zero-process-leakage-the-job-object-contract). Stderr must be demultiplexed per handle or diagnostics become unreadable at exactly the moment they matter.
- **`browser_get_config` becomes per-handle** — and is the natural per-instance drift check.

#### The session directory is the identity

**One directory is one session, and it is simultaneously the name, the handle and the lock.** Settled 2026-08-15, replacing an earlier design with a central registry, opaque handles and a separate label concept. All three collapsed into the directory.

```
<session-dir>/
  lock.json      <- ours. The only file at the root
  profile/       <- --user-data-dir
  output/        <- --output-dir: screenshots, traces, video, network logs
  downloads/     <- browser downloads
```

Everything except `lock.json` is a subfolder, so the one file that matters is unmissable, and [§F](#f-artifact-management)'s routing gets a home instead of scattering artifacts among Chromium's internals.

**`lock.json` is both the lock and the record.** Held open `FileAccess.ReadWrite, FileShare.Read`: a second BrowserAI requesting write access fails — that is the lock — while any reader can still display who holds it and why. It is rewritten in place on the handle we already own; a reader that catches a torn write retries once. Contents: schema version, mode, browser, purpose and its history, created/last-used timestamps, BrowserAI version, and the holder record — PID, process creation time, client process name. The holder record persists after death on purpose, so a stale lock yields *"held by PID 1234 since 14:02, no longer running — reclaiming"* instead of a bare refusal. `(pid, creationFileTime)` together defeat PID reuse.

**There is no bearer token, and that is deliberate.** An earlier draft minted a 128-bit handle so the `resume` redirect could not be bypassed by guessing a path. It bought less than it cost. Within one BrowserAI process the lock does not isolate callers at all — two subagents share one MCP connection, which is the exact fork case `resume` exists for — and a token would not have stopped the second agent either: it would have called `resume`, read the warning, and proceeded. **The token's entire value was guaranteeing the warning was displayed.** Against that: an opaque token is precisely the state that evaporates when a model is compacted, and an agent that loses its handle cannot drive its own session, whereas a directory path is always reconstructible.

So the guarantee is recovered differently, and better placed: **BrowserAI knows whether this connection created the session.** A caller driving a session it did not `init` gets a notice prepended to its *first* response — *"you are driving a session this connection did not create; opened 2026-08-12, purpose: …; another agent may be using it."* That fires at first use rather than at reclaim time, which is where it matters.

> ⚠️ This holds because BrowserAI is **stdio-only, local, single-user**: another process is blocked by the file lock, another user cannot reach the server. **If BrowserAI ever gains an HTTP transport the handle becomes network-reachable and the token question reopens.** Recorded so a future transport change does not cross that line silently.

**`init` refuses a directory that already has one, including a cleanly closed one.** It fails with an error naming the existing session, its purpose and its mode, and directs the caller to `resume`. Being made to say "resume" is the point: it converts an accidental collision into a stated intent. There is deliberately **no difference between a lost session and a neatly closed one** — both must be resumed, so both behave identically, and the reason a session ended stops being a thing anyone has to model.

**`init` takes** the mode, the browser, a **required** `purpose`, and optional directory and console-level. **`resume` takes only the directory**; mode and browser come from `lock.json` and are refused as arguments, because a profile is browser-specific and a session cannot change what it is. A missing or unparseable `lock.json` is an error, never a guess.

> `purpose` is free text written by one agent and replayed into another's context, which makes it a channel between agents. Cap its length, strip control characters, and frame it explicitly as recorded data — *"purpose recorded by a previous session:"* — so it cannot read as an instruction. Store the facts we get for free alongside it — created, last-used, mode, browser, last origins visited — because *"last used 3 days ago, last on portal.customer.example"* usually answers "what was this" better than prose written three days ago.

#### Lifetime: one timer, and reclaim is forever

**Exactly one timer exists: browser-idle, ~10 minutes, reset by any tool call.** It closes the browser and keeps the node child — measured 329 → 110 MB, and 186 ms to relaunch ([kb: timings](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)). The relaunch is implicit: a caller that navigates after an idle close must never see "browser is closed", and that invisibility is a test, because it is the whole reason the timer is safe.

**No handle-expiry timer, no session TTL, no reclaim window.** A torn-down session stays resumable indefinitely against its recorded directory, because the durable thing is the profile, not the process — measured, a resume after killing the node child preserves cookies, localStorage, IndexedDB, service workers and CacheStorage, losing only `sessionStorage`, in ~515 ms ([kb: timings](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)). Every expiry timer considered was a cliff that deleted work in exchange for nothing: an agent thinking for 61 minutes came back to a dead handle, and the recovery was a `resume` it could have done anyway.

The cost is honest — **directories accumulate forever** — and it is why explicit `list` and `destroy` tools matter more here, not less. Deliberate deletion beats a timer that deletes.

**Teardown** closes stdin, which trips the child's own `setupExitWatchdog` (`stdin` close, `SIGINT`, `SIGTERM` → `gracefullyCloseAll()`, hard exit after 15 s — [kb: upstream config](kb/playwright/configuration.md)). No killing is involved in the normal path. Force is closing the job handle, and only that — see [the job object contract](#zero-process-leakage-the-job-object-contract).

#### Finding sessions without a registry

Three mechanisms, none of which stores state. **That is the property that made the registry a liability and these not:** the registry held handle mappings, config and liveness, so two BrowserAIs could disagree, a stale entry was a bug, and every write needed a machine-wide mutex.

Because [there is no default directory](#the-init-contract), there is no root to scan and **the pointer store is the only inventory.** That makes it load-bearing, so it is designed to fail safe rather than to be correct under every race.

- **One pointer per session directory**, keyed by the SHA-256 of the canonical path, holding just that path. One immutable fact — *a session directory once existed here*. No state, no mapping, no liveness: that is the entire difference from the central registry this replaces, which held all three and therefore needed a mutex on every write and had a bug for every stale entry.
- **Written on every `init` *and* every `resume`, idempotently.** Re-asserting rather than writing-once is what makes a lost pointer self-heal: the cost of losing one is a single sweep cycle of invisibility, not a permanently orphaned directory. This is deliberate — it lets the store skip locking entirely (see [race R7](#race-conditions-and-what-closes-each)).
- **Self-cleaning on sweep.** A pointer whose directory is gone, or whose directory has no readable `lock.json`, is removed. The store therefore shrinks as sessions are destroyed, without anyone maintaining it.
- **The directory proves its own ownership.** Anything the store points at is verified by opening `lock.json` inside it, so the inventory never has to be trusted — only followed. A personal Chrome profile contains no `lock.json` and cannot be mistaken for ours however it was reached.

**Store it in `HKCU\Software\BrowserAI\Sessions` rather than as files.** Both work and the choice is close, but the registry wins on the axis that matters here: `RegSetValueEx` and `RegDeleteValue` are atomic single-value operations designed for concurrent access, whereas enumerating a directory while other processes create and delete entries in it is a race the file system does not promise anything about. Enumeration is also cheaper, and the store cannot be damaged by a disk-cleanup tool that decides an unknown folder is junk. Two things to hold against it: HKCU roams with a roaming profile, so entries can arrive naming paths that never existed on this machine — harmless, because self-cleaning removes them on the first sweep — and it is less pleasant to inspect by hand, which a `list` tool answers better than a folder would. Verify `Microsoft.Win32.Registry` is NativeAOT-clean before committing to it.

#### The stray sweep, and the concurrency it must survive

**Design for ~100 concurrent BrowserAI processes, not for one.** Eight editor windows with a dozen agent sessions each is a normal working day, and every session spawns its own MCP server. Any sweep design that is merely *correct* for a single process is wrong here: 96 processes all sweeping at startup is a thundering herd, and 96 processes racing to kill the same stray is a correctness problem, not a performance one.

**Two triggers, each looking twice.** BrowserAI's own startup is the primary signal — free, no install footprint, and it fires exactly when a stray matters most, because that is when something is about to contend for a lock. A logon scheduled task covers what the first cannot: nobody starts a client for a week while a resurrected browser eats memory. Neither can know when Windows has finished restoring apps and no documented event marks it, so **neither tries to win the race — both simply look more than once**: an immediate pass plus a re-check at ~10 minutes, expressed as Task Scheduler's native repetition rather than an in-process sleep.

> ⚠️ **The scheduled task must run in the user's interactive session, not as a service.** `FindWindowExW(HWND_MESSAGE, …)` is scoped to a window station and desktop, so a task configured *"run whether user is logged on or not"* lands in session 0 and **sees no message windows at all** — it would sweep, find nothing, and report success forever ([kb: object names and window scoping](kb/windows/detection.md#windows-object-names-and-window-scoping)). Configure it *"run only when user is logged on"*, as the user, non-elevated. This is a silent-success failure mode, so it needs a test that would fail if the task definition changed: assert the sweeper finds a browser it launched itself.

**Concurrency: try-acquire-and-skip, never queue.**

- The sweep runs on a **background thread, fire-and-forget, never awaited**, and nothing on the MCP request path waits for it or observes it. It touches the stdout wrapper never and stderr only.
- One machine-wide mutex, `Global\BrowserAI-Sweep`. A process does `WaitOne(0)` — **zero timeout**. If it fails, a sweep is already running, so this one exits immediately rather than queueing. With 96 startups, one sweeps and 95 do nothing but pay a mutex acquire.
- **A skipped sweep is not a missed sweep.** Whoever holds the mutex is scanning the same store this process would have scanned. Retrying would be pure duplication.
- The sweep must never be a startup gate: if the mutex or the store is unavailable, log and continue. A BrowserAI that cannot sweep is degraded; a BrowserAI that will not start is broken.

**Why not the named pipe.** A pipe would let a running sweeper tell a newcomer "already running" — but that is exactly what a zero-timeout mutex already says, at a fraction of the machinery. A pipe adds a server whose death orphans clients, a protocol to version, and a second failure mode where the pipe exists but the sweeper is gone. The only thing a pipe buys is handing back *results*, and a newcomer does not need results — it needs to not duplicate work. Mutex.

**Three lock scopes exist and must not be conflated:**

| Scope | Name | Held for |
|---|---|---|
| Per-directory, guarding create-or-take | `Global\BrowserAI-{sha256(path)[..32]}` | milliseconds |
| Per-session, proving ownership | `lock.json`, `FileShare.Read` | the session's life |
| Machine-wide, guarding the sweep | `Global\BrowserAI-Sweep` | one sweep pass |

##### Race conditions, and what closes each

Every row is a test, not a note. The first three are the ones that lose data or kill the wrong process.

| # | Race | What closes it |
|---|---|---|
| **R1** | **The sweep kills a browser a live session just launched.** Process X sweeps; process Y is mid-`init` on the same directory. | **The sweep may only kill a browser whose directory lock it can itself acquire.** If `lock.json` cannot be opened for write, someone owns the directory — skip, unconditionally. Y-takes-lock-then-launches and Y-launching-then-holding are both covered, and X-holds-while-killing makes Y wait and then launch cleanly. The directory lock is held for the whole kill. |
| **R2** | **PID reuse between detection and kill.** | Capture `(pid, creationFileTime)` at detection and **hold an `OpenProcess` handle from that moment**: Windows will not recycle a PID while a handle is open. Re-verify creation time immediately before `TerminateProcess` regardless. |
| **R3** | **`AbandonedMutexException`.** A sweeper dies holding the mutex; every later acquire throws. | The mutex **is** acquired when that exception is thrown — catch it, treat it as acquired, and proceed. Unhandled, this disables sweeping permanently after the first crash, and nothing reports it. Same handling on the per-directory mutex. |
| **R4** | The scheduled task and BrowserAI use different mutexes. | One name, one place in code, `Global\` prefixed. A `Local\` prefix would silently give per-session mutexes and let two sweeps run. |
| **R5** | Session 0 blindness (above). | Task runs in the interactive session; test asserts it finds a self-launched browser. |
| **R6** | The store is enumerated while an `init` adds an entry. | Benign: a missed entry is a live session, which the sweep would skip anyway, and it is present next pass. |
| **R7** | **The sweep deletes a pointer for a directory an `init` is creating right now.** | Not prevented — **absorbed**. Pointers are re-asserted idempotently on every `init` and `resume`, so a wrongly-deleted pointer costs one cycle of invisibility and is restored on next use. Locking the store to close this would put a machine-wide lock on the hot path of every session start, which is a worse trade at 96 processes. Deletion additionally re-checks absence immediately before acting. |
| **R8** | Two sweeps in different terminal-server sessions. | Correct and intended: message windows are per-session, so each session must sweep its own. The `Global\` mutex serialises them, which costs a little parallelism and prevents nothing valid. |
| **R9** | A sweep runs longer than the 10-minute re-check. | Try-acquire-and-skip means the re-check simply does nothing. No pile-up is possible. |
| **R10** | Killing a stray mid-write corrupts its profile. | Accepted. The profile has no owner by definition (R1), and Chromium is built to survive `taskkill`, which is what upstream itself does. |
| **R11** | An exception in the sweep kills the process. | Catch-all at the thread boundary. A sweep failure is a log line, never a crash and never a protocol error. |
| **R12** | The sweep writes to `stdout`. | Forbidden process-wide already; the sweep is inside that rule, not an exception to it. |

##### Detection: enumerate, then prove ownership

**Settled 2026-08-15 by two independent agents, one briefed to refute it.** Enumeration works: `FindWindowExW(HWND_MESSAGE, prev, "Chrome_MessageWindow", NULL)` walks every such window in 0.43 ms, and plain documented `GetWindowTextW` reads each title in well under a microsecond — it reads the kernel-side window name rather than sending `WM_GETTEXT`, so a hung, suspended or hostile owner cannot defeat it. Full detail and the discriminator measurements are [kb: cross-process title reads](kb/windows/detection.md#cross-process-title-reads--settled-by-two-independent-agents).

**This changes the sweep for the better and the safety story for the worse.**

Better: the sweep no longer needs the inventory at all. It finds strays in directories the pointer store has forgotten — observed live, when one agent's sweep surfaced the other agent's browser in a directory it was never told about. **The sweep and the inventory are now independent**: enumeration answers *what is running*, the pointer store answers *what directories exist*, and neither depends on the other.

Worse, and this is the part to get right:

> ⚠️ **Enumeration hands back strangers' paths.** The earlier claim that the API "cannot return a profile you did not name" is true of the exact-title probe and **false of the enumerating sweep**. Docker Desktop, Discord, Signal, 1Password, Steam, Teams, WhatsApp and ChatGPT all publish real user-data-dirs on that channel. **The ownership test is the entire safety boundary now** — not a refinement on top of a safe primitive.
>
> And the signal is **forgeable**: a plain console app registered the class `Chrome_MessageWindow` (classes are per-process) and published an arbitrary path, indistinguishable from a real Chromium singleton.

##### Detection is documented; attribution may fail, and must fail safe

The window read is **undocumented behaviour of a documented function**: `GetWindowTextW`'s contract says a window with no caption returns a null string, and `Chrome_MessageWindow` has no caption. It works on every Windows since NT and across 1,271 measured windows — but nothing says it must keep working, and a project whose founding complaint is *"it reported healthy while broken"* cannot rest a safety mechanism on that.

So the sweep is split, and **the undocumented part is deliberately not load-bearing**:

**Detection — fully documented, and it decides.** `EnumProcesses` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageNameW`, keeping any process whose **full image path** equals the Chrome for Testing binary BrowserAI provisioned. **Measured 13.88 ms median** for 611 PIDs / 454 opened ([kb: image-path detection](kb/windows/detection.md#process-image-path--the-fully-documented-detection-path)) — trivial on a background thread, and the sweep mutex means one process pays it, not ninety-six.

This is path matching against a binary we installed, not image-name matching, and it does not weaken [that rule](#never-by-image-name): matching one absolute path is the opposite of matching `chrome.exe` wherever it appears.

**It also sees the case the window walk structurally cannot: a browser that fell back to a different profile.** A Chrome that could not open our directory retitles its message window to the *fallback* path, so title-keyed detection loses it entirely — and the tempting fix, extending the matcher to cover fallback paths, is the one that would have it matching a personal Chrome. Image-path detection finds it immediately, because it is still our binary.

> ⚠️ **Finding it is not the same as being able to kill it.** A fallen-back browser cannot be attributed to a session, and it may well *belong* to a live one whose directory turned out to be unusable. So it takes the fail-safe path below — reported, not killed. That is the correct outcome and it is still strictly better than not knowing. **The actual defence remains [validating the directory before launch](README.md#settled-2026-08-15) so the fallback never happens.**

**Attribution — the window title, with a fallback.** Needed only to tie a candidate to a directory, so the [R1](#race-conditions-and-what-closes-each) lock test can run and the report can name the session. `GetWindowTextW` first; on empty, retry `InternalGetWindowText`, which is documented on MS Learn, declared unguarded in the SDK, and does exactly what its documentation says — its caveat is availability, not semantics. Measured zero divergence between them across ~1,550 windows.

**If attribution fails, refuse to kill and report loudly.** This is the property that makes the whole thing acceptable: the undocumented path can only ever cause us to *decline to act and say so*. It can never cause a wrong kill, and it can never cause silence.

**A candidate becomes a stray only when both guards agree:** its image path is our binary, **and** its attributed directory contains our `lock.json` whose lock we can acquire ourselves.

**Two enumeration-specific hazards, both measured:**

- **The title is an untrusted string that we are about to use as a filesystem path.** A `\\host\share` title makes `File.Exists` block for **21 seconds** (measured: 21,037 ms; a dead hostname 22,225 ms). **Reject anything that is not a rooted local drive-letter path before touching the filesystem** — this is the sweep's single largest availability risk and it is closed by a string check.
- **The walk truncates silently.** If the `prev` handle dies between iterations, `FindWindowExW` returns `NULL` with `GetLastError() == 1400`; normal exhaustion returns `NULL` with error 0. Check and restart, or the sweep under-reports **exactly when browsers are exiting**, which is when it is most likely to be running.

`SendMessageTimeoutW(WM_GETTEXT)` must never be used for this: it is the one API a hung or wedged stray can defeat, and it burns a full timeout doing so.

**`destroy` deletes a whole session directory, and refuses anything without a valid `lock.json`.** That refusal is what makes it safe to hand a model a tool that deletes trees: it cannot be aimed at `Documents\`. It tears the browser down first, then deletes under the lock; if the lock is held elsewhere it reports the holder instead. Pair it with a `list` that shows each session's purpose, mode, last-used and **size on disk** — an agent cannot make a good retention decision without knowing it is sitting on 4 GB.

### D. Locking and single-instance

Keyed on the **resolved absolute directory**, not on a repository name. Must handle: stale locks from crashed processes, alive-but-orphaned holders, and PID recycling. The existing launcher's mutex + sibling-lockfile + signature-check pattern solves all three and is worth porting rather than redesigning.

**You cannot put a path in a mutex name.** Backslashes are illegal after the `Global\` prefix — `"Global\C:\Source\..."` throws `DirectoryNotFoundException`. Canonicalise and hash instead: `Path.GetFullPath` → `TrimEnd('\')` → `ToUpperInvariant()` → SHA-256 → hex, then `$"Global\BrowserAI-{hash[..32]}"`. (The real length limit is ~32,000 characters, not the documented 260, but hashing is required regardless.) `Global\` also needs `SeCreateGlobalPrivilege`, which interactive users have and low-integrity/AppContainer processes do not. All three are in [kb: object names and window scoping](kb/windows/detection.md#windows-object-names-and-window-scoping).

Prefer a `FileStream` with `FileShare.None` for the sibling lockfile over the current signature-heuristic approach: the OS releases the handle on process death, so "stale" and "alive" become distinguishable without guessing. Keep the mutex as well — it gives the fast no-IO path.

#### Never by image name

**Killing a user's own `chrome.exe` or `firefox.exe` must be impossible by construction, not merely avoided.** This is a structural rule, not a review item, because a review already passed on code that would have violated it: our own Chromium probes counted and killed by image name — harmless for Chromium on that machine, and it would have killed ~40 personal `firefox.exe` processes if adapted naively ([kb: the legacy setup](kb/history.md#the-legacy-setup-and-this-machine)).

The invariant: **BrowserAI can only terminate a process that belongs to a job object it created, or whose identity it verified against a path it owns.** Two mechanisms, no third:

- **The job object** covers everything spawned in this process's lifetime. Closing the handle terminates exactly its members — no name, no PID list, no filter, so there is nothing to get wrong. A user's browser cannot be a member; it was never assigned.
- **Path-keyed identification** covers anything that outlived us. The match is on *our own* session directory, which by construction cannot name a personal profile in `%LOCALAPPDATA%\Google\Chrome\User Data`.

Forbidden outright, and enforceable as an analyzer at error severity: `Process.GetProcessesByName`, `taskkill /IM`, and any WMI or toolhelp query filtered by executable name. Assert zero occurrences in the tree.

> ⚠️ `--user-data-dir` alone is **not** an ownership signal. Measured on the maintainer's machine 2026-08-15: Discord, VS Code, Signal, Teams, WhatsApp, Steam, ChatGPT and four `msedgewebview2.exe` processes all pass it. Only an exact match against a directory BrowserAI created is safe.

### E. Lifecycle and observability

Non-negotiable, because every bug this month was a visibility failure. The .NET MCP SDK already implements roughly half of this — `StandardErrorLines` wired before `Start()`, a rolling stderr tail, and a `StdioClientCompletionDetails { ProcessId, ExitCode, StandardErrorTail }` type that makes bug #2 in [the README's opening table](README.md#read-this-before-designing-anything) structurally impossible ([kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)).

- **Capture the child's stderr from before it starts.** `RedirectStandardError` + `ErrorDataReceived` + `BeginErrorReadLine()`. The anonymous pipe exists before `CreateProcess` and the kernel buffers, so nothing written earlier is lost ([measured](kb/windows/processes.md#stdio-exit-codes-and-process-startup): 5 lines survived a 3 s delay *and* child exit). The real risk is the opposite — a full pipe blocks the child.
- **Record the child's real exit code and cache it as an `int` immediately.** .NET is *worse* than PowerShell here, not better: `Process.ExitCode` **throws** after `Dispose()`, and `Process.GetProcessById(pid).ExitCode` **always** throws. Microsoft's own SDK carries a `beforeDispose` callback commented "to read ExitCode before Dispose() invalidates it" — they hit this too ([kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup)).
- **Use `await WaitForExitAsync(ct)`**, never `WaitForExit(int)`. Only the former drains the async readers.
- **Distinguish error-shaped stderr from benign output.** Port the two regexes verbatim; a healthy start prints `Session: <path>` every time.
- **Kill the descendant tree with a Windows Job Object**, not `Process.Kill(entireProcessTree: true)`. The latter requires BrowserAI to be alive and running code — which is exactly the case that fails. The full contract, measured end to end, is [below](#zero-process-leakage-the-job-object-contract).
- **Identify a process by `(pid, creationFileTime)`** — never by a bare PID, which identifies nothing once PIDs recycle, and never by image name (see [§D](#never-by-image-name)). Better still, hold an `OpenProcess` handle: Windows will not recycle a PID while a handle is open, and the handle is signalled on exit, so liveness becomes event-driven instead of a poll loop.
- **There are no orphaned processes to collect after a crash.** The job object takes the whole tree with it, so a dead BrowserAI leaves no running children — an earlier draft of this document said the opposite. What *can* survive is a stale lockfile, and that is a file problem solved by `FileShare.None` plus the holder record, not a process problem.

**stdout is the protocol channel and it is wrong by default.** Measured: `Console.Out` writes CP437, not UTF-8 (`é` → `0x82`); `Console.InputEncoding` also defaults to CP437; any `TextWriter` emits CRLF; and a hand-rolled `new StreamWriter(stream, Encoding.UTF8)` emits a BOM ([kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup)). Own the raw streams, never touch `Console.Out`, and let no code anywhere in the process call `Console.WriteLine` — including inside a `catch`. This should be a reviewed invariant owned by one wrapper type, not a convention.

#### Zero process leakage: the job object contract

Verified end to end 2026-08-15 against real Chromium and Firefox trees: **16 runs, 106 spawned processes, 0 escapees, 0 survivors.** The measurement harness lives at `.work/jobtest/` and is the working prototype of the acceptance test below. Full provenance: [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment).

**The rule, and why the intuition runs backwards.** On Windows, job membership is inherited *automatically* by every descendant created with `CreateProcess`. A component that spawns children "the normal way" is precisely the case that works. Escaping requires an explicit opt-in **that our job must grant** — and when a process requests `CREATE_BREAKAWAY_FROM_JOB` from a job that does not permit it, `CreateProcess` **fails with `ERROR_ACCESS_DENIED` rather than escaping**. A job that grants no breakaway flags therefore converts every escape attempt into a launch failure. This is the inverse of Linux process-group semantics; do not reason about it by analogy, and do not assume a component that "just spawns normally" is a hole.

Nested jobs make the chain safer, not weaker. Per [Nested Jobs](https://learn.microsoft.com/windows/win32/procthread/nested-jobs), a breakaway "moves up the hierarchy until it reaches a job that does not permit breakaway" — so a nested job that permits *silent* breakaway still cannot launder a process out of ours. That matters because the production chain already contains one: **libuv creates a global job with `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`** and assigns every non-detached child to it, and Playwright spawns the browser with `detached: false` on Windows (`coreBundle.js`). Firefox's launcher process stacks a second such job. Containment held through both — the strongest available confirmation, because that is the exact configuration that would leak if our job were misconfigured.

Neither browser fights us. Every Chromium caller of `CREATE_BREAKAWAY_FROM_JOB` is installer, updater or remote-desktop code; no renderer, GPU, utility, network-service or crashpad path requests it. Firefox's `NeedToBreakAwayFromJob()` returns false unless the job carries **both** `KILL_ON_JOB_CLOSE` and `BREAKAWAY_OK` — ours carries only the first, so Firefox checks and declines.

**Must do:**

1. Create the job with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` via `SetInformationJobObject(JobObjectExtendedLimitInformation)`, **and nothing else**.
2. **Create the job handle non-inheritable** (`NULL` security attributes) and never duplicate it into the child. Measured fatal otherwise: with an inherited handle, BrowserAI's death no longer closes the *last* handle, `KILL_ON_JOB_CLOSE` never fires, and **every child survived**. Redirecting stdio forces `bInheritHandles=TRUE`, so this is one flag away at all times.
3. **Assign at creation** with `PROC_THREAD_ATTRIBUTE_JOB_LIST` + `EXTENDED_STARTUPINFO_PRESENT` on `CreateProcessW`. Membership becomes part of process creation, so the race window does not exist rather than being closed after the fact. Windows 10+, which matches the floor. Preferred over `CREATE_SUSPENDED` → assign → `ResumeThread`, which also works but leaks a suspended process if BrowserAI dies mid-sequence.
4. **One job per instance, created fresh at each spawn.** Never assign BrowserAI itself to a job: with N instances in one process that fuses every tree together and makes BrowserAI a casualty of any single teardown.
5. **Check every return value and fail loudly** — `CreateJobObject`, `SetInformationJobObject`, `CreateProcessW`, and `AssignProcessToJobObject` if ever used as a fallback. libuv swallows `ERROR_ACCESS_DENIED` from this call; BrowserAI must not. A swallowed failure here is exactly the reported-healthy-while-broken class this project exists to eliminate.
6. **Hold the job handle for the instance's whole life** and let process death close it. Never `CloseHandle` it early.

**Must never do:**

1. **Never `JOB_OBJECT_LIMIT_BREAKAWAY_OK` or `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`.** Either turns the guarantee into a suggestion, and `BREAKAWAY_OK` specifically flips Firefox's `NeedToBreakAwayFromJob()` to true — actively arming an escape that is otherwise disarmed.
2. **Never `JobObjectBasicUIRestrictions`.** Jobs nest only if neither sets UI limits; setting them can stop Chromium's sandbox job from nesting inside ours.
3. **Never `Process.Start` followed by `AssignProcessToJobObject`.** Measured: 2 escapees, because the child spawns grandchildren before the assign lands. `.NET` cannot express the correct pattern — `ProcessStartInfo` has no creation-flags surface and .NET exposes no job-object API — so a P/Invoke is mandatory, not a preference.
4. **Never `Process.Kill(entireProcessTree: true)`.** It walks parent-child links, which are re-parentable and PID-reusable. The job is neither.
5. **Never rely on libuv's job as the containment mechanism.** It is a happy accident of Playwright's `detached: false` on Windows and could change in any release — exactly the floating-dependency risk the test suite exists to catch. Useful as a second, independent kill path; never as the first.

**Expected side effect, not a defect.** Firefox's background tasks (`BackgroundTasksRunner`) and its crash reporter request breakaway, so inside our job their `CreateProcess` fails with `ERROR_ACCESS_DENIED`. That is the correct trade — a failed helper launch beats an escaped `firefox.exe --backgroundtask`. If Firefox ever logs a failed background task, this is why, and it is not a bug to fix.

**NativeAOT.** Use `[LibraryImport]`, not `DllImport` — the latter relies on runtime IL-stub generation. `LibraryImport` does not support `StringBuilder`, so pass the command line as a writable `char[]`/`Span<char>`: `CreateProcessW` mutates the buffer, so a `string` literal is not valid ([kb: interop](kb/windows/processes.md#interop-and-the-toolchain)). Keep `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` and `STARTUPINFOEX` blittable and consider `DisableRuntimeMarshalling`. Nothing here touches AOT's limitation list — no dynamic loading, no `Reflection.Emit`, no built-in COM.

**BrowserAI running inside someone else's job is safe**, measured in all three ancestor configurations: an outer job with `KILL_ON_JOB_CLOSE` only (a CI runner), one that also permits breakaway, and one with `SILENT_BREAKAWAY_OK` — which is the realistic case, since any MCP client that spawns BrowserAI through Node `child_process` puts us in libuv's job.

**The acceptance test.** Launch the child, enumerate via `QueryInformationJobObject(JobObjectBasicProcessIdList)`, assert `IsProcessInJob` is true for every PID in the descendant tree, hard-kill the launcher from outside, assert every PID is gone and every profile directory can be deleted — a directory that still holds a lock proves an escaped browser. Cross-check the job's PID list against a toolhelp descendant walk seeded from an I/O completion port on the job, so a process whose parent already exited is not missed. **Never match, count or terminate by image name at any step**; act only on PIDs recorded at spawn, validated against recorded creation time.

### F. Artifact management

Playwright writes every artifact flat into one directory with a generated name, mixing machine churn with hand-named work. Nine fixed generator prefixes make classification exact rather than heuristic: `console`, `download`, `network`, `page`, `request`, `response`, `result`, `storage-state`, `video` ([kb: artifacts](kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)).

Port the prefix-based sort. **Classification must be by generator prefix, never by date** — that is precisely what keeps a hand-named file out of the machine-generated folders, and no date rule can make that distinction.

### Route on the way in, do not sort on the way out

A sort is cleanup; a proxy can do better. BrowserAI sees every `tools/call` before the child does and every result before the caller does, so **files can be born in the right place instead of being swept there.** Three levers, in increasing order of effort:

1. **Set the child's `WorkingDirectory` to the instance's output root.** Already required by [§Windows process spawning](README.md#windows-process-spawning) for a different reason. It makes the stray-file failure *impossible* rather than *caught*: a bare `foo.png` resolves inside the instance tree by construction. Ten repositories currently run a `deny` hook because upstream resolves a relative `filename` against the child's cwd ([kb: artifacts](kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) — this closes that without a hook.
2. **Normalise `filename` on the way in.** Route it to the typed subfolder its generator prefix implies, so the agent never has to construct a path.
3. **Return the resolved absolute path on the way out.** Non-negotiable if lever 2 is used. Silently relocating a file while telling the model it went somewhere else produces an agent that confidently reports the wrong location — a new silent failure introduced by the fix for an old one.

**There is no default root, because there is no default.** `init` requires an explicit session directory and rejects an empty or invalid one — see [the `init` contract](#the-init-contract). An earlier draft defaulted to `%LocalAppData%\BrowserAI\sessions\<label>\` when given no path; that is now an error instead. The founding stray-file problem was files landing in repo roots *because nobody chose where they should go*, and a safe default answers the symptom while preserving the cause. Making the caller name the location is what actually removes it.

Layout beneath the root, following the nine generator prefixes rather than inventing new ones:

```
<root>\<label>\
  screenshots\   video\      traces\
  network\       console\    downloads\
  storage\       results\
```

**`downloads\` is the one exception to routing** — a browser-initiated download lands where the browser puts it, not where a `filename` argument says. It is classified after the fact like the old sort, and that difference should be visible in the code rather than discovered.

### Three obligations that follow

- **Names must be legible.** Upstream generates `page-2026-08-14T04-11-50-882Z.png` ([kb: artifacts](kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)). Prefer the caller's own name where one was given, and a page-derived slug plus a counter where none was — `checkout-step-3.png` survives a month, a timestamp does not. This is what made 346 session directories unnavigable.
- **Never overwrite silently.** Two screenshots named `login.png` in one session is data loss. Suffix, and say so in the result.
- **Report cumulative session size in the result.** The current setup reached 1.5 GB in three months with nothing saying so. BrowserAI routes every file and therefore knows; not reporting it is a choice to stay blind.
- **Return a repository-relative path alongside the absolute one.** When an agent writes a commit message, a PR body or a report, `docs/screenshots/login.png` is what it needs; an absolute path is machine-specific and useless there. BrowserAI resolves both anyway — emitting only one of them discards work already done.

### The session index

Routing means BrowserAI knows, at write time, every fact worth recording: which tool produced a file, when, at which URL, under which handle and session type. Not writing that down throws away information that cannot be reconstructed afterwards — which is exactly how 346 session directories became untriageable. **The label tells you what a session was; the index tells you what is in it.**

Write `session.json` into each session folder as artifacts are routed: label, handle, session type, created and last-touched timestamps, resolved absolute paths, and one entry per artifact with its tool, timestamp, page URL, size and both path forms.

**Scope the roll-up by root, never by machine.** BrowserAI is registered once and serves every repository on the host, so an index that aggregates everything would pull sessions from unrelated projects into whatever context happens to be open. That is a **noise problem rather than a security boundary** — the paths were the caller's own choice and [§settled](README.md#settled-2026-08-13) accepts any of them — but noise in an agent's context is a real cost, and the cheap fix is to default the aggregate to the root in play:

- A roll-up index sits at each **output root**, covering sessions beneath that root only.
- `init`'s result names prior sessions under the same root — count and labels, nothing wider.
- A machine-wide view stays available and is **opt-in**: an explicit request for everything, or for a root that happens to contain everything, returns everything. Scoping is a default, not a restriction.

**No new tool is needed for any of this, and none should be added.** The index is a file; the calling agent reads it with its own filesystem tools. A `browser_list_artifacts` tool would be BrowserAI composing a capability out of its own state — the boundary this document holds everywhere else, and there is no reason to cross it for something `Read` already does.

### Retention is no longer ours alone to promise

An earlier draft of this section said *"Nothing is ever auto-deleted."* That is **no longer true of the runtime**: `@playwright/mcp` now carries `--output-max-size <bytes>`, *"Threshold for evicting old output files, in bytes."* Unless BrowserAI asserts it stays unset — and strips `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`, which is the other door to it — the promise in this document is not the promise the child keeps, and a silently evicted artifact is precisely the failure class in [the README's opening table](README.md#read-this-before-designing-anything). **Settled 2026-08-15: it has no default at any merge stage** (`defaultConfig` carries only `browser` and `timeouts`; `mergeConfig` filters through `pickDefined`, which drops `undefined`), so eviction is off unless someone turns it on. [kb: upstream config](kb/playwright/configuration.md).

### G. Updates

> **Re-verified against Velopack 1.2.0 on 2026-08-15 by a real install → update → rollback → uninstall cycle.** Of the nine landmines below, **four are still real, three no longer apply, one was wrong for 1.2.0, and one is partly fixed** — and three new ones were found, including a defect that hangs `Setup.exe` forever. The section text below is the pre-verification record; **[kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) carries each claim with the verdict against it and is authoritative** where the two disagree. Corrections that change what we build:
>
> - ⚠️ **`Setup.exe` must never be re-run over an existing install.** A repair install renames the root aside and **deletes it** — a 203.8 MB re-download. Updates go through the update path only.
> - ⚠️ **`Setup.exe -- <args>` panics and never exits**, installing nothing. Never pass start arguments to the installer.
> - ⚠️ **`force_stop_package` kills by image path under the root, after every hook returns** — so a Velopack update terminates every running browser without our teardown, bypassing the job object. Chromium survives it and our locks release on process death, so this costs a session rather than a profile.
> - **Target `net10.0-windows`**, not `net10.0`: the hook callbacks are `[SupportedOSPlatform("windows")]`.
> - **`UpdateExe.Start(waitPid)` in this section does not compile** — the first positional parameter is the locator.
> - The `-beta`-suffix rationale for setting the channel explicitly is **wrong** (a suffix has no effect on channel derivation). Set it anyway, for a better reason: a client installed from a beta `Setup.exe` inherits `beta` silently. Also `ExplicitChannel = ""` yields `releases..json` → 404, and `vpk` lowercases the channel while the client does not.
> - **Browsers beside `current\` survive update and rollback** — the arrangement §A depends on, now confirmed rather than assumed.

The reason the project exists. Requirements: updates published on **our** schedule after we have validated a new Playwright version; one release updating runtime, browser, config, opinions and BrowserAI itself; rollback to a known-good release; and silent background update with a clear "restart to apply" signal, since MCP servers are long-lived child processes.

**Mechanism: Velopack 1.2.0** (MIT, per-user install to `%LocalAppData%`, no elevation, no commercial tier). Everything below that was read out of Velopack itself — the delta scheme, the feed-URL composition, the startup and stub behaviours, the Rust binaries' own Windows floor — is recorded with provenance in [kb: Velopack](kb/packaging/velopack.md).

It wins on delta granularity. Its scheme is per-file zstd `--patch-from`, and unchanged files collapse to **zero-byte markers** — so against a ~380 MB footprint dominated by Chromium, a BrowserAI-only release ships single-digit MB. It is also the only option with both a **stable executable path** (`current\`, a real directory, not a junction) *and* a stage-now/swap-later primitive that composes with a parent-owned process lifecycle.

Go **per-user, not per-machine**. The `--msi PerMachine` layout installs to `Program Files`, which makes the updater self-elevate — and a UAC prompt cannot be answered by a background MCP server. Per-user also matches how MCP registrations and browser profiles already work. Ship the MSI as a *second* artifact for IT deployment if a colleague ever needs it, never as the update path.

**One track.** No beta channel. A second track doubles the release matrix and makes the version string load-bearing — UCC derives its runtime track by testing the version for a `-beta` suffix, so a formatting change silently breaks track detection. Single-track does **not** mean the channel can be ignored: Velopack still stamps one into the manifest and package names (`releases.win.json`, `BrowserAI-1.0.0-win-full.nupkg`), `vpk pack --channel` still sets it, and landmine 1 is entirely about how that channel reaches the client.

Landmines, in descending order of blast radius. All of them fail silently:

1. **Never put the channel in the feed URL.** `SimpleWebSource` composes the request as `{BaseUrl}/releases.{channel}.json`. Build the base URL as `{BaseUrl}/{channel}` and Velopack fetches `{BaseUrl}/{channel}/releases.{channel}.json` — a 404, surfaced to the user as *"no update available"* and nothing else. Set the channel through `UpdateOptions.ExplicitChannel`, never in the URL path. This is the worst hazard in the section because **it is unrecoverable in the field**: a client that cannot reach the feed cannot be told to roll back either, so every install already shipped needs a manual reinstall. Rollback does not cover it — rollback assumes the feed works. It follows that the update test must resolve the **real feed URL**; a local-directory source composes paths differently and will pass where production 404s.
2. **`SetAutoApplyOnStartup(false)` is mandatory.** The default is `true`: on finding a staged package, `VelopackApp.Run()` applies it, exits(0), and relaunches — with no inherited stdio. Claude Code sees its MCP server exit at handshake time.
3. **Register `%LocalAppData%\BrowserAI\current\BrowserAI.exe` directly**, never the execution stub. The stub is compiled `#![windows_subsystem = "windows"]` and returns immediately without waiting, so a stdio client sees the child die instantly with no pipes attached.
4. **`force_stop_package` kills every process under the install root** without asking. With four registrations that is three other live sessions destroyed mid-task. Gate the apply on "am I the last instance" using the same directory-keyed lock from §D, then spawn `Update.exe apply --silent --norestart --waitPid <ownPid>` and exit. The next session starts the new version from the identical path — in normal use there is no "restart to apply" prompt at all.
5. **Reading the installed version must not touch the network.** `UpdateManager` is network-capable, so constructing one merely to read the current version issues a request. `VelopackLocator` reads local metadata only. For BrowserAI this sits on the stdio startup path, where a stray network call is a handshake delay at best and a hang behind a captive portal at worst.
6. **`NotInstalledException` is the normal outcome under `dotnet run` and every test host.** Neither is a Velopack install, so every Velopack call throws. Do not guard on `Debugger.IsAttached` — a test runner attaches no debugger. Put an injectable seam around every Velopack call from day one; without it a server that self-restarts relaunches itself out of the test suite.
7. **Do not call `ApplyUpdatesAndRestart(null)` to mean "just restart".** It works — the internals skip the `--package` argument when there is no local full package — but the behaviour is undocumented and rests on an implementation detail. `UpdateExe.Start(waitPid)` is the supported restart.
8. **`IVelopackLogger` must be bridged on both paths.** The runtime `UpdateManager` and the `VelopackApp.Build()` startup hooks take *separate* logger registrations. Bridge only the first and the installer, first-run and post-restart hooks are silent — which is the path that runs precisely when something has already gone wrong. §E applies to the updater too.
9. **Velopack's Windows floor is separate from BrowserAI's.** Two layers: the .NET SDK's minimum, and the Rust `Setup.exe`/`Update.exe` minimum. The Rust binaries can fail *before* the managed app exists — before 0.0.530 they statically linked `IsWow64Process2` and crashed below Windows 10 1709. Setting `--runtime win7` does not help if the installer binary itself cannot run.

**`Update.exe` is also the restart coordinator, not only the update applier.** It is a separate Rust binary that outlives BrowserAI, so it can wait for full termination before launching the replacement — which is what guarantees the §D directory-keyed lock is released first. The two races it removes are both real: spawning before exit means the new instance finds the lock held and does the wrong thing; releasing the lock before spawning opens a window in which an unrelated launch takes primacy. Use `ApplyUpdatesAndRestart` when a package is staged and `UpdateExe.Start(waitPid)` when one is not — never one for both.

**A download needs three independent timers.** An absolute cap, a stall timeout reset on every progress callback, and an outer deadline that is a crash tripwire rather than flow control — all linked to the process lifetime token. A single timeout either aborts a healthy slow link or hangs forever on a stalled one. The check and download must also run off the message loop: a `tools/call` has to stay answerable while a ~105 MB full package is in flight.

Rollback needs hand-rolling: Velopack prunes `packages\` down to the current full `.nupkg` and deltas are forward-only, so archive each full package yourself or every rollback is a fresh ~105 MB download. **Two halves have to agree.** On the client, `AllowVersionDowngrade` is what makes an older version acceptable — it *is* the rollback mechanism and must be on. On the pipeline, a release-validation rule enforcing strictly increasing versions makes a rollback impossible to publish. Write that rule as "monotonic **or** an explicit rollback republish" or the runtime will accept a rollback the build refuses to emit — which is exactly the state UCC is in.

State must live **outside** `current\`, which is wholly replaced on update — resolve every path from `VelopackLocator.Current.RootAppDir`. The trap is `AppContext.BaseDirectory`: it reads as "next to the binary", which is precisely where logs and caches must not go, and any retention policy written against it is silently reset by every update. Nor should update state be persisted alongside the binary — **derive it from the installed version instead.** Anything stored out-of-band desyncs across an exit-and-relaunch: a config edited between stage and swap, a save that never completed, a binary replaced by hand.

**MSIX is disqualified on evidence, not theory.** Two production AI tools hit exactly this in 2026: claude-code [#63397](https://github.com/anthropics/claude-code/issues/63397) (`0x80073D02` / `ERROR_SHARING_VIOLATION`, and the report names the cause — "Claude Code runs as a child process of Claude Desktop") and openai/codex [#25770](https://github.com/openai/codex/issues/25770). A package cannot re-register while any process in its family is running, and BrowserAI's entire design is to be a long-lived child. Hydraulic Conveyor emits MSIX on Windows and inherits the same failure.

#### Prior art: ExoFabric/UCC

Landmines 1 and 5–9 above were not found in documentation. They were found in a working Velopack deployment: **`ExoFabric/UCC`** ships Velopack today — per-user `%LocalAppData%\UCC\current\`, no elevation, S3-compatible feed, silent background check — and is the only in-house evidence that exists. Its state as observed is in [kb: UCC prior art](kb/packaging/velopack.md#prior-art-exofabricucc). Two caveats before borrowing anything from it:

- **It runs Velopack 0.0.1298, not 1.2.0.** That is the pre-1.0 line; behaviour and API surface have both moved. Re-verify every claim against 1.2.0 and stamp it with the `Verified <date> @ <version>` convention when you do.
- **UCC is single-instance; BrowserAI is not.** A named mutex means exactly one UCC process ever runs under the install root, which makes `force_stop_package` harmless there. Landmine 4 — the one that matters most for four concurrent registrations — is therefore **untested by the only prior art available.**

What UCC proves, and what it does not:

| Area | State in UCC | Implication for BrowserAI |
|---|---|---|
| Per-user install, no elevation, `current\` swap | Works, in production | The §G layout is not theoretical |
| **Delta packages** | **Never produced.** Every shipped artifact is a full `.nupkg`; delta validation is still an open TODO | Delta granularity is the stated reason Velopack won §G. It is unproven in-house — make it a release gate, not an assumption |
| Feed URL composition | Bricked auto-update for three shipped versions, manual reinstall only | Landmine 1, from the field rather than from theory |
| `SetAutoApplyOnStartup(false)` | Never called; runs the default | Landmine 2 is live in a shipping app. Survivable for a foreground tray app, fatal for a stdio child |
| Rollback | No code, no doc. The client would accept one; the version-validation script refuses to emit one | Build both halves together or neither works |
| Logs | Written to `AppContext.BaseDirectory` — inside `current\`, with 10-day retention | Wiped by every update; the retention can never apply |
| Restart choreography | Cooperative shutdown with per-component acks, a 10 s hard-kill backstop, log flush, *then* apply | Worth copying wholesale. The apply message has to be allowed through during exit, which needs deliberate handling |
| Test seam | Update wrapper unsealed with `virtual` network methods; 48 hermetic tests over the agent driving it | Copy this. It is the reason UCC can test the update path at all |
| Coverage of the wrapper itself | **Zero tests** | And that is exactly where the feed-URL bug lived |
| Signing | None — no certificate, no `--signParams`, package signature verification unexplored | Decide before first colleague handoff; see the SmartScreen hazard |

The wider point: UCC's update path has been in production for multiple releases, and **five of the nine landmines above are ones it hit rather than avoided** — 1, 2, 5, 6 and 8, of which 2 and 8 are still live. None of them announced itself. That is the same failure class as everything in [the README's opening table](README.md#read-this-before-designing-anything), and it is why §G is enumerated rather than described.

---

## Implementation stack

Verified 2026-08-13. **The versions below are provenance stamps, not targets** — see [Versioning policy](README.md#versioning-policy-everything-floats-the-build-freezes-it). The build resolves the latest of each; these record what was current when the surrounding claims were checked, and carry the same convention as `playwright/README.md`: re-verify on every bump. Each lookup, with the date it was made, is in [kb: package provenance](kb/packaging/dependencies.md#package-provenance-as-looked-up).

| Concern | Choice | Notes |
|---|---|---|
| MCP protocol | `ModelContextProtocol` (latest; **2.2.0** as of 2026-08-13) | Apache-2.0, 23.6M downloads. **Tier 1** SDK under the MCP project — which Anthropic donated to the Linux Foundation's **Agentic AI Foundation** on 2025-12-09, so "official" now means LF-governed, with day-to-day engineering by the Microsoft .NET team. Began as `PederHP/mcpdotnet` (now archived). Full `2026-07-28`. The main package's hosting dependency is abstractions-only — it does **not** drag in ASP.NET. `ModelContextProtocol.Core` alone is a viable smaller surface (`McpServer.Create` + `StdioServerTransport`, and the `[McpServerTool]` attributes already live there) at the cost of `AddMcpServer()` and assembly scanning — not worth it unless the hosting stack becomes a problem. Verified 2026-08-14. |
| Updates | **Velopack 1.2.0** + `vpk` 1.2.0 | MIT. See §G. |
| Node runtime | **v24.19.0 LTS**, `node.exe` only | v26 is Current, not LTS, and its `node.exe` is 10 MB larger. |
| Job objects | Hand-rolled `[LibraryImport]` | No credible NuGet wrapper exists — the candidates have <6K downloads and the newest was published in 2017. `dotnet/runtime` [#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in support and was closed as not planned. ~60 lines. `Microsoft.Windows.CsWin32` is the reasonable alternative once a seventh Win32 API is needed. |
| Parent PID | `NtQueryInformationProcess` | ~0.77 µs/call vs ~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. This is what `dotnet/runtime` itself uses. See [kb: interop](kb/windows/processes.md#interop-and-the-toolchain). |
| Tests | **TUnit** (latest; 1.65.0 as of 2026-08-13) | MIT, source-generated, reflection-free, **MTP-native**. Matches `SixFive7/Jeeves`. 1.0 shipped 2025-11-05; ~623K downloads/mo and growing 2.24× YoY. Chosen over xUnit v3 because [we do not vendor the SDK's fixtures](#we-write-our-own-harness) — that was xUnit's only argument here. |
| Snapshots | `Verify.TUnit` (latest; **31.28.0** as of 2026-07-31) | Exact parity with `Verify.XunitV3` — same monorepo, same release, and Verify's own repo carries *more* test projects for the TUnit integration than the xUnit v3 one. |
| Assertions | TUnit built-ins | `await Assert.That(actual).IsEqualTo(expected)`. **Never add FluentAssertions** — it relicensed at exactly 8.0.0 to a bespoke non-SPDX licence with a commercial tier. Jeeves carries the identical prohibition for the identical reason. |
| External smoke | `@modelcontextprotocol/inspector` **2.2.0** | Language-independent CI check. Exit code **5** means the tool reported `isError` — the signal `claude mcp` does not give you. |

### Nine places where the SDK must be deviated from

> Was three. Six more were found by measurement on 2026-08-15, and **two of the original three were wrong in detail** — corrections are marked inline. A spike drove the real child through a published NativeAOT binary; everything below is observed rather than read.

**1. Write your own `IClientTransport`.** The SDK's `StdioClientTransport` prepends `cmd.exe /c` to every non-cmd command on Windows, unconditionally. That directly contradicts [§Windows process spawning](README.md#windows-process-spawning): it adds a shell layer, an extra process between BrowserAI and `node` (complicating tree ownership and exit-code attribution), and cmd.exe quoting semantics. The interface is two members (`Name`, `ConnectAsync`) and the replacement is ~120 lines. Port `StdioClientTransportOptions`' stderr and shutdown handling rather than reinventing it.

**2. Use the raw `ListToolsAsync` overload.** The convenience overload `ListToolsAsync(RequestOptions?, ct)` **silently drops** any tool whose `x-mcp-header` annotations fail SEP-2243 validation. A proxy must call `ListToolsAsync(ListToolsRequestParams, ct)`, which returns the server's result unfiltered. Using the wrong one shrinks the exposed surface with no error anywhere — the same failure class as everything in [the README's opening table](README.md#read-this-before-designing-anything).

**3. Proxy `tools/call` through `McpServerOptions.Filters.Message.IncomingFilters`, short-circuiting rather than calling `next`.** The `ContentBlock` converter **silently drops unknown properties** and **throws on unknown content *types***, failing the whole call at deserialization before any BrowserAI code runs. The filter sees `JsonRpcResponse.Result` as a raw `JsonNode?` and never touches `ContentBlock`.

> ⚠️ **Not `WithMessageFilters`.** That is a DI extension in the *hosting* package. A Core/AOT proxy uses `McpServerOptions.Filters.Message.IncomingFilters` / `OutgoingFilters` directly. Charter corrected 2026-08-15.

**4. Rewrite `tools/list` on `JsonNode` too — not typed.** An earlier draft of this section said "rewrite `tools/list` typed"; that is wrong. A typed `ListToolsResult` round-trip **silently discards unknown top-level tool members**, because `Tool` carries no `[JsonExtensionData]`. Schema keywords survive (`inputSchema` is a `JsonElement`), tool-level extensions do not. Measured both ways in the same run.

**5. Write our own *server*-side transport as well.** `StreamServerTransport` hard-codes `McpJsonUtilities.JsonContext` with no options seam, so every outgoing string is re-escaped by `JavaScriptEncoder.Default`. Decoded values are unchanged, but the bytes are not — every backtick, apostrophe, angle bracket and non-ASCII character becomes a `\uXXXX` escape. Measured on a real `browser_navigate` result, and on a unicode case that grew **154 → 218 bytes**. **Settled 2026-08-15: build the transport** and serialize with `UnsafeRelaxedJsonEscaping`. Two reasons, and the second is the stronger: passthrough becomes genuinely byte-exact, and the inflation this removes is **tokens in the model's context on every result**. The cost is small because it is the same `TransportBase` pattern deviation 1 already requires. ("Unsafe" refers to embedding JSON in HTML; we write to a pipe consumed by a JSON parser.)

**6. Cancellation does not work at all — hand-roll it.** Measured 2026-08-15, and isolated away from the proxy entirely: a plain `McpClient` over a plain transport, cancelling both the raw and typed call paths, **never emits `notifications/cancelled` downstream**. The machinery exists in `McpSessionHandler.SendRequestAsync`, but its registration is disposed as `tcs.Task.WaitAsync(ct)` unwinds — CTS callbacks run LIFO, so `WaitAsync`'s callback wins and the notification callback is cancelled before it can run. **Remedy, proven in the same run:** assign `JsonRpcRequest.Id` yourself (it survives to the wire verbatim) and send `notifications/cancelled` by hand from your own `ct.Register`. Without this, a cancelled `browser_navigate` leaves the child working.

**7. `McpClientOptions` has no `Filters`.** Every filter API is server-side, so observing and forwarding *child→caller* notifications needs an `ITransport` decorator (~30 lines). The caller→child direction is covered by `IncomingFilters`.

**8. JSON-RPC errors are lossy above the transport.** `code` and `data` survive, but the message is prefixed — `"upstream exploded"` arrives as `"Request failed (remote): upstream exploded"`, and `data` is destructured into `Exception.Data`. Reconstruct from `McpProtocolException` and strip the prefix, or forward a message that is not upstream's.

**9. Answer the `server/discover` probe.** A child that ignores it costs the full `DiscoverProbeTimeout` **per connect** — the spike burned 30 s per rig against a fake child until it returned `-32601`. Real `@playwright/mcp` 0.0.79 handles it, so this is a hazard for our own test doubles rather than for production.

These are places where the SDK's design goal (a forward-compatible *client*) and BrowserAI's (a lossless *proxy*) genuinely differ. **Every one of the failure modes above is silent** — dropped tools, dropped members, a cancellation that never arrives — which is the class this project exists to eliminate. All of it is measured, not inferred: see [kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around).

> **NativeAOT is proven, not assumed.** `PublishAot=true`, win-x64, self-contained: **zero trim/AOT warnings, no `JsonSerializerContext` of our own required, 9.76 MiB binary.** The published binary drove a real `@playwright/mcp` child over stdio — 24 tools through the proxy, `handle` injected into every schema, `browser_navigate` returning a non-error result, child PID gone after dispose. One AOT trap, in *our* code rather than the SDK: `JsonArray.Add(x)` binds to the generic overload, which is `RequiresDynamicCode`; cast to `(JsonNode)` to clear it.

---

## Testing: a hard requirement, and the release gate

> **This section is a requirement, not an aspiration, and it is not severable from the [versioning policy](README.md#versioning-policy-everything-floats-the-build-freezes-it).**
>
> [Versioning policy](README.md#versioning-policy-everything-floats-the-build-freezes-it) puts every dependency on latest at build time. That makes the suite **the only thing standing between an upstream change and a shipped regression** — floating without a suite that can catch a breaking change is strictly worse than pinning. The two decisions are one decision: neither is valid alone, and weakening the suite silently converts the versioning policy into a liability.
>
> Three rules follow, and none of them are negotiable per-release:
>
> - **No release is cut with a red test.** Not "a known failure", not "unrelated to this change", not "it passes locally".
> - **No release is cut with a skipped, quarantined or conditionally-ignored test.** A `Skip` attribute in the tree at release time is a red build wearing a disguise. Flakiness is a defect to fix, not a state to tolerate.
> - **Coverage of the boundary is mandatory, not incidental.** Every tool classified by session type, every config key validated against the shipped runtime, every `PLAYWRIGHT_MCP_*` override accounted for. An unclassified tool fails the build — that rule is what makes an upstream addition a red build instead of a security incident.

The founding bug was reproduced during research. Pointing `executablePath` at a non-existent binary produces:

```
exit code 0  ·  stderr EMPTY  ·  initialize OK  ·  tools/list → 24 tools
tools/call   → JSON-RPC *success* response, body: {"isError": true, ...}
```

**Every conventional health signal is green.** The single bit that says "broken" is `isError` inside a 200-equivalent body. Transport-level assertions, protocol-level assertions, exit codes, stderr scanning and `tools/list` **cannot detect this class at all**.

So: any smoke test that stops at `tools/list` reproduces the exact five-day blindness described at the top of this document. The minimum viable assertion is a real navigation — measured at **0.43 s** with no network and no local server ([kb: timings](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)):

```csharp
var result = await client.CallToolAsync("browser_navigate",
    new() { ["url"] = "data:text/html,<h1>ok</h1>" }, ct);

Assert.NotEqual(true, result.IsError);   // IsError is bool?; success omits the field
Assert.Contains(result.Content.OfType<TextContentBlock>(),
                b => b.Text.Contains("Page URL: data:text/html"));
```

Use `data:`, not `about:blank` — the latter succeeds too trivially and its snapshot is empty.

Five layers, run at different cadences:

| Layer | Drives | Cost | When |
|---|---|---|---|
| **Unit** | stderr classifier, artifact prefix sort, tool filter/rename, **session-type enforcement**, lock signature and PID-recycle logic, config validator | ms | every build |
| **Fake child** | Full proxy over an in-process `Pipe` pair — no `Process`, no Node. Passthrough fidelity, error shapes, image bytes, cancellation, child death, stderr back-pressure | ms | every build |
| **Real-child contract** | Real `node` + the **resolved** `cli.js`, **no browser**. Golden `tools/list` snapshot, negotiated protocol version, argv contract, config-key validation | 2–5 s | **every build** |
| **Smoke** | Real child **and real browser**. `browser_navigate`, `isError`, real stderr classification, process-tree lifecycle | 10–30 s | every build · **mandatory before release** |
| **Update** | Real feed URL resolves and returns a manifest; `vpk pack` emits a delta; N→N+1 applies and the installed version moves | 1–3 min | **mandatory before release** |

**The real-child contract layer changes character under a floating build.** When the payload was hand-pinned it was a slow-moving regression check that could reasonably run nightly. Now it is *the* mechanism that detects an upstream Playwright change, and it runs on **every build** — 2–5 seconds is nothing against the alternative of finding out from a user. Its golden snapshots are the tripwire; a diff there is not a test failure to suppress but the notification this whole design exists to produce.

`McpClient.CreateAsync` accepts the `IClientTransport` *interface*, so the fake child is an in-process `McpServer` joined by two `Pipe`s — no processes, no ports, fully parallel-safe.

**The most important test in the suite** is mechanical and follows from §"Known trade-offs": read the real child's `tools/list`, then assert **every** tool name carries an explicit session-type classification. An unclassified tool fails the build. That turns "a new upstream tool leaks into interactive mode" from a security incident into a red CI run.

**A nightly run still earns its place, for a narrower reason than before.** Every build now resolves latest, so every build is already a drift check — the standalone drift job's original purpose is absorbed. What remains is the gap between builds: upstream publishes **daily alphas**, and a week with no commits is a week in which the tree silently diverges from what was last proven green. A nightly full-suite run against a freshly resolved payload closes that gap and makes the first build after a quiet period predictable rather than a surprise. Its failure is *expected* when upstream moves, and it is a notification, not an incident.

Lifecycle tests must wrap themselves in their own job object (`KILL_ON_CLOSE`, `using`-scoped) so a failed assertion cannot leave a stray `chrome.exe`, and must never match processes by image name — a test that kills `chrome.exe` by name will one day close the developer's browser.

**The update path needs its own tests, and one of them has to be a real upgrade.** Put every Velopack call behind an interface with `virtual` network methods so the check → download → apply state machine can be driven hermetically; that seam is what lets UCC hold 48 update tests without ever touching the network. But both bugs that actually shipped in UCC sat *outside* that seam — the feed-URL composition, and the wrapper class itself, which has no tests at all. So the pre-release lane must also spend real time on two assertions the hermetic tests structurally cannot make: **resolve the production feed URL** over HTTP and assert a manifest comes back (a local-directory source composes paths differently and will pass where production 404s), and **publish N→N+1, apply it, and assert both that a delta package was generated and that the installed version moved**. Delta granularity is the reason [§G](#g-updates) chose Velopack at all, and nothing in-house has ever proved `vpk` produces one.

### We write our own harness

We do **not** vendor the MCP SDK's test fixtures. They are 1,082 lines (Apache-2.0, unpublished to NuGet), they wire a single client↔server pipe pair where a proxy needs two hops, and copying them means a permanent three-way merge against an upstream that edits `tests/` weekly ([kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)). Writing ~100–200 lines ourselves buys a harness shaped for *this* product and frees the framework choice.

Two lessons are inherited deliberately rather than by copying, because they cost upstream real time to find:

- **Pin `DiscoverProbeTimeout` in test clients.** The SDK's own base class sets it explicitly, citing [csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701) — CI slowness spuriously tripped the probe. This is the same 5-second hazard as [§B](#b-be-the-mcp-server), met from the other side.
- **Disposal order is load-bearing:** cancel the token → complete *both* pipe writers → await the server task → dispose the provider. Any other order hangs or throws.

What we build, and what each replaces:

| Component | Purpose | Replaces |
|---|---|---|
| `McpTestHarness` | The **two-hop** topology: test client → BrowserAI (server) … BrowserAI (client) → fake child. Two pipe pairs, not one. | `ClientServerTestBase` |
| `FakePlaywrightChild` | Scriptable in-process MCP server standing in for `@playwright/mcp`: canned `tools/list`, programmable `tools/call` results, injectable errors, delays, oversized payloads, unknown content types, mid-call death | `TestServerTransport` |
| `TUnitLoggerProvider` | Routes `ILogger` into TUnit's per-test output | `XunitLoggerProvider` + `DelegatingTestOutputHelper` |
| `CapturingLoggerProvider` | Captures log records for assertions | `MockLoggerProvider` |
| `TestDefaults` | Shared timeouts, including the probe-timeout pin above | `TestConstants` |
| `JobObjectScope` | `using`-scoped job object so a failed assertion cannot leak a `chrome.exe` | *(nothing upstream)* |

Nothing from `NodeHelpers.cs` (577 lines of `npm install` machinery for the SDK's conformance suite) is wanted.

### The tests, enumerated

Extensive is a requirement, so it is written down rather than implied. **Every item below is a release gate.**

**Unit** — no processes, no pipes:

- stderr classifier: error-shaped vs. the benign `Session: <path>` a healthy start always prints
- artifact prefix sort across all nine generator prefixes; a hand-named file is never swept into a machine folder
- tool filter / rename / re-describe, **order-stable** (the spec SHOULDs deterministic ordering for prompt-cache hit rates)
- **session-type enforcement, deny-by-default**, exercised against every tool in the surface
- **the mode list is generated from one table** and appears identically in the server `instructions`, in `init`'s description, in `resume`'s playback and in the refusal text — a mode added in one place and missed in another fails the build
- **`init` rejects an absent, empty, relative or malformed session directory** rather than defaulting or normalising it
- **the cross-process window read still bypasses `WM_GETTEXT`** — spawn a child that creates a message-only window whose WndProc suppresses `WM_GETTEXT`, named with a known GUID, and assert the parent reads the GUID anyway. **No browser needed, milliseconds, every build.** This pins undocumented behaviour of a documented function; it goes red the day Windows routes that read through the message queue
- **a candidate whose directory cannot be attributed is reported, never killed** — the property that keeps the undocumented read off the critical path. Simulate an attribution failure and assert the sweep emits a stray-found-cannot-attribute diagnostic and terminates nothing
- **`GetWindowTextW` and `InternalGetWindowText` agree** on every window the sweep enumerates; a divergence means the fast path changed and the fix is a one-line switch
- **`EnumWindows` returns zero `Chrome_MessageWindow`s**, so nobody later "simplifies" the class-qualified walk into an `EnumWindows` loop that silently finds nothing
- **a window destroyed mid-walk produces `GetLastError() == 1400` and triggers a restart**, rather than truncating the enumeration
- **a title that is not a rooted local drive-letter path is rejected before any filesystem call** — a UNC title otherwise stalls the sweep for 21 seconds
- **every race in [the sweep table](#race-conditions-and-what-closes-each) has a test.** Specifically: an `AbandonedMutexException` leaves sweeping functional (**R3**); the sweep refuses to kill when the directory lock is held (**R1**); a zero-timeout acquire under N concurrent starters runs exactly one sweep and blocks none of the others (**R9**); a re-asserted pointer restores itself after a wrongful delete (**R7**); and the sweep never writes to `stdout` (**R12**)
- **a mode refusal names the mode that would permit the call**, not merely that the call was refused
- handle lifecycle: missing, unknown and expired produce **three distinct LLM-readable errors**, each naming `init` and recoverable in one turn
- lock-name derivation: `GetFullPath` → `TrimEnd('\')` → `ToUpperInvariant` → SHA-256 → `Global\BrowserAI-{hash[..32]}`
- PID-recycle logic keyed on `(pid, creationFileTime)`, not a bare PID
- config generator and validator
- environment allowlist: `Clear()` runs first; `INIT_CWD`, `NODE_OPTIONS`, `NODE_PATH`, `DEBUG`, `DEBUG_FILE`, **`PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`** and the four `PLAYWRIGHT_DOWNLOAD_HOST` variants stripped; `PLAYWRIGHT_SKIP_BROWSER_GC=1` and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` set; `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` **not** set; `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`/`ALL_PROXY`/`NODE_EXTRA_CA_CERTS` passed through
- **job object construction**, asserted on the flags actually set: `KILL_ON_JOB_CLOSE` present; `BREAKAWAY_OK`, `SILENT_BREAKAWAY_OK` and `JobObjectBasicUIRestrictions` absent; the handle non-inheritable; assignment performed through `PROC_THREAD_ATTRIBUTE_JOB_LIST` rather than after the fact
- **no process is reachable by name**: zero occurrences of `Process.GetProcessesByName`, `taskkill /IM` or name-filtered WMI in the tree, at analyzer-error severity
- stdout wrapper: UTF-8, LF, no BOM — and no path in the process can reach `Console.Out`
- **model-facing text stays inside its budget**: the server `instructions` string and every tool description measured at build time, failing over **2 KB**. Claude Code truncates both silently, so the tail of any guidance that exceeds it does not exist and nothing reports that. Same shape as *an unclassified tool fails the build*; `SixFive7/OutlookAI` already pins its instructions string with a test
- artifact routing: a `filename` maps to the right typed subfolder, a duplicate name is suffixed rather than overwritten, `..` is refused rather than normalised, and the result carries **both** the absolute and the repository-relative path
- the session index records one entry per routed artifact, and its roll-up is scoped to the output root rather than the machine

**Fake child** — full proxy over in-process pipes:

- `tools/call` results pass through **byte-identical**, including image and binary payloads — asserted on the exact byte span of `result` via `Utf8JsonReader` token offsets, never by re-serialising and comparing. This is what the custom server transport buys; against the SDK's own it fails on any string containing a backtick, apostrophe, angle bracket or non-ASCII character
- **unknown content *types* pass through** rather than throwing (the SDK's typed layer throws; a proxy must not)
- **unknown *properties* survive** (the SDK's converter drops them, with tests asserting it does)
- `isError: true` bodies preserved verbatim; nested JSON-RPC `error.data` not flattened
- `handle` injected into every schema, order-stable
- **cancellation relay, hand-rolled**: an upstream `notifications/cancelled` produces a downstream one. The SDK emits nothing at all here — measured 2026-08-15 — so this test covers *our* `ct.Register` path and the self-assigned `JsonRpcRequest.Id` it depends on, not SDK behaviour
- progress relay: the child's token is remapped to the caller's
- child death mid-call; stderr back-pressure with a full pipe; oversized payload

**Real-child contract** — real `node`, resolved `cli.js`, **no browser**:

- golden `tools/list` snapshot
- **every tool carries an explicit session-type classification; an unclassified tool fails the build**
- negotiated protocol version asserted (the child caps or echoes silently — it never rejects)
- argv contract; `--caps` is never passed
- **config round-trip via `browser_get_config`** — every opinion BrowserAI generated survived into the child. This is where silently-discarded config keys are caught, and it needs no browser

**Smoke** — real child **and** real browser:

- `browser_navigate` to `data:text/html,<h1>ok</h1>` returns not-`isError`
- **the resolved `executablePath` is *our* Chromium.** A bare navigation assertion is insufficient: verified 2026-08-13, with an **empty** browsers directory `initialize`, `tools/list` *and* `browser_navigate` all succeed via system Chrome
- **an empty browsers directory must FAIL this layer.** Without this the entire batteries-included premise can be silently dead code with the suite green
- error-shaped stderr classified correctly against a real start
- **the browser command line carries `--sandbox` and not `--no-sandbox`** — the config key is silently discarded, so only the resolved command line proves it
- **the browser is not registered for restart.** Open the browser process with `PROCESS_VM_READ` and assert `GetApplicationRestartSettings` returns `0x80070490` (`ERROR_NOT_FOUND`). This is the insurance against a future Playwright arg-list trim silently re-enabling resurrection, and it is a direct test of the mechanism rather than a proxy for it
- **the resolved user-data-dir is exactly what we passed** — catches both a `UserDataDir` policy hijack and a silent fallback to a default profile
- **the `Chrome_MessageWindow` lookup finds the browser we launched**, keyed on the canonicalised path, and finds nothing for a directory with no browser
- **zero process leakage after a hard kill.** Enumerate via `QueryInformationJobObject(JobObjectBasicProcessIdList)`, assert `IsProcessInJob` for every PID in the descendant tree, `TerminateProcess` the launcher from outside, then assert every PID is gone **and every profile directory deletes cleanly** — a directory that still holds a lock proves an escaped browser. Cross-check the job's PID list against a toolhelp walk seeded from an I/O completion port on the job, so a process whose parent already exited is not missed. Run it against both browsers: Firefox stacks two `SILENT_BREAKAWAY_OK` jobs between ours and its content processes and is the harder case. A working prototype is at `.work/jobtest/`

**Update** — see the paragraph above: real feed URL, real delta, real N→N+1, plus rollback under `AllowVersionDowngrade`.

**Marker** — resolved version equals reviewed version, for every upstream in [`upstream-review.json`](upstream-review.json).

### The upstream-review marker

Our suite passing after a version bump means *our assumptions still hold*. It cannot mean *we noticed what upstream learned*. The golden snapshot catches surface changes; it is blind to behaviour changes, new config keys, changed defaults and fixed bugs.

So adopting a new upstream version is gated. [`upstream-review.json`](upstream-review.json) records the version each dependency was last **reviewed** at; a test asserts that equals the version the build **resolved**. When the float moves, that test goes red and the release gate cannot pass until someone edits the marker — and editing it is what forces the look. The procedure lives in [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md); three layers point an agent at it before it can change the file:

| Layer | Mechanism |
|---|---|
| The file | `_instructions` keys naming the procedure, in-band |
| The repo | A rule in [`CLAUDE.md`](CLAUDE.md) |
| The harness | A `PreToolUse` hook on `Edit|Write` that returns `permissionDecision: "ask"` with the procedure as its reason |

**This is a speed bump, not a proof.** Nothing verifies the diff was actually read. What it buys is that skipping the review becomes a deliberate act, recorded in git history, at the moment it matters — rather than the silent default. Requiring a `notes` entry means discharging it leaves an artifact, so an empty note is visible in review.

### The release gate

**Releases are triggered manually, by the maintainer, through the agent. There is no release pipeline, no scheduled publish, and no auto-merge on green.** That simplification is affordable *only* because the gate itself is mechanical: when to release is a human decision, whether a release is permitted is not.

The sequence, in order, no step skippable:

1. **Resolve.** The build takes the latest of every dependency and records what it got — `packages.lock.json`, the resolved `package-lock.json`, browser revisions from the resolved `browsers.json`.
2. **Build.** NativeAOT (or trimmed self-contained), analyzers at error severity. A warning-as-error is a red build.
3. **Run everything.** All five layers, including the two marked *mandatory before release*. Not a subset, not "the fast ones", not "the ones related to this change". This is also where [the upstream-review marker](#the-upstream-review-marker) fires: if the resolved version moved past the reviewed one, the suite is red and there is nothing to decide at step 5.
4. **Green, or stop.** A failure is a work item, never a waiver. If upstream broke something, the fix is to make the new version work — [rule 4](README.md#the-four-rules-that-make-floating-safe).
5. **The maintainer decides.** Green is necessary and not sufficient: a green build is *releasable*, not *released*.
6. **Cut it.** `vpk pack`, publish, and record the resolved set alongside the artifact so the release can state exactly what it contains.

**Why manual is right here, and the condition under which it stops being right.** With one maintainer and a single track, a release pipeline is ceremony around a decision one person makes anyway — and [§G](#g-updates) is already the most hazard-dense section in this document without adding pipeline-authored releases to it. The honest cost: **the gate is only as good as the person invoking it.** It rests entirely on step 3 being *run* rather than assumed. The day a second person can cut a release, that assumption breaks and the gate has to move into automation.

**What manual does not mean.** It does not mean the suite runs when someone remembers. Steps 1–4 are the ordinary build and run on every build, whether or not a release is in view. Manual governs step 5 alone.

---

## Hazard index

Every failure mode surfaced during research, in one checkable list. The overwhelming majority are **silent** — that is why they are enumerated rather than left to prose.

**This table is a lens, not a record.** Each row states the hazard in one line and points at the place that establishes it: an article under [`kb/`](kb/README.md) where a measurement stands behind it, a section of this plan or of the [README](README.md) where a decision does. Reading a row is not reading the evidence. A row whose *Established in* cell opens with `—` has no article to go to — it is a design consequence or a reading of the spec, recorded here only.

**If you find a new hazard, add it here** — and put the fact behind it in the `kb/` article that owns the topic. This list is what a reviewer checks the implementation against.

Section shorthands: [§A](#a-ship-and-own-the-runtime) · [§B](#b-be-the-mcp-server) · [§C](#c-the-init-tool-and-instance-handles) · [§D](#d-locking-and-single-instance) · [§E](#e-lifecycle-and-observability) · [§F](#f-artifact-management) · [§G](#g-updates) · [§stack](#implementation-stack) · [§testing](#testing-a-hard-requirement-and-the-release-gate) · [§trade-offs](README.md#known-trade-offs) · [§settled](README.md#settled-2026-08-13) · [§still-open](README.md#still-open)

| Area | Hazard | Established in |
|---|---|---|
| Protocol and SDK | `ListToolsAsync(RequestOptions?, ct)` silently drops any tool whose `x-mcp-header` annotations fail SEP-2243 validation — the exposed surface shrinks with no error. Use the raw `ListToolsRequestParams` overload | [kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around) · [§stack](#implementation-stack) |
| Protocol and SDK | The `ContentBlock` converter drops unknown properties — data loss on passthrough, and the SDK has tests asserting it does | [kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around) · [§stack](#implementation-stack) |
| Protocol and SDK | The `ContentBlock` converter **throws** on unknown content *types* — an additive upstream change fails the entire call at deserialization, before any BrowserAI code runs | [kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around) · [§stack](#implementation-stack) |
| Protocol and SDK | The typed client flattens JSON-RPC `error.data` to primitives, losing nested error structures. Protocol errors only — tool failures travel as `isError:true` data | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) |
| Protocol and SDK | A tools-only proxy does not forward **resources or prompts**. `@playwright/mcp` advertises only `tools` today, so nothing is lost now — but a future release adding either would silently not appear | — |
| Protocol and SDK | `McpServerToolCreateOptions` has `OutputSchema` but **no `InputSchema`**, so the obvious factory API always reflects the schema from the .NET signature — unusable for a proxy, and the one that will be reached for first | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) |
| Protocol and SDK | Spec 2026-07-28 SHOULDs a **deterministic tool order** for client-side and prompt-cache hit rates — the rewrite step must be order-stable, not incidentally ordered | — |
| Protocol and SDK | `DiscoverProbeTimeout` is 5 s when the client version is unpinned — a flat 5 s per child spawn against a ~300 ms baseline, presenting as "slow", never as an error | [kb: the protocol split](kb/mcp/protocol.md#the-protocol-split) · [§B](#b-be-the-mcp-server) |
| Protocol and SDK | The child never *rejects* a protocol version — it caps or echoes silently, so a mis-negotiation produces nothing to catch. Assert on the negotiated value | [kb: the protocol split](kb/mcp/protocol.md#the-protocol-split) · [§B](#b-be-the-mcp-server) |
| Protocol and SDK | **The SDK never sends `notifications/cancelled` downstream** — not merely "not automatic": it emits nothing at all, on the raw and typed paths alike, because CTS callbacks run LIFO and `WaitAsync`'s disposal wins. A cancelled `browser_navigate` leaves the child working | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§stack](#implementation-stack) |
| Protocol and SDK | `StreamServerTransport` re-escapes every outgoing string, with no options seam — inflating every result, and those tokens land in the model's context | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§stack](#implementation-stack) |
| Protocol and SDK | Typed `ListToolsResult` drops unknown **tool-level** members — `Tool` carries no `[JsonExtensionData]`, so `tools/list` must be rewritten on `JsonNode` | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§stack](#implementation-stack) |
| Protocol and SDK | `RequestHandlers`' XML doc contradicts its behaviour — documented as taking precedence over built-in handlers, it actually throws `InvalidOperationException` when the method is already handled | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§stack](#implementation-stack) |
| Protocol and SDK | `cmd.exe` expands `%VAR%` in arguments, and a whitespace-bearing argument plus `&` kills the child outright — the second only bites when the command path has a space, i.e. the stock `C:\Program Files\nodejs\node.exe`. Both are closed by the custom transport | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§stack](#implementation-stack) |
| Protocol and SDK | `_meta.json` / `_meta.cwd` / `_meta.raw` are read by the child before zod parsing and stripped before the tool sees them — undocumented but real, and available for BrowserAI to inject | [kb: artifacts](kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour) |
| Handle routing and instance lifetime | The model will call a tool without a handle, especially first thing after a cold start — every call fails until it recovers, so the error must name `init`, state what it needs, and be fixable in one turn | — · [§C](#c-the-init-tool-and-instance-handles) |
| Handle routing and instance lifetime | Injecting `handle` into ~69 schemas can perturb tool ordering — an unstable rewrite quietly costs prompt-cache hits on every session | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§C](#c-the-init-tool-and-instance-handles) |
| Handle routing and instance lifetime | Handle→type lookup is shared mutable state on every call's hot path — with N concurrent instances a lookup race is a session-type enforcement bypass. Must be tested under concurrency, not assumed | — · [§trade-offs](README.md#known-trade-offs) |
| Handle routing and instance lifetime | N children means N stderr streams — undemultiplexed, diagnostics become unreadable at exactly the moment they matter | — · [§C](#c-the-init-tool-and-instance-handles) |
| Handle routing and instance lifetime | A handle can outlive the child it points at — a lock held by a dead instance is the precise failure the current launcher needed a signature heuristic to survive | — · [§still-open](README.md#still-open) |
| Handle routing and instance lifetime | `init` accepts **any** path, by decision — an instance can be pointed at the user's real Chrome profile, or write anywhere the process can reach. **Accepted**; the mitigation is descriptive (a tool description an agent will follow, plus logging resolved absolute paths), not enforced | — · [§settled](README.md#settled-2026-08-13) |
| Bundling and AOT | Bundling Chromium transfers **CVE response** from Google to us — every install stays vulnerable until BrowserAI cuts a release. "Update on our schedule" cuts the wrong way here | — · [§A](#a-ship-and-own-the-runtime) |
| Bundling and AOT | Chromium is not Chrome — no proprietary codecs, no Widevine, a different UA and fingerprint surface. Verify the portals actually in use before cutting over | [kb: fingerprinting](kb/chromium/fingerprinting.md) · [§A](#a-ship-and-own-the-runtime) |
| Bundling and AOT | NativeAOT forbids reflection-based serialization, so `System.Text.Json` needs source-generated contexts. `IsAotCompatible=true` is the author's claim about their code, not a proof for our usage — publish AOT and run the suite before committing | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§A](#a-ship-and-own-the-runtime) |
| Bundling and AOT | A floating build with a weakened suite — the versioning policy stops being safe the moment a test is skipped, quarantined or waived. The one hazard here that is **self-inflicted rather than upstream**, and the only one whose mitigation is a habit rather than a mechanism | — · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Bundling and AOT | **TUnit: a test passes silently if its assertion is not awaited.** Listed first on TUnit's own troubleshooting page. Green-when-broken, so the analyzers must run at error severity, never merely enabled | — · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Bundling and AOT | TUnit is MTP-only and conflicts with `Microsoft.NET.Test.Sdk` — the package must be absent, not merely unused; mixing VSTest- and MTP-based projects in one solution is explicitly unsupported | [kb: package provenance](kb/packaging/dependencies.md#package-provenance-as-looked-up) · [§stack](#implementation-stack) |
| Bundling and AOT | Coverlet does not work under MTP — coverage needs `Microsoft.Testing.Extensions.CodeCoverage`, diverging permanently from any VSTest-based sibling repo | [kb: package provenance](kb/packaging/dependencies.md#package-provenance-as-looked-up) · [§stack](#implementation-stack) |
| Bundling and AOT | IDE test discovery is not zero-config under MTP — Visual Studio needs *Use testing platform server mode*, Rider needs Testing Platform support enabled. A developer seeing "no tests" is a config gap, not a broken suite | — · [§stack](#implementation-stack) |
| Bundling and AOT | **`chrome-headless-shell` writes no profile `lockfile`** — two headless instances share one profile directory with **no error anywhere**, silently corrupting the cookie and storage databases, in the mode upstream defaults to | [kb: image-path detection](kb/windows/detection.md#process-image-path--the-fully-documented-detection-path) · [§D](#d-locking-and-single-instance) |
| Bundling and AOT | Killed children leak `browser@<guid>` descriptors into the browsers-registry root — which §A puts inside the Velopack payload, a tree that should be read-only and is wiped on update | [kb: artifacts](kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour) · [§A](#a-ship-and-own-the-runtime) |
| Bundling and AOT | `browser_storage_state` never captures IndexedDB — a "saved" session silently omits it, and the persistent profile carries it, so the tool is *weaker* than doing nothing | [kb: tools that reach credentials](kb/playwright/tools-and-artifacts.md#tools-that-reach-credentials) · [§C](#c-the-init-tool-and-instance-handles) |
| Bundling and AOT | The `PLAYWRIGHT_MCP_*` count is **42**, not 40 — two are read outside the config env mapping, so the allowlist test must derive the count from the resolved bundle and never carry a literal | [kb: environment and merge order](kb/playwright/configuration.md#environment-merge-order-and-startup-output) · [§trade-offs](README.md#known-trade-offs) |
| Bundling and AOT | §C's `tools/list_changed` evidence is stale — accurate at client 2.0.65, **false at 2.1.231**. It does not unlock a per-connection tool list (SEP-2567 stands), but the cited issues need re-dating | [kb: the client](kb/mcp/protocol.md#the-client-claude-code) · [§C](#c-the-init-tool-and-instance-handles) |
| Bundling and AOT | Rewriting a `filename` without rewriting the result path — the agent reports a location the file is not at. A new silent failure introduced by the fix for an old one; levers 2 and 3 ship together or neither ships | — · [§F](#f-artifact-management) |
| Bundling and AOT | A `filename` containing `..` — normalising traversal instead of refusing it turns a routing feature into an arbitrary-write primitive | — · [§C](#c-the-init-tool-and-instance-handles) |
| Bundling and AOT | Two artifacts with the same caller-supplied name in one session — silent overwrite is data loss. Suffix and say so | — · [§F](#f-artifact-management) |
| Bundling and AOT | `--output-max-size` evicting artifacts BrowserAI promised to keep. **It has no default at any merge stage** (verified 2026-08-15), so eviction is off unless someone turns it on — but the env var is a second door, so assert both | [kb: upstream config](kb/playwright/configuration.md) · [§F](#f-artifact-management) |
| Bundling and AOT | Server `instructions` or a tool description exceeding 2 KB — Claude Code truncates both **silently**; the tail of the guidance simply does not exist, and nothing reports it | [kb: the client](kb/mcp/protocol.md#the-client-claude-code) · [§C](#c-the-init-tool-and-instance-handles) |
| Bundling and AOT | The upstream-review marker can be discharged without reading anything — a speed bump, not a proof. Accepted deliberately: the value is that skipping becomes deliberate and recorded, not that it becomes impossible | — · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Child runtime and configuration | `browserName` defaults to system Chrome and `headless:false` on Windows — the bundled Chromium is never consulted. Verified against an empty browsers directory | [kb: upstream config](kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | **42** `PLAYWRIGHT_MCP_*` env vars override the generated config — silent override of every opinion, including a capability wipe. Counted from the resolved bundle 2026-08-15 | [kb: environment and merge order](kb/playwright/configuration.md#environment-merge-order-and-startup-output) · [§trade-offs](README.md#known-trade-offs) |
| Child runtime and configuration | `loadConfig` is a bare `JSON.parse` with no schema validation — unknown or renamed keys are silently discarded | [kb: upstream config](kb/playwright/configuration.md#silent-config-failures) · [§why-3](README.md#3-two-failure-classes-exist-that-no-configuration-can-fix) |
| Child runtime and configuration | `core-install` is declared in `config.d.ts` but **no tool carries it** in 0.0.79 — a dead capability string; setting it does nothing | [kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape) |
| Child runtime and configuration | `chromiumSandbox:true` in a **config file** is discarded; only the `--sandbox` CLI flag works — so the browser and every child run `--no-sandbox` while the config says otherwise. Pass the flag, and assert `--no-sandbox` is absent from the child's resolved browser command line | [kb: upstream config](kb/playwright/configuration.md#silent-config-failures) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | `--output-max-size` evicts files it did not create — recursively lists the whole output dir, sorts oldest-first and unlinks past the threshold, sparing only the current response's writes. It must stay unset, and **`PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE` must be stripped** | [kb: upstream config](kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | `--isolated` puts the profile in a temp dir **deleted on close** — silent total data loss. Structurally impossible for us (`validateBrowserConfig` throws on `isolated` + `userDataDir`), but three of the four legacy modes set it | [kb: upstream config](kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | A relative `PLAYWRIGHT_BROWSERS_PATH` resolves against `INIT_CWD` before `cwd` — pointing at a directory inherited from any npm ancestor | [kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | Outer browser dirs use underscores, inner ones dashes — `chromium_headless_shell-1237\chrome-headless-shell-win64\`, so a path built consistently is wrong | [kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | Playwright writes `DEPENDENCIES_VALIDATED` into the browsers root on first launch — under `Program Files` that write silently fails and the validation re-runs every launch. Prefer `%LOCALAPPDATA%` or `%ProgramData%` with write ACLs | [kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | `.links/` records the **build machine's** absolute paths — leaking build paths into the shipped tree, and useless on the target. Strip it | [kb: first-run provisioning](kb/playwright/provisioning-and-timings.md#first-run-provisioning) · [§A](#a-ship-and-own-the-runtime) |
| Child runtime and configuration | Playwright's stale-browser GC deletes registry dirs not referenced by `.links` — blast radius "deletes BrowserAI's shipped Chromium". Pin `PLAYWRIGHT_SKIP_BROWSER_GC=1` | [kb: upstream config](kb/playwright/configuration.md#environment-merge-order-and-startup-output) · [§trade-offs](README.md#known-trade-offs) |
| Child runtime and configuration | `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` writes a line to stderr when set — tripping the error-shaped-stderr detection in §E. Do not set it | [kb: upstream config](kb/playwright/configuration.md#environment-merge-order-and-startup-output) · [§trade-offs](README.md#known-trade-offs) |
| Child runtime and configuration | The `playwright` package (4.85 MB) is a declared dependency that is **never loaded** — prunable, but `npm ls` will then call the tree broken. A deliberate choice, not an oversight | [kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape) |
| Child runtime and configuration | Upstream publishes **daily alpha** builds — the `@latest` float is sharper than it appears; another argument for the pin | [kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape) · [§why-2](README.md#2-the-version-chain-floats-and-it-breaks-silently) |
| Process and OS (Windows) | Backslashes are illegal in a mutex name after `Global\` — a path-keyed lock cannot contain the path. Canonicalise and hash | [kb: object names and window scoping](kb/windows/detection.md#windows-object-names-and-window-scoping) · [§D](#d-locking-and-single-instance) |
| Process and OS (Windows) | `Global\` requires `SeCreateGlobalPrivilege` — fine for interactive users, fails under low-integrity / AppContainer | [kb: object names and window scoping](kb/windows/detection.md#windows-object-names-and-window-scoping) · [§D](#d-locking-and-single-instance) |
| Process and OS (Windows) | `Process.ExitCode` throws after `Dispose()` and always throws for `GetProcessById` — worse than PowerShell's `$null`. Cache the `int` immediately | [kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `WaitForExit(int)` does not drain the async readers — truncated stderr. Only `WaitForExit()` and `WaitForExitAsync` drain | [kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `Console` stdio defaults to CP437 in **both** directions, any `TextWriter` adds CRLF, and a hand-rolled `StreamWriter` adds a BOM — corrupting JSON-RPC on the first non-ASCII byte | [kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `Process.Kill(entireProcessTree:true)` needs BrowserAI alive and running code — it cannot help in the one case that matters, which is BrowserAI being the thing killed | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `Process.Start` then `AssignProcessToJobObject` races the child's own spawns — **measured: 2 escapees.** .NET cannot express the fix, so `PROC_THREAD_ATTRIBUTE_JOB_LIST` via P/Invoke is mandatory | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | An **inheritable** job handle is inherited by the child, so BrowserAI's death no longer closes the last handle and `KILL_ON_JOB_CLOSE` never fires — **measured: every child survived.** Redirecting stdio forces `bInheritHandles=TRUE`, so this is always one flag away | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `JOB_OBJECT_LIMIT_BREAKAWAY_OK` also **arms** Firefox's escape — `NeedToBreakAwayFromJob()` returns true only when the job carries `KILL_ON_JOB_CLOSE` **and** `BREAKAWAY_OK`, so setting it does not merely permit an escape, it causes one | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `JobObjectBasicUIRestrictions` blocks job nesting — it can prevent Chromium's sandbox job from nesting inside ours | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | libuv assigns every non-detached child to its own `SILENT_BREAKAWAY_OK` job — a second kill path for free, but an accident of Playwright's `detached:false` on Windows. Never depend on it | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | Firefox background tasks and the crash reporter request breakaway — their `CreateProcess` fails `ERROR_ACCESS_DENIED` inside our job. The correct trade, not a bug; do not "fix" it | [kb: job objects](kb/windows/processes.md#job-objects-and-process-containment) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | Full Chromium calls `RegisterApplicationRestart` unconditionally, guarded upstream only by `--browser-test`. **The call already fails** — Playwright's command line overshoots the 1023-character limit by 531–807 — so a future arg-list trim would silently re-enable it, which is what the unregistered-browser test exists to catch | [kb: resurrection](kb/chromium/resurrection.md) |
| Process and OS (Windows) | An unusable `--user-data-dir` makes Chrome **fall back**, invisibly — 8 healthy processes, `initialize` and `browser_navigate` both OK, nothing surfaced to the client, and the message window titled with the **fallback** path, so stray detection goes blind to exactly the broken instances | [kb: profile fallback](kb/chromium/profiles.md) |
| Process and OS (Windows) | Chrome's "Failed to create data directory" dialog **blocks startup** until dismissed — measured at 1 process and no renderers at 6 s, 10 processes after `WM_CLOSE`. On a background server this is an invisible hang, and `--noerrdialogs` does **not** suppress it | [kb: profile fallback](kb/chromium/profiles.md) |
| Process and OS (Windows) | A deny-all DACL on an existing profile dir exits with code 21 (`CHROME_RESULT_CODE_PROFILE_IN_USE`) at ~2.5 s with no fallback — a **different** code path from the missing-directory case, because `RecursiveDirectoryCreate` succeeds on an existing directory | [kb: profile fallback](kb/chromium/profiles.md) |
| Process and OS (Windows) | The `Chrome_MessageWindow` class alone is ambiguous — the same process owns one titled `DeviceMonitorMessageWindow` and several untitled, and the GPU process owns one too. **The title match is load-bearing**, and must be backslashes, absolutised, no trailing separator | [kb: stray detection](kb/windows/detection.md) |
| Process and OS (Windows) | A `UserDataDir` **policy** overrides the command line, read in `chrome_elf` before argv is parsed — per-session profile isolation collapses silently. Absent on this machine; assert the resolved dir is what we passed | [kb: upstream config](kb/playwright/configuration.md#policy) |
| Process and OS (Windows) | Firefox registers for restart too, but honours a pref — `toolkit.winRegisterApplicationRestart:false` calls `UnregisterApplicationRestart` at runtime. Pass it via `firefoxUserPrefs` on every launch | [kb: resurrection](kb/chromium/resurrection.md#the-mechanism-and-what-is-still-unproven) · [§E](#e-lifecycle-and-observability) |
| Process and OS (Windows) | `chrome-headless-shell` creates no `Chrome_MessageWindow` and no `lockfile` — the one binary that can leak but cannot be cheaply found. Reason enough to run full Chromium in every mode | [kb: image-path detection](kb/windows/detection.md#process-image-path--the-fully-documented-detection-path) · [§A](#a-ship-and-own-the-runtime) |
| Process and OS (Windows) | `psi.Environment` is pre-populated with the inherited block and assignment **merges** — an allowlist requires `Clear()` first | [kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup) · [§trade-offs](README.md#known-trade-offs) |
| Process and OS (Windows) | `psi.WorkingDirectory` left unset passes `null` to `CreateProcess` — the child inherits BrowserAI's cwd, whatever the client happened to have | [kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup) · [§trade-offs](README.md#known-trade-offs) |
| Process and OS (Windows) | `ArgumentList` and `Arguments` are **mutually exclusive** — setting both is undefined behaviour. Use `ArgumentList` for its quoting rules | [kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup) |
| Packaging and updates | A feed base URL built as `{BaseUrl}/{channel}` makes `SimpleWebSource` request `{channel}/releases.{channel}.json` → 404, reported as "no update available" and nothing else. Use `UpdateOptions.ExplicitChannel` | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | A local-directory update source composes feed paths differently from `SimpleWebSource` — an update test that passes against a local folder still 404s in production. The test must resolve the real URL | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Packaging and updates | `SetAutoApplyOnStartup` defaults to **true** — BrowserAI exits(0) at handshake time and relaunches with dead pipes | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | The Velopack execution stub is `windows_subsystem = "windows"` and **exits in 59 ms** while the app runs on — a stdio client registered against it sees the child die instantly | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | `force_stop_package` kills every process under the install root, matched by image **path**, on apply/install/start/uninstall **and after every hook returns** — three other live sessions destroyed mid-task, and every running browser with them, bypassing the job object | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | `UpdateManager` constructed merely to read the installed version issues a **network request** on the stdio startup path — *superseded: never applied to 1.2.0, where the ctor only assigns fields. `VelopackLocator` is the one that is not free* | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | `NotInstalledException` is thrown by every Velopack call under `dotnet run` and any test host — *superseded for 1.2.0: it is `InvalidOperationException`, `VelopackApp.Build().Run()` succeeds, and the test seam is a boolean.* `Debugger.IsAttached` still does not detect a test runner | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | `ApplyUpdatesAndRestart(null)` restarts without a package by undocumented fall-through — *now documented; the advice holds for a different reason, and the charter's `UpdateExe.Start(waitPid)` does not compile* | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | `VelopackApp.Build()` takes a **separate** logger registration from `UpdateManager`, silencing installer, first-run and post-restart hooks — *fixed in 1.2.0: one `SetLogger()` reaches all of them* | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | Spawning the replacement before exit, or releasing the lock before spawning — both race the §D directory-keyed lock. `Update.exe` outlives the process specifically to close this | — · [§G](#g-updates), [§D](#d-locking-and-single-instance) |
| Packaging and updates | A single download timeout aborts a healthy slow link or hangs forever on a stalled one — it needs absolute + stall + lifetime, and the download must run off the message loop | — · [§G](#g-updates) |
| Packaging and updates | Velopack prunes `packages\` to the current full `.nupkg` and deltas are forward-only — every rollback is a full download unless you archive packages yourself | [kb: Velopack](kb/packaging/velopack.md#rollback) · [§G](#g-updates) |
| Packaging and updates | `AllowVersionDowngrade` off, **or** a strictly-increasing version rule in the release script — rollback fails at one end or the other. Both halves must agree; UCC has the mismatch today | [kb: Velopack](kb/packaging/velopack.md#rollback) · [§G](#g-updates) |
| Packaging and updates | `current\` is wholly replaced on update — all state must resolve from `VelopackLocator.Current.RootAppDir` | [kb: Velopack](kb/packaging/velopack.md#where-state-may-live--the-finding-the-provisioning-design-rests-on) · [§G](#g-updates) |
| Packaging and updates | `AppContext.BaseDirectory` resolves *inside* `current\` — it reads as "next to the binary", so logs and caches written there are wiped by every update and any retention policy is silently reset | [kb: Velopack](kb/packaging/velopack.md#where-state-may-live--the-finding-the-provisioning-design-rests-on) · [§G](#g-updates) |
| Packaging and updates | Update state persisted alongside the binary desyncs across exit-and-relaunch — derive it from the installed version instead | — · [§G](#g-updates) |
| Packaging and updates | The swap holds old + new `current\` simultaneously — budget ~600–700 MB transient disk, plus a full re-extraction of ~380 MB per update | [kb: component sizes](kb/playwright/provisioning-and-timings.md#component-sizes) |
| Packaging and updates | Velopack's Rust `Setup.exe`/`Update.exe` carry their **own** Windows floor, separate from .NET's — they can fail before the managed app exists, and `--runtime win7` does not help if the installer binary cannot run | [kb: Velopack](kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict) · [§G](#g-updates) |
| Packaging and updates | `vpk` rejects 4-part version numbers — semver2, three parts only. A build-pipeline failure, not a runtime one | [kb: Velopack](kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output) |
| Packaging and updates | Delta generation is assumed, not verified — UCC has shipped Velopack for multiple releases and **never produced a delta package**, and delta granularity is why §G chose Velopack at all | [kb: UCC prior art](kb/packaging/velopack.md#prior-art-exofabricucc) · [§G](#g-updates) |
| Packaging and updates | Every unsigned `Setup.exe` is a new file to SmartScreen — landing precisely on colleague onboarding. Azure Artifact Signing ≈ $10/mo buys instant reputation | [kb: Velopack](kb/packaging/velopack.md#distribution-msix-and-code-signing) |
| Packaging and updates | MSIX cannot re-register while a package process is running — disqualifying for a long-lived child. Two production AI tools hit this in 2026 | [kb: Velopack](kb/packaging/velopack.md#distribution-msix-and-code-signing) · [§G](#g-updates) |
| Packaging and updates | **Re-running `Setup.exe` over an existing install destroys the root.** `install.rs` renames a non-empty root aside and, on success, deletes it — taking the provisioned browsers with it, at a 203.8 MB re-download. Updates must go through the update path only | [kb: Velopack](kb/packaging/velopack.md) · [§G](#g-updates) |
| Packaging and updates | **`Setup.exe -- <args>` panics and never exits**, installing nothing and leaving one log line. An argument-parser mismatch in `setup.rs`; `update.rs` is unaffected. Never pass start arguments to the installer | [kb: Velopack](kb/packaging/velopack.md) · [§G](#g-updates) |
| Packaging and updates | **`force_stop_package` kills by image path under the root**, on apply/install/start/uninstall **and after every hook returns**. An update therefore terminates every running browser without our teardown, bypassing the job object — and a hook must never leave a helper running under the root | [kb: Velopack](kb/packaging/velopack.md) · [§G](#g-updates) |
| Packaging and updates | Desktop and Start Menu shortcuts are created **by default** (`--shortcuts` defaults to `Desktop,StartMenuRoot`), and `%LOCALAPPDATA%elopack\` is created unconditionally and **not removed by uninstall** | [kb: Velopack](kb/packaging/velopack.md) · [§G](#g-updates) |
| Tooling and CI | `claude mcp list` / `get` **exit 0 even when the server is dead** — unusable as a CI gate without grepping stdout for `✘`. Itself an instance of "reports healthy while broken" | [kb: protocol tooling](kb/mcp/protocol.md#tooling-around-the-protocol) |
| Tooling and CI | The official MCP conformance suite is **HTTP-only** (`--url`) — not directly usable against a stdio server; it needs a test-only listener or a ~50-line bridge | [kb: protocol tooling](kb/mcp/protocol.md#tooling-around-the-protocol) · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Tooling and CI | The Inspector CLI cannot spawn `.cmd` shims on Windows — same root cause as #58510. Address `cli.js` with an absolute path | [kb: protocol tooling](kb/mcp/protocol.md#tooling-around-the-protocol) · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Tooling and CI | Real screenshots are not byte-stable across runs — fidelity assertions need a canned blob from the fake child, not a live capture | [kb: artifacts](kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour) · [§testing](#testing-a-hard-requirement-and-the-release-gate) |
| Tooling and CI | The SDK's test fixtures are **not published** to NuGet, and they wire *one* pipe pair where a proxy needs two hops. **Settled: not vendored — we write our own harness.** The hazard is the temptation, not the act: copying them would mean a permanent three-way merge against an upstream that edits `tests/` weekly, and would lock the framework to xUnit | [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15) · [§stack](#implementation-stack) |

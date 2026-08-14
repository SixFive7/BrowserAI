# BrowserAI

A self-contained, system-installed MCP server that fronts a **pinned** `@playwright/mcp` runtime and exposes browser automation to AI agents through a small, opinionated, centrally-updatable surface.

BrowserAI is a **proxy**. It ships the runtime, owns the lifecycle, and rewrites the tool surface. It does **not** reimplement Playwright, and it does not reimplement Playwright's MCP tool layer. That boundary is the single most important design constraint in this project and is spelled out in [Scope](#scope-proxy-not-implementation) below.

---

## Read this before designing anything

> **The reference implementation is [`SixFive7/Workspace657`](https://github.com/SixFive7/Workspace657), directory `playwright/`.**
>
> Specifically at commits `9d315b2` and `a9ac747` or later. That is the only correct copy of this setup in existence.

The current PowerShell-based setup was copy-pasted into other repositories over several months. As of **2026-08-13**, a filesystem sweep of `C:\Source` (depth 7) found **13 copies of `playwright/launch.ps1` across 10 repositories**:

`ExoFabric/Infrastructure`, `Netwerkplek`, `FluxTone`, `HitsterCardGenerator`, `ImmichDater`, `Jeeves`, `PortainerCompose`, `StationeersPlus`, `SyncthingMonitor`, `Workspace657` — plus 3 worktree/backup copies inside `StationeersPlus`.

**All nine non-Workspace657 copies are byte-identical to each other and all of them differ from Workspace657.** The same holds for `.claude/hooks/playwright-config-hook.ps1`. If the count is "15+", the remainder live outside `C:\Source` or deeper than 7 levels.

**Do not use any other repository as a reference.** They are all stale by the same delta and they all still contain bugs that were diagnosed and fixed on 2026-08-12/13:

| Fixed in Workspace657 | Still broken everywhere else |
|---|---|
| `--output-mode` flag removed | Passes a flag deleted in `@playwright/mcp` 0.0.79 → `error: unknown option`, exit 1, **all four servers dead** |
| Process handle cached before `WaitForExit` | `.ExitCode` reads back `$null`, so a hard startup failure logs identically to a clean shutdown — this is why the above went unnoticed for five days |
| Bundled-Chromium preflight | Missing browser revision → MCP handshake *succeeds*, `tools/list` returns normally, first `browser_navigate` fails |
| Detached installer via `cmd /c start "" /b` | `Start-Process` redirection does not prevent stderr-pipe inheritance; a client reading stderr blocks for the entire ~300 MB download (measured 11.71s → 0.37s after fix) |
| Error-shaped stderr detection | Warned on any stderr; Playwright prints a benign `Session: <path>` line on every healthy start |
| Typed artifact folders + prefix classification | Everything flat in one `output/`; grew to 346 session dirs / 1.5 GB in ~3 months |
| Screenshot-hook allowlist widened | Denies saving into any folder other than `output/` |

The lesson worth carrying into BrowserAI: **every one of those defects was invisible.** The setup reported healthy while being broken. Observability is a feature requirement here, not a nicety.

---

## Why this project needs to exist

### 1. There is no update path, and that is the actual problem

A fix made today reaches one of thirteen checkouts. Nothing propagates. The setup was designed to be *copied*, which means every copy is a fork the moment it lands.

This is the core justification for BrowserAI. It is **not** token cost — see [Non-reasons](#non-reasons-do-not-relitigate-these).

### 2. The version chain floats, and it breaks silently

`launch.ps1` runs `npx -y @playwright/mcp@latest`. The dependency chain below that point is **exact** — no semver ranges anywhere:

```
@playwright/mcp 0.0.79
  └── playwright-core 1.63.0-alpha-2026-08-05   (exact pin, no ^ or ~)
        └── browsers.json → chromium-headless-shell rev 1237 (152.0.7977.8)
```

So the package is pinned to a browser revision, but *which package* is not pinned at all. An upstream publish silently invalidates the local browser cache and changes the CLI surface. Both failure modes fired this month, on the same day.

### 3. Two failure classes exist that no configuration can fix

- **CLI flags fail loudly.** An unknown flag crashes at startup with a clear message — recoverable once you can see stderr.
- **JSON config keys fail silently.** `loadConfig` is a bare `JSON.parse` with no schema validation. A key that upstream renamed or removed is simply ignored. `--output-mode` turned out to have been a **no-op for its entire life** — a hardcoded literal in 0.0.78's bundle, never read from config.

A setup that cannot distinguish "this setting is applied" from "this setting is silently discarded" cannot be trusted to be opinionated. BrowserAI must validate its own config against the runtime it ships.

### 4. Exclusivity is keyed on the wrong thing

Mutexes are named `Global\<RepoName>-PlaywrightInteractive`. That locks a *repository folder name*, which is arbitrary: two clones of the same repo collide, and two different repos wanting the same profile do not. The thing that actually requires exclusivity is **the browser profile directory**.

> **And nothing else enforces it in the mode that matters.** Measured 2026-08-14: the **full** chromium build writes a `lockfile` into the profile and a second instance is refused (`Browser is already in use for <dir>`). **`chrome-headless-shell` writes no `lockfile` at all** — two headless instances opened the same profile directory, both launched, both worked, and no error was raised anywhere. Two browsers writing one profile's cookie and storage databases is silent corruption, and headless is the default mode. An earlier draft of this document said *"Chrome's own `SingletonLock` already gets this right for the persistent mode"*; that is true only of the headed build. **[§D](#d-locking-and-single-instance)'s directory-keyed lock is therefore the only protection that exists, not defence in depth.**

### 5. Relative paths make silent data loss possible

All four `config.json` files use paths relative to the working directory, including `userDataDir: "playwright/persistent/profile"`. The launcher currently guarantees cwd via `Set-Location $RepoRoot`. Any change to how the process is started — a proxy, a plugin, a service — re-opens this, and the failure mode is **a fresh empty Chrome profile with every saved login gone, and no error raised**.

### 6. Profiles are fragmented by accident

Thirteen checkouts means thirteen `persistent/profile/` directories. A login established in one repository does nothing for the other twelve. This was never a decision; it is a side effect of copy-paste distribution.

### 7. Distribution to colleagues has no story

The current setup requires a repo, a `.mcp.json`, hook registrations, a `.gitignore` section, and a working Node installation. Onboarding another person means replicating all of it by hand.

---

## Scope: proxy, not implementation

**BrowserAI spawns `@playwright/mcp` as a child process and forwards JSON-RPC to it.**

This is a hard boundary. It exists because the temptation to cross it is real and will present itself as a reasonable next step.

### In scope

- Spawning and supervising a pinned `@playwright/mcp` child over stdio
- Forwarding `tools/call` to that child and returning its response verbatim
- Fetching `tools/list` **from the child at runtime** and rewriting it — filtering, renaming, re-describing, adding parameters
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

The implementation lives in `playwright-core/lib/coreBundle.js` — 3.4 MB, esbuild-bundled, containing a 78-entry tool array. Note the numbers, because a golden test written against the wrong one fails on day one: **78 is the internal registry; 69 is the maximum ever exposed over MCP** (9 are `skillOnly` and always stripped), and **24 is the default** with no `capabilities` set. Its value is not browser control; Playwright for .NET does browser control perfectly well. Its value is the **ref-based accessibility snapshot system**, the response formatting, and the error handling — the layer that turns a browser into something a language model can operate. That layer is large, subtle, actively developed upstream, and would drift permanently the day it is forked.

"We don't want to reimplement Playwright" is the easy half of this rule. The half that matters: **reimplementing the MCP tool layer is also reimplementation**, even though it never touches a browser API.

### The one sanctioned exception, if it is ever needed

`playwright-core` explicitly whitelists `"./lib/coreBundle"` in its `exports` map, so `require('playwright-core/lib/coreBundle')` is a supported import, not a blocked deep path. It exposes `browserTools` (a flat array of plain, inert objects), `filteredTools`, `createConnection`, and `BrowserBackend`. `defineTool` is literally the identity function — there is no class, no registry, no side effect.

This means in-process tool manipulation is *available* if the proxy approach ever proves insufficient. It is **not** the plan, it carries no type definitions and no semver guarantee, and taking it requires pinning `@playwright/mcp` and `playwright-core` together and re-verifying the tool array on every bump. Documented here so it is a considered decision rather than a discovery.

---

## What BrowserAI must do

### A. Ship and own the runtime

| Component | Requirement | Measured size |
|---|---|---|
| Node.js | Bundled — a **single `node.exe`**, nothing else. Verified driving the full MCP protocol with no npm, no `node_modules`, no `.cmd` shims. | v24.19.0 LTS, **88.53 MB** |
| `@playwright/mcp` + `playwright-core` | Resolved to latest at **build** time, then vendored into the artifact as an exact tree — never `@latest` at **spawn** time. Zero native binaries; the tree is portable JS. See [Versioning policy](#versioning-policy-everything-floats-the-build-freezes-it). | 0.0.79, **18.11 MB** |
| `chromium-headless-shell` | Bundled at the revision the pin requires. | rev 1237, **268.49 MB** |
| `ffmpeg` | Required for video capture; without it the `video` artifact type throws. | rev 1011, **3.35 MB** |
| `winldd` | Not currently required — its validation path is a no-op on Windows due to a `chrome-win` / `chrome-win64` mismatch upstream. Ship it as insurance against that being fixed. | rev 1007, **0.25 MB** |
| Full `chromium` | **Required** — the three headed modes run on it. | rev 1237, **426.88 MB** |
| BrowserAI itself | NativeAOT, single file, no .NET runtime on the host. | ~10–15 MB |
| System Chrome | **Not used.** See below. | — |

**Total payload: ~806 MB installed, ~239 MB compressed** (measured, 7z LZMA2 `-mx=5`).

> ### Decided 2026-08-13: batteries included, zero host dependencies
>
> BrowserAI depends on **nothing** from the host — no Node, no .NET runtime, no Google Chrome. All four modes set `browserName: "chromium"` and **drop `channel: "chrome"` entirely**, so headed modes run Playwright's bundled Chromium rather than the user's Chrome. Ship as NativeAOT single-file.

Two consequences worth taking deliberately, because both are real costs of that decision:

- **You now own the browser's CVE response.** Google Chrome self-updates; a bundled Chromium updates only when BrowserAI cuts a release. When a Chromium RCE lands, every install stays vulnerable until you ship. Browser-security patching becomes a release obligation, not someone else's problem — and it is the one part of this payload where "update on our schedule" cuts the wrong way.
- **Headed behaviour changes.** Chromium is not Chrome: no proprietary codecs, different UA and fingerprint surface, no Widevine. Sites that behave differently under Chromium — DRM video, some enterprise SSO, anything fingerprinting the build — will differ from today. Verify the portals actually in use before cutting over.

"Single file" means BrowserAI.exe carries no .NET runtime dependency. The *payload* — `node.exe`, the vendored packages, both browsers — remains a directory tree beside it, which is what Velopack's per-file delta needs to stay cheap.

**NativeAOT is not free.** AOT forbids runtime reflection-based serialization, so `System.Text.Json` needs source-generated contexts (`JsonSerializerContext`). Velopack declares `IsAotCompatible=true` for net8.0+, and **so does `ModelContextProtocol`** — verified in-source on 2026-08-14, set on every target except `netstandard2.0`, at both `v1.4.1` and `v2.2.0`. That is a meaningful signal and **not a proof for our usage**: the `JsonElement` passthrough at the heart of the proxy is AOT-friendly, but a declaration is the author's claim about their code, not about how we drive it. Publish AOT and run the suite against it before committing. The fallback is self-contained trimmed (~70 MB), which against ~806 MB of payload is noise. **AOT buys cold-start latency and the dropped runtime dependency here, not size.**

Shipping the browser deletes an entire subsystem from the current design: the preflight, the install mutex, the staleness timeout, the detached installer, and the retry instructions written for an LLM to read. That machinery exists solely because the browser arrives on demand.

> ### `browserName: "chromium"` must be set explicitly or none of this runs
>
> `@playwright/mcp` 0.0.79 defaults to **`channel: "chrome"`** — system Chrome — and `headless: false` on Windows. Verified empirically: with an **empty** browsers directory, `initialize`, `tools/list` *and* `browser_navigate` all succeeded, because the bundled tree was never consulted.
>
> Omit `browserName` and the entire shipping-Chromium premise is silently dead code. This is the same failure shape as every bug in the table above.

> ### The sandbox is off, and the config key that should enable it does nothing
>
> Measured 2026-08-15: with `"launchOptions": { "chromiumSandbox": true }` set explicitly in a config file, the browser and **every** child still ran with `--no-sandbox`. Only the CLI flag `--sandbox` actually enabled it. `validateBrowserConfig` intends `chromiumSandbox = true` on non-Linux, so this is upstream behaviour contradicting upstream intent.
>
> Two consequences. **BrowserAI must pass `--sandbox` on the command line, never the config key** — and must assert the absence of `--no-sandbox` from the child's resolved browser command line, because the key reads fine and has no effect. **And today's setup runs Chromium unsandboxed**, which is a security posture nobody chose. This is [§why-3](#3-two-failure-classes-exist-that-no-configuration-can-fix) with a live instance attached: a config key that parses, validates, and is discarded. Raise it upstream.

`PLAYWRIGHT_BROWSERS_PATH` must be **absolute** — a relative value resolves against `INIT_CWD` (inherited from any npm ancestor) before `cwd`. The expected layout, verified by execution:

```
<browsers-root>\
  chromium_headless_shell-1237\chrome-headless-shell-win64\chrome-headless-shell.exe
  chromium-1237\chrome-win64\chrome.exe
  ffmpeg-1011\ffmpeg-win64.exe
```

Note the asymmetry: the outer directory uses **underscores**, the inner one **dashes**. No sentinel files (`INSTALLATION_COMPLETE`, `DEPENDENCIES_VALIDATED`) are needed to launch — the only launch-time check is file accessibility of the executable. Strip `.links/` from the shipped tree; it contains the build machine's absolute paths.

Build the browser payload with the pinned package itself, so the revision comes from `browsers.json` rather than a hand-typed URL:

```
set PLAYWRIGHT_BROWSERS_PATH=<staging>
node.exe <staging>\node_modules\@playwright\mcp\cli.js install-browser chromium --only-shell --no-progress
```

**Node SEA, `pkg` and `nexe` are all dead ends** — do not spend time on them. `playwright-core` violates SEA's "no filesystem module loading" constraint in at least five verified ways (`packageRoot` computed from `__dirname`, a runtime `require` of `browsers.json` at a computed path, two `childProcess.fork()` calls on sibling scripts, sibling bundle requires, and `.wasm`/`vite` assets loaded by path). SEA would also save nothing — the output *is* a copy of `node.exe` plus your blob. `vercel/pkg` was archived 2024-01-13. Bun and Deno both have open issues on precisely the Playwright browser-launch path.

### B. Be the MCP server

stdio transport. Registered once at system or user scope, available in every repository, with no per-repo files.

**The protocol split is solved by configuration, not code.** `@playwright/mcp` 0.0.79 caps at `2025-11-25`; the current spec is `2026-07-28`, a breaking rewrite (removes `initialize`/`notifications/initialized`, adds `server/discover`, replaces server→client requests with the MRTR retry pattern, deprecates Roots/Sampling/Logging). The .NET SDK implements every revision from `2024-11-05` through `2026-07-28` and shipped 2026-07-28 support **on the spec's release date**. So the newer-upward/older-downward split is two properties:

```csharp
McpServerOptions.ProtocolVersion = null;          // upward: accept 2024-11-05 … 2026-07-28
McpClientOptions.ProtocolVersion = "2025-11-25";  // downward: pin to the child's ceiling
```

**The second line is not optional.** Left at `null`, the client probes the child with `server/discover` first, bounded by `DiscoverProbeTimeout` — **5 seconds by default**. If the child silently drops the unknown method instead of returning an error, *every child spawn costs a flat 5 s* against a ~300 ms baseline. It would present as "browser automation got slow," with no error anywhere. Pinning the client version skips the probe entirely.

Assert on the negotiated version at startup. The child never *rejects* a version — it caps or echoes silently (verified: offering `1999-01-01` returns `2025-11-25` with no error), so a mis-negotiation produces nothing to catch.

### C. The `init` tool and instance handles

> **Decided 2026-08-13: one MCP registration, handle-based instance routing.**

The client calls `init` first, passing the session type and all target-directory information. BrowserAI resolves the directories, takes the appropriate locks, generates the child config, spawns a Playwright runtime, and returns a **short unique handle**. Every subsequent tool call carries that handle, which routes it to the right instance.

The point of the handle is separation: **the MCP server's lifetime is decoupled from the Playwright instances it supervises.** One BrowserAI process may own several children, and instance creation, lifetime and cleanup belong entirely to BrowserAI — not to the client, and not to Playwright.

This also replaces four static configurations with one dynamic one, and eliminates the relative-path hazard by making the directory an explicit argument rather than an implicit consequence of cwd.

#### The `init` contract

**Takes:** the session type; an output root (optional — defaults outside any repository, see [§F](#f-artifact-management)); a **label** (optional, defaults to the handle); and the profile directory where that applies.

The label exists because `checkout-flow-bug\` is navigable and `2026-08-14T04-11-50-882Z\` is not. Three months of the current setup produced 346 session directories and 1.5 GB, and the reason nobody pruned them is that nobody could tell what any of them had been.

**Returns:** the handle, **and the resolved absolute paths.** [§settled](#settled-2026-08-13) already requires logging those at instance creation; returning them costs nothing extra and puts them where the agent can act on them — it can tell the user where the screenshot is instead of guessing, and the paths become auditable from the transcript rather than only from a log file nobody opens.

**Per-instance paths are unconstrained; per-call filenames are not.** These look like the same decision and are not. `init`'s directory arguments are deliberately unrestricted — [§settled](#settled-2026-08-13) accepts any path, because the caller is declaring where its data lives. A per-call `filename` names a file *within* a workspace already declared, so normalising it into that workspace is not a restriction on the caller's choice; it is honouring the choice already made. Record this distinction, because the two rules read as contradictory to anyone meeting them cold.

**Reject traversal rather than normalising it.** A `filename` of `..\..\..\foo.png` must resolve, be recognised as an escape, and be refused with an LLM-readable error — never silently collapsed into a path that happens to land somewhere.

**Check free disk space at `init`, and only with an O(1) query.** First-run browser provisioning peaks near 0.9 GB and a session then grows unbounded. A refusal at `init` that names the number is recoverable in one turn; a failure partway through a 323 MB download is the `spawn EFTYPE` shape — success-shaped, stderr empty, discovered at first navigation. **This must be a volume free-space query, never a directory walk**: `init` sits on the hot path of every session, and a check that scans the output tree would make the fix slower than the failure it prevents.

#### Where guidance lives: three channels, two of them capped

An MCP server can address the model in three places, and they are seen at different times. Putting the wrong content in the wrong one is why agents forget handles.

| Channel | Seen | Budget | Carries |
|---|---|---|---|
| **Server `instructions`** (on `initialize`) | Always, at session start — Claude Code loads tool *names* and server instructions eagerly and defers schemas | **2 KB, truncated silently** | That `init` comes first, and why. The fact that is useless once it is needed |
| **`init`'s description** | When the model reaches for `init` | **2 KB, truncated silently** | Argument meanings, the real-Chrome-profile warning, and the retention policy — the spec requires retention to be stated *here*, in the creation tool's description |
| **`init`'s result** | Immediately after the call | none | Resolved absolute paths, the layout, the session label |

The server instructions exist to pre-empt the cold-start failure named above: **the first call after a restart will forget the handle.** Only an eagerly-loaded string can reach the model before it makes that mistake. Detail belongs in the result, where there is no budget and the paths are concrete — spending a third of a 2 KB allowance on a directory diagram every agent re-reads at the wrong moment is the wrong trade.

`SixFive7/OutlookAI` is the in-house precedent for treating this as a contract rather than prose: its instructions string lives in `ServerMetadata.cs` and is pinned by tests.

**Why a handle beats a `mode` enum.** A handle is *minted by the server*. The model cannot invent one for a session type it never created, so `browser_cookie_list` cannot be aimed at an interactive session by choosing the wrong string — an interactive handle simply does not permit storage tools. A `mode` parameter, by contrast, is a value the model composes fresh on every call and can compose wrongly. The handle converts a model-authored assertion into a server-issued capability reference, which is a materially stronger position.

It does not close the gap entirely: a connection holding both an interactive and a persistent handle can still route a call to the persistent one. But that grants nothing new — an agent holding a persistent handle was already entitled to those cookies. **The interactive guarantee holds**, and that is the one that matters.

**Critical constraint — read before designing `init`:** the MCP spec (2026-07-28, *Tools § Capabilities*) states the tool set "**MAY** change over time … but **MUST NOT** vary per-connection or as a side effect of other requests on the connection." SEP-2567 removed protocol-level sessions outright. Separately, `notifications/tools/list_changed` is unreliable in practice — Claude Code registers no handler for it (issues [#13646](https://github.com/anthropics/claude-code/issues/13646), [#4118](https://github.com/anthropics/claude-code/issues/4118)).

**Therefore `init` cannot shrink the tool list.** There is one static list; session-inappropriate calls must be **rejected at runtime by BrowserAI**, keyed on the handle's type. Storage tools remain *visible* in every session and are refused at call time. See [Trade-offs](#known-trade-offs) for what this costs.

**Obligations the handle design creates:**

- **Every tool schema gains a required `handle` parameter**, injected by BrowserAI into the raw `inputSchema` of all ~69 tools. Do this on the `JsonElement`; never materialise a typed schema to do it. Keep the injection order-stable — the spec SHOULDs deterministic tool ordering for prompt-cache hit rates.
- **Missing, unknown and expired handles need three distinct, LLM-readable errors.** That text is read by a model deciding what to do next, not by a human tailing a console — the same principle as the current launcher's browser-preflight message, which exists precisely because "the server is stuck" was the wrong conclusion to invite.
- **The first call after a cold start will forget the handle.** Design for it: the error must name `init`, state what it needs, and be recoverable in one turn.
- **Instance lifetime is BrowserAI's to define.** At minimum: explicit teardown, an idle timeout, and **stdin EOF as the backstop** that reaps everything — EOF fires instantly when the parent holding the pipe is `TerminateProcess`d (measured), and the SDK already treats it as shutdown.
- **N children per process.** One BrowserAI now supervises several `node` children, each with its own config, stderr stream and directory locks. One job object should contain them all; stderr must be demultiplexed per handle or diagnostics become unreadable at exactly the moment they matter.
- **`browser_get_config` becomes per-handle** — and is the natural per-instance drift check.

### D. Locking and single-instance

Keyed on the **resolved absolute directory**, not on a repository name. Must handle: stale locks from crashed processes, alive-but-orphaned holders, and PID recycling. The existing launcher's mutex + sibling-lockfile + signature-check pattern solves all three and is worth porting rather than redesigning.

**You cannot put a path in a mutex name.** Backslashes are illegal after the `Global\` prefix — `"Global\C:\Source\..."` throws `DirectoryNotFoundException`. Canonicalise and hash instead: `Path.GetFullPath` → `TrimEnd('\')` → `ToUpperInvariant()` → SHA-256 → hex, then `$"Global\BrowserAI-{hash[..32]}"`. (The real length limit is ~32,000 characters, not the documented 260, but hashing is required regardless.) `Global\` also needs `SeCreateGlobalPrivilege`, which interactive users have and low-integrity/AppContainer processes do not.

Prefer a `FileStream` with `FileShare.None` for the sibling lockfile over the current signature-heuristic approach: the OS releases the handle on process death, so "stale" and "alive" become distinguishable without guessing. Keep the mutex as well — it gives the fast no-IO path.

#### Never by image name

**Killing a user's own `chrome.exe` or `firefox.exe` must be impossible by construction, not merely avoided.** This is a structural rule, not a review item, because a review already passed on code that would have violated it: our own Chromium probes counted and killed by image name — harmless for Chromium on that machine, and it would have killed ~40 personal `firefox.exe` processes if adapted naively.

The invariant: **BrowserAI can only terminate a process that belongs to a job object it created, or whose identity it verified against a path it owns.** Two mechanisms, no third:

- **The job object** covers everything spawned in this process's lifetime. Closing the handle terminates exactly its members — no name, no PID list, no filter, so there is nothing to get wrong. A user's browser cannot be a member; it was never assigned.
- **Path-keyed identification** covers anything that outlived us. The match is on *our own* session directory, which by construction cannot name a personal profile in `%LOCALAPPDATA%\Google\Chrome\User Data`.

Forbidden outright, and enforceable as an analyzer at error severity: `Process.GetProcessesByName`, `taskkill /IM`, and any WMI or toolhelp query filtered by executable name. Assert zero occurrences in the tree.

> ⚠️ `--user-data-dir` alone is **not** an ownership signal. Measured on the maintainer's machine 2026-08-15: Discord, VS Code, Signal, Teams, WhatsApp, Steam, ChatGPT and four `msedgewebview2.exe` processes all pass it. Only an exact match against a directory BrowserAI created is safe.

### E. Lifecycle and observability

Non-negotiable, because every bug this month was a visibility failure. The .NET MCP SDK already implements roughly half of this — `StandardErrorLines` wired before `Start()`, a rolling stderr tail, and a `StdioClientCompletionDetails { ProcessId, ExitCode, StandardErrorTail }` type that makes bug #2 in the table above structurally impossible.

- **Capture the child's stderr from before it starts.** `RedirectStandardError` + `ErrorDataReceived` + `BeginErrorReadLine()`. The anonymous pipe exists before `CreateProcess` and the kernel buffers, so nothing written earlier is lost (measured: 5 lines survived a 3 s delay *and* child exit). The real risk is the opposite — a full pipe blocks the child.
- **Record the child's real exit code and cache it as an `int` immediately.** .NET is *worse* than PowerShell here, not better: `Process.ExitCode` **throws** after `Dispose()`, and `Process.GetProcessById(pid).ExitCode` **always** throws. Microsoft's own SDK carries a `beforeDispose` callback commented "to read ExitCode before Dispose() invalidates it" — they hit this too.
- **Use `await WaitForExitAsync(ct)`**, never `WaitForExit(int)`. Only the former drains the async readers.
- **Distinguish error-shaped stderr from benign output.** Port the two regexes verbatim; a healthy start prints `Session: <path>` every time.
- **Kill the descendant tree with a Windows Job Object**, not `Process.Kill(entireProcessTree: true)`. The latter requires BrowserAI to be alive and running code — which is exactly the case that fails. The full contract, measured end to end, is [below](#zero-process-leakage-the-job-object-contract).
- **Identify a process by `(pid, creationFileTime)`** — never by a bare PID, which identifies nothing once PIDs recycle, and never by image name (see [§D](#never-by-image-name)). Better still, hold an `OpenProcess` handle: Windows will not recycle a PID while a handle is open, and the handle is signalled on exit, so liveness becomes event-driven instead of a poll loop.
- **There are no orphaned processes to collect after a crash.** The job object takes the whole tree with it, so a dead BrowserAI leaves no running children — an earlier draft of this document said the opposite. What *can* survive is a stale lockfile, and that is a file problem solved by `FileShare.None` plus the holder record, not a process problem.

**stdout is the protocol channel and it is wrong by default.** Measured: `Console.Out` writes CP437, not UTF-8 (`é` → `0x82`); `Console.InputEncoding` also defaults to CP437; any `TextWriter` emits CRLF; and a hand-rolled `new StreamWriter(stream, Encoding.UTF8)` emits a BOM. Own the raw streams, never touch `Console.Out`, and let no code anywhere in the process call `Console.WriteLine` — including inside a `catch`. This should be a reviewed invariant owned by one wrapper type, not a convention.

#### Zero process leakage: the job object contract

Verified end to end 2026-08-15 against real Chromium and Firefox trees: **16 runs, 106 spawned processes, 0 escapees, 0 survivors.** The measurement harness lives at `.work/jobtest/` and is the working prototype of the acceptance test below.

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

**NativeAOT.** Use `[LibraryImport]`, not `DllImport` — the latter relies on runtime IL-stub generation. `LibraryImport` does not support `StringBuilder`, so pass the command line as a writable `char[]`/`Span<char>`: `CreateProcessW` mutates the buffer, so a `string` literal is not valid. Keep `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` and `STARTUPINFOEX` blittable and consider `DisableRuntimeMarshalling`. Nothing here touches AOT's limitation list — no dynamic loading, no `Reflection.Emit`, no built-in COM.

**BrowserAI running inside someone else's job is safe**, measured in all three ancestor configurations: an outer job with `KILL_ON_JOB_CLOSE` only (a CI runner), one that also permits breakaway, and one with `SILENT_BREAKAWAY_OK` — which is the realistic case, since any MCP client that spawns BrowserAI through Node `child_process` puts us in libuv's job.

**The acceptance test.** Launch the child, enumerate via `QueryInformationJobObject(JobObjectBasicProcessIdList)`, assert `IsProcessInJob` is true for every PID in the descendant tree, hard-kill the launcher from outside, assert every PID is gone and every profile directory can be deleted — a directory that still holds a lock proves an escaped browser. Cross-check the job's PID list against a toolhelp descendant walk seeded from an I/O completion port on the job, so a process whose parent already exited is not missed. **Never match, count or terminate by image name at any step**; act only on PIDs recorded at spawn, validated against recorded creation time.

### F. Artifact management

Playwright writes every artifact flat into one directory with a generated name, mixing machine churn with hand-named work. Nine fixed generator prefixes make classification exact rather than heuristic: `console`, `download`, `network`, `page`, `request`, `response`, `result`, `storage-state`, `video`.

Port the prefix-based sort. **Classification must be by generator prefix, never by date** — that is precisely what keeps a hand-named file out of the machine-generated folders, and no date rule can make that distinction.

### Route on the way in, do not sort on the way out

A sort is cleanup; a proxy can do better. BrowserAI sees every `tools/call` before the child does and every result before the caller does, so **files can be born in the right place instead of being swept there.** Three levers, in increasing order of effort:

1. **Set the child's `WorkingDirectory` to the instance's output root.** Already required by [§Windows process spawning](#windows-process-spawning) for a different reason. It makes the stray-file failure *impossible* rather than *caught*: a bare `foo.png` resolves inside the instance tree by construction. Ten repositories currently run a `deny` hook because upstream resolves a relative `filename` against the child's cwd — this closes that without a hook.
2. **Normalise `filename` on the way in.** Route it to the typed subfolder its generator prefix implies, so the agent never has to construct a path.
3. **Return the resolved absolute path on the way out.** Non-negotiable if lever 2 is used. Silently relocating a file while telling the model it went somewhere else produces an agent that confidently reports the wrong location — a new silent failure introduced by the fix for an old one.

**The default root must be outside any repository.** When `init` is given no path, artifacts go to `%LocalAppData%\BrowserAI\sessions\<label>\` — never the current working directory. The founding stray-file problem is files landing in repo roots; a default that cannot land there makes the common case safe without relying on the caller.

Layout beneath the root, following the nine generator prefixes rather than inventing new ones:

```
<root>\<label>\
  screenshots\   video\      traces\
  network\       console\    downloads\
  storage\       results\
```

**`downloads\` is the one exception to routing** — a browser-initiated download lands where the browser puts it, not where a `filename` argument says. It is classified after the fact like the old sort, and that difference should be visible in the code rather than discovered.

### Three obligations that follow

- **Names must be legible.** Upstream generates `page-2026-08-14T04-11-50-882Z.png`. Prefer the caller's own name where one was given, and a page-derived slug plus a counter where none was — `checkout-step-3.png` survives a month, a timestamp does not. This is what made 346 session directories unnavigable.
- **Never overwrite silently.** Two screenshots named `login.png` in one session is data loss. Suffix, and say so in the result.
- **Report cumulative session size in the result.** The current setup reached 1.5 GB in three months with nothing saying so. BrowserAI routes every file and therefore knows; not reporting it is a choice to stay blind.
- **Return a repository-relative path alongside the absolute one.** When an agent writes a commit message, a PR body or a report, `docs/screenshots/login.png` is what it needs; an absolute path is machine-specific and useless there. BrowserAI resolves both anyway — emitting only one of them discards work already done.

### The session index

Routing means BrowserAI knows, at write time, every fact worth recording: which tool produced a file, when, at which URL, under which handle and session type. Not writing that down throws away information that cannot be reconstructed afterwards — which is exactly how 346 session directories became untriageable. **The label tells you what a session was; the index tells you what is in it.**

Write `session.json` into each session folder as artifacts are routed: label, handle, session type, created and last-touched timestamps, resolved absolute paths, and one entry per artifact with its tool, timestamp, page URL, size and both path forms.

**Scope the roll-up by root, never by machine.** BrowserAI is registered once and serves every repository on the host, so an index that aggregates everything would pull sessions from unrelated projects into whatever context happens to be open. That is a **noise problem rather than a security boundary** — the paths were the caller's own choice and [§settled](#settled-2026-08-13) accepts any of them — but noise in an agent's context is a real cost, and the cheap fix is to default the aggregate to the root in play:

- A roll-up index sits at each **output root**, covering sessions beneath that root only.
- `init`'s result names prior sessions under the same root — count and labels, nothing wider.
- A machine-wide view stays available and is **opt-in**: an explicit request for everything, or for a root that happens to contain everything, returns everything. Scoping is a default, not a restriction.

**No new tool is needed for any of this, and none should be added.** The index is a file; the calling agent reads it with its own filesystem tools. A `browser_list_artifacts` tool would be BrowserAI composing a capability out of its own state — the boundary this document holds everywhere else, and there is no reason to cross it for something `Read` already does.

### Retention is no longer ours alone to promise

An earlier draft of this section said *"Nothing is ever auto-deleted."* That is **no longer true of the runtime**: `@playwright/mcp` now carries `--output-max-size <bytes>`, *"Threshold for evicting old output files, in bytes."* Unless BrowserAI sets it explicitly and asserts it through `browser_get_config`, the promise in this document is not the promise the child keeps — and a silently evicted artifact is precisely the failure class in the opening table. **Its default value is unverified; establish it before restating any retention guarantee.**

### G. Updates

The reason the project exists. Requirements: updates published on **our** schedule after we have validated a new Playwright version; one release updating runtime, browser, config, opinions and BrowserAI itself; rollback to a known-good release; and silent background update with a clear "restart to apply" signal, since MCP servers are long-lived child processes.

**Mechanism: Velopack 1.2.0** (MIT, per-user install to `%LocalAppData%`, no elevation, no commercial tier).

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

Landmines 1 and 5–9 above were not found in documentation. They were found in a working Velopack deployment: **`ExoFabric/UCC`** ships Velopack today — per-user `%LocalAppData%\UCC\current\`, no elevation, S3-compatible feed, silent background check — and is the only in-house evidence that exists. Two caveats before borrowing anything from it:

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

The wider point: UCC's update path has been in production for multiple releases, and **five of the nine landmines above are ones it hit rather than avoided** — 1, 2, 5, 6 and 8, of which 2 and 8 are still live. None of them announced itself. That is the same failure class as everything in the opening table, and it is why §G is enumerated rather than described.

---

## Versioning policy: everything floats, the build freezes it

Adopted from `SixFive7/Jeeves` — `V1.md` § *Versioning policy: never pin* and its *Modernity doctrine* — and applied here **without exception, including to the shipped payload**.

**Every dependency floats to the latest release at build time.** NuGet packages, `@playwright/mcp` and the `playwright-core` it carries, `node.exe`, both Chromium builds, `ffmpeg`, `winldd` — the build resolves each to the newest available version and then **freezes what it resolved into the artifact**. For .NET, the newest **GA** major, adopted at each annual GA, **including when that major is STS rather than LTS**. `LangVersion` is `latestMajor`, never `preview`.

**Every version number in this document is a floor and a provenance stamp, never a target.** The versions named in [Implementation stack](#implementation-stack) and [§A](#a-ship-and-own-the-runtime) record what was current when each claim was verified, so a reader can tell how stale the prose is. **The build does not read them.**

> **Why:** stale dependencies are a defect, not a safety measure. Riding the newest release keeps each upgrade small instead of saving them into one nobody ever takes, and [Testing](#testing-a-hard-requirement-and-the-release-gate) is what catches breakage. A version pin is not a substitute for a test suite — it is a way of not finding out.

**Latest patterns are the house style**, not merely latest versions. Code is written with the newest stable idioms the GA toolchain actually compiles, and rewritten toward them; familiar-but-dated patterns are style defects. Style is law rather than advice — analyzers run at error severity, and a severity is never weakened to make code pass.

### The pin is an output, not an input

This is the whole model, and it needs stating precisely because the charter's founding complaint was *about* floating:

| | Today (`launch.ps1`) | BrowserAI |
|---|---|---|
| **When** the version resolves | Every spawn | Once, at build time |
| **Where** it resolves | The user's machine | Ours |
| **What validates it** | Nothing | The full suite, which must be green |
| **What the client runs** | Whatever npm served that minute | Exactly the bytes we tested |
| **Failure surfaces** | Silently, in production, five days later | As a red test, before anything ships |

[Why-reason 2](#2-the-version-chain-floats-and-it-breaks-silently) is not weakened by this — it is sharpened. **The defect was never that the version moved. It was that it moved untested, unobserved, and on someone else's machine.** Moving it under a test suite before anything ships inverts every row in that table. The artifact a client installs is still exactly fixed; what changed is that its contents are *derived from a green build* rather than hand-typed into a config file and left there.

### The four rules that make floating safe

1. **The resolved set is recorded, not remembered.** For NuGet this is **two steps, not one**: `dotnet restore --force-evaluate` to resolve the float, then a locked-mode restore to verify. They are mutually exclusive in a single invocation — with a lock file present and no `--force-evaluate`, NuGet **does not re-resolve**, and the float is silently dead ([NU1512](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1512); warned by default from the .NET 11 SDK). A one-step `--locked-mode` build is the `browserName: "chromium"` failure again. It also yields the cheapest possible drift detector: `git diff --exit-code -- "**/packages.lock.json"` after the resolve. Then the resolved `package-lock.json` for the vendored npm tree, and browser revisions read from the resolved package's own `browsers.json`, never a hand-typed URL. **An artifact that cannot state exactly what went into it is not releasable** — that is also what makes a rollback meaningful and a regression bisectable.
2. **The shipped artifact never floats.** The client resolves nothing at runtime: no `npx`, no `@latest`, no network at spawn. What was tested is what runs. This property is the non-negotiable one, and it is why [§A](#a-ship-and-own-the-runtime) vendors the tree at all.
3. **GA is a hard floor.** No preview or RC builds a released artifact. Upstream Playwright publishes **daily alphas**, but `@playwright/mcp@latest` is the released dist-tag — the `playwright-core` alpha beneath it arrives as that package's own exact dependency, not as a choice we make.
4. **Green is the only gate, and it gates the *release*, not the *update*.** The response to a breaking upstream change is to make the newest version work. Holding the previous version is not a fix, and "pin it back for now" is the failure this policy exists to prevent.

### Never assert a version from memory

**If it was not looked up this session, it is unverified — say so.** Model training knowledge lags this toolchain by design, and a confident stale version is worse than an admitted gap. Same discipline as the `Verified <date> @ <version>` stamps in [Reference material](#reference-material), applied to the act of writing a version rather than to the value written.

> **In-house evidence, and it is three weeks old.** `SixFive7/OutlookAI` pins `ModelContextProtocol` **1.4.1**, with a csproj comment reading *"1.4.1 = latest stable on nuget.org as of 2026-07-23 (2.0.0 is still preview)."* That was true when written. Re-checked against nuget.org's flat-container index on **2026-08-14**: 2.0.0, 2.1.0 and 2.2.0 have all shipped stable, so the comment's central claim is now false and nothing in that build says so.
>
> The comment was **correctly stamped with its date**, which is the only reason the staleness is detectable at all — an unstamped "latest stable" claim would still read as current. Stamp the date; never trust one that lacks it.

---

## Implementation stack

Verified 2026-08-13. **The versions below are provenance stamps, not targets** — see [Versioning policy](#versioning-policy-everything-floats-the-build-freezes-it). The build resolves the latest of each; these record what was current when the surrounding claims were checked, and carry the same convention as `playwright/README.md`: re-verify on every bump.

| Concern | Choice | Notes |
|---|---|---|
| MCP protocol | `ModelContextProtocol` (latest; **2.2.0** as of 2026-08-13) | Apache-2.0, 23.6M downloads. **Tier 1** SDK under the MCP project — which Anthropic donated to the Linux Foundation's **Agentic AI Foundation** on 2025-12-09, so "official" now means LF-governed, with day-to-day engineering by the Microsoft .NET team. Began as `PederHP/mcpdotnet` (now archived). Full `2026-07-28`. The main package's hosting dependency is abstractions-only — it does **not** drag in ASP.NET. `ModelContextProtocol.Core` alone is a viable smaller surface (`McpServer.Create` + `StdioServerTransport`, and the `[McpServerTool]` attributes already live there) at the cost of `AddMcpServer()` and assembly scanning — not worth it unless the hosting stack becomes a problem. Verified 2026-08-14. |
| Updates | **Velopack 1.2.0** + `vpk` 1.2.0 | MIT. See §G. |
| Node runtime | **v24.19.0 LTS**, `node.exe` only | v26 is Current, not LTS, and its `node.exe` is 10 MB larger. |
| Job objects | Hand-rolled `[LibraryImport]` | No credible NuGet wrapper exists — the candidates have <6K downloads and the newest was published in 2017. `dotnet/runtime` [#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in support and was closed as not planned. ~60 lines. `Microsoft.Windows.CsWin32` is the reasonable alternative once a seventh Win32 API is needed. |
| Parent PID | `NtQueryInformationProcess` | ~0.77 µs/call vs ~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. This is what `dotnet/runtime` itself uses. |
| Tests | **TUnit** (latest; 1.65.0 as of 2026-08-13) | MIT, source-generated, reflection-free, **MTP-native**. Matches `SixFive7/Jeeves`. 1.0 shipped 2025-11-05; ~623K downloads/mo and growing 2.24× YoY. Chosen over xUnit v3 because [we do not vendor the SDK's fixtures](#we-write-our-own-harness) — that was xUnit's only argument here. |
| Snapshots | `Verify.TUnit` (latest; **31.28.0** as of 2026-07-31) | Exact parity with `Verify.XunitV3` — same monorepo, same release, and Verify's own repo carries *more* test projects for the TUnit integration than the xUnit v3 one. |
| Assertions | TUnit built-ins | `await Assert.That(actual).IsEqualTo(expected)`. **Never add FluentAssertions** — it relicensed at exactly 8.0.0 to a bespoke non-SPDX licence with a commercial tier. Jeeves carries the identical prohibition for the identical reason. |
| External smoke | `@modelcontextprotocol/inspector` **2.2.0** | Language-independent CI check. Exit code **5** means the tool reported `isError` — the signal `claude mcp` does not give you. |

### Three places where the SDK must be deviated from

**1. Write your own `IClientTransport`.** The SDK's `StdioClientTransport` prepends `cmd.exe /c` to every non-cmd command on Windows, unconditionally. That directly contradicts §"Windows process spawning" below: it adds a shell layer, an extra process between BrowserAI and `node` (complicating tree ownership and exit-code attribution), and cmd.exe quoting semantics. The interface is two members (`Name`, `ConnectAsync`) and the replacement is ~120 lines. Port `StdioClientTransportOptions`' stderr and shutdown handling rather than reinventing it.

**2. Use the raw `ListToolsAsync` overload.** The convenience overload `ListToolsAsync(RequestOptions?, ct)` **silently drops** any tool whose `x-mcp-header` annotations fail SEP-2243 validation. A proxy must call `ListToolsAsync(ListToolsRequestParams, ct)`, which returns the server's result unfiltered. Using the wrong one shrinks the exposed surface with no error anywhere — the same failure class as everything in the opening table.

**3. Proxy `tools/call` at the message-filter layer, not the typed layer.** The SDK's `ContentBlock` converter **silently drops unknown properties** (it has tests asserting this — correct forward-compatibility for a client, data loss for a proxy) and **throws on unknown content *types***, which fails the entire call at deserialization before any BrowserAI logic runs. `WithMessageFilters` operates on `JsonRpcMessage` where `JsonRpcResponse.Result` is a raw `JsonNode?`. Rewrite `tools/list` typed; let `tools/call` results pass through as raw JSON.

Both of these are places where the SDK's design goal (a forward-compatible *client*) and BrowserAI's (a lossless *proxy*) genuinely differ. Decide them now rather than discovering them later — and note that both failure modes are silent, which is the class this project exists to eliminate.

---

## Testing: a hard requirement, and the release gate

> **This section is a requirement, not an aspiration, and it is not severable from the section above.**
>
> [Versioning policy](#versioning-policy-everything-floats-the-build-freezes-it) puts every dependency on latest at build time. That makes the suite **the only thing standing between an upstream change and a shipped regression** — floating without a suite that can catch a breaking change is strictly worse than pinning. The two decisions are one decision: neither is valid alone, and weakening the suite silently converts the versioning policy into a liability.
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

So: any smoke test that stops at `tools/list` reproduces the exact five-day blindness described at the top of this document. The minimum viable assertion is a real navigation — measured at **0.43 s** with no network and no local server:

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

We do **not** vendor the MCP SDK's test fixtures. They are 1,082 lines (Apache-2.0, unpublished to NuGet), they wire a single client↔server pipe pair where a proxy needs two hops, and copying them means a permanent three-way merge against an upstream that edits `tests/` weekly. Writing ~100–200 lines ourselves buys a harness shaped for *this* product and frees the framework choice.

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

- `tools/call` results pass through **byte-identical**, including image and binary payloads
- **unknown content *types* pass through** rather than throwing (the SDK's typed layer throws; a proxy must not)
- **unknown *properties* survive** (the SDK's converter drops them, with tests asserting it does)
- `isError: true` bodies preserved verbatim; nested JSON-RPC `error.data` not flattened
- `handle` injected into every schema, order-stable
- cancellation relay: an upstream `notifications/cancelled` produces a downstream one, not just a local abort
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
4. **Green, or stop.** A failure is a work item, never a waiver. If upstream broke something, the fix is to make the new version work — [rule 4](#the-four-rules-that-make-floating-safe).
5. **The maintainer decides.** Green is necessary and not sufficient: a green build is *releasable*, not *released*.
6. **Cut it.** `vpk pack`, publish, and record the resolved set alongside the artifact so the release can state exactly what it contains.

**Why manual is right here, and the condition under which it stops being right.** With one maintainer and a single track, a release pipeline is ceremony around a decision one person makes anyway — and [§G](#g-updates) is already the most hazard-dense section in this document without adding pipeline-authored releases to it. The honest cost: **the gate is only as good as the person invoking it.** It rests entirely on step 3 being *run* rather than assumed. The day a second person can cut a release, that assumption breaks and the gate has to move into automation.

**What manual does not mean.** It does not mean the suite runs when someone remembers. Steps 1–4 are the ordinary build and run on every build, whether or not a release is in view. Manual governs step 5 alone.

---

## What this improves over the current setup

| | Today (`Workspace657/playwright/`) | BrowserAI |
|---|---|---|
| Update path | Copy-paste to 13 checkouts | One release, one channel |
| Playwright version | `@latest`, re-resolved at every spawn, on the user's machine, untested | Resolved to latest at build time, gated by the suite, frozen into the artifact |
| Chromium | Downloaded on first use, ~300 MB, needs preflight + retry protocol | Shipped in the installer, both builds, before first use |
| Node | Must exist on the host | Bundled (`node.exe`, 88.5 MB) |
| .NET / Chrome | n/a / required for headed modes | Neither — NativeAOT single-file, bundled Chromium |
| Browser patching | Chrome self-updates | **Ours to ship.** A Chromium CVE is now a release obligation |
| Tree teardown | `Get-CimInstance` walk + `Stop-Process`; nothing survives a hard kill of the launcher | Job object — the kernel reaps the tree even if BrowserAI is `TerminateProcess`d |
| Lock granularity | Repository folder name | Resolved profile directory |
| Output directories | Static, relative, cwd-dependent | Explicit argument to `init` |
| Profile | 13 separate ones by accident | One, deliberately — or per-directory by choice |
| Config validity | Unknown keys silently ignored | Validated against the shipped runtime |
| Colleague onboarding | Clone repo, replicate 6 files, install Node | Run installer |
| MCP registrations | 4 | **1**, with `init`-issued handles routing to instances |

---

## Known trade-offs

Recorded so they are inherited as decisions, not rediscovered as surprises.

### The `init` design weakens a security boundary

Today, four separate processes give **process-level isolation**. The `interactive` mode exists so a human can type credentials the agent must never capture, and its server process is launched without the `storage` capability — the 17 cookie/localStorage/`storageState` tools do not exist in that process. There is no code path to reach them.

Under one server with `init`, that becomes a **runtime check in BrowserAI's own code**. The tools exist in the shared surface; correctness depends on the session-state lookup being right, including under concurrency.

To be precise about the size of this: it is *not* a demotion to "the model must behave" — BrowserAI enforces it server-side, and a model that calls `browser_cookie_list` in an interactive session gets refused. It *is* a demotion from "the capability does not exist in this process" to "our code declines to use it." Weaker, and worth the eyes-open acknowledgement.

The handle design ([§C](#c-the-init-tool-and-instance-handles)) narrows this considerably — a server-minted handle cannot be forged for a session type the agent never created — but it does not change the *kind* of guarantee. It remains our code declining, not a capability that does not exist.

**Requirement:** session-type enforcement must be centralized in exactly one place, deny-by-default, and unit-tested against every tool in the surface. A new upstream tool must be *unreachable* until explicitly classified — never reachable by default. With N concurrent instances in one process, the handle→type lookup is now shared mutable state on the hot path of every call: it must be correct under concurrency, and that is a test, not an assumption.

### Storage tools capture bearer tokens

`browser_storage_state` and the cookie tools return `httpOnly` cookies, which JavaScript cannot read. These are session bearer tokens. Any mode permitted to call them must be treated as credential-bearing.

### `browser_get_config` does not redact

Its handler is `JSON.stringify(context.config, null, 2)` with no filtering, so it would emit `config.secrets` in plaintext if that key were ever set. It is not set today. BrowserAI should either redact before forwarding or refuse to expose the tool.

### The child's environment overrides the config file BrowserAI generates

The merge order is **config file → environment → CLI**, and `@playwright/mcp` reads **40** `PLAYWRIGHT_MCP_*` variables covering essentially every option: `BROWSER`, `HEADLESS`, `USER_DATA_DIR`, `EXECUTABLE_PATH`, `OUTPUT_DIR`, `ISOLATED`, `CONFIG`, `SECRETS_FILE`, `STORAGE_STATE`, `CAPS`, and 30 more.

So a stray variable in the user's environment silently overrides BrowserAI's opinions — and **`PLAYWRIGHT_MCP_CAPS` triggers the same replace-not-merge wipe documented below for `--caps`**, meaning there is an environment route to a bug the "never pass `--caps`" rule does not close.

**Requirement: build the child environment allowlist-style, never inherited-and-patched.** `ProcessStartInfo.Environment` is pre-populated with the inherited block and assignment *merges*, so `Environment.Clear()` must come first. Also strip `INIT_CWD`, `NODE_OPTIONS`, `NODE_PATH`, `DEBUG` and `DEBUG_FILE`, and set `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` and `PLAYWRIGHT_SKIP_BROWSER_GC=1` — the latter because Playwright's stale-browser GC deletes any registry directory not referenced by a `.links` entry, and the blast radius is "deletes BrowserAI's shipped Chromium."

Do **not** set `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS`: it writes a line to stderr, which trips the error-shaped-stderr detection in §E.

### `capabilities` replaces, it does not merge

`mergeConfig` spreads defined overrides, so passing `--caps` on the command line **silently wipes** the config file's capability list. The current launcher passes only `--config` and `--output-dir`, which is why this has not bitten. BrowserAI generates the child config and must not introduce a `--caps` argument alongside it — nor allow `PLAYWRIGHT_MCP_CAPS` to survive into the child environment.

### Windows process spawning

Node's `spawn` cannot execute `.cmd` shims without `shell: true` — a live Claude Code bug ([#58510](https://github.com/anthropics/claude-code/issues/58510)) for plugin-shipped servers using bare `npx`. Since BrowserAI ships its own Node, it must invoke the resolved `cli.js` with the bundled Node executable directly and never depend on PATH resolution or shell shims:

```
<install>\node\node.exe  <install>\mcp\node_modules\@playwright\mcp\cli.js  --config <abs path>
```

**This is also why the SDK's own `StdioClientTransport` cannot be used** — it prepends `cmd.exe /c` unconditionally on Windows. See [Implementation stack](#implementation-stack).

Set `WorkingDirectory` explicitly on every spawn. Left unset, .NET passes `null` to `CreateProcess` and the child inherits BrowserAI's cwd — whatever Claude Code happened to have. That is reason 5 above, verbatim.

### Being in the data path is a new failure domain

The current launcher is a **supervisor, not a proxy** — it inherits stdio and never touches a JSON-RPC message. BrowserAI is in the data path by design. A crash in it takes down browser automation entirely, where today a crash in one mode leaves the other three working. Error propagation, cancellation, progress notifications, and binary/image passthrough all become BrowserAI's responsibility.

Prior measurement of an equivalent Node prototype: images passed through byte-identical (509,620 base64 bytes), error shapes preserved, ~50 ms added latency on a 500 KB payload, ~300 ms one-off child spawn. The overhead is acceptable; the ownership is the real cost.

Two pieces of ownership the SDK does **not** hand you:

- **Progress notification relay.** The child's `notifications/progress` arrives keyed to the token BrowserAI minted; re-emitting it upward with the caller's token needs your own token map. Nothing bridges this automatically.
- **Cancellation relay.** A `CancellationToken` cancels the outbound child request, but it is unverified whether an upstream `notifications/cancelled` produces a downstream one rather than just a local abort. Needs a test — it is precisely the kind of thing that fails invisibly.

### Why C#, and what it costs

Recorded because the research raised it from two directions and the answer is not obvious.

**C# is not required for the update story.** Velopack is language-agnostic — the same 1.2.0 release ships `lib-nodejs`, `lib-rust` and `lib-python`, and the Rust `Update.exe` doing the real work is identical for all of them. In Node, "bundle a Node runtime" would also stop being a payload shipped *alongside* the app and become the app's own runtime.

**C# is chosen for §D and §E.** Node's `child_process` has **no job object support at all** — every Node process supervisor on Windows falls back to `taskkill /T /F` or a native addon, and none survive a hard kill of the supervisor. Named mutexes are the same story: `System.Threading.Mutex` is a first-class kernel object with automatic abandonment on crash, where Node needs `proper-lockfile` and its stale-detection heuristics. Those two primitives are exactly the locking and lifecycle requirements above, and they are the things that actually failed in the current setup.

**The price is two failure modes .NET makes *worse*, both silent, both already named above**: `Process.ExitCode` throwing after `Dispose()` (§E), and stdio encoding defaulting to CP437 with CRLF and BOM hazards (§E). Neither is hard to handle; both must be **invariants owned by a single wrapper type**, not conventions maintained by discipline. If those two are handled properly, the choice pays for itself.

---

## Open design decisions

### Settled 2026-08-13

| Decision | Outcome |
|---|---|
| **MCP registrations** | **One.** `init` returns a short server-minted handle; every other tool takes it for instance routing. See [§C](#c-the-init-tool-and-instance-handles). |
| **Profile and artifact locations** | **Arguments to `init`**, not global policy. The caller states where its data goes, per session — so "shared or per-directory" is the caller's choice, not BrowserAI's. This is what removes the relative-path hazard. |
| **Path validation** | **Any path is accepted.** Correct use is the calling agent's responsibility. See below. |
| **Child process model** | **One node child per handle.** Stays firmly on the proxy side of the scope boundary; costs ~300 ms and one process per instance. |
| **Browser and packaging** | **Full Chromium, batteries included, NativeAOT single-file.** No host dependency on Node, .NET or Chrome. ~806 MB installed / ~239 MB compressed. See [§A](#a-ship-and-own-the-runtime). |

**On accepting any path.** `init` does not constrain `userDataDir` or artifact paths to a sanctioned root. The consequence, recorded so it is inherited rather than rediscovered: an agent may point an instance at the user's **real Chrome profile** and read live browser state, or write artifacts anywhere the process can reach.

This is consistent with the trust model already in force — the current `persistent` mode grants any calling agent every login stored in its profile, and the workspace runs in `bypassPermissions`. BrowserAI is not the boundary; the agent's own instructions are.

Two things follow, and they are design obligations rather than caveats:

- **The `init` tool description is a security surface.** It is the only place the calling agent is told what a path argument means. Write it as guidance an agent will actually follow — name the sensible default location, and say plainly what pointing at an existing browser profile does.
- **Log the resolved absolute paths at instance creation.** If BrowserAI does not enforce a boundary, it must at least make crossings visible after the fact. This is the same principle as §E: the failure that cannot be seen is the one that costs five days.

**On one child per handle.** Research verified a single node process *can* serve several configurations — two servers with correctly divergent surfaces (42 vs 59 tools), no module-global browser state, browsers created lazily on first tool call. That path is rejected not on capability but on scope: it is reachable only through the programmatic `createConnection` API, which means writing a JS shim and moving toward the boundary [Scope](#scope-proxy-not-implementation) forbids. Spawning `cli.js` per handle keeps the proxy a proxy.

### Settled 2026-08-14

| Decision | Outcome |
|---|---|
| **License** | **Source-available**, under a bespoke five-year variant of the Functional Source License 1.1 (MIT Future License). Fixed before any code exists, and it constrains dependency selection from here. See [License](#license). |
| **Repository visibility** | **Private for now.** Source-available is the licensing posture, not a commitment to publish. Opening the repository is a separate decision and has not been made. |
| **Third-party payload** | Keeps its own terms. Bundling creates redistribution obligations that bite at first installer handoff *regardless* of which license BrowserAI itself carries — enumerated under [Third-party components](#third-party-components). |
| **Update tracks** | **One.** No beta channel. A second track doubles the release matrix and makes the version string load-bearing — UCC derives its runtime track from a `-beta` suffix, so a formatting change breaks track detection silently. A single track still requires the channel to be set explicitly, for the reason in [§G](#g-updates) landmine 1. |
| **Dependency versioning** | **Everything floats at build time; the build freezes it.** Nothing is pinned by hand, the payload included. The build resolves latest, the suite gates it, the release records exactly what shipped, and the client resolves nothing at runtime. Adopted from `SixFive7/Jeeves` and applied without exception. See [Versioning policy](#versioning-policy-everything-floats-the-build-freezes-it). |
| **Release trigger** | **Manual, by the maintainer, through the agent.** No release pipeline, no scheduled publish, no auto-merge on green. Green is necessary and not sufficient — a human decides when a green build becomes a release. See [The release gate](#the-release-gate). |
| **Test framework** | **TUnit**, matching `SixFive7/Jeeves`. Source-generated, reflection-free, MTP-native, MIT. See [Implementation stack](#implementation-stack). |
| **SDK test fixtures** | **Not vendored. We write our own harness.** The MCP SDK's `ClientServerTestBase` + `tests/Common/Utils/*` (1,082 lines, Apache-2.0, unpublished to NuGet) wire *one* pipe pair; a proxy needs two hops. Copying them would mean a permanent three-way merge against an upstream that edits `tests/` weekly, and would lock the framework to xUnit. See [We write our own harness](#we-write-our-own-harness). |
| **Instance teardown** | **Explicit close tool + client-liveness watcher** (stdin EOF, plus an `OpenProcess` handle on the client PID — never ping-based; `ping` is removed at 2026-07-28). **Expiry is reclaim, not destruction:** a torn-down handle stays resumable against its recorded config and directory, because the durable thing is the profile, not the process. Measured 2026-08-14: resume costs **515 ms** and loses only `sessionStorage`. Timer values remain open — see [Still open](#still-open). |
| **`browser_run_code_unsafe`** | **Hidden in `interactive` sessions.** Demonstrated 2026-08-14: against the default 24-tool surface with zero `browser_cookie_*` exposed, `async (page) => page.context().cookies()` returned an `httpOnly` bearer token. It is in `core`, so no capability setting disables it, and it was the only hole — `browser_evaluate` → `document.cookie` returns `""`, and `browser_network_request` strips `Cookie` and `Set-Cookie`. Hiding it in the one mode that exists to keep human credentials from the agent makes [§trade-offs](#the-init-design-weakens-a-security-boundary)' claim true for the first time; it stays available elsewhere as an escape hatch. |
| **Artifact placement** | **Routed on the way in, not sorted on the way out.** The child's cwd is the instance output root, `filename` arguments are normalised into typed subfolders, and every result carries the resolved absolute path. The default root is outside any repository. Per-instance paths stay unconstrained; per-call filenames do not. See [§F](#f-artifact-management) and [the `init` contract](#the-init-contract). |
| **Tool naming** | **Never renamed.** Names are upstream's byte-for-byte; the only name BrowserAI authors is `init`. Descriptions are append-only. A `deny` hook keyed on `browser_take_screenshot` exists in ten repositories and a rename would disable it silently — but the deciding argument is maintenance: upstream renamed one of its own tools inside four months, and a rename map is a second surface to re-review on every bump. |
| **Upstream review** | **Gated by a marker that fails closed.** A version bump cannot reach a release until someone has reviewed what upstream changed and recorded it. See [The upstream-review marker](#the-upstream-review-marker). |

**Why not permissive.** Apache-2.0 was the runner-up and is the smoothest technical fit: it is what `@playwright/mcp` and the C# SDK already carry, and its §4(b) change-statement requirement is real protection for something distributed as a binary installer nobody reads the source of. It was rejected only because it gives the commercial market away outright. AGPL-3.0 was considered and rejected on the merits — BrowserAI is stdio-only, one machine, one user, **no processes and no ports**, so §13's network-interaction clause could never fire and the license would be inert boilerplate.

### Settled 2026-08-15

| Decision | Outcome |
|---|---|
| **Process containment** | **One job object per instance, `KILL_ON_JOB_CLOSE` and nothing else, assigned at creation via `PROC_THREAD_ATTRIBUTE_JOB_LIST`.** Verified end to end against real Chromium and Firefox trees: 16 runs, 106 processes, **0 escapees, 0 survivors**. The guarantee is conditional on our implementation, not on the browsers — two of the failure modes were proven fatal by measurement. See [the job object contract](#zero-process-leakage-the-job-object-contract). |
| **Never by image name** | **Structural, not procedural.** BrowserAI can only terminate a process belonging to a job it created, or one identified against a path it owns. `GetProcessesByName`, `taskkill /IM` and name-filtered WMI are forbidden and analyzer-enforced. See [§D](#never-by-image-name). |
| **Chromium binary** | **Full Chromium in every mode**, headless included; `chrome-headless-shell` is not shipped. It makes the payload ~120 MB *smaller*, gives Chromium's own `lockfile` and `Chrome_MessageWindow` in every mode, and removes a per-mode branch. The shell's only advantage — it cannot be resurrected after a reboot — is worth less than being findable when it leaks, and the sweep it would avoid must exist anyway for the three headed modes. |
| **Chromium sandbox** | **Passed as the `--sandbox` CLI flag, never the config key**, which is silently discarded. Asserted by a test on the child's resolved browser command line. |
| **Firefox restart registration** | **Disabled on every launch** via `firefoxUserPrefs: { "toolkit.winRegisterApplicationRestart": false }`. The pref is observed at runtime and calls `UnregisterApplicationRestart`. The only place browser resurrection can be prevented outright rather than cleaned up after. |
| **Windows `RestartApps`** | **Never touched.** The maintainer's machine has it enabled, which is the direct cause of the resurrection incident, but it is a personal, global, per-user setting. BrowserAI reads nothing and writes nothing there. |
| **`browser_annotate`** | **`interactive` sessions only.** It opens a dashboard window and blocks until a human finishes drawing; `interactive` is the one mode with a human at the keyboard by design. In `headless` it must be hidden regardless — its window appears even there, breaking the only promise that mode makes. |
| **`--isolated`** | **Never set, in any mode.** It puts the profile in a temp directory deleted on close. Three of the four legacy modes set it; BrowserAI gives every mode a real directory and deletes nothing automatically. |
| **`--output-max-size`** | **Never set, and `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE` stripped from the child's environment.** It is a recursive oldest-first deleter pointed at directories agents choose. Retention is the calling agent's decision, supported by an explicit cleanup tool, not by an eviction threshold. |
| **Console level** | **Exposed on `init`.** The upstream default of `info` silently drops `debug` messages; which trade-off is right is per-session, not global. |

### Still open

1. **What ends an instance?** Explicit teardown tool, idle timeout, stdin EOF, or all three — and what happens to a handle whose child died underneath it. Affects §D locking directly: a lock held by a dead instance is the exact failure the current launcher needed a signature heuristic to survive.

2. **Which capabilities ship by default?** Current: `vision`, `devtools`, `config` on all modes; `storage` on persistent only. `testing`, `network`, and `pdf` are off. Measured cost per instance: base 24 tools, `vision` +6, `devtools` +11, `config` +1, `storage` +17.

3. **How aggressively should the tool surface be curated?** BrowserAI can rename, re-describe and drop tools. Dropping reduces what the model can do; renaming diverges from upstream documentation.

---

## Non-reasons — do not relitigate these

**Token cost is not why this project exists.** Measured 2026-08-13 with `tiktoken` `cl100k_base` against live `tools/list` payloads from `@playwright/mcp` 0.0.79:

| | Eager clients (Claude Desktop, Cursor, older Claude Code) | Claude Code with deferred tool loading |
|---|---:|---:|
| Four servers as registered today | ~23,000 tok | **~985 tok** |
| One perfectly-curated proxy | ~11,600 tok | **~330 tok** |

Claude Code defers MCP tool schemas — they arrive as bare names and load on demand. **The entire achievable saving in that client is ~650 tokens**, around 0.3% of a 200k window. Dropping the `devtools` capability from four JSON files saves a comparable amount for no engineering effort at all.

The number only becomes significant in clients without deferred loading, where a consolidated surface saves ~65%. Worth knowing it exists. Not worth building for, and **not a justification to cite in design arguments.**

---

## Hazard index

Every failure mode surfaced during research, in one checkable list. The overwhelming majority are **silent** — that is why they are enumerated rather than left to prose. Items discussed above are cross-referenced; the rest are recorded here only.

**If you find a new hazard, add it here.** This list is what a reviewer checks the implementation against.

### Protocol and SDK

| Hazard | Consequence | See |
|---|---|---|
| `ListToolsAsync(RequestOptions?, ct)` drops tools failing SEP-2243 `x-mcp-header` validation | Exposed surface shrinks, no error. Use the raw `ListToolsRequestParams` overload | §stack |
| `ContentBlock` converter drops unknown properties | Data loss on passthrough; the SDK has tests asserting this behaviour | §stack |
| `ContentBlock` converter **throws** on unknown content *types* | An additive upstream change fails the entire call at deserialization, before any BrowserAI code runs | §stack |
| Typed client flattens JSON-RPC `error.data` to primitives | Nested error structures are lost. Affects protocol errors only — tool failures travel as `isError:true` data | — |
| A tools-only proxy does not forward **resources or prompts** | `@playwright/mcp` advertises only `tools` today, so nothing is lost now — but a future release adding either would silently not appear | — |
| `McpServerToolCreateOptions` has `OutputSchema` but **no `InputSchema`** | The obvious factory API always reflects the schema from the .NET signature — unusable for a proxy, and it is the one that will be reached for first | — |
| Spec 2026-07-28 SHOULDs a **deterministic tool order** for client-side and prompt-cache hit rates | The rewrite step must be order-stable, not incidentally ordered | — |
| `DiscoverProbeTimeout` is 5 s when the client version is unpinned | Flat 5 s per child spawn against a ~300 ms baseline; presents as "slow", never as an error | §B |
| The child never *rejects* a protocol version — it caps or echoes silently | A mis-negotiation produces nothing to catch. Assert on the negotiated value | §B |
| Progress and cancellation relay are **not** automatic across a proxy | A cancelled `browser_navigate` can leave an orphan running in the child | §data-path |
| `_meta.json` / `_meta.cwd` / `_meta.raw` are read by the child before zod parsing | Undocumented but real, and stripped before the tool sees them — available for BrowserAI to inject (JSON error format, relative-path base) | — |

### Handle routing and instance lifetime

| Hazard | Consequence | See |
|---|---|---|
| The model will call a tool without a handle, especially first thing after a cold start | Every call fails until it recovers. The error must name `init`, state what it needs, and be fixable in one turn | §C |
| Injecting `handle` into ~69 schemas can perturb tool ordering | Spec SHOULDs deterministic order for prompt-cache hit rates; an unstable rewrite quietly costs cache hits on every session | §C |
| Handle→type lookup is shared mutable state on every call's hot path | With N concurrent instances, a lookup race is a session-type enforcement bypass. Must be tested under concurrency, not assumed | §trade-offs |
| N children means N stderr streams | Undemultiplexed, diagnostics become unreadable at exactly the moment they matter | §C |
| A handle can outlive the child it points at | A lock held by a dead instance is the precise failure the current launcher needed a signature heuristic to survive | §still-open |
| `init` accepts **any** path, by decision | An instance can be pointed at the user's real Chrome profile and read live browser state, or write anywhere the process can reach. **Accepted** — correct use is the calling agent's responsibility. Mitigation is descriptive, not enforced: a tool description an agent will follow, plus logging resolved absolute paths at creation | §settled |

### Bundling and AOT

| Hazard | Consequence | See |
|---|---|---|
| Bundling Chromium transfers **CVE response** from Google to us | Every install stays vulnerable until BrowserAI cuts a release. "Update on our schedule" cuts the wrong way here | §A |
| Chromium is not Chrome | No proprietary codecs, no Widevine, different UA and fingerprint surface. Verify the portals actually in use before cutting over | §A |
| NativeAOT forbids reflection-based serialization | `System.Text.Json` needs source-generated contexts. The SDK declares `IsAotCompatible=true` on all non-`netstandard2.0` targets (in-source, `v1.4.1` and `v2.2.0`, 2026-08-14) — the author's claim about their code, not a proof for our usage. Publish AOT and run the suite before committing | §A |
| A floating build with a weakened suite | The versioning policy stops being safe the moment a test is skipped, quarantined or waived. This is the one hazard here that is **self-inflicted rather than upstream** — and the only one whose mitigation is a habit rather than a mechanism | §testing |
| **TUnit: a test passes silently if its assertion is not awaited** | Listed first on TUnit's own troubleshooting page. Green-when-broken — precisely the class this project exists to eliminate. **Mitigation is not optional here:** TUnit's analyzers must run at error severity, never merely enabled | §testing |
| TUnit is MTP-only and conflicts with `Microsoft.NET.Test.Sdk` | The package must be absent, not merely unused. Mixing VSTest-based and MTP-based projects in one solution is explicitly unsupported | §stack |
| Coverlet does not work under MTP | Coverage needs `Microsoft.Testing.Extensions.CodeCoverage`. Diverges permanently from any VSTest-based sibling repo | §stack |
| IDE test discovery is not zero-config under MTP | Visual Studio needs *Use testing platform server mode*; Rider needs Testing Platform support enabled. A developer seeing "no tests" is a config gap, not a broken suite | §stack |
| **`chrome-headless-shell` writes no profile `lockfile`** | Two headless instances share one profile directory with **no error anywhere** — silent corruption of the cookie and storage databases, in the default mode. Measured 2026-08-14. §D's lock is the only protection | §D |
| Killed children leak `browser@<guid>` descriptors | Each is a JSON file in the browsers-registry root holding the absolute `userDataDir` and `workspaceDir`; `BrowserServer.stop()` only removes them when there is **no** `userDataDir`. §A puts that root inside the Velopack payload — a tree that should be read-only and is wiped on update. 28 observed and removed on 2026-08-14 | §A |
| `browser_storage_state` never captures IndexedDB | It calls `storageState()` with no options, so `{indexedDB:true}` is never passed. A "saved" session silently omits it — and the persistent profile carries it, so the tool is *weaker* than doing nothing | §C |
| The `PLAYWRIGHT_MCP_*` count is **42**, not 40 | `PLAYWRIGHT_MCP_PING_TIMEOUT_MS` and `PLAYWRIGHT_MCP_EXTENSION_TOKEN` are read outside the config env mapping. The allowlist test must derive the count from the resolved bundle, never carry a literal | §trade-offs |
| §C's `tools/list_changed` evidence is stale | *"Claude Code registers no handler"* was accurate at 2.0.65 (Dec 2025). At **2.1.231 it is false** — measured twice; the client re-listed in 1–2 ms and the model called a tool that appeared only in the second list. This does **not** unlock a per-connection tool list (SEP-2567 stands), but the cited issues need re-dating | §C |
| Rewriting a `filename` without rewriting the result path | The agent reports a location the file is not at. A new silent failure introduced by the fix for an old one — levers 2 and 3 ship together or neither ships | §F |
| A `filename` containing `..` | Normalising traversal instead of refusing it turns a routing feature into an arbitrary-write primitive | §C |
| Two artifacts with the same caller-supplied name in one session | Silent overwrite is data loss. Suffix and say so | §F |
| `--output-max-size` evicting artifacts BrowserAI promised to keep | Upstream now auto-evicts; §F's retention guarantee is not the runtime's unless BrowserAI sets the flag and asserts it. **Default value unverified** | §F |
| Server `instructions` or a tool description exceeding 2 KB | Claude Code truncates both **silently**. The tail of the guidance simply does not exist, and nothing reports it | §C |
| The upstream-review marker can be discharged without reading anything | It is a speed bump, not a proof. Accepted deliberately — the value is that skipping becomes deliberate and recorded, not that it becomes impossible | §testing |

### Child runtime and configuration

| Hazard | Consequence | See |
|---|---|---|
| `browserName` defaults to system Chrome, `headless:false` on Windows | The bundled Chromium is never consulted. Verified against an empty browsers directory | §A |
| 40 `PLAYWRIGHT_MCP_*` env vars override the generated config | Silent override of every opinion, including a capability wipe | §trade-offs |
| `loadConfig` is a bare `JSON.parse` with no schema validation | Unknown or renamed keys are silently discarded | §why-3 |
| `core-install` is declared in `config.d.ts` but **no tool carries it** in 0.0.79 | A dead capability string; setting it does nothing | — |
| `chromiumSandbox:true` in a **config file** is discarded; only the `--sandbox` CLI flag works | The browser and every child run `--no-sandbox` while the config says otherwise. Pass the flag, and assert `--no-sandbox` is absent from the child's browser command line | §A |
| `--output-max-size` evicts files it did not create | Recursively lists the whole output dir, sorts oldest-first and unlinks past the threshold, skipping only the current response's writes. Unset by default and it must stay unset — **also strip `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`** | §A |
| `--isolated` puts the profile in a temp dir **deleted on close** | Silent total data loss. Structurally impossible for us — `validateBrowserConfig` throws on `isolated` + `userDataDir` — but three of the four legacy modes set it | §A |
| Relative `PLAYWRIGHT_BROWSERS_PATH` resolves against `INIT_CWD` before `cwd` | Points at a directory inherited from any npm ancestor | §A |
| Outer browser dirs use underscores, inner ones dashes | `chromium_headless_shell-1237\chrome-headless-shell-win64\` — a path built consistently is wrong | §A |
| Playwright writes `DEPENDENCIES_VALIDATED` into the browsers root on first launch | Under `Program Files` that write silently fails and re-runs every launch. Prefer `%LOCALAPPDATA%` or `%ProgramData%` with write ACLs | §A |
| `.links/` records the **build machine's** absolute paths | Leaks build paths into the shipped tree; useless on the target. Strip it | §A |
| Playwright's stale-browser GC deletes registry dirs not referenced by `.links` | Blast radius is "deletes BrowserAI's shipped Chromium". Pin `PLAYWRIGHT_SKIP_BROWSER_GC=1` | §trade-offs |
| `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` writes to stderr when set | Trips the error-shaped-stderr detection in §E. Do not set it | §trade-offs |
| The `playwright` package (4.85 MB) is a declared dependency that is **never loaded** | Prunable, but `npm ls` will call the tree broken. Deliberate choice, not an oversight | — |
| Upstream publishes **daily alpha** builds | The `@latest` float is sharper than it appears; another argument for the pin | §why-2 |

### Process and OS (Windows)

| Hazard | Consequence | See |
|---|---|---|
| Backslashes are illegal in a mutex name after `Global\` | A path-keyed lock cannot use the path. Canonicalise and hash | §D |
| `Global\` requires `SeCreateGlobalPrivilege` | Fine for interactive users; fails under low-integrity / AppContainer | §D |
| `Process.ExitCode` throws after `Dispose()`; always throws for `GetProcessById` | Worse than PowerShell's `$null`. Cache the `int` immediately | §E |
| `WaitForExit(int)` does not drain async readers | Truncated stderr. Only `WaitForExit()` and `WaitForExitAsync` drain | §E |
| `Console` stdio defaults to CP437 in **both** directions; `TextWriter` adds CRLF; hand-rolled `StreamWriter` adds a BOM | Corrupts JSON-RPC on the first non-ASCII byte | §E |
| `Process.Kill(entireProcessTree:true)` needs BrowserAI alive and running code | Cannot help when BrowserAI is the thing killed. Job object required | §E |
| `Process.Start` then `AssignProcessToJobObject` races the child's own spawns | **Measured: 2 escapees.** .NET cannot express the fix; `PROC_THREAD_ATTRIBUTE_JOB_LIST` via P/Invoke is mandatory | §E |
| An **inheritable** job handle is inherited by the child | BrowserAI's death no longer closes the last handle, so `KILL_ON_JOB_CLOSE` never fires. **Measured: every child survived.** Redirecting stdio forces `bInheritHandles=TRUE`, so this is always one flag away | §E |
| `JOB_OBJECT_LIMIT_BREAKAWAY_OK` also **arms** Firefox's escape | `NeedToBreakAwayFromJob()` returns true only when the job carries `KILL_ON_JOB_CLOSE` **and** `BREAKAWAY_OK`. Setting it does not merely permit escape, it causes one | §E |
| `JobObjectBasicUIRestrictions` blocks job nesting | Can prevent Chromium's sandbox job from nesting inside ours | §E |
| libuv assigns every non-detached child to its own `SILENT_BREAKAWAY_OK` job | A second kill path exists for free, but it is an accident of Playwright's `detached:false` on Windows. Never depend on it | §E |
| Firefox background tasks and crash reporter request breakaway | Their `CreateProcess` fails `ERROR_ACCESS_DENIED` inside our job. Correct trade, not a bug — do not "fix" it | §E |
| Full Chromium calls `RegisterApplicationRestart` unconditionally | Windows relaunches it after a reboot with `--user-data-dir` intact, plus `--restart --restore-last-session`. The only guard upstream is `--browser-test`. **No supported way to disable it** | §E |
| Firefox registers too, but honours a pref | `toolkit.winRegisterApplicationRestart:false` calls `UnregisterApplicationRestart` at runtime. Pass it via `firefoxUserPrefs` on every launch | §E |
| `chrome-headless-shell` creates no `Chrome_MessageWindow` and no `lockfile` | The one binary that can leak but cannot be cheaply found. Reason enough to run full Chromium in every mode | §A |
| `psi.Environment` is pre-populated and assignment **merges** | An allowlist requires `Clear()` first | §trade-offs |
| `psi.WorkingDirectory` unset passes `null` to `CreateProcess` | Child inherits BrowserAI's cwd — reason 5, verbatim | §trade-offs |
| `ArgumentList` and `Arguments` are **mutually exclusive** | Setting both is undefined behaviour. Use `ArgumentList` for its quoting rules | — |

### Packaging and updates

| Hazard | Consequence | See |
|---|---|---|
| Feed base URL built as `{BaseUrl}/{channel}` | `SimpleWebSource` then requests `{channel}/releases.{channel}.json` → 404, reported as "no update available". **Unrecoverable in the field** — every shipped install needs a manual reinstall. Use `UpdateOptions.ExplicitChannel` | §G |
| A local-directory update source composes feed paths differently from `SimpleWebSource` | An update test that passes against a local folder still 404s in production. The test must resolve the real URL | §testing |
| `SetAutoApplyOnStartup` defaults to **true** | BrowserAI exits(0) at handshake time and relaunches with dead pipes | §G |
| The Velopack execution stub is `windows_subsystem = "windows"` and returns immediately | A stdio client sees the child die instantly with no pipes | §G |
| `force_stop_package` kills every process under the install root | Three other live sessions destroyed mid-task | §G |
| `UpdateManager` constructed merely to read the installed version issues a **network request** | A network call on the stdio startup path. `VelopackLocator` reads local metadata only | §G |
| `NotInstalledException` is thrown by every Velopack call under `dotnet run` and any test host | Not an error — the normal case. `Debugger.IsAttached` does not detect a test runner, so an ungated self-restart relaunches out of the suite | §G |
| `ApplyUpdatesAndRestart(null)` restarts without a package by undocumented fall-through | Works today, breaks silently on any Velopack refactor. Use `UpdateExe.Start(waitPid)` | §G |
| `VelopackApp.Build()` takes a **separate** logger registration from `UpdateManager` | Installer, first-run and post-restart hooks log nothing — the path that runs when something has already gone wrong | §G |
| Spawning the replacement before exit, or releasing the lock before spawning | Both race the §D directory-keyed lock. `Update.exe` outlives the process specifically to close this | §G, §D |
| A single download timeout | Aborts a healthy slow link or hangs forever on a stalled one. Needs absolute + stall + lifetime, and the download must run off the message loop | §G |
| Velopack prunes `packages\` to the current full `.nupkg`; deltas are forward-only | Every rollback is a full ~105 MB download unless you archive packages yourself | §G |
| `AllowVersionDowngrade` off, **or** a strictly-increasing version rule in the release script | Rollback fails at one end or the other. Both halves must agree; UCC has the mismatch today | §G |
| `current\` is wholly replaced on update | All state must resolve from `VelopackLocator.Current.RootAppDir` | §G |
| `AppContext.BaseDirectory` resolves *inside* `current\` | Reads as "next to the binary". Logs and caches written there are wiped by every update, and any retention policy is silently reset | §G |
| Update state persisted alongside the binary | Desyncs across exit-and-relaunch. Derive it from the installed version instead | §G |
| The swap holds old + new `current\` simultaneously | Budget ~600–700 MB transient disk, plus full re-extraction of ~380 MB per update | — |
| Velopack's Rust `Setup.exe`/`Update.exe` carry their **own** Windows floor, separate from .NET's | Can fail before the managed app exists. `--runtime win7` does not help if the installer binary cannot run | §G |
| `vpk` rejects 4-part version numbers | Semver2 three-part only — a build-pipeline failure, not a runtime one | — |
| Delta generation is assumed, not verified | UCC has shipped Velopack for multiple releases and **never produced a delta package**. Delta granularity is why §G chose Velopack | §G |
| Every unsigned `Setup.exe` is a new file to SmartScreen | Lands precisely on colleague onboarding. Azure Artifact Signing ≈ $10/mo buys instant reputation | — |
| MSIX cannot re-register while a package process is running | Disqualifying for a long-lived child. Two production AI tools hit this in 2026 | §G |

### Tooling and CI

| Hazard | Consequence | See |
|---|---|---|
| `claude mcp list` / `get` **exit 0 even when the server is dead** | Unusable as a CI gate without grepping stdout for `✘`. Itself an instance of "reports healthy while broken" | — |
| The official MCP conformance suite is **HTTP-only** (`--url`) | Not directly usable against a stdio server. Needs a test-only listener or a ~50-line bridge | §testing |
| Inspector CLI cannot spawn `.cmd` shims on Windows | Same root cause as #58510. Address `cli.js` with an absolute path | §testing |
| Real screenshots are not byte-stable across runs | Fidelity assertions need a canned blob from the fake child, not a live capture | §testing |
| The SDK's test fixtures are **not published** to NuGet | Vendor ~300 lines from `tests/Common/Utils/` + `ClientServerTestBase.cs` (**Apache-2.0**, keep the upstream headers) and record the upstream versions — they will drift | §stack |

---

## Reference material

| What | Where |
|---|---|
| **Canonical current setup** | [`SixFive7/Workspace657`](https://github.com/SixFive7/Workspace657) → `playwright/` @ `a9ac747`+ |
| Architecture, settings table, drift rules | `playwright/README.md` in that repo — single source of truth |
| Launcher internals: mutexes, watchdog, preflight, artifact sort | `playwright/LAUNCHER.md` |
| Per-mode intent and concurrency model | `playwright/{headless,interactive,tracing,persistent}/README.md` |
| Hook behaviour | `.claude/hooks/playwright-config-hook.ps1`, `.claude/hooks/playwright-screenshot-hook.ps1` |
| MCP specification | https://modelcontextprotocol.io/specification/latest (currently 2026-07-28) |

The `playwright/README.md` settings table carries `Verified <date> @ <version>` provenance stamps on default values, because upstream defaults drift and a bare "Default: X" claim cannot tell you when it was last true. **Carry that convention into BrowserAI.** Motivation lives only in prose — it cannot recover itself from code.

---

## Status

**Design phase. Nothing is built.**

Feasibility research completed 2026-08-13 across five streams: MCP SDK capability, Windows auto-update, Node/Chromium bundling, .NET process supervision, and test harness design. Everything marked *measured* or *verified* in this document was executed on a real machine against `@playwright/mcp` 0.0.79 — not inferred from documentation. Three claims in the first draft of this charter were wrong and have been corrected: the tool count, the framing of the protocol split as a risk, and the assumption that a bundled Chromium is used by default.

**Architecture is settled.** One MCP registration with `init`-issued handles · profile and artifact locations as `init` arguments · any path accepted, correct use being the calling agent's responsibility · one node child per handle · full batteries-included bundling as a NativeAOT single-file binary with no host dependencies.

The three [remaining open items](#still-open) — instance teardown policy, default capabilities, and how far to curate the tool surface — are implementation-shaping rather than architecture-shaping. They can be closed during the build.

One verification task, not a decision: **confirm the MCP SDK is NativeAOT-compatible in our usage** before committing to single-file AOT. Partially discharged on 2026-08-14 — the SDK declares `IsAotCompatible=true` on every non-`netstandard2.0` target, and the `JsonElement` passthrough at the proxy's core is AOT-friendly — but a declaration is not a publish-and-run. The fallback, self-contained trimmed at ~70 MB, is noise against ~806 MB of payload.

**This document is a specification, not a plan of work.** It states what to build and what is known to go wrong. The build happens in this repository, from here. Work items — settled in intent, not yet done — live in [`TODO.md`](TODO.md); open design questions and hazards stay here.

This document is the charter and is expected to be revised as the build proceeds. Carry the provenance convention with it — a bare "Default: X" claim cannot tell you when it was last true.

---

## License

BrowserAI is **source-available** under a **bespoke variant of the Functional Source License 1.1 (MIT Future License)**, modified so the Change Date is the **fifth** anniversary of each release rather than the canonical second. On that date the release additionally becomes available under the **MIT License**. In spirit: read it, run it, modify it, deploy it inside your organisation — but do not ship a commercial product or service that competes with it, for five years, after which it becomes MIT.

This is **not** the canonical FSL and must not be referred to by, or distributed under, the SPDX identifier `FSL-1.1-MIT`. Where an SPDX expression is required, use `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr`. The authoritative terms are in [`LICENSE`](LICENSE) and prevail over this summary.

Copyright 2026 Jori Huisman.

**Source files carry no license header.** [`LICENSE`](LICENSE) is the notice, and the Redistribution clause is satisfied by shipping it — nothing in the license asks for a per-file stamp, and comment-less formats such as JSON could not carry one anyway. Vendored third-party files are the exception and keep their upstream headers, which Apache-2.0 §4 requires.

### Third-party components

The license above covers **BrowserAI's own code and this document**. It does not cover the bundled payload, which keeps its own terms. Shipping that payload creates obligations that attach at first installer handoff, independent of BrowserAI's own license. Verified 2026-08-14 against the versions pinned in [§A](#a-ship-and-own-the-runtime):

| Component | Terms | Obligation on redistribution |
|---|---|---|
| `@playwright/mcp`, `playwright-core` 0.0.79 | Apache-2.0 | Keeping the vendored `node_modules` tree intact ships the package's `LICENSE` and satisfies §4. Upstream publishes no `NOTICE` file, so §4(d) has nothing to propagate. [Scope](#scope-proxy-not-implementation) forbids modification, so §4(b) is clean by construction. |
| `ModelContextProtocol` 2.2.0 | Apache-2.0 | Mid-transition from MIT; unrelicensed contributions remain MIT. Vendored fixture files keep their upstream headers. |
| Velopack 1.2.0 | MIT | Notice. |
| Node.js v24 | MIT, plus aggregate terms for OpenSSL, ICU, V8, zlib, c-ares | **Ship Node's full `LICENSE`.** "A single `node.exe`, nothing else" drops it. Not optional. |
| `chromium-headless-shell` 1237 | BSD-3-Clause + 40,178-line credits file | Ship `LICENSE.headless_shell` and the credits file unchanged. Binary is unbranded. |
| `ffmpeg` 1011 | LGPL-2.1 | `COPYING.LGPLv2.1` already ships in the directory. Identify the version and offer corresponding source. Spawned as an unmodified separate executable by `playwright-core`, so §6's relink requirement does not bite and it does not reach BrowserAI's own code. |
| `winldd` 1007 | **no license file shipped** | Source one from `microsoft/playwright` before redistributing. |
| **full `chromium` 1237** | **Google Chrome for Testing — Google-branded, no OSS license file anywhere in the tree** | Not an open-source question. `chrome.exe` reports CompanyName "Google LLC" and "Copyright 2026 Google LLC. All rights reserved."; its `ABOUT` points at Google's Chrome Terms of Service. Redistributing it inside a third-party installer needs a decision taken against those terms. **Unresolved.** |

Playwright is a trademark of Microsoft Corporation. Chrome and Chromium are trademarks of Google LLC. BrowserAI is not affiliated with, endorsed by, or sponsored by either. Apache-2.0 §6 grants no trademark rights, and the inherited `browser_*` tool names surface upstream branding directly in BrowserAI's own API — ship a short disclaimer in the installed artifact.

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

Mutexes are named `Global\<RepoName>-PlaywrightInteractive`. That locks a *repository folder name*, which is arbitrary: two clones of the same repo collide, and two different repos wanting the same profile do not. The thing that actually requires exclusivity is **the browser profile directory**. Chrome's own `SingletonLock` already gets this right for the persistent mode; nothing else does.

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
| `@playwright/mcp` + `playwright-core` | Vendored at an exact pinned version, never `@latest`. Zero native binaries — the tree is portable JS. | 0.0.79, **18.11 MB** |
| `chromium-headless-shell` | Bundled at the revision the pin requires. | rev 1237, **268.49 MB** |
| `ffmpeg` | Required for video capture; without it the `video` artifact type throws. | rev 1011, **3.35 MB** |
| `winldd` | Not currently required — its validation path is a no-op on Windows due to a `chrome-win` / `chrome-win64` mismatch upstream. Ship it as insurance against that being fixed. | rev 1007, **0.25 MB** |
| Full `chromium` | **Optional.** Only needed if headed modes stop using `channel: "chrome"`. | rev 1237, +426.88 MB |
| System Chrome | Used by the three headed modes via `channel: "chrome"`. Not bundled — it self-updates. | — |

**Total payload: 378.73 MB installed, 104.60 MB compressed** (measured, 7z LZMA2 `-mx=5`), before BrowserAI's own binaries. That is *less disk than the ~300 MB the current setup downloads on first use*, and it arrives at install time instead of mid-session.

Shipping the browser deletes an entire subsystem from the current design: the preflight, the install mutex, the staleness timeout, the detached installer, and the retry instructions written for an LLM to read. That machinery exists solely because the browser arrives on demand.

> ### `browserName: "chromium"` must be set explicitly or none of this runs
>
> `@playwright/mcp` 0.0.79 defaults to **`channel: "chrome"`** — system Chrome — and `headless: false` on Windows. Verified empirically: with an **empty** browsers directory, `initialize`, `tools/list` *and* `browser_navigate` all succeeded, because the bundled tree was never consulted.
>
> Omit `browserName` and the entire shipping-Chromium premise is silently dead code. This is the same failure shape as every bug in the table above.

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

### C. The `init` tool

The agent's first call declares what kind of session it wants, where data should be stored, and what should be persisted. BrowserAI then resolves directories, takes the appropriate lock, generates the child config, and spawns the runtime.

This replaces four static configurations with one dynamic one, and eliminates the relative-path hazard by making the directory an explicit argument rather than an implicit consequence of cwd.

**Critical constraint — read before designing `init`:** the MCP spec (2026-07-28, *Tools § Capabilities*) states the tool set "**MAY** change over time … but **MUST NOT** vary per-connection or as a side effect of other requests on the connection." SEP-2567 removed protocol-level sessions outright. Separately, `notifications/tools/list_changed` is unreliable in practice — Claude Code registers no handler for it (issues [#13646](https://github.com/anthropics/claude-code/issues/13646), [#4118](https://github.com/anthropics/claude-code/issues/4118)).

**Therefore `init` cannot shrink the tool list.** There is one static list; session-inappropriate calls must be **rejected at runtime by BrowserAI**. See [Trade-offs](#known-trade-offs) for what this costs.

### D. Locking and single-instance

Keyed on the **resolved absolute directory**, not on a repository name. Must handle: stale locks from crashed processes, alive-but-orphaned holders, and PID recycling. The existing launcher's mutex + sibling-lockfile + signature-check pattern solves all three and is worth porting rather than redesigning.

### E. Lifecycle and observability

Non-negotiable, because every bug this month was a visibility failure. The .NET MCP SDK already implements roughly half of this — `StandardErrorLines` wired before `Start()`, a rolling stderr tail, and a `StdioClientCompletionDetails { ProcessId, ExitCode, StandardErrorTail }` type that makes bug #2 in the table above structurally impossible.

- **Capture the child's stderr from before it starts.** `RedirectStandardError` + `ErrorDataReceived` + `BeginErrorReadLine()`. The anonymous pipe exists before `CreateProcess` and the kernel buffers, so nothing written earlier is lost (measured: 5 lines survived a 3 s delay *and* child exit). The real risk is the opposite — a full pipe blocks the child.
- **Record the child's real exit code and cache it as an `int` immediately.** .NET is *worse* than PowerShell here, not better: `Process.ExitCode` **throws** after `Dispose()`, and `Process.GetProcessById(pid).ExitCode` **always** throws. Microsoft's own SDK carries a `beforeDispose` callback commented "to read ExitCode before Dispose() invalidates it" — they hit this too.
- **Use `await WaitForExitAsync(ct)`**, never `WaitForExit(int)`. Only the former drains the async readers.
- **Distinguish error-shaped stderr from benign output.** Port the two regexes verbatim; a healthy start prints `Session: <path>` every time.
- **Kill the descendant tree with a Windows Job Object**, not `Process.Kill(entireProcessTree: true)`. The latter requires BrowserAI to be alive and running code — which is exactly the case that fails. A job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` survives `TerminateProcess` of BrowserAI itself (measured against a 3-deep tree, including nested inside an existing job). **Assign BrowserAI itself to the job before spawning anything**, so descendants inherit membership at `CreateProcess` time and the assign-after-spawn race disappears. The job handle must be non-inheritable and unnamed.
- **Reap orphans keyed on `(pid, creationFileTime)`** — a bare PID identifies nothing once PIDs recycle. Better still, hold an `OpenProcess` handle on the parent: Windows will not recycle a PID while a handle is open, and the handle is signalled on exit, so liveness becomes event-driven instead of a poll loop.

**stdout is the protocol channel and it is wrong by default.** Measured: `Console.Out` writes CP437, not UTF-8 (`é` → `0x82`); `Console.InputEncoding` also defaults to CP437; any `TextWriter` emits CRLF; and a hand-rolled `new StreamWriter(stream, Encoding.UTF8)` emits a BOM. Own the raw streams, never touch `Console.Out`, and let no code anywhere in the process call `Console.WriteLine` — including inside a `catch`. This should be a reviewed invariant owned by one wrapper type, not a convention.

### F. Artifact management

Playwright writes every artifact flat into one directory with a generated name, mixing machine churn with hand-named work. Nine fixed generator prefixes make classification exact rather than heuristic: `console`, `download`, `network`, `page`, `request`, `response`, `result`, `storage-state`, `video`.

Port the prefix-based sort. **Classification must be by generator prefix, never by date** — that is precisely what keeps a hand-named file out of the machine-generated folders, and no date rule can make that distinction. Nothing is ever auto-deleted.

### G. Updates

The reason the project exists. Requirements: updates published on **our** schedule after we have validated a new Playwright version; one release updating runtime, browser, config, opinions and BrowserAI itself; rollback to a known-good release; and silent background update with a clear "restart to apply" signal, since MCP servers are long-lived child processes.

**Mechanism: Velopack 1.2.0** (MIT, per-user install to `%LocalAppData%`, no elevation, no commercial tier).

It wins on delta granularity. Its scheme is per-file zstd `--patch-from`, and unchanged files collapse to **zero-byte markers** — so against a ~380 MB footprint dominated by Chromium, a BrowserAI-only release ships single-digit MB. It is also the only option with both a **stable executable path** (`current\`, a real directory, not a junction) *and* a stage-now/swap-later primitive that composes with a parent-owned process lifecycle.

Go **per-user, not per-machine**. The `--msi PerMachine` layout installs to `Program Files`, which makes the updater self-elevate — and a UAC prompt cannot be answered by a background MCP server. Per-user also matches how MCP registrations and browser profiles already work. Ship the MSI as a *second* artifact for IT deployment if a colleague ever needs it, never as the update path.

Three landmines, all of which fail silently:

1. **`SetAutoApplyOnStartup(false)` is mandatory.** The default is `true`: on finding a staged package, `VelopackApp.Run()` applies it, exits(0), and relaunches — with no inherited stdio. Claude Code sees its MCP server exit at handshake time.
2. **Register `%LocalAppData%\BrowserAI\current\BrowserAI.exe` directly**, never the execution stub. The stub is compiled `#![windows_subsystem = "windows"]` and returns immediately without waiting, so a stdio client sees the child die instantly with no pipes attached.
3. **`force_stop_package` kills every process under the install root** without asking. With four registrations that is three other live sessions destroyed mid-task. Gate the apply on "am I the last instance" using the same directory-keyed lock from §D, then spawn `Update.exe apply --silent --norestart --waitPid <ownPid>` and exit. The next session starts the new version from the identical path — in normal use there is no "restart to apply" prompt at all.

Rollback needs hand-rolling: Velopack prunes `packages\` down to the current full `.nupkg` and deltas are forward-only, so archive each full package yourself or every rollback is a fresh ~105 MB download. State must live **outside** `current\`, which is wholly replaced on update — resolve every path from `VelopackLocator.Current.RootAppDir`.

**MSIX is disqualified on evidence, not theory.** Two production AI tools hit exactly this in 2026: claude-code [#63397](https://github.com/anthropics/claude-code/issues/63397) (`0x80073D02` / `ERROR_SHARING_VIOLATION`, and the report names the cause — "Claude Code runs as a child process of Claude Desktop") and openai/codex [#25770](https://github.com/openai/codex/issues/25770). A package cannot re-register while any process in its family is running, and BrowserAI's entire design is to be a long-lived child. Hydraulic Conveyor emits MSIX on Windows and inherits the same failure.

---

## Implementation stack

Verified 2026-08-13. Versions carry the same provenance convention as `playwright/README.md`: re-verify on every bump.

| Concern | Choice | Notes |
|---|---|---|
| MCP protocol | `ModelContextProtocol` **2.2.0** | Official C# SDK, Apache-2.0, Microsoft co-maintained, 23.5M downloads. Full `2026-07-28`. The main package's hosting dependency is abstractions-only — it does **not** drag in ASP.NET. |
| Updates | **Velopack 1.2.0** + `vpk` 1.2.0 | MIT. See §G. |
| Node runtime | **v24.19.0 LTS**, `node.exe` only | v26 is Current, not LTS, and its `node.exe` is 10 MB larger. |
| Job objects | Hand-rolled `[LibraryImport]` | No credible NuGet wrapper exists — the candidates have <6K downloads and the newest was published in 2017. `dotnet/runtime` [#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in support and was closed as not planned. ~60 lines. `Microsoft.Windows.CsWin32` is the reasonable alternative once a seventh Win32 API is needed. |
| Parent PID | `NtQueryInformationProcess` | ~0.77 µs/call vs ~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. This is what `dotnet/runtime` itself uses. |
| Tests | xUnit **v3 3.2.2** | Matches the SDK's own suite, so `ClientServerTestBase` and `tests/Common/Utils/*` vendor in unmodified (~300 lines, MIT). |
| Snapshots | `Verify.XunitV3` **31.28.0** | Not `Verify.Xunit` — that ID targets xUnit v2. |
| Assertions | Built-in `Assert` | The SDK uses no assertion library. **Avoid FluentAssertions** — v8+ is commercial at $129.95/seat. `Shouldly` 4.3.0 (BSD-3) or `AwesomeAssertions` 9.5.0 (Apache-2.0) if a fluent style is wanted. |
| External smoke | `@modelcontextprotocol/inspector` **2.2.0** | Language-independent CI check. Exit code **5** means the tool reported `isError` — the signal `claude mcp` does not give you. |

### Two places where the SDK must be deviated from

**1. Write your own `IClientTransport`.** The SDK's `StdioClientTransport` prepends `cmd.exe /c` to every non-cmd command on Windows, unconditionally. That directly contradicts §"Windows process spawning" below: it adds a shell layer, an extra process between BrowserAI and `node` (complicating tree ownership and exit-code attribution), and cmd.exe quoting semantics. The interface is two members (`Name`, `ConnectAsync`) and the replacement is ~120 lines. Port `StdioClientTransportOptions`' stderr and shutdown handling rather than reinventing it.

**2. Proxy `tools/call` at the message-filter layer, not the typed layer.** The SDK's `ContentBlock` converter **silently drops unknown properties** (it has tests asserting this — correct forward-compatibility for a client, data loss for a proxy) and **throws on unknown content *types***, which fails the entire call at deserialization before any BrowserAI logic runs. `WithMessageFilters` operates on `JsonRpcMessage` where `JsonRpcResponse.Result` is a raw `JsonNode?`. Rewrite `tools/list` typed; let `tools/call` results pass through as raw JSON.

Both of these are places where the SDK's design goal (a forward-compatible *client*) and BrowserAI's (a lossless *proxy*) genuinely differ. Decide them now rather than discovering them later — and note that both failure modes are silent, which is the class this project exists to eliminate.

---

## Testing: the smoke test is the whole point

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

Four layers, run at different cadences:

| Layer | Drives | Cost | When |
|---|---|---|---|
| **Unit** | stderr classifier, artifact prefix sort, tool filter/rename, **session-type enforcement**, lock signature and PID-recycle logic, config validator | ms | every push |
| **Fake child** | Full proxy over an in-process `Pipe` pair — no `Process`, no Node. Passthrough fidelity, error shapes, image bytes, cancellation, child death, stderr back-pressure | ms | every push |
| **Real-child contract** | Real `node` + pinned `cli.js`, **no browser**. Golden `tools/list` snapshot, negotiated protocol version, argv contract, config-key validation | 2–5 s | main + nightly |
| **Smoke** | Real child **and real browser**. `browser_navigate`, `isError`, real stderr classification, process-tree lifecycle | 10–30 s | main + pre-release |

`McpClient.CreateAsync` accepts the `IClientTransport` *interface*, so the fake child is an in-process `McpServer` joined by two `Pipe`s — no processes, no ports, fully parallel-safe.

**The most important test in the suite** is mechanical and follows from §"Known trade-offs": read the real child's `tools/list`, then assert **every** tool name carries an explicit session-type classification. An unclassified tool fails the build. That turns "a new upstream tool leaks into interactive mode" from a security incident into a red CI run.

**Add a nightly drift job** that installs `@playwright/mcp@latest` (not the pin) and runs only the golden snapshots. It is *expected* to fail when upstream moves; that failure is the notification the current setup does not have. Upstream publishes daily alphas, so this is the highest-value piece of CI in the plan.

Lifecycle tests must wrap themselves in their own job object (`KILL_ON_CLOSE`, `using`-scoped) so a failed assertion cannot leave a stray `chrome.exe`, and must never match processes by image name — a test that kills `chrome.exe` by name will one day close the developer's browser.

---

## What this improves over the current setup

| | Today (`Workspace657/playwright/`) | BrowserAI |
|---|---|---|
| Update path | Copy-paste to 13 checkouts | One release, one channel |
| Playwright version | `@latest`, floats silently | Pinned, updated deliberately |
| Chromium | Downloaded on first use, ~300 MB, needs preflight + retry protocol | Shipped in the installer — 268 MB, arrives before first use |
| Node | Must exist on the host | Bundled (`node.exe`, 88.5 MB) |
| Tree teardown | `Get-CimInstance` walk + `Stop-Process`; nothing survives a hard kill of the launcher | Job object — the kernel reaps the tree even if BrowserAI is `TerminateProcess`d |
| Lock granularity | Repository folder name | Resolved profile directory |
| Output directories | Static, relative, cwd-dependent | Explicit argument to `init` |
| Profile | 13 separate ones by accident | One, deliberately — or per-directory by choice |
| Config validity | Unknown keys silently ignored | Validated against the shipped runtime |
| Colleague onboarding | Clone repo, replicate 6 files, install Node | Run installer |
| MCP registrations | 4 | 1 or 4 — see [open decisions](#open-design-decisions) |

---

## Known trade-offs

Recorded so they are inherited as decisions, not rediscovered as surprises.

### The `init` design weakens a security boundary

Today, four separate processes give **process-level isolation**. The `interactive` mode exists so a human can type credentials the agent must never capture, and its server process is launched without the `storage` capability — the 17 cookie/localStorage/`storageState` tools do not exist in that process. There is no code path to reach them.

Under one server with `init`, that becomes a **runtime check in BrowserAI's own code**. The tools exist in the shared surface; correctness depends on the session-state lookup being right, including under concurrency.

To be precise about the size of this: it is *not* a demotion to "the model must behave" — BrowserAI enforces it server-side, and a model that calls `browser_cookie_list` in an interactive session gets refused. It *is* a demotion from "the capability does not exist in this process" to "our code declines to use it." Weaker, and worth the eyes-open acknowledgement.

**Requirement:** session-type enforcement must be centralized in exactly one place, deny-by-default, and unit-tested against every tool in the surface. A new upstream tool must be *unreachable* until explicitly classified — never reachable by default.

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

Not yet settled. Each changes the shape of the build.

1. **One MCP registration, or four?** These are *independent* decisions: owning the runtime in C# does not require collapsing to a single server entry. Four entries — each launching BrowserAI in a different mode — preserves process-level isolation, per-mode hook matchers, and a blast radius of one mode, at the cost of the single-entry aesthetic and roughly 650 tokens. **Recommendation: start with four, add the unified `init` entry once enforcement is proven.**

2. **One shared profile, or one per directory?** One shared profile means a login anywhere is a login everywhere — convenient, and it means any repository with BrowserAI installed can reach every stored credential.

3. **Where do artifacts live?** Under the caller's project directory (visible, needs `.gitignore` entries) or in BrowserAI's own data directory (nothing touches the user's repos, less discoverable).

4. **Which capabilities ship by default?** Current: `vision`, `devtools`, `config` on all modes; `storage` on persistent only. `testing`, `network`, and `pdf` are off. Measured cost per server: base 24 tools, `vision` +6, `devtools` +11, `config` +1, `storage` +17.

5. **How aggressively should the tool surface be curated?** BrowserAI can rename, re-describe and drop tools. Dropping reduces what the model can do; renaming diverges from upstream documentation.

6. **Ship the headless shell only, or the full Chromium too?** Headless-only is 268 MB and keeps the three headed modes on system Chrome via `channel: "chrome"` — matching today's behaviour exactly. Adding the full build costs **+426.88 MB installed / +134 MB compressed** and buys one thing: headed modes that do not depend on the user having Google Chrome installed. **Recommendation: headless-only for v1.** It matches the current configs, and the dependency it would remove is one every target machine already satisfies.

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

Outstanding before a build starts: the five [open decisions](#open-design-decisions).

This document is the charter and is expected to be revised as the build proceeds. Carry the provenance convention with it — a bare "Default: X" claim cannot tell you when it was last true.

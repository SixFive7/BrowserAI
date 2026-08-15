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

The lesson worth carrying into BrowserAI: **every one of those defects was invisible.** The setup reported healthy while being broken. Observability is a feature requirement here, not a nicety. The sweep above and the measurement behind each row — 11.71 s → 0.37 s on the stderr-pipe fix, five days of a hard startup failure logging as a clean shutdown, 346 session directories — are recorded in [kb: the legacy setup](kb/history.md#the-legacy-setup-and-this-machine).

### Where things are written down

| File | Holds | Changes when |
|---|---|---|
| **`README.md`** (this file) | What we **decided**, and why — the architecture, the scope boundary, the trade-offs taken | We change our minds |
| **[`PLAN.md`](PLAN.md)** | What to **build**: the requirements §A–§G, the implementation stack, the test suite, the hazard index | It gets built, or the design behind it moves |
| **[`kb/`](kb/README.md)** | What we **measured** — about Chromium, Firefox, Playwright, Node and Windows, one article per topic, with provenance and a re-verification hook | Upstream ships, and a re-measurement says something different |
| **[`TODO.md`](TODO.md)** | Work settled in intent but not yet done | Something gets decided, or gets done |
| **[`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md)** | The procedure for adopting a new upstream version | The procedure proves insufficient |

**Three different half-lives, which is the whole reason for the split.** A decision stays true until we revisit it. A plan is *consumed* — every section of `PLAN.md` is spent the day the code it describes exists. A measurement stays true until upstream ships, which is a clock nobody here controls. Mixing them means the whole document reads as equally settled, and the parts with the shortest half-life are exactly the ones that quietly stop being true. When this file states a measured fact, it is a summary — the article under [`kb/`](kb/README.md) carries the number, the date, the versions it held under, and how to re-establish it.

---

## Why this project needs to exist

### 1. There is no update path, and that is the actual problem

A fix made today reaches one of thirteen checkouts. Nothing propagates. The setup was designed to be *copied*, which means every copy is a fork the moment it lands.

This is the core justification for BrowserAI. It is **not** token cost — see [Non-reasons](#non-reasons--do-not-relitigate-these).

### 2. The version chain floats, and it breaks silently

`launch.ps1` runs `npx -y @playwright/mcp@latest`. The dependency chain below that point is **exact** — no semver ranges anywhere:

```
@playwright/mcp 0.0.79
  └── playwright-core 1.63.0-alpha-2026-08-05   (exact pin, no ^ or ~)
        └── browsers.json → chromium-headless-shell rev 1237 (152.0.7977.8)
```

So the package is pinned to a browser revision, but *which package* is not pinned at all. An upstream publish silently invalidates the local browser cache and changes the CLI surface. Both failure modes fired this month, on the same day. The exactness of that chain, and upstream's daily-alpha cadence, are in [kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape).

### 3. Two failure classes exist that no configuration can fix

- **CLI flags fail loudly.** An unknown flag crashes at startup with a clear message — recoverable once you can see stderr.
- **JSON config keys fail silently.** `loadConfig` is a bare `JSON.parse` with no schema validation. A key that upstream renamed or removed is simply ignored. `--output-mode` turned out to have been a **no-op for its entire life** — a hardcoded literal in 0.0.78's bundle, never read from config ([kb: upstream config](kb/playwright/configuration.md)).

A setup that cannot distinguish "this setting is applied" from "this setting is silently discarded" cannot be trusted to be opinionated. BrowserAI must validate its own config against the runtime it ships.

### 4. Exclusivity is keyed on the wrong thing

Mutexes are named `Global\<RepoName>-PlaywrightInteractive`. That locks a *repository folder name*, which is arbitrary: two clones of the same repo collide, and two different repos wanting the same profile do not. The thing that actually requires exclusivity is **the browser profile directory**.

> **And nothing else enforces it in the mode that matters.** Measured 2026-08-14: the **full** chromium build writes a `lockfile` into the profile and a second instance is refused (`Browser is already in use for <dir>`). **`chrome-headless-shell` writes no `lockfile` at all** — two headless instances opened the same profile directory, both launched, both worked, and no error was raised anywhere. Two browsers writing one profile's cookie and storage databases is silent corruption, and headless is the default mode. An earlier draft of this document said *"Chrome's own `SingletonLock` already gets this right for the persistent mode"*; that is true only of the headed build. **[§D](PLAN.md#d-locking-and-single-instance)'s directory-keyed lock is therefore the only protection that exists, not defence in depth.** Both launches are recorded in [kb: image-path detection](kb/windows/detection.md#process-image-path--the-fully-documented-detection-path).

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

The implementation lives in `playwright-core/lib/coreBundle.js` — 3.4 MB, esbuild-bundled, containing a 78-entry tool array. Note the numbers, because a golden test written against the wrong one fails on day one: **78 is the internal registry; 69 is the maximum ever exposed over MCP** (9 are `skillOnly` and always stripped), and **24 is the default** with no `capabilities` set — all three in [kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape), which also records that the per-capability breakdown was never counted. Its value is not browser control; Playwright for .NET does browser control perfectly well. Its value is the **ref-based accessibility snapshot system**, the response formatting, and the error handling — the layer that turns a browser into something a language model can operate. That layer is large, subtle, actively developed upstream, and would drift permanently the day it is forked.

"We don't want to reimplement Playwright" is the easy half of this rule. The half that matters: **reimplementing the MCP tool layer is also reimplementation**, even though it never touches a browser API.

### The one sanctioned exception, if it is ever needed

`playwright-core` explicitly whitelists `"./lib/coreBundle"` in its `exports` map, so `require('playwright-core/lib/coreBundle')` is a supported import, not a blocked deep path. It exposes `browserTools` (a flat array of plain, inert objects), `filteredTools`, `createConnection`, and `BrowserBackend`. `defineTool` is literally the identity function — there is no class, no registry, no side effect ([kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)).

This means in-process tool manipulation is *available* if the proxy approach ever proves insufficient. It is **not** the plan, it carries no type definitions and no semver guarantee, and taking it requires pinning `@playwright/mcp` and `playwright-core` together and re-verifying the tool array on every bump. Documented here so it is a considered decision rather than a discovery.

---

## Versioning policy: everything floats, the build freezes it

Adopted from `SixFive7/Jeeves` — `V1.md` § *Versioning policy: never pin* and its *Modernity doctrine* — and applied here **without exception, including to the shipped payload**.

**Every dependency floats to the latest release at build time.** NuGet packages, `@playwright/mcp` and the `playwright-core` it carries, `node.exe`, both Chromium builds, `ffmpeg`, `winldd` — the build resolves each to the newest available version and then **freezes what it resolved into the artifact**. For .NET, the newest **GA** major, adopted at each annual GA, **including when that major is STS rather than LTS**. `LangVersion` is `latestMajor`, never `preview`.

**Every version number in this document is a floor and a provenance stamp, never a target.** The versions named in [Implementation stack](PLAN.md#implementation-stack) and [§A](PLAN.md#a-ship-and-own-the-runtime) record what was current when each claim was verified, so a reader can tell how stale the prose is. **The build does not read them.**

> **Why:** stale dependencies are a defect, not a safety measure. Riding the newest release keeps each upgrade small instead of saving them into one nobody ever takes, and [Testing](PLAN.md#testing-a-hard-requirement-and-the-release-gate) is what catches breakage. A version pin is not a substitute for a test suite — it is a way of not finding out.

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

1. **The resolved set is recorded, not remembered.** For NuGet this is **two steps, not one**: `dotnet restore --force-evaluate` to resolve the float, then a locked-mode restore to verify. They are mutually exclusive in a single invocation — with a lock file present and no `--force-evaluate`, NuGet **does not re-resolve**, and the float is silently dead ([NU1512](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1512); warned by default from the .NET 11 SDK). A one-step `--locked-mode` build is the `browserName: "chromium"` failure again. It also yields the cheapest possible drift detector: `git diff --exit-code -- "**/packages.lock.json"` after the resolve ([kb: interop](kb/windows/processes.md#interop-and-the-toolchain)). Then the resolved `package-lock.json` for the vendored npm tree, and browser revisions read from the resolved package's own `browsers.json`, never a hand-typed URL. **An artifact that cannot state exactly what went into it is not releasable** — that is also what makes a rollback meaningful and a regression bisectable.
2. **The shipped artifact never floats.** The client resolves nothing at runtime: no `npx`, no `@latest`, no network at spawn. What was tested is what runs. This property is the non-negotiable one, and it is why [§A](PLAN.md#a-ship-and-own-the-runtime) vendors the tree at all.
3. **GA is a hard floor.** No preview or RC builds a released artifact. Upstream Playwright publishes **daily alphas**, but `@playwright/mcp@latest` is the released dist-tag — the `playwright-core` alpha beneath it arrives as that package's own exact dependency, not as a choice we make ([kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)).
4. **Green is the only gate, and it gates the *release*, not the *update*.** The response to a breaking upstream change is to make the newest version work. Holding the previous version is not a fix, and "pin it back for now" is the failure this policy exists to prevent.

### Never assert a version from memory

**If it was not looked up this session, it is unverified — say so.** Model training knowledge lags this toolchain by design, and a confident stale version is worse than an admitted gap. Same discipline as the `Verified <date> @ <version>` stamps in [Reference material](#reference-material), applied to the act of writing a version rather than to the value written.

> **In-house evidence, and it is three weeks old.** `SixFive7/OutlookAI` pins `ModelContextProtocol` **1.4.1**, with a csproj comment reading *"1.4.1 = latest stable on nuget.org as of 2026-07-23 (2.0.0 is still preview)."* That was true when written. Re-checked against nuget.org's flat-container index on **2026-08-14**: 2.0.0, 2.1.0 and 2.2.0 have all shipped stable, so the comment's central claim is now false and nothing in that build says so.
>
> The comment was **correctly stamped with its date**, which is the only reason the staleness is detectable at all — an unstamped "latest stable" claim would still read as current. Stamp the date; never trust one that lacks it. ([kb: package provenance](kb/packaging/dependencies.md#package-provenance-as-looked-up))

---

## What this improves over the current setup

| | Today (`Workspace657/playwright/`) | BrowserAI |
|---|---|---|
| Update path | Copy-paste to 13 checkouts | One release, one channel |
| Playwright version | `@latest`, re-resolved at every spawn, on the user's machine, untested | Resolved to latest at build time, gated by the suite, frozen into the artifact |
| Chromium | Downloaded on first use by `npx`, on the user's machine, untested, with no integrity check | Downloaded on first use **by us**, at a revision the build resolved and the suite gated, once per machine rather than once per spawn |
| Node | Must exist on the host | Bundled (`node.exe`, 88.5 MB) |
| .NET / Chrome | n/a / required for headed modes | Neither — NativeAOT single-file, and a Chromium we provision rather than the user's |
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

Today, four separate processes give **process-level isolation**. The `interactive` mode exists so a human can type credentials the agent must never capture, and its server process is launched without the `storage` capability — the 17 cookie/localStorage/`storageState` tools do not exist in that process ([kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)). There is no code path to reach them.

Under one server with `init`, that becomes a **runtime check in BrowserAI's own code**. The tools exist in the shared surface; correctness depends on the session-state lookup being right, including under concurrency.

To be precise about the size of this: it is *not* a demotion to "the model must behave" — BrowserAI enforces it server-side, and a model that calls `browser_cookie_list` in an interactive session gets refused. It *is* a demotion from "the capability does not exist in this process" to "our code declines to use it." Weaker, and worth the eyes-open acknowledgement.

The handle design ([§C](PLAN.md#c-the-init-tool-and-instance-handles)) narrows this considerably — a server-minted handle cannot be forged for a session type the agent never created — but it does not change the *kind* of guarantee. It remains our code declining, not a capability that does not exist.

**Requirement:** session-type enforcement must be centralized in exactly one place, deny-by-default, and unit-tested against every tool in the surface. A new upstream tool must be *unreachable* until explicitly classified — never reachable by default. With N concurrent instances in one process, the handle→type lookup is now shared mutable state on the hot path of every call: it must be correct under concurrency, and that is a test, not an assumption.

### Storage tools capture bearer tokens

`browser_storage_state` and the cookie tools return `httpOnly` cookies, which JavaScript cannot read. These are session bearer tokens. Any mode permitted to call them must be treated as credential-bearing. `browser_storage_state` additionally never captures IndexedDB — see [kb: tools that reach credentials](kb/playwright/tools-and-artifacts.md#tools-that-reach-credentials).

### `browser_get_config` does not redact

Its handler is `JSON.stringify(context.config, null, 2)` with no filtering, so it would emit `config.secrets` in plaintext if that key were ever set. It is not set today. BrowserAI should either redact before forwarding or refuse to expose the tool ([kb: tools that reach credentials](kb/playwright/tools-and-artifacts.md#tools-that-reach-credentials)).

### The child's environment overrides the config file BrowserAI generates

The merge order is **config file → environment → CLI**, and `@playwright/mcp` reads **40** `PLAYWRIGHT_MCP_*` variables covering essentially every option: `BROWSER`, `HEADLESS`, `USER_DATA_DIR`, `EXECUTABLE_PATH`, `OUTPUT_DIR`, `ISOLATED`, `CONFIG`, `SECRETS_FILE`, `STORAGE_STATE`, `CAPS`, and 30 more — **42 in total**, [two of them read outside that mapping](kb/playwright/configuration.md#environment-merge-order-and-startup-output).

So a stray variable in the user's environment silently overrides BrowserAI's opinions — and **`PLAYWRIGHT_MCP_CAPS` triggers the same replace-not-merge wipe documented below for `--caps`**, meaning there is an environment route to a bug the "never pass `--caps`" rule does not close.

**Requirement: build the child environment allowlist-style, never inherited-and-patched.** `ProcessStartInfo.Environment` is pre-populated with the inherited block and assignment *merges*, so `Environment.Clear()` must come first. Also strip `INIT_CWD`, `NODE_OPTIONS`, `NODE_PATH`, `DEBUG` and `DEBUG_FILE`, and set `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` and `PLAYWRIGHT_SKIP_BROWSER_GC=1` — the latter because Playwright's stale-browser GC deletes any registry directory not referenced by a `.links` entry, and the blast radius is "deletes BrowserAI's shipped Chromium."

Do **not** set `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS`: it writes a line to stderr, which trips the error-shaped-stderr detection in §E.

### `capabilities` replaces, it does not merge

`mergeConfig` spreads defined overrides, so passing `--caps` on the command line **silently wipes** the config file's capability list. The current launcher passes only `--config` and `--output-dir`, which is why this has not bitten. BrowserAI generates the child config and must not introduce a `--caps` argument alongside it — nor allow `PLAYWRIGHT_MCP_CAPS` to survive into the child environment ([kb: environment and merge order](kb/playwright/configuration.md#environment-merge-order-and-startup-output)).

### Windows process spawning

Node's `spawn` cannot execute `.cmd` shims without `shell: true` — a live Claude Code bug ([#58510](https://github.com/anthropics/claude-code/issues/58510)) for plugin-shipped servers using bare `npx`. Since BrowserAI ships its own Node, it must invoke the resolved `cli.js` with the bundled Node executable directly and never depend on PATH resolution or shell shims:

```
<install>\node\node.exe  <install>\mcp\node_modules\@playwright\mcp\cli.js  --config <abs path>
```

**This is also why the SDK's own `StdioClientTransport` cannot be used** — it prepends `cmd.exe /c` unconditionally on Windows. See [Implementation stack](PLAN.md#implementation-stack).

Set `WorkingDirectory` explicitly on every spawn. Left unset, .NET passes `null` to `CreateProcess` and the child inherits BrowserAI's cwd — whatever Claude Code happened to have ([kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup)). That is reason 5 above, verbatim.

### Being in the data path is a new failure domain

The current launcher is a **supervisor, not a proxy** — it inherits stdio and never touches a JSON-RPC message. BrowserAI is in the data path by design. A crash in it takes down browser automation entirely, where today a crash in one mode leaves the other three working. Error propagation, cancellation, progress notifications, and binary/image passthrough all become BrowserAI's responsibility.

Prior measurement of an equivalent Node prototype: images passed through byte-identical (509,620 base64 bytes), error shapes preserved, ~50 ms added latency on a 500 KB payload, ~300 ms one-off child spawn. The overhead is acceptable; the ownership is the real cost. It was a *Node* prototype, so it predicts BrowserAI's overhead rather than measuring it — [kb: timings](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead) says so explicitly.

Two pieces of ownership the SDK does **not** hand you:

- **Progress notification relay.** The child's `notifications/progress` arrives keyed to the token BrowserAI minted; re-emitting it upward with the caller's token needs your own token map. Nothing bridges this automatically.
- **Cancellation relay.** A `CancellationToken` cancels the outbound child request, but it is unverified whether an upstream `notifications/cancelled` produces a downstream one rather than just a local abort. Needs a test — it is precisely the kind of thing that fails invisibly.

### Why C#, and what it costs

Recorded because the research raised it from two directions and the answer is not obvious.

**C# is not required for the update story.** Velopack is language-agnostic — the same 1.2.0 release ships `lib-nodejs`, `lib-rust` and `lib-python`, and the Rust `Update.exe` doing the real work is identical for all of them. In Node, "bundle a Node runtime" would also stop being a payload shipped *alongside* the app and become the app's own runtime.

**C# is chosen for §D and §E.** Node's `child_process` has **no job object support at all** — every Node process supervisor on Windows falls back to `taskkill /T /F` or a native addon, and none survive a hard kill of the supervisor ([kb: stdio and exit codes](kb/windows/processes.md#stdio-exit-codes-and-process-startup)). Named mutexes are the same story: `System.Threading.Mutex` is a first-class kernel object with automatic abandonment on crash, where Node needs `proper-lockfile` and its stale-detection heuristics. Those two primitives are exactly the locking and lifecycle requirements above, and they are the things that actually failed in the current setup.

**The price is two failure modes .NET makes *worse*, both silent, both already named above**: `Process.ExitCode` throwing after `Dispose()` (§E), and stdio encoding defaulting to CP437 with CRLF and BOM hazards (§E). Neither is hard to handle; both must be **invariants owned by a single wrapper type**, not conventions maintained by discipline. If those two are handled properly, the choice pays for itself.

---

## Open design decisions

### Settled 2026-08-13

| Decision | Outcome |
|---|---|
| **MCP registrations** | **One.** `init` returns a short server-minted handle; every other tool takes it for instance routing. See [§C](PLAN.md#c-the-init-tool-and-instance-handles). |
| **Profile and artifact locations** | **Arguments to `init`**, not global policy. The caller states where its data goes, per session — so "shared or per-directory" is the caller's choice, not BrowserAI's. This is what removes the relative-path hazard. |
| **Path validation** | **Any path is accepted.** Correct use is the calling agent's responsibility. See below. |
| **Child process model** | **One node child per handle.** Stays firmly on the proxy side of the scope boundary; costs ~300 ms and one process per instance. |
| **Browser and packaging** | **Full Chromium, NativeAOT single-file.** No host dependency on Node, .NET or Chrome. Installer ~117 MB; browsers provisioned on first run (203.8 MB down, 433 MiB on disk), once per machine rather than once per update. See [§A](PLAN.md#a-ship-and-own-the-runtime). |

**On accepting any path.** `init` does not constrain `userDataDir` or artifact paths to a sanctioned root. The consequence, recorded so it is inherited rather than rediscovered: an agent may point an instance at the user's **real Chrome profile** and read live browser state, or write artifacts anywhere the process can reach.

This is consistent with the trust model already in force — the current `persistent` mode grants any calling agent every login stored in its profile, and the workspace runs in `bypassPermissions`. BrowserAI is not the boundary; the agent's own instructions are.

Two things follow, and they are design obligations rather than caveats:

- **The `init` tool description is a security surface.** It is the only place the calling agent is told what a path argument means. Write it as guidance an agent will actually follow — name the sensible default location, and say plainly what pointing at an existing browser profile does.
- **Log the resolved absolute paths at instance creation.** If BrowserAI does not enforce a boundary, it must at least make crossings visible after the fact. This is the same principle as §E: the failure that cannot be seen is the one that costs five days.

**On one child per handle.** Research verified a single node process *can* serve several configurations — two servers with correctly divergent surfaces (42 vs 59 tools), no module-global browser state, browsers created lazily on first tool call ([kb: tool surface](kb/playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)). That path is rejected not on capability but on scope: it is reachable only through the programmatic `createConnection` API, which means writing a JS shim and moving toward the boundary [Scope](#scope-proxy-not-implementation) forbids. Spawning `cli.js` per handle keeps the proxy a proxy.

### Settled 2026-08-14

| Decision | Outcome |
|---|---|
| **License** | **Source-available**, under a bespoke five-year variant of the Functional Source License 1.1 (MIT Future License). Fixed before any code exists, and it constrains dependency selection from here. See [License](#license). |
| **Repository visibility** | **Private for now.** Source-available is the licensing posture, not a commitment to publish. Opening the repository is a separate decision and has not been made. |
| **Third-party payload** | Keeps its own terms. Bundling creates redistribution obligations that bite at first installer handoff *regardless* of which license BrowserAI itself carries — enumerated under [Third-party components](#third-party-components). |
| **Update tracks** | **One.** No beta channel. A second track doubles the release matrix and makes the version string load-bearing — UCC derives its runtime track from a `-beta` suffix, so a formatting change breaks track detection silently. A single track still requires the channel to be set explicitly, for the reason in [§G](PLAN.md#g-updates) landmine 1. |
| **Dependency versioning** | **Everything floats at build time; the build freezes it.** Nothing is pinned by hand, the payload included. The build resolves latest, the suite gates it, the release records exactly what shipped, and the client resolves nothing at runtime. Adopted from `SixFive7/Jeeves` and applied without exception. See [Versioning policy](#versioning-policy-everything-floats-the-build-freezes-it). |
| **Release trigger** | **Manual, by the maintainer, through the agent.** No release pipeline, no scheduled publish, no auto-merge on green. Green is necessary and not sufficient — a human decides when a green build becomes a release. See [The release gate](PLAN.md#the-release-gate). |
| **Test framework** | **TUnit**, matching `SixFive7/Jeeves`. Source-generated, reflection-free, MTP-native, MIT. See [Implementation stack](PLAN.md#implementation-stack). |
| **SDK test fixtures** | **Not vendored. We write our own harness.** The MCP SDK's `ClientServerTestBase` + `tests/Common/Utils/*` (1,082 lines, Apache-2.0, unpublished to NuGet) wire *one* pipe pair; a proxy needs two hops. Copying them would mean a permanent three-way merge against an upstream that edits `tests/` weekly, and would lock the framework to xUnit. See [We write our own harness](PLAN.md#we-write-our-own-harness). |
| **Instance teardown** | **Explicit close tool + client-liveness watcher** (stdin EOF, plus an `OpenProcess` handle on the client PID — never ping-based; `ping` is removed at 2026-07-28). **Expiry is reclaim, not destruction:** a torn-down handle stays resumable against its recorded config and directory, because the durable thing is the profile, not the process. Measured 2026-08-14: resume costs **515 ms** and loses only `sessionStorage` ([kb: timings](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)). Timer values remain open — see [Still open](#still-open). |
| **`browser_run_code_unsafe`** | **Hidden in `interactive` sessions.** Demonstrated 2026-08-14: against the default 24-tool surface with zero `browser_cookie_*` exposed, `async (page) => page.context().cookies()` returned an `httpOnly` bearer token ([kb: tools that reach credentials](kb/playwright/tools-and-artifacts.md#tools-that-reach-credentials)). It is in `core`, so no capability setting disables it, and it was the only hole — `browser_evaluate` → `document.cookie` returns `""`, and `browser_network_request` strips `Cookie` and `Set-Cookie`. Hiding it in the one mode that exists to keep human credentials from the agent makes [§trade-offs](#the-init-design-weakens-a-security-boundary)' claim true for the first time; it stays available elsewhere as an escape hatch. |
| **Artifact placement** | **Routed on the way in, not sorted on the way out.** The child's cwd is the instance output root, `filename` arguments are normalised into typed subfolders, and every result carries the resolved absolute path. The default root is outside any repository. Per-instance paths stay unconstrained; per-call filenames do not. See [§F](PLAN.md#f-artifact-management) and [the `init` contract](PLAN.md#the-init-contract). |
| **Tool naming** | **Never renamed.** Names are upstream's byte-for-byte; the names BrowserAI authors carry the `browserai_` prefix — see [Settled 2026-08-15](#settled-2026-08-15). (This row originally read "the only name BrowserAI authors is `init`", which the design outgrew.) Descriptions are append-only. A `deny` hook keyed on `browser_take_screenshot` exists in ten repositories and a rename would disable it silently — but the deciding argument is maintenance: upstream renamed one of its own tools inside four months, and a rename map is a second surface to re-review on every bump. |
| **Upstream review** | **Gated by a marker that fails closed.** A version bump cannot reach a release until someone has reviewed what upstream changed and recorded it. See [The upstream-review marker](PLAN.md#the-upstream-review-marker). |

**Why not permissive.** Apache-2.0 was the runner-up and is the smoothest technical fit: it is what `@playwright/mcp` and the C# SDK already carry, and its §4(b) change-statement requirement is real protection for something distributed as a binary installer nobody reads the source of. It was rejected only because it gives the commercial market away outright. AGPL-3.0 was considered and rejected on the merits — BrowserAI is stdio-only, one machine, one user, **no processes and no ports**, so §13's network-interaction clause could never fire and the license would be inert boilerplate.

### Settled 2026-08-15

| Decision | Outcome |
|---|---|
| **Process containment** | **One job object per instance, `KILL_ON_JOB_CLOSE` and nothing else, assigned at creation via `PROC_THREAD_ATTRIBUTE_JOB_LIST`.** Verified end to end against real Chromium and Firefox trees: 16 runs, 106 processes, **0 escapees, 0 survivors**. The guarantee is conditional on our implementation, not on the browsers — two of the failure modes were proven fatal by measurement. See [the job object contract](PLAN.md#zero-process-leakage-the-job-object-contract). |
| **Never by image name** | **Structural, not procedural.** BrowserAI can only terminate a process belonging to a job it created, or one identified against a path it owns. `GetProcessesByName`, `taskkill /IM` and name-filtered WMI are forbidden and analyzer-enforced. See [§D](PLAN.md#never-by-image-name). |
| **Chromium binary** | **Full Chromium in every mode**, headless included; `chrome-headless-shell` is not shipped. It makes the payload ~120 MB *smaller*, gives Chromium's own `lockfile` and `Chrome_MessageWindow` in every mode, and removes a per-mode branch. The shell's only advantage — it cannot be resurrected after a reboot — is worth less than being findable when it leaks, and the sweep it would avoid must exist anyway for the three headed modes. |
| **Chromium sandbox** | **Passed as the `--sandbox` CLI flag, never the config key**, which is silently discarded. Asserted by a test on the child's resolved browser command line. |
| **Firefox restart registration** | **Disabled on every launch** via `firefoxUserPrefs: { "toolkit.winRegisterApplicationRestart": false }`. The pref is observed at runtime and calls `UnregisterApplicationRestart`. The only place browser resurrection can be prevented outright rather than cleaned up after. |
| **Windows `RestartApps`** | **Never touched.** The maintainer's machine has it enabled, which is the direct cause of the resurrection incident, but it is a personal, global, per-user setting. BrowserAI reads nothing and writes nothing there. |
| **`browser_annotate`** | **`interactive` sessions only.** It opens a dashboard window and blocks until a human finishes drawing; `interactive` is the one mode with a human at the keyboard by design. In `headless` it must be hidden regardless — its window appears even there, breaking the only promise that mode makes. See [kb: tools that reach credentials](kb/playwright/tools-and-artifacts.md#tools-that-reach-credentials). |
| **`--isolated`** | **Never set, in any mode.** It puts the profile in a temp directory deleted on close. Three of the four legacy modes set it; BrowserAI gives every mode a real directory and deletes nothing automatically. |
| **`--output-max-size`** | **Never set, and `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE` stripped from the child's environment.** It is a recursive oldest-first deleter pointed at directories agents choose. Retention is the calling agent's decision, supported by an explicit cleanup tool, not by an eviction threshold. |
| **Console level** | **Exposed on `init`.** The upstream default of `info` silently drops `debug` messages; which trade-off is right is per-session, not global. |
| **Authored tool names** | **`browserai_` prefix on every tool BrowserAI authors** — `browserai_init`, `browserai_resume`, `browserai_list`, `browserai_destroy`, `browserai_set_purpose`, `browserai_reinstall_browser`. Not `browser_*`: **MCP spec SEP-2567 names `destroy_*` and `list_*` as the documented companions to a creation tool**, so upstream shipping `browser_list` is the expected pattern rather than a hypothetical — and since upstream names are never renamed, a collision would be unresolvable. Bare names are worse still: MCP tool names share a flat namespace with every other server's, and a bare `destroy` is a name a model could reach for meaning something else entirely. |
| **Browser reinstall** | **A tool that refuses rather than coordinates.** Takes a machine-wide mutex, then **refuses if any session anywhere has a live browser**, naming what is live; only when nothing is running does it delete and re-provision. Reuses the sweep's own detection to answer "is anything live". Downloading alongside and swapping does not work — Windows will not rename a directory holding open executables, and live browsers hold `chrome.exe` ([kb: object names and window scoping](kb/windows/detection.md#windows-object-names-and-window-scoping)). A force flag is not offered, because force here means terminating browsers other sessions are using. If refusal proves too restrictive, the fallback is deferral: mark the install bad and let the next process that finds nothing running do the work. |
| **Restart-registration lever** | **None shipped. A test instead.** Measured 2026-08-15: Playwright's command line overshoots Windows' 1023-character `RegisterApplicationRestart` limit by **531–807 characters** in every shippable configuration, so registration already fails and `GetApplicationRestartSettings` on a live browser returns `ERROR_NOT_FOUND`. `--browser-test` does suppress it and is **not** web-detectable (0 differences across 486 fingerprint fields, 65 launches) — but it suppresses something that is not happening, and drags in unrelated behaviour changes. **A test asserting the browser is unregistered is better insurance**: it fails loudly the day the margin closes, instead of silently changing browser behaviour forever. Prefer a mechanism over a habit. See [kb: resurrection](kb/chromium/resurrection.md). |
| **Validate every path before launch** | **Required, and it prevents a hang rather than untidiness.** An unusable `--user-data-dir` makes Chrome fall back to a default profile — invisibly to the MCP client, with 8 healthy processes and both `initialize` and `browser_navigate` returning OK — while its message window is titled with the **fallback** path, so our own stray detector goes blind to exactly the broken instances. In other configurations the same condition raises a native `#32770` dialog that **blocks startup entirely** until dismissed (measured: 1 process at 6 s; 10 processes after `WM_CLOSE`), which on a background server is an invisible hang that `--noerrdialogs` does not suppress. Third native-dialog trap after Firefox's profile-lock modal. |
| **Our own Chrome for Testing, never `channel: "chrome"`** | With `channel: "chrome"` the fallback profile is `%LOCALAPPDATA%\Google\Chrome\User Data` — **the user's own browser**. A stray detector extended to cover fallbacks would identify a personal Chrome as ours, and Chrome's `ProcessSingleton` would forward the launch into the user's running browser. Using the CfT build BrowserAI provisions makes both structurally impossible. **"Our own" means the one BrowserAI manages, not one shipped inside the installer** — the redistribution position is unresolved and provisioning stays first-run download. |
| **Session modes** | **Three — `headless`, `interactive`, `persistent` — with `tracing` a boolean on any of them.** `tracing` was never a mode; it is `interactive` plus a flag. Promoting it removes a mode *and* adds capability. Headless-with-storage is deliberately not offered: it is the one combination granting full credential access with no visible signal, and that should be its own decision rather than a side effect. Mode is bound at `init`, recorded in `lock.json`, and read back by `resume`. Discoverability is a hard requirement across all four model-facing channels, generated from one table and pinned by tests. See [Three modes](PLAN.md#three-modes-and-tracing-as-a-modifier). |

### Still open

1. **Firefox's `parent.lock` preflight and its own stray detection.** Designed, not yet a charter requirement. Playwright never checks `parent.lock`, so a collision raises a native modal that blocks up to 3 minutes. Our lock is taken before launch, so ordering covers it — but coverage-by-ordering needs a test. Firefox also has no `Chrome_MessageWindow` equivalent, so its stray detection is a different path: `parent.lock` sharing violation → Restart Manager `RmGetList` ([kb: image-path detection](kb/windows/detection.md#process-image-path--the-fully-documented-detection-path), [kb: profile fallback](kb/chromium/profiles.md)).

2. **Whether the vertical slice changes anything** — see below. *(The logon-task question that stood here is closed: verified 2026-08-15, a Velopack install hook runs as the user, non-elevated, and `schtasks /Create /XML` with `LogonType=InteractiveToken` succeeded, survived update and rollback, and was removed by the uninstall hook. [kb: Velopack hooks and vpk output](kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output).)*

3. **Nothing here is built.** Several decisions — the three lock scopes under real concurrency, `PROC_THREAD_ATTRIBUTE_JOB_LIST` in a published AOT binary, the session-index file layout — are settled on paper and unexercised. Expect at least one to move. (The SDK and NativeAOT halves of this closed on 2026-08-15; see [kb: the 2026-08-15 spike](kb/mcp/sdk.md#measured-by-spike-2026-08-15).)

**Recently closed, listed so they are not reopened by habit:** what ends an instance (one browser-idle timer, stdin EOF as backstop, explicit `browserai_destroy`; reclaim is forever); which capabilities ship (unchanged — `vision`, `devtools`, `config` everywhere, `storage` on `persistent`); and how far to curate the surface (upstream names never renamed, descriptions append-only, `browser_annotate` classified to `interactive`).

---

## Non-reasons — do not relitigate these

**Token cost is not why this project exists.** Measured 2026-08-13 with `tiktoken` `cl100k_base` against live `tools/list` payloads from `@playwright/mcp` 0.0.79:

| | Eager clients (Claude Desktop, Cursor, older Claude Code) | Claude Code with deferred tool loading |
|---|---:|---:|
| Four servers as registered today | ~23,000 tok | **~985 tok** |
| One perfectly-curated proxy | ~11,600 tok | **~330 tok** |

Claude Code defers MCP tool schemas — they arrive as bare names and load on demand. **The entire achievable saving in that client is ~650 tokens**, around 0.3% of a 200k window. Dropping the `devtools` capability from four JSON files saves a comparable amount for no engineering effort at all.

The number only becomes significant in clients without deferred loading, where a consolidated surface saves ~65%. Worth knowing it exists. Not worth building for, and **not a justification to cite in design arguments.** Method and provenance: [kb: token cost](kb/packaging/dependencies.md#token-cost-of-the-tool-surface).

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

The [remaining open items](#still-open) — and the fact that nothing is built — are implementation-shaping rather than architecture-shaping, with the exception of the last. Instance teardown policy, default capabilities and tool-surface curation were the three open items this paragraph used to name; all three closed on 2026-08-15 and are listed under [Recently closed](#still-open).

One verification task, not a decision: **confirm the MCP SDK is NativeAOT-compatible in our usage** before committing to single-file AOT. Partially discharged on 2026-08-14 — the SDK declares `IsAotCompatible=true` on every non-`netstandard2.0` target, and the `JsonElement` passthrough at the proxy's core is AOT-friendly — but a declaration is not a publish-and-run. The fallback, self-contained trimmed at ~70 MB, is noise against the browser download.

**This document is the charter, not the specification and not a plan of work.** It states what was decided and why. What to build, and what is known to go wrong while building it, is [`PLAN.md`](PLAN.md) — a document that is *consumed* as the code appears, where this one is not. What we measured is [`kb/`](kb/README.md). The build happens in this repository, from here. Work items — settled in intent, not yet done — live in [`TODO.md`](TODO.md); open design questions stay here, and the [hazard index](PLAN.md#hazard-index) went with the plan it gates.

This document is the charter and is expected to be revised as the build proceeds. Carry the provenance convention with it — a bare "Default: X" claim cannot tell you when it was last true.

---

## License

BrowserAI is **source-available** under a **bespoke variant of the Functional Source License 1.1 (MIT Future License)**, modified so the Change Date is the **fifth** anniversary of each release rather than the canonical second. On that date the release additionally becomes available under the **MIT License**. In spirit: read it, run it, modify it, deploy it inside your organisation — but do not ship a commercial product or service that competes with it, for five years, after which it becomes MIT.

This is **not** the canonical FSL and must not be referred to by, or distributed under, the SPDX identifier `FSL-1.1-MIT`. Where an SPDX expression is required, use `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr`. The authoritative terms are in [`LICENSE`](LICENSE) and prevail over this summary.

Copyright 2026 Jori Huisman.

**Source files carry no license header.** [`LICENSE`](LICENSE) is the notice, and the Redistribution clause is satisfied by shipping it — nothing in the license asks for a per-file stamp, and comment-less formats such as JSON could not carry one anyway. Vendored third-party files are the exception and keep their upstream headers, which Apache-2.0 §4 requires.

### Third-party components

The license above covers **BrowserAI's own code and this document**. It does not cover the bundled payload, which keeps its own terms. Shipping that payload creates obligations that attach at first installer handoff, independent of BrowserAI's own license. Verified 2026-08-14 against the versions pinned in [§A](PLAN.md#a-ship-and-own-the-runtime); what is actually present in each shipped tree is recorded in [kb: payload licensing](kb/packaging/dependencies.md#third-party-payload-as-shipped):

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

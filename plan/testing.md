<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Testing: a hard requirement, and the release gate

> **This section is a requirement, not an aspiration, and it is not severable from the [versioning policy](../README.md#versioning-policy-everything-floats-the-build-freezes-it).**
>
> [Versioning policy](../README.md#versioning-policy-everything-floats-the-build-freezes-it) puts every dependency on latest at build time. That makes the suite **the only thing standing between an upstream change and a shipped regression** — floating without a suite that can catch a breaking change is strictly worse than pinning. The two decisions are one decision: neither is valid alone, and weakening the suite silently converts the versioning policy into a liability.
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

So: any smoke test that stops at `tools/list` reproduces the exact five-day blindness described at the top of this document. The minimum viable assertion is a real navigation — measured at **0.43 s** with no network and no local server ([kb: timings](../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)):

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
| **Unit** | stderr classifier, artifact prefix sort, tool filter and re-describe with **names passed through unchanged**, **session-type enforcement**, lock signature and PID-recycle logic, config validator | ms | every build |
| **Fake child** | Full proxy over an in-process `Pipe` pair — no `Process`, no Node. Passthrough fidelity, error shapes, image bytes, cancellation, child death | ms | every build |
| **Real-child contract** | Real `node` + the **resolved** `cli.js`, **no browser**. Golden `tools/list` snapshot, negotiated protocol version, argv contract, config-key validation | 2–5 s | **every build** |
| **Smoke** | Real child **and real browser**. `browser_navigate`, `isError`, real stderr classification, process-tree lifecycle | 10–30 s | every build · **mandatory before release** |
| **Update** | Real feed URL resolves and returns a manifest; `vpk pack` emits a delta; N→N+1 applies and the installed version moves | 1–3 min | **mandatory before release** |

**The real-child contract layer changes character under a floating build.** When the payload was hand-pinned it was a slow-moving regression check that could reasonably run nightly. Now it is *the* mechanism that detects an upstream Playwright change, and it runs on **every build** — 2–5 seconds is nothing against the alternative of finding out from a user. Its golden snapshots are the tripwire; a diff there is not a test failure to suppress but the notification this whole design exists to produce.

`McpClient.CreateAsync` accepts the `IClientTransport` *interface*, so the fake child is an in-process `McpServer` joined by two `Pipe`s — no processes, no ports, fully parallel-safe.

> ⚠️ **Corrected 2026-08-16 at [build-order step 9](build-order.md#9-lossless-passthrough) (previously the Fake-child row also claimed "stderr back-pressure").** A layer whose whole premise is that it starts no process has no stderr pipe to fill, so that item could only ever have been satisfied by a fake of the thing under test. It lives where the pipe does: `DirectStdioClientTransportTests.AChildThatFillsTheStderrPipeBeforeAnyWorkStillGetsItsWorkDone` writes ~20,000 lines — well past the 64 KiB a Windows anonymous pipe buffers — **before** the child writes its report, so a transport that does not drain does not fail the test, it hangs it. The other item on that row worth naming is **cancellation**, which the layer really can do and does: a call the double holds open without blocking its own read loop, so it can still hear the notification it is being asked to observe.

**The most important test in the suite** is mechanical and follows from §"Known trade-offs": read the real child's `tools/list`, then assert **every** tool name carries an explicit session-type classification. An unclassified tool fails the build. That turns "a new upstream tool leaks into interactive mode" from a security incident into a red build.

> **Superseded 2026-08-16.** This paragraph argued that a nightly full-suite run against a freshly resolved payload still earned its place. **There are no automated checks of any kind** — no hosted CI, no scheduled job, no git hook ([Settled 2026-08-16](../README.md#settled-2026-08-16)). The paragraph is replaced rather than deleted because the gap it identified is real and still has to be answered.

**The gap between builds, and what actually covers it.** Every build resolves latest, so every build is already a drift check and the standalone drift job's original purpose is absorbed. What remains is the quiet week: upstream publishes **daily alphas**, so a week with no commits is a week in which the tree silently diverges from what was last proven green.

Two things close it, and neither is a scheduled job:

- **[The daily drift check](../CLAUDE.md#the-daily-drift-check)** — a directive that fires at the start of a working session rather than on a clock. It runs by construction, because the check happens when the work happens.
- **[The pre-release checklist](pre-release.md)** — which re-resolves everything and requires green before a release may be cut, so a quiet period cannot reach a user unexamined.

What is genuinely lost is *predictability*: the first build after a quiet period discovers the divergence rather than being told about it in advance. That cost is accepted, recorded here rather than softened, and **is part of what the post-v1 review of the no-automated-checks decision has to weigh**.

Lifecycle tests must wrap themselves in their own job object (`KILL_ON_CLOSE`, `using`-scoped) so a failed assertion cannot leave a stray `chrome.exe`, and must never match processes by image name — a test that kills `chrome.exe` by name will one day close the developer's browser.

**Every run starts by reclaiming what a previous run may have leaked.** Settled 2026-08-16. This suite drives machine-wide named objects, real processes and real directories, so a run that is killed — a failed assertion taking the host with it, a debugger detached, a CI agent recycled — leaves state behind that the *next* run meets as a failure. That failure reports the wrong cause: it names the change under test while describing the previous run's crash, and the time goes on the wrong bug.

The reclaim pass runs before anything else and is idempotent:

- **The machine-wide mutexes are acquired with `AbandonedMutexException` caught and treated as acquired**, which is [race R3](C-sessions.md#race-conditions-and-what-closes-each) met in the test host rather than in production. Unhandled, one crashed run disables the suite the same way it would disable sweeping.
- **Anything the previous run recorded is terminated by `(pid, creationFileTime)`** from its own spawn record — never by image name, which is the [§D](D-locking.md#never-by-image-name) rule and applies to test code with no exception. A PID whose creation time no longer matches is skipped, not killed.
- **The scratch root is deleted with the routine that survives a locked file** ([§E](E-lifecycle.md#deleting-a-tree-that-fights-back)), because the common leftover is a session directory a browser has not finished letting go of, and a delete that fails whole here fails the run for the previous run's reason.
- **Leftover session-index entries are removed**, since the index is machine-wide and a test run's entries otherwise show up in a developer's real `browserai_list`.

**The pass is itself a test.** It runs the same reclaim the product performs, so a defect in reclaim shows up as a suite that cannot start clean — which is a louder signal than a sweep that quietly finds nothing.

**The update path needs its own tests, and one of them has to be a real upgrade.** Put every Velopack call behind an interface with `virtual` network methods so the check → download → apply state machine can be driven hermetically; that seam is what lets UCC hold 48 update tests without ever touching the network. But both bugs that actually shipped in UCC sat *outside* that seam — the feed-URL composition, and the wrapper class itself, which has no tests at all. So the pre-release lane must also spend real time on two assertions the hermetic tests structurally cannot make: **resolve the production feed URL** over HTTP and assert a manifest comes back (a local-directory source composes paths differently and will pass where production 404s), and **publish N→N+1, apply it, and assert both that a delta package was generated and that the installed version moved**. Delta granularity is the reason [§G](G-updates.md) chose Velopack at all, and nothing in-house has ever proved `vpk` produces one.

## We write our own harness

We do **not** vendor the MCP SDK's test fixtures. They are 1,082 lines (Apache-2.0, unpublished to NuGet), they wire a single client↔server pipe pair where a proxy needs two hops, and copying them means a permanent three-way merge against an upstream that edits `tests/` weekly ([kb: SDK behaviours](../kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)). Writing ~100–200 lines ourselves buys a harness shaped for *this* product and frees the framework choice.

Two lessons are inherited deliberately rather than by copying, because they cost upstream real time to find:

- **Pin `DiscoverProbeTimeout` in test clients.** The SDK's own base class sets it explicitly, citing [csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701) — CI slowness spuriously tripped the probe. This is the same 5-second hazard as [§B](B-mcp-server.md), met from the other side.
- **Disposal order is load-bearing:** cancel the token → complete *both* pipe writers → await the server task → dispose the provider. Any other order hangs or throws.

  > ⚠️ **Measured 2026-08-16 at [build-order step 8](build-order.md#8-the-harness-and-the-fake-child): right about the consequence, wrong about the mechanism.** Removing one step at a time and running the whole suite, cancellation and completing both writers **each** end `McpServer.RunAsync` on their own — the suite is green with the cancellation removed entirely — and **only** completing both writers closes the hop. They are two independent ways to end the server task, not a sequence in which the first enables the second. Both are kept, for reasons that are now stated rather than inherited. [The four-way table is in kb](../kb/mcp/sdk.md#added-2026-08-16--the-in-process-harness-at-220).

What we build, and what each replaces:

| Component | Purpose | Replaces |
|---|---|---|
| `McpTestHarness` | The **two-hop** topology: test client → BrowserAI (server) … BrowserAI (client) → fake child. Two pipe pairs, not one. | `ClientServerTestBase` |
| `FakePlaywrightChild` | Scriptable in-process MCP server standing in for `@playwright/mcp`: canned `tools/list`, programmable `tools/call` results, injectable errors, delays, oversized payloads, unknown content types, mid-call death | `TestServerTransport` |
| `TUnitLoggerProvider` | Routes `ILogger` into TUnit's per-test output | `XunitLoggerProvider` + `DelegatingTestOutputHelper` |
| `CapturingLoggerProvider` | Captures log records for assertions | `MockLoggerProvider` |
| `TestDefaults` | Shared timeouts, including the probe-timeout pin above | `TestConstants` |
| `JobObjectScope` | `using`-scoped job object so a failed assertion cannot leak a `chrome.exe` | *(nothing upstream)* |
| `RawStdioClient` | A **hand-written JSON-RPC client over raw stdio**, sharing no code with the product. The independent oracle for the real-child and smoke tiers | *(nothing upstream — and nothing of ours)* |

Nothing from `NodeHelpers.cs` (577 lines of `npm install` machinery for the SDK's conformance suite) is wanted.

### Why the raw client is mandatory, not a nicety

**Settled 2026-08-16.** [Deviations 1 and 5](stack.md#nine-places-where-the-sdk-must-be-deviated-from) commit BrowserAI to writing **both** its own client transport *and* its own server transport. That is the right call for the product and it has a consequence the plan never stated: with the SDK's transports replaced on both ends, a test that drives BrowserAI through an `McpClient` is **testing the code under test using the code under test.** A symmetric bug — the same escaping assumption on the way out and on the way in, the same framing mistake made twice — passes green, and every layer above it inherits the same blind spot.

The oracle has to be something that shares no code with the product. Concretely, a client that:

- speaks **newline-delimited JSON-RPC** directly onto the child's stdin and reads its stdout, with no SDK type between the assertion and the bytes;
- **correlates by `id`** and **skips notifications** rather than assuming the next line is the answer — the reason the naive version works locally and hangs under load;
- **drains stderr and attaches it to every failure message**, because [the founding bug's only signal was in a body that looked successful](#testing-a-hard-requirement-and-the-release-gate) and a failure with the child's stderr missing is a failure that has to be reproduced by hand before it can be read;
- sets **`UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` on all three streams**, since [`Console` stdio defaults to CP437 in both directions and a hand-rolled `StreamWriter` adds a BOM](E-lifecycle.md) — a test harness that gets this wrong fails the product for the harness's defect;
- sets **`WorkingDirectory` explicitly**, because [an unset one passes `null` to `CreateProcess`](../README.md#windows-process-spawning) and the child silently inherits the test host's cwd.

**Prior art to copy rather than reinvent:** `C:\Source\SixFive7\OutlookAI\McpServer\OutlookAI.McpServer.Tests\T3\McpStdioClient.cs` — 261 lines, verified 2026-08-16, carrying all five properties above. (An earlier note in this project put it at 233 lines; it has grown since.) It exists there for the same reason it is needed here: to prove the wire protocol rather than the SDK's model of it.

## The tests, enumerated

Extensive is a requirement, so it is written down rather than implied. **Every item below is a release gate.**

**Unit** — no processes, no pipes:

- stderr classifier: error-shaped vs. the benign `Session: <path>` a healthy start always prints
- artifact prefix sort across all nine generator prefixes; a hand-named file is never swept into a machine folder
- tool filter / re-describe / `session` injection, **order-stable** (the spec SHOULDs deterministic ordering for prompt-cache hit rates) — and **every surviving upstream name arrives byte-for-byte**. [Renaming is settled as forbidden](../README.md#settled-2026-08-14), so this asserts identity rather than exercising a rename map; the day a map appears, the assertion is what says so
- **session-type enforcement, deny-by-default**, exercised against every tool in the surface
- **the handle→type lookup under concurrency.** [The hazard index calls this mandatory](hazards.md) and this list did not contain it until 2026-08-16, which is the omission the index exists to catch. The lookup is shared mutable state on every call's hot path, so with N instances live a race in it is not a glitch — it is a **session-type enforcement bypass**, an `interactive` handle resolving to a `persistent` classification for one call. Drive many concurrent `tools/call`s across handles of *different* modes and assert every refusal and every permission matches the handle that was passed, never a neighbour's. A single-threaded pass proves nothing about it, and "it uses a concurrent collection" is a claim about a type rather than about the read-decide-act sequence around it
- **the mode list is generated from one table** and appears identically in the server `instructions`, in `init`'s description, in `resume`'s playback and in the refusal text — a mode added in one place and missed in another fails the build
- **`init` rejects an absent, empty, relative or malformed session directory** rather than defaulting or normalising it
- **the cross-process window read still bypasses `WM_GETTEXT`** — spawn a child that creates a message-only window whose WndProc suppresses `WM_GETTEXT`, named with a known GUID, and assert the parent reads the GUID anyway. **No browser needed, milliseconds, every build.** This pins undocumented behaviour of a documented function; it goes red the day Windows routes that read through the message queue
- **a candidate whose directory cannot be attributed is reported, never killed** — the property that keeps the undocumented read off the critical path. Simulate an attribution failure and assert the sweep emits a stray-found-cannot-attribute diagnostic and terminates nothing
- **`GetWindowTextW` and `InternalGetWindowText` agree** on every window the sweep enumerates; a divergence means the fast path changed and the fix is a one-line switch
- **`EnumWindows` returns zero `Chrome_MessageWindow`s**, so nobody later "simplifies" the class-qualified walk into an `EnumWindows` loop that silently finds nothing
- **a window destroyed mid-walk produces `GetLastError() == 1400` and triggers a restart**, rather than truncating the enumeration
- **a title that is not a rooted local drive-letter path is rejected before any filesystem call** — a UNC title otherwise stalls the sweep for 21 seconds
- **every race in [the sweep table](C-sessions.md#race-conditions-and-what-closes-each) has a test.** Specifically: an `AbandonedMutexException` leaves sweeping functional (**R3**); the sweep refuses to kill when the directory lock is held (**R1**); a zero-timeout acquire under N concurrent starters runs exactly one sweep and blocks none of the others (**R9**); a re-asserted pointer restores itself after a wrongful delete (**R7**); and the sweep never writes to `stdout` (**R12**)
- **a mode refusal names the mode that would permit the call**, not merely that the call was refused
- session lifecycle: missing and unknown produce **two distinct LLM-readable errors**, each naming a recovery tool and recoverable in one turn. There is no *expired* state to test — reclaim is forever
- lock-name derivation: `GetFullPath` → `TrimEnd('\')` → `ToUpperInvariant` → SHA-256 → `Global\BrowserAI-{hash[..32]}`
- PID-recycle logic keyed on `(pid, creationFileTime)`, not a bare PID
- config generator and validator
- environment allowlist: `Clear()` runs first; `INIT_CWD`, `NODE_OPTIONS`, `NODE_PATH`, `DEBUG`, `DEBUG_FILE`, **`PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`** and the four `PLAYWRIGHT_DOWNLOAD_HOST` variants stripped; `PLAYWRIGHT_SKIP_BROWSER_GC=1` and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` set; `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` **not** set; `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`/`ALL_PROXY`/`NODE_EXTRA_CA_CERTS` passed through
- **job object construction**, asserted on the flags actually set: `KILL_ON_JOB_CLOSE` present; `BREAKAWAY_OK`, `SILENT_BREAKAWAY_OK` and `JobObjectBasicUIRestrictions` absent; the handle non-inheritable; assignment performed through `PROC_THREAD_ATTRIBUTE_JOB_LIST` rather than after the fact
- **no process is reachable by name**: zero occurrences of `Process.GetProcessesByName`, `taskkill /IM` or name-filtered WMI in the tree, at analyzer-error severity
- stdout wrapper: UTF-8, LF, no BOM — and no path in the process can reach `Console.Out`
- **model-facing text stays inside its budget**: the server `instructions` string and every tool description measured at build time, failing over **2 KB**. Claude Code truncates both silently, so the tail of any guidance that exceeds it does not exist and nothing reports that. Same shape as *an unclassified tool fails the build*; `SixFive7/OutlookAI` already pins its instructions string with a test
- artifact routing: a `filename` maps to the right typed subfolder, a duplicate name is suffixed rather than overwritten, `..` is refused rather than normalised, and the result carries **both** the absolute and the session-relative path *(built 2026-08-16 as `ArtifactRoutingTests`; **session**-relative rather than repository-relative, because BrowserAI cannot know where a repository root is — corrected in [§F](F-artifacts.md#four-obligations-that-follow) the same day)*
- the session index records one entry per routed artifact, and its roll-up is scoped to the output root rather than the machine

**Fake child** — full proxy over in-process pipes:

- `tools/call` results pass through **byte-identical**, including image and binary payloads — asserted on the exact byte span of `result` via `Utf8JsonReader` token offsets, never by re-serialising and comparing. This is what the custom server transport buys; against the SDK's own it fails on any string containing a backtick, apostrophe, angle bracket or non-ASCII character. **Qualified 2026-08-16 by [§F](F-artifacts.md), and by addition rather than by relaxation:** a call BrowserAI forwarded unchanged comes back unchanged, and a call whose `filename` BrowserAI rewrote comes back with every byte the child wrote, in order, plus **one appended `content` element** spliced into those same bytes. The test finds the single contiguous insertion rather than comparing lengths, so a rewritten middle fails
- **unknown content *types* pass through** rather than throwing (the SDK's typed layer throws; a proxy must not)
- **unknown *properties* survive** (the SDK's converter drops them, with tests asserting it does)
- `isError: true` bodies preserved verbatim; nested JSON-RPC `error.data` not flattened
- `session` injected into every schema, order-stable
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

**Marker** — resolved version equals reviewed version, for every upstream in [`upstream-review.json`](../upstream-review.json).

## What the build itself must fail on

**Settled 2026-08-16.** Some of this suite's job is done before a single test runs, by the compiler and by the AOT compiler. These are release gates in the same sense as everything above — the build configuration that enforces them is [in §stack](stack.md#the-build-configuration-this-plan-has-never-mentioned), and what each one catches is here, because each exists to close a specific silent failure.

**Warnings are errors, and `CS0162` — unreachable code — is promoted rather than left as a warning.** Unreachable code is not a tidiness complaint; it means the compiler proved a branch cannot execute, and in this codebase the branch that cannot execute is usually a guard, a `catch`, or a cleanup path. A warning in a build with thousands of lines of output is a warning nobody reads, and the failure it predicts is one this project has already named repeatedly: the recovery path that was never going to run.

**Non-empty ILC output fails the publish, and this is the one the analyzers cannot catch.** Observed and recorded here rather than re-run in this repository: **a NativeAOT publish exited 0 while ILC emitted `Method '...' will always throw because: Failed to load assembly '...'`.** Exit code zero, an artifact on disk, and a binary that throws the moment that code path is reached. Analyzers at error severity do not see it, because the analyzer ran against source and this was decided by the AOT compiler afterwards — [the same shape as everything in the README's opening table](../README.md#read-this-before-designing-anything), arriving from the toolchain instead of from upstream. The gate is mechanical: capture ILC's output and fail the publish if it is non-empty. **[§A](A-runtime.md) already requires publishing AOT and running the suite against it** before committing to it, and this is the check that makes that publish meaningful rather than merely completed.

**AOT and trim warning suppression is scoped per-assembly, never repo-wide.** A repo-wide suppression is permanent and invisible: it silences the warning for every assembly added afterwards, including the one that will actually be broken by it. Suppress on the assembly that needs it, with the reason beside the suppression, and the day a second assembly needs the same suppression is a day someone has to decide that on purpose.

**Never `UseSystemResourceKeys`.** It strips the framework's exception message strings, leaving bare resource keys in their place — a real size win for a NativeAOT binary and completely wrong for this product. **This product's error text is read by a model deciding what to do next**, which is the founding premise of [§H.4](H-model-surface.md#h4-the-error-catalogue); an exception surfacing as `Arg_DirectoryNotFound` instead of a sentence naming the path is an error catalogue that has been silently emptied. The saving is measured in kilobytes against a ~117 MB payload, so there is no version of this trade that is close. Assert the property is unset, so it cannot arrive later as somebody's size optimisation.

## The upstream-review gate

**Settled 2026-08-15, replacing an approval prompt with evidence.** The first design put a `PreToolUse` hook on `upstream-review.json` that asked a human to approve every edit. It was abandoned for two reasons, and the second is the one that matters.

It did not work. Measured 2026-08-15: under `permission_mode: bypassPermissions` a hook's `permissionDecision: "ask"` returned to a **sub-agent** is silently downgraded to allow. The edit lands unprompted. The gate was inert against precisely the caller most likely to trip it, and nothing reported that.

And it asked the wrong question. *"Did a human approve this?"* proves nothing even when answered — a click is not a review — and it puts a person in a loop they have no reason to want to be in. **The question that matters is "did the checks run, and what did they find?"**, and that is very nearly mechanical.

### What the gate actually checks

Four snapshots, regenerated from the resolved payload on every build and diffed against committed copies:

| Snapshot | Catches | Why it cannot be caught otherwise |
|---|---|---|
| `tools-list.json` | a tool added, removed, renamed, or its schema changed | — |
| `cli-help.txt` | a flag that vanished | **the `--output-mode` class** — that flag was a no-op for its entire life and nothing noticed |
| `config-schema.d.ts` | a renamed or removed config key | **the silent class.** `loadConfig` is a bare `JSON.parse` with no schema validation, so a renamed key is discarded without error |
| `browsers.json` | a moved browser revision | changes what first-run provisioning downloads |

**A diff fails the build with the diff itself in the failure message.** That is the whole point: not *"someone should look"*, but *"here is precisely what moved — adjudicate it."*

Alongside them, the two things the suite already proves: **every test green**, and the `browser_get_config` round-trip showing every opinion BrowserAI generated survived into the child.

### Re-verification, automated where it can be

[The re-verification index](../kb/README.md#re-verification-index) lists the measured facts a version bump can silently invalidate. Each row carries an **`Automated by`** column naming the test that re-establishes it, or `—` where no test can.

Some rows can be automated and some cannot: whether an upstream behaviour change affects an abstraction of ours is a judgement, not an assertion.

> ⚠️ **Corrected 2026-08-16 (previously: "Several already exist as tests and simply need wiring to their row — the browser is unregistered for restart, `--no-sandbox` is absent from the resolved command line, zero process leakage after a hard kill, the `Chrome_MessageWindow` lookup finds a browser we launched, the cross-process window read still bypasses `WM_GETTEXT`.")** None of those tests existed. Build-order step 4 wired the column and found **eight rows naming a test type that no build had ever produced** — rows 1, 2, 3, 4, 4a, 4b, 7 and 8 — which is the exact failure this section warns about one paragraph later. They were written from spike work that lived in `.work/`, never in the suite. All eight are back to *manual*, each naming the step that owes it, and `ReVerificationIndexTests` now fails the build on any row naming a test the assembly does not carry. Three rows moved the other way in the same step: 11, 12 and 15 are answered by the snapshot gate, which measures them from the resolved payload on every build.

**The split is the design.** An automated row is answered by the suite and needs no human. A manual row **must** be answered in the marker entry, by name, with an outcome. A row that is neither automated nor answered fails the gate.

### What the marker records

`reviewed` and `date` stay, but they stop meaning *"a human read this"* and start meaning *"these diffs were adjudicated"*. Each entry gains:

- **`snapshots`** — for each of the four, `unchanged` or an adjudication of what changed.
- **`reverification`** — for each manual row, its outcome. Automated rows are not listed; the suite owns them.
- **`notes`** — unchanged in spirit and now checkable: what changed, what was adopted, **and what was declined and why.** A decline with a reason is worth as much as an adoption, because it stops the same question being re-litigated at the next bump.

**A test asserts the entry is consistent with what actually moved.** If `config-schema.d.ts` changed and the entry's `snapshots` does not adjudicate it, the gate fails. If a manual re-verification row has no outcome, the gate fails. **You cannot record a review that ignores what the build observed** — which is the property the approval prompt never had.

> **Built 2026-08-16, and what is not.** Build-order step 4 built the four snapshots, the regenerate-and-diff on every build, the marker test (`UpstreamReviewTests`), the snapshot's own coherence checks (`UpstreamSnapshotTests`) and the `Automated by` gate (`ReVerificationIndexTests`).
>
> **The `snapshots` and `reverification` fields are deliberately not built yet, and the reason is not effort.** Today every entry in `upstream-review.json` is a baseline: nothing has moved, so there is nothing to adjudicate. A test demanding those fields now could only be satisfied by writing an adjudication of no change for four snapshots and an outcome for **every manual row** — around forty of them, most naming code that does not exist before step 12. That is a review that did not happen, typed out to make a suite green, which is the same act as [editing the marker to make a test pass](../CLAUDE.md#before-changing-upstream-reviewjson--stop-and-read-the-procedure). The fields land with the **first real bump**, when there is something true to write in them; until then the marker test is what fires, and it fires on exactly the event that creates the obligation.

### What the hook becomes

Not a permission gate. It can inject the procedure as `additionalContext` when the marker is touched, so whoever is editing knows what is required — but it decides nothing, blocks nothing, and prompts nobody.

> **The general lesson, recorded because it outlives this file.** A hook returning `ask` is **not** an enforcement mechanism: it is inert against sub-agents under bypass, and against a human it only proves a click. Enforcement belongs in the suite, where it is evidence rather than assent. If a rule can be a failing test, it must be one — and this is the case that proves the rule applies to our own tooling too.


## The release gate

**Releases are triggered manually, by the maintainer, through the agent. There is no release pipeline, no scheduled publish, and no auto-merge on green.** That simplification is affordable *only* because the gate itself is mechanical: when to release is a human decision, whether a release is permitted is not.

The sequence, in order, no step skippable:

1. **Resolve.** The build takes the latest of every dependency and records what it got — `packages.lock.json`, the resolved `package-lock.json`, browser revisions from the resolved `browsers.json`.
2. **Build.** NativeAOT (or trimmed self-contained), analyzers at error severity. A warning-as-error is a red build.
3. **Run everything.** All five layers, including the two marked *mandatory before release*. Not a subset, not "the fast ones", not "the ones related to this change". This is also where [the upstream-review gate](#the-upstream-review-gate) fires: if the resolved version moved past the reviewed one, or a snapshot changed without an adjudication, or a manual re-verification row has no outcome, the suite is red and there is nothing to decide at step 5.
4. **Green, or stop.** A failure is a work item, never a waiver. If upstream broke something, the fix is to make the new version work — [rule 4](../README.md#the-five-rules-that-make-floating-safe).
5. **The maintainer decides.** Green is necessary and not sufficient: a green build is *releasable*, not *released*.
6. **Cut it.** `vpk pack`, publish, and record the resolved set alongside the artifact so the release can state exactly what it contains.

**Why manual is right here, and the condition under which it stops being right.** With one maintainer and a single track, a release pipeline is ceremony around a decision one person makes anyway — and [§G](G-updates.md) is already the most hazard-dense section in this document without adding pipeline-authored releases to it. The honest cost: **the gate is only as good as the person invoking it.** It rests entirely on step 3 being *run* rather than assumed. The day a second person can cut a release, that assumption breaks and the gate has to move into automation.

**What manual does not mean.** It does not mean the suite runs when someone remembers. Steps 1–4 are the ordinary build and run on every build, whether or not a release is in view. Manual governs step 5 alone.

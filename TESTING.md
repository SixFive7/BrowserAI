<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Testing: a hard requirement, and the release gate

> **Salvaged 2026-08-17 from `TESTING.md`, which is consumed and deleted
> like every other section of the plan.** What moved here is the part that does
> not stop being true once the code exists: the argument for the suite, what the
> build itself must fail on, why the upstream-review gate is the suite rather
> than a hook, and why the test harness is ours rather than upstream's. What
> stayed behind was the plan's own checklist of tests to write, because the
> suite is now the record of which tests exist. Nothing was reworded in the
> move except links that pointed at files being deleted.

> **This document is a requirement, not an aspiration, and it is not severable
> from the [versioning policy](DECISIONS.md#versioning-policy-everything-floats-the-build-freezes-it).**
>
> [Versioning policy](DECISIONS.md#versioning-policy-everything-floats-the-build-freezes-it)
> puts every dependency on latest at build time. That makes the suite **the only
> thing standing between an upstream change and a shipped regression** —
> floating without a suite that can catch a breaking change is strictly worse
> than pinning. The two decisions are one decision: neither is valid alone, and
> weakening the suite silently converts the versioning policy into a liability.
>
> Three rules follow, and none of them are negotiable per-release:
>
> - **No release is cut with a red test.** Not "a known failure", not "unrelated
>   to this change", not "it passes locally".
> - **No release is cut with a skipped, quarantined or conditionally-ignored
>   test.** A `Skip` attribute in the tree at release time is a red build wearing
>   a disguise. Flakiness is a defect to fix, not a state to tolerate.
> - **Coverage of the boundary is mandatory, not incidental.** Every tool
>   classified by session type, every config key validated against the shipped
>   runtime, every `PLAYWRIGHT_MCP_*` override accounted for. An unclassified
>   tool fails the build — that rule is what makes an upstream addition a red
>   build instead of a security incident.

The founding bug was reproduced during research. Pointing `executablePath` at a
non-existent binary produces:

```
exit code 0  ·  stderr EMPTY  ·  initialize OK  ·  tools/list → 24 tools
tools/call   → JSON-RPC *success* response, body: {"isError": true, ...}
```

**Every conventional health signal is green.** The single bit that says "broken"
is `isError` inside a 200-equivalent body. Transport-level assertions,
protocol-level assertions, exit codes, stderr scanning and `tools/list` **cannot
detect this class at all**.

So: any smoke test that stops at `tools/list` reproduces the exact five-day
blindness described at the top of the charter. The minimum viable assertion is a
real navigation — measured at **0.43 s** with no network and no local server
([kb: timings](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)).
Use `data:text/html,<h1>ok</h1>`, not `about:blank` — the latter succeeds too
trivially and its snapshot is empty.

Five layers, run at different cadences:

| Layer | Drives | Cost | When |
|---|---|---|---|
| **Unit** | stderr classifier, artifact prefix sort, tool filter and re-describe with **names passed through unchanged**, **session-type enforcement**, lock signature and PID-recycle logic, config validator | ms | every build |
| **Fake child** | Full proxy over an in-process `Pipe` pair — no `Process`, no Node. Passthrough fidelity, error shapes, image bytes, cancellation, child death | ms | every build |
| **Real-child contract** | Real `node` + the **resolved** `cli.js`, **no browser**. Golden `tools/list` snapshot, negotiated protocol version, argv contract, config-key validation | 2–5 s | **every build** |
| **Smoke** | Real child **and real browser**. `browser_navigate`, `isError`, real stderr classification, process-tree lifecycle | 10–30 s | every build · **mandatory before release** |
| **Update** | Real feed URL resolves and returns a manifest; `vpk pack` emits a delta; N→N+1 applies and the installed version moves | 1–3 min | **mandatory before release** |

**The real-child contract layer changes character under a floating build.** When
the payload was hand-pinned it was a slow-moving regression check that could
reasonably run nightly. Now it is *the* mechanism that detects an upstream
Playwright change, and it runs on **every build** — 2–5 seconds is nothing
against the alternative of finding out from a user. Its golden snapshots are the
tripwire; a diff there is not a test failure to suppress but the notification
this whole design exists to produce.

`McpClient.CreateAsync` accepts the `IClientTransport` *interface*, so the fake
child is an in-process `McpServer` joined by two `Pipe`s — no processes, no
ports, fully parallel-safe.

> ⚠️ **Corrected 2026-08-16 (previously the Fake-child row also claimed "stderr
> back-pressure").** A layer whose whole premise is that it starts no process has
> no stderr pipe to fill, so that item could only ever have been satisfied by a
> fake of the thing under test. It lives where the pipe does:
> `DirectStdioClientTransportTests.AChildThatFillsTheStderrPipeBeforeAnyWorkStillGetsItsWorkDone`
> writes ~20,000 lines — well past the 64 KiB a Windows anonymous pipe buffers —
> **before** the child writes its report, so a transport that does not drain does
> not fail the test, it hangs it. The other item on that row worth naming is
> **cancellation**, which the layer really can do and does: a call the double
> holds open without blocking its own read loop, so it can still hear the
> notification it is being asked to observe.

**The most important test in the suite** is mechanical and follows from the
charter's [Known trade-offs](DECISIONS.md#known-trade-offs): read the real child's
`tools/list`, then assert **every** tool name carries an explicit session-type
classification. An unclassified tool fails the build. That turns "a new upstream
tool leaks into interactive mode" from a security incident into a red build.

**The gap between builds, and what actually covers it.** Every build resolves
latest, so every build is already a drift check. What remains is the quiet week:
upstream publishes **daily alphas**, so a week with no commits is a week in which
the tree silently diverges from what was last proven green. There are **no
automated checks of any kind** — no hosted CI, no scheduled job, no git hook
([DECISIONS → Locking, logging, versioning and registration](DECISIONS.md#locking-logging-versioning-and-registration)).

Two things close it, and neither is a scheduled job:

- **[The daily drift check](CLAUDE.md#the-daily-drift-check)** — a directive that
  fires at the start of a working session rather than on a clock. It runs by
  construction, because the check happens when the work happens.
- **[The release checklist](RELEASING.md)** — which re-resolves everything
  and requires green before a release may be cut, so a quiet period cannot reach
  a user unexamined.

What is genuinely lost is *predictability*: the first build after a quiet period
discovers the divergence rather than being told about it in advance. That cost is
accepted, recorded here rather than softened, and **is part of what the post-v1
review of the no-automated-checks decision has to weigh**.

Lifecycle tests must wrap themselves in their own job object (`KILL_ON_CLOSE`,
`using`-scoped) so a failed assertion cannot leave a stray `chrome.exe`, and must
never match processes by image name — a test that kills `chrome.exe` by name will
one day close the developer's browser.

**Every run starts by reclaiming what a previous run may have leaked.** Settled
2026-08-16. This suite drives machine-wide named objects, real processes and real
directories, so a run that is killed — a failed assertion taking the host with
it, a debugger detached, a CI agent recycled — leaves state behind that the
*next* run meets as a failure. That failure reports the wrong cause: it names the
change under test while describing the previous run's crash, and the time goes on
the wrong bug.

The reclaim pass runs before anything else and is idempotent:

- **The machine-wide mutexes are acquired with `AbandonedMutexException` caught
  and treated as acquired**, which is race R3 (`StraySweepTests`) met in the test
  host rather than in production. Unhandled, one crashed run disables the suite
  the same way it would disable sweeping.
- **Anything the previous run recorded is terminated by `(pid,
  creationFileTime)`** from its own spawn record — never by image name, which is
  the rule `NeverByImageNameTests` enforces and which applies to test code with no
  exception. A PID whose creation time no longer matches is skipped, not killed.
- **The scratch root is deleted with the routine that survives a locked file**
  (`TreeDelete`), because the common leftover is a session directory a browser
  has not finished letting go of, and a delete that fails whole here fails the run
  for the previous run's reason.
- **Leftover session-index entries are removed**, since the index is machine-wide
  and a test run's entries otherwise show up in a developer's real
  `browserai_list`.

**The pass is itself a test.** It runs the same reclaim the product performs, so a
defect in reclaim shows up as a suite that cannot start clean — which is a louder
signal than a sweep that quietly finds nothing.

**The update path needs its own tests, and one of them has to be a real
upgrade.** Put every Velopack call behind an interface with `virtual` network
methods so the check → download → apply state machine can be driven
hermetically; that seam is what lets the in-house prior art hold 48 update tests
without ever touching the network. But both bugs that actually shipped in that
prior art sat *outside* that seam — the feed-URL composition, and the wrapper
class itself, which has no tests at all. So the pre-release lane must also spend
real time on two assertions the hermetic tests structurally cannot make:
**resolve the production feed URL** over HTTP and assert a manifest comes back (a
local-directory source composes paths differently and will pass where production
404s), and **publish N→N+1, apply it, and assert both that a delta package was
generated and that the installed version moved**. Delta granularity is the reason
Velopack was chosen at all ([kb](kb/packaging/velopack.md#the-update-lane-end-to-end-against-a-real-feed)),
and nothing in-house had ever proved `vpk` produces one before 2026-08-16.

## The first-run download runs at most once an hour

**Settled 2026-08-17, on the maintainer's instruction:** *"Add a cache of sorts
so that it only downloads once per hour. I don't want to hammer the servers.
Especially if we are going to run this more with more tests now."*

`FirstRunProvisioningTests` provisions Chromium into an **empty** browsers root
through the published binary, which is **203.8 MB** off Playwright's CDN every
run ([kb](kb/playwright/provisioning-and-timings.md#first-run-provisioning)).
That was one run a day. It is about to be dozens.

**What is cached is the provisioned tree, and the hour runs from the download.**
`.work/first-run-cache/` holds a single entry — the browsers root a cold run
produced, and a stamp naming when the bytes were fetched, which revision they
are, and how many files and bytes the tree holds. Inside the hour the test seeds
from it; outside it, the test downloads for real and replaces the entry. **A
cached run never touches the stamp**, so the ceiling is also a floor: the
genuinely cold path runs at least once per hour of runs, rather than being
deferred forever by use.

**A cached run is not a different test wearing the same name — it is the same
test taking the product's own cross-process path.** BrowserAI serialises installs
on a `Global\` mutex keyed on the browsers root, and a process that does not get
it *does not download*: it watches for the marker the holder will write. So a
cached run takes that mutex first, and everything else is unchanged — `init`
answers at once and reports `downloading`, every browser tool is refused with the
size and a route out, `browserai_list` keeps answering, and the same child
navigates against a real Chromium once the marker lands, with no restart. The
seed writes every byte before it writes a single `INSTALLATION_COMPLETE`, which
is upstream's own ordering and the reason a watching process cannot see a
half-copied tree. It also exercises `WaitForAnotherProcess` end to end, which
nothing else does outside a double.

**What a cached run cannot prove**, stated here because a test that quietly stops
exercising its subject is the failure class this repository exists to eliminate:
that Playwright's CDN is up, that `cftUrl` still resolves, that the revision the
payload pins is still served, and that `install-browser --no-shell` still does
what its name says. The layout assertions still run, against bytes a real
download produced **within the hour** rather than within the second.

**Four mechanisms stop that becoming silent, and the first is the one that
matters:**

- **A release run never uses the cache.** `BROWSERAI_RELEASE_RUN=1` forces the
  CDN, so no release is cut on evidence that came out of `.work\`.
- **Every run says which path it took**, in the same coverage block that makes a
  degraded run distinguishable from a real one — a `first-run bytes` row reading
  `CDN`, `CACHED` or `NOT RUN`, with the age of the tree it used.
- **The ceiling is a ceiling**, and a stamp dated in the future is refused rather
  than trusted forever.
- **`BROWSERAI_FIRST_RUN_CACHE=off`** forces a cold run without editing anything.

**A partial cache is refused rather than used**, and the completeness signal is
the same one BrowserAI itself trusts plus a census the marker cannot give:
`chromium-<rev>/INSTALLATION_COMPLETE`, an `ffmpeg-*` marker, `chrome.exe` where
the payload says, **no** `chromium_headless_shell-*` — a cache carrying one would
make the negative assertion pass for the wrong reason — and a file-and-byte count
matching the stamp. `FirstRunCacheTests` drives every one of those refusals
against a three-file planted tree, in milliseconds, and never touches the real
cache.

**Publishing is committed by a rename, never by a write.** The tree is copied
into `.staging-<guid>\`, which readers do not enumerate, and then moved to
`entry-<stamp>-<guid>\` in one `MoveFileEx`. The destination name carries a GUID,
so two publishers cannot collide and neither needs a lock — the same answer, for
the same reason, that the session index gives to eight concurrent writers rather
than reaching for `FileMode.Append`.

**The cost, measured 2026-08-17 over eight full-suite runs on the reference
machine** ([kb](kb/playwright/provisioning-and-timings.md#what-the-first-run-download-costs-the-suite)):
a cached run's suite wall time is **31.2–34.7 s** against **35.8–36.8 s** cold,
and the test itself drops from **13.8–17.1 s** to **3.2–3.9 s**. The download is
therefore worth **10–12%** of the suite's wall clock — far less than the test's
own duration suggests, because the suite runs every test at once and the download
overlaps other tests. *(Corrected 2026-08-17 (previously "the suite runs
four-wide"): the four-way cap was removed the next day, and the percentage above
was measured under it — the ratio is not re-measured here and the wall times it
quotes are the capped ones.)* **The saving is bandwidth, not seconds**, which is what was asked
for; and the same measurement establishes that a cached run reaches the network
for **133,761 B** where a cold one moves **425 MB** across the adapter counters.

## We write our own harness

We do **not** vendor the MCP SDK's test fixtures. They are 1,082 lines
(Apache-2.0, unpublished to NuGet), they wire a single client↔server pipe pair
where a proxy needs two hops, and copying them means a permanent three-way merge
against an upstream that edits `tests/` weekly
([kb: SDK behaviours](kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)).
Writing ~100–200 lines ourselves buys a harness shaped for *this* product and
frees the framework choice.

Two lessons are inherited deliberately rather than by copying, because they cost
upstream real time to find:

- **Pin `DiscoverProbeTimeout` in test clients.** The SDK's own base class sets it
  explicitly, citing [csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701)
  — CI slowness spuriously tripped the probe. This is the same 5-second hazard as
  the protocol split ([kb](kb/mcp/protocol.md#the-protocol-split)), met from the
  other side.
- **Disposal order is load-bearing:** cancel the token → complete *both* pipe
  writers → await the server task → dispose the provider. Any other order hangs
  or throws.

  > ⚠️ **Measured 2026-08-16: right about the consequence, wrong about the
  > mechanism.** Removing one step at a time and running the whole suite,
  > cancellation and completing both writers **each** end `McpServer.RunAsync` on
  > their own — the suite is green with the cancellation removed entirely — and
  > **only** completing both writers closes the hop. They are two independent ways
  > to end the server task, not a sequence in which the first enables the second.
  > Both are kept, for reasons that are now stated rather than inherited.
  > [The four-way table is in kb](kb/mcp/sdk.md#error-shape-and-teardown-seen-from-an-in-process-harness).

What we build, and what each replaces:

| Component | Purpose | Replaces |
|---|---|---|
| `McpTestHarness` | The **two-hop** topology: test client → BrowserAI (server) … BrowserAI (client) → fake child. Two pipe pairs, not one. | `ClientServerTestBase` |
| `FakePlaywrightChild` | Scriptable in-process MCP server standing in for `@playwright/mcp`: canned `tools/list`, programmable `tools/call` results, injectable errors, delays, oversized payloads, unknown content types, mid-call death | `TestServerTransport` |
| `TUnitLoggerProvider` | Routes `ILogger` into TUnit's per-test output | `XunitLoggerProvider` + `DelegatingTestOutputHelper` |
| `CapturingLoggerProvider` | Captures log records for assertions | `MockLoggerProvider` |
| `TestDefaults` | The suite's whole vocabulary of **hang detectors** — `InProcessHang`, `ProcessHang`, `BrowserHang` — plus the probe-timeout pin above and the initialization pin below | `TestConstants` |
| `JobObjectScope` | `using`-scoped job object so a failed assertion cannot leak a `chrome.exe` | *(nothing upstream)* |
| `RawStdioClient` | A **hand-written JSON-RPC client over raw stdio**, sharing no code with the product. The independent oracle for the real-child and smoke tiers | *(nothing upstream — and nothing of ours)* |

Nothing from `NodeHelpers.cs` (577 lines of `npm install` machinery for the SDK's
conformance suite) is wanted.

### Why the raw client is mandatory, not a nicety

**Settled 2026-08-16.** [Deviations 1 and 5](STACK.md#nine-places-where-the-sdk-must-be-deviated-from)
commit BrowserAI to writing **both** its own client transport *and* its own
server transport. That is the right call for the product and it has a consequence
the plan never stated: with the SDK's transports replaced on both ends, a test
that drives BrowserAI through an `McpClient` is **testing the code under test
using the code under test.** A symmetric bug — the same escaping assumption on
the way out and on the way in, the same framing mistake made twice — passes
green, and every layer above it inherits the same blind spot.

The oracle has to be something that shares no code with the product. Concretely, a
client that:

- speaks **newline-delimited JSON-RPC** directly onto the child's stdin and reads
  its stdout, with no SDK type between the assertion and the bytes;
- **correlates by `id`** and **skips notifications** rather than assuming the next
  line is the answer — the reason the naive version works locally and hangs under
  load;
- **drains stderr and attaches it to every failure message**, because
  [the founding bug's only signal was in a body that looked successful](#testing-a-hard-requirement-and-the-release-gate)
  and a failure with the child's stderr missing is a failure that has to be
  reproduced by hand before it can be read;
- sets **`UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` on all three
  streams**, since [`Console` stdio defaults to CP437 in both directions and a
  hand-rolled `StreamWriter` adds a BOM](kb/windows/processes.md#stdio-exit-codes-and-process-startup)
  — a test harness that gets this wrong fails the product for the harness's
  defect;
- sets **`WorkingDirectory` explicitly**, because
  [an unset one passes `null` to `CreateProcess`](DECISIONS.md#windows-process-spawning)
  and the child silently inherits the test host's cwd.

**Prior art to copy rather than reinvent:** an in-house `McpStdioClient` in a
sibling project's test tree, unpublished and not reachable from this repository
— 261 lines, verified 2026-08-16, carrying all five properties above. (An earlier
note in this project put it at 233 lines; it has grown since.) It exists there for
the same reason it is needed here: to prove the wire protocol rather than the
SDK's model of it.

## Every duration is a hang detector, or it is a defect

**Settled 2026-08-18**, the maintainer's instruction verbatim: *"Remove any
timings other than timeouts that catch really hung processes. Even on slow
systems. … Best case scenario is that we remove all the timing things and have
everything push or event driven with only timeouts for VERY good reasons. Like
relaxed timeouts that catch hung tests or something. But these should have ample
of room so tests do not hit these even under constrained system resources."*

Every duration in `tests/` therefore answers one question, and the answer decides
what happens to it.

| It is… | Then |
|---|---|
| **guessing how long something takes** | Delete it. Replace it with the event it was standing in for: a handle that signals, a process that exits, a file that appears, a frame that arrives, a gate the test releases, a `ManualClock` the test drives |
| **catching a hang** | Keep it, take it from `TestDefaults`, and give it headroom a starved machine cannot reach. Say on it that it is a hang detector and that nothing may assert on it |

**A promptness assertion wearing a hang detector's name is the defect**, not the
safeguard. Deleting one is not weakening a test; it is deleting a test of the
wrong thing — and in every case here the property the stopwatch claimed to
establish was already asserted, decisively, one or two lines away.

Three rules follow, each of which has a scar behind it:

- **The vocabulary is `TestDefaults` and nothing else invents a number.**
  `InProcessHang` (5 min), `ProcessHang` (10 min), `BrowserHang` (30 min),
  `InitializationHang`. Twenty-two per-file constants used to hold 30, 60, 90,
  120 or 180 seconds of their own; they derive now, so two layers cannot
  disagree.
- **The same bound must never be expressed twice.** The shape to hunt hardest is
  the tighter of two layers winning invisibly: a launcher that gave up at 60 s
  under a host advertising 180 s reported a launch that failed at 60 s as a
  three-minute timeout, and an unset SDK `InitializationTimeout` did the same
  thing wearing a dependency's clothes. Make the inner one derive from the outer,
  or delete it.
- **A bound a busy machine can reach is not detecting a hang.** At
  `SuiteParallelism.Unbounded` the entire failure population was three messages —
  46 `Initialization timed out`, 71 `No frame arrived on this pipe within 30 s`,
  48 bare `A task was canceled` — and not one was a logic fault
  ([kb](kb/toolchain.md#running-419-tests-at-once-what-starves-and-by-how-much)).

**A polling interval is not a bound and is not covered by this.** A loop that
samples every 25 ms until a condition holds can make a run slower; it cannot make
it redder. What matters is the deadline the loop gives up at.

## What the build itself must fail on

**Settled 2026-08-16.** Some of this suite's job is done before a single test
runs, by the compiler and by the AOT compiler. These are release gates in the same
sense as everything above — the build configuration that enforces them is in
[STACK.md](STACK.md#the-build-configuration), and what each one catches is here,
because each exists to close a specific silent failure.

**Warnings are errors, and `CS0162` — unreachable code — is promoted rather than
left as a warning.** Unreachable code is not a tidiness complaint; it means the
compiler proved a branch cannot execute, and in this codebase the branch that
cannot execute is usually a guard, a `catch`, or a cleanup path. A warning in a
build with thousands of lines of output is a warning nobody reads, and the failure
it predicts is one this project has already named repeatedly: the recovery path
that was never going to run.

**Non-empty ILC output fails the publish, and this is the one the analyzers cannot
catch.** Observed and recorded rather than re-run in this repository: **a
NativeAOT publish exited 0 while ILC emitted `Method '...' will always throw
because: Failed to load assembly '...'`.** Exit code zero, an artifact on disk, and
a binary that throws the moment that code path is reached. Analyzers at error
severity do not see it, because the analyzer ran against source and this was
decided by the AOT compiler afterwards —
[the same shape as everything in the charter's opening table](DECISIONS.md#the-setup-this-replaces),
arriving from the toolchain instead of from upstream. The gate is mechanical:
capture ILC's output and fail the publish if it is non-empty. Publishing AOT and
running the suite against it is required before committing to it, and this is the
check that makes that publish meaningful rather than merely completed.

**AOT and trim warning suppression is scoped per-assembly, never repo-wide.** A
repo-wide suppression is permanent and invisible: it silences the warning for
every assembly added afterwards, including the one that will actually be broken by
it. Suppress on the assembly that needs it, with the reason beside the
suppression, and the day a second assembly needs the same suppression is a day
someone has to decide that on purpose.

**Never `UseSystemResourceKeys`.** It strips the framework's exception message
strings, leaving bare resource keys in their place — a real size win for a
NativeAOT binary and completely wrong for this product. **This product's error
text is read by a model deciding what to do next**, which is the founding premise
of the error catalogue (`SessionErrors`); an exception surfacing as
`Arg_DirectoryNotFound` instead of a sentence naming the path is an error
catalogue that has been silently emptied. The saving is measured in kilobytes
against a ~117 MB payload, so there is no version of this trade that is close.
Assert the property is unset, so it cannot arrive later as somebody's size
optimisation. ✅ **Asserted 2026-08-16 by
`BuildConfigurationTests.UseSystemResourceKeysIsExplicitlyFalseEverywhereItAppears`**,
which had been required and written nowhere for as long as the sentence existed —
found by [release checklist item 7](RELEASING.md) looking for the evidence it asks
for. It refuses any value other than `false` in any build file **and requires the
declaration to be present in `Directory.Build.props`**: the default is already off,
so a file that never mentions it would pass a "not true" check while telling the
next reader nothing.

## The upstream-review gate

**Settled 2026-08-15, replacing an approval prompt with evidence.** The first
design put a `PreToolUse` hook on [`upstream-review.json`](upstream-review.json)
that asked a human to approve every edit. It was abandoned for two reasons, and
the second is the one that matters.

It did not work. Measured 2026-08-15: under `permission_mode: bypassPermissions` a
hook's `permissionDecision: "ask"` returned to a **sub-agent** is silently
downgraded to allow. The edit lands unprompted. The gate was inert against
precisely the caller most likely to trip it, and nothing reported that.

And it asked the wrong question. *"Did a human approve this?"* proves nothing even
when answered — a click is not a review — and it puts a person in a loop they have
no reason to want to be in. **The question that matters is "did the checks run, and
what did they find?"**, and that is very nearly mechanical.

### What the gate actually checks

Four snapshots, regenerated from the resolved payload on every build and diffed
against committed copies:

| Snapshot | Catches | Why it cannot be caught otherwise |
|---|---|---|
| `tools-list.json` | a tool added, removed, renamed, or its schema changed | — |
| `cli-help.txt` | a flag that vanished | **the `--output-mode` class** — that flag was a no-op for its entire life and nothing noticed |
| `config-schema.d.ts` | a renamed or removed config key | **the silent class.** `loadConfig` is a bare `JSON.parse` with no schema validation, so a renamed key is discarded without error |
| `browsers.json` | a moved browser revision | changes what first-run provisioning downloads |

**A diff fails the build with the diff itself in the failure message.** That is the
whole point: not *"someone should look"*, but *"here is precisely what moved —
adjudicate it."*

Alongside them, the two things the suite already proves: **every test green**, and
the `browser_get_config` round-trip showing every opinion BrowserAI generated
survived into the child.

### Re-verification, automated where it can be

[The re-verification index](kb/re-verification.md) lists the measured
facts a version bump can silently invalidate. Each row carries an **`Automated
by`** column naming the test that re-establishes it, or `—` where no test can.

Some rows can be automated and some cannot: whether an upstream behaviour change
affects an abstraction of ours is a judgement, not an assertion.

> ⚠️ **Corrected 2026-08-16 (previously: "Several already exist as tests and
> simply need wiring to their row — the browser is unregistered for restart,
> `--no-sandbox` is absent from the resolved command line, zero process leakage
> after a hard kill, the `Chrome_MessageWindow` lookup finds a browser we
> launched, the cross-process window read still bypasses `WM_GETTEXT`.")** None of
> those tests existed. Wiring the column found **eight rows naming a test type
> that no build had ever produced** — rows 1, 2, 3, 4, 4a, 4b, 7 and 8 — which is
> the exact failure this section warns about one paragraph later. They were
> written from spike work that lived in `.work/`, never in the suite. All eight
> went back to *manual*, each naming what owed it, and `ReVerificationIndexTests`
> now fails the build on any row naming a test the assembly does not carry. Three
> rows moved the other way in the same pass: 11, 12 and 15 are answered by the
> snapshot gate, which measures them from the resolved payload on every build.

**The split is the design.** An automated row is answered by the suite and needs no
human. A manual row **must** be answered in the marker entry, by name, with an
outcome. A row that is neither automated nor answered fails the gate.

### What the marker records

`reviewed` and `date` stay, but they stop meaning *"a human read this"* and start
meaning *"these diffs were adjudicated"*. Each entry gains:

- **`snapshots`** — for each of the four, `unchanged` or an adjudication of what
  changed.
- **`reverification`** — for each manual row, its outcome. Automated rows are not
  listed; the suite owns them.
- **`notes`** — unchanged in spirit and now checkable: what changed, what was
  adopted, **and what was declined and why.** A decline with a reason is worth as
  much as an adoption, because it stops the same question being re-litigated at
  the next bump.

**A test asserts the entry is consistent with what actually moved.** If
`config-schema.d.ts` changed and the entry's `snapshots` does not adjudicate it,
the gate fails. If a manual re-verification row has no outcome, the gate fails.
**You cannot record a review that ignores what the build observed** — which is the
property the approval prompt never had.

> **Built 2026-08-16, and what is not.** The four snapshots, the
> regenerate-and-diff on every build, the marker test (`UpstreamReviewTests`), the
> snapshot's own coherence checks (`UpstreamSnapshotTests`) and the `Automated by`
> gate (`ReVerificationIndexTests`) are all built.
>
> **The `snapshots` and `reverification` fields are deliberately not built yet, and
> the reason is not effort.** Today every entry in `upstream-review.json` is a
> baseline: nothing has moved, so there is nothing to adjudicate. A test demanding
> those fields now could only be satisfied by writing an adjudication of no change
> for four snapshots and an outcome for **every manual row** — around forty of
> them. That is a review that did not happen, typed out to make a suite green,
> which is the same act as
> [editing the marker to make a test pass](CLAUDE.md#rules-a-mechanism-enforces).
> The fields land with the **first real bump**, when there is something true to
> write in them; until then the marker test is what fires, and it fires on exactly
> the event that creates the obligation.

### What the hook becomes

Not a permission gate. It can inject the procedure as `additionalContext` when the
marker is touched, so whoever is editing knows what is required — but it decides
nothing, blocks nothing, and prompts nobody.

> **The general lesson, recorded because it outlives this file.** A hook returning
> `ask` is **not** an enforcement mechanism: it is inert against sub-agents under
> bypass, and against a human it only proves a click. Enforcement belongs in the
> suite, where it is evidence rather than assent. If a rule can be a failing test,
> it must be one — and this is the case that proves the rule applies to our own
> tooling too.

## Continuous integration

**Added 2026-08-18, and it is the first time any of this ran on a machine nobody
owns.** [`.github/workflows/build.yml`](.github/workflows/build.yml) builds the
payload, provisions Chromium and Firefox, publishes the NativeAOT binary and runs
the **whole** suite — `SaturationTests` included, because a CI that skipped the
expensive half would recreate the gap it exists to close. Its own header states
what it costs (about 204 MB of first-run browser download per run, plus ~125.7 MB
for Firefox, uncached on purpose) and what it does **not** cover.

Two things a reader should know before treating a red build as a defect:

- **`BROWSERAI_RELEASE_RUN` is deliberately unset.** It turns an absent capability
  from a loud skip into a failure, and two capabilities are genuinely absent on a
  runner — the packed release and an installed MCP client. CI is an ordinary run
  that states its own coverage; the release gate is still
  [`RELEASING.md`](RELEASING.md#the-release-gate), driven locally.
- **A red `UpstreamReviewTests` means upstream moved and nobody reviewed it.**
  That is [the marker gate](#the-upstream-review-gate) working, not a stale file.
  Lock-file drift is reported into the job summary and never fails the build,
  because drift is information; **adopting** a moved version is what needs the
  review.

## The release gate

**Lives in [`RELEASING.md`](RELEASING.md#the-release-gate)**, beside the
checklist that enforces it — the six-step sequence of resolve, build, run
everything, green-or-stop, the maintainer decides, cut it. This heading is kept so
every link into it still resolves.

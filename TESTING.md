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
> - **Coverage of the boundary is mandatory, not incidental.** Every tool in the
>   surface diffed against the golden `tools/list` snapshot **with its
>   `inputSchema`**, every config key validated against the shipped runtime,
>   every `PLAYWRIGHT_MCP_*` override accounted for. A tool upstream adds,
>   removes or re-shapes fails the build — that rule is what makes an upstream
>   change a red build instead of a surprise in production.
>
>   ⚠️ *Corrected 2026-08-18 (previously "Every tool **classified by session
>   type** … An unclassified tool fails the build — that rule is what makes an
>   upstream addition a red build instead of a **security incident**").* The
>   tool-permission policy was removed: it was never a boundary against the
>   caller, who chooses the session directory and reads the profile inside it as
>   the same Windows user — [measured 2026-08-18](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18),
>   which is later than the removal and is recorded that way round.
>   The snapshot was doing the change-detection all along
>   and does it better — a name-keyed classification never saw a schema change.
>   See [the upstream-review gate](#the-upstream-review-gate).

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
| **Unit** | stderr classifier, artifact prefix sort, tool filter and re-describe with **names passed through unchanged**, **`session` routing and the one liveness refusal**, lock signature and PID-recycle logic, config validator | ms | every build |
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

**The most important test in the suite** is mechanical: read the real child's
`tools/list` and assert it is exactly the committed snapshot — every name, in
order, **with every `inputSchema`** — so a tool upstream adds, removes or
re-shapes is a red build that a human has to adjudicate before a release can be
cut. `VerticalSliceTests` also asserts every one of those names gained
BrowserAI's `session` parameter, because a tool that slipped through the rewrite
would be answerable by the run's own child.

⚠️ *Corrected 2026-08-18 (previously "assert **every** tool name carries an
explicit session-type classification. An unclassified tool fails the build. That
turns 'a new upstream tool leaks into interactive mode' from a security incident
into a red build", following the charter's
[Known trade-offs](DECISIONS.md#known-trade-offs)).* The classification and the
`(tool, mode)` permission matrix behind it were removed on 2026-08-18: they were
never a boundary against the caller, who owns the session directory and therefore
the profile inside it. The sentence is kept in corrected form rather than deleted
because "the most important test in the suite" moved, and a reader who learned the
old one needs to be told where.

**The gap between builds, and what actually covers it.** Every build resolves
latest, so every build is already a drift check. What remains is the quiet week:
upstream publishes **daily alphas**, so a week with no commits is a week in which
the tree silently diverges from what was last proven green. There are **no
automated checks of any kind** — no hosted CI, no scheduled job, no git hook
([DECISIONS → Locking, logging, versioning and registration](DECISIONS.md#locking-logging-versioning-and-registration)).

⚠️ **That sentence was false between 2026-08-18 and 2026-08-20 and is true again.**
Hosted CI existed for those two days and was removed at the maintainer's decision;
[Continuous integration](#continuous-integration) below is what is left of it, and
what it cost to remove.

Two things close it, and neither is a scheduled job:

- **[The daily drift check](CLAUDE.md#the-daily-drift-check)** — a directive that
  fires at the start of a working session rather than on a clock.
  ***Corrected 2026-08-18 (previously "It runs by construction, because the check
  happens when the work happens").*** **That is reasoned, not measured, and the
  reasoning does not hold in the case this bullet is answering.** The gap
  [`drift-check.json`](drift-check.json) names is *"a quiet month is a month in
  which upstream can move unobserved"* — and a directive that fires only when
  work happens is, by definition, silent during a quiet month. It closes the
  *working-session* half and nothing else. What is actually established is
  compliance so far: `lastChecked` was stamped on four consecutive days against
  commits on six, on a repository that has never yet had a quiet period. **The
  release checklist below is what really closes the gap**, because it re-resolves
  before anything reaches a user; this bullet catches drift earlier and only
  while somebody is working.
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
  `SpawnRecord` is the record: `.work\spawn-record.txt`, one line per process the
  harness starts, appended by `JobObjectScope.Launch` and `ProbeProcess`, read and
  emptied by the pass before it touches the tree — a live process is what holds
  the files a delete cannot take, so the other order reports a locked file and
  names the wrong cause. ⚠️ *Written 2026-08-19; before that **nothing wrote a
  record**, so this bullet had no input and the pass quietly did nothing while
  reading as though it did.* It terminates the process it named rather than a
  tree, because `Process.Kill(entireProcessTree: true)` is banned repository-wide;
  a grandchild is the job object's business.
  `ProcessLogTests.TheSpawnRecordEndsAPreviousRunsProcessAndSkipsARecycledPid`
  drives all three cases, and the middle one is **this test host's own pid with a
  deliberately wrong creation time** — a reclaim that regressed to matching on the
  number alone would end the run rather than fail it.
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

## How the suite is run: detached, teed, and the log polled

**Settled 2026-08-23. This section owns the invocation; every other file points
at it.** A suite run here is two minutes on a quiet machine and has been over
four on a loaded one, and until this was written the documented way to start one
was to type `dotnet test` and let it stream.

**Never let the run hold the caller's pipe.** `dotnet test` starts MSBuild nodes
and a test host, and the test host starts probes and browsers — and a grandchild
that inherits the stdout handle keeps the pipe open after the command it came
from has exited. The caller then never sees EOF, **so its declared timeout does
not fire**: the call is wedged, and no timeout, no `Stop` hook and no watchdog
can reach it, because a wedged caller never reaches a turn boundary at all. That
is not a hypothetical — it cost a full night's work once, 824 minutes against a
400-second limit. The second half is cheaper and happens more often: **a run
stopped part-way used to take its output with it**, so the evidence of the thing
you stopped it to look at was gone.

Both are answered by the same shape. Redirect the run into a file, detach it, and
poll the file.

**From PowerShell:**

```powershell
$root = (Get-Location).Path
$root = $root.Substring(0, 1).ToUpperInvariant() + $root.Substring(1)   # C:\… — forced
$log  = ".work\suite\ps-$(Get-Date -Format yyyyMMdd-HHmmss).log"
$run  = "`$env:BROWSERAI_DRIVE_CASE='upper'; dotnet test '$root\BrowserAI.slnx' 2>&1 |" +
        " Tee-Object -LiteralPath '$log'; Get-Content .work\suite-coverage.txt | Add-Content -LiteralPath '$log'"
Start-Process pwsh -PassThru -WindowStyle Hidden -WorkingDirectory $root `
    -ArgumentList '-NoProfile','-Command',$run
```

**From Git Bash:**

```bash
root=$(cygpath -m "$PWD")                                              # C:/…
root="$(printf %s "${root:0:1}" | tr 'A-Z' 'a-z')${root:1}"            # c:/… — forced
log=.work/suite/bash-$(date +%Y%m%d-%H%M%S).log
nohup bash -c "BROWSERAI_DRIVE_CASE=lower dotnet test '$root/BrowserAI.slnx' 2>&1 | tee $log
               cat .work/suite-coverage.txt >> $log" >/dev/null 2>&1 </dev/null &
```

Then poll `$log` — `Get-Content -Tail`, `tail -c`, or wait on the summary:

```bash
until grep -q "Test run summary" "$log"; do sleep 5; done; tail -12 "$log"
```

**Four things this shape has to keep, and does:**

- **Each half forces its own drive-letter spelling and declares what it forced**,
  which is the entire point of running both — see
  [the section below](#the-two-spellings-are-forced-and-the-run-says-which-one-it-got).
  A wrapper script shared between the two shells would destroy exactly this and
  would look like a simplification.

  ⚠️ ***Corrected 2026-08-24 (previously "The shell the test host inherits is
  still the shell you started from … `Start-Process pwsh` from PowerShell and
  `bash -c` from Git Bash each pass their own working directory down, so the
  drive letter still arrives `C:\…` from one and `c:\…` from the other —
  verified 2026-08-23, on the six-run gate that shipped this section, by reading
  the spelling back out of each log").*** The verification was real and the
  property was not: it holds run to run rather than by construction. **On the
  2026-08-24 gate all six runs received `C:`** — three of them silently
  duplicating the other three — and the gate reported exactly what a genuine
  two-instrument gate reports; on the very next gate the two shells did differ.
  What was inherited was never the *shell*, it was whatever started the shell.
- **Everything the gate reads is still produced**: the `total` / `failed` /
  `succeeded` / `skipped` block, the coverage block, and the run's file
  artifacts. The coverage block goes to `.work\suite-coverage.txt` and the HTML
  report to `TestResults\` regardless of where the console output went.
- **`Tee-Object` and `tee` both flush as the run goes**, so the log is readable
  while the run is still going and survives the run being killed. Observed
  directly: the log carried the test host's first line 90 seconds before the
  summary arrived.
- **The window is hidden rather than absent**, because a detached run still
  starts a process and [every launch in this tree suppresses its
  console](CLAUDE.md). A run started this way puts nothing on the screen.

**`BROWSERAI_RELEASE_RUN=1` goes on the same invocation** — set it in the
detached shell's own environment, not the caller's, or the variable is not where
the test host will read it. [Release checklist item 8](RELEASING.md#8-run-everything)
is where that matters and what it changes.

**Nothing enforces this.** A test could read this file and check the code fence
still says `nohup`, and that would assert the documentation rather than the
practice; the practice is a habit of whoever types the command, and this section
is the reader it needs.

### The two spellings are forced, and the run says which one it got

**Settled 2026-08-24, at the maintainer's decision, and it replaces a property
that held by luck.** [Continuous integration](#continuous-integration) below says
the gate runs two shells because they hand the test host two different
drive-letter spellings. That was true often enough to be believed and it was
never guaranteed: **all six runs of the 2026-08-24 gate received `C:`**, three of
them silently duplicating the other three, and nothing anywhere said so. The
spelling comes from whatever started the shell, so a harness-started Git Bash and
a human-started one are not the same instrument.

**Measured 2026-08-24 on this machine, which is why `cd` is not the lever.** A
Git Bash that *inherits* its working directory hands a child `c:\…`; the same
shell after **any** `cd` — `/c/…`, `c:/…`, `C:/…`, `c:\…` — hands it `C:\…`,
because MSYS resolves the real path and Windows always answers upper. So the two
invocations above force the spelling somewhere `cd` cannot reach it:

- **`dotnet test` is given an absolute, explicitly-spelled path to the
  solution.** That spelling lands in `MSBuildProjectDirectory`, in `TargetPath`
  and therefore in the test host's own `AppContext.BaseDirectory`, whatever the
  shell's working directory says — measured through
  `dotnet msbuild -getProperty:TargetPath` from both shells, each handed the
  other's spelling, and confirmed end to end by reading the coverage row back out
  of a real run. **MSYS re-spells a command path and a `cd`, and does not touch a
  path passed as an argument**, which is what makes this work from Git Bash at
  all.
- **Each half declares what it forced**, in `BROWSERAI_DRIVE_CASE` — `upper` from
  PowerShell, `lower` from Git Bash — set in the detached shell's own
  environment, exactly as `BROWSERAI_RELEASE_RUN` is and for the same reason.

⚠️ **The declaration is the half that is not optional.** A forced spelling that
silently fails to take is the same trap in a new coat, so
`SuiteCoverageTests.TheRunReportsTheDriveLetterSpellingItActuallyReceived` fails
the run when the spelling it received is not the one this run declared, and
`SuiteEnvironment.Summary()` carries a **`drive letter`** row on every run —
declared or not — naming the spelling, the base directory it was read off, and
whether the forcing took. Unset declares nothing and asserts nothing, which is
what an ordinary developer run has always done; the fault is planted in both
directions by the pure arm beside it, because a machine that declares nothing
cannot plant it live.

**The coverage block reaches `.work\suite-coverage.txt` and does not reach a
`dotnet test` log**, measured 2026-08-24: neither the real stdout handle nor the
real stderr handle survives the MTP integration, which talks to the test app over
a channel of its own. That is why both invocations above append that file to the
run's log as their last act — a six-run gate otherwise keeps one copy of a block
it needs six of.

**This is not `DriveLetterCase` restated.** That type spells every guard path
both ways *inside* a run, including a spelling no Windows API ever returns, so
the class of defect is red from either shell whoever runs it. What is forced here
is the **gate's claim about itself**: that its two halves are two instruments.
Different guarantees, and the first cannot stand in for the second.

### The run says whether it was filtered, and a release may not be

**A filtered run is a CORRECT run.** Every number it prints is true of what it
ran; the false thing is the sentence a human writes underneath it, and no test
can read that sentence. What a test can do is make the run **state the premise**,
so `SuiteEnvironment.Summary()` carries a **`filter`** row on every run, beside
`drive letter` and for the same reason: it is the run's claim about itself rather
than a fact about the machine.

| State | What it means |
|---|---|
| `FULL RUN` | The platform handed the framework no filter, so the run discovered the whole assembly |
| `FILTERED` | The platform handed the framework a filter, quoted in full beside it. Six further lines say the run is **evidence about what it selected and about nothing else**, and name the `\|` trap below |
| `UNREAD` | Nothing read the filter, or the framework had not populated its contexts when it was read. **Never spelled `FULL RUN`** — a null filter read too early is identical to a run that had none, and that false green is worse than no row at all |
| `DISAGREED` | The two contexts TUnit fills from one value carry different values. The instrument is broken, so the run may not be read as filtered *or* as unfiltered, and it fails in **both** modes exactly as `CapabilityState.Partial` does |

⚠️ **It is read from the platform's own `ITestExecutionFilter` and never from
`Environment.GetCommandLineArgs()`.** `TUnitTestFramework.ExecuteRequestAsync`
takes the filter off `ExecuteRequestContext.Request`, stringifies it and gives it
to the context provider, which fills `GlobalContext.TestFilter` and
`TestSessionContext.TestFilter` — so `SuiteFilter` reads the filter the framework
**applied**, whatever route it arrived by, including an IDE's uid-list selection
that never touches a command line. `ICommandLineOptions` was the first choice and
is unreachable from a test: it lives on TUnit's `internal` service provider and is
never registered in the dictionary that provider's own `GetService` reads
([kb](kb/toolchain.md#a-filter-reaches-the-test-hosts-own-command-line-under-dotnet-test--measured-2026-08-24)).

**`BROWSERAI_RELEASE_RUN=1` fails the run when the state is `FILTERED`**, and
when it is `UNREAD` with it — *this run cannot say whether it was filtered* is not
a premise a release may rest on. An ordinary run is never refused for being
filtered, because the iteration loop the rule exists to permit depends on it.

⚠️ ***Corrected 2026-08-24 (previously "**`BROWSERAI_RELEASE_RUN=1` makes
`FILTERED` a failing test**, and `UNREAD` with it").*** The sentence was
unqualified and the mechanism was not: the refusal shipped as an ordinary
`[Test]`, `SuiteCoverageTests.ARunThatWasFilteredIsNeverARelease`, so
`BROWSERAI_RELEASE_RUN=1` together with a filter that did not happen to select
that one method was a filtered run, a claimed release, and **green** — the guard
failing in exactly the class of run it exists to guard. **A refusal a filter can
remove is not a refusal.** It is now raised from
`SuiteCoverage.ReportWhatThisRunExercised`, the `[After(TestSession)]` hook that
writes the coverage block, and the `[Test]` is kept as the in-run echo rather than
as the mechanism.

**What TUnit guarantees about that, measured 2026-08-24 at TUnit `1.65.0` /
`Microsoft.Testing.Platform` `2.3.3` rather than assumed:** a
`[Before(TestSession)]`/`[After(TestSession)]` hook is registered against the
session and not against a test node, so no `--treenode-filter` and no IDE
uid-list selection can deselect it; an exception thrown out of one is reported as
`Test adapter test session failure`, counted in the summary as a failure, and the
host **exits 10**. Re-establish it by running the test executable directly with
`BROWSERAI_RELEASE_RUN=1` and a filter naming any single method, and reading the
exit code — which is exactly what the child control below does.

⚠️ **The guarantee has one limit and it is stated rather than implied.** The
refusal needs a test session to exist. A run that never starts one — a filter
naming no assembly at all, a host that fails before the framework registers its
hooks — reports nothing and is refused by nothing. That run is not a release
either, and nothing in this repository makes it fail.

⚠️ **The positive control is a real child process, and it is not decoration.**
Every run of this suite is unfiltered, so the reading comes back empty on every
one of them — and a reading that can only ever come back empty is
indistinguishable from one that cannot read.
`SuiteCoverageTests.AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease`
starts the test host again with `BROWSERAI_RELEASE_RUN=1` and a filter naming
**one method that is not the refusal** — `AReleaseRunFailsWhereAnOrdinaryRunSkips`,
which passes — and asserts the child read `FILTERED` carrying that exact filter
string, that it exited non-zero anyway, and that its console carries the refusal's
own sentence. Watched red three ways: with the reading blinded the parent printed
`FULL RUN` over a genuinely filtered run and **only this arm caught it**, 11 of
the other 12 staying green; with the refusal still an ordinary `[Test]` the child
reported `total: 1 · failed: 0 · succeeded: 1 · Passed!` and exited 0, against
`Expected to not be equal to 0`; and in that same child console the variable name
`BROWSERAI_RELEASE_RUN` appeared **six times** while the refusal's sentence
appeared **none**, which is why the console assertion names the sentence.

⚠️ ***The console assertion was a tautology until 2026-08-24*** — it read
`console.Contains("BROWSERAI_RELEASE_RUN")` under a comment calling it the
decisive half, and the `FILTERED` row itself ends *"`BROWSERAI_RELEASE_RUN=1`
makes this state a failure."*, printed through the real standard output handle on
every filtered run whether anything refused or not. It now names a sentence
`SuiteFilter.Refusal` is the only producer of.

**The child writes its own coverage block beside its own report and not over the
repository's.** `SuiteCoverage.ReportPath` is repository-rooted for an ordinary
run and derived from `BROWSERAI_FILTER_PROBE` for the probe child; before
2026-08-24 the child wrote its one-test `FILTERED` block over `.work/suite-coverage.txt`
while the parent was still running. The parent rewrote it at its own session end,
so the copy a gate log appends was never wrong — but anyone reading the file
during that window got the child's.

## Provisioning caps: what a duration test may assert here

**Two of the suite's arms drive a cap that is measured in wall-clock time, and
both are written as a RATIO rather than as a duration** —
[the house rule](#every-duration-is-a-hang-detector-or-it-is-a-defect) is why.

- `ProvisioningTests.TheStallCapStopsADownloadThatNeverProgresses` drives a
  double that writes **nothing at all**, so the cap fires on a state rather than
  on a race: no amount of scheduling delay can make an installer that never
  touches the disk look like one that is working.
- `.ASlowInstallThatKeepsWritingIsNotStoppedHoweverLongItTakes` is the other half,
  and it is the arm that is red against a total-time ceiling. ⚠️ **Rewritten
  2026-08-20 onto two seams** *(previously "Sixty writes 25 ms apart against a
  one-second cap: the **total** is about 1.5× the cap and the largest **gap** is
  about a fortieth of it, so everything can stretch by a factor of thirty before
  the two meet")*. The ratio reasoning was sound and insufficient — a ratio
  between two **real** clocks is still a race, and this arm went red once in nine
  consecutive full-suite runs with the product behaving perfectly. It now survives
  **1,000 polls each one tick short of the whole budget**, nearly seven simulated
  days against a ten-minute cap, in milliseconds of wall clock.
- `.TheStallCapFiresOnTheFirstPollAfterTheBudgetPassesWithNoBytes` is the other
  side of the same statement and pins the **exact poll** the detector fires on,
  which no wall-clock test of this could ever have asserted.

⚠️ **Both of the detector's inputs are seams, and one alone would not have been
enough.** `ProvisioningTimers.Clock` is a `TimeProvider` — and the **poll wait**
goes through it too, because a loop whose arithmetic reads an injected clock and
whose sleep reads the wall clock cannot be driven at all. The second seam is
`BrowserProvisioner.WeighBrowsersRoot`, because the detector judges an install on
bytes under the browsers root as well as on time, so a frozen clock beside a real
directory is still half a race. The two together let a test drive the loop in
**lockstep**: the product asks what the root weighs, and answering is where the
test moves the clock. **There is no real duration anywhere in either arm**, so
there is no load under which they behave differently — which is what closed
[the hazard row](HAZARDS.md#hazard-index) that this flake opened on the same day.

⚠️ **What that replaced, kept because the reasoning still applies to every other
double in this suite.** The old arm's installer ran on a `LongRunning` thread
rather than on the pool, and that was a correctness requirement: the product's
watcher polls from a thread of its own and never starves, so a double that ticked
from the pool would starve at unbounded parallelism while the watcher did not, and
the cap would fire on the scheduler rather than on the behaviour under test.
**Observed exactly that way on 2026-08-19: green alone, red in a full run.** The
real installer is a separate OS process and is never pool-bound either, so a
double must be as schedulable as the thing it replaces rather than be given an
advantage.

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
answers at once and reports `provisioning`, every browser tool is refused with the
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

## The run says which question it answered about focus

**Added 2026-08-24.** `JobLauncher` sets `STARTF_USESHOWWINDOW` with
`SW_SHOWNOACTIVATE` so that a headed browser's first window appears without
taking the foreground, and that is measured
([kb](kb/windows/processes.md#sw_shownoactivate-keeps-a-headed-chromium-off-the-foreground-and-firefox-never-takes-it--measured-2026-08-24)).
**No test in this suite can check it, and the reason is the machine rather than
the code.** `SPI_GETFOREGROUNDLOCKTIMEOUT` reads `2147483647` ms here, so Windows
refuses a foreground change in the general case: a focus experiment answers *no
steal* on both arms, and a change that reintroduced stealing would pass here and
fail on somebody else's screen.

**So the run reports what it could have seen, and never implies more.** A
`foreground lock` row sits in the coverage block beside `first-run bytes`, and
carries one of four states with the number it read:

| State | What it means |
|---|---|
| `CAN SEE` | The timeout is zero. The lock never applies, so a browser taking the foreground is visible the moment it happens |
| `IF IDLE` | The timeout expires inside the budget an experiment here may take, so a machine nobody is typing at reaches the moment a steal becomes visible |
| `BLIND` | The timeout outlasts that budget. **This machine.** Three further lines say the run *did not answer* the question and name the exception — a foreground window owned by an ancestor of the launching process — that makes a null trial read as a pass |
| `UNREAD` | Windows refused the call, which is neither of the above and is not reported as either |

**The band edge derives from `TestDefaults.BrowserHang`** rather than being
written at the comparison, for [the same reason every other bound
does](#every-duration-is-a-hang-detector-or-it-is-a-defect): *can this machine
discriminate?* is exactly *can the lock expire inside the time an experiment here
is allowed to take?*, and the only budget in this suite for anything involving a
real browser is that one.

⚠️ **It reports and it never repairs, and both halves are deliberate.** Nothing
calls `SPI_SETFOREGROUNDLOCKTIMEOUT`: the timeout is a machine-wide user
preference, and a suite that wrote to it to make itself informative would be
editing the developer's desktop and invalidating every `[MACHINE]` figure already
recorded against this machine. Nothing here starts a browser or touches the
foreground either — a test that provoked a real steal would put a window over
whoever is at the keyboard.

**It is a row and not a `SuiteCapability`**, and the distinction is the one
`first-run bytes` already draws. Every capability names a command that produces
it, so `BROWSERAI_RELEASE_RUN=1` turning an absence into a failure is actionable.
This one is not: the only thing that would turn it green is changing that
setting, so a capability would make every release from this machine unreachable
with no permitted remedy. `ForegroundLockTests` holds the bands, the boundary
between them and both directions of the warning; `SuiteCoverageTests` holds that
the row reaches the block.

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

⚠️ **Half of this is a mechanism now, and the half that is not is the more
interesting one.** *Added 2026-08-23, after the sweep this rule authorised was
found to have left two of these standing.*
`HouseRuleTests.NoAssertionBoundsAMeasuredDurationWithANumberItInvented` reads
the tree for an assertion that bounds a **measured** duration from above, and
fails if the bound is a number rather than the name of one — `1000`, or a
`TimeSpan.From…` around a literal. It carries a synthetic positive control
rebuilt from the exact assertion deleted that day, because the tree is clean and
a clean tree is indistinguishable from a scan whose needles stopped matching.

**What it cannot see is a promptness claim wearing a named constant**, which by
text alone is the same thing as a hang detector — so the two rows of the table
above are still adjudicated by a reader, and the mechanism only closes the
*"invented its own number"* half. That is worth stating plainly rather than
letting a green build imply the rule is now automatic. What made the survivors
findable at all was that the 2026-08-18 sweep **left its comments behind** where
it deleted each one; the second survivor was found on 2026-08-23 only because the
first had just been fixed and somebody went looking for siblings. Lower bounds
are not examined — load can only make one pass — and neither is the inverse shape
that watches a call *fail* to return (`SessionLockTests.StillBlocked`), which is
sized against the defect rather than against the product and is the one duration
here a starved machine makes safer.

⚠️ **And a third case the two rows above do not cover: a property whose only
witness is the clock.** Added 2026-08-19. `SessionDirectoryGuard` answers *is
this a network path* before the one call in it that opens a directory, and an
**ordering** cannot be observed any other way — the answer is identical either
way, twenty-two seconds apart. There is no bound that satisfies both halves of
the rule: one with headroom a 419-test run cannot reach is far above a single
22-second stall. **So it is not asserted, and the test says so in place** rather
than carrying a number that would eventually go red on a busy machine and be
"fixed" by raising it. What is asserted instead is *which branch produced the
answer*, which is decisive about the branch and silent about the order;
`SessionDirectoryGuardTests.TheNetworkRefusalDoesNotComeFromTheCallThatOpensThings`
names the gap, and [the re-verification index](kb/re-verification.md) carries the
22-second figure as a manual row. **A named gap beats a bound that measures the
machine.**

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
catalogue that has been silently emptied. The saving is **161,280 bytes, 157.5 KiB, 0.9% of the
binary** against a shipped payload of **111,984,018 bytes (106.8 MiB)** — both
halves *measured 2026-08-18* by publishing twice and changing only this property.
*Corrected 2026-08-18 (previously "The saving is measured in kilobytes against a
~117 MB payload")*: the word **measured** was doing work nothing supported —
there was no numerator and the denominator was never weighed. The number came out
where the sentence guessed, and there is no version of this trade that is close.
Assert the property is unset, so it cannot arrive later as somebody's size
optimisation. ✅ **Asserted 2026-08-16 by
`BuildConfigurationTests.UseSystemResourceKeysIsExplicitlyFalseEverywhereItAppears`**,
which had been required and written nowhere for as long as the sentence existed —
found by [release checklist item 7](RELEASING.md) looking for the evidence it asks
for. It refuses any value other than `false` in any build file **and requires the
declaration to be present in `Directory.Build.props`**: the default is already off,
so a file that never mentions it would pass a "not true" check while telling the
next reader nothing.

## The model-facing surface, and the counts documents publish

**Added 2026-08-18.** Two gates that are the same idea in two places: a claim the
repository makes about itself is checked against the thing it claims about.

**Nothing model-facing may exceed the client's silent truncation budget.** Claude
Code's MCP documentation states, verbatim, *"Claude Code truncates tool
descriptions and server instructions at 2KB each. Keep them concise to avoid
truncation, and put critical details near the start."* The truncation is
**positional, not semantic** — it cuts wherever the limit falls, mid-sentence, and
everything after it is never seen. That is the worst failure shape available: the
text exists in source, reads correctly in review, and never arrives.

`ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget` is
the gate, and five things about it are deliberate:

- **It measures off the wire, not off source.** It reads `SliceRun` — the
  published NativeAOT binary, a real `@playwright/mcp` child, real JSON-RPC over
  real pipes. These strings are assembled from concatenated constants,
  interpolated tables and a schema rewrite performed on the child's own nodes, so
  a scan of string literals misses precisely the cases that break.
- **Three surfaces, and the third is the one that was uncovered:** the server
  `instructions`, every tool `description`, and **every parameter `description`
  inside every `inputSchema`**. That last is where BrowserAI's own injected
  `session` description lands on 58 upstream tools at once, and it was asserted by
  nothing at all. *Corrected 2026-08-18 (previously "59") — `browser_annotate` is
  withheld from the surface, so its schema is one fewer place that string lands.*
- **Enumerated dynamically**, so a tool upstream adds next year is covered without
  anybody editing the test. Per-surface floors keep that from becoming vacuous.
- **Characters and UTF-8 bytes measured, failing on characters.** *Corrected
  2026-08-18 (previously "failing on whichever is larger. It is not documented
  which the client counts").* It is now measured: the client counts UTF-16
  characters and never bytes. The byte figure is printed and not gated.
- **Hard at 100%, with no warning tier.** This does not contradict the recorded
  argument against a headroom gate — that argument was against failing *below*
  100%, so that a surface that grew failed on the line about the surface rather
  than on a budget line. Over 100% is a broken state rather than a tight one.
  *(Reworded 2026-08-20, previously "so that a fourth session mode fails on the
  six-consumer line"; session modes and the six-consumer test are gone, and the
  argument was never about modes.)*

It prints every length sorted on a **passing** run, to the run output and to
`.work/description-budget.txt`, because a gate that only speaks when it fails
cannot tell anybody they are forty bytes from silent truncation.

> **The per-string reading is measured, not assumed.** *Corrected 2026-08-18
> (previously "⚠️ The per-string reading is an assumption and the constant says
> so … the experiment commissioned to settle it has its data").* The experiment
> ran @ Claude Code 2.1.234, reading the `tools` array the client sends to the
> Messages API: **per string, 2,048 UTF-16 characters, cut at `> 2048`**, no
> per-tool bucket, no whole-surface total, and parameter descriptions not
> truncated at all. `browserai_init`'s whole entry — 3,360 bytes as the client
> sends it — arrives intact, so the feared casualty was never one. The per-tool
> totals stay **reported and not asserted**, now as the figure a future release
> introducing a per-tool bucket would be judged against. See
> `ClientTruncationBudget` and
> [kb](kb/mcp/protocol.md#what-2kb-each-means--measured-2026-08-18--claude-code-21234).

**Every count a surviving document publishes about this repository is checked
against a live scan.** `ReVerificationIndexTests` had done this for one sentence
since 2026-08-17; on 2026-08-18 four counts in prose were wrong at once — one of
them because a *correction* had replaced a right number with a wrong one by
measuring a different predicate over the same table. `RecordedCountTests`
generalises it, on two rules:

- **The published number and its check derive from one implementation.** Nothing
  re-implements a scan, because a second definition of *what counts as a row* is a
  second answer waiting to happen.
- **A per-category count is asserted category by category**, and the sum against
  the total. A wrong total is only visible against the sum of its parts, which is
  exactly how the earlier error survived.

The sentence in the document is the anchor, so **rewording it fails the build**
rather than silently unhooking the check. Four counts it cannot mechanise are
named in the class, each with the reason — the executed-test count in `README.md`
is a different predicate from any reflection over `[Test]` methods, and the
installer size needs an artifact that `Releases/` gitignores.

### The dated records are append-only

**Added 2026-08-20, because the failure had already happened.** The `lock.json` →
`browserai.json` rename swept the whole tree, reached `docs/reviews/` and
`CHANGELOG.md`'s released sections and rewrote history — a 2026-08-18 review came
out claiming a filename that did not exist for another two days. Nothing failed;
a human reading the diff caught it, and the only thing standing between the next
sweep and the same outcome was a sentence of prose in
[`docs/reviews/README.md`](docs/reviews/README.md). **Prose does not stop a
find-and-replace.**

`AppendOnlyRecordTests` is the mechanism, and it is deliberately **narrow**. Two
kinds of thing are dated records: every `docs/reviews/*.md` **except**
`README.md`, which carries the index and the status table and is *meant* to be
updated; and each **released** `CHANGELOG.md` section, never `[Unreleased]`,
which is not a record of anything until a release stamps it. What is held is the
**prefix** — the characters a record already had, by count and by SHA-256 — so
appending an addendum passes, rewriting a sentence in the middle fails, and
truncating fails with the arithmetic in the message.

**A deliberate edit stays possible and stops being silent.** A typo fix in a
review is legitimate; changing a sealed record means changing its seal in the
same commit, which is a line in the diff — the same trade
[`upstream-review.json`](upstream-review.json) takes, and the same warning
applies: **re-sealing a record to make the test pass is rewriting history with an
extra step.** A second arm requires every dated record in the tree to be sealed
and every seal to still resolve, so the newest review — the one a sweep is most
likely to be run beside — cannot be the one thing nobody registered.

**Why not `git log --numstat`**, which would say for free whether a file has ever
had a line deleted: it fails on both halves. A legitimate typo fix deletes a
line, and the changelog's protection is per-*section* rather than per-file, which
no whole-file history check can express.

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

**There is none, as of 2026-08-20.** `.github/workflows/build.yml` existed from
2026-08-18 and was deleted that day at the maintainer's decision, verbatim:
*"Remove CI completely. Let all the tests run on my machine only. I want no CI and
no github runner."* `.github/` is gone entirely rather than left as an empty husk.
Bringing it back needs self-hosted runner infrastructure that does not exist yet,
and the maintainer is considering leaving GitHub before that happens — so
[the TODO item](TODO.md#continuous-integration) deliberately does not assume
GitHub Actions.

**The whole gate is now the suite, run on the maintainer's machine, from both
shells.** [The release gate](RELEASING.md#the-release-gate) is the only thing
between a change and a shipped release, and there is nothing behind it. Two
consequences a reader has to carry:

- **Run the suite from PowerShell *and* from Git Bash.** Not a preference. The
  drive-letter casing a test host inherits differs between them, and a
  single-shell run bakes in whichever spelling happens to agree — which is exactly
  what CI did, running `pwsh` end to end, and why that defect was reported twice
  from a machine and never once from a build
  ([kb](kb/windows/detection.md#windows-re-spells-a-paths-drive-letter-a-process-never-re-spells-its-own)).
  `DriveLetterCase` is the mechanism that catches it from either shell; running
  both is the belt beside it. ⚠️ **Since 2026-08-24 the difference is *forced*
  rather than inherited, and each half declares what it forced** — *previously
  this bullet's "differs between them" was the whole of it, and it was true run
  to run rather than by construction*. See
  [the two spellings are forced](#the-two-spellings-are-forced-and-the-run-says-which-one-it-got).

  ⚠️ **Once from each shell is the gate for ordinary work; three from each is the
  RELEASE gate.** *Written down 2026-08-24, after the release gate had been
  applied to every intermediate batch — six full runs, at two to four minutes
  each, where two would do.* The repetition exists for the flake that appears
  once in three, which is how [the probe-report race](HAZARDS.md#hazard-index)
  was found on 2026-08-19; nothing about an intermediate batch needs it, and
  [the release checklist](RELEASING.md#8-run-everything) is where it is owed.
- **Nothing builds a contributor's pull request any more.** For a public
  repository that is the real cost of the removal: 54% of this project's
  enforcement is a test or a release-phase check, and a pull request can now break
  any of it with nothing to say so before somebody pulls it and runs the suite.

⚠️ **`BROWSERAI_EXPECTED_ABSENT` still exists and nothing sets it.** *Corrected
2026-08-20 (previously "`build.yml`'s test step sets `BROWSERAI_EXPECTED_ABSENT:
PackagedRelease,ClientCommandLine`", added 2026-08-19).* The workflow was the
variable's only consumer anywhere in the repository, so with CI gone the live arm
`SuiteCoverageTests.EveryAbsentCapabilityIsOneThisRunsEnvironmentDeclared` asserts
nothing on every run — which is *correct rather than broken*: an unset variable
declares nothing, which is what a developer machine has always done. The
reconciliation itself is still held by
`SuiteCoverageTests.TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent`,
which is pure and in-process and unaffected. **The third arm was deleted rather
than re-pointed.** `TheWorkflowStillDeclaresWhatItExpectsToBeAbsent` read
`build.yml`, scoped to the step that ran the suite, and its positive control —
*this really is the step that runs the suite* — is the thing a re-pointed version
could not have: a scan for "any pipeline definition that runs the suite without
declaring the pin" passes over an empty directory, over a typo in its own path and
over a pipeline shape it does not recognise, indistinguishably. Restoring it
against whatever runs the suite next is part of
[the TODO item](TODO.md#continuous-integration).

**What a capability skip means, kept because it did not depend on CI.** *Settled
2026-08-18, because a green build reporting skips reads like a rule being broken
and is not.* [The house rule](CLAUDE.md) — *no skipped, quarantined or
conditionally-ignored test in the tree* — is about the **tree**, and
`HouseRuleTests.NoTestInTheTreeIsSkipped` enforces exactly that: no `[Skip]`
attribute anywhere. A capability skip is a different thing. It is decided at run
time, it names the capability, the path to restore it and the switch that makes it
fatal, and it is reported as **skipped rather than passed** so the run's summary
cannot be mistaken for a healthy one. That is the gate working. **Zero skipped is
a release requirement**: [release checklist item 8](RELEASING.md#the-release-gate)
demands it, and it is met by cutting from a machine that has every capability
present. *Previously this paragraph also recorded that a GitHub runner skipped
exactly 4 — `EveryNoticeIsInsideThePackedRelease` for the missing packed `.nupkg`,
and `TheClientIsLocatedByFileNameAndNeverAsAShim`,
`TheClientStillSaysWhatTheExitCodesCannot` and
`TheRealClientRegistersBrowserAiAtUserScopeAndNothingElseIsTouched` for the missing
`claude.exe`. There is no runner to count on now; the number is kept here because
it is the only record of what a machine without those two capabilities reports.*

**A red `UpstreamReviewTests` means upstream moved and nobody reviewed it.** That
is [the marker gate](#the-upstream-review-gate) working, not a stale file.
Adopting a moved version is what needs the review. Lock-file drift is now reported
by [release checklist item 1](RELEASING.md#1-everything-re-resolved-to-latest-and-green)
alone — with `--exit-code` on both diffs, which is stricter than the `git diff` the
workflow ran into a job summary.

## The release gate

**Lives in [`RELEASING.md`](RELEASING.md#the-release-gate)**, beside the
checklist that enforces it — the six-step sequence of resolve, build, run
everything, green-or-stop, the maintainer decides, cut it. This heading is kept so
every link into it still resolves.

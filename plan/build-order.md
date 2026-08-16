<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

## Build order

The ordered list of what to build. The rest of [the plan](../PLAN.md) is exhaustive
by topic and silent on sequence; this file is the sequence, and it is what drives
the plan's `Status` and `Implemented by` columns.

### Why an ordered list is the first artifact

A sweep of 101 repositories on this machine, 2026-08-16: **no project sustained
more than two weeks of work.** Every one that shipped compressed 40–200 commits
into 2–19 days. The single thing separating the projects that shipped from the
ones that died at five commits was whether **an ordered, decomposed build list
with a per-step done-test existed when the session started.**

Verified 2026-08-16: this repository has **27 tracked files, all documentation,
and zero lines of product code.** That is the state this list exists to move.

### How to use it

- **Start at step 1 with an empty repository.** Steps are ordered by dependency.
  Where the order is not obvious, the reason is stated on the step.
- **A step is done when its done-test passes**, not when the code looks finished.
  Every done-test is something a person or an agent can check and be wrong about.
- **Each step names the plan sections it consumes.** When it lands, mark those
  sections `built` in [the plan's table](../PLAN.md) and record what implements
  them. That is how [the plan gets deleted](../PLAN.md#how-this-plan-ends): a
  folder that empties as the build proceeds.
- **Do not batch steps.** The list is short deliberately so that stopping between
  any two of them leaves the repository in a state the next session can resume.

### Every done-test ends with a clean working tree

`git status --porcelain` must be empty. This is not tidiness.

A repository in this estate holds a working app, a test project and a good CI
workflow that were **never committed**: `git ls-files` returns 29 files and zero
`.cs`. Its checks have therefore never run once, while every surface signal —
the files on disk, the workflow YAML, the green editor — reads healthy. That is
[the founding failure class](../README.md#read-this-before-designing-anything)
applied to a repository instead of a browser, and the check that catches it costs
one command.

---

## Phase 0 — the repository can build, and can prove things

### 1. The build skeleton

**Consumes:** [stack](stack.md) (toolchain rows) · [testing](testing.md)
(framework prohibitions)

> ✅ **Built 2026-08-16.** `global.json` · `Directory.Packages.props` ·
> `Directory.Build.props` · `.editorconfig` · `BrowserAI.slnx` ·
> `src/BrowserAI/{BrowserAI.csproj, app.manifest, Program.cs}` ·
> `tests/BrowserAI.Tests/{BrowserAI.Tests.csproj, RepositoryLayout.cs,
> BuildConfigurationTests.cs}`. Every done-test below was run, including the
> three that require planting a failure and reverting it. Three of this step's
> requirements had prerequisites nobody had measured; see the correction under
> `Directory.Build.props`.

Configuration files only, plus two empty projects. Nothing here is a pin —
**this is where the float is declared**, per the maintainer's versioning rule of
2026-08-16: *always build against latest, never pin; updating everything is the
first step of touching this project, not a step before release.*

- **`global.json`** — an SDK entry whose `rollForward` reaches the newest
  installed major, so a machine that is behind **fails loudly** rather than
  building subtly differently. Plus the runner TUnit requires:
  `{ "test": { "runner": "Microsoft.Testing.Platform" } }` (Microsoft Learn,
  looked up 2026-08-16; MTP mode of `dotnet test`, .NET 10 SDK and later). The
  runner entry is not a version at all. **Look the SDK major up; never type it
  from memory.**
- **`Directory.Packages.props`** — `ManagePackageVersionsCentrally` and
  `CentralPackageTransitivePinningEnabled` both true, every `PackageVersion` at
  `Version="*"`. One file, so no stale number can hide in a project file. It
  carries a comment saying exactly that: **this is the float, not a pin.**
- **`Directory.Build.props`** — shared build settings declared once.
  `LangVersion` `latestMajor`, never `preview`. `TreatWarningsAsErrors`, with
  **CS0162 named explicitly** in `WarningsAsErrors`. `EnforceCodeStyleInBuild`.
  `RestorePackagesWithLockFile`, which is what makes
  [the two-step resolve](../README.md#the-five-rules-that-make-floating-safe)
  possible. `UseSystemResourceKeys` explicitly **false** — it strips exception
  messages, which is this project's enemy wearing a size optimisation.

  > ⚠️ **Corrected 2026-08-16 (previously: CS0162 is named in
  > `WarningsAsErrors` "so a later `NoWarn` cannot quietly demote unreachable
  > code back to a warning").** That mechanism does not work. Measured on SDK
  > 10.0.302 by planting a statement after a `return`: with `NoWarn` set to
  > CS0162 the build reported **0 warnings and 0 errors**, and adding
  > `dotnet_diagnostic.CS0162.severity = error` on top did not change that
  > either, across a forced full rebuild.
  > [`NoWarn` beats both](../kb/windows/processes.md#diagnostic-severity-what-actually-enforces-a-rule-and-what-only-looks-like-it).
  > Naming CS0162 there is still worth doing — it survives
  > `TreatWarningsAsErrors` being turned off — but the protection against bulk
  > suppression had to come from outside the compiler's precedence order, and
  > it is now a test: `BuildConfigurationTests.NoBuildFileSuppressesWarnings`
  > fails on any `NoWarn` or `WarningsNotAsErrors` in a project or shared-props
  > file. **Two further requirements below turned out to have hidden
  > prerequisites, both also measured rather than read:**
  > `Version="*"` under central package management needs
  > `CentralPackageFloatingVersionsEnabled` or restore fails NU1011, and
  > `.editorconfig` at error severity needs `GenerateDocumentationFile` before
  > Roslyn will run IDE0005 on build.
- **`.editorconfig`** at error severity. A severity is never weakened to make
  code pass.
- **`app.manifest`** with `longPathAware=true`. Session directories are the
  caller's choice and nothing constrains their depth.
- **`src/` + `tests/`**, one project each. The app targets the newest GA major
  with the `-windows` suffix — `net<major>.0-windows`, not `net<major>.0` —
  because Velopack's hook callbacks are `[SupportedOSPlatform("windows")]`
  ([§G](G-updates.md)).
- **AOT gated to publish.** `dotnet build` never invokes ILC; `dotnet publish`
  does. Everyday builds stay fast and only a release pays for native
  compilation. Record the **MSVC / `link.exe` prerequisite** in the same file:
  a machine without the Visual C++ toolchain fails at publish and nowhere else,
  and that failure reads as unrelated.
- **ILC output must be empty.** Trim and AOT warnings fail the publish. A
  non-empty ILC report is a silent-failure warrant, not advice.
- **Test project: TUnit only.** No `Microsoft.NET.Test.Sdk` — TUnit is MTP-only
  and mixing the two is unsupported. No FluentAssertions, ever. TUnit's
  analyzers run at **error** severity, because an un-awaited assertion passes
  silently and green-when-broken is the class this project exists to eliminate.

**Done when:**

- `dotnet build` on a clean clone succeeds with zero warnings.
- Adding a statement after a `return` fails the build with **CS0162 as an
  error**, not a warning. Revert it.
- Writing a TUnit assertion without `await` **fails the build**. Revert it.
- `dotnet restore --force-evaluate` writes `packages.lock.json` for both
  projects; a following locked-mode restore succeeds;
  `git diff --exit-code -- "**/packages.lock.json"` is clean.
- `dotnet publish` produces a native binary that runs and exits 0 — which is
  also the proof that `link.exe` is present.
- No `Version=` attribute exists on any `PackageReference`, and no version
  literal exists in any `.csproj`.
- `git status --porcelain` is empty.

### 2. stdout is owned, and nothing else can reach it

**Consumes:** [§E](E-lifecycle.md) (the stdio half)

> ✅ **Built 2026-08-16.** `src/BrowserAI/Protocol/StdioChannel.cs` ·
> `src/BrowserAI/Logging/{ProcessLog,FileLoggerProvider,RollingFileWriter}.cs` ·
> `src/BrowserAI/Hosting/{IAppPaths,LocalAppDataPaths}.cs` ·
> `src/BrowserAI/Interop/NativeFile.cs` · `src/BrowserAI/BannedSymbols.txt` ·
> `tests/BrowserAI.TestProbe/` · `tests/BrowserAI.Tests/{StdioChannelTests,
> ProcessLogTests,Harness/}`.
>
> **One requirement in §E turned out to be unimplementable as written.** *"One
> rolling process log"* shared by ~100 processes loses records under .NET's
> `FileMode.Append` — measured, eight processes lost 70 of 200 lines, silently.
> The sink opens the file with `FILE_APPEND_DATA` instead;
> [kb](../kb/windows/processes.md#interop-and-the-toolchain) has the numbers and
> re-verification row 45 has the regression guard.
>
> **And one is deliberately not built as described.** §E asks the sink to
> *"flush before exiting, on every path including the crash path."* Nothing is
> buffered, so there is nothing to flush and no flush hook exists; a no-op
> `Flush()` would be a mechanism that only looks like one. The property §E wants
> is delivered instead by every record being one unbuffered write, and it is the
> crash test that proves it.

Before any code exists that might reach for `Console.WriteLine`. A rule
retro-fitted to a tree is a rule already broken somewhere in it.

- One wrapper type owns the raw stdin and stdout streams: **UTF-8, LF, no BOM.**
  `Console.Out` writes CP437, any `TextWriter` emits CRLF, and a hand-rolled
  `StreamWriter(stream, Encoding.UTF8)` emits a BOM — all three corrupt JSON-RPC
  on the first non-ASCII byte.
- A banned-symbol check at error severity: nothing anywhere in the process calls
  `Console.WriteLine`, including inside a `catch`. **Reading from the console is
  banned alongside writing to it.**
- Logging: `ILogger`, every line forced to **stderr by configuration, not by
  rule**. Append, never wipe on start. Flush before exiting, including on the
  crash path. No destination that can block or produce a window.
- Two destinations, one seam. The **process log** — startup, the stray sweep,
  provisioning, applying an update — is a single rolling file under the app data
  root, **outside `current\`**, so updates do not wipe it. The **session log**
  lives in the session directory beside `lock.json` and arrives at step 10.
- Both resolve through one injectable app-paths seam. It returns a plain
  `%LocalAppData%\BrowserAI` path until [§G](G-updates.md) swaps in
  `VelopackLocator.Current.RootAppDir` at step 19 — because **every Velopack
  call throws under `dotnet run` and every test host**, and a seam bolted on
  later is the seam that was needed on day one.

**Done when:**

- Adding `Console.WriteLine` anywhere under `src/` fails the build. Revert it.
- A frame containing `é`, a backtick and a newline is written through the
  wrapper and asserted **on bytes**: UTF-8 encoded, `\n` line ending, no BOM.
- Two consecutive runs both appear in the process log — the second did not
  truncate the first.
- A deliberately unhandled exception still leaves its last log line on disk.
- `git status --porcelain` is empty.

---

## Phase 1 — the payload exists, and upstream is watched

### 3. The payload build

**Consumes:** [§A](A-runtime.md) (vendoring and payload)

> ✅ **Built 2026-08-16.** `build/Build-Payload.ps1` ·
> `build/payload/{package.json, package-lock.json}` ·
> `tests/BrowserAI.Tests/PayloadTests.cs` · the payload half of `.gitignore`.
> Resolved: `@playwright/mcp` **0.0.79** (npm `latest`), `playwright-core`
> **1.63.0-alpha-2026-08-05** (that package's own exact dependency),
> `node` **v24.19.0** *Krypton* (newest `nodejs.org/dist/index.json` entry with
> an `lts` field), Chromium **rev 1237 / 152.0.7977.8** from the resolved
> `browsers.json`. Every done-test below was run.
>
> **`.links/` is not where this step said it was.** Measured 2026-08-16 in
> `playwright-core/lib/coreBundle.js`: it is `path.join(registryDirectory,
> '.links')` in **three** call sites and nowhere else, so it is written into the
> **browsers root** and never into `node_modules`. The done-test bullet below
> passes vacuously — an assembled payload has never contained one — and the
> strip belongs to a tree this design does not ship at all, because browsers are
> [provisioned on first run](A-runtime.md#first-run-browser-provisioning). The
> claim was correct for the **bundled** build it was written against and stopped
> applying on 2026-08-14 without being moved, which is the same defect as the
> superseded payload table in [§A](A-runtime.md). The script still asserts the
> payload holds none, because the requirement is about what ships and a future
> upstream could move it. Corrected in [§A](A-runtime.md),
> [the hazard index](hazards.md#hazard-index) and
> [kb](../kb/playwright/provisioning-and-timings.md#first-run-provisioning).
>
> **Node's `LICENSE` cannot be fetched beside `node.exe`.** Measured 2026-08-16:
> `nodejs.org/dist/v24.19.0/win-x64/` publishes `node.exe`, `node.lib`,
> `node_pdb.7z` and `node_pdb.zip` — **no `LICENSE`** — and neither does the
> version root. The only route to it is inside an archive, so the build takes
> `node-v24.19.0-win-x64.zip`, verifies it against `SHASUMS256.txt`, and extracts
> the two entries it needs. That is also **35.58 MiB downloaded instead of
> 88.53 MiB**. This step required shipping the file and said nothing about where
> it comes from; a build that took the obvious route would have shipped no
> licence at all.
>
> **Two things this step deliberately does not do.** The payload is **not** wired
> into `dotnet build` — assembling it costs an `npm install` and a download, and
> the snapshots that must regenerate on every build are step 4's. And the browser
> was **seeded by local copy** from an existing `%LOCALAPPDATA%\ms-playwright`
> holding the identical revisions rather than re-downloaded; the installer still
> ran and still decided. `-SeedBrowsersFrom` is empty by default, so the honest
> default is a download.

Everything after this step needs a real child to test against, and the
[four golden snapshots](testing.md#the-upstream-review-gate) are generated from
this tree.

- Resolve `@playwright/mcp` to the npm **`latest` dist-tag** and install it into
  a staging tree. `playwright-core` follows as that package's own exact
  dependency — never npm `latest`.
- Record the resolved `package-lock.json`. Strip `.links/`: it holds the build
  machine's absolute paths. **(See the correction above: it is written into the
  browsers root, not the staged tree.)**
- Fetch `node.exe` for the newest entry in `nodejs.org/dist/index.json` carrying
  an `lts` field. **Ship Node's full `LICENSE`** — it aggregates OpenSSL, ICU,
  V8, zlib and c-ares terms, and "a single `node.exe`, nothing else" drops it.
  Not optional.
- **Second half, and it is not the product's provisioning subsystem.** Run
  upstream's own installer once to give the test rig a browser:
  `node.exe <staging>\node_modules\@playwright\mcp\cli.js install-browser
  chromium --no-shell --no-progress`, with an **absolute**
  `PLAYWRIGHT_BROWSERS_PATH` — a relative one resolves against `INIT_CWD` first.
  `--no-shell` is load-bearing: `chrome-headless-shell` is never provisioned.
  BrowserAI's own provisioning — the non-blocking `init`, the timers, the error
  text, the reinstall tool — is step 15.

**Done when:**

- `node.exe --version` equals the version the resolver returned.
- `node.exe <staging>\...\cli.js --help` exits 0 and prints a usage block.
- `.links/` is absent from the staged tree; `node\LICENSE` is present.
- The browsers root holds `chromium-<rev>\chrome-win64\chrome.exe` and **no**
  `chromium_headless_shell-<rev>\`. Note the asymmetry — outer directory
  underscores, inner dashes — and build no path that assumes otherwise.
- `package-lock.json` is committed, and a re-run with no upstream movement
  leaves `git diff --exit-code` clean.
- `git status --porcelain` is empty.

### 4. The four snapshots and the marker test

**Consumes:** [testing](testing.md#the-upstream-review-gate) ·
[`UPSTREAM-REVIEW.md`](../UPSTREAM-REVIEW.md)

> ✅ **Built 2026-08-16.** `upstream-snapshots/{tools-list.json, cli-help.txt,
> config-schema.d.ts, browsers.json}` · `build/upstream-snapshots.mjs` ·
> `build/Update-UpstreamSnapshots.ps1` · `build/UpstreamSnapshots.targets`
> (imported by the test project) · `tests/BrowserAI.Tests/{UpstreamSnapshotTests,
> UpstreamReviewTests, ReVerificationIndexTests, ResolvedVersions}.cs` · the
> `upstream-snapshots/** -text` exemption in `.gitattributes`. Every done-test
> below was run, including the two that require breaking something and reverting
> it. 30 tests green.
>
> **The surface is counted, and the bracket below is gone.** 24 default, 69 the
> maximum exposed, 78 in the internal registry, 9 `skillOnly` — all re-measured
> rather than carried over. **BrowserAI's own modes are 42 and 59**, measured
> over the wire: `config` + `vision` + `devtools` is 42 on `headless` and
> `interactive`, plus `storage` is 59 on `persistent`. The mechanism behind it
> was not recorded anywhere: `filteredTools` ors `capability.startsWith("core")`
> with the configured list, so **the base 24 is unconditional and no
> configuration can go below it**, and naming a `core*` capability does nothing.
> The per-capability table and the nine `skillOnly` names are
> [in the kb](../kb/playwright/tools-and-artifacts.md#the-per-capability-breakdown-counted);
> [§C](C-sessions.md) now carries 42/59.
>
> **Eight rows of the re-verification index named tests that have never
> existed** — rows 1, 2, 3, 4, 4a, 4b, 7 and 8 — and
> [testing](testing.md#re-verification-automated-where-it-can-be) asserted they
> already did. They were written from spike code that lived in `.work/`. All
> eight are back to *manual*, each naming the step that owes it, and
> `ReVerificationIndexTests` fails the build on any row naming a test the
> assembly does not carry. Rows 11, 12 and 15 moved the other way: the snapshot
> gate measures them from the resolved payload on every build. The index's own
> marker count is now a test rather than a sentence, which caught its two
> operands being wrong in opposite directions.
>
> **One requirement is deliberately deferred, and the reason is in
> [testing](testing.md#what-the-marker-records).** The marker entry's
> `snapshots` and `reverification` fields cannot be asserted at a baseline:
> nothing has moved, so the only way to satisfy such a test today is to write an
> adjudication of no change plus an outcome for ~40 manual rows, most of which
> name code that does not exist before step 12. That is a review that did not
> happen. Those fields land with the first real bump — the event that creates
> the obligation is also the event the marker test fires on.
>
> **Three toolchain traps were found wiring it, all measured:**
> `$(IntermediateOutputPath)` is empty in a `.targets` imported from the project
> body, so the incrementality stamp escaped `obj\` into the working tree and was
> caught only by `git status --porcelain`; PowerShell 7 colours redirected
> output, which arrives in an MSBuild error as line noise around the diff; and
> `Get-Command git` returns **two** executables on this machine. The snapshot
> directory is also exempt from git's line-ending normalisation, because a byte
> comparison whose two sides pass through different rules is not one — measured
> with `git hash-object --path`, a CRLF file hashes raw under the exemption and
> LF-converted anywhere else in this repository.
>
> ⚠️ **One measurement in this step was wrong and was caught by another.**
> `grep -c $'\r'` reported every line of the four snapshots as containing a CR,
> which is how "npm ships `config.d.ts` and `browsers.json` CRLF" reached four
> documents. Counting CR **bytes** — `tr -cd '\r' < file | wc -c` — returns
> **zero** for all of them: every snapshot is LF. The `-text` exemption is
> therefore a guard against a future upstream change rather than a fix for a
> present one, and it is documented as that. The claim was corrected in
> `.gitattributes`, [kb](../kb/windows/processes.md#interop-and-the-toolchain),
> [row 51](../kb/README.md#re-verification-index) and
> [the hazard index](hazards.md) before this note was written.
>
> **Not wired into `dotnet build` alone:** the gate lives in the test project,
> because [the gate is the suite](../CLAUDE.md#before-changing-upstream-reviewjson--stop-and-read-the-procedure),
> and it is incremental on the payload's files **and** the committed snapshots,
> so a bumped payload or a hand-edited snapshot both re-run it. With no
> `payload/` — a clean clone — it says so at high importance and writes no
> stamp, because step 1's done-test requires `dotnet build` to pass there.

This is the tripwire. It comes before the proxy because a snapshot taken after
the code is written records whatever the code happened to accept.

- Regenerate `tools-list.json`, `cli-help.txt`, `config-schema.d.ts` and
  `browsers.json` from the resolved payload on **every build**, and diff them
  against committed copies. **A diff fails the build with the diff itself in the
  failure message** — not *"someone should look"*, but *"here is precisely what
  moved."*
- The marker test: resolved version equals reviewed version for every upstream
  in [`upstream-review.json`](../upstream-review.json). A red marker is a review
  that has not happened, never a stale file to fix.
- Wire the `Automated by` column of the
  [re-verification index](../kb/README.md#re-verification-index). A row naming a
  test that does not exist yet stays `manual` — **naming a test that does not
  exist is worse than leaving it manual, because it reads as covered.**

**Done when:**

- All four snapshots are committed, regenerated and diffed by the build.
- Changing one byte of any snapshot turns the build red, and the failure message
  contains the diff. Revert it.
- The marker test is green against `upstream-review.json` as it stands, and
  turns red when a reviewed version is hand-lowered. Revert it.
- Every `[FLOATS]` row in the re-verification index names either a test that
  exists or `manual`.
- `git status --porcelain` is empty.

> ✅ **It did, as expected.** [§C](C-sessions.md) stated the exposed surface as
> *"somewhere between 24 and 69, and not the same number in every mode"*,
> because the per-capability breakdown had never been counted. The
> `tools-list.json` snapshot counted it: **42 on `headless` and `interactive`,
> 59 on `persistent`**, and the bracket is gone from §C. The table is
> [in the kb](../kb/playwright/tools-and-artifacts.md#the-per-capability-breakdown-counted),
> where it replaced an `[UNVERIFIED]` entry that said the numbers had never been
> observed.

---

## Phase 2 — the first vertical slice

Three decisions are settled on paper and unexercised
([README → Still open](../README.md#still-open)): the three lock scopes under
real concurrency, `PROC_THREAD_ATTRIBUTE_JOB_LIST` in a published AOT binary,
and the session-index file layout. **Expect at least one to move.** The slice
below drives the second of them within six steps of an empty repository; the
other two arrive at steps 10 and 11.

### 5. The two custom transports

**Consumes:** [stack](stack.md) (deviations 1 and 5)

> ✅ **Built 2026-08-16.** `src/BrowserAI/Protocol/{JsonLines, JsonLinesTransport,
> DirectStdioClientTransport, ChildProcessSession, DirectStdioServerTransport,
> ChildEnvironment}.cs` · `src/BrowserAI/Protocol/StdioChannel.cs` (bytes are now
> the write primitive) · `tests/BrowserAI.TestProbe/Program.cs` (a
> `transport-child` mode) · `tests/BrowserAI.Tests/{ChildEnvironmentTests,
> DirectStdioClientTransportTests, DirectStdioServerTransportTests,
> SdkStdioClientTransportTests, Harness/{ParentProcess, ProbeChild}}.cs`. Every
> done-test below was run. **51 tests green, `dotnet build` 0 warnings**, and the
> AOT publish stayed clean.
>
> **Both decisions under test held, and the second was cheaper than the plan
> said.** The escaping `StreamServerTransport` performs comes from
> `Utf8JsonWriter`'s own encoder rather than from the contract, so the SDK's
> source-generated `JsonTypeInfo` is reused unchanged and only the encoder
> differs — no `JsonSerializerContext` of ours, on any path. Re-measured on
> 2.2.0: the same result frame is **127 bytes through ours and 190 through the
> SDK's**, +49.6%.
>
> **Three things this step found that no document said.**
> `TransportBase.Logger` and `LogTransportSendingMessageSensitive` are
> `private protected`, so a transport written outside the SDK's assembly cannot
> use either and carries its own `ILogger`. `McpJsonUtilities.JsonContext` is
> `internal`; the public route to the message contract is the `GetTypeInfo<T>()`
> extension on `DefaultOptions`. And `StreamServerTransport` does **not** set
> `JsonRpcMessage.Context`, so neither does ours.
>
> ⚠️ **`dotnet test` runs zero tests on this toolchain, and it does so at HEAD
> too.** Measured 2026-08-16 against a clean worktree of `b8a6553`, before any of
> this step's changes: `dotnet test` exits **5** with *"Zero tests ran"* in
> ~250 ms while `BrowserAI.Tests.exe` runs the suite green and `--list-tests`
> enumerates every one. It is the `--server dotnettestcli` handshake, not
> discovery. So **the evidence for every done-test in this file since step 1 came
> from running the test executable**, which is a fact about how the suite is run
> that nothing recorded until now.
> [kb](../kb/windows/processes.md#interop-and-the-toolchain) has the
> characterisation and [TODO.md](../TODO.md) carries the fix.
>
> ⚠️ **The parent-process assertion was wrong, and planting the failure is what
> said so.** As first written it read
> `ParentProcess.IdOf(child.Session.ProcessId)` — the pid the *transport*
> recorded — and with `cmd.exe /c` planted in front of the probe it **passed**,
> because the shell is then the process the transport spawned and BrowserAI
> really is its parent. The assertion this step exists for has to start from the
> pid the **child reports about itself**; it now does, and re-running the plant
> fails on that line rather than on a weaker one further down. This is exactly
> the case the done-test's *"rather than being invisible"* is aimed at, and it
> was invisible in the test written to catch it.
>
> **The environment allowlist is now a list rather than a habit.**
> `ChildEnvironment` names what may pass, what is forced, and — separately —
> what is *refused*, so a later step adding `NODE_OPTIONS` to a child throws
> rather than working. The four `PLAYWRIGHT_DOWNLOAD_HOST` variants were read
> out of the resolved bundle rather than typed from memory.
>
> **Not built, deliberately.** The SDK's server transport also recovers a
> top-level `id` from a frame that fails to parse and answers `-32700`, so the
> caller fails instead of hanging. Ours logs at Error and carries on. That is
> error shaping rather than transport and it lands at
> [step 9](#9-lossless-passthrough) with the error catalogue;
> [TODO.md](../TODO.md) carries it so the boundary is a decision rather than an
> omission.

**Decisions under test:** that the SDK's `StdioClientTransport` must be replaced
rather than configured, and that owning the server transport buys genuinely
byte-exact passthrough.

- **Client transport.** `IClientTransport` is two members. Spawn
  `node.exe cli.js --config <abs path>` **directly** — no `cmd.exe`, no PATH
  resolution, no shell shim. Set `WorkingDirectory` explicitly; left unset,
  .NET passes `null` and the child inherits whatever cwd the client had.
- **Environment allowlist, `Clear()` first.** `psi.Environment` arrives
  pre-populated and assignment *merges*.
- **Stderr captured from before the child starts**, and the exit code cached as
  an `int` immediately — `Process.ExitCode` throws after `Dispose()`.
  `await WaitForExitAsync(ct)`, never `WaitForExit(int)`, which does not drain
  the async readers.
- **Server transport** on the same `TransportBase` pattern, serializing with
  `UnsafeRelaxedJsonEscaping`.

**Done when:**

- A child spawned through the transport has **BrowserAI as its direct parent** —
  asserted via `NtQueryInformationProcess`, so an interposed `cmd.exe` fails the
  test rather than being invisible.
- A result string carrying a backtick, an apostrophe, an angle bracket and a
  non-ASCII character arrives at the test client **byte-identical**.
- The child's environment is exactly the allowlist: `INIT_CWD`, `NODE_OPTIONS`,
  `NODE_PATH`, `DEBUG`, `DEBUG_FILE`, `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE` and the
  four `PLAYWRIGHT_DOWNLOAD_HOST` variants absent; `PLAYWRIGHT_SKIP_BROWSER_GC=1`
  and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` present;
  `PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` absent, because setting it writes
  a line to stderr and trips the error-shaped-stderr detection.
- Killing the child yields an exit code still readable after `Dispose()`.
- Five lines written to the child's stderr before it is given any work all
  survive to the classifier.
- `git status --porcelain` is empty.

### 6. The job object

**Consumes:** [§E](E-lifecycle.md#zero-process-leakage-the-job-object-contract) ·
[§D](D-locking.md#never-by-image-name)

**Decisions under test:** `PROC_THREAD_ATTRIBUTE_JOB_LIST` under `[LibraryImport]`
in a NativeAOT binary, and *never by image name* as a structural rule rather than
a review item.

- `[LibraryImport]`, not `DllImport`. The command line passes as a writable
  `char[]`/`Span<char>` — `CreateProcessW` mutates the buffer, so a `string` is
  not valid, and `LibraryImport` does not support `StringBuilder`. Keep
  `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` and `STARTUPINFOEX` blittable.
- `KILL_ON_JOB_CLOSE` **and nothing else**. Handle **non-inheritable** —
  redirecting stdio forces `bInheritHandles=TRUE`, so this is one flag away at
  all times, and getting it wrong was measured fatal. Assign at creation via
  `PROC_THREAD_ATTRIBUTE_JOB_LIST` + `EXTENDED_STARTUPINFO_PRESENT`, never
  `Process.Start` then `AssignProcessToJobObject`. One job per instance. Every
  return value checked and failing loudly.
- The forbidden-API analyzer at **error** severity:
  `Process.GetProcessesByName`, `taskkill /IM`, name-filtered WMI or toolhelp.
- `JobObjectScope` for the test harness, so a failed assertion cannot leak a
  browser.
- `.work/jobtest/` is the working prototype **of the acceptance test**, not of
  the product code. Port it as a test.

**Done when:**

- The flags test passes on what is actually set: `KILL_ON_JOB_CLOSE` present;
  `BREAKAWAY_OK`, `SILENT_BREAKAWAY_OK` and `JobObjectBasicUIRestrictions`
  absent; handle non-inheritable; assignment through the attribute list.
- The acceptance test passes against a real `node` child and its descendants:
  every PID reports `IsProcessInJob`, the launcher is `TerminateProcess`d from
  outside, every PID is gone. Cross-check the job's PID list against a toolhelp
  walk seeded from an I/O completion port on the job, so a process whose parent
  already exited is not missed.
- Adding `Process.GetProcessesByName` anywhere fails the build. Revert it.
- `git status --porcelain` is empty.

> The **"16 runs, 106 processes, 0 escapees, 0 survivors"** half of this contract
> was measured against real Chromium and Firefox trees. There is no browser under
> BrowserAI's control until step 15, so re-run the acceptance test there against
> both browsers. Step 6 proves the flags and the ownership; step 15 proves the
> containment at scale.

### 7. Vertical slice: a published AOT binary proxies a real child

**Consumes:** [§B](B-mcp-server.md) · [stack](stack.md) · [§A](A-runtime.md)
(the launch half) · [§E](E-lifecycle.md)

Deliberately thin: no sessions, no locking, no artifact routing, no injected
`session` parameter. `tools/list` and `tools/call` forwarded verbatim. Its job
is to make the risky decisions real.

**Decisions under test:** NativeAOT with the SDK and our P/Invoke in one binary ·
the protocol-version split · `browserName` and an explicit chromium-alias channel
being mandatory · `--sandbox` as a CLI flag and never a config key · the
hand-written raw-protocol test client.

- `McpServerOptions.ProtocolVersion = null` upward; `McpClientOptions.
  ProtocolVersion` pinned to the child's ceiling downward. **The second is not
  optional** — unpinned, the client probes with `server/discover` and pays
  `DiscoverProbeTimeout` per spawn against a ~300 ms baseline, presenting as
  *"browser automation got slow"* with no error anywhere. **Assert on the
  negotiated version**: the child never rejects one, it caps or echoes silently.
- Minimum config generation only, and exactly the parts that decide *which
  browser runs*: `browserName` explicit, an explicit chromium-alias channel
  (never `chrome`, never absent), an absolute `PLAYWRIGHT_BROWSERS_PATH`, and
  `--sandbox` **on the command line**.
- A **hand-written raw-protocol test client** — no SDK client between the
  assertion and the wire.
- Run the slice from the **published** binary, not from `dotnet run`.

**Done when:**

- `dotnet publish` with `PublishAot`, win-x64, self-contained emits **zero
  trim/AOT warnings**. Introducing `JsonArray.Add(x)` without the `(JsonNode)`
  cast turns it red — that is the one AOT trap the spike found, and it is in our
  code, not the SDK's. Revert it.
- Driven by the raw-protocol client against the published binary: `initialize`
  negotiates the expected version; `tools/list` returns the child's tools with
  **names byte-for-byte upstream's**; `browser_navigate` to
  `data:text/html,<h1>ok</h1>` returns a result whose `isError` is not `true`
  and whose text contains `Page URL: data:text/html`. Use `data:`, never
  `about:blank`, which succeeds too trivially and has an empty snapshot.
- The resolved `executablePath` is **our** Chromium, and **an empty browsers
  directory fails this test.** Without that assertion the entire
  batteries-included premise can be silently dead code with the suite green.
- `--no-sandbox` is absent from the child's **resolved browser command line** —
  the config key reads fine and has no effect, so only the command line proves
  it.
- The child is in a job created at process creation; `TerminateProcess` on the
  published binary from outside leaves no `node.exe` and no browser.
- `git status --porcelain` is empty.

---

## Phase 3 — the proxy is lossless

### 8. The harness and the fake child

**Consumes:** [testing](testing.md#we-write-our-own-harness)

`McpTestHarness` (**two** pipe pairs, not one — a proxy needs two hops),
`FakePlaywrightChild`, `TUnitLoggerProvider`, `CapturingLoggerProvider`,
`TestDefaults`, and `JobObjectScope` from step 6. ~100–200 lines. The SDK's own
fixtures are **not vendored**: 1,082 lines, unpublished to NuGet, one pipe pair,
and a permanent three-way merge against an upstream that edits `tests/` weekly.

- **Pin `DiscoverProbeTimeout` in `TestDefaults`**, and answer `server/discover`
  with `-32601` in the fake child. A double that ignores it costs the full probe
  timeout per connect; the spike burned 30 s per rig on exactly this.
- **Disposal order is load-bearing:** cancel the token → complete *both* pipe
  writers → await the server task → dispose the provider. Any other order hangs
  or throws.

**Done when:**

- A test drives test client → BrowserAI → fake child and back with no `Process`
  and no Node, in milliseconds, in parallel.
- The fake child demonstrably supports each capability it exists for, one test
  each: a canned `tools/list`, a programmable result, an injected error, a
  delay, death mid-call, an unknown content type, an oversized payload.
- No test leaves a live pipe, a live job or a live process behind — asserted by
  the rig, not by inspection.
- `git status --porcelain` is empty.

### 9. Lossless passthrough

**Consumes:** [stack](stack.md) (deviations 2, 3, 4, 6, 7, 8, 9) ·
[testing](testing.md) (the fake-child layer)

Every failure this step closes is silent. That is why it is its own step rather
than a detail of step 7.

- Raw `ListToolsAsync(ListToolsRequestParams, ct)`. The convenience overload
  **silently drops** tools whose `x-mcp-header` annotations fail SEP-2243
  validation.
- `tools/list` rewritten on **`JsonNode`**, order-stable. A typed
  `ListToolsResult` round-trip discards unknown tool-level members, because
  `Tool` carries no `[JsonExtensionData]`.
- `tools/call` proxied through `McpServerOptions.Filters.Message.IncomingFilters`,
  **short-circuiting rather than calling `next`** — not `WithMessageFilters`,
  which is a DI extension in the hosting package.
- **Cancellation hand-rolled.** The SDK emits nothing downstream, on the raw and
  typed paths alike. Assign `JsonRpcRequest.Id` yourself and send
  `notifications/cancelled` from your own `ct.Register`.
- Progress relay through an `ITransport` decorator — `McpClientOptions` has no
  `Filters`, so there is no server-side seam for the child→caller direction.
- Strip the `"Request failed (remote): "` prefix the SDK adds to JSON-RPC error
  messages, and reconstruct `data` from `Exception.Data`.

**Done when:**

- `tools/call` results pass through **byte-identical**, including image and
  binary payloads — asserted on the exact byte span of `result` via
  `Utf8JsonReader` token offsets, **never** by re-serialising and comparing.
- An unknown content **type** and an unknown **property** both survive the trip.
- `isError: true` bodies are preserved verbatim; nested `error.data` is not
  flattened.
- Cancelling a call produces a `notifications/cancelled` **observed at the fake
  child**, not merely a local abort.
- A child progress notification reaches the caller under the **caller's** token.
- Child death mid-call, a full stderr pipe and an oversized payload each produce
  a defined error rather than a hang.
- `tools/list` order is unchanged by the rewrite.
- `git status --porcelain` is empty.

---

## Phase 4 — sessions

### 10. The session directory, `lock.json`, and the three lock scopes

**Consumes:** [§C](C-sessions.md#the-session-directory-is-the-identity) ·
[§D](D-locking.md)

**Decision under test:** the three lock scopes under real concurrency — named in
[README → Still open](../README.md#still-open) as settled on paper and
unexercised.

- **One canonicalisation function**, used by the mutex name, the lock and the
  index alike, and tested to agree with itself: `Path.GetFullPath` →
  `TrimEnd('\')` → `ToUpperInvariant()` → SHA-256 → hex →
  `Global\BrowserAI-{hash[..32]}`. A path cannot go in a mutex name; backslashes
  are illegal after `Global\`.
- **`Global\` only. There is no `Local\` fallback.** If the machine-wide lock
  cannot be created there is no lock and therefore no session — a hard blocker,
  communicated to the calling LLM with the reason. A silently weaker lock is the
  exact failure class this project exists to eliminate.
- **Acquisition never waits.** Zero-timeout attempt; on contention, return
  immediately with an error naming the holder and when it was taken. **Whether
  to retry is the calling LLM's decision, not a timer inside BrowserAI.** The
  one exception is the internal mutex held for milliseconds around index writes,
  which keeps its short bounded wait.
- `lock.json` held `FileAccess.ReadWrite, FileShare.Read` — a second BrowserAI
  requesting write fails, while any reader can still say who holds it and why.
  Required schema version. Holder record keyed on `(pid, creationFileTime)`.
  Durable writes (temp + rename); a reader that catches a torn write retries
  once.
- **Reject unknown keys** in our own files, and make the recovery differ from the
  failed call. ISO 8601, invariant, everywhere we write a date.

**Done when:**

- Three spellings of one directory — trailing separator, different case, a
  relative form — produce one lock name, one file path and one index key.
- **Under N concurrent processes racing one directory, exactly one acquires** and
  every other fails immediately, each error naming the holder and the time.
  Measured across processes, not asserted against a single-threaded stub.
- A holder killed with `TerminateProcess` leaves a `lock.json` that a second
  process reads and reclaims, reporting *"held by PID … since …, no longer
  running"* — [error row 9](H-model-surface.md#h4-the-error-catalogue), which is
  not an error.
- A `lock.json` with an unknown key is refused, and the refusal names a recovery
  that is not the call that just failed.
- An `AbandonedMutexException` leaves the mutex **acquired** and the code
  proceeds (**R3**) — unhandled, this disables locking permanently after the
  first crash and nothing reports it.
- `git status --porcelain` is empty.

### 11. The session index

**Consumes:** [§D](D-locking.md#the-session-index-on-disk)

**Decision under test:** the session-index file layout — the third of the three
unexercised items.

`%LOCALAPPDATA%\BrowserAI\index\<sha256-of-canonical-path>`, one file per session
directory, holding that path and nothing else. Written idempotently on every
`init` **and** every `resume`; self-cleaning on sweep; **never trusted, only
followed**; no lock, because create and delete are atomic per file and a
wrongly-deleted entry is restored by the next use.

**Done when:**

- `init` then `resume` on one directory leaves exactly one index file; deleting
  it by hand and resuming restores it.
- An entry pointing at a deleted directory, and one pointing at a directory with
  no readable `lock.json`, are both removed on the next sweep.
- An entry pointing at a personal Chrome profile is followed, found to have no
  `lock.json`, and produces **no action** — the index is an inventory, never an
  authorisation.
- Two processes writing the same entry concurrently leave one valid file.
- `git status --porcelain` is empty.

### 12. The session tools and config generation

**Consumes:** [§C](C-sessions.md#the-init-contract) ·
[§H.2](H-model-surface.md#h2-the-authored-tools)

Five of the six authored tools. `browserai_reinstall_browser` waits for step 15,
because it exists to re-provision something that does not yet exist.

- `browserai_init` — **required** directory, `purpose` and `mode`; optional
  `browser`, `tracing`, `consoleLevel`, and **debug logging as an optional
  argument** (which is also true of `resume`). No default directory and no
  fallback: an empty, relative, malformed or unusable path is rejected outright,
  never normalised into something that happens to work.
- `init` **refuses a directory that already has a session**, including a cleanly
  closed one, and directs the caller to `resume`. Being made to say "resume" is
  the point.
- **Move versus copy on `resume`.** The directory is the identity; the path in
  `lock.json` is provenance. Recorded path gone → it was moved: repair the
  record, log it, carry on. Recorded path still exists → it was copied: refuse,
  and require explicit acknowledgement. No extra fingerprint field — the
  recorded path is already the discriminator.
- **Free-space check at `init`, O(1) volume query only.** Provisioning peaks near
  640 MiB. A directory walk here would make the check slower than the failure it
  prevents, and `init` is on the hot path of every session.
- `browserai_resume`, `browserai_list` (required directory, path-prefix match,
  size on disk), `browserai_destroy` (refuses anything without a valid
  `lock.json`, and **survives a locked file**), `browserai_set_purpose`.
- The **full config generator**, and the `browser_get_config` round-trip asserting
  every generated opinion survived into the child. `loadConfig` is a bare
  `JSON.parse` with no schema validation, so a key we set that does not come back
  is a red build rather than a mystery in production.
- The `session` parameter injected into every tool's raw `inputSchema`,
  order-stable, on the `JsonNode`.

**Done when:**

- All five tools round-trip through the raw-protocol client.
- `init` on a directory holding a `lock.json` produces
  [error row 4](H-model-surface.md#h4-the-error-catalogue), naming the existing
  purpose, mode and date.
- A session directory **moved** on disk resumes with a repaired record and a log
  line; a **copied** one is refused.
- `browser_get_config` returns every key BrowserAI generated. Deleting one key
  from the generator turns the test red. Revert it.
- Every tool in the child's `tools/list` gains `session`, and the list order is
  unchanged.
- `destroy` refuses `%USERPROFILE%\Documents`; `destroy` on a session with one
  file held open still completes and reports what it could not remove.
- An absent, empty, relative or malformed directory is rejected by both `init`
  and `resume`.
- `git status --porcelain` is empty.

### 13. The one table, enforcement, and the model-facing surface

**Consumes:** [§H](H-model-surface.md) ·
[§C](C-sessions.md#three-modes-and-tracing-as-a-modifier)

Six consumers, one source: server `instructions`, `init`'s description,
`resume`'s result, refusal text, session-type enforcement, and the tests.

- Session-type enforcement centralised in **exactly one place, deny-by-default**,
  and **identical in debug and release builds**. An unclassified tool fails the
  build. `browser_run_code_unsafe` hidden in `interactive`; `browser_annotate`
  `interactive` only; `browser_get_config` redacted or refused, because its
  handler is `JSON.stringify(context.config)` with no filtering.
- The handle→type lookup is shared mutable state on the hot path of every call.
  **Test it under concurrency**; a lookup race here is an enforcement bypass.
- The error catalogue, every row emitted by a real triggering condition.
  `purpose` capped, control-characters stripped, and framed as recorded data —
  it is a channel between agents and is replayed into another model's context.
- **Descriptions are append-only, and the check runs both ways.** Ours breaking
  theirs is a test here: assert the phrases a model relies on survive our
  rewrite. Theirs breaking ours is [a pre-release
  adjudication](pre-release.md) — the `tools-list.json` snapshot from step 4
  already carries descriptions, so a rewording is already a diff; what is owed is
  the judgement, not a second snapshot.

**Done when:**

- Adding a fourth mode to the table and to nothing else leaves the build red
  until all six consumers render it; removing it from one consumer turns it red
  again. Revert it.
- Every tool in the real child's `tools/list` carries an explicit classification.
  Adding an unclassified tool to the fake child's list fails the build.
- A storage tool on a `headless` session is refused with text **naming
  `persistent`**, derived from the table rather than written by hand.
- The `instructions` string and every tool description are measured **in bytes**
  and fail over 2 KB.
- Every error row is produced by triggering it — a string no code path emits
  fails the test, because that is documentation rather than behaviour.
- `purpose` round-trips through `lock.json` with control characters stripped and
  its length capped.
- `git status --porcelain` is empty.

> **Expect this step to invalidate the measured instructions size.**
> [§H.3](H-model-surface.md#h3-the-server-instructions-string) states
> ~1,050 bytes of a 2,048-byte budget. The build measures it; record what it
> actually is.

### 14. Artifact routing

**Consumes:** [§F](F-artifacts.md)

Route on the way in; do not sort on the way out.

- The child's `WorkingDirectory` **is** the instance output root, which makes the
  stray-file failure impossible rather than caught.
- `filename` normalised inbound into the typed subfolder its **generator prefix**
  implies — never by date, which cannot tell a hand-named file from a generated
  one. Eight prefixes under `output\`, spelled exactly as upstream spells them;
  `download` is the ninth and sits at the session root, because a
  browser-initiated download lands where the browser puts it. `traces\` is ours
  by choice and is **not** a prefix.
- Both path forms returned on the way out. Levers 2 and 3 ship together or
  neither ships: relocating a file while telling the model otherwise is a new
  silent failure introduced by the fix for an old one.
- `session.json` per session, one entry per routed artifact. Roll-up scoped to
  the **output root**, never the machine.

**Done when:**

- All nine prefixes route to the right folder, and a hand-named file is never
  swept into a machine folder.
- `..\..\foo.png`, `C:\foo.png`, `C:foo.png`, `\\server\share\foo.png` and
  `\foo.png` are each **refused with a readable error** — none normalised into a
  path that happens to land somewhere.
- Two artifacts named `login.png` produce two files, the second suffixed, and the
  result says so.
- Every result carries the absolute and the session-relative path, and the file
  is at the absolute one.
- Cumulative session size is reported in the result.
- `session.json` has one entry per routed artifact, and the roll-up covers only
  the root in play.
- `git status --porcelain` is empty.

---

## Phase 5 — the machine

### 15. First-run provisioning, and `browserai_reinstall_browser`

**Consumes:** [§A](A-runtime.md#first-run-browser-provisioning) ·
[§H.2](H-model-surface.md#h2-the-authored-tools) (the sixth tool)

> **Maintainer decision, 2026-08-16: provision for real.** Every step that needs
> a browser downloads it into `%LocalAppData%\BrowserAI\browsers\` rather than
> reusing the `%LOCALAPPDATA%\ms-playwright` copy that spike work left on this
> machine. The alternative — point `PLAYWRIGHT_BROWSERS_PATH` at the existing
> tree and only exercise a download once, here — was offered and declined.
>
> What that buys is the thing a seeded copy cannot: **the download path is
> exercised by construction rather than at one step**, so a broken CDN URL, a
> moved revision or a failed integrity check surfaces at the step that caused it.
> Step 3 seeded from a local copy via its `-SeedBrowsersFrom` parameter; under
> this decision that parameter stays available for a re-run but is not the
> default, and step 15's done-test still requires an empty-root run regardless.

- Browsers at `%LocalAppData%\BrowserAI\browsers\`, resolved through the
  app-paths seam and **never inside `current\`**, or every update re-downloads.
- **`init` must not block.** Return immediately with
  `browserProvisioning: "downloading"`; browser-needing calls return
  [error row 6](H-model-surface.md#h4-the-error-catalogue);
  `browser_get_config` still works. In-session recovery is proven — the same
  child navigates once the install lands, with no restart.
- **Strip upstream's remediation string.** It says
  `Run npx @playwright/mcp install-browser chromium`, which BrowserAI does not
  ship and which resolves a different package at a different revision. A model
  will act on it. Replace it with ours.
- Timers: stall 30 s (Playwright's own default — leave it), absolute cap 45 min,
  extraction cap 10 min, outer deadline 60 min as a crash tripwire.
- `browserai_reinstall_browser` **refuses rather than coordinates**: takes a
  machine-wide mutex, refuses if any session anywhere has a live browser and
  names what is live, and only then deletes and re-provisions. No force flag —
  force here means terminating browsers other sessions are using.
- **Re-run the [§E](E-lifecycle.md#zero-process-leakage-the-job-object-contract)
  acceptance test here against real Chromium and Firefox trees.** Step 6 could
  only reach `node`.

**Done when:**

- With an empty browsers root, `init` returns immediately and a browser-needing
  call returns error row 6 rather than hanging; the same child navigates
  successfully once the install lands.
- The smoke layer **fails** with an empty browsers directory.
- The resolved `executablePath` is ours; the resolved `user-data-dir` is exactly
  what we passed — catching both a `UserDataDir` policy hijack and a silent
  fallback to a default profile.
- The launched browser is **not registered for restart**:
  `GetApplicationRestartSettings` returns `0x80070490` (`ERROR_NOT_FOUND`).
- `reinstall_browser` refuses while a browser is live and names it; it succeeds
  when nothing is running.
- The job-object acceptance test passes against Chromium **and** Firefox: every
  descendant in the job, zero survivors after an external kill, and **every
  profile directory deletes cleanly** — a directory still holding a lock proves
  an escaped browser.
- `git status --porcelain` is empty.

> **Expect this step to put a `[FLOATS]` fact in play.** The 203.8 MB down /
> 433 MiB on disk figures were measured 2026-08-15 against the revision in that
> day's `browsers.json`. If the revision moved, **re-measure — never adjust the
> number by reasoning.**

### 16. The stray sweep

**Consumes:** [§C](C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive)

Designed for ~100 concurrent BrowserAI processes, not for one.

- **Detection decides and is fully documented**: `EnumProcesses` →
  `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` →
  `QueryFullProcessImageNameW`, keeping any process whose full image path is the
  binary BrowserAI provisioned.
- **Attribution may fail and must fail safe.** `GetWindowTextW`, falling back to
  `InternalGetWindowText`. If attribution fails, **refuse to kill and report
  loudly** — the undocumented path can only ever cause us to decline to act and
  say so.
- **A candidate is a stray only when both guards agree**: our image path, **and**
  an attributed directory holding our `lock.json` whose lock we can acquire
  ourselves.
- **Reject any title that is not a rooted local drive-letter path before touching
  the filesystem.** A `\\host\share` title makes `File.Exists` block for 21
  seconds — the sweep's single largest availability risk, closed by a string
  check.
- Check `GetLastError() == 1400` and restart the walk; normal exhaustion returns
  error 0, so an unchecked walk under-reports exactly when browsers are exiting.
- `Global\BrowserAI-Sweep` with `WaitOne(0)` — try-acquire-and-skip, never queue.
  Background thread, fire-and-forget, never awaited, never a startup gate, never
  `stdout`. A catch-all at the thread boundary.
- The **logon scheduled task** is built here and **registered at step 19** by the
  Velopack install hook — the registration is verified to work non-elevated, but
  there is no install to hook until §G lands.
- **Reclaim a leaked system resource before each test run**: a rig that inherits
  an abandoned `Global\BrowserAI-Sweep` from a previous run tests nothing.

**Done when:**

- Every row **R1–R12** in
  [the race table](C-sessions.md#race-conditions-and-what-closes-each) has a test
  and all of them pass.
- The cross-process window read still bypasses `WM_GETTEXT`: spawn a child whose
  message-only window suppresses `WM_GETTEXT` and is named with a known GUID, and
  read the GUID anyway. No browser, milliseconds, every build.
- `GetWindowTextW` and `InternalGetWindowText` agree on every window enumerated,
  and `EnumWindows` returns **zero** `Chrome_MessageWindow`s — so nobody later
  "simplifies" the class-qualified walk into a loop that silently finds nothing.
- A UNC title is rejected **and the test asserts elapsed time**, because the
  failure it prevents is a 21-second stall, not a wrong answer.
- A candidate whose directory cannot be attributed is reported and **not**
  terminated.
- The sweeper finds a browser it launched itself, running in the interactive
  session — the session-0 blindness guard.
- The sweep never writes to `stdout`, asserted rather than assumed.
- `git status --porcelain` is empty.

### 17. Firefox

**Consumes:** [§D](D-locking.md#firefox-the-preflight-and-a-second-detection-path)

- **The `parent.lock` preflight is mandatory, not defence in depth.** Open
  `<profile>\parent.lock` for write before launching; on
  `ERROR_SHARING_VIOLATION`, refuse with
  [error row 11](H-model-surface.md#h4-the-error-catalogue). Taking our own lock
  first already makes the collision unreachable — but that is coverage by
  ordering, and ordering survives a refactor unnoticed.
- Attribution: `RmStartSession` → `RmRegisterResources(parent.lock)` →
  `RmGetList`, using `ProcessStartTime` as the PID-reuse guard. Detection by
  image path already covers Firefox for free. Mozilla's own
  `ProfileUnlockerWin::TryToTerminate` does exactly this and is worth copying
  line for line.
- `firefoxUserPrefs: { "toolkit.winRegisterApplicationRestart": false }` on
  **every** launch — the one place resurrection is prevented outright rather
  than cleaned up after.

**Done when:**

- With the profile lock held, the preflight refuses, **no Firefox process
  starts, and no window appears.** Assert on elapsed time: the failure this
  prevents is a three-minute desktop modal on a background server.
- `RmGetList` attributes a Firefox we launched to its profile, and a foreign
  Firefox is attributed to none of our sessions.
- A launched Firefox has restart registration off.
- `git status --porcelain` is empty.

---

## Phase 6 — shipping

### 18. Versions from git tags, and the changelog

**Consumes:** the maintainer's decision record, 2026-08-16 — *Version numbering*
and item 40

- **Versions are derived from git tags**, three parts plus a pre-release suffix.
  Tag `1.2.0` and that build is `1.2.0`; an untagged build five commits later is
  `1.2.1-alpha.0.5`. Auto-incrementing, nothing hand-edited. The house four-part
  `base.commitcount` convention **cannot be carried** — `vpk` rejects four-part
  versions outright.
- This also removes any need for a magic development-build number: an untagged
  build already carries the not-a-release suffix, so the rule is simply *never
  self-update from a build that is not a release.*
- **Reject `0.0.0`.** It means the derivation found no tag — a build that does
  not know what it is.
- **Create `CHANGELOG.md`.** Verified 2026-08-16: there is none in the
  repository. [Pre-release](pre-release.md) refuses to release on an empty
  unreleased section, and a checklist cannot enforce a file that does not exist.

**Done when:**

- A tagged build stamps the tag; an untagged build five commits later stamps a
  three-part version with a pre-release suffix; neither appears hand-typed
  anywhere in the tree.
- A build resolving `0.0.0` fails with a message that says why.
- `CHANGELOG.md` exists with an `Unreleased` section, and a release attempt with
  that section empty is refused.
- `git status --porcelain` is empty.

### 19. Velopack: package, update, roll back

**Consumes:** [§G](G-updates.md)

- Per-user to `%LocalAppData%`, never `--msi PerMachine` — a UAC prompt cannot be
  answered by a background MCP server.
- `SetAutoApplyOnStartup(false)` — the default is `true`, and it makes BrowserAI
  exit(0) at handshake time and relaunch with dead pipes.
- Register `current\BrowserAI.exe` **directly**, never the execution stub, which
  is `windows_subsystem = "windows"` and returns in tens of milliseconds.
- `UpdateOptions.ExplicitChannel`, **never the channel in the feed URL** — the
  worst hazard in §G, because it is unrecoverable in the field: a client that
  cannot reach the feed cannot be told to roll back either.
- Swap the app-paths seam from step 2 onto `VelopackLocator.Current.RootAppDir`.
  Never `AppContext.BaseDirectory` — it resolves *inside* `current\`, which is
  wholly replaced on update.
- One `SetLogger()`; `AllowVersionDowngrade` on **and** a release-validation rule
  reading *"monotonic **or** an explicit rollback republish"*. Both halves or
  neither works.
- Archive every full `.nupkg` yourself; Velopack prunes `packages\` and deltas
  are forward-only.
- Gate apply on *am I the last instance* using the step-10 directory lock, then
  `Update.exe apply --silent --norestart --waitPid <ownPid>` and exit.
- **Never re-run `Setup.exe` over an existing install** — it renames the root
  aside and deletes it, taking the provisioned browsers with it. **Never pass
  `-- <args>` to it** — it panics and never exits.
- The install hook registers the step-16 logon task; the uninstall hook removes
  it.
- Three download timers — absolute, stall, and an outer deadline as a crash
  tripwire — with the download off the message loop, because a `tools/call` has
  to stay answerable while a package is in flight.

**Done when:**

- The real production feed URL resolves over HTTP and returns a manifest. A
  local-directory source composes paths differently and will pass where
  production 404s, so this assertion cannot be made hermetically.
- `vpk pack` emits a **delta** package for N→N+1. Nothing in-house has ever
  produced one, so this is proof rather than confirmation.
- N→N+1 applies and the installed version moves; a rollback under
  `AllowVersionDowngrade` applies and the version moves back.
- **The browsers beside `current\` survive both**, and no re-download occurs.
- The process log under the app-data root survives an update.
- The logon task exists after install with `LogonType=InteractiveToken`, and is
  gone after uninstall.
- With two BrowserAI processes live, the second does not apply.
- `git status --porcelain` is empty.

> **Expect this step to correct §G.** That section is explicitly the
> pre-verification record;
> [kb: Velopack](../kb/packaging/velopack.md#the-nine-landmines-claim-and-verdict)
> is authoritative where the two disagree. The compressed size of the ~117 MB
> payload has never been measured, so size the timers against a link speed, not
> against a figure nobody took — and record the size when the first pack
> produces it.

### 20. The first release

**Consumes:** [pre-release](pre-release.md) ·
[testing → the release gate](testing.md#the-release-gate)

Run the checklist in full. It has never been executed, so **the first run is
also its test**: an item that cannot be evidenced is a defect in the checklist,
and fixing it belongs to this step.

**Done when:**

- Every item in [pre-release](pre-release.md) is checked, with the command run
  and what it returned recorded beside the release. No item's evidence is *"I
  believe this is fine."*
- Any item that could not be evidenced has been rewritten so it can be.
- The maintainer decides. Green is *releasable*, not *released*.
- `git status --porcelain` is empty.

---

## What this list deliberately does not sequence

Stated so an absence reads as a decision rather than an oversight.

- **The `.gitignore` items still owed at v1** ([`TODO.md`](../TODO.md)) — the
  upstream-half refresh and the `.vscode/mcp.json` question. Repository hygiene,
  not a build step. Do them when steps 3 and 19 first emit real artifacts and the
  guessed paths become observable.
- **Code signing.** Undecided. §G's SmartScreen hazard says decide before the
  first colleague handoff, which is not the same as before the first release.
- **A bundled-browser build.** Blocked on a redistribution answer from Google.
  Waiting is not work.
- **Forwarding resources and prompts.** `@playwright/mcp` advertises only
  `tools`, so there is nothing to build until it does not — recorded as a hazard
  precisely because the day it changes, nothing will announce it.
- **An HTTP transport.** It would make the session directory network-reachable
  and reopen the bearer-token question. Out of scope, and named here so a future
  transport change does not cross that line silently.

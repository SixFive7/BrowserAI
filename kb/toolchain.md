<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The build toolchain: MSBuild, NuGet, npm, analyzers and git

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · .NET SDK **10.0.302** and **10.0.400**, runtime **10.0.11** · TUnit **1.65.0** · `Microsoft.Testing.Platform` **2.3.3** · npm **11.19.0** · PowerShell **7** · `Microsoft.CodeAnalysis.BannedApiAnalyzers` as resolved by the build.
Measured on [the reference machine](README.md#the-reference-machine).

Traps in the tooling that builds this kind of product rather than in the product
itself. Nothing here is about processes or about browsers; it is here because
every one of these cost a build, and most of them fail quietly.

## MSBuild property evaluation

**A double hyphen in an XML comment in `Directory.Build.props` presents as
`NETSDK1207: Ahead-of-time compilation is not supported for the target
framework`.** Measured twice, 2026-08-16, SDK **10.0.302**: XML forbids `--`
inside a comment, MSBuild then cannot load the file, and the project builds
*without* it — so `TargetFramework` is never set and the AOT check fails on a
framework nobody chose. **The two entry points disagree, and only one is
useful:** `dotnet build` reports NETSDK1207 from
`Microsoft.NET.Sdk.FrameworkReferenceResolution.targets(120,5)`, while
`dotnet msbuild <project> -getProperty:TargetFramework` reports the real cause,
`MSB4024 … An XML comment cannot contain '--'`, with the line and column. Reach
for `-getProperty` whenever a shared props file has just been edited and the
error names something unrelated. `[FLOATS]` for the SDK version; `[STABLE]` for
the XML rule.

**`$(IntermediateOutputPath)` is empty in a `.targets` file imported from the
project body.** Measured 2026-08-16 on SDK **10.0.302** while wiring the
upstream-snapshot gate: a stamp written to `$(IntermediateOutputPath)x.stamp`
landed in the **project directory**, not in `obj\`. The property is defined by
`Microsoft.Common.CurrentVersion.targets`, which the SDK imports *after* the
project body, so a `PropertyGroup` in an imported `.targets` evaluates it to
nothing and the path degrades to a bare filename. `$(BaseIntermediateOutputPath)`
comes from `Microsoft.Common.props` at the top and is set. The failure is
quiet in the worst way: the build works, incrementality works, and the only
symptom is an untracked file that `git status --porcelain` reports — which is
why every build-order step ends by running exactly that. Re-establish by
pointing a `Touch` task at `$(IntermediateOutputPath)` from an imported
`.targets` and looking at where the file lands. `[FLOATS]`

## NuGet: floating versions and central package management

**Floating NuGet is two restore steps, not one.** `dotnet restore
--force-evaluate` resolves the float; a second, locked-mode restore verifies it.
They are mutually exclusive in one invocation: **with a lock file present and no
`--force-evaluate`, NuGet does not re-resolve and the float is silently dead**
([NU1512](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1512),
warned by default from the .NET 11 SDK). `git diff --exit-code --
"**/packages.lock.json"` after the resolve is then the cheapest available drift
detector. `[FLOATS]`

**Central package management refuses a floating version by default.** With
`ManagePackageVersionsCentrally` set, a `PackageVersion` of `Version="*"` fails
restore outright: *"NU1011: The following PackageVersion items cannot specify a
floating version"*. The enabling property is
`CentralPackageFloatingVersionsEnabled`, and without it the two properties the
plan named produce a `Directory.Packages.props` that reads exactly like the
float and cannot restore at all. Measured 2026-08-16 on SDK **10.0.302** while
building the skeleton. Re-establish by deleting the property and restoring.
`[FLOATS]`

## npm, for a vendored payload

**npm keys a lock file's root package on the empty string, and PowerShell's
`ConvertFrom-Json` refuses that outright.** Measured 2026-08-16 on npm **11.19.0**
and PowerShell **7**, while building the payload: `package-lock.json`
`lockfileVersion` 3 opens `"packages": { "": { … } }`, and parsing it raises *"The
provided JSON includes a property whose name is an empty string, this is only
supported using the -AsHashTable switch."* It is a hard parse failure rather than
a dropped key, so it surfaces immediately — but only if something parses the lock
at all, and the natural first version of a payload build does not. Re-establish
by piping any npm lock through `ConvertFrom-Json` with and without
`-AsHashtable`. `[FLOATS]`

**`npm ci` does not rewrite the lock**, verified in the same run by comparing the
file byte for byte either side of the call — which is what makes it usable as the
npm half of the two-restore pattern above: `npm install` from a **deleted** lock
and an empty `node_modules` resolves the `latest` dist-tag, `npm ci` then proves
the resulting lock reproduces that tree on its own. Deleting the lock first is
what guarantees the re-resolution. **Whether `npm install` re-resolves a dist-tag
dependency with a lock already present was not measured** — the payload build
never gets into that state, so the question is open rather than answered.
`[FLOATS]`

## Analyzers and diagnostic severity

**`BannedApiAnalyzers` merges every additional file named `BannedSymbols.txt`.**
Measured 2026-08-16 by planting one call per project: with
`build/BannedSymbols.txt` supplied to all projects from `Directory.Build.props`
and `src/BrowserAI/BannedSymbols.txt` supplied only to the product, the product
project reports **both** files' bans on the same build. That is what lets the
repository-wide rule and the product-only rules live in separate files instead of
being duplicated. Re-establish by planting a banned call and reading the RS0030
message, which quotes the entry's own text. `[FLOATS]`

### Diagnostic severity: what actually enforces a rule, and what only looks like it

All four measured 2026-08-16 on SDK **10.0.302**, by planting the failure and
rebuilding with `--no-incremental` rather than by reading documentation. They
matter here because [a severity is never weakened to make code pass](../CLAUDE.md#rules-a-mechanism-enforces) and a
severity that is quietly inert is the same defect as a config key
`loadConfig` discards.

**`NoWarn` beats `WarningsAsErrors`, and it beats an `.editorconfig` severity
too.** A method holding a statement after a `return` was compiled three ways.
With `TreatWarningsAsErrors` plus `WarningsAsErrors` naming `CS0162`: **1
error**. Adding `<NoWarn>CS0162</NoWarn>`: **0 warnings, 0 errors** — the
unreachable code compiled. Adding `dotnet_diagnostic.CS0162.severity = error`
on top of that NoWarn, and forcing a full rebuild: still **0 warnings, 0
errors**. So naming a warning in `WarningsAsErrors` does **not** protect it from
a later bulk suppression, which is what
the build order asserted and what this
measurement corrected. What naming it there does buy is survival if
`TreatWarningsAsErrors` is ever turned off — a smaller claim, and a true one.
The protection that works has to sit outside the compiler's precedence order
entirely, and here it is a test:
`BuildConfigurationTests.NoBuildFileSuppressesWarnings` fails on any `NoWarn` or
`WarningsNotAsErrors` in a project or shared-props file. `[FLOATS]`

**Bulk `.editorconfig` analyzer configuration is ignored once `AnalysisMode` is
set as an MSBuild property.** `dotnet_analyzer_diagnostic.category-<X>.severity`
had no effect at all: set to `none` for the TUnit assertion category, the rule
kept firing at error. The **per-rule** form is honoured in the same build —
`dotnet_diagnostic.TUnitAssertions0002.severity = none` did suppress it. This is
documented behaviour rather than a bug, and it is worth a measured entry because
the failing form fails *silently*: a category line reads as protection, is
ignored, and nothing reports that. Anything in this repository's
`.editorconfig` that must actually hold is therefore written per-rule.
`[FLOATS]`

**IDE0005 will not run on build without `GenerateDocumentationFile`.** With
`EnforceCodeStyleInBuild` on and IDE0005 escalated, the build fails with a
diagnostic named `EnableGenerateDocumentationFile` telling you to set the
property ([dotnet/roslyn#41640](https://github.com/dotnet/roslyn/issues/41640)).
It is an error rather than a quiet skip, which is the good outcome; the trap is
that the fix also turns on CS1591, so every publicly visible member then needs
an XML doc comment or the build is red under `TreatWarningsAsErrors`. `[FLOATS]`

## PowerShell as a build-script host

**Two PowerShell traps, both measured 2026-08-16 while making a build script's
output into a build error message.** `Get-Command 'git' -CommandType
Application` returns **two** entries on a Git-for-Windows machine —
`cmd\git.exe` and `mingw64\bin\git.exe` are both on `PATH` — so `$git.Source`
is one string naming two executables and invoking it fails with *"The term
'C:\…\mingw64\bin\git.exe C:\…\cmd\git.exe' is not recognized"*. `Select-Object
-First 1` is required rather than tidy. And **PowerShell 7 emits ANSI colour
escapes even when its output is redirected into a pipe**, which arrives in an
MSBuild `<Error>` as line noise around the diff it is supposed to be carrying;
`$PSStyle.OutputRendering = 'PlainText'` is the switch. `[MACHINE]` for the
duplicate git, `[FLOATS]` for the rendering default.

## git line-ending normalisation

**A committed byte copy and its regenerated twin are not governed by the same
line-ending rules, and `git add` is where they diverge.** Measured 2026-08-16
with `git hash-object --path`: a two-line CRLF file hashes to its **raw** bytes
under a path matched by `upstream-snapshots/** -text`, and to the **LF-converted**
form under any other path in this repository, where `* text=auto eol=lf` and
`*.json text` apply. `git add` on the second prints *"CRLF will be replaced by
LF the next time Git touches it"* and stores the converted blob. So a
regenerate-and-diff gate over committed copies of upstream files needs its
directory exempted, or the comparison is between a normalised side and an
unnormalised one — **permanently red on a difference that is not a difference**,
whose tempting fix (normalise the generator's output too) makes an upstream
line-ending change invisible instead.

> **All four snapshots are LF today** — `config.d.ts` and
> `playwright-core/browsers.json` as npm installs them, `cli.js --help`'s
> output, and our generated JSON — so the exemption currently changes nothing
> and is a guard against an upstream that changes its mind. **The conversion it
> guards against is not hypothetical in this repository:** every
> `dotnet restore` prints *"in the working copy of
> `src/BrowserAI/packages.lock.json`, CRLF will be replaced by LF"*, because
> NuGet writes those files CRLF and `*.json text` normalises them on the way in.
> Nothing byte-compares a lock file, so there it is harmless. Re-establish by
> counting CR **bytes**: `tr -cd '\r' < file | wc -c`. **Not** with `grep -c
> $'\r'`, which in Git Bash reported every line of an all-LF file as a match
> and produced a confident wrong answer that survived into four documents
> before a byte count contradicted it. `[FLOATS]`

## `dotnet test` and the test host

**`dotnet test` transiently reported zero tests once, and does not reproduce.**
Observed 2026-08-16 during
the two custom transports on
SDK **10.0.302** / .NET **10.0.11**, TUnit **1.65.0**,
`Microsoft.Testing.Platform` **2.3.3**: exit **5**, *"Zero tests ran"*, in about
250 ms, with `--diagnostic` showing the host's log stopping right after
`Setting PlatformExitProcessOnUnhandledException` and a command line ending
`--server dotnettestcli --dotnet-test-pipe testingplatform.pipe.<guid>` — the
handshake `dotnet test` alone uses.

> ⚠️ **Corrected 2026-08-16 (previously: "`dotnet test` runs zero tests against
> this suite … It is not caused by anything in this repository, and that had to
> be proven rather than assumed. A clean `git worktree` of `b8a6553` … reproduces
> it exactly").** It does not reproduce. Re-run the same day against the same
> machine and the same SDK: `dotnet test BrowserAI.slnx` at `e5f4684` returned
> **51 passed, exit 0**, and a fresh `git worktree --detach` of **`b8a6553`** —
> the exact commit the entry named as its proof — returned **30 passed, exit 0**.
> The original entry's load-bearing sentence was therefore false, and so was its
> consequence that *"the evidence recorded for every build-order done-test since
> step 1 came from the executable"*: steps 1 and 2 were evidenced with
> `dotnet test` reporting 5 and then 13 passing tests.
>
> **What went wrong is worth more than the entry was.** A single failing
> observation was written up as a standing property of the toolchain, complete
> with a reproduction that had not been re-run at the moment it was cited. That
> is the same shape as the `grep -c $'\r'` error two entries above, and it is the
> failure this whole directory exists to prevent — the difference between *"I saw
> this once"* and *"this is how it behaves"* is a second run, and it costs
> seconds.

**What to do if it recurs:** run `dotnet test`, then `BrowserAI.Tests.exe`, then
`dotnet test` again. Two disagreeing runs of the same command are a transient;
a stable disagreement between the two commands is the real thing and earns a new
entry with the versions it held under. Do not remove `{ "test": { "runner":
"Microsoft.Testing.Platform" } }` from `global.json` reaching for a fix — it is
the documented MTP opt-in, TUnit is MTP-only, and there is no VSTest mode to
fall back to. `[MACHINE]` for the single observation; nothing here is
`[FLOATS]`, because no standing behaviour was established.

**It recurred, it is now stable, and the retraction above still stands.**
Measured 2026-08-16 during
lossless passthrough, following
the procedure the paragraph above prescribes. `dotnet test` reports *"Zero tests
ran"*, `error: 1`, exit **5**, in 177–646 ms:

| Run | Result |
|---|---|
| `dotnet test BrowserAI.slnx`, Git Bash, working tree | zero |
| the same, repeated | zero |
| the same, from PowerShell 7 | zero |
| `dotnet test tests/BrowserAI.Tests/BrowserAI.Tests.csproj` | zero |
| `dotnet test BrowserAI.slnx --list-tests` | *"Discovered 0 tests"* |
| the working tree with every step-9 change stashed, i.e. `c9d30d4` | zero |
| **a fresh `git worktree --detach` of `b8a6553`** | **zero** |
| `BrowserAI.Tests.exe` | 106 passed, exit 0 |
| `dotnet BrowserAI.Tests.dll --list-tests` | 88 found *(before the step-9 tests were written)* |

**The last three rows are the whole finding.** The same commit that returned
**30 passed** hours earlier returns zero from a clean worktree, while the same
built assembly run directly finds and runs everything. So **nothing in this
repository causes it**, and the earlier retraction was not wrong. Versions are
identical either side: SDK **10.0.302**, .NET **10.0.11**, and the committed lock
file still resolves TUnit **1.65.0** and `Microsoft.Testing.Platform` **2.3.3**,
so it is not a package float.

> ⚠️ **Corrected 2026-08-16, minutes after the table above was written
> (previously: "It recurred, it is now stable").** It is **not** stable, and the
> variable is not time. Immediately after that entry landed, the same
> `dotnet test BrowserAI.slnx` was run three times in a row from the **root
> session's** shell against the same commit: **106 passed / exit 0**,
> **`Discovered 106 tests`**, **106 passed / exit 0**.
>
> **The discriminator is which shell issues the command, not when.** Every
> zero-test observation on record — the transient at step 5 and the seven-row
> table above at step 9 — was made inside a **sub-agent's** shell, by two
> different agents hours apart, including both times a clean worktree of
> `b8a6553` was cited. Every successful run — 5, 13, 30, 51, 106, 106, 106 — was
> made in the root session's shell. **Both sets of measurements are real**; what
> was wrong each time was the generalisation from one shell to the toolchain.
>
> This is worth more than the failure it describes. Twice tonight a correct
> observation became a false standing claim by being attributed to the wrong
> subject — first to the toolchain, then to the machine, when it belongs to the
> execution context. **When a result cannot be reproduced by someone else,
> suspect the environment before the artifact**, and name the environment in the
> entry.

**What is not established**, and must not be written in without measuring: why
the sub-agent shell differs. Candidates not yet tested include MSBuild node
reuse across many back-to-back builds, a stale build server, and concurrent
`dotnet` processes — the agents run long build sequences, the root session does
not.

**The practical consequence is small, and it should be stated so nobody
over-reacts to it.** `BrowserAI.Tests.exe` runs the whole suite in every
environment, exit code and all, so the release gate is unaffected: the suite
genuinely runs, and a red test is genuinely red. What a sub-agent must not do is
read a zero-test result as *"the suite has never run"*.

The cause is not established. `--diagnostic` shows the host launched with
`--server dotnettestcli --dotnet-test-pipe testingplatform.pipe.<guid>` and the
log ending immediately after `Setting
PlatformExitProcessOnUnhandledException` — the same fingerprint as the transient,
which is consistent with a defect in the `dotnet test` ↔ MTP handshake rather
than in discovery. **Do not write a cause into this entry without measuring
one.** [`TODO.md`](../TODO.md) carries the investigation, and build-order
step 9's evidence came from `BrowserAI.Tests.exe`, which is stated on that step
rather than left implicit. `[MACHINE]` — it is a fact about this machine on this
date, and the identical tree behaved differently on the same day.

### Running 419 tests at once: what starves, and by how much

**At `SuiteParallelism.Unbounded` the whole failure population is bounds
expiring, not logic.** Measured 2026-08-17, twenty consecutive runs of the full
suite with the TUnit parallel limiter set to 1024 — above the test count, so the
scheduler's semaphore never blocks. **Eleven runs of twenty went red**, and every
failure across all eleven was one of three messages:

| Message | Occurrences | The bound behind it |
|---|---|--:|
| `No frame arrived on this pipe within 30 s` | 71 | `TestDefaults.Patience`, applied per frame over an **in-process** pipe |
| `A task was canceled` | 48 | assorted 30 s `CancellationTokenSource`s, carrying no method, no elapsed time and no peer |
| `Initialization timed out` | 46 | the MCP SDK's unset 60 s `InitializationTimeout` ([kb](mcp/sdk.md#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation)) |

**Not one was a logic fault.** The mechanism is thread-pool starvation and it is
arithmetic rather than mystery. ***Corrected 2026-08-18*** on the *reason*, not
the conclusion: the injection rate quoted here was *"roughly **one a second**"*
while the same platform fact in
[`windows/processes.md`](windows/processes.md) says *"roughly one thread per
500 ms"* — **two numbers a factor of two apart, for one behaviour, neither of
them measured** — and that article goes further and **measures injection away as
the mechanism**: `ThreadPool.SetMinThreads(1024, 1024)` made the same run
*worse*, not better. The starvation is real and the fix was right; what is
**not** established is that hill-climbing injection causes it, and the surviving
explanation is the one that article does measure, plain CPU oversubscription.
Read the rate as `[UNVERIFIED]`. So 419 tests admitted at once —
several of which block a worker in `Thread.Sleep` inside a polling loop, and one
of which (`SaturationTests`) puts a hundred processes on the machine — leave an
in-process exchange that normally costs single-digit milliseconds waiting
**tens of seconds** for a thread to run its continuation on. A thirty-second
silence between two objects in the same process stops meaning *deadlock* at that
point, which is exactly what those messages claimed it meant.

**The fix was the bounds, not the parallelism.** Re-measured 2026-08-18 with
every promptness assertion removed and every surviving bound sized so a starved
machine cannot reach it (`TestDefaults`: 5 minutes in-process, 10 minutes across
a process, 30 minutes for a real browser): **two separate streaks of 20
consecutive green runs**, 419 tests, 0 failed, 0 skipped. The limiter did not
move.

| Streak | Tree | Wall clock | Machine |
|---|---|---|---|
| first | before the lock-rename work | 66–91 s | quiet |
| final | every fix in | **72–142 s** | three other agents working on it |

**The second is the better number, and the spread is why.** The final streak ran
while other agents were building and testing on the same box — 14 `claude`
processes, 11 `node`, Defender at 2.5 GB — and the per-run wall clock moved by a
factor of two across it. Twenty green under a load that varies that much is a
stronger statement about the bounds than twenty green on an idle machine, because
the whole claim being made is that a busy machine cannot reach them.

> ⚠️ **The range here was once written before it was measured, and was wrong.**
> An earlier revision said *78–108 s* on the strength of a handful of runs; the
> twenty it claimed to describe were 66–91 s. Recorded rather than quietly
> overwritten, because a plausible number typed ahead of the measurement is
> indistinguishable from a measured one — which is the failure this directory's
> first rule exists to prevent, and it happened here.

**Getting there took eight streaks and about 120 runs, and not one of the
failures along the way was a duration.** Every one was a real defect that
four-way parallelism had never surfaced: a rename refused `ERROR_ACCESS_DENIED`
on a destination the test had just released; the same delete-pending window on
the *read* side, throwing out of `SessionLock.ReadRecord` past every handler on
the path; the discovery that `MoveFileEx` leaves the destination name
transiently unbound; and a two-second retry budget that a starved process
exhausted in three attempts
([kb](windows/processes.md#files-durable-writes-and-deletes)). That is the
argument for the limiter being where it is, made by the limiter.

> **One failure in that set was not a defect and is worth naming, because it is
> the honest limit of this method.** A real Chromium exited with code 1 and no
> output on either stream, once, while three other agents were saturating the
> machine — a browser that could not start, rather than anything the suite
> controls. It did not recur across the 20-run streak that followed on the same
> tree. A suite that runs 419 tests at once on a shared box will occasionally
> measure the box.

**Two shapes are worth carrying off this machine.** First, *the same bound
expressed at two layers, with the tighter one winning invisibly*: a launcher that
waited 60 s under a host advertising 180 s reported a launch that failed at 60 s
as a three-minute timeout, and the SDK's unset 60 s initialization timeout is the
same defect wearing a dependency's clothes. Second, *a promptness assertion
wearing a hang detector's name*: a bound that a busy machine can reach is not
detecting a hang, and every one of them here reported something other than
"this machine is busy". `[MACHINE]` for the counts and the wall clock — the
reference machine is 32 cores, and a smaller one will starve harder rather than
differently.

## What a NativeAOT publish emits

**NativeAOT embeds `ApplicationManifest` into the published binary.** Verified by
reading the bytes of a `PublishAot` win-x64 publish: `longPathAware`,
`asInvoker` and the Windows 10/11 `supportedOS` GUID are all present in
`BrowserAI.exe`. This is not inherited from an apphost — the publish output is
the native exe, a `.pdb` and the XML doc file, with no managed `.dll` beside it.
It matters because the long-path guarantee is otherwise unfalsifiable: session
directories are caller-chosen and unbounded, and a manifest that silently failed
to embed would present as a path failure deep inside a browser profile tree.
`[FLOATS]`

⚠️ **The manifest is only half of what Windows requires, and the other half was
asserted nowhere in this repository until 2026-08-19.** `longPathAware` in the
manifest is necessary and **not sufficient**: Win32 honours it only when
`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled` is also
`1`, which is a **machine setting an administrator sets and a default install
leaves at `0`**. So every long-path measurement recorded anywhere here is
conditional on a value nobody had written down. Read 2026-08-19 on the reference
machine, Windows 10.0.26200: **`LongPathsEnabled` = `1` (`REG_DWORD`)**. `[MACHINE]`,
and the most consequential `[MACHINE]` stamp in this file — a reader reproducing
any long-path behaviour on a machine where it is `0` will get the opposite answer
from a correctly-built binary. **What this does not tell you** is what BrowserAI
does on such a machine: nothing here has run against `LongPathsEnabled = 0`, and
the product makes no check and produces no diagnostic that would name it.
Re-establish with `Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem
-Name LongPathsEnabled`.

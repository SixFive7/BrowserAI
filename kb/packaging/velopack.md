<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Velopack and the update path

Read from Velopack **1.2.0** and its Rust binaries unless noted. `[FLOATS]` —
this is a floating dependency like any other.

**Two passes are recorded here, merged rather than stacked.** The first was read
out of Velopack's own sources and out of a working in-house deployment. The
second was a spike on **2026-08-15**: a NativeAOT app packed with `vpk`,
installed per-user, updated 1.0.0 → 1.0.1, rolled back, and uninstalled, against
a local feed server. Everything from that run is observed. Where the two
disagree, **the 2026-08-15 spike wins and the disagreement is stated inline** —
a claim that was quietly corrected reads identically to one that was never
wrong, which is the failure mode this whole file exists to prevent.

## What Velopack is, and the shape of the install

**Per-user install to `%LocalAppData%`, no elevation, MIT, no commercial tier.**
The same 1.2.0 release ships `lib-nodejs`, `lib-rust` and `lib-python`, and the
Rust `Update.exe` doing the real work is identical for all of them — so the
update story does not by itself require C#. `--msi PerMachine` installs to
`Program Files` and makes the updater self-elevate, which a background stdio
server cannot answer.

**The delta scheme is per-file zstd `--patch-from`, and unchanged files collapse
to zero-byte markers.** `current\` is a real directory, not a junction, so the
executable path is stable across updates.

**Measured 2026-08-15: delta for an 8.76 MiB binary change is 3,210 bytes.**
Update wall time ~2.5 s, and **`current\` is absent for 1.7 ms** — two
`fs::rename` calls, effectively atomic. Running instances are killed without
warning. `[FLOATS]`

## Where state may live — the finding the provisioning design rests on

Spike 2026-08-15. `[FLOATS]`

**`RootAppDir` is the directory containing `Update.exe` — the parent of
`current\`.** Across update *and* rollback, a sibling directory accumulated all 7
hook stamps including the original install's, and a 5 MB payload file kept its
sha256 unchanged. **Siblings of `current\` survive both.** The
`AppContext.BaseDirectory` trap is real and was confirmed in the same run: files
written inside `current\` lost their pre-swap contents both times.

Three caveats that bear on the design, none of which the charter had:

- ⚠️ **A repair or overwrite install destroys them.** `install.rs` renames a
  non-empty root to `{root}.{random16}` and, on success, **deletes it**. Re-running
  `Setup.exe` over an existing install therefore costs a **203.8 MB re-download**.
  **Updates must go through the update path; `Setup.exe` must never be re-run over
  an existing install.**
- **Uninstall wipes the whole root** (`remove_dir_contents`) — browsers included,
  which is correct but worth stating.
- Transient update space is `<root>\packages\VelopackTemp\`: same volume, outside
  `current\`.

⚠️ **`force_stop_package` will kill our browsers.** It matches by image **path**
under the root and runs on `apply`, `install`, `start`, `uninstall` **and after
every hook returns** (`windows/util.rs:59`). Two unrelated processes were killed
by an update launched from a third. Our browsers live under `RootAppDir`, so an
update terminates every running browser without warning and without our teardown.
Chromium survives hard kills and our locks release on process death, so the damage
is a lost session rather than corruption — but it bypasses the job object entirely,
and a hook must never leave a helper running under the root.

## The nine landmines, claim and verdict

Each entry is the standing record first — what was read out of Velopack and out
of the in-house deployment — then what the 2026-08-15 install/update/rollback
spike found when it went looking. **Four are still real, three no longer apply,
one was wrong for 1.2.0, and one is fixed.**

### 1. The channel must not go in the feed URL

**`SimpleWebSource` composes the feed request as
`{BaseUrl}/releases.{channel}.json`.** A base URL built as `{BaseUrl}/{channel}`
therefore fetches `{BaseUrl}/{channel}/releases.{channel}.json` — a 404, surfaced
as *"no update available"* and nothing else. The channel belongs in
`UpdateOptions.ExplicitChannel`. A local-directory source composes paths
differently and passes where production 404s.

> **Verdict, 2026-08-15: still real, consequence wrong.** 1.2.0 throws
> `HttpRequestException … 404`. Not silent, not "unrecoverable in the field" —
> catchable, so a health check can detect it.

> **Qualified 2026-08-16: a 404 is not by itself a misconfiguration signal.** The
> verdict above is right that the 404 is catchable, but it leaves the impression
> that catching one is enough for a health check to conclude something is wrong.
> It is not — **a legitimately empty channel returns the same 404.** Read from
> `C:\Source\ExoFabric\UCC\KnowledgeBase\Velopack\Troubleshooting.md`, which lists
> *"Empty channel: no releases published to that channel yet"* first among the
> causes and ships the discrimination as
> `catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)`
> → log at Debug, report "no update", re-arm the schedule. So the status code
> separates *404* from *transport failure*, and nothing more; distinguishing a
> misconfigured feed URL from an unpublished channel needs a second signal, such
> as whether any channel resolves. Consequence for us: a health check that alarms
> on 404 will cry wolf on every pre-release channel we have not yet published to.
> Read from source, not run. `[MACHINE]` for the UCC code; the 404-on-empty
> behaviour is Velopack's and `[FLOATS]` with it.

### 2. `SetAutoApplyOnStartup(false)` is mandatory

**`SetAutoApplyOnStartup` defaults to `true`.** On finding a staged package,
`VelopackApp.Run()` applies it, `exit(0)`s and relaunches — **with no inherited
stdio**, so an MCP client sees its server exit at handshake time.

> **Verdict, 2026-08-15: still real.** Default is `true`; the relaunch is
> detached.

### 3. Never register the execution stub

**The execution stub is compiled `#![windows_subsystem = "windows"]` and returns
immediately** without waiting, so a stdio client registered against the stub sees
the child die instantly with no pipes attached.

> **Verdict, 2026-08-15: still real, reason wrong.** "No pipes attached" is
> false — stdio is inherited stub → `Update.exe` → app, and 3,220 bytes of app
> stdout arrived on the stub's pipe 12.9 s after the stub died. The killer is that
> the stub **exits in 59 ms** while the app runs on.

### 4. `force_stop_package` kills everything under the root

**`force_stop_package` kills every process under the install root** without
asking.

> **Verdict, 2026-08-15: still real, broader than stated.** It matches by image
> **path** and runs on `apply`, `install`, `start`, `uninstall` **and after every
> hook returns** — see
> [the provisioning finding above](#where-state-may-live--the-finding-the-provisioning-design-rests-on).

### 5. Reading the installed version must not touch the network

**Constructing an `UpdateManager` merely to read the installed version issues a
network request.** `VelopackLocator` reads local metadata only.

> **Verdict, 2026-08-15: never applied to 1.2.0.** Ctor only assigns fields; 0 ms
> against an invalid host, zero requests logged. *New caveat:* `VelopackLocator`
> is not free — it probes writability, **creates `packages\` and
> `packages\VelopackTemp`**, and opens a log file.

### 6. `NotInstalledException` under `dotnet run` and every test host

**`NotInstalledException` is the normal outcome under `dotnet run` and every test
host** — neither is a Velopack install, so every Velopack call throws.
`Debugger.IsAttached` does not detect a test runner.

> **Verdict, 2026-08-15: wrong for 1.2.0.** `VelopackLocator.Current` and
> `new UpdateManager(url)` throw `InvalidOperationException: No VelopackLocator
> has been set`; `VelopackApp.Build().Run()` **succeeds**, warns, and leaves
> `IsInstalled == false`. The test seam is a **boolean**, not exception handling.

### 7. `ApplyUpdatesAndRestart(null)` as a bare restart

**`ApplyUpdatesAndRestart(null)` restarts without a package by undocumented
fall-through** — the internals skip the `--package` argument when there is no
local full package. `UpdateExe.Start(waitPid)` is the supported restart.

> **Verdict, 2026-08-15: now documented.** Advice still right for a different
> reason: `toApply ?? GetLatestLocalFullPackage()` means null is "apply whatever
> is staged", not "just restart". ⚠️ **Charter code error:**
> `UpdateExe.Start(waitPid)` does not compile — the first positional parameter is
> the locator.

### 8. `IVelopackLogger` needs two registrations

**`IVelopackLogger` takes two separate registrations** — the runtime
`UpdateManager` and the `VelopackApp.Build()` startup hooks. Bridging only the
first leaves the installer, first-run and post-restart hooks silent.

> **Verdict, 2026-08-15: fixed.** One `VelopackApp.SetLogger()` reaches
> installer, hooks, `UpdateManager` and bridged Rust output.

### 9. The Rust binaries carry their own Windows floor

**Velopack's Rust `Setup.exe`/`Update.exe` carry their own Windows floor,
separate from .NET's**, and can fail *before* the managed app exists: before
**0.0.530** they statically linked `IsWow64Process2` and crashed below Windows 10
1709. `--runtime win7` does not help if the installer binary cannot run.

> **Verdict, 2026-08-15: general claim holds; the cited defect is fixed.**
> `IsWow64Process2` is now dynamically loaded with an error path. Shipped binaries
> are MinOS 6.0, 32-bit PE32 GUI; no `vpk pack` option sets `os_min_version`.

## The restart handover race, and why `Update.exe` is the answer

Read 2026-08-16 from
`C:\Source\ExoFabric\UCC\KnowledgeBase\Velopack\Application Restart.md`, which
documents both wrong answers as well as the right one. Not run here. `[STABLE]`
for the race; `[FLOATS]` for the API surface.

The pieces are already elsewhere in this article — `UpdateExe.Start(waitPid)` in
[landmine 7](#7-applyupdatesandrestartnull-as-a-bare-restart), `force_stop_package`
in [landmine 4](#4-force_stop_package-kills-everything-under-the-root), and UCC
being single-instance via a named mutex in
[Prior art](#prior-art-exofabricucc) — but they are never joined into the race
itself, which is the part that bites:

- **Spawn the new instance, then exit.** The new process checks the mutex while
  the old one still holds it, concludes another instance is running, takes the
  secondary path — for UCC, sending a `TOGGLE` over the pipe — and exits. *"Result:
  App hides instead of restarting."*
- **Release the mutex first, then spawn.** Now there is a window in which no
  instance holds it, and any unrelated launch in that window — a Start-menu click
  — becomes primary. *"Result: Wrong instance becomes primary."*

There is no ordering of "release" and "spawn" that closes both, because the
process doing the handover is one of the two parties to it. **`Update.exe`
resolves it by being neither:** it is an external Rust binary that outlives the
app, receives the PID via `waitPid`, waits for *actual process death* rather than
a close request, and only then launches. The mutex is released by the OS at
termination, which is strictly before the new process starts.

Two consequences worth carrying:

- **The signature is `Start(IVelopackLocator locator = null, uint waitPid = 0,
  string[] startArgs = null)`** — first positional parameter is the locator,
  confirming the ⚠️ charter code error already recorded in
  [landmine 7](#7-applyupdatesandrestartnull-as-a-bare-restart) from an
  independent source.
- **The update path must not double-restart.** `ApplyUpdatesAndRestart` restarts
  on its own (`WaitExitThenApplyUpdates(..., restart: true, ...)` then
  `Environment.Exit(0)`), so calling `UpdateExe.Start` as well produces two
  relaunches. UCC's fix is to route the update case through the same cooperative
  shutdown but with `IsRestart: false`.

**Where this stops applying to us:** the whole race is about a *single-instance*
app coordinating with its own successor. BrowserAI is a stdio child spawned per
client, so it has no mutex to hand over — but it does inherit the harder half,
because the relaunched process
[does not inherit the caller's stdio](#rollback) either way.

## Rollback

**Velopack prunes `packages\` down to the current full `.nupkg` and deltas are
forward-only**, so every rollback is a fresh full download unless packages are
archived by hand. `AllowVersionDowngrade` is the client half of rollback.

> **How big is a full BrowserAI package? `[UNVERIFIED]`, and it was stated as
> ~105 MB with no provenance anywhere in the repository.** Nothing has ever
> compressed the real payload. What is known: the payload is
> [~117 MB installed](../playwright/provisioning-and-timings.md#component-sizes),
> `node.exe` is 88.53 MB of it, and the only compression figure on record —
> ~806 MB → ~239 MB, 7z LZMA2 `-mx=5` — is for the browser-dominated tree that no
> longer ships. The spike's own full nupkg was **6,072,200 b**, but that was a
> payload-free test app and predicts nothing here. Measure it at the first real
> `vpk pack`; until then, no number.

**Measured 2026-08-15.** Rollback needs `AllowVersionDowngrade = true` (default
false yields "no updates", silently) and then forces a **full re-download** —
6,072,200 b, zero deltas — because `packages\` was pruned to the new full nupkg
during the forward update. Rollback fires the same obsolete/updated/restarted
hooks; from the app's view it is an ordinary update. `restartArgs` pass through,
but **the relaunched app does not inherit the caller's stdio.** `[FLOATS]`

## Channel — the charter's reason was wrong

Measured 2026-08-15. `[FLOATS]`

Default channel is `win` (the OS short name), stamped into `sq.version` and read
back by the locator. **A `-beta` version suffix has zero effect on channel
derivation** — packing `1.0.3-beta.1` with no `--channel` emitted
`releases.win.json`. The charter attributed the hazard to Velopack; it was
application code in a sibling project.

The real reason to set it explicitly: **a client installed from a beta
`Setup.exe` inherits `beta` in its manifest and stays there silently.** Two new
hazards: `ExplicitChannel = ""` produces `releases..json` → 404 (the code
null-coalesces, so empty is not unset), and **`vpk pack` lowercases the channel
while the client does not** — `"Beta"` passes on NTFS and 404s on a
case-sensitive store, which is exactly a sibling project's S3 setup.

## NativeAOT, hooks, and `vpk` output

Measured 2026-08-15. `[FLOATS]`

**NativeAOT + Velopack 1.2.0: zero trim/AOT/IL warnings.** `VelopackApp.Build().Run()`
works; install, delta update and rollback all work. Exe 9,182,720 b (8.76 MiB),
full nupkg 6,072,200 b, `Setup.exe` 10,533,768 b. The 34 MB pdb is excluded
automatically. ⚠️ **Target `net10.0-windows`** — the hook callbacks are
`[SupportedOSPlatform("windows")]`, so plain `net10.0` produces CA1416.

**Hooks can register the logon sweep task — confirmed.** All hooks ran **as the
user, non-elevated**, session 1. Fast-exit hooks with their timeouts:
`--veloapp-install` (30 s), `--veloapp-updated` (15 s), `--veloapp-obsolete`
(15 s), `--veloapp-uninstall` (60 s); `OnFirstRun` and `OnRestarted` do not exit.
`schtasks /Create /XML` from the install hook succeeded with
**`LogonType=InteractiveToken`** — "run only when user is logged on", which is
exactly what the sweep needs and the opposite of the session-0 trap. The task
**survived update and rollback** because it targets the stable `current\` path,
and the uninstall hook removed it.

**`vpk` emits**, into `Releases` by default: `{id}-{version}-full.nupkg`,
`{id}-{version}-delta.nupkg`, `{id}-{channel}-Portable.zip`,
`{id}-{channel}-Setup.exe`, `releases.{channel}.json`, `assets.{channel}.json`,
`RELEASES`. **It does not prune** — after 5 versions all 5 fulls and 4 deltas
remained and the feed advertised all of them.

**`vpk` rejects 4-part version numbers** — semver2, three parts only.

**And the assembly version does not follow it: a 4-part assembly version renders
as `1.0.0.0` wherever the app reads it back.** .NET assembly versions are 4-part
by default, so packing succeeds with a 3-part semver while the running app — a
window title, an about box, a log banner — shows a fourth component that exists
nowhere in the feed. The two numbers are separate and only one of them is
constrained by `vpk`. Read 2026-08-16 from
`C:\Source\ExoFabric\UCC\KnowledgeBase\Velopack\Troubleshooting.md`
(*Version Shows 4 Parts*), where it is filed as a shipped symptom rather than a
theory. Not run here. `[STABLE]` — this is .NET assembly-version behaviour, not
Velopack's.

> ⚠️ **Corrected 2026-08-16 (previously: "the fix is to format explicitly from
> the parts: `$"{v.Major}.{v.Minor}.{v.Build}"`").** That fix is wrong here, and
> it fails in the opposite direction to the symptom it treats. It reads
> `Assembly.GetName().Version`, and under the mechanism this project actually
> uses — see below — that number is **`{Major}.0.0.0`**, so formatting three
> parts out of it renders `0.0.0` for every build of the 0.x line and `1.0.0`
> for every build of the 1.x line. Measured on this repository's own artifact at
> tag `v0.1.0`: `AssemblyVersion` is `0.0.0.0` while the version is `0.1.0`. The
> correct source is `AssemblyInformationalVersionAttribute`, and the rule is
> that **nothing reads the assembly version at all**.

**`.gitignore` verdict** (closes the deferred v1 item): `/Releases/` ✅ ·
`*-Portable.zip` ✅ · `/RELEASES` ✅ default channel only · **`Setup.exe` never
matches** — the real name is `{id}-{channel}-Setup.exe` · **`/payload/`,
`/staging/`, `/.staging/` are not vpk output at all**; they are BrowserAI's own
build conventions and must be justified on that basis or dropped.

## Versions from git tags — MinVer 7.0.0 — 2026-08-16

Measured while building [step 18](../../plan/build-order.md), on SDK **10.0.302**
with **MinVer 7.0.0** resolved through the float (`Version="*"`, product project
only, `MinVerTagPrefix` of `v`). Everything below is read off the build or off
the artifact, never off MinVer's documentation. `[FLOATS]` — MinVer, the SDK and
git all move under it.

**What MinVer produced, at three heights.** Re-establish any row with
`dotnet msbuild src/BrowserAI/BrowserAI.csproj -t:MinVer -getProperty:MinVerVersion,Version,AssemblyVersion,FileVersion,InformationalVersion`.

| Where HEAD is | `MinVerVersion` | `AssemblyVersion` | `FileVersion` | `InformationalVersion` |
|---|---|---|---|---|
| On the annotated tag `v0.1.0` | `0.1.0` | **`0.0.0.0`** | `0.1.0.0` | `0.1.0` |
| 5 commits past it, no new tag | `0.1.1-alpha.0.5` | **`0.0.0.0`** | `0.1.1.0` | `0.1.1-alpha.0.5` |
| No reachable tag at all | `0.0.0-alpha.0.71` | — | — | — |

The five-commit row was produced by five `--allow-empty` commits on a throwaway
branch, built, read off the produced `BrowserAI.dll`, and the branch deleted; the
tagless row is what this repository produced **before** `v0.1.0` existed, which
is the only moment that state is free to observe. The height 71 is the commit
count since the root commit, one less than `git rev-list --count HEAD`.

**`AssemblyVersion` is `{Major}.0.0.0` by design, and that is the trap.**
`[FLOATS]` — MinVer's choice, and it is the one MinVer would change. It is
not a defect and MinVer will not be talked out of it — a 4-part assembly version
is a binding identity in .NET and MinVer keeps it stable across a major. The
consequence is that the number a caller reaches for first is **`0.0.0.0` for the
entire 0.x line**. The published binary's Win32 resource carries the useful
pair instead: `ProductVersion` is the informational version and `FileVersion` is
`{Major}.{Minor}.{Patch}.0` — read on the AOT-published `BrowserAI.exe` as
`0.1.0` and `0.1.0.0`.

**The SDK's `SourceRevisionId` decoration is gated on two properties, not one.**
`[FLOATS]` — the SDK floats under `rollForward: latestMajor`, so this moves
without anyone choosing it.
`AddSourceRevisionToInformationalVersion` in
`Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.GenerateAssemblyInfo.targets`
requires **`SourceControlInformationFeatureSupported == 'true'` and
`IncludeSourceRevisionInInformationalVersion == 'true'`**, and the second
defaults to `true` in that same file. The first is set by a source-control
provider — SourceLink and nothing in the SDK — so in a repository with no
SourceLink the decoration **cannot fire at all**, which means the property alone
proves nothing and a repository can look protected while being merely
un-provoked. Measured twice, each arm:

| Arm | `InformationalVersion` |
|---|---|
| Repository as committed, nothing supplied | `0.1.0` |
| Feature on, `SourceRevisionId` supplied, property `false` | `0.1.0` |
| Feature on, `SourceRevisionId` supplied, property `true` | `0.1.0+a273b31c0ffee1234567890abcdef1234567890a` |

So the property in `Directory.Build.props` **is** the thing that stops it, once
anything arms the feature. Two details cost time and are worth inheriting:
`-getProperty` reports the value only after the targets actually named ran, and
this decoration hangs off **`GetAssemblyAttributes`** rather than
`GetAssemblyVersion` or `MinVer` — asking after either of the latter two returns
an undecorated string and reads as proof of something it did not test. And a
`-p:InformationalVersion=…` global property does **not** survive: MinVer's own
target overwrites it, so the *`.`-separated* form the SDK produces when the
string already carries a `+` (`0.1.0+a273b31` becoming
`0.1.0+a273b31.<40-char sha>`) could not be reproduced here and is **read from
the target's own text** at lines 67–71 rather than measured. That form is the
one `SixFive7/FrameLink` shipped: a fleet where every frame downloaded the
binary it was already running, swapped it, restarted, and repeated hourly,
because its updater **matches** the served version against the reported one.

**A version derived from no tag fails the build.** `RefuseAVersionDerivedFromNoTag`
in `src/BrowserAI/BrowserAI.csproj` runs `AfterTargets="MinVer"` and refuses
anything beginning `0.0.0`, naming `fetch-depth: 0` and a tag fetch as the
remedies. Provoked for real on 2026-08-16 before the first tag existed:

```
error : This build derived the version 0.0.0-alpha.0.71 from git, and a version
beginning 0.0.0 means MinVer found no 'v*' tag to count from. ...
```

To re-provoke it without deleting a tag, build with a prefix that matches
nothing: `dotnet build src/BrowserAI/BrowserAI.csproj -p:MinVerTagPrefix=zzz`.

## New defect: `Setup.exe -- <args>` hangs forever

Found 2026-08-15. `[FLOATS]`

`setup.rs` declares `EXE_ARGS` without `.value_parser(value_parser!(OsString))`
but reads `get_many::<OsString>`, so passing start arguments panics with
*"Mismatch between definition and access of EXE_ARGS … Could not downcast"*. **The
process never exits**, installs nothing, and leaves one log line. `update.rs` has
the value parser and is unaffected. Any scripted install passing start arguments
hangs forever — the purest instance of this project's own failure class, found in
the tool we were about to trust with it. **BrowserAI must never pass start
arguments to `Setup.exe`.**

Two smaller ones: **Desktop and Start Menu shortcuts are created by default**
(`--shortcuts` defaults to `Desktop,StartMenuRoot`), and
`%LOCALAPPDATA%\velopack\` is created unconditionally, **not removed by
uninstall**, with non-installed runs writing to a machine-shared `velopack.log`.

## Distribution: MSIX and code signing

**MSIX is disqualified on evidence.** A package cannot re-register while any
process in its family is running: claude-code
[#63397](https://github.com/anthropics/claude-code/issues/63397) (`0x80073D02` /
`ERROR_SHARING_VIOLATION`, the report naming "Claude Code runs as a child process
of Claude Desktop") and openai/codex
[#25770](https://github.com/openai/codex/issues/25770), both in 2026. Hydraulic
Conveyor emits MSIX on Windows and inherits the same failure.

**Every unsigned `Setup.exe` is a new file to SmartScreen.** Azure Artifact
Signing at roughly **$10/mo** buys instant reputation. `[UNVERIFIED]` price — a
list figure, not a quote obtained.

## Prior art: ExoFabric/UCC

In-house evidence, not upstream behaviour. `[MACHINE]` — true of one repository
at one point in time.

**UCC runs Velopack 0.0.1298, not 1.2.0** — the pre-1.0 line, with both behaviour
and API surface since moved. It ships per-user to `%LocalAppData%\UCC\current\`,
no elevation, S3-compatible feed, silent background check, in production across
multiple releases.

**Five of the nine landmines above are ones UCC hit rather than avoided**, and
none announced itself:

- **Feed URL composition** bricked auto-update for **three shipped versions**;
  manual reinstall was the only recovery.
- **`SetAutoApplyOnStartup` is never called**, so the default is live in a
  shipping app — survivable for a foreground tray app, fatal for a stdio child.
- **Logs are written to `AppContext.BaseDirectory`** — inside `current\` — with a
  10-day retention policy that every update resets.
- **Delta packages have never been produced.** Every shipped artifact is a full
  `.nupkg`; delta validation is still an open TODO. Delta granularity is the
  stated reason Velopack was chosen at all, so it is unproven in-house.
- **Rollback has no code and no documentation.** The client would accept one; the
  version-validation script refuses to emit one.

**What UCC does prove:** the per-user `current\`-swap layout works in production;
a test seam of `virtual` network methods carries **48 hermetic update tests**; and
its restart choreography — cooperative shutdown with per-component acks, a 10 s
hard-kill backstop, log flush, *then* apply — is worth copying wholesale.

**Coverage of the update wrapper itself is zero tests**, which is exactly where
the feed-URL bug lived. **UCC is single-instance** via a named mutex, so
`force_stop_package` is harmless there — meaning the landmine that matters most
for concurrent registrations is **untested by the only prior art available**. No
signing: no certificate, no `--signParams`, package signature verification
unexplored.

## Not verified

MSI/PerMachine, signing, `--runtime win7`, `autoApply=true` with a staged
package, behaviour on older Windows, stdin inheritance through the stub.

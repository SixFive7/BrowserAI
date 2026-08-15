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

## Rollback

**Velopack prunes `packages\` down to the current full `.nupkg` and deltas are
forward-only**, so every rollback is a fresh full download (~105 MB here) unless
packages are archived by hand. `AllowVersionDowngrade` is the client half of
rollback.

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

**`.gitignore` verdict** (closes the deferred v1 item): `/Releases/` ✅ ·
`*-Portable.zip` ✅ · `/RELEASES` ✅ default channel only · **`Setup.exe` never
matches** — the real name is `{id}-{channel}-Setup.exe` · **`/payload/`,
`/staging/`, `/.staging/` are not vpk output at all**; they are BrowserAI's own
build conventions and must be justified on that basis or dropped.

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

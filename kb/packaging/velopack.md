<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Velopack and the update path

**Versions in force** unless an entry says otherwise: Velopack and `vpk` **1.2.158** since 2026-09-22 and **1.2.0** before it (0.0.1298 where an entry says so) · MinVer **8.0.0** since 2026-09-15 and **7.0.0** before it · .NET SDK 10.0.401, runtime and ILC 10.0.12 since 2026-09-14, and 10.0.400 and 10.0.11 before it · Windows 11 Pro 26200.
Measured on [the reference machine](../README.md#the-reference-machine).
*Corrected 2026-09-24 (previously "Velopack and `vpk` **1.2.0** (0.0.1298 where an entry says so) · MinVer **7.0.0** · .NET SDK 10.0.400, runtime and ILC 10.0.11")*: each was what the build resolved when the line was written, and each had moved since without the line moving with it. Read 2026-09-24 from the committed lock files, `dotnet tool list -g` and `dotnet --version`, and dated from the commits that moved the locks and from `global.json`'s own provenance note. An entry that names no version was taken under whatever this line said on the day it was written, so the older values stay beside the new ones. The four entries re-established on 2026-09-24 name 1.2.158 in place ([re-verification rows](../re-verification.md) 123, 124, 126 and 130).

Read from Velopack **1.2.0** and its Rust binaries unless noted. `[FLOATS]` --
this is a floating dependency like any other.

**Two passes are recorded here, merged and not stacked.** The first was read
out of Velopack's own sources and out of a working in-house deployment. The
second was a spike on **2026-08-15**: a NativeAOT app packed with `vpk`,
installed per-user, updated 1.0.0 → 1.0.1, rolled back, and uninstalled, against
a local feed server. Everything from that run is observed. Where the two
disagree, **the 2026-08-15 spike wins and the disagreement is stated inline** --
a claim that was quietly corrected reads identically to one that was never
wrong, which is the failure mode this whole file exists to prevent.

## What Velopack is, and the shape of the install

**Per-user install to `%LocalAppData%`, no elevation, MIT, no commercial tier.**
The same 1.2.0 release ships `lib-nodejs`, `lib-rust` and `lib-python`, and the
Rust `Update.exe` doing the real work is identical for all of them -- so the
update story does not by itself require C#. `--msi PerMachine` installs to
`Program Files` and makes the updater self-elevate, which a background stdio
server cannot answer.

**The delta scheme is per-file zstd `--patch-from`, and unchanged files collapse
to zero-byte markers.** `current\` is a real directory, not a junction, so the
executable path is stable across updates.

**Measured 2026-08-15: delta for an 8.76 MiB binary change is 3,210 bytes.**
Update wall time ~2.5 s, and **`current\` is absent for 1.7 ms** -- two
`fs::rename` calls, effectively atomic. Running instances are killed without
warning. `[FLOATS]`

## Where state may live -- the finding the provisioning design rests on

Spike 2026-08-15. `[FLOATS]`

⚠️ **BrowserAI stopped keeping state under the install root on 2026-09-15, and
the three caveats below are why.** The install root is
`%LocalAppData%\BrowserAI.app` -- Velopack derives it from the pack id and
nothing else, and the id is the only lever: there is no flag for the install
directory at 1.2.0 and an id may not carry a path. The data root is the constant
`%LocalAppData%\BrowserAI` beside it. So *siblings of `current\` survive an
update and a rollback* is still true and is no longer what the product relies
on: what it relies on is that a repair install and an uninstall cannot reach the
browsers, the session index or the log at all.

**`RootAppDir` is the directory containing `Update.exe` -- the parent of
`current\`.** Across update *and* rollback, a sibling directory accumulated all 7
hook stamps including the original install's, and a 5 MB payload file kept its
sha256 unchanged. **Siblings of `current\` survive both.** The
`AppContext.BaseDirectory` trap is real and was confirmed in the same run: files
written inside `current\` lost their pre-swap contents both times.

Three caveats that bear on the design, none of which the charter had:

- ⚠️ **A repair or overwrite install destroys them.** `install.rs` renames a
  non-empty root to `{root}.{random16}` and, on success, **deletes it**. Re-running
  `Setup.exe` over an existing install therefore costs a **207.3 MB re-download**
  (*corrected 2026-09-17 @ chromium 1244, previously "a **203.8 MB
  re-download**"*; the figure follows
  [the measured first-run download](../playwright/provisioning-and-timings.md#first-run-provisioning)
  and moves with every browser roll).
  **Updates must go through the update path; `Setup.exe` must never be re-run over
  an existing install.**
- **Uninstall wipes the whole root** (`remove_dir_contents`) -- browsers included,
  which is correct.
- Transient update space is `<root>\packages\VelopackTemp\`: same volume, outside
  `current\`.

⚠️ **`force_stop_package` will kill our browsers.** It matches by image **path**
under the root and runs on `apply`, `install`, `start`, `uninstall` **and after
every hook returns** (`windows/util.rs:59`). Two unrelated processes were killed
by an update launched from a third. Our browsers live under `RootAppDir`, so an
update terminates every running browser without warning and without our teardown.
Chromium survives hard kills and our locks release on process death, so the
damage is a lost session and not corruption -- **measured 2026-09-23 @
chromium 1246 (154.0.8037.0) on Windows 10.0.26200.9457, against a kill of
the shape `force_stop_package` performs and not against Velopack itself**: a
twelve-process tree terminated with `taskkill /T /F` left a profile that
reopened on the next launch with **all 19 of its SQLite stores reporting
`PRAGMA integrity_check = ok`**, and with the session gone -- 0 localStorage
keys and 0 cookies, against the 40 keys and the cookie that a graceful
`Browser.close` of the identical write hands straight back. **Our own claim
is an open handle and nothing else** (`FileAccess.ReadWrite,
FileShare.Read`, [`LockFile`](../../src/BrowserAI/Storage/LockFile.cs)): a
second writer refused while the holder lived took the file **68 ms** after
the holder was terminated outright, and the file outlived the holder while
the claim on it did not. ⚠️ **The clean `integrity_check` is worth exactly
the control behind it**: the same check reports *database disk image is
malformed* on four of five doctored copies of that very `Cookies` file and
**`ok` on the fifth** -- 1 KiB of garbage written into an unused region --
so *not corruption* here means *no corruption `integrity_check` can see*.
`[FLOATS]` on the browser build. But it bypasses the job object entirely,
and a hook must never leave a helper running under the root.

⚠️ ***Corrected 2026-09-24 @ Velopack 1.2.158, by addition (previously, and still
above, "runs on `apply`, `install`, `start`, `uninstall` **and after every hook
returns**").*** **At this release `start` calls it on one branch only**: while
migrating a legacy install whose versions live in `app-` folders
(`src/bins/src/commands/start_windows_impl.rs:146`), which a Velopack-native
install such as BrowserAI's never is. `apply` calls it after each hook and once
more immediately before the swap, and `install` and `uninstall` call it. Read at
tag `1.2.158`, commit `3c7f52c`, and measured the same day against real servers:
[what an apply does to every process under the root](#what-an-apply-does-to-every-process-under-the-root----read-and-measured-at-12158-2026-09-24).
Whether 1.2.0's `start` called it was not re-read; the kill the 2026-08-15 spike
observed was an update's.

## The nine landmines, claim and verdict

Each entry is the standing record first -- what was read out of Velopack and out
of the in-house deployment -- then what the 2026-08-15 install/update/rollback
spike found when it went looking. **Four are still real, three no longer apply,
one was wrong for 1.2.0, and one is fixed.**

### 1. The channel must not go in the feed URL

**`SimpleWebSource` composes the feed request as
`{BaseUrl}/releases.{channel}.json`.** A base URL built as `{BaseUrl}/{channel}`
therefore fetches `{BaseUrl}/{channel}/releases.{channel}.json` -- a 404, surfaced
as *"no update available"* and nothing else. The channel belongs in
`UpdateOptions.ExplicitChannel`. A local-directory source composes paths
differently and passes where production 404s.

> **Verdict, 2026-08-15: still real, consequence wrong.** 1.2.0 throws
> `HttpRequestException ... 404`. Not silent, not "unrecoverable in the field" --
> catchable, so a health check can detect it.

> **Qualified 2026-08-16: a 404 is not by itself a misconfiguration signal.** The
> verdict above is right that the 404 is catchable, but it leaves the impression
> that catching one is enough for a health check to conclude something is wrong.
> It is not -- **a legitimately empty channel returns the same 404.** Read from
> an in-house Velopack deployment's own troubleshooting notes, which list
> *"Empty channel: no releases published to that channel yet"* first among the
> causes and ships the discrimination as
> `catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)`
> → log at Debug, report "no update", re-arm the schedule. So the status code
> separates *404* from *transport failure*, and nothing more; distinguishing a
> misconfigured feed URL from an unpublished channel needs a second signal, such
> as whether any channel resolves. Consequence for us: a health check that alarms
> on 404 will cry wolf on every pre-release channel we have not yet published to.
> Read from source, not run. `[MACHINE]` for that deployment's code; the 404-on-empty
> behaviour is Velopack's and `[FLOATS]` with it.

### 2. `SetAutoApplyOnStartup(false)` is mandatory

**`SetAutoApplyOnStartup` defaults to `true`.** On finding a staged package,
`VelopackApp.Run()` applies it, `exit(0)`s and relaunches -- **with no inherited
stdio**, so an MCP client sees its server exit at handshake time.

> **Verdict, 2026-08-15: still real.** Default is `true`; the relaunch is
> detached.

### 3. Never register the execution stub

**The execution stub is compiled `#![windows_subsystem = "windows"]` and returns
immediately** without waiting, so a stdio client registered against the stub sees
the child die instantly with no pipes attached.

> **Verdict, 2026-08-15: still real, reason wrong.** "No pipes attached" is
> false -- stdio is inherited stub → `Update.exe` → app, and 3,220 bytes of app
> stdout arrived on the stub's pipe 12.9 s after the stub died. The killer is that
> the stub **exits in 59 ms** while the app runs on.

### 4. `force_stop_package` kills everything under the root

**`force_stop_package` kills every process under the install root** without
asking.

> **Verdict, 2026-08-15: still real, broader than stated.** It matches by image
> **path** and runs on `apply`, `install`, `start`, `uninstall` **and after every
> hook returns** -- see
> [the provisioning finding above](#where-state-may-live----the-finding-the-provisioning-design-rests-on).

> **Re-read 2026-09-24 @ Velopack 1.2.158: still real, and `start` is the one
> word that no longer holds.** It is called by `start` only on the legacy `app-`
> migration branch; the rest stands, and a measured apply, with twenty-one
> servers started before and during it, is in
> [what an apply does to every process under the root](#what-an-apply-does-to-every-process-under-the-root----read-and-measured-at-12158-2026-09-24).

### 5. Reading the installed version must not touch the network

**Constructing an `UpdateManager` merely to read the installed version issues a
network request.** `VelopackLocator` reads local metadata only.

> **Verdict, 2026-08-15: never applied to 1.2.0.** Ctor only assigns fields; 0 ms
> against an invalid host, zero requests logged. *New caveat:* `VelopackLocator`
> is not free -- it probes writability, **creates `packages\` and
> `packages\VelopackTemp`**, and opens a log file.

### 6. `NotInstalledException` under `dotnet run` and every test host

**`NotInstalledException` is the normal outcome under `dotnet run` and every test
host** -- neither is a Velopack install, so every Velopack call throws.
`Debugger.IsAttached` does not detect a test runner.

> **Verdict, 2026-08-15: wrong for 1.2.0.** `VelopackLocator.Current` and
> `new UpdateManager(url)` throw `InvalidOperationException: No VelopackLocator
> has been set`; `VelopackApp.Build().Run()` **succeeds**, warns, and leaves
> `IsInstalled == false`. The test seam is a **boolean**, not exception handling.

### 7. `ApplyUpdatesAndRestart(null)` as a bare restart

**`ApplyUpdatesAndRestart(null)` restarts without a package by undocumented
fall-through** -- the internals skip the `--package` argument when there is no
local full package. `UpdateExe.Start(waitPid)` is the supported restart.

> **Verdict, 2026-08-15: now documented.** Advice still right for a different
> reason: `toApply ?? GetLatestLocalFullPackage()` means null is "apply whatever
> is staged", not "just restart". ⚠️ **Charter code error:**
> `UpdateExe.Start(waitPid)` does not compile -- the first positional parameter is
> the locator.

### 8. `IVelopackLogger` needs two registrations

**`IVelopackLogger` takes two separate registrations** -- the runtime
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

## Two installs of one app id share one uninstall key -- measured 2026-09-14

**Measured 2026-09-14 @ Velopack 1.2.0**, on this machine, by accident. The app
id is `BrowserAI`, and Velopack writes **one** Add/Remove Programs entry per id
per user:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BrowserAI
```

⚠️ **The id moved to `BrowserAI.app` on 2026-09-15** (the pack-id rename that
chooses the install directory), so the key this product writes is now
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BrowserAI.app`.
**The measurement below is unchanged** -- it is about ids in Velopack's model, not
about this id -- and it has one incidental consequence: the
release candidate installed as `BrowserAI` and a v1.0.0 installed as
`BrowserAI.app` are two ids, so they no longer share an entry. The suite's own
real-installer arm refuses to run at all when a key for its id already exists
(`ReleaseLayout.ARealInstallIsRegistered`), because `--installto` would repoint
that entry and its uninstall would delete it.

**The key is named for the id and not for the location.** A probe installed a
second BrowserAI into `C:\Users\jori\AppData\Local\BrowserAI-probe-ed175a83`
with `--installto`, and the entry that had pointed at the release candidate in
`AppData\Local\BrowserAI-rc` since 2026-09-06 -- `InstallLocation`,
`DisplayIcon`, `UninstallString` and `QuietUninstallString` all naming that root
-- was rewritten to point at the probe. **Then the probe's uninstall DELETED the
key**, and with it the only Add/Remove entry the release candidate had ever had.
The probe had exported the key first and restored it byte-identical afterwards,
so nothing was actually lost; the export had been taken as a precaution against
a different risk, and this is the one it caught. Evidence:
[`docs/evidence/2026-09-14-firstrun/`](../../docs/evidence/2026-09-14-firstrun/README.md)
-- `arp-before.reg` and `arp-after.reg`, identical after the restore, and
`probe-root.txt`.

⚠️ **So two installs of one app id per user are not a supported shape**, and
nothing in the installer says so. `--installto` changes where the files land and
nothing else: the id is a global name in Velopack's model, the second install
does not notice that the name is taken, and the *first* uninstall to run removes
the entry for both. The files under the other root are untouched -- what is lost
is the machine's only record of how to remove them.

**What is NOT affected, and the distinction matters.** The update lane preserves
the id by construction, so install-then-update is one install throughout and
never meets this. What meets it is exactly what a maintainer does when testing a
release candidate beside a real install, and what a user does if they ever
install a second copy somewhere else.

**How to re-establish.** Export the key above. Install a second copy with
`--installto <scratch>`, re-read it -- `InstallLocation` will name the scratch
root -- and then uninstall the second copy and ask for the key again: it is gone.
Restore from the export. **Take the export first and verify the restore by
hashing both**: without it this measurement removes a real install's uninstall
entry, and the measurement is the damage. `[FLOATS]`

**Re-established 2026-09-24 @ Velopack 1.2.158, and UNCHANGED, in both orders,
under the suite's test id `BrowserAI.app.test`.** A second silent install into
another scratch root rewrote `InstallLocation`, `DisplayIcon`, `UninstallString`
and `QuietUninstallString` to name it; uninstalling either root deleted the key
while the other root still held a complete install, including the root the key
did not name; and the uninstall that then found no key logged
`Unable to remove uninstall registry entry (The system cannot find the file
specified. (os error 2)).` and exited **0**. Read at the 1.2.158 tag:
`registry.rs:40` creates the subkey from `get_manifest_id()` alone, and
`registry.rs:65` is an unconditional `delete_subkey_all(&app_id)`, reached from
`uninstall.rs:38`. The real `BrowserAI.app` key hashed the same before and after
every step. **The method above is superseded, and kept because it is what the
2026-09-14 reading was taken with**: the fact is about ids, so the test id
measures it without writing the real key at all, and nothing needs exporting or
restoring. Point
`CLAUDE_CONFIG_DIR`, `CODEX_HOME` and `BROWSERAI_ROOT` at scratch first, because
the install hooks run ([re-verification row 123](../re-verification.md), [evidence](../../docs/evidence/2026-09-24-velopack-rows/README.md)).

## The restart handover race, and why `Update.exe` is the answer

Read 2026-08-16 from the application-restart notes of a shipping in-house
Velopack deployment, which document both wrong answers as well as the right one.
Not run here. `[STABLE]`
for the race; `[FLOATS]` for the API surface.

The pieces are already elsewhere in this article -- `UpdateExe.Start(waitPid)` in
[landmine 7](#7-applyupdatesandrestartnull-as-a-bare-restart), `force_stop_package`
in [landmine 4](#4-force_stop_package-kills-everything-under-the-root), and that deployment
being single-instance via a named mutex in
[Prior art](#prior-art-one-shipping-velopack-deployment) -- but they are never joined into the race
itself, which is the part that bites:

- **Spawn the new instance, then exit.** The new process checks the mutex while
  the old one still holds it, concludes another instance is running, takes the
  secondary path -- there, sending a `TOGGLE` over the pipe -- and exits. *"Result:
  App hides instead of restarting."*
- **Release the mutex first, then spawn.** Now there is a window in which no
  instance holds it, and any unrelated launch in that window -- a Start-menu click
  -- becomes primary. *"Result: Wrong instance becomes primary."*

There is no ordering of "release" and "spawn" that closes both, because the
process doing the handover is one of the two parties to it. **`Update.exe`
resolves it by being neither:** it is an external Rust binary that outlives the
app, receives the PID via `waitPid`, waits for *actual process death* and not for
a close request, and only then launches. The mutex is released by the OS at
termination, which is strictly before the new process starts.

Two consequences:

- **The signature is `Start(IVelopackLocator locator = null, uint waitPid = 0,
  string[] startArgs = null)`** -- first positional parameter is the locator,
  confirming the ⚠️ charter code error already recorded in
  [landmine 7](#7-applyupdatesandrestartnull-as-a-bare-restart) from an
  independent source.
- **The update path must not double-restart.** `ApplyUpdatesAndRestart` restarts
  on its own (`WaitExitThenApplyUpdates(..., restart: true, ...)` then
  `Environment.Exit(0)`), so calling `UpdateExe.Start` as well produces two
  relaunches. Its fix is to route the update case through the same cooperative
  shutdown but with `IsRestart: false`.

**Where this stops applying to us:** the whole race is about a *single-instance*
app coordinating with its own successor. BrowserAI is a stdio child spawned per
client, so it has no mutex to hand over -- but it does inherit the harder half,
because the relaunched process
[does not inherit the caller's stdio](#rollback) either way.

## Rollback

**Velopack prunes `packages\` down to the current full `.nupkg` and deltas are
forward-only**, so every rollback is a fresh full download unless packages are
archived by hand. `AllowVersionDowngrade` is the client half of rollback.

> ✅ **Answered 2026-08-16 by the first real `vpk pack`: the full package is
> 49,043,498 b (46.8 MiB).** See
> [The update lane](#the-update-lane-end-to-end-against-a-real-feed) for the whole set. The
> question and the refusal to guess are kept below because the number that was
> circulating was invented, and a reader who remembers *"~105 MB"* has to be able
> to find out where it went.
>
> **How big is a full BrowserAI package? `[UNVERIFIED]`, and it was stated as
> ~105 MB with no provenance anywhere in the repository.** Nothing has ever
> compressed the real payload. What is known: the payload is
> [~117 MB installed](../playwright/provisioning-and-timings.md#component-sizes),
> `node.exe` is 88.53 MB of it, and the only compression figure on record --
> ~806 MB → ~239 MB, 7z LZMA2 `-mx=5` -- is for the browser-dominated tree that no
> longer ships. The spike's own full nupkg was **6,072,200 b**, but that was a
> payload-free test app and predicts nothing here. Measure it at the first real
> `vpk pack`; until then, no number.

**Measured 2026-08-15.** Rollback needs `AllowVersionDowngrade = true` (default
false yields "no updates", silently) and then forces a **full re-download** --
6,072,200 b, zero deltas -- because `packages\` was pruned to the new full nupkg
during the forward update. Rollback fires the same obsolete/updated/restarted
hooks; from the app's view it is an ordinary update. `restartArgs` pass through,
but **the relaunched app does not inherit the caller's stdio.** `[FLOATS]`

## The update lane, end to end against a real feed

Everything here was run while building
the Velopack update lane,
against **Velopack 1.2.0** and **`vpk` 1.2.0**, on Windows 11 Pro 26200, SDK
10.0.302. It is the first time this project has packed, installed, updated or
rolled back its own payload and not a test app. `[FLOATS]` -- every number
moves with Node, `@playwright/mcp` and the toolchain.

**Re-establish the whole set** with
`pwsh -File build/New-Release.ps1 -PackVersion <v> -OutputDir <feed>`, twice at
two versions, then install the first `Setup.exe` with
`--silent --installto <scratch>` and run
`<scratch>\current\BrowserAI.Server.exe` with `BROWSERAI_UPDATE_FEED` pointed at
the feed directory. ⚠️ *Corrected 2026-09-16 (previously
`<scratch>\current\BrowserAI.exe`)* -- since the two-binary split of 2026-09-15
that name is the configuration app, which has no update lane of its own to
exercise and simply opens a window; the server is what reads
`BROWSERAI_UPDATE_FEED` and runs the pass this section measures. **Never install into `%LocalAppData%\BrowserAI`** -- see the
repair-install finding above. *Re-stated 2026-09-15: that directory is the DATA
root now, and the default install location is `%LocalAppData%\BrowserAI.app`, so
the accident this sentence warns about is no longer reachable by default -- only
by typing `--installto` and naming the data root, which would hand the whole of
it to `install.rs`'s rename-and-delete.*

> ✅ **RUN 2026-09-17, and the staleness is cleared.** The two publishes this
> notice was waiting for were taken -- `1.0.1-reverify.1` and
> `1.0.1-reverify.2` into a scratch feed -- and
> [the sizes table](#sizes) below now carries measured figures with a
> `previously` clause naming every one it replaced. **The `[STALE]` marker is
> removed from this entry**, which is one edit with the sentence in
> [`kb/README.md`](../README.md) that publishes how many articles carry one.
> The notice itself is kept below, unedited, because it is the account of what
> was owed and for how long -- three renewals across three weeks -- and deleting
> it would take the record of the gap with it.
>
> ⚠️ **One clause of this section is still NOT re-established, and it is named
> instead of left inside a cleared notice:** *what Velopack costs the AOT
> binary, 11,874,816 b → 17,853,952 b*. That pair is a publish taken **either
> side of adding the package**, and `Velopack` is now referenced by three
> projects and wired into the update lane, the hooks and the restart handover --
> so producing the "before" half means building a tree with the update lane
> removed, which is a different product, not a different measurement. It
> stays `[STALE]` on its own, in place, with that as the reason.
>
> ---
>
> ⚠️ **What the notice said until 2026-09-17, kept because it is the record of
> the gap:** `[STALE]` **A re-measurement of this whole set is OWED as of
> 2026-08-27, was OWED AGAIN on 2026-09-14, and HAS NOT BEEN RUN either time.**
> The
> [Node upstream review](../../upstream-review.json) of 2026-08-27 adopted
> **v24.19.0 → v24.20.0**, and `node.exe` grew **92,825,416 → 93,381,448
> bytes**, `+556,032`. That is precisely the trigger
> [row 85](../re-verification.md) names -- *"Node moves (it is 92,825,416 b of
> 130,434,486)"* -- so every figure below that includes the payload is now a
> number measured against a payload this repository no longer builds.
>
> **The 2026-09-14 review moved it a second time, and moved the other half of
> the payload too.** Adopting **v24.20.0 → v24.21.0** with
> **`@playwright/mcp` 0.0.79 → 0.0.80** took `node.exe` to **93,580,104 bytes**
> (`+198,656` on the day, `+754,688` since the figures below were taken) and the
> `payload\mcp` tree the other way, **18,997,245 → 18,623,990** (`-373,255`) --
> both read off `payload/payload.json`, which the payload build writes from the
> files it just produced. The published `BrowserAI.exe` is **19,186,688 bytes**
> against the **17,853,952** recorded below, which is a third input moving. **So
> the gap is now three-sided and wider, not narrower**, and the table below is
> further from true than when it was first marked.
>
> **Still not one figure has been adjusted, and the arithmetic is even less
> available than it was**: the two payload halves moved in *opposite*
> directions, so even a reader tempted to add a delta has no single delta to
> add.
>
> **Not one of them has been adjusted, and none may be.** The new shipped total
> is not 130,434,486 plus 556,032: `vpk` recompresses, the delta is computed
> against a different full package, and `Setup.exe` and the ratio are outputs of
> that compression, not sums. Arithmetic here would produce a
> measured-looking number nobody measured, which is the one edit
> [the kb conventions](../README.md) forbid outright.
>
> **Why it was not re-run, and not why it should be:** the re-establishment
> above is two real release publishes plus an install, the row itself says
> *manual and must be* because the suite may neither publish nor install, and
> the batch that took the Node bump was a review-and-records batch with no
> release in it. The figures stay exactly as they were measured, carrying this
> notice, until somebody runs the procedure above.
>
> ✅ **Stamped 2026-08-29** -- *previously "**The staleness marker is what this
> paragraph would otherwise be**, and it is still not stamped here -- but that is
> now a choice rather than a refusal by the build, and the difference is the
> whole of the correction below."* The choice is taken: the marker is at the head
> of this notice, where a reader meets it before the figures and not after
> them. **It is one edit with the sentence in [`kb/README.md`](../README.md)**,
> which now publishes *one* article carrying a stamp instead of none -- neither
> half passes alone, which is the pairing the correction below built. **Nothing
> below was adjusted and nothing was re-measured**: the stamp is a statement
> about what is owed, not a substitute for running it.
>
> ✅ **Both guard defects closed 2026-08-27.**
> `RecordedCountTests.TheStaleMarkerCountInTheArticleIndexIsWhatTheArticlesHold`
> -- *`Corrected 2026-08-27 (previously
> "RecordedCountTests.NoKnowledgeBaseArticleCarriesAStaleMarker")`* -- matches the
> **backticked** marker and not the bare token, so an article may spell the
> bracketed form in prose without turning the suite red; and it holds the count
> against the sentence in [`kb/README.md`](../README.md) and not against zero,
> so stamping an entry and moving that sentence pass **as a pair**. The
> resolution its message names is now one the assertion permits, which it was not
> before. Both narrowings were watched red and green in both directions before
> they went in.
>
> ⚠️ **What this paragraph said until then, because it is why the wording above it
> is what it is.** *Previously: the marker "is deliberately not stamped here -- nor
> is it spelled in brackets anywhere above, which is the second half of the same
> problem", because the guard matched the token itself and so could not tell a
> stamp from a mention, and because its assertion was unconditional while its
> failure message named a resolution it forbade.* That is the trap
> [the re-verification index](../re-verification.md) had already met for the
> floats marker and answered by narrowing the counter's **scope**; scope could not
> answer it here, because the article that has to discuss this marker is this one,
> full of real measurements. The narrowing is by **shape** instead. The
> say-it-in-words workaround above is left standing and not rewritten: it is
> still an accurate account of the figures, and re-cutting a paragraph to use a
> mechanism the day the mechanism arrives is how a document starts being written
> for the build.

### How often the feed is asked, and by what -- measured 2026-09-22

**One request per server start of an INSTALLED, non-pre-release BrowserAI, and
none at all from anything else.** There is no timer, no interval and no retry:
`UpdateService.StartInBackground` calls `RunOnceAsync` once and the type holds no
`PeriodicTimer` of any kind. The question was asked by the maintainer as *"is
there not a risk of flooding the release page with update checks if an agent
starts 100+ browsers in sustained bursts?"* -- and the answer is that browsers are
not what asks.

**Three guards decide it, and two of them are BrowserAI's own** -- read out of
`src/BrowserAI.Core/Updates/UpdateService.cs` on 2026-09-22, not inferred:

| Guard | Where | What it does |
|---|---|---|
| `if (!isInstalled) return;` | `UpdateService.StartInBackground` | Returns **before any network call**, writing `UpdateLog.NotAnInstall`. This is what a checkout, a `dotnet run`, every test host and every suite-started server look like |
| `if (BuildVersion.HasPreReleaseSuffix(build)) return;` | the line below it | *Never self-update from a build that is not a release.* An installed pre-release checks nothing either |
| `VelopackLocator.Current.CurrentlyInstalledVersion is null` | `InstallLocation.Locate` | Supplies the predicate the first guard reads. Velopack answers the question; **BrowserAI performs the skip** |

⚠️ **THE MECHANISM IS OURS, NOT VELOPACK'S, AND THAT CORRECTS WHAT WAS WRITTEN
DOWN.** *The ledger entry of 2026-09-22 ~04:00 recorded this as an inference --
"the mechanism keeping suite servers off the feed is inferred (Velopack's
not-installed detection: the `[17] "Installed at"` line appears only for the
installed binary), not read this turn".* Read this turn, it is a plain early
return in this repository's own code, above Velopack entirely. The inference
pointed at the wrong component and happened to reach the right conclusion, which
is the shape a confident wrong answer takes.

**A session is not a check, and a browser is certainly not.** Sessions and
browsers are children of one server per client connection, so 100 browsers inside
one session cost **zero** extra requests. The only things that ask are: a server
start of an installed release build, the configuration app's own *Check for
updates* button (`ConfigurationDialog.Command.CheckForUpdates`, one request per
click), and the release chain's own post-publish polls.

**The monitor is the asset's own download counter, and it discriminates.** Read
from `gh release view v1.0.0` on 2026-09-22: `releases.win.json` **48**, against
**1** for every other asset on the release -- `BrowserAI.exe`, `BrowserAI.zip`,
`BrowserAI.app-1.0.0-full.nupkg`, `RELEASES`, `assets.win.json` and the manifest
zip. *(47 earlier the same day, so one further check landed in about nine hours --
one server start.)* Those 48 requests stand against **thousands** of server
processes the suite started in the same window -- the saturation arm alone starts
100 per run -- which is the measurement that says non-installed binaries really do
ask for nothing. **And the flat `1` on the package assets says something else
too: no install anywhere has ever applied an update, because nothing has
ever fetched a package.**

⚠️ **RE-READ 2026-09-23 ON `v1.1.0`, AND THE DISCRIMINATION HOLDS ON A
SECOND RELEASE.** Read immediately before the asset trim below:
`releases.win.json` **11**, `BrowserAI.app-1.1.0-full.nupkg` **2**, and **0** for
each of `BrowserAI.exe`, `BrowserAI.zip`, `RELEASES`, `assets.win.json` and the
manifest zip. **The `2` on the package is the first non-zero package counter this
project has ever recorded**, so *"nothing has ever fetched a package"* is still
true of `v1.0.0` and is no longer true of the product. At least one of the two is
the maintainer's own install staging 1.1.0, which was observed the same day and
matched the published asset to the byte; **what the second one is has not been
established** and the counters cannot say. `v1.0.0`'s counters the same minute,
for comparison and untouched:
`releases.win.json` **102** against **1** for every other asset and **2** for
`BrowserAI.exe`.

⚠️ **AND `v1.1.0` NOW CARRIES THREE ASSETS, NOT SEVEN -- 2026-09-23,
at the maintainer's word (*"Q234 b"*).** `BrowserAI.zip`, `RELEASES`,
`assets.win.json` and `BrowserAI-1.1.0-manifest.zip` were deleted from the
published release with `gh release delete-asset`, one at a time, leaving exactly
the [declared upload set](../../RELEASING.md#what-a-release-publishes):
`BrowserAI.exe`, `BrowserAI.app-1.1.0-full.nupkg` and `releases.win.json`.
**Four of the four deleted had a download counter of `0`.** Verified immediately
afterwards: `releases/latest` still resolves to `v1.1.0`,
`releases/latest/download/releases.win.json` still answers **200** with the one
`Full` row for 55,022,716 b, and the installer alias still answers. **`v1.0.0` is
untouched and still carries all seven**, which is the control that says the
deletion was scoped to one release -- and what a reader comparing the two releases
is looking at.

**The pathological case, named and not defended against.** A client that
spawns a fresh server process per task, in bursts, produces one 260-byte
conditional GET per start. The CDN does not care, GitHub documents no rate limit
on release-asset downloads (abuse detection aside), and what actually degrades is
the download counter's value as a monitor -- cosmetically, by inflation.
**Q225 b**, the maintainer's answer, is to leave the per-start check exactly as it
is and document it: *"Q225 b"*. The alternative offered and declined was a
machine-wide check stamp in the shared data root, skipping the feed when any
instance of this install had checked within the hour.

### Nothing anywhere reads `RELEASES` or `assets.{channel}.json` from a release -- measured 2026-09-23

**Measured 2026-09-23 @ `vpk` 1.2.158, Velopack 1.2.158, against the files the
published `v1.1.0` release carries.** `[FLOATS]` -- it is a property of Velopack's
sources and moves when they do.

**The question.** `vpk pack` writes three feed files beside the packages and a
release had been uploading all three. `releases.{channel}.json` is the one
[`UpdateFeed`](../../src/BrowserAI.Core/Updates/UpdateFeed.cs) composes and the
one this product asks for; what nobody had established is whether the **Velopack
library inside the product, or any other part of the release chain**, reads the
other two from a release.

**It does not, and the control is the strong one.** Four `vpk download local`
runs, which drive `SimpleFileSource` -- the same `IUpdateSource` an
`UpdateManager` uses, reached through the CLI so that no new binary has to exist:

| Feed held | Result |
|---|---|
| all four files | `Found 1 release(s)`, downloaded **55,022,716** b, checksum verified |
| **`RELEASES` and `assets.win.json` deleted** | identical -- `Found 1 release(s)`, same bytes, **not one message about either file** |
| `releases.win.json` deleted, package present | ⚠️ **a WARNING naming `releases.win.json` by path**, then a fallback scan of `*.nupkg` that succeeds |
| ⭐ **`RELEASES` and `assets.win.json` present, `releases.win.json` and every `.nupkg` absent** | `Found 0 release(s)` · *"No full / applicable release was found to download. Aborting."* |

**The fourth row is the measurement.** `RELEASES` in that feed reads
`23FB729B035F39D30FD6E045965593E61DCE0ACC BrowserAI.app-1.1.0-full.nupkg 55022716`
-- the package, its SHA-1 and its exact size -- and `assets.win.json` lists all
three artifacts by name and type. Velopack found **nothing**. Neither file is a
feed to it.

⚠️ **The third row is why the obvious control does not work on a LOCAL feed.**
`SimpleFileSource` falls back to scanning the directory for `*.nupkg` when the
JSON is missing, so deleting `releases.win.json` alone is not a refusal -- the
packages have to go with it. `SimpleWebSource` has no such fallback, which is
the difference [row 217 of the hazard index](../../HAZARDS.md#hazard-index)
records for a different reason.

**The suite half, against a scratch feed.** `Releases/` copied to
`.work/feed-measure/suite-feed` with `BROWSERAI_RELEASE_FEED` pointed at it,
`RELEASES` and `assets.win.json` deleted from the root **and** from
`test-pack/`: `RealInstallerTests`, `UpdateTests` and `ReleaseScriptTests` ran
**57 of 57 green, 0 skipped**, with the coverage block reading
`release installer PRESENT` at the doctored feed -- so the three arms that install
a real pack twice over one root, launch the installed binary and uninstall it all
ran, and none of them missed either file.

**The positive control names the file that IS read.** Same feed with both legacy
files RESTORED and `test-pack/releases.win.json` deleted, under
`BROWSERAI_RELEASE_RUN=1`: all three `ReleaseInstaller`-gated arms **failed**,
each saying *"`BrowserAI.test-installer.exe` exists but `releases.win.json` names
no package with the id `BrowserAI.app.test`"*. So the arms are the ones that
would have failed, and what they fail on is the JSON.

**What the source says, read at tag `1.2.158` (sha `3c7f52c1`).** Every client
source reads `releases.{channel}.json` and only that -- `SimpleWebSource.cs:43`,
`SimpleFileSource.cs:37`, `GitBase.cs:82`, Rust `sources/http.rs:39` -- and
`UpdateManager`'s check, download, delta and rollback paths
(`UpdateManager.cs:240-315`) touch nothing else. **Nothing in `lib-csharp`
outside the definition of `GetReleasesFileName` ever composes the name
`RELEASES`**, so the client library cannot read it. `Update.exe` and `Setup.exe`
never fetch a feed at all. `RELEASES` is a Squirrel-migration shim --
`ReleaseEntryHelper.cs:115`, *"We write a legacy RELEASES file to allow older
applications to update to velopack"* -- and this product has no Squirrel
predecessor: `git grep -ni squirrel` over `src/`, `build/` and `tests/` is empty.

⚠️ **`assets.{channel}.json` MUST STAY ON DISK and is inert once published.** It
is the pack-to-upload hand-off: `vpk upload github` reads it with
`BuildAssets.Read` from the **local** `Releases/` directory (`_GitRelease.cs:154`)
to learn what to upload, and a future `vpk upload` fails without it. What it is
not is something anybody fetches from a release page. `vpk pack`'s own
existing-release detection does not read it either -- it enumerates `*.nupkg` on
disk (`ReleaseEntryHelper.cs:30-42`).

⚠️ **One upstream comment says the opposite and is stale in both languages.**
`SimpleWebSource.cs:12` and `lib-rust/src/sources/http.rs:11` say the source
requests `{baseUri}/RELEASES`; the code three lines below reads the JSON. Nothing
in this repository repeats that sentence -- checked, 2026-09-23 -- and it is
recorded here so that a reader who meets the comment upstream does not believe
it.

**How to re-establish it.** Copy `Releases/`'s four feed files into four scratch
directories, delete a different combination from each, and run
`vpk download local --path <dir> -o <out> -c win` against each; the fourth
combination -- both legacy files present, the JSON and every `.nupkg` absent -- is
the one that has to come back empty. For the suite half, copy `Releases/` without
`archive/`, delete the two files at both levels, point `BROWSERAI_RELEASE_FEED`
at the copy and run `RealInstallerTests`; then restore them, delete
`test-pack/releases.win.json` and run it again under `BROWSERAI_RELEASE_RUN=1`
for the control. **The clearance conditions apply around every run that installs**
-- `RealInstallerTests` runs a real `Setup.exe` -- and they were byte-identical
either side of both runs here.

### Sizes

**Re-measured 2026-09-22** by running `build/New-Release.ps1` twice, at
`1.0.1-reverify.3` and `1.0.1-reverify.4`, into a scratch feed -- the procedure
[row 85](../re-verification.md) names -- against Velopack and `vpk` **1.2.0**,
node **v24.21.0**, `@playwright/mcp` **0.0.82** and `playwright-core`
**1.64.0-alpha-1789764292000**. *(Everything here floats, and it is stamped once
at the head of this section and not again on this table: that marker already
says every number in the section moves with Node, `@playwright/mcp` and the
toolchain, and a second stamp on the same cluster would add an obligation
without adding a fact. Written in words because the counter reads the token and
cannot tell a mention from a stamp.)*

| | Bytes | Note |
|---|---|---|
| Publish directory on disk | 262,441,359 | Includes the `.pdb`s, which `vpk` excludes by default |
| **What ships** (pdb excluded) | **143,715,555** | 206 files. `payload\node` 93,740,659 · `BrowserAI.Server.exe` 19,266,048 · `payload\mcp` 18,697,570 · `BrowserAI.exe` 10,412,544 · the three `.xml` 1,559,174 · notices 26,240 · `payload` other 13,320 |
| **Full `.nupkg`** | **54,981,749** | 52.4 MiB. Compression ratio **0.3826** |
| **Delta `.nupkg`, N→N+1** | **138,943** | **0.2527% of the full package -- a 396× reduction** |
| `Setup.exe` | 59,490,421 | The download, renamed from `BrowserAI.app-win-Setup.exe` |
| `-Portable.zip` | 54,943,009 | |

> ⚠️ `Corrected 2026-09-22 @ Velopack 1.2.0 · node v24.21.0 ·
> @playwright/mcp 0.0.82 · playwright-core 1.64.0-alpha-1789764292000
> (previously "Publish directory on disk 262,007,766 ... **What ships** (pdb
> excluded) **143,503,406** -- `payload\node` 93,740,659 ·
> `BrowserAI.Server.exe` 19,202,560 · `payload\mcp` 18,619,618 ·
> `BrowserAI.exe` 10,411,520 · the three `.xml` 1,497,348 · notices 22,610 ·
> `payload` other 9,091 ... **Full `.nupkg`** **54,926,688** ... ratio **0.3828**
> ... **Delta** **138,515** ... **0.2522%** ... `Setup.exe` 59,435,360 ...
> `-Portable.zip` 54,887,948", measured 2026-09-17 at `1.0.1-reverify.1` and
> `.2`)`. **This clears the `[STALE]` renewed on 2026-09-21, and the trigger was
> the one row 85 names.**
>
> **WHAT MOVED, AND `payload\mcp` IS THE ONLY INPUT THAT MOVED FOR A REASON
> ANYBODY CHOSE.** `@playwright/mcp` 0.0.81 → 0.0.82 took the vendored JS tree
> **18,619,618 → 18,697,570** (`+77,952`) across 194 → 196 files. **Node did
> not move at all** -- `payload\node` is 93,740,659 in both tables, to the byte,
> because v24.21.0 is unchanged. Everything else is a rebuild: the server exe
> `+63,488`, the app exe `+1,024`, the three `.xml` `+61,826`, the notices
> `+3,630` (the 2026-09-18 rewrite that named `playwright` and both browser
> families) and `payload` other `+4,229`.
>
> ⭐ **THREE ARTIFACTS MOVED BY EXACTLY THE SAME NUMBER, +55,061 B**, and it is
> recorded because it is the kind of coincidence a reader should be able to check
> instead of wonder about: full `.nupkg` 54,926,688 → 54,981,749,
> `Setup.exe` 59,435,360 → 59,490,421 and `-Portable.zip` 54,887,948 →
> 54,943,009. `Setup.exe` carries the full package and the portable zip is the
> same content, so all three move with the one compression.
>
> ⚠️ **THE DELTA IS A FLOOR AND NOT A COST, and this run makes that
> visible for the first time.** `.3` and `.4` are packs of the **same source
> tree** -- nothing was edited between them -- and their full packages differ by
> **6 bytes** (54,981,755 against 54,981,749). So the 138,943 b delta is what
> Velopack charges for two packs that differ only by a rebuild: NativeAOT output
> is not byte-reproducible run to run, both binaries are re-published by each
> pack, and the delta carries both. **It is not the cost of a change**, and the
> previous table's 138,515 was the same quantity. The row's predicate note --
> *"for a release in which **both** binaries changed, against 97,216 b for one"*
> -- is exactly this and stands unaltered.
>
> ⭐ **A CROSS-CHECK THAT COST NOTHING AND RECONCILES TWO FILES**:
> `payload/payload.json` records `node.bytes` **93,580,104** and `npm.bytes`
> **18,697,570**. The publish tree's `payload\mcp` agrees to the byte; its
> `payload\node` is **93,740,659**, and the difference is exactly the
> **160,555 B** `LICENSE` that
> [the licensing read](dependencies.md#third-party-payload-as-shipped)
> enumerates -- so `node.bytes` is `node.exe` alone and the two files are
> measuring different things on purpose.

> ⚠️ `Corrected 2026-09-17 @ Velopack 1.2.0 · node v24.21.0 · @playwright/mcp
> 0.0.81 (previously "Publish directory on disk 206,427,574 ... **What ships**
> (pdb excluded) **130,434,486** -- `BrowserAI.exe` 17,853,952 · `payload\node`
> 92,985,968 · `payload\mcp` 18,997,245 · `BrowserAI.xml` 596,517 ... **Full
> `.nupkg`** **49,043,498** ... Compression ratio **0.376** ... **Delta `.nupkg`,
> N→N+1** **97,216** ... **0.198% of the full package -- a 504× reduction** ...
> `Setup.exe` 53,505,061 ... `-Portable.zip` 49,042,468")`. **This closes the
> `[STALE]` notice at the head of this section, which had stood since 2026-08-27
> and been renewed twice without being run.**
>
> **What moved, and it is four inputs and not one:**
>
> - **The two-binary split of 2026-09-15 is the biggest of them.** The old table
>   is about a single 17,853,952 b `BrowserAI.exe`; what ships now is
>   `BrowserAI.Server.exe` **19,202,560** *and* `BrowserAI.exe` **10,411,520** --
>   **29,614,080 b of AOT binary where there was 17,853,952**, `+11,760,128`.
> - **Node** 92,985,968 → 93,740,659 as measured in the publish tree
>   (`+754,691`), across the v24.19.0 → v24.21.0 moves the stale notice named.
> - **`payload\mcp` went the other way**, 18,997,245 → 18,619,618 (`-377,627`),
>   which is why the notice was right that no single delta could be added.
> - **The XML documentation trebled**, 596,517 → 1,497,348 across three files,
>   because the split gave `BrowserAI.Core` and `BrowserAI.Server` their own.
>
> **The delta row's PREDICATE changed and the number must be read with it.** The
> old 97,216 b was for *a release in which only `BrowserAI.exe` changed*. These
> two packs were cut from the same tree with nothing altered but the version
> string, so **both** AOT binaries were rebuilt and differ -- 29.6 MB of changed
> input against the old 17.9 MB. **138,515 b for twice as much changed binary is
> the delta lane working**, and the 396× reduction against the old 504× is a
> larger absolute delta against a larger full package and not a regression
> in compression.

> ✅ **Corroborated against the real v1.0.0 cut the day before, which nobody
> planned as a control.** `Releases\BrowserAI.app-1.0.0-full.nupkg` is
> **54,926,948 b** and its `Setup.exe` **59,435,620 b**, against this
> measurement's 54,926,688 and 59,435,360 -- **260 bytes apart in both**. A
> re-measurement that landed within 5 ppm of a real release cut from the same
> tree is measuring the shipped artifact and not a rig. *What accounts for
> the 260 bytes was not established* -- the version strings differ in length and
> appear in several places, which is the obvious candidate and is not the same
> thing as a measurement.

⚠️ **Take the ratio against what ships, not against the publish directory.** The
`.pdb`s are 118,504,360 b of a 262 MB directory, so the naive ratio reads 0.2096
for what is really 0.3828. The first run of the release script reported the
wrong one. *`Corrected 2026-09-17 (previously "The `.pdb` is 76 MB of a 206 MB
directory, so the naive ratio reads 0.350 for what is really 0.376")` -- same
trap, re-derived at the current sizes.*

⚠️ **`payload\.cache\` was shipping, and it was 37,304,352 b of the package.**
`Build-Payload.ps1` keeps the downloaded Node archive there so a re-run does not
re-download it, and the publish glob in `src/BrowserAI/BrowserAI.csproj` took the
whole `payload\**\*` tree. **The full package was 85,348,009 b before the
exclusion and 49,043,498 b after** -- 42.5% of every release was an already-
compressed zip nobody reads at runtime. Nothing else showed it: the publish
succeeded, the suite passed, and the only symptom was a number that had never
been measured.

### The delta is real, and what it costs to produce one

⚠️ **NO RELEASE CARRIES ONE SINCE 2026-09-22, AND THIS SECTION IS NOW ABOUT A
ROAD NOT TAKEN.** *Added by addition; nothing below is re-measured or retracted.*
The maintainer's decision, verbatim: *"always produce full packages only. The
sizes are so small, and internet speeds nowadays are so fast that we don't want
to exert any effort in creating deltas. Full downloads are always just easier."*
`build/New-Release.ps1` passes `--delta None` from that day, so every published
feed holds `Full` rows alone. **The figures below stay measured and stay here**:
they are what the decision costs, they are what `vpk` still does when asked, and
they are the numbers anybody revisiting the decision would otherwise have to
re-establish. **`--delta None` is `vpk`'s own name for it** and was resolved from
the tool, not from memory: `vpk pack --help` documents `--delta <MODE>`
without enumerating the modes, and handing it an unparseable value makes it name
them. `[FLOATS]`

⚠️ `Corrected 2026-09-17 (previously "**`BrowserAI-0.9.1-delta.nupkg` is 97,216 b
against a 49,043,493 b full package**, for a release in which only
`BrowserAI.exe` changed")` -- **re-measured as 138,515 b against a 54,926,688 b
full package**, for a release in which **both** AOT binaries changed, which is
the predicate two packs cut from one tree at two versions produce. The
paragraph below is kept as written because what it argues -- that the delta lane
really produces a delta, and that the receiving end really applies it -- is
unchanged, and only its two numbers moved. **The `deltas=1` confirmation on the
client was NOT re-run on 2026-09-17**: these two packs were never installed, so
what is re-measured is the production of a delta and not its application.

**`BrowserAI-0.9.1-delta.nupkg` is 97,216 b against a 49,043,493 b full
package**, for a release in which only `BrowserAI.exe` changed. That is the
claim §G bought Velopack for, and the one shipping deployment available has never produced one in
production. Confirmed on the receiving end as well: the client logged
`deltas=1` and applied it.

⚠️ **A delta-reconstructed full package is not byte-identical to the published
one.** After applying the delta, `packages\BrowserAI-0.9.1-full.nupkg` was
**49,043,340 b** against the feed's **49,043,493 b** -- the client rebuilds and
recompresses instead of downloading. It is verified by hash against the
manifest's own recorded checksum for the reconstruction, not against the
published file, so this is not a defect; it does mean **a size comparison
between `packages\` and the feed proves nothing.**

### Install → update → rollback, end to end

| Step | Result |
|---|---|
| `Setup.exe --silent --installto <scratch>` | Exit 0. `<root>\{BrowserAI.exe, Update.exe, current\, packages\}` |
| Stub vs. real binary | **392,704 b** at the root against **17,853,952 b** in `current\` -- landmine 3 made visible: the stub is what a registration must never name |
| `current\sq.version` | `<version>0.9.0`, `<channel>win`, `<mainExe>BrowserAI.exe`, `<shortcutLocations>None` -- ⚠️ **read on the day and still what that pack held; neither field describes a pack cut now.** `<mainExe>` is the same string for a different binary (the configuration app, since 2026-09-15), and `<shortcutLocations>` is `StartMenuRoot` since the same day. *Noted 2026-09-16; the row is not rewritten, because it is a measurement of a 0.9.0 pack* |
| Update 0.9.0 → 0.9.1 | Found, `deltas=1`, **downloaded and staged in 5.7 s** from a local directory feed, applied, version moved |
| Rollback 0.9.1 → 0.9.0 | `rollback=True`, **`deltas=0`** -- a full re-download, because `packages\` had been pruned. Staged in **0.2 s** (same volume). Version moved back |
| **Browsers beside `current\`** | **Byte-identical across both**, by SHA-256 over every file. 52,428,869 b planted at `<root>\browsers\` |
| **The process log** | Survived both, and the single file carries **`BrowserAI 0.9.0 started` and `BrowserAI 0.9.1 started`** -- which is the §E claim demonstrated, not asserted |
| `packages\` after the update | **Pruned to the new full package only.** The 0.9.0 full was gone, which is why archiving every full `.nupkg` is mandatory and not tidy |

⚠️ **The browsers tree was planted, not provisioned.** 52,428,869 b of known
bytes at `<root>\browsers\chromium-1237\`, hashed before and after. What that
establishes is the property §A depends on -- *a sibling of `current\` survives an
update and a rollback* -- and it does not establish anything about a real
Chromium tree beyond it being files in a directory. A real one was deliberately not used: **measured 2026-09-23, the real tree
is 2,513,947,001 B in 1,233 files (2,397.5 MiB), of which 2,446,370,634 B in
1,119 files is `browsers\`** holding three Chromium and three Firefox
revisions, and it lives under `%LocalAppData%\BrowserAI` -- the one
directory an installer must never be pointed at. *Corrected 2026-09-23
(previously "the real tree is 768 MB"), which named no date, no revision set
and no unit convention.* **One revision per provisioned family is
820,279,796 B (782.3 MiB)** at chromium 1246 / firefox 1549 / ffmpeg 1011 /
winldd 1007, so which of the two figures to quote depends entirely on which
question is being asked. `[MACHINE]`

### The apply gate, against two real instances

**Two installed BrowserAIs started two seconds apart, both offered 0.9.1, and
neither applied.** The version stayed at 0.9.0.

- The first downloaded, staged, and logged *"Update 0.9.1 is staged and was NOT
  applied, because another BrowserAI is running out of this install"* -- which is
  BrowserAI's own gate, the held `<root>\live\<pid>-<guid>.live` handle.
- The second failed its check with Velopack's own
  **`AcquireLockFailedException: Failed to acquire exclusive lock file`** -- a
  `packages\.velopack_lock` held by the first one's download. **That is a second,
  independent guard nobody wrote here**: it
  serialises concurrent *downloads* but says nothing about concurrent processes,
  so it does not replace the gate.

### What an apply does to every process under the root -- read and measured at 1.2.158, 2026-09-24

`[FLOATS]` Read from Velopack's source at **tag `1.2.158`, commit
`3c7f52c1bf17d10ad21b794b006d5ebd1a879a3b`**, and measured the same day against
BrowserAI servers installed from the suite's test pack into a scratch root, under the
installer lock, with the real install read before and after and unchanged:
[`docs/evidence/2026-09-24-coordinator-lifecycle`](../../docs/evidence/2026-09-24-coordinator-lifecycle/README.md).
It answers the maintainer's *"I have a hard time believing it only monitors the
process calling update. That would mean it kills all instances of a multi instance
app!?"*: **it does.**

| Step | Where, at the tag | What it does |
|---|---|---|
| Wait | `src/bins/src/shared/util_common.rs:14-26`, `src/lib-rust/src/process_win.rs:466-498` | Opens the one pid `--waitPid` names, with `SYNCHRONIZE` only, and waits up to 60 s for it. No other process is looked at |
| Kill | `src/bins/src/shared/util_windows.rs:81-102`, `src/lib-rust/src/process_win.rs:427-436` | `force_stop_package`: every process whose image path is under the install root is ended with `TerminateProcess(handle, 1)`. Its own pid is the only exclusion. No message, no grace period, no count of who is running |
| When, in an apply | `src/bins/src/windows/util.rs:59-60`, `src/bins/src/commands/apply_windows_impl.rs:159` | After every hook returns (*"in case the hook left running processes"*), and once more immediately before the old `current\` is renamed away |
| When, otherwise | `src/bins/src/commands/install.rs:106` and `:160`, `src/bins/src/commands/uninstall.rs:21`, `src/bins/src/commands/start_windows_impl.rs:146` | Install and uninstall; `start` only on its legacy `app-` migration branch |

**Nothing in it knows that an app may run more than once.** The waited-on pid is the
process that asked; every other process under the root is in the kill set, and so,
once its wait is over, is the one that asked.

**Run 1: one apply, three servers, and eighteen more started while it ran.**
Servers A, B and C were serving; the rig started `Update.exe apply --waitPid <A>`
and from then on started another server about every 415 ms. It closed A's standard
input so that A ended, as a server that has asked for an apply does.

- **A exited on its own**, code 0, 0.02 s after its input closed.
- **B, C and the six servers started up to +2,065 ms were terminated at once**, exit
  code **1**, at 11:46:31.79Z, by the kill pass after the `--veloapp-obsolete`
  hook: *"Inspected 706 running processes in 211ms, 17 matched directory"*, then
  sixteen `Killing process` lines, eight servers and their eight `node.exe`
  children, and *"Skipping killing self"*. The seventh, started at +2,494 ms, was
  terminated by the second pass immediately before the swap.
- **The eleven started from +2,897 ms onward ran from the NEW `current\`** -- their
  logs report `manifestVersion=1.1.1` -- and survived; `Update.exe` exited 0 at
  +4,247 ms.

So a server that starts during an apply is killed if it starts before the swap and
runs the new version if it starts after it. **A lock the servers took among
themselves could not change that**: a server waiting on one would be waiting in a
process whose image is under `current\`, and the kill pass would end it with the
rest. Every killed server's own log simply stops, with no line about why, and its
client saw a server that exited 1.

**Run 2: two applies at once, for a pid that never exits.** A's standard input was
left open, so A never ended, and two `Update.exe apply --waitPid <A>` were started
21 ms apart.

- **Both waited the full 60 s and logged nothing when the wait ran out**: *"Waiting
  60000ms for process handle to exit."* at 11:50:14Z, then *"Getting ready to apply
  package"* at 11:51:14Z, with no line between. A timeout comes back as
  `Ok(WaitTimeout)`, and `operation_wait` warns only on an error.
- **The second took the packages lock**; the first retried it three times, *"The
  process cannot access the file because it is being used by another process."*,
  after 333, 666 and 1,000 ms. The second's first kill pass then ended A, the pid both
  had waited on, together with B, both `node.exe` children **and the first
  `Update.exe`**, which exited 1. **The kill pass decided which apply won, not the
  lock.**

⚠️ **Every apply whose wait works logs that the wait failed.** In run 1, A exited
normally and the wait returned, and the log still says *"Failed to wait for process
(66536) to exit (Access is denied. (os error -2147024891)). Continuing..."*. The wait
opens the pid with `SYNCHRONIZE` only (`process_win.rs:496`) and, once
`WaitForSingleObject` returns, calls `GetExitCodeProcess` (`:489`), which needs a
query right that handle does not carry. So the warning marks a wait that worked, and
a wait that ran out marks nothing: **read the two cases off this log backwards, or
better, read an apply's wait from the asking process's exit time**. `[FLOATS]`

**`SetAutoApplyOnStartup` still defaults to true at 1.2.158**
(`src/lib-csharp/VelopackApp.cs:35`, `_autoApply = true`), so
[landmine 2](#2-setautoapplyonstartupfalse-is-mandatory) stands, and both BrowserAI
binaries still turn it off in `VelopackStartup`.

**What BrowserAI does about it, not part of the measurement:** the design set by
Q280 b and Q285 a applies only when a path scan under the install root finds nothing
but the coordinator itself, which is exactly the set `force_stop_package` would end,
and Q286 b makes a server stopped for an update, or started while this install's
`Update.exe` runs, refuse its calls with a sentence
([DECISIONS](../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client)).

**Re-establish it** with the batch's `kill-measure.ps1.txt` and
`linger-measure.ps1.txt`, renamed back to `.ps1`: each installs the suite's test pack
into a scratch root under the installer lock, stages a repacked version, runs the
apply against live servers and removes everything again. And read the eight files
the table cites, and `VelopackApp.cs`, at the release under review.
[Re-verification rows](../re-verification.md) 159 and 160.

### Two things the toolchain does that nothing else records

**`vpk` has no `--version` flag** -- it answers *"Unrecognized command or
argument"*. The version is in the first line of `vpk --help`: `Velopack CLI
1.2.0, for distributing applications.` The release script reads it there, because
the CLI and the library must be the same version and the CLI is a global tool
that no lock file can see.

**`Setup.exe` takes `--installto <DIR>`** (short `-t`), alongside `--silent`,
`--verbose` and `--log <FILE>`. Read from `Setup.exe --help` at 1.2.0. It is what
makes the update lane testable at all without pointing an installer at the real
`%LocalAppData%\BrowserAI`.

### A version-string sweep over a publish directory is too broad to use

`[STABLE]` -- this is about how NuGet packages are built, not about Velopack.

**The check as first specified -- *"grep every version string in the linked binary
and fail on a decorated one"*, carried over from another project's `build.sh` --
can never go green here.** The first AOT publish of this repository carried **six**
decorated version strings and **not one of them was ours**:

```
1.2.0+f2edcbc                                            (Velopack)
2.2.0+6fa3825973949a9c4f0cd8af344e15a8db09dc35           (ModelContextProtocol)
10.0.10+f7d90799ce4ef09a0bb257852a57248d2a8fb8dd         (Microsoft.Extensions.*)
10.0.10-servicing.26326.116+f7d90799ce4ef09a0bb257852a57248d2a8fb8dd
10.0.11+e2f47b0110ed922f21a1522da67279133ce28f32
10.8.3+ccb356f31db9d894807c4fd0c97c2f41553d1524          (Microsoft.Extensions.AI.Abstractions)
```

Every one is its publisher's own SourceLink decoration, linked in by ILC. That
sweep is only sound for a binary with **no** third-party dependency carrying one,
which this is not and will not become -- and the trap generalises: a check written
against a single-assembly build silently becomes a check against the whole
dependency closure the moment the build is statically linked. **The check that is both
sound and still a sweep is narrower: a decorated string whose version *core* is
the version being packed.** That is ours -- the entry assembly's attribute, or a
referenced project of ours sharing the derived version -- and it is the only
string that can reach the feed comparison, because the updater matches
`BuildVersion.Current` against the served version. A third-party package's own
decoration is inert. Implemented that way in `build/New-Release.ps1`.

### The ILC-output check needs the severity word, not the code

`[STABLE]` -- a property of how csc is invoked.

**A pattern of `\bIL[0-9]{4}\b` over the publish log fails every publish.** At
`-v:normal` the log contains csc's full command line, which carries
`/nowarn:1701,1702,NU5105,IL2121,...` -- so the pattern matches a **suppression
list**. Measured on the first run of `build/New-Release.ps1`. The pattern has to
require the severity word: `(warning|error)\s+IL[0-9]{4}`, plus the literal
`will always throw`, which is the case the whole check exists for and is not a
diagnostic at all.

## Channel -- the charter's reason was wrong

Measured 2026-08-15. `[FLOATS]`

Default channel is `win` (the OS short name), stamped into `sq.version` and read
back by the locator. **A `-beta` version suffix has zero effect on channel
derivation** -- packing `1.0.3-beta.1` with no `--channel` emitted
`releases.win.json`. The charter attributed the hazard to Velopack; it was
application code in a sibling project.

The real reason to set it explicitly: **a client installed from a beta
`Setup.exe` inherits `beta` in its manifest and stays there silently.** Two new
hazards: `ExplicitChannel = ""` produces `releases..json` → 404 (the code
null-coalesces, so empty is not unset), and **`vpk pack` lowercases the channel
while the client does not** -- `"Beta"` passes on NTFS and 404s on a
case-sensitive store, which is exactly a sibling project's S3 setup.

## NativeAOT, hooks, and `vpk` output

Measured 2026-08-15. `[FLOATS]`

**NativeAOT + Velopack 1.2.0: zero trim/AOT/IL warnings.** `VelopackApp.Build().Run()`
works; install, delta update and rollback all work. Exe 9,182,720 b (8.76 MiB),
full nupkg 6,072,200 b, `Setup.exe` 10,533,768 b. The 34 MB pdb is excluded
automatically. ⚠️ **Target `net10.0-windows`** -- the hook callbacks are
`[SupportedOSPlatform("windows")]`, so plain `net10.0` produces CA1416.

> **Confirmed against the real product 2026-08-16**, not against a spike app:
> zero trim/AOT warnings and **zero `will always throw`** with Velopack
> referenced -- read out of ILC's own console output and not inferred from the
> exit code, by `build/New-Release.ps1`. **What Velopack costs the binary:
> 11,874,816 b → 17,853,952 b**, a **5,979,136 b / +50.4%** increase, measured on
> the AOT publish either side of adding the package. Against a 130 MB shipped
> payload that is noise; against a **97,216 b delta** it is not, because our own
> binary is the only file a BrowserAI-only release ships. `[FLOATS]`

**Hooks run as the user, non-elevated, in session 1.** Fast-exit hooks with their
timeouts: `--veloapp-install` (30 s), `--veloapp-updated` (15 s),
`--veloapp-obsolete` (15 s), `--veloapp-uninstall` (60 s); `OnFirstRun` and
`OnRestarted` do not exit.

> ⚠️ **Corrected 2026-08-16 (previously "Hooks can register the logon sweep task
> -- confirmed ... `schtasks /Create /XML` from the install hook succeeded with
> `LogonType=InteractiveToken` ... The task survived update and rollback ... and the
> uninstall hook removed it").** **The observation was real and the subject was
> wrong**, which is the more expensive of the two ways to be wrong. What the
> spike established is what is left above: the hooks' identity, session and
> timeouts. What it did **not** establish is that *BrowserAI* can register a
> scheduled task, and
> [step 16 measured that it cannot](../windows/detection.md#the-logon-sweep-task)
> -- `Access is denied` / `0x80070005` from the same machine, for a minimal
> definition as much as for ours. One `schtasks` success in a spike directory
> became a standing claim about the product, and it reached
> [the charter](../../DECISIONS.md) as *"verified"*. **The task is dropped**
> (the Velopack update lane), so
> nothing now turns on it; the entry is corrected and not deleted because a
> reader who remembers *"confirmed"* has to be able to find out what happened to
> it.

**`vpk` emits**, into `Releases` by default: `{id}-{version}-full.nupkg`,
`{id}-{version}-delta.nupkg`, `{id}-{channel}-Portable.zip`,
`{id}-{channel}-Setup.exe`, `releases.{channel}.json`, `assets.{channel}.json`,
`RELEASES`. **It does not prune** -- after 5 versions all 5 fulls and 4 deltas
remained and the feed advertised all of them.

⚠️ **Every one of those names carries the pack id, which is also the install
directory -- so the downloads are renamed after the pack, 2026-09-15.**
`build/New-Release.ps1` moves `BrowserAI.app-win-Setup.exe` to `BrowserAI.exe`
and `BrowserAI.app-win-Portable.zip` to `BrowserAI.zip`, rewrites both names in
`assets.{channel}.json`, and renames the human-facing manifest directory under
`Releases\archive\` to `BrowserAI-{version}-manifest`. *(Corrected later the same
day, previously "the download is renamed after the pack ... moves
`BrowserAI.app-win-Setup.exe` to `BrowserAI-win-Setup.exe` and rewrites that one
name": it was one artifact and it kept vpk's `-win-Setup` vocabulary, which says
what the tool calls the file and not what it is.)* **The channel leaves the
name only on the default channel** -- `BrowserAI-beta.exe` otherwise -- so two
packs into one output directory still cannot overwrite each other, which is the
one property vpk's own naming had. **The `.nupkg`s and `releases.{channel}.json`
are deliberately left alone**: Velopack resolves those by name, so renaming one
is a feed that 404s on the first update.
`ReleaseScriptTests.ThePackIdIsTheInstallDirectoryAndTheDownloadsAreRenamedBack`
holds every half, with the two package names as the control.

⚠️ **THE PORTABLE ZIP IS STILL PRODUCED AND IS NO LONGER PUBLISHED --
added 2026-09-23 by addition, and the two halves of that sentence are
independent.** `vpk pack` still emits `BrowserAI.app-win-Portable.zip`,
`build/New-Release.ps1` still renames it to `BrowserAI.zip` and still rewrites
that name in `assets.{channel}.json`, and `ReleaseScriptTests` still holds the
rename with `Required = $true`. What changed is the **upload set**: the
maintainer's decision, verbatim, is *"2 drop and update the readme to not
mention it"*, so from the next release the zip is not uploaded to GitHub at all.
It remains a local artifact of every pack -- which is what keeps the rename
step exercised on every cut and not only on the ones somebody remembers.
**The published `v1.1.0` release carries three assets** -- *corrected
2026-09-23 the same day (previously "**Nothing has been removed from the
published `v1.1.0` release**, which carries seven assets")*, when the maintainer
gave the word and the four were deleted. See
[the counters](#how-often-the-feed-is-asked-and-by-what----measured-2026-09-22).
`v1.0.0` still carries all seven and was deliberately left alone.

⚠️ **`BrowserAI.exe` names THREE different files, and the difference matters
when reading any other line in this article.** *Corrected 2026-09-15 (previously
"two different files ... the installed binary is `<install root>\current\BrowserAI.exe`,
~17.9 MB, which is what `--mainExe` names, what registration points a client at,
and what the sizes table below means").* The **download** is the self-extracting
installer, 59,353,329 bytes as of 2026-09-15, which exists only until it has been
run. The **stub** is `<install root>\BrowserAI.exe`, 392,704 bytes, which
Velopack writes and names after `--mainExe`. The **installed main executable** is
`<install root>\current\BrowserAI.exe`, which is what `--mainExe` names and what
the stub, `Update.exe start`, the Start Menu shortcut and all four hooks reach --
and since 2026-09-15 that is the **configuration app**, 10,382,848 bytes, not the
server. ⚠️ **Registration does NOT point a client at it any more**: it names
`<install root>\current\BrowserAI.Server.exe`, 19,180,032 bytes, composed from
the app's own directory and refused unless its PE header says console subsystem.
Every size in the table below predates the split and is about the single binary
that was there then. Nothing in Velopack relates the two: the Setup stub
locates its payload from the bundle appended to itself and never from its own
filename, which is why renaming it is safe at all.

**`vpk` rejects 4-part version numbers** -- semver2, three parts only.

**And the assembly version does not follow it: a 4-part assembly version renders
as `1.0.0.0` wherever the app reads it back.** .NET assembly versions are 4-part
by default, so packing succeeds with a 3-part semver while the running app -- a
window title, an about box, a log banner -- shows a fourth component that exists
nowhere in the feed. The two numbers are separate and only one of them is
constrained by `vpk`. Read 2026-08-16 from an in-house Velopack deployment's own troubleshooting notes
(*Version Shows 4 Parts*), where it is filed as a shipped symptom and not a
theory. Not run here. `[STABLE]` -- this is .NET assembly-version behaviour, not
Velopack's.

> ⚠️ **Corrected 2026-08-16 (previously: "the fix is to format explicitly from
> the parts: `$"{v.Major}.{v.Minor}.{v.Build}"`").** That fix is wrong here, and
> it fails in the opposite direction to the symptom it treats. It reads
> `Assembly.GetName().Version`, and under the mechanism this project actually
> uses -- see below -- that number is **`{Major}.0.0.0`**, so formatting three
> parts out of it renders `0.0.0` for every build of the 0.x line and `1.0.0`
> for every build of the 1.x line. Measured on this repository's own artifact at
> tag `v0.1.0`: `AssemblyVersion` is `0.0.0.0` while the version is `0.1.0`. The
> correct source is `AssemblyInformationalVersionAttribute`, and the rule is
> that **nothing reads the assembly version at all**.

**`.gitignore` verdict** (closes the deferred v1 item): `/Releases/` ✅ ·
`*-Portable.zip` ⚠️ **stopped matching on 2026-09-15**, when the portable
archive was renamed `BrowserAI.zip` -- harmless only because `/Releases/` covers
the whole directory, which is the line actually doing the work · `/RELEASES` ✅
default channel only · **`Setup.exe` never matches** -- vpk's own name is
`{id}-{channel}-Setup.exe` · **`/payload/`,
`/staging/`, `/.staging/` are not vpk output at all**; they are BrowserAI's own
build conventions and must be justified on that basis or dropped.

### How long a test pack takes -- measured 2026-09-24

`[MACHINE]` vpk **1.2.158**, .NET SDK **10.0.401**, the reference machine, a pack of
1.1.1-alpha.0.116. **`build/New-Release.ps1 -TestPackOnly` took 40.2 s end to end**:
deriving the version through MinVer, copying the two published directories (the
server's 275 MB and the app's 52 MB) into one pack directory, and two `vpk pack`
runs of the same directory -- **15.8 s** for the suite's installer,
`BrowserAI.app.test`, and **18.0 s** for its shipping-id twin, each a full package of
about 55 MB with an installer and a portable archive. One run, timed by the script's
own stopwatch around each `vpk pack`; every gate driver prints those two lines into
its log, which is where later readings are.

**Re-establish** with the command itself: it prints *"vpk pack of ... took N s"* for
each pack, and the wall time is the caller's.

**Twelve more readings the same evening, from six gate halves, and the first reading
above is the slow one.** `[MACHINE]` The gate drivers packed 1.1.1-alpha.0.120, .122
and .124, and their logs read **9.7 to 10.4 s** for the suite's installer and **9.2 to
9.7 s** for the twin every time (PowerShell 10.2 / 9.4, 10.0 / 9.5, 10.4 / 9.4 and
9.9 / 9.3; Git Bash 9.7 / 9.2 and 10.4 / 9.7), against 15.8 s and 18.0 s in the one run
above. Two packs run by hand at .117 read 9.4 / 9.3 and 10 / 9.2. Why the first run was
slower is not established. The drivers do not print the pack step's end-to-end time.

## Deriving the version from git tags, with MinVer

Measured while building the git-tag versioning, on SDK **10.0.302**
with **MinVer 7.0.0** resolved through the float (`Version="*"`, product project
only, `MinVerTagPrefix` of `v`). Everything below is read off the build or off
the artifact, never off MinVer's documentation. `[FLOATS]` -- MinVer, the SDK and
git all move under it.

**What MinVer produced, at three heights.** Re-establish any row with
`dotnet msbuild src/BrowserAI/BrowserAI.csproj -t:MinVer -getProperty:MinVerVersion,Version,AssemblyVersion,FileVersion,InformationalVersion`.

| Where HEAD is | `MinVerVersion` | `AssemblyVersion` | `FileVersion` | `InformationalVersion` |
|---|---|---|---|---|
| On the annotated tag `v0.1.0` | `0.1.0` | **`0.0.0.0`** | `0.1.0.0` | `0.1.0` |
| 5 commits past it, no new tag | `0.1.1-alpha.0.5` | **`0.0.0.0`** | `0.1.1.0` | `0.1.1-alpha.0.5` |
| No reachable tag at all | `0.0.0-alpha.0.71` | - | -- | - |

The five-commit row was produced by five `--allow-empty` commits on a throwaway
branch, built, read off the produced `BrowserAI.dll`, and the branch deleted; the
tagless row is what this repository produced **before** `v0.1.0` existed, which
is the only moment that state is free to observe. The height 71 is the commit
count since the root commit, one less than `git rev-list --count HEAD`.

**`AssemblyVersion` is `{Major}.0.0.0` by design, and that is the trap.**
`[FLOATS]` -- MinVer's choice, and it is the one MinVer would change. It is
not a defect and MinVer will not be talked out of it -- a 4-part assembly version
is a binding identity in .NET and MinVer keeps it stable across a major. The
consequence is that the number a caller reaches for first is **`0.0.0.0` for the
entire 0.x line**. The published binary's Win32 resource carries the useful
pair instead: `ProductVersion` is the informational version and `FileVersion` is
`{Major}.{Minor}.{Patch}.0` -- read on the AOT-published `BrowserAI.exe` as
`0.1.0` and `0.1.0.0`.

**The SDK's `SourceRevisionId` decoration is gated on two properties, not one.**
`[FLOATS]` -- the SDK floats under `rollForward: latestMajor`, so this moves
without anyone choosing it.
`AddSourceRevisionToInformationalVersion` in
`Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.GenerateAssemblyInfo.targets`
requires **`SourceControlInformationFeatureSupported == 'true'` and
`IncludeSourceRevisionInInformationalVersion == 'true'`**, and the second
defaults to `true` in that same file. The first is set by a source-control
provider -- SourceLink and nothing in the SDK -- so in a repository with no
SourceLink the decoration **cannot fire at all**, which means the property alone
proves nothing and a repository can look protected while being merely
un-provoked. Measured twice, each arm:

| Arm | `InformationalVersion` |
|---|---|
| Repository as committed, nothing supplied | `0.1.0` |
| Feature on, `SourceRevisionId` supplied, property `false` | `0.1.0` |
| Feature on, `SourceRevisionId` supplied, property `true` | `0.1.0+a273b31c0ffee1234567890abcdef1234567890a` |

So the property in `Directory.Build.props` **is** the thing that stops it, once
anything arms the feature. Two details cost time:
`-getProperty` reports the value only after the targets actually named ran, and
this decoration hangs off **`GetAssemblyAttributes`** and not
`GetAssemblyVersion` or `MinVer` -- asking after either of the latter two returns
an undecorated string and reads as proof of something it did not test. And a
`-p:InformationalVersion=...` global property does **not** survive: MinVer's own
target overwrites it, so the *`.`-separated* form the SDK produces when the
string already carries a `+` (`0.1.0+a273b31` becoming
`0.1.0+a273b31.<40-char sha>`) could not be reproduced here and is **read from
the target's own text** and not measured. That form is the one that shipped
in an earlier in-house updater, with a consequence: a fleet where
every device downloaded the binary it was **already running**, swapped it,
restarted, and repeated hourly -- because the updater compared the served version
against the reported one, and the reported one had gained a suffix the feed's
never carried.

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

## `Setup.exe -- <args>` hangs forever

Found 2026-08-15. `[FLOATS]`

`setup.rs` declares `EXE_ARGS` without `.value_parser(value_parser!(OsString))`
but reads `get_many::<OsString>`, so passing start arguments panics with
*"Mismatch between definition and access of EXE_ARGS ... Could not downcast"*. **The
process never exits**, installs nothing, and leaves one log line. `update.rs` has
the value parser and is unaffected. Any scripted install passing start arguments
hangs forever -- the purest instance of this project's own failure class, found in
the tool we were about to trust with it. **BrowserAI must never pass start
arguments to `Setup.exe`.**

Two smaller ones: **Desktop and Start Menu shortcuts are created by default**
(`--shortcuts` defaults to `Desktop,StartMenuRoot`), and
`%LOCALAPPDATA%\velopack\` is created unconditionally, **not removed by
uninstall**, with non-installed runs writing to a machine-shared `velopack.log`.

## A non-silent install starts the app in a console window, and nobody is on the other end of it -- measured 2026-09-14

**Measured 2026-09-14 @ Velopack 1.2.0**, end to end, against a real install of
this product. Evidence:
[`docs/evidence/2026-09-14-firstrun/`](../../docs/evidence/2026-09-14-firstrun/README.md)
(`setup.log`, `observe.jsonl`, `tree.jsonl`, `uninstall.log`). `[FLOATS]`

**`Setup.exe` without `--silent` finishes by starting the app itself**, through
`shared::start_package`, and that call passes `show_window = true`
(`util_windows.rs`). `process_win.rs` turns that into the creation flags:

```rust
let flags = if show_window { CREATE_UNICODE_ENVIRONMENT } else { CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT };
```

so a **console-subsystem** binary launched this way gets a real console. Observed
from a windowless launcher: flags **1024** for the app start against
**134,218,752** for a hook, a Windows Terminal window titled with the full
executable path, and a `PseudoConsoleWindow` owned by our own pid.

**Three facts in one:**

| | |
|---|---|
| The window | The app's stdin is that console. It never reports end-of-file, because there is no writer to close it |
| The launcher | `Setup.exe` exited **44 ms** later, so the pid the app inherits is already dead by the time it looks |
| The consequence | BrowserAI's client-liveness watch could not attach (`OpenProcess` → `ERROR_INVALID_PARAMETER`), warned that teardown fell back to stdin EOF alone, and then **served nobody until the machine was rebooted** -- 254 s observed, killed only by the uninstall |

**`VELOPACK_FIRSTRUN=true` is how the app can know.** `shared::start_package`
inserts it into a copy of its own environment block for that one start, with the
literal value `true` (`constants.rs: HOOK_ENV_FIRSTRUN`). It is **not** passed to
hooks, and it is inherited by anything the started app spawns. Velopack's own
`OnFirstRun` callback is not a substitute: it runs inside `VelopackApp.Run()`,
before the app has a log, and it does not exit.

**What BrowserAI does about it, since 2026-09-15.** `Program.Main` exits 0 on
`VELOPACK_FIRSTRUN=true` before the sweep, the live marker, the instance
directory and the child -- one log line and a window that flickers. And in
general, a run whose launcher cannot be opened **and** whose stdin is a console
has no teardown signal that can ever arrive, so it exits cleanly instead of
serving nobody: both halves have to hold, because each on its own is ordinary.
⚠️ **The upstream half cannot be fixed from here** -- there is no window or
no-start option on `Setup.exe`'s command line, and `--silent` skips the start
only as a side effect of hiding every dialog and answering yes to every prompt.
It is filed as [velopack/velopack#1056](https://github.com/velopack/velopack/issues/1056).

**How to re-establish.** Launch `Setup.exe` from a windowless parent (a detached
`pwsh` with no console, as
[`docs/probes/2026-09-14-firstrun/launch-detached.ps1`](../../docs/probes/2026-09-14-firstrun/README.md)
does),
without `--silent`, against a scratch `--installto`, and watch the process tree
and the top-level windows. ⚠️ **Sandbox it**: point `CLAUDE_CONFIG_DIR` at a
scratch directory first, because the install hook registers with the real client
by name, and export the Add/Remove key for the pack id, because the uninstall
deletes it.

**Re-established 2026-09-24 @ Velopack 1.2.158, and UNCHANGED on Velopack's
side**, installing the suite's test pack without `--silent` from a
`DETACHED_PROCESS` parent into a scratch `--installto`. Setup's own verbose log
printed `PROCESS_CREATION_FLAGS(134218752)` for the `--veloapp-install` hook and
`PROCESS_CREATION_FLAGS(1024)` for the app start, with
`Setting environment variable: VELOPACK_FIRSTRUN=true` logged just before that
start, and `Setup.exe` exited **26.9 ms** after the started app's own creation
time, both read as kernel process times. Read at the 1.2.158 tag, with
`process_win.rs` byte-identical to 1.2.0: `util_windows.rs:117` passes `true` for
`show_window`, `process_win.rs:388-392` turns it into `CREATE_UNICODE_ENVIRONMENT`
alone, `install.rs:252-255` starts the app on every non-silent install, and
`setup.rs:97-104` has no option that skips the start.
[velopack/velopack#1056](https://github.com/velopack/velopack/issues/1056) was
still open with no comments on 2026-09-24. ⚠️ **The console-window half was not
re-observed, and no pack this repository builds can show it**: `--mainExe` has
been the Windows-subsystem configuration app since 2026-09-15, so what the start
produced was that app's `#32770` dialog, whose log line reads `(FirstRun)`, and
the 254 s above belongs to the published v1.0.0. *The sandbox is narrower now*:
under the test pack's id no key but `BrowserAI.app.test` is written, so nothing
needs exporting, and `CODEX_HOME` and `BROWSERAI_ROOT` go to scratch beside
`CLAUDE_CONFIG_DIR` because the hooks register with both clients
([re-verification row 124](../re-verification.md), [evidence](../../docs/evidence/2026-09-24-velopack-rows/README.md)).

**The console-window half was re-measured later the same day, and it is
UNCHANGED too.** *Corrected 2026-09-24, by addition (previously the paragraph
above ended its Velopack half with "⚠️ **The console-window half was not
re-observed, and no pack this repository builds can show it**").* A probe pack,
`vpk` 1.2.158 over the test pack's own publish with `--mainExe
BrowserAI.Server.exe` under a third id `BrowserAI.app.r124`, installed without
`--silent`: the start put up a visible `CASCADIA_HOSTING_WINDOW_CLASS` window,
3049x1745, titled with the server's full path and owned by the already-running
Windows Terminal, which then read *"[process exited with code 0 (0x00000000)]"*;
the server logged `Startup[78]` and `Startup[9]` and exited **0** after 343 ms,
`Startup[78]` because the observing rig held a handle to `Setup.exe`. **The
shipping pack still cannot show it**, for the reason above: its main executable is
the configuration app. Runs `I3` and `I3b` of the evidence batch; the screenshot
of that window is not kept, because it showed the maintainer's own windows around
it.

### Re-measured 2026-09-15 against the published v1.0.0, and the paragraph above was half wrong

**Everything above about the window and the launcher held; the paragraph headed
*What BrowserAI does about it, since 2026-09-15* did not.** Measured
2026-09-15 @ Velopack 1.2.0, BrowserAI 1.0.0, Windows 11 Pro 26200, on the
maintainer's own machine, installing the **published** `BrowserAI-win-Setup.exe`
from GitHub Releases with no `--installto` and no `--silent`. Evidence:
[`docs/evidence/2026-09-15-install/`](../../docs/evidence/2026-09-15-install/README.md)
-- `setup.log` lines 44 and 51, and `install-observe.jsonl` -- and the product's
own log at
`%LocalAppData%\BrowserAI\logs\browserai-20260915-000.log`. `[FLOATS]`

| | 2026-09-14 | 2026-09-15 |
|---|---|---|
| App-start creation flags | 1024 | **1024** -- confirmed |
| Hook creation flags | 134,218,752 | **134,218,752** -- confirmed |
| The window | Windows Terminal, titled with the exe path | **`CASCADIA_HOSTING_WINDOW_CLASS`, 1506x1490**, visible 16:36:40.884 until the kill ~215 s later |
| `Setup.exe` at the moment the app looked | gone, 44 ms | **gone** -- `OpenProcess` → `ERROR_INVALID_PARAMETER` |
| The app's own verdict | *no client-liveness watch* | `Startup[72]` then `Startup[9]`, **1.89 s** after start |
| What happened next | served nobody until reboot | **did not exit either**: alive **213.6 s** past its own *is exiting* line, 10 threads, 224 handles, holding `node.exe` and `conhost.exe` |

⚠️ **`VELOPACK_FIRSTRUN` did not reach the branch that reads it, because
`VelopackApp.Run()` clears it on an installed process before that branch runs.**
*Corrected 2026-09-24 @ Velopack 1.2.158 (previously "and why is unresolved")*.
The product logged `Startup[72]`/`Startup[9]` -- the *general*
no-client decision -- and not `Startup[8]`, the installer exit, which sits
thirty lines earlier in `Main`. The same binary **does** take `Startup[8]`, in
0.313 s, when the variable is set on a start it is not installed for (measured
the same day through the orphan rig,
[`docs/evidence/2026-09-15-fix/repro-firstrun.txt`](../../docs/evidence/2026-09-15-fix/README.md)),
so the read is not broken. What runs in between is `VelopackApp.Run()`, which
reads the variable at `VelopackApp.cs:232` and clears it at `VelopackApp.cs:237`,
and does both only on an installed process: `VelopackApp.cs:227-230` returns
first when `CurrentlyInstalledVersion` is null. The file is byte-identical at
1.2.0 and 1.2.158, so the published v1.0.0 cleared it the same way.
*Corrected 2026-09-24 (previously "which is the only code with the opportunity;
**that it clears the variable is INFERRED and has not been measured**, and it is
recorded here as an open question and not as a fact")*: read at both tags, and
measured @ Velopack 1.2.158 through the orphan-console rig with
`VELOPACK_FIRSTRUN=true` on two byte-identical copies of the suite's test pack's
`BrowserAI.Server.exe`. The copy inside an installed scratch root logged
`Updates[17]`, `Startup[72]` and `Startup[9]`; the copy outside any install
logged `Startup[8]`, 47 ms after its own creation
([re-verification row 126](../re-verification.md), [evidence](../../docs/evidence/2026-09-24-velopack-rows/README.md)). Nothing depends on the
answer: see the next paragraph.

⚠️ **So this product's own installer branch cannot fire on an installed
process** -- *added 2026-09-24*. `src/BrowserAI/Program.cs:228-232` asks
`VelopackStartup.StartedByTheInstaller()` after
`VelopackStartup.RunWithoutLifecycleHooks` at `:131` has already called
`VelopackApp.Run()`, so on an installed start the variable is gone before the
branch reads it, and only an uninstalled start with the variable set takes
`Startup[8]`. The exit still happens, through the general branch the next
paragraph describes. Whether the branch stays is a question put to the
maintainer on 2026-09-24 (Q276) and not decided here.

⚠️ **Decided the same day: the branch is deleted -- Q276 a, the maintainer's words
verbatim, "Q276 a".** *Added by addition; the paragraph above stands as the
account of why.* `Main` no longer reads `VELOPACK_FIRSTRUN`, `Startup[8]` is
retired in `StartupLog` and may not be reused, and the general exit is the whole
of the answer to the installer's shape. The suite holds both directions:
`InstallerHandoffTests.ThePublishedBinaryExitsWhenItsLauncherIsGoneAndStdinIsAConsole`
requires `Startup[9]` with the variable set and without it, and
`InstallerHandoffTests.ThePublishedBinaryServesTheClientThatStartedItWithTheInstallersVariableSet`
requires a live client to be served whatever the variable says -- both watched red
against a published build that still carried the branch. **The configuration
app's own read is unchanged**: it takes the value before `Run()` clears it, which
is what lets its dialog say it is a first run. The last packed test pack is a
1.1.0 build and still carries `Startup[8]`, so the measurement above keeps
reproducing on it until the next release is cut.

**So the exit may never key on the variable alone, and this is a second reason
and not a restatement of the first.** The stub `BrowserAI.exe` that Velopack
leaves in the install root reaches the app through `Update.exe start`
(`start_windows_impl.rs:122-131`), which is console-bearing and sets no
`VELOPACK_FIRSTRUN` at all -- so a person double-clicking the thing the installer
put on their machine arrives in exactly the shape the installer's own start
arrives in, with no variable to recognise it by. The decision that has to carry
both is *launcher gone or unopenable **and** stdin is a console*.

⚠️ **Self-update passes `--norestart` today**
(`Updates/VelopackUpdateClient.cs`), so an applied update does not produce a
second console-bearing start. That is one argument away from a regression and not
a property of the design.

**What was wrong in the product, and it was not the decision.** The decision was
correct and the *exit* was not reachable: `JsonLinesTransport.DisposeAsync`
awaited a read loop parked on the console (see
[the read nothing wakes](../windows/processes.md#a-read-parked-on-standard-input-is-woken-by-neither-cancelling-it-nor-disposing-the-stream----measured-2026-09-15)),
and the child, the sweep and the update check had all already run because the
question was asked 506 ms too late. Both are fixed 2026-09-15 and both now carry
an end-to-end arm over the published binary,
`InstallerHandoffTests.ThePublishedBinaryExitsWhenItsLauncherIsGoneAndStdinIsAConsole`
and `.ARunWithNobodyToServeStartsNothingAndCreatesNothingButItsLog`.

**What the fixed run costs, measured through the same rig on the same day.**
Against a published slice of this tree carrying both fixes -- commit `4da72a3`;
⚠️ *its own log line says `BrowserAI 1.0.0` because MinVer derived the version
from the tag the publish was made at, and it is NOT the released v1.0.0* -- on
Windows 11 Pro 26200:

| | v1.0.0 as published | this tree |
|---|--:|--:|
| Launcher start → the no-client decision | 0.531 s | **0.312 s** |
| The decision → the process gone | never (60 s observed here, 213.6 s on the real install) | **0.006 s** |
| Launcher start → the process gone | never | **0.317 s** |
| Under the data root afterwards | `instances\`, `live\`, `logs\` | **`logs\` alone** |
| Children left behind | `node.exe`, `conhost.exe` | **none** |

The 0.219 s the decision moved earlier is the stray sweep, the live-marker mutex
and the `playwright-mcp` child that a run with nobody to serve no longer pays
for; the 0.006 s is the whole of what the exit now costs.

**How to re-establish it.** Start the published binary with a launcher that is
already gone and a standard input that is a console, which needs neither an
installer nor a window: `cmd.exe /c start /b "" cmd.exe /c start /b "" "<exe>"`,
with the outer `cmd` started `CreateNoWindow` so the console it allocates has no
window, and **nothing redirected** -- redirecting any stream makes .NET set
`STARTF_USESTDHANDLES` and hand the child the *caller's* standard input instead.
Two `start /b`s and not one **when the shape being reproduced is a pid that
will not open at all**: a process handle keeps a dead pid openable, and the test
host's own handle on a single intermediate is enough to leave one.
`BrowserAI.Tests.Harness.OrphanedConsoleStart` is that rig, and it now produces
either shape on request -- `LauncherCorpse.Freed` for the two-level launch above,
`LauncherCorpse.Openable` for one level with the handle held.

⚠️ **Corrected 2026-09-15, later the same day (previously the paragraph above
said only "Two `start /b`s rather than one, because a process handle keeps a
dead pid openable and the test host's own handle on a single intermediate is
enough to make the launcher watchable").** That sentence was true and the
conclusion drawn from it -- *therefore the rig must avoid the openable corpse* --
was wrong in the expensive direction. **Nothing makes Windows free a pid on
request**: the console host holds a handle to a process that ran in a console,
so the installer's own `Setup.exe` leaves either shape behind depending on
nothing anybody controls, and the second full run of the day met the openable one
and sat at the whole of `TestDefaults.ProcessHang`. **It is the product that
decides now** -- an opened parent that has already exited is nobody to serve, on
the same path as one that cannot be opened, under a record of its own so the log
says which route it took.

**Re-measured 2026-09-15 through the one-launcher rig, the shape the fix is
about**, on the same machine and against a published slice of this tree carrying
it (`1.0.1-alpha.0.16`, `BROWSERAI_ROOT` pointed at an empty scratch root,
`VELOPACK_FIRSTRUN` removed):

| | openable corpse, one launcher |
|---|--:|
| Launcher start → launcher gone | 0.085 s |
| Launcher start → the product's first record | 0.114 s |
| Launcher start → the no-client decision | **0.116 s** |
| The decision → the process gone | ≤ 0.050 s, polled at 2 ms |
| Under the data root afterwards | **`logs\` alone** |

**The launcher is gone 30 ms before the product writes its first record**, which
is what the shape was always going to do and is *not* what decided the earlier
red -- a launcher still running at the parent read is a race this rig has never
been measured to lose. The 0.116 s is smaller than the 0.312 s above for a reason
that is the rig and not the product: one `cmd` start on the path instead of
two. Re-establish it with
[`docs/probes/2026-09-15-corpse/Measure-Corpse.ps1`](../../docs/probes/2026-09-15-corpse/README.md),
or read
the four records the run leaves: `Startup[1]`, `Startup[4]`, `Startup[76]` -- the
new one -- and `Startup[9]`.

## Two binaries in one pack, measured end to end -- 2026-09-15

**Measured 2026-09-15 @ Velopack 1.2.0, `vpk` 1.2.0, SDK 10.0.400, ILC 10.0.12,
win-x64, Windows 11 Pro 26200.** Evidence:
[`docs/evidence/2026-09-15-app/`](../../docs/evidence/2026-09-15-app/README.md)
-- `pack2.log` and the feed's text under `packfeed/`, with the three large
binaries recorded by size and digest in `packfeed-inventory.csv` instead of
kept. `[FLOATS]`

**A second executable is an ordinary payload file.** It is signed with the rest,
it gets no stub of its own -- the root stub is named after `--mainExe` -- and
`CompatUtil.Verify` is a no-op for an AOT binary, so nothing inspects it. The
pack ran clean with both present.

| | Bytes |
|---|--:|
| `BrowserAI.exe` -- the configuration app, Windows subsystem | **10,382,848** |
| `BrowserAI.Server.exe` -- the MCP server, console subsystem | **19,180,032** |
| the payload beside them | **108 MiB** |
| pack directory, everything on disk | **261,460,220** |
| shipped bytes (the same, less the two `.pdb`s) | **143,406,536** |
| `BrowserAI.app-<v>-full.nupkg` | **54,853,873** |
| `BrowserAI.exe` -- the installer, after the rename | **59,353,329** |
| `BrowserAI.zip` -- the portable archive | **54,824,804** |
| compression ratio, package against shipped | **0.3825** |

⚠️ **The ratio is not comparable with the 0.51 in `build/New-Release.ps1`'s own
comment**, which is from 2026-08-16 and was taken over a much smaller payload.
This is the number the script printed on the first two-binary run; re-measure it
by running the script and not by adjusting it.

**The configuration app costs 4,693,726 bytes of installer** -- 59,353,329 against
the 54,659,603 `v1.0.0` was packed at -- which is 8.6%, against a 10,382,848-byte
binary. LZMA2 compresses a second AOT binary well because it shares most of its
runtime with the first.

### The app opens its window in 155-243 ms, and the window is 556 × 426

Measured 2026-09-15 over three runs of the **published** binary, launched with no
window from PowerShell, polling `EnumWindows` continuously from the moment the
process started until a visible `#32770` owned by it appeared: **243.2 ms**,
**167.8 ms**, **154.8 ms**. The first is the cold one. `GetWindowRect` reported
**556 × 426** device pixels every time, on a per-monitor-v2 process.

**Poll; do not sleep once.** Two by-hand probes of the same binary disagreed
at a fixed 2.5 s wait and agreed at 500 ms when polled -- the window arrives when
the shell gets round to it, and a fixed wait measures the wait.

**The window is class `#32770` and the process owns no other visible one.**
`#32770` is the Windows dialog class and a task dialog is a dialog; the only
other top-level windows the process owns are `IME` and `MSCTFIME UI`, both
invisible, which every GUI process on this machine carries. `WM_CLOSE` posted to
the dialog exits the process **0**.

**`TASKDIALOGCONFIG` is 160 bytes and `TASKDIALOG_BUTTON` is 12, on x64.**
Both are `#pragma pack(1)` in the Windows headers, which is the whole trap: the
natural C# layout pads every pointer to eight bytes and produces **184** bytes
for the same fields -- measured as the positive control -- and Windows then reads
a 184-byte structure as though it were the 160-byte one, field by field, with no
diagnostic at all. Checked against Microsoft's own metadata through `CsWin32`,
size **and** all 22 field offsets, because a size that agrees says nothing about
a field that moved: two swapped pointers leave the total unchanged and turn the
window title into the instruction.

### `<consoleAllocationPolicy>detached</consoleAllocationPolicy>` -- TRIED, AND DROPPED because its benefit could not be established

**Measured 2026-09-15 on the server's own manifest, then removed.** The element
is from *Learn: windows/console/console-allocation-policy*, floors at Windows 11
24H2 / build 26100, and this machine is 26200.

- **It survives ILC and reaches the binary.** With the element in
  `src/BrowserAI/app.manifest`, the published `BrowserAI.Server.exe` carried
  `consoleAllocationPolicy` **twice** (the open and close tags) beside
  `longPathAware`'s two -- so `ApplicationManifest` embedding is confirmed a
  second time, for a 2024-schema element.
- **It costs nothing in terminal visibility, measured both ways.** From a Git
  Bash terminal, `BrowserAI.Server.exe --sweep` printed its startup record, the
  `BROWSERAI_ROOT` override warning, the client-watch line and the sweep summary,
  and exited 0 -- identically with and without the element.
- ⚠️ **The benefit could not be established, and the rig said so instead of
  reporting a success.** A `DETACHED_PROCESS` `pwsh` starting the server, polled
  at 100 ms for six seconds for any visible `ConsoleWindowClass`,
  `CASCADIA_HOSTING_WINDOW_CLASS` or `PseudoConsoleWindow`, saw **zero** new
  windows -- and so did the **control arm**, a binary carrying no such element.
  A zero from a rig whose positive control does not fire is not evidence.

**So the line was removed.** Keeping a manifest element whose benefit was never
observed, on a binary that no installer launches any more -- the configuration app
is `--mainExe` now -- would be a change with nothing behind it, and it would owe a
hazard row for its OS floor. **To re-establish it**, the rig needs a parent that
provably has no console of its own and a child that provably outlives the poll;
neither was separated here. The candidates are that the intermediate detached
`pwsh` is itself given a console the child then joins, or that a `--sweep` run is
over before the window is mapped.

## Where the feed and the package are hosted, and the four places they could have gone instead -- read 2026-09-23

**Decided 2026-09-23, Q237 = f: they stay release assets.** The question was
raised by the asset trim -- if a release is going to carry three files, does it
need to carry them at all? -- and the answer is that every alternative costs
something real and buys nothing this product needs. What follows is what was read
to get there, kept because the argument is only re-checkable if the facts are.
Evidence:
[`docs/evidence/2026-09-23-feed-hosting-research/`](../../docs/evidence/2026-09-23-feed-hosting-research/README.md).

⚠️ **Nothing in this section carries a floating-fact marker, deliberately, and
this sentence is why.** Nothing in the product reads any of it: these are the
alternatives to a decision and not facts a build rests on, so re-reading them
is part of re-opening the question and not a standing debt -- which is the
[one exemption](../re-verification.md#a-floats-entry-with-no-row-the-one-rule)
read the other way round. The one fact here the product *does* depend on -- that a
client reads `releases.{channel}.json` and nothing else -- is marked and carries
[row 137](../re-verification.md) of its own.

*(Written without the marker token itself on purpose: the counter that keeps
[the holes table](../re-verification.md) honest reads the literal string wherever
it appears in an article, so prose about the convention inside an article is
counted as a use of it. It went red here first, at 22 against a recorded 21.)*

⚠️ **And one fact governs every alternative at once: a published release asset
cannot be redirected.** There is no GitHub facility that makes
`.../releases/latest/download/releases.win.json` serve from somewhere else, so any
move strands every already-installed build that has not first updated through the
old URL. The only runtime lever is `BROWSERAI_UPDATE_FEED`
(`UpdateConfiguration.Resolve`), which is a thing a person sets by hand on one
machine. **A hosting move is therefore a one-way door for the installed base**,
and that is true of all four alternatives below.

### The release alias is never cached, and GitHub Pages is cached for 600 seconds -- measured 2026-09-23

`curl -sSI` on this project's own feed URL, redirect not followed so no asset was
fetched and no counter moved:

```
HTTP/1.1 302 Found
Location: https://github.com/SixFive7/BrowserAI/releases/download/v1.1.0/releases.win.json
Cache-Control: no-cache
```

**`no-cache`: the alias is never cached, and a new release is visible
immediately.** Against that, `https://pages.github.com/` and
`https://docs.velopack.io/` -- itself a Pages site -- both answer
`Cache-Control: max-age=600` from a Fastly edge (`Via: 1.1 varnish`,
`x-proxy-cache: HIT`). So a Pages-hosted feed is **up to ten minutes stale**.

**Harmless for correctness and real all the same.** A stale feed reports the
previous version, which a client treats as *no update available*; nothing here
depends on the difference today. It is recorded because *immediate* is what the
current arrangement gives for free, and a move would spend it without anybody
noticing.

### What GitHub Pages would cost, as documented 2026-09-23

From `docs.github.com/en/pages/.../github-pages-limits`, verbatim: *"Published
GitHub Pages sites may be no larger than 1 GB."* · *"GitHub Pages sites have a
**soft** bandwidth limit of 100 GB per month."* · *"GitHub Pages sites have a
**soft** limit of 10 builds per hour."* · *"GitHub Pages source repositories have
a recommended limit of 1 GB."* · *"GitHub Pages deployments will timeout if they
take longer than 10 minutes."* The page says nothing about cache duration, which
is why the TTL above had to be measured and not read.

**At 55,022,716 bytes a package, 100 GB per month is about 1,860 downloads.** The
1 GB site cap is about **18 packages live at once**. Release assets carry no
equivalent limit --
`docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github`,
verbatim: *"We don't limit the total size of the binary files in the release or
the bandwidth used to deliver them."*

⚠️ **"No CI" is not quite available either.** A publishing source can be a
branch with no workflow authored, but
`docs.github.com/en/pages/.../configuring-a-publishing-source-for-your-github-pages-site`
says verbatim: *"Your GitHub Pages site will always be deployed with a GitHub
Actions workflow run, even if you've configured your GitHub Pages site to be
built using a different CI tool."* So a GitHub-owned `pages-build-deployment` run
fires on every push. **Whether that breaks this project's no-hosted-CI stance is a
decision and not a fact**, and it is named here instead of resolved. Pages
is not enabled today: `gh api repos/SixFive7/BrowserAI` reads `has_pages: false`
and `repos/.../pages` is a 404.

### A 55 MB package per release is permanent, and a clone pays for every branch

`docs.github.com/.../about-large-files-on-github`, verbatim: *"If you attempt to add
or update a file that is larger than 50 MiB, you will receive a warning from
Git."* · *"GitHub blocks files larger than 100 MiB."* · *"We recommend
repositories remain small, ideally less than 1 GB, and less than 5 GB is strongly
recommended."* A 55 MB `.nupkg` is **over the warning and under the block**, so it
commits, with a warning on every push.

**A `.nupkg` is a zip, so git delta-compresses successive versions to
approximately nothing**: each release adds ~55 MB permanently. Against a
repository that was **18.1 MiB** on 2026-09-23:

| Releases committed | Repository |
|--:|---|
| 1 | ~73 MB |
| 10 | ~570 MB |
| 18 | ~1 GB -- the *ideally less than* line, and the Pages source-repository recommendation |
| 90 | ~5 GB -- the *strongly recommended* line |

⚠️ **An orphan `gh-pages` branch does not avoid this**, which is the half that
looks like an escape and is not. `git-scm.com/docs/git-clone` on
`--single-branch`, verbatim: *"Clone only the history leading to the tip of a
single branch ... Further fetches into the resulting repository will only update
the remote-tracking branch for the branch this option was used for."* -- i.e. the
**default** clone fetches every branch's objects. Everyone who clones BrowserAI
would pay for every package ever published unless they passed `--single-branch`,
`--depth` or `--filter=blob:none`. **The only way to keep packages out of a
contributor's clone is a different repository.**

### A feed may name an absolute package URL in Velopack's code and may not in its documentation

The two disagree, and the disagreement is the risk. `SimpleWebSource.cs:76-83` @
**1.2.158** takes `releaseEntry.FileName` as an absolute URL when it is one and
appends it to the base URL when it is not -- its own comment says so. But
`docs.velopack.io/distributing/overview` says, verbatim: *"You must distribute
these packages in the same folder as the `releases.{channel}.json` file for
updates to work."* and *"This file should be distributed in the same folder as
the `nupkg` files are deployed."*

**So the documented contract is *beside* and the implemented behaviour is *beside
or absolute*.** Velopack floats in this repository, so relying on the undocumented
half is relying on a branch the vendor has not promised to keep.

⚠️ **And `vpk` never writes an absolute `FileName` anyway.**
`VelopackAsset.cs:84` is `FileName = Path.GetFileName(filePath)`, and
`ReleaseEntryHelper.UpdateReleaseFilesAsync` regenerates
`releases.{channel}.json` from the `.nupkg`s on **every pack** -- so a
hand-written absolute URL is silently reverted by the next run of
`build/New-Release.ps1`. A split feed is therefore not a configuration; it is a
post-processing step somebody has to remember, on the one file a client cannot do
without.

### The automatic Source code links on a release cannot be removed -- read 2026-09-23

Asked because a release trimmed to three assets still shows five things, and two
of them are not ours to choose.
`docs.github.com/en/repositories/releasing-projects-on-github/about-releases`,
verbatim: *"GitHub will automatically include links to download a zip file and a
tarball containing the contents of the repository at the point of the tag's
creation."* -- *automatically include*, with no setting, flag or opt-out anywhere
on the page.

**Three independent readings say the same thing.** The REST API's `POST` and
`PATCH` release bodies carry no parameter that controls them, and `zipball_url`
and `tarball_url` are *required* response fields. They are **not members of
`assets[]`** -- measured on this repository's own `v1.1.0`, where they sit as
top-level URL fields -- so `gh release delete-asset` and every asset endpoint
cannot reach them. And GitHub's own answer, on
[community discussion 6003](https://github.com/orgs/community/discussions/6003),
from MylesBorins on **2021-10-06**, verbatim: *"We are looking at a number of
improvements that we could make to artifacts / assets, but we may not be funding
this effort in the immediate future (to set expectations)"*. Still open, with
comments as recent as April 2026, beside three later duplicates that are
unanswered.

**What can be changed is what the archives CONTAIN, not whether they appear** --
`export-ignore` in `.gitattributes`, which GitHub does not document itself for
zipballs and which was **not** tested here, and the Git LFS archive setting, which
does not apply because this repository uses no LFS. **Taken to its limit that
produces a near-empty zip, which is a worse artifact than the real source and
not a removal.**

## What bounds a stalled download and a stalled check -- measured 2026-09-23

`[FLOATS]` Velopack 1.2.158, .NET 10.0.401, Windows 10.0.26200.

**Velopack downloads through `HttpClientFileDownloader`, whose only bound is
`client.Timeout = TimeSpan.FromMinutes(SimpleWebSource.Timeout)`** -- 30 minutes
by default, and BrowserAI takes the default
(`src/lib-csharp/Sources/SimpleWebSource.cs` ctor,
`HttpClientFileDownloader.CreateHttpClient`, read 2026-09-23 from
velopack/velopack@1.2.158).

⚠️ **That timeout does not cover the body read.**
`DownloadToStreamInternal` reads under `HttpCompletionOption.ResponseHeadersRead`,
so the clock stops at the response headers. Against a server trickling one byte a
second with `HttpClient.Timeout` set to **5 s**, the body read ran **60.55 s** and
ended only when the server closed. **The control is the other half**: the same
client and the same server, with the response complete, ended at **2.05 s**. So a
half-dead socket is bounded by nothing underneath, and every stall bound on a
Velopack download is one this product chooses.

**And a check timeout arrives wearing an `OperationCanceledException`.**
`GetStringAsync`, which is the call `SimpleWebSource.GetReleaseFeed` makes, ends
its `HttpClient.Timeout` by throwing **`TaskCanceledException`** -- which *is* an
`OperationCanceledException`, so a catch that reads cancellation as *somebody
cancelled us* reads a network timeout as that instead. `UpdateService`'s crash
tripwire says so in place.

⚠️ **WHAT BROWSERAI DOES ABOUT IT, ADDED 2026-09-24 SO THIS ENTRY IS NOT READ AS
A LIVE GAP.** The maintainer's decision, verbatim: *"Wrap the check in its own
timer. So all three timers sit in the tripwire's time."* `UpdateService.CheckBudget`
is 15 minutes, derived and not chosen -- the tripwire's arithmetic needs
`check + absolute < tripwire`, and at 30 and 45 that leaves 15 as the ceiling.
**It is applied by AWAITING the call through that budget's token and not by
handing the token over**, because handing it over is what does not work: past the
point where `CheckAsync` reaches Velopack the token is inert, which is the
measurement above. A check that outruns the budget logs its own event, 21, and
never the tripwire's 11 -- so a slow feed stops reporting as a defect in our own
timers. **The cost is stated:** the pass ends on time and Velopack's own call
finishes into nothing, which is the only alternative to a pass that cannot be
ended at all.

**Re-establish** by pointing a `HttpClient` with a short timeout at a socket that
sends headers and then trickles, and by timing a `GetStringAsync` against a port
that accepts and never answers. The rig is
`.work/assumed-2026-09-23/src/probes/`; the output is `probe-AB-httptimeout.txt`
in the same batch.

## Distribution: MSIX and code signing

**MSIX is disqualified on evidence.** A package cannot re-register while any
process in its family is running: claude-code
[#63397](https://github.com/anthropics/claude-code/issues/63397) (`0x80073D02` /
`ERROR_SHARING_VIOLATION`, the report naming "Claude Code runs as a child process
of Claude Desktop") and openai/codex
[#25770](https://github.com/openai/codex/issues/25770), both in 2026. Hydraulic
Conveyor emits MSIX on Windows and inherits the same failure.

**Every unsigned `Setup.exe` is a new file to SmartScreen.** Azure Artifact
Signing at roughly **$10/mo** buys instant reputation. `[UNVERIFIED]` price -- a
list figure, not a quote obtained.

## Prior art: one shipping Velopack deployment

In-house evidence, not upstream behaviour, and **not reproducible from this
repository** -- the application is unpublished. `[MACHINE]` -- true of one
deployment at one point in time. It is kept because **five of the nine landmines
above are ones it hit and not avoided**, which is the only evidence available
that they bite in production and not only in a spike, and because what it does
right is the restart choreography this project copied.

**It runs Velopack 0.0.1298, not 1.2.0** -- the pre-1.0 line, with both behaviour
and API surface since moved, so read every item below as *this happened once*,
never as *this is how Velopack behaves*. It ships per-user into a
`%LocalAppData%\<app>\current\` layout, no elevation, S3-compatible feed, silent
background check, in production across multiple releases.

**Five of the nine landmines above are ones it hit and not avoided**, and
none announced itself:

- **Feed URL composition** bricked auto-update for **three shipped versions**;
  manual reinstall was the only recovery.
- **`SetAutoApplyOnStartup` is never called**, so the default is live in a
  shipping app -- survivable for a foreground tray app, fatal for a stdio child.
- **Logs are written to `AppContext.BaseDirectory`** -- inside `current\` -- with a
  10-day retention policy that every update resets.
- **Delta packages have never been produced.** Every shipped artifact is a full
  `.nupkg`; delta validation is still an open TODO. Delta granularity is the
  stated reason Velopack was chosen at all, so it is unproven in-house.
- **Rollback has no code and no documentation.** The client would accept one; the
  version-validation script refuses to emit one.

**What it does prove:** the per-user `current\`-swap layout works in production;
a test seam of `virtual` network methods carries **48 hermetic update tests**; and
its restart choreography -- cooperative shutdown with per-component acks, a 10 s
hard-kill backstop, log flush, *then* apply -- is worth copying wholesale.

**Coverage of the update wrapper itself is zero tests**, which is exactly where
the feed-URL bug lived. **That application is single-instance** via a named mutex,
so `force_stop_package` is harmless there -- meaning the landmine that matters most
for concurrent registrations is **untested by the only prior art available**. No
signing: no certificate, no `--signParams`, package signature verification
unexplored.

## Setup will not install over an existing install without being told to, and on a same-version re-ship the button says "Repair" -- measured 2026-09-16

⚠️ **A non-silent `Setup.exe` whose target directory is not empty STOPS and asks,
at any version, and after 300 s with no answer it cancels itself, exits 0 and
installs nothing.** *Corrected 2026-09-24 @ Velopack 1.2.158 (previously "and
waits indefinitely")*. It is not a same-version behaviour and
it is not a BrowserAI one. Measured 2026-09-16 @ Velopack 1.2.0 on the published
`v1.0.0` installer, launched through `CreateProcessW` with `DETACHED_PROCESS`
from a parent that had freed its own console: Setup read the bundle, resolved
`Installation Directory: C:\Users\jori\AppData\Local\BrowserAI.app`, and then
put up a `#32770` titled **`BrowserAI Setup`**, 572x201, and sat there. The log
stops at `Using root packages directory:` until somebody answers, or until the
timeout.

**The timeout, measured 2026-09-24 @ Velopack 1.2.158** against the suite's test
pack over a scratch root that already held an install of it: the same dialog,
titled `BrowserAI (suite) Setup`, 572x201, was left alone. It closed **299.9 s**
after it was first seen, `Setup.exe` exited **0** 300.0 s after it was created,
nothing was installed, and the log's last lines read
`Overwrite/repair dialog timed out, treating as cancel.` The source says why:
`setup.rs:118-120` arms a 300 s dialog timeout on every run without `--silent`,
`dialogs.rs:250-253` turns the timeout into a cancel, and xdialog 3.1.9's
`message.rs:99-105` closes the window. `update.rs:170` arms the same 300 s for
`Update.exe`, so every dialog either binary shows on a non-silent run is bounded
the same way; that half is read from source and was not measured. **1.2.0 armed
it too**, at `setup.rs:110-112` over xdialog 2.1.1, so *indefinitely* was wrong
at the version it was written for. It is one more thing
[re-verification row 130](../re-verification.md) watches, beside the trigger,
the wording and the labels. The dialog reads, the Setup logs and the clearance
readings are the
[2026-09-24 evidence batch](../../docs/evidence/2026-09-24-velopack-rows/README.md).

The dialog, read through UI Automation:

```
[Text] BrowserAI is already installed
[Text] This application is already installed on your computer. If it is not working
       correctly, you can try repairing it by reinstalling.

       Installed at: C:\Users\jori\AppData\Local\BrowserAI.app
[Pane] Cancel   [Pane] Open Install Directory   [Pane] Repair
```

**The trigger is the DIRECTORY, not the version** -- `src/bins/src/commands/install.rs`:

```rust
// does the target directory exist and have files? (eg. already installed)
if !shared::is_dir_empty(&root_path) {
    let installed_version = auto_locate_app_manifest(...)...;
    if !dialogs::show_overwrite_repair_dialog(&app.title, &app.version, &root_path, installed_version.as_ref()) {
        error!("Directory already exists, and user cancelled overwrite.");
        return Ok(());          // <- a CANCEL exits 0 and installs nothing
    }
    shared::force_stop_package(&root_path)?;     // everything under the root dies
    fs::rename(&root_path, &renamed)?;           // old root kept aside for rollback
}
```

**The affirmative button's LABEL and the dialog's BODY both depend on the
version** -- `l18n/src/dialogs.rs`: `Update` when what is installed is older,
`Downgrade` when it is newer, and **`Repair` when the two are equal**, which is
what a re-ship of the same number produces. *Corrected 2026-09-24 @ Velopack
1.2.158 (previously "**Only the affirmative button's LABEL depends on the
version**")*: measured against the suite's test pack at 1.1.0, with the scratch
install's `current\sq.version` set below and above the installer's version, and
read through UI Automation:

| Installed | Heading | Body | Button | Size |
|---|---|---|---|---|
| 1.1.0, the same | *BrowserAI (suite) is already installed* | *This application is already installed on your computer. If it is not working correctly, you can try repairing it by reinstalling.* | `Repair` | 572x201 |
| 1.0.0, older | *BrowserAI (suite) is already installed* | *Version 1.0.0 is currently installed. Would you like to update to version 1.1.0?* | `Update` | 487x186 |
| 9.9.9, newer | *A newer version of BrowserAI (suite) is already installed* | *Version 9.9.9 is currently installed, which is newer than this installer. Downgrading is not recommended and may cause problems. Continue anyway?* | `Downgrade` | 572x201 |

Each body ends with the same `Installed at:` line, and a scratch directory holding
one text file and no install got the same-version dialog, `Repair` included.
The English strings are `locales/en-US.ftl:35-41`, byte-identical to 1.2.0, as is
`dialogs.rs`. Whichever label it wears, taking it runs the **full
`install_impl`**: the same code path as a first install, over an emptied
directory, with the old root renamed aside and deleted on success.

**Three things follow, and the third is the one that surprises.**

- **`--silent` skips the prompt entirely.** `show_overwrite_repair_dialog` returns
  `true` before doing anything when `get_silent()`. So the suite's own installer
  arms never meet it, and neither does any automated install.
- **Cancel exits 0.** A caller that only reads the exit code cannot tell a
  cancelled install from a completed one; the log's
  `Installation completed successfully!` is the line that separates them.
  *Added 2026-09-24*: **so does a prompt nobody answers**, after 300 s, which
  makes an unattended non-silent run over an existing install an exit-0 no-op.
- ⚠️ **On a same-version re-ship the button a user is asked to press says
  `Repair`.** The version number on the release page has not moved, and the
  installer says the application is *already installed* -- so the correct action
  reads as a repair of something broken and not as *install the new build of
  this number*. There is nothing to configure here: it is Velopack's wording,
  chosen from the version comparison. It matters before anybody is told
  to "just run the installer" after a re-ship. `[FLOATS]`

**Re-establish it** by running any non-silent `Setup.exe` against a root that
already holds an install and reading the top-level windows of its pid --
[`docs/probes/2026-09-16-release/Read-Dialog.ps1`](../../docs/probes/2026-09-16-release/README.md)
enumerates the children and
`Add-Type -AssemblyName UIAutomationClient` reads the task dialog's text, which
`GetWindowTextW` cannot because the body is a `DirectUIHWND`.
*Added 2026-09-24*: the suite's test pack over a scratch root reproduces all of
it without touching the developer's own install, and that is how the 2026-09-24
readings were taken. The buttons expose no UIA `InvokePattern`, and `WM_CLOSE`
dismisses the dialog down the same arm a Cancel takes (`dialogs.rs:244-255` maps
every result but buttons 0 and 1 to cancel). ⚠️ **The dialog is still a modal
window on the developer's screen for as long as it is up**, up to 300 s, which is
why this stays manual.

*Added 2026-09-24, later the same day*: **a real mouse click on `Cancel` does
what `WM_CLOSE` does.** Measured over a silent install of the test pack into a
scratch root of 211 files: the click landed on the dialog's own `Cancel` at the
point its rectangle gave, `Setup.exe` exited **0** logging
`Directory already exists, and user cancelled overwrite.`, and the root still held
211 files of the same total size, with no `Renaming` line in the log. Run `I2` of
the [evidence](../../docs/evidence/2026-09-24-velopack-rows/README.md).

## Not verified

MSI/PerMachine, signing, `--runtime win7`, `autoApply=true` with a staged
package, behaviour on older Windows, stdin inheritance through the stub.

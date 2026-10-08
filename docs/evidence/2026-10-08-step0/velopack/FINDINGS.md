<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I ran all five measurements. Each was repeated at least 3 times, apart from 5, which is a source reading. Everything was cleaned up afterwards and both locks are free.

**FINDINGS.md was not written.** The harness refused the write: "Subagents should return findings as text, not write report files". This reply carries its content. The per-run tables are in `C:\Source\SixFive7\BrowserAI\.work\step0-velopack\tables.md` and the per-process digest is in `...\runs\full-20261008T141238Z\analysis.txt`. Each run's journals, Velopack log slice and install state before and after are in `...\runs\full-20261008T141238Z\logs\<run>\`. You can write the file from this reply if you want it.

**Setup:** Windows 11 Pro 10.0.26300, SDK 10.0.401 with runtime 10.0.12, measured 2026-10-08 between 14:09Z and 14:21Z.
- The Velopack package is 1.2.161, and its NuGet contentHash `OpHNac...` matches the one in `src/BrowserAI/packages.lock.json`.
- The global vpk is 1.2.161, and Setup.exe and Update.exe report 1.2.161 in their own logs.
- Source line numbers below are from tag 1.2.161, commit `92d6a1c`, cloned into `velopack-src\`.

## Answers

**1. A local folder as the update source**
- The class is `Velopack.Sources.SimpleFileSource(DirectoryInfo)`. `new UpdateManager("<path>")` builds the same class for any path that is not an HTTP URL (`UpdateManager.cs:535-546`). It reads `releases.win.json`, and "downloading" is a file copy plus a SHA256 check.
- **Finding 1.0.1:** with 1.0.0 installed and a folder holding 1.0.0 and 1.0.1, `CheckForUpdatesAsync` returned 1.0.1, 3 of 3.
- **Download and apply:** worked 10 of 10 times when nothing was blocking it (7 with restart, 3 without). Downloads took 11 to 84 ms.
- **Pre-release in the folder:** it takes the highest version, pre-release or not.
  - With 1.0.2-alpha.1 added, it picked 1.0.2-alpha.1 over 1.0.1, 3 of 3.
  - With 1.0.2 also added, it picked 1.0.2, 3 of 3.
  - The choice is a plain version maximum (`UpdateManager.cs:139`). `UpdateOptions` has no setting that filters pre-releases.
- **Extra test:** the same alpha packed into a separate `beta` channel in the same folder was ignored by a normal check (1.0.1, 3 of 3). It was found when the check asked for channel `beta` (3 of 3).

**2. A successful apply with restart** (3 of 3, plus 4 more restarts in later runs, all identical)
- **How it was called:** `WaitExitThenApplyUpdates(asset, silent: true, restart: true, args)`, the same call BrowserAI makes.
- **What starts:** `<root>\current\BrowserAI.Measure.exe` from the new version.
- **Arguments:** exactly the ones I passed, and nothing added. That included `two words`, an embedded quote and a trailing backslash, all intact.
- **VELOPACK variables:** only `VELOPACK_RESTART=true`, 7 of 7. `Run()` clears it, and the restarted hook fires.
- **Parent:** the Update.exe that the app started (same pid and creation time). It had always already exited, 8.9 to 17.3 ms after starting the app.
- **Other details:** the working folder is `current\`, it is not in a job object, and the creation flags are 1024, which does not suppress a console window. Update.exe start to new process took 1.8 to 2.1 s.

**3. A failed apply with restart** (3 of 3, plus 1 in the dry run)
- **How I made it fail:** my own PowerShell process, outside the install root, held a file inside `current\` open with no sharing.
- **What Update.exe did:**
  1. Ran the old version's obsolete hook.
  2. Made two passes that end every process under the install root.
  3. Tried to rename `current\` and got "Access is denied", 11 tries one second apart.
  4. Deleted the new version it had extracted to a temp folder.
  5. Exited with code 1 after 11.0 to 11.3 s.
- **Rollback:** none was needed and none ran. The rename is the first step that changes the install, and that step failed. `current\` was identical by hash before and after, 3 of 3.
- **Restart:** it restarted the **old** version with the same arguments and `VELOPACK_RESTART=true`. The old version's restarted hook fired as if an update had happened. `Run()` logged "Launching app is out-dated. Current: 1.0.3, Newest Local Available: 1.0.4".
- **The log** is `%LOCALAPPDATA%\velopack\velopack_BrowserAI.Measure.log`. It ends with "Unable to start the update, because one or more running processes prevented it...".
- **Back to working:** after the file was released, the app started as 1.0.3 and exited 0, 3 of 3. A normal apply of the 1.0.4 package, still downloaded, then succeeded.

**4. The `--veloapp-updated` hook**
- **Time limit:** the hook was killed 15.008, 15.009 and 15.014 s after it started, exit code 1. Update.exe logged "[ERROR] Process timed out after 15s and was killed.", 3 of 3.
- **The update still completed** and the app was restarted, 3 of 3.
- **The kb's "60 s":** that is the uninstall hook's limit and the limit on waiting for the calling app to exit, not this hook's (`uninstall.rs:24`, `util_common.rs:16`).
- **Detached child of the stand-in, started by the hook:** killed 462, 584 and 490 ms after the hook returned, exit code 1. Update.exe ends every process it finds under the install root after each hook; it logged "Killing process: ...\current\BrowserAI.Measure.exe". The child was dead before Update.exe exited, 3 of 3.
- **Extra control:** a PowerShell child started from outside the install root ran its full 90 s and exited 0. It outlived Update.exe by about 86 s, 3 of 3.

**5. Does Velopack check on its own, debounce, or store a check time?** No to all three. This is from reading the 1.2.161 source; nothing was run for it.
- **Never on its own:** `VelopackApp.Run()` (`VelopackApp.cs:172-287`) never creates an update manager or contacts a feed. It only applies an already-downloaded package when auto-apply is on (`:242-251`; on by default at `:35`, and BrowserAI turns it off).
- **No timers:** no file in the C# library mentions `Timer`; the same search does find `splash.rs`, so it works.
- **Update.exe cannot check:** its commands are apply, start, patch, uninstall and update-self (`update.rs:16-70`).
- **No debounce:** each `CheckForUpdatesAsync` call fetches the feed once (`UpdateManager.cs:128-170`, the fetch at `:136`), and the web source sends no cache headers (`SimpleWebSource.cs:40-66`).
- **No stored check time:** the only thing a check writes is a staging GUID in `packages\.betaId` (`UpdateManager.cs:132`, then `VelopackLocator.cs:177-202`). I saw it created on the first check (35 bytes, no time in it) and read back on later checks.

## What surprised me
1. A pre-release in the normal feed beats a lower stable version. Only a separate channel keeps it out.
2. From inside the app, a failed apply looks like a successful one. The restarted hook fires, and Update.exe's exit code 1 reaches nobody.
3. One open file handle anywhere under `current\` blocks every update for about 11 s. By then the obsolete hook and two process-ending passes have already run.
4. The download step changes the install before any apply. It overwrites `Update.exe` and deletes the installed version's own package (`UpdateManager.cs:290-313`).
5. A hook killed at 15 s does not fail the update, and the app is never told.
6. The restarted process's parent is always already gone.
7. `ApplyUpdatesAndRestart` runs Update.exe without `--silent`, so it would show a progress window (`UpdateManager.cs:327`). I read this but did not run it, because of the no-window rule.
8. A source mismatch: the C# comment says the uninstall hook gets 30 s, but Update.exe gives it 60 s.
9. Without `--runtime`, vpk bundles the 32-bit Update.exe and Setup.exe, as BrowserAI's own packs do.

## Cleanup proof
- **Uninstall:** I ran `Update.exe --uninstall --silent` (exit 0). It removed the Start Menu shortcut and scheduled a `cmd`/`choice` that deleted the install folder; I followed both by parent pid and they exited after 3 s.
- **Install folder, shortcut and registry:** `%LOCALAPPDATA%\BrowserAI.Measure`, `Start Menu\Programs\BrowserAI.Measure.lnk`, the `HKCU\...\Uninstall\BrowserAI.Measure` key and `%TEMP%\velopack_BrowserAI.Measure` are all absent. Each was present during the install, so the check can see them; no Desktop shortcut was ever created.
- **Processes:** I recorded 106 pid and creation-time pairs across both sessions, and none is alive (`verify-gone.json`). The check found its own process as a positive control. No process image is under the install root; the same filter applied to PowerShell's folder found 1.
- **Logs:** Velopack's own log for this app did not exist before. I copied it into the session folders and deleted it, since uninstall does not remove it. The shared `velopack.log` is unchanged in size and time.
- **BrowserAI.app:** untouched.

## Method
- **Stand-in:** a .NET 10 WinExe in `app\`, framework-dependent, with Velopack pinned to exactly 1.2.161. The repository's `.editorconfig` is enforced at build and the build is clean. `VelopackApp.Build().SetAutoApplyOnStartup(false)...Run()` runs with all six hooks. Before `Run()` it takes an in-memory snapshot of arguments, raw command line, VELOPACK variables and parent, then writes one log file per process.
- **Hook behaviour:** the `--veloapp-updated` hook reads `control.json` to decide how long to stay alive and which children to start.
- **Packing:** `pack.ps1` uses BrowserAI's own vpk flags (`--shortcuts StartMenuRoot --delta None`). Versions in order: 1.0.0, 1.0.1, 1.0.2-alpha.1, 1.0.2, then 1.0.3 to 1.0.10, plus the alpha again in a `beta` channel. After each pack the feed folder was copied to `feeds\after-<version>`.
- **Driver:** `orchestrate.ps1 -Mode full` took the suite lock and the installer lock, then installed 1.0.0 with `Setup.exe --silent --log` and ran the measurements.
- **Run order:** 15 checks; M2 three times; M3 three times, each followed by a start of the app; one recovery apply; M4a and M4b three times each; then the uninstall.
- **Observation only:** it never stopped a process. It opened read and wait handles only to pids it had started, or pids a log named together with their creation time, and read exit codes from those.
- **Supporting scripts:** `analyze.ps1`, `tables.py` and `verify-gone.ps1`.

## Report items RULES.txt asks for
- **Commits and pushes:** none. Nothing in the repository was edited.
- **Left:** FINDINGS.md (blocked, see above). Possible kb entries for the root to decide: the pre-release choice, the failed-apply restart, the side effects of the download step, the 15 s hook kill and the child kill timing.
- **Decisions taken for review:**
  - I added versions up to 1.0.10 so every repeat is a normal forward update.
  - I used `WaitExitThenApplyUpdates` with silent instead of `ApplyUpdatesAndRestart`, which would show a window.
  - The app is framework-dependent instead of NativeAOT.
  - I took both locks.
  - Setup.exe got `--log`, so it did not write the shared `velopack.log`.
  - I deleted Velopack's per-app log after copying it.
- **What went wrong:**
  - In the dry run my driver misread timestamps and skipped watching 4 short-lived processes. Their own logs captured everything, so nothing was lost, and this was fixed before the full run.
  - The first beta-channel copy failed because vpk puts the channel name into the package file name (`-beta-full.nupkg`). Fixed.
- **Effect on another lane:** a suite test host (pid 108228) waited about 4 minutes for the installer lock while the full run held it. It took the lock at 14:20:13Z, the moment I released it.
- **Locks:** I held both from 14:09:17Z to 14:09:55Z (dry run) and from 14:12:39Z to 14:20:13Z (full run), then released them. Both were free when last checked.

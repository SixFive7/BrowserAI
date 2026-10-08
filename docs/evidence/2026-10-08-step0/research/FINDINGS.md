<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Step 0 research: Velopack restart and failure, toasts, Task Scheduler, input polling

Researched 2026-10-08 for the next BrowserAI build. Read-only: no window, no toast, no
scheduled task, no install, nothing under `%LOCALAPPDATA%\BrowserAI.app` touched, and no
claude or codex invocation.

## How to read this

Every answer names its source and how it was obtained:

- **[CODE]** read in source code at the pinned revision, file and line given.
- **[DOC]** read in vendor documentation, URL and section given.
- **[KB]** already measured in this repository, cited by file and section, not re-measured here.
- **[NOT FOUND]** looked for and absent; the place looked is named.

Pinned sources:

- **Velopack 1.2.161** = tag `1.2.161`, commit `92d6a1c91716729d449034df5c50307dcce39493`
  (2026-09-29). Shallow clone: `.work/step0-research/velopack-1.2.161`. GitHub compare
  `1.2.158...1.2.161`: ahead by 3, behind by 0, merge base `3c7f52c1` (the commit the kb read
  1.2.158 at). Files changed: `src/bins/src/shared/runtime_arch.rs`, plus `package.json` and
  `package-lock.json` under `src/lib-nodejs` and `samples/NodeJSElectron`. So every kb reading of
  apply, hooks and the kill pass taken at 1.2.158 is the same source at 1.2.161. That is identity
  of source, not a re-measurement.
- **Velopack docs** = `velopack/velopack.docs` main @ `1ca8eea6017fb9c5743e070575a9f28da08c26c1`
  (2026-09-30), the source of docs.velopack.io. Clone: `.work/step0-research/velopack-docs`.
  A path `docs/x/y.mdx` is the page `https://docs.velopack.io/x/y`.
- **Microsoft Learn** pages fetched 2026-10-08, URLs below.
- **Windows SDK 10.0.26100.0** headers on this machine,
  `C:\Program Files (x86)\Windows Kits\10\Include\10.0.26100.0\winrt\`.
- **microsoft/WindowsAppSDK** main, `dev/AppNotifications/AppNotificationUtility.cpp` (last commit
  to the file `f753170e`, 2024-06-30). Copy: `.work/step0-research/wasdk-AppNotificationUtility.cpp`.

The scratch copies are a convenience, not a record: every line cited can be re-read at the pinned
revision.

---

## A. Velopack 1.2.161

### A1. What restarts the program after an apply, which executable, which arguments, and can ours be passed

**Answer.** Update.exe itself restarts it, as the last step of `apply`, with `CreateProcessW` on the
main executable named in the manifest (`<root>\current\<mainExe>`, for BrowserAI `current\BrowserAI.exe`,
the configuration app). Our own arguments pass through verbatim, after `--`. The environment is
Update.exe's own (which it inherited from us) plus `VELOPACK_RESTART=true`. Velopack adds no
argument of its own. [CODE] [DOC]

The chain, at tag 1.2.161:

1. `UpdateManager.ApplyUpdatesAndRestart(asset, restartArgs)` is
   `WaitExitThenApplyUpdates(asset, silent: false, restart: true, restartArgs)` followed by
   `Environment.Exit(0)` (`src/lib-csharp/UpdateManager.cs:325-329`).
   `WaitExitThenApplyUpdates` only starts Update.exe with this process's pid as `--waitPid`
   (`:354-357`); its caller must exit by itself. `ApplyUpdatesAndExit` is `silent: true, restart: false`
   (`:338-342`).
2. `UpdateExe.Apply` builds
   `[--silent] apply [--package <packagesDir>\<file>] [--waitPid <pid>] [--norestart] --rootDir <root> --packageDir <packagesDir> [-- <restartArgs...>]`
   (`src/lib-csharp/UpdateExe.cs:58-103`). `--norestart` is added when `restart` is false (`:81`).
   **`restartArgs` are appended only when `restart` is true** (`:88-93`). Update.exe is started with
   `Process.Start`, `CreateNoWindow = true`, working directory the root, our environment inherited,
   then `AllowSetForegroundWindow(pid)` is attempted (`src/lib-csharp/Locators/DefaultProcessImpl.cs:43-71`).
3. In Update.exe restart is the default and `--norestart` turns it off
   (`src/bins/src/update.rs:22`, `:261-267`). Everything after `--` is kept verbatim, flags included
   (`update.rs:79-85`; `EXE_ARGS ... .last(true)` at `:26`).
4. `commands::apply` waits for `--waitPid` up to 60 s (`src/bins/src/shared/util_common.rs:14-26`),
   applies, and on success calls `start_package(&applied_locator, exe_args, Some(VELOPACK_RESTART))`
   (`src/bins/src/commands/apply.rs:45-55`). `applied_locator` carries the NEW manifest, so the
   program started is the new manifest's `mainExe`.
5. `start_package` starts `current\<mainExe>` with exactly `exe_args`, working directory `current\`
   (`src/bins/src/shared/util_windows.rs:104-119`). The environment block copies Update.exe's own
   variables, skipping any whose value is empty, then adds `VELOPACK_RESTART=true`
   (`src/lib-rust/src/process_win.rs:135-175`). Creation flags are `CREATE_UNICODE_ENVIRONMENT` only,
   then `AllowSetForegroundWindow(pid)` (`process_win.rs:352-412`).
6. The restart runs after the `--veloapp-updated` hook and its kill pass, the removal of the temp
   directories, the shortcut rewrite, the Update.exe sync and the stub extraction
   (`src/bins/src/commands/apply_windows_impl.rs:217-272`).
7. In the restarted process `VelopackApp.Run()` reads and clears `VELOPACK_RESTART`, skips its
   auto-apply because of it, and calls `OnRestarted(currentVersion)`
   (`src/lib-csharp/VelopackApp.cs:232-251`, `:280-286`).

Docs: `docs/reference/cli/content/update-windows.mdx:21-27`
(`update.exe apply [OPTIONS] [-- [EXE_ARGS]...]`, "Arguments to pass to the started executable.
Must be preceded by '--'."); `docs/reference/cs/Velopack/UpdateExe.md:63` (`restartArgs`: "The
arguments to pass to the application when it is restarted.", generated from 1.2.0);
`docs/integrating/hooks.mdx:30` and `docs/troubleshooting/debugging.mdx:66` (`VELOPACK_RESTART`).

Two consequences for the build:

- `ApplyUpdatesAndRestart` is **not silent**. A non-silent Update.exe shows its progress window
  (`src/l18n/src/progress.rs:76-94`: a window unless silent) and arms 300 s dialogs
  (`update.rs:169-171`). The silent route with a restart is
  `WaitExitThenApplyUpdates(asset, silent: true, restart: true, restartArgs)` followed by our own exit;
  Velopack's docs: "If your app has not exited within 60 seconds it will be killed"
  (`docs/integrating/overview.mdx:117`).
- Environment variables set in our own process before the call reach the restarted process
  (ours -> Update.exe -> restarted app). Arguments and environment are the two channels; neither
  differs between success and failure (A2).

### A2. When an apply fails

**Answer.** There is no rollback of a half-done swap at 1.2.161. Failures before the swap leave
`current\` untouched. When a restart was requested, Update.exe restarts the **old** version with the
**same** arguments and the **same** `VELOPACK_RESTART=true`, so nothing in the arguments or the
environment says the update failed. Update.exe exits 1, writes the error to its log, and shows no
error dialog. [CODE] [DOC, partly]

What each failure does, in the order `apply_package_impl` runs (`src/bins/src/commands/apply_windows_impl.rs`):

| Failure | Where | Effect |
|---|---|---|
| Package unreadable (bundle or manifest) | `:50-59` | **The package file is deleted** "to prevent update loop"; error. `current\` untouched |
| Packages lock held by a download or another apply | `:113`, `src/lib-rust/src/locator.rs:337-344` | Error. Untouched |
| A missing prerequisite, and the user cancels | `:120-127` | Error. Untouched |
| Extraction fails | `:138-147` | Package deleted; error. Untouched |
| `--veloapp-obsolete` hook fails or times out | `:151-156` | Ignored: "don't care if it fails" |
| `current\` cannot be renamed away (a file in it held open), after 1 try and 10 retries 1 s apart | `:158-163`, `retry_io_ex` at `util_common.rs:36-52` | Error *"Unable to start the update, because one or more running processes prevented it. Try again later, or if the issue persists, restart your computer."* `current\` untouched; the package stays staged |
| The new tree cannot be renamed into `current\`, after 1 try and 30 retries 1 s apart | `:172-177` | Error *"Unable to complete the update, and the app was left in a broken state. You may need to re-install or repair this application manually."* The comment says "if this fails we will yolo a rollback..." and **no rollback code follows**. The cleanup after the closure removes both temp directories unconditionally (`:275-278`), and the second of them **is the old `current\`** (`packages\VelopackTemp\tmp_<16 chars>`, `locator.rs:206-213`). Result: no `current\` at all |
| `--veloapp-updated` hook fails or times out | `:217-222` | Logged; return value discarded. "from this point on, we're past the point of no return and should not bail" (`:207`) |

Then `commands::apply` (`src/bins/src/commands/apply.rs:57-63`): on an error with `restart`, it calls
`start_package(locator, exe_args, Some(VELOPACK_RESTART))` with the **original** locator, so the old
`current\<mainExe>`, the same `exe_args`, `VELOPACK_RESTART=true`; then
`bail!("Error applying package: ...")`. When no package can be found at all, the old version is
restarted the same way (`:65-70`). In the broken-state row the old executable no longer exists,
`start_package` fails (`util_windows.rs:108-110`) and nothing is started.

- Exit code: 0 on success, 1 on any error (`src/bins/src/update.rs:128-136`). No dialog: "Update.exe
  runs unattended, so we never surface a dialog for genuine errors" (`update.rs:141-144`); the old
  error dialog is commented out (`apply_windows_impl.rs:187-204`).
- Log: `%LocalAppData%\velopack\velopack_<packId>.log` (`src/lib-rust/src/logging.rs:57-72`), so
  `velopack_BrowserAI.app.log` for the shipping pack id `BrowserAI.app` [KB kb/packaging/velopack.md].
- The restarted old process gets `OnRestarted` with the **old** version (`VelopackApp.cs:280-286`).
  It can tell a failure only by (a) comparing its version with one it was told to expect, which
  BrowserAI can put in `restartArgs`, (b) `UpdatePendingRestart` still returning a staged package
  (`UpdateManager.cs:39-46`), true after a rename failure and false after a bad-package failure
  because that package was deleted, or (c) reading the log.

Docs: "If true, restarts the application after updates are applied (or if they failed)"
(`docs/reference/cs/Velopack/UpdateExe.md:62`); "`VELOPACK_RESTART` is true if the application was
restarted by Velopack (usually because an update was applied.)" (`docs/integrating/hooks.mdx:30`).
[NOT FOUND] in the docs: any rollback, any failure signal to the restarted app, the broken-state
case (grep of `docs/` for rollback, fail, broken state, restart).

### A3. How long `--veloapp-updated` may run, and what happens to processes it starts

**Answer.** 15 seconds. Update.exe waits only on the hook process itself and on timeout
TerminateProcess-es that one process, not its tree. Then, whatever happened, it terminates every
process whose image path is under the install root. Processes the hook started outside the root are
neither waited for nor killed. [CODE] [DOC]

- Call: `run_hook(&new_locator, "--veloapp-updated", 15)`, after the swap
  (`apply_windows_impl.rs:217-222`). Started as `current\<mainExe> --veloapp-updated <version>`,
  working directory `current\`, `CREATE_NO_WINDOW`, Update.exe's environment with nothing added
  (`src/bins/src/windows/util.rs:19-29`).
- Wait: on the hook's own handle, 15 s (`util.rs:38`). Exit 0 logs success, non-zero logs a warning,
  a timeout calls `TerminateProcess(handle, 1)` on the hook process only (`util.rs:50-53`,
  `process_win.rs:427-436`). No job-object API appears anywhere in `src/bins/src` or
  `src/lib-rust/src` (grep, with `TerminateProcess` as the positive control), so a kill never reaches
  the hook's children.
- Always afterwards: `force_stop_package(root_dir)`, commented "in case the hook left running
  processes" (`util.rs:59-60`). Every process whose image is under the root is terminated with exit
  code 1, Update.exe itself excepted (`util_windows.rs:81-102`).
- C# side: `VelopackApp.Run()` runs the `OnAfterUpdateFastCallback`, then `Exit(0)`, or `Exit(-1)` on
  an exception (`VelopackApp.cs:211-223`).
- Docs: "`--veloapp-updated {version}` Runs on the new version of the app, after an update is
  applied. App must handle and exit within 15 seconds.", "you may not show any UI to the user", and
  "If your application receives one of these arguments and does not exit within the alloted time, it
  will be killed." (`docs/integrating/hooks.mdx:17-23`; 15 s again at `overview.mdx:59`).
  [NOT FOUND] in the docs: anything about the hook's own child processes.
- Other limits in code: install 30 s (`install.rs:245`), obsolete 15 s (`apply_windows_impl.rs:153`),
  uninstall **60 s** (`uninstall.rs:24`). The docs say uninstall 30 s (`hooks.mdx:18`,
  `overview.mdx:59`): docs and code disagree, and the kb already records 60 s.

So a process the hook starts from `current\`, directly or through the Task Scheduler, is in the kill
set if it is alive when the post-hook pass runs, and survives if it starts after it. That is a race.

### A4. Does Velopack check on a schedule, debounce or rate-limit checks, or store the last check time

**Answer.** Confirmed: none of the three. [CODE] [DOC]

- `CheckForUpdatesAsync` reads the feed once per call and returns (`UpdateManager.cs:128-170`). The
  only state a check persists is the staging id `packages\.betaId`
  (`src/lib-csharp/Locators/VelopackLocator.cs:180-206`). A grep for `File.Write/Create/Copy/Move`
  over `src/lib-csharp` finds that write, the package copy and move, the `Update.exe` extraction and
  copy, a delete-on-close writability probe and a symlink helper; none stores a time.
- A grep of `src/lib-csharp`, `src/lib-rust/src`, `src/bins/src` for timers, debounce, throttle,
  last check, interval and rate limit found only a progress-report "debounce" comment
  (`Sources/HttpClientFileDownloader.cs:104`) and GitHub rate-limit doc comments. Those hits are the
  positive control that the search matches.
- Docs: the app calls `CheckForUpdatesAsync` itself (`docs/integrating/overview.mdx:65-84`);
  "Updates can be done silently in the background, or integrated into your application UI. It's
  always up to you." (`:86-88`). A grep of `docs/` and `blog/` for periodic, interval, timer,
  schedule, throttle, debounce, rate limit, last check found only GitHub's unauthenticated limit of
  60 requests per hour for `GithubSource` (`docs/integrating/update-sources.mdx:42`).
- The one guard is the per-app `.velopack_lock`, taken by download and apply
  (`overview.mdx:111-112`). It serialises operations; it limits nothing in time.

### A5. A local folder as an update source

**Answer.** `Velopack.Sources.SimpleFileSource(DirectoryInfo)`. `new UpdateManager("C:\\path")`
resolves to it for any string that is not an HTTP URL. The channel picks the file it reads; there is
no pre-release switch. [CODE] [DOC]

- `CreateSimpleSource`: HTTP URL -> `SimpleWebSource`, anything else ->
  `SimpleFileSource(new DirectoryInfo(urlOrPath))` (`UpdateManager.cs:535-546`).
- It reads `<dir>\releases.{channel}.json` when present (`src/lib-csharp/Sources/SimpleFileSource.cs:36-41`);
  otherwise it warns and scans `*.nupkg`, keeping only packages whose channel metadata equals the
  requested channel (`:43-71`). A missing directory logs an error and returns an empty feed
  (`:31-34`), which `CheckForUpdatesAsync` reports as "No remote full releases found" and null.
- Download is a `File.Copy` into the packages directory (`:75-84`), then the same checksum check as a
  web download (`UpdateManager.cs:282-286`).
- Channel: `UpdateOptions.ExplicitChannel ?? <channel in sq.version> ?? OS short name`
  (`UpdateManager.cs:64`, `:111`); it decides the file name `releases.{channel}.json`.
- Pre-release: `SimpleFileSource` has none. Only `GithubSource` and `GiteaSource` take `prerelease`,
  and `GitlabSource` `upcomingRelease` (`docs/integrating/update-sources.mdx:38-60`). A version with a
  pre-release suffix in a local feed is an ordinary semver: the highest full version above the
  installed one is offered (`UpdateManager.cs:139-149`). The rule that an installed pre-release
  checks nothing is BrowserAI's own [KB kb/packaging/velopack.md "How often the feed is asked"].
- Docs: `docs/integrating/update-sources.mdx:27-31` ("Reads the feed and packages from a local
  directory. ... Useful for testing or for apps that update from a network/USB share."),
  `overview.mdx:91`, `testing.mdx:46` and `:57-61`, `docs/distributing/self-hosting.mdx:44-53`
  (`vpk upload local --path`, `--regenerate`).
- Already in the kb: the nupkg-scan fallback exists in `SimpleFileSource` and not in
  `SimpleWebSource` [KB kb/packaging/velopack.md "Nothing anywhere reads RELEASES ..."].

---

## B. Toasts from an unpackaged Win32 app under `velopack.BrowserAI.app`

Already measured in the kb and not repeated here: the AUMID is set by Velopack on the shortcut and in
every installed process; a NativeAOT binary raises toasts through hand-written WinRT calls; a
`CustomActivator` COM activator receives clicks and the dropdown with no raiser running; the X is
heard only by a running raiser; buttons lay out in one row and five labels truncate
[KB kb/windows/notifications.md].

### B1. Updating a toast already on screen

**Answer.** Bindable and updatable in place: all four progress-bar fields (`title`, `value`,
`valueStringOverride`, `status`) **and the text of the top-level `<text>` lines**, so a plain line
(the title included) can carry a live countdown without any progress bar. Text inside groups or
subgroups cannot be bound. The API is `ToastNotifier.Update(NotificationData, tag[, group])`,
returning `Succeeded`, `Failed` or `NotificationNotFound`. A sequence number orders updates (greatest
non-zero wins, 0 means always apply). No rate limit is documented. [DOC] [CODE: SDK headers]

- "The following elements in app notifications support data binding: All properties on
  AppNotificationProgressBar; The Text property on the top-level text elements."
  ([progress bar and data binding](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-progress-bar),
  section "Elements that support data binding"; also served at
  `/windows/apps/design/shell/tiles-and-notifications/toast-progress-bar`). "Text elements support
  data binding, which allows you to update text content after the notification is displayed."
  ([app notification content, Text elements](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-content#text-elements)).
  `AdaptiveText.Text`: "Data binding only works for top-level text elements."
  ([schema, AdaptiveText](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-schema#itoastbindinggenericchild)).
  Up to three top-level text elements; `status` is required on a progress bar
  ([toast schema, progress](https://learn.microsoft.com/uwp/schemas/tiles/toastschema/element-progress)).
- Payload syntax: a bound field is written `{name}` in the XML, shown for the progress fields
  ("Binds to {progressTitle} in xml payload",
  [AppNotificationProgressBar](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.builder.appnotificationprogressbar)).
  A `<text>{countdown}</text>` line follows that convention; no page shows the XML for a bound text
  line. [NOT FOUND] for the text form.
- Behaviour of an update: "the notification stays in the same position in Notification Center";
  "Won't reappear as a popup; the notification's data is silently updated within Notification
  Center"; "If the user dismissed the notification, the update will fail."; the method "returns a
  NotificationUpdateResult that indicates whether the update succeeded or whether the notification
  couldn't be found (the user may have dismissed it)" (progress bar page, "Update a progress bar with
  data binding" and "Update or replace a notification").
- [ToastNotifier.Update](https://learn.microsoft.com/uwp/api/windows.ui.notifications.toastnotifier.update):
  two overloads, tag and tag+group, Windows 10 15063+.
  [NotificationData.SequenceNumber](https://learn.microsoft.com/uwp/api/windows.ui.notifications.notificationdata.sequencenumber):
  "When multiple NotificationData objects are received, the system displays the NotificationData
  with the greatest non-zero number. Setting this value to 0 causes it to always displays." The
  progress page: "Increment the sequence number so the platform knows this is a newer update."
- Limits found: [Tag](https://learn.microsoft.com/uwp/api/windows.ui.notifications.toastnotification.tag)
  "can be maximum 16 characters long. However, the Creators Update (15063) extends this limit to 64
  characters." **Rate of updates: [NOT FOUND]** on the progress-bar page, `ToastNotifier.Update`,
  `NotificationData`, `SequenceNumber`.
- For the hand-written NativeAOT path, read from the SDK 10.0.26100.0 IDL
  (`winrt\windows.ui.notifications.idl`) and header; slot numbers count `IInspectable`'s six first,
  in IDL order:

| Interface | IID | Members (slot) | Line |
|---|---|---|---|
| `INotificationData` | `9FFD2312-9D6A-4AAF-B6AC-FF17F0C1F280` | `get_Values` (6), `get_SequenceNumber` (7), `put_SequenceNumber` (8) | idl:946-952 |
| `INotificationDataFactory` | `23C1E33A-1C10-46FB-8040-DEC384621CF8` | create with values (and sequence) | idl:956-961 |
| `IToastNotification4` | `15154935-28EA-4727-88E9-C58680E2D118` | `get_Data` (6), `put_Data` (7), `get_Priority` (8), `put_Priority` (9) | idl:1302-1309 |
| `IToastNotification6` | `43EBFE53-89AE-5C1E-A279-3AECFE9B6F54` | `get_ExpiresOnReboot` (6), `put_ExpiresOnReboot` (7) | idl:1313-1318 |
| `IToastNotifier2` | `354389C6-7C01-4BD5-9C20-604340CD2B74` | `UpdateWithTagAndGroup` (6), `UpdateWithTag` (7) | idl:1458-1463 |
| `IMap<HSTRING,HSTRING>` | `f6d1f700-49c2-52ae-8154-826f9908773c` | `Lookup` (6), `get_Size` (7), `HasKey` (8), `GetView` (9), `Insert` (10), `Remove` (11), `Clear` (12) | `windows.ui.notifications.h:1947-1949`, vtable at `:9659` onward |

  `NotificationData` is default-activatable (idl:1576-1584), so `RoActivateInstance` gives one;
  `NotificationUpdateResult` is `Succeeded = 0, Failed = 1, NotificationNotFound = 2` (idl:462-467).
  The initial values go on the toast through `IToastNotification4.put_Data` before `Show`.

### B2. Keeping a toast on screen until the person acts

**Answer.** `scenario="reminder"` keeps it on screen until dismissed or acted on, and it needs a
button. The two Microsoft pages disagree on which: the current content page says "at least one
button"; the XML schema says it "will be silently ignored unless there's a toast button action that
activates in background". Under Do Not Disturb, reminders get a banner only if the person's priority
settings allow reminders; otherwise the toast goes straight to Notification Center. A toast outlives
the process that raised it; its lifetime in Notification Center is 3 days by default and at most.
[DOC] [KB]

- [Toast schema, toast element](https://learn.microsoft.com/uwp/schemas/tiles/toastschema/element-toast),
  `scenario`: "reminder - A reminder notification. This will be displayed pre-expanded and stay on
  the user's screen till dismissed. Note that this will be silently ignored unless there's a toast
  button action that activates in background." "urgent - An important notification. This allows
  users to have more control over what apps can send them high-priority toast notifications that can
  break through Focus Assist (Do not Disturb). This can be modified in the notifications settings."
- [App notification content, Scenarios](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-content#scenarios):
  "In the reminder scenario, the notification will stay on screen until the user dismisses it or
  takes action. ... A reminder sound will be played. You must provide at least one button on your app
  notification. Otherwise, the notification will be treated as a normal notification." Important
  (urgent) notifications: "Requires: ... Windows Insider Preview Build 22546 or later"; the page's
  screenshot shows a system notification offering the person to allow or disallow urgent
  notifications from the app.
- For a desktop app "background" changes nothing about delivery: "Setting `activationType="background"`
  in the notification XML payload is ignored for desktop apps"
  ([Windows App SDK quickstart, Handle activation](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart#handle-activation-from-an-app-notification)).
  Whether the reminder behaviour still keys on that attribute for an unpackaged app is [NOT FOUND].
  The kb's measured reminders all carried `activationType="background"` buttons
  (`docs/design/toast-2026-09-24/v1.xml` to `v3.xml`) [KB], so a reminder with only foreground,
  protocol or `system` buttons has not been observed here.
- Do Not Disturb: "With do not disturb on, you will only receive banners for alarms, reminders, and
  apps of your choice. Other notifications are sent directly to the notification center until you
  turn it off." and "Under Calls and reminders, select the checkboxes to allow incoming calls or
  reminders while do not disturb is on."
  ([support: Notifications and Do Not Disturb](https://support.microsoft.com/en-us/windows/notifications-and-do-not-disturb-in-windows-feeca47f-0baf-5680-16f0-8801db1a8466),
  read through a fetch tool that returned these two sentences; the default of the checkbox is not
  stated there). The settings catalog lists "Set Priority Notifications > Calls and reminders > Show
  reminders, regardless of app used", and for the automatic rules "When duplicating your display
  (priority notification banners are also hidden)" and "When using an app in full-screen mode
  (priority notification banners are also hidden)"
  ([settings catalog, System](https://learn.microsoft.com/windows/configuration/windows-backup/catalog#system)).
  "Set 'do not disturb' status manually or automatically, so that notifications will be sent directly
  to the notification center."
  ([settings reference, Do not disturb](https://learn.microsoft.com/windows/apps/develop/settings/settings-common#do-not-disturb)).
- Process exit: the notification belongs to the platform. "When the app is not running, Windows
  launches the app via COM activation" (quickstart, as above) presupposes a toast that outlives its
  raiser; the kb measured clicks on toasts whose raiser had exited [KB notifications.md "A click
  carries the dropdown's value only to a COM activator"]. No page states it for an on-screen reminder
  in so many words: [NOT FOUND].
- Lifetime: "The default and maximum expiration time for a notification is 3 days."
  ([manage app notifications, expiration](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/manage-app-notifications#set-an-expiration-time));
  "By default, local toast notifications expire in three days"
  ([delivery methods](https://learn.microsoft.com/windows/apps/develop/notifications/choosing-a-notification-delivery-method#local-notifications)).
  `ExpiresOnReboot` removes it at restart (same page; `IToastNotification6`, B1 table).
- Also documented and possibly useful: `afterActivationBehavior="pendingUpdate"` on an action:
  "After the user clicks a button on your toast, the notification will remain present, in a 'pending
  update' visual state. You should immediately update your toast ..."; "This only works on Desktop"
  ([toast schema, action](https://learn.microsoft.com/uwp/schemas/tiles/toastschema/element-action);
  [schema, ToastActivationOptions](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-schema#toastbutton)).
  Not measured here.

### B3. Replacing a toast in place with the same Tag and Group

**Answer.** A replacement moves to the top of Notification Center (it does not keep its position),
can pop up again unless `SuppressPopup` is set, and is shown even when the earlier one was
dismissed. An update through data binding keeps the position and never pops up. [DOC]

From the table in "Update or replace a notification"
([progress bar and data binding](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-progress-bar)):
Replacing "Moves the notification to the top of Notification Center", "Can completely change all
content and layout", "Can reappear as a popup if SuppressPopup is false (or set to true to silently
send it to Notification Center)", "Replacement notification is always sent regardless of whether the
user dismissed the previous notification." Updating "Leaves the notification in place", "Can only
change properties that support data binding", "Won't reappear as a popup", "If the user dismissed the
notification, the update will fail." The page's advice: update for frequent changes, replace for the
final step. What a replacement does to a banner that is on screen at that moment (swap in place or
new banner) is [NOT FOUND].

### B4. A button click after the raiser has exited or been killed

**Answer.** With a COM activator registered, foreground and background buttons both start our
executable through COM and deliver the arguments and inputs to
`INotificationActivationCallback::Activate`; this works with no process of ours running. A protocol
button starts the handler of that scheme with no process of ours running, and input values are lost.
Without an activator only the raiser's in-process `Activated` event exists, so a click after the
raiser is gone does nothing. [DOC] [CODE: WindowsAppSDK] [KB]

| Activation | No process of ours running | Source |
|---|---|---|
| foreground, with COM activator | Works: COM starts the `LocalServer32` command, `Activate` gets AUMID, `arguments`, inputs | [C++/WinRT COM activator walkthrough](https://learn.microsoft.com/windows/apps/develop/cpp-winrt/author-coclasses#a-more-realistic-and-interesting-example): the CLSID and its COM server path "is the mechanism by which a toast notification knows what class to create an instance of when its callback button is clicked (whether the notification is clicked in Action Center or not)"; the sample checks for `-Embedding`. [Quickstart](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart#handle-activation-from-an-app-notification): "When the app is not running, Windows launches the app via COM activation". [KB] measured `-ToastActivated -Embedding`, 21 to 40 ms |
| background, with COM activator | Same as foreground for a desktop app | Quickstart: "activationType=background ... is ignored for desktop apps"; [migration guide](https://learn.microsoft.com/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/toast-notifications): with the Toolkit, background activation "Arrives through same ToastNotificationManagerCompat.OnActivated event (or COM class for C++)", and "You must bring your window to the foreground if desired" |
| foreground or background, no activator | Nothing happens; the toast is consumed | [Activated event](https://learn.microsoft.com/uwp/api/windows.ui.notifications.toastnotification.activated): "Apps that are running subscribe to this event." [KB] measured the click lost for both kinds |
| protocol | Works: the scheme's handler is launched (for `https:` the default browser) | [Content, Buttons](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-content#buttons): "Another app is activated via protocol launch"; [schema, action](https://learn.microsoft.com/uwp/schemas/tiles/toastschema/element-action): "Launch a different app using protocol activation". [KB] measured the button's `arguments` arriving exactly and the dropdown value lost |
| system (snooze, dismiss) | Handled by Windows; no app activation is described | Content, Inputs, Snooze/dismiss |

Registration:

- Documented route: the Start-menu shortcut carries `System.AppUserModel.ToastActivatorCLSID`
  ("Used to CoCreate an INotificationActivationCallback interface to notify about toast activations",
  [property page](https://learn.microsoft.com/windows/win32/properties/props-system-appusermodel-toastactivatorclsid))
  and `HKCU\SOFTWARE\Classes\CLSID\{clsid}\LocalServer32` names the executable (C++/WinRT walkthrough,
  `update_registry()` and `create_shortcut()`). `Activate` remarks: "You also will need to create a
  shortcut on the start menu."
  ([Activate](https://learn.microsoft.com/windows/win32/api/notificationactivationcallback/nf-notificationactivationcallback-inotificationactivationcallback-activate));
  a failing `Activate` lets "the user ... try again".
- Registry-only route, `HKCU\Software\Classes\AppUserModelId\<aumid>` with `DisplayName`, `IconUri`
  and `CustomActivator = {clsid}`: **[NOT FOUND] in Microsoft Learn** (searched "CustomActivator").
  It is what the Windows App SDK writes for unpackaged apps
  (`dev/AppNotifications/AppNotificationUtility.cpp:298-328`, under `HKEY_CURRENT_USER`, with
  `LocalServer32 = "<exe>" ----AppNotificationActivated:` at `:114-136`) [CODE], and the kb measured it
  working beside Velopack's shortcut, which carries no activator CLSID [KB notifications.md].

### B5. How many short labels fit in the one button row

**Answer.** Not documented. The documented limit is the count: up to 5 buttons, context-menu items
included. [DOC] [NOT FOUND for widths] [KB]

- "You can only have up to 5 buttons (including context menu items which we discuss later)."
  ([Content, Buttons](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-content#buttons));
  `actions`: "up to five inputs and up to five button actions"
  ([schema, toast](https://learn.microsoft.com/uwp/schemas/tiles/toastschema/element-toast)).
- No per-label length, truncation rule or width is stated on the content page, the toast schema, the
  `action` element page or the schema's `ToastButton`. The Win32 UX guide gives lengths only for a
  notification's title (63 chars, 48 recommended) and body (255, 200 recommended)
  ([UX guide, notifications text](https://learn.microsoft.com/windows/win32/uxguide/mess-notif#text)).
- Closest evidence is the kb's rendering at 3840x2160, 96 DPI: two labels of 18 and 12 characters,
  *Review 27 sessions* and *Ask me later*, rendered whole; five labels truncated to their first word
  [KB notifications.md "Every button is laid out in ONE row"]. *Install now* is 11 characters and
  *Wait for inactivity* is 19, one longer than the longest label seen whole. Whether it fits is not
  established; it needs a rendering.

---

## C. Task Scheduler 2.0

### C1. `MultipleInstancesPolicy` `IgnoreNew` when a second run is requested

**Answer.** Documented: the new instance is not started. What the requester gets back is not
documented for `IgnoreNew`. [DOC] [NOT FOUND]

- "IgnoreNew: Default. Does not start a new instance if an existing instance of the task is
  running." ([MultipleInstancesPolicy element](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-multipleinstancespolicy-settingstype-element));
  `TASK_INSTANCES_IGNORE_NEW = 2`
  ([TASK_INSTANCES_POLICY](https://learn.microsoft.com/windows/win32/api/taskschd/ne-taskschd-task_instances_policy)).
- [IRegisteredTask::Run](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-run):
  "If this method succeeds, it returns S_OK"; `ppRunningTask` "defines the new instance of the task".
  The page gives two cases and neither is `IgnoreNew`: "This method will return without error, but
  the task will not run if the AllowDemandStart property ... is set to false", and a disabled task
  returns `SCHED_E_TASK_DISABLED`. The [RunEx](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-runex)
  page says a disabled task returns S_OK and does not run, so "S_OK and nothing started" is a
  documented pattern of this API family.
- [schtasks run](https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-run)
  describes no result for an instance already running.
- [Error and success constants](https://learn.microsoft.com/windows/win32/taskschd/task-scheduler-error-and-success-constants)
  define `SCHED_E_ALREADY_RUNNING 0x8004131F` "An instance of this task is already running.",
  `SCHED_E_TASK_ATTEMPTED 0x80041324` and `SCHED_S_TASK_QUEUED 0x00041325`. No page ties any of them
  to `Run` under `IgnoreNew`. Whether `Run` returns S_OK, one of these, or a running-task pointer to
  the existing instance needs a measurement.
- Already decided against, and why: `MultipleInstancesPolicy` is `Parallel` "where the default
  ignores a start while an instance runs: the coordinator's pipe decides which start is the
  coordinator, and an ignored start could be the only one a blocked server makes"
  (`src/BrowserAI.Core/Registration/SignInTask.cs:70-74`).

### C2. `End` and `schtasks /end` on a running instance

**Answer.** The service first sends `WM_CLOSE`; if the task does not respond, it uses
`TerminateProcess`, and only when `AllowHardTerminate` is true, which is the default. Whether child
processes are ended is not documented. [DOC] [NOT FOUND for children and timing]

- "Gets or sets a Boolean value that indicates that the task may be terminated by the Task Scheduler
  service using TerminateProcess. The service will try to close the running task by sending the
  WM_CLOSE notification, and if the task does not respond, the task will be terminated only if this
  property is set to true."
  ([ITaskSettings::AllowHardTerminate](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-itasksettings-get_allowhardterminate));
  default `true` ([AllowHardTerminate element](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-allowhardterminate-settingstype-element)).
- "Stops only the instances of a program started by a scheduled task. To stop other processes, you
  must use the TaskKill command."
  ([schtasks end](https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-end)).
  [IRunningTask::Stop](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-irunningtask-stop)
  and [IRegisteredTask::Stop](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-stop)
  give return codes only ("The IRegisteredTask::Stop function stops all instances of the task").
- [NOT FOUND] in the documentation: how long the service waits after `WM_CLOSE`, which windows it
  sends it to (a process with no top-level window has nothing to receive it), and whether children
  or the task's job are ended. The only statement found is a Microsoft Q&A answer, not product
  documentation, saying that a task stopped by its time limit leaves its child processes running
  ([Q&A 12500795](https://learn.microsoft.com/answers/a/12500795)).
- Bearing on BrowserAI [KB kb/windows/processes.md "A process the Task Scheduler starts keeps its
  job's processes through every client's exit"]: the scheduler's own job has limit flags `0x0` (no
  kill on close), and the coordinator's own kill-on-close job takes the session host and every browser
  with it "however it goes". A hard `End` of the coordinator therefore ends the browsers without
  `browser_close`.

### C3. `InteractiveToken`: when it runs, sign-out, and session-ending notification

**Answer.** It runs only in an existing interactive session of that user. At sign-out every process
in the session is terminated. A windowless process is told only through a **hidden top-level
window** handling `WM_QUERYENDSESSION` and `WM_ENDSESSION`; console control events do not reach
interactive processes at logoff, and a message-only window does not receive broadcasts. A process
with no visible window cannot hold the session open and is terminated if it has not answered within
5 seconds. [DOC]

- "InteractiveToken: User must already be logged on. The task will be run only in an existing
  interactive session."
  ([LogonType element](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-logontype-principaltype-element);
  [TASK_LOGON_TYPE](https://learn.microsoft.com/windows/win32/api/taskschd/ne-taskschd-task_logon_type)).
  What the Task Scheduler itself records for an instance ended by sign-out: [NOT FOUND].
- [Logging Off](https://learn.microsoft.com/windows/win32/shutdown/logging-off): "the system sends the
  WM_QUERYENDSESSION message to each window"; "When an application returns TRUE for
  WM_QUERYENDSESSION, it receives the WM_ENDSESSION message and it is terminated"; "The system also
  sends the CTRL_LOGOFF_EVENT control signal to every process during a log-off operation"; "If the
  process that called ExitWindowsEx is running in the logon session of the interactive user, all
  processes in the logon session are terminated."
- [HandlerRoutine](https://learn.microsoft.com/windows/console/handlerroutine), `CTRL_LOGOFF_EVENT`:
  "Note that this signal is received only by services. Interactive applications are terminated at
  logoff, so they are not present when the system sends this signal." Its timeouts table gives
  `CTRL_LOGOFF_EVENT` `SPI_GETWAITTOKILLTIMEOUT`, 5000 ms.
- [SetConsoleCtrlHandler](https://learn.microsoft.com/windows/console/setconsolectrlhandler): a process
  that loads `user32.dll` or `gdi32.dll` does not get `CTRL_LOGOFF_EVENT`/`CTRL_SHUTDOWN_EVENT`; "To
  receive events when a user signs out or the device shuts down in these circumstances, create a
  hidden window in your console application, and then handle the WM_QUERYENDSESSION and
  WM_ENDSESSION window messages that the hidden window receives. You can create a hidden window by
  calling the CreateWindowEx method with the dwExStyle parameter set to 0."
- A message-only window "is not visible, has no z-order, cannot be enumerated, and does not receive
  broadcast messages" ([Window Features](https://learn.microsoft.com/windows/win32/winmsg/window-features#window-types));
  "Applications receive messages through the window procedure of their top-level windows. Messages
  are not sent to child windows."
  ([About Messages, Broadcasting](https://learn.microsoft.com/windows/win32/winmsg/about-messages-and-message-queues#broadcasting-messages)).
- Time: "the system does not allow console applications or applications without a visible window to
  cancel shutdown. These applications are automatically terminated if they do not respond to
  WM_QUERYENDSESSION or WM_ENDSESSION within 5 seconds or if they return FALSE"
  ([Shutdown Changes for Windows Vista](https://learn.microsoft.com/windows/win32/shutdown/shutdown-changes-for-windows-vista)).
  [WM_ENDSESSION](https://learn.microsoft.com/windows/win32/shutdown/wm-endsession): "the session can
  end any time after all applications have returned from processing this message";
  `ENDSESSION_LOGOFF 0x80000000` marks a sign-out
  ([WM_QUERYENDSESSION](https://learn.microsoft.com/windows/win32/shutdown/wm-queryendsession)).

### C4. Can a task-started process bring its own window to the foreground

**Answer.** Documentation only: not by itself in the usual case. None of the conditions that let a
process set the foreground is normally true for a process the Task Scheduler started; it can when
the foreground lock time-out has expired, when there is no foreground window, when it received the
last input event, or when a process that holds the right calls `AllowSetForegroundWindow` for it,
and that grant is lost at the next input not directed at it. Task Scheduler's own pages say nothing
on the subject. [DOC] [NOT FOUND in Task Scheduler docs] [KB]

- [SetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow):
  all of (desktop app; foreground not locked by `LockSetForegroundWindow`; no menus active) and at
  least one of (lock time-out expired; caller is the foreground process; caller was started by the
  foreground process; no foreground window; caller received the last input event; caller or
  foreground process being debugged). "It is possible for a process to be denied the right to set the
  foreground window even if it meets these conditions." "An application cannot force a window to the
  foreground while the user is working with another window. Instead, Windows flashes the taskbar
  button of the window to notify the user."
- [AllowSetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow):
  "The calling process must already be able to set the foreground window"; the grantee "loses the
  ability to set the foreground window the next time that either the user generates input, unless
  the input is directed at that process, or the next time a process calls AllowSetForegroundWindow".
- Measured already [KB kb/windows/processes.md "A process a task starts may not take the
  foreground"]: three task-started probes read `AllowSetForegroundWindow` on their own pid as false,
  error 5, with this machine's foreground lock time-out at 2147483647 ms; and a window opened by a
  real click through the COM activator did take the foreground [KB notifications.md].
- Velopack's own `AllowSetForegroundWindow` calls for Update.exe and for the restarted app
  (`DefaultProcessImpl.cs:58-66`, `process_win.rs:402-404`) need the caller to hold the right, which a
  task-started coordinator does not, so a restart after an update cannot take the foreground either.

---

## D. Cost of `GetLastInputInfo`, `GetForegroundWindow`, `GetWindowThreadProcessId`

**Answer.** The cost is not documented: no page says whether these are kernel calls or cached reads,
and none says they affect input latency. What is documented is behaviour, plus the contrast that
hooks do slow input and that periodic wake-ups cost power. [DOC] [NOT FOUND for cost and input effect]

- [GetLastInputInfo](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getlastinputinfo):
  "useful for input idle detection"; "does not provide system-wide user input information across all
  running sessions. Rather, GetLastInputInfo provides session-specific user input information for
  only the session that invoked the function."; the tick count "is not guaranteed to be incremental
  ... can be caused by a timing gap between the raw input thread and the desktop thread or an event
  raised by SendInput, which supplies its own tick count."
- [LASTINPUTINFO](https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-lastinputinfo):
  `dwTime` is a `DWORD`, "The tick count when the last input event was received", see `GetTickCount`;
  [GetTickCount](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount):
  "the time will wrap around to zero if the system is run continuously for 49.7 days ... check for an
  overflow condition when comparing times". So idle time is a 32-bit unsigned difference against
  `GetTickCount`, never against `GetTickCount64`.
- [GetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow):
  "The foreground window can be NULL in certain circumstances, such as when a window is losing
  activation." [GetWindowThreadProcessId](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid):
  "If the window handle is invalid, the return value is zero" and the pid variable is left unchanged,
  which is the case when the window is destroyed between the two calls.
- Hooks, the documented contrast: "Hooks tend to slow down the system because they increase the
  amount of processing the system must perform for each message."
  ([Hooks Overview](https://learn.microsoft.com/windows/win32/winmsg/about-hooks)); a low-level
  keyboard or mouse hook is called synchronously by "sending a message to the thread that installed
  the hook", with a `LowLevelHooksTimeout` of at most 1000 ms, after which the hook "is silently
  removed" ([LowLevelKeyboardProc](https://learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc#remarks)).
  No page found says a poll of the three functions above affects input. [NOT FOUND]
- Power, the documented cost of polling itself: "Periodic timer wake-ups -- Timers that fire every
  few seconds keep the CPU active. Use coalescing timers or event-driven designs instead."
  ([About Power Management, Modern Standby](https://learn.microsoft.com/windows/win32/power/about-power-management));
  "If a timer is necessary, use a timer that is signaled once rather than a periodic timer, or set the
  interval to a value greater than one second."
  ([Waitable Timer Objects](https://learn.microsoft.com/windows/win32/sync/waitable-timer-objects));
  `SetTimer`, `CreateTimerQueueTimer` and `Sleep` do not coalesce; use `SetCoalescableTimer`,
  `SetWaitableTimerEx` with a tolerable delay, or a threadpool timer with `msWindowLength`
  ([Idle Energy Efficiency Assessment, Issues](https://learn.microsoft.com/windows-hardware/test/assessments/results-for-the-idle-energy-efficiency-assessment#issues)).
- The maintainer's f4 condition (same pace whatever the number of windows) holds by construction:
  two calls per tick, independent of how many windows exist. That is a property of the call pattern,
  not a measurement.

---

## What changes the design

1. **Do the after-update work in Velopack's restart, not in `--veloapp-updated`** (A1, A3). Anything
   the hook starts from `current\`, including a coordinator started through the Task Scheduler, is
   killed by the post-hook pass if it is up in time and survives if not. The restart runs after that
   pass, after the swap, the shortcut rewrite and the Update.exe sync.
2. **The restarted process cannot tell success from failure from its arguments or environment** (A2).
   A failed apply restarts the OLD version with the same `restartArgs` and `VELOPACK_RESTART=true`.
   Put the version being applied into `restartArgs` (for example `--after-update 1.2.0`) and have the
   after-update mode compare it with its own version, with `UpdatePendingRestart` as a second signal;
   "installed" and "failed" toasts follow from that comparison. The failed toast can name
   `%LocalAppData%\velopack\velopack_BrowserAI.app.log`.
3. **A swap failure leaves no program at all** (A2): after 31 failed renames 1.2.161 deletes the
   backup, leaves no `current\`, restarts nothing and exits 1. Nothing of BrowserAI's can run to
   raise a toast; only a reinstall recovers, and a non-silent `Setup.exe` over a non-empty root stops
   at the overwrite dialog [KB velopack.md]. A hazard-row candidate; upstream's comment promises a
   rollback the code does not have.
4. **Use `WaitExitThenApplyUpdates(asset, silent: true, restart: true, restartArgs)` and exit within
   60 s**; `ApplyUpdatesAndRestart` is non-silent and shows Velopack's progress window (A1).
5. **The "at most once per 10 minutes, across crashes and restarts" rule (d9) must be BrowserAI's own
   persisted stamp** (A4). Velopack has no schedule, no debounce and no stamp; it writes only
   `packages\.betaId`.
6. **A local folder feed is a supported source** (`SimpleFileSource`, A5); the channel picks the file
   and nothing filters pre-releases.
7. **Toasts with no timeout** (B2): every such toast needs a button, and to satisfy the stricter page,
   at least one `activationType="background"` action. The "installed" toast's *Dismiss* is safer as a
   background button our activator ignores than as the system dismiss, which is unmeasured. Even then:
   3 days at most in Notification Center; under Do Not Disturb the banner shows only if the person
   allows reminders; under full-screen or display duplication even priority banners are hidden.
   Consider `ExpiresOnReboot` for the "ready" toast.
8. **The live countdown can be a plain bound text line** (B1), updated with `IToastNotifier2.Update`
   and an increasing sequence number. `NotificationNotFound` means the person dismissed it, so stop
   updating. Whether a reminder moved to Notification Center by its X still counts as present for an
   update, and what a once-per-second update rate does on screen, are not documented: measure both.
9. **State changes by replacement re-alert and move to the top** (B3). "Installing now" replacing
   "ready" pops up unless `SuppressPopup` is set; "installed" or "failed" after it pops up again.
   `afterActivationBehavior="pendingUpdate"` could keep the toast visible after *Wait for inactivity*
   until it is updated (B2); unmeasured.
10. **Clicks need the COM activator** (B4) for anything that must work with no process of ours
    running; a protocol button opening an `https:` page needs nothing of ours but loses input values.
11. **`IgnoreNew` is still not a safe single-instance mechanism** (C1): its result to the requester is
    undocumented, and the reason recorded in `SignInTask.cs:70-74` for `Parallel` stands.
12. **Never stop the coordinator with `End`** (C2): with no window to receive `WM_CLOSE` it is a hard
    terminate, and the coordinator's kill-on-close job then ends every browser without
    `browser_close`. Stop it through its pipe.
13. **Sign-out gives the coordinator at most about 5 seconds, and only with a hidden top-level window**
    (C3). Today it has a pipe and no window. Five seconds is far below the one-minute
    `SessionTimes.BrowserCloseCap`, so a sign-out cannot run the clean close; this matches the
    accepted loss in DECISIONS.md ("Firefox's session-store intervals stay at their defaults": a
    sign-out is one of the deaths without a clean close). A hidden window would buy notification, not
    time.
14. **Input polling** (D): use a coalescable timer, keep it to "only while a visible window is open",
    compute idle time with 32-bit wraparound against `GetTickCount`, treat a NULL foreground window and
    a zero thread id as "unknown, no activity", and expect `SendInput` and non-monotonic ticks.

## Open questions this research could not settle (each needs a measurement, none was run)

1. What `IRegisteredTask::Run` returns under `IgnoreNew` while an instance runs (C1).
2. How long the scheduler waits between `WM_CLOSE` and `TerminateProcess`, and whether `End` touches
   children or the task's job (C2).
3. **Whether the Task Scheduler ends processes left in a task's job when the task's own process
   exits.** Not documented; the kb measured the job's flags (`0x0`) but not this. It matters twice:
   the sign-in step hands the package to Update.exe and exits (DECISIONS.md "The sign-in step is a
   per-user logon task"), and in the new design the restarted app is Update.exe's child in that same
   job. `SignInStepTests.ThePublishedAppsSignInStartRunsTheStepAndExits` starts the app directly, not
   through the scheduler; I found no kb entry that observes Update.exe finishing after its
   task-started parent exits.
4. Whether `scenario="reminder"` holds with only foreground, protocol or system buttons for an
   unpackaged app (B2).
5. Whether *Install now* and *Wait for inactivity* fit whole in one row at 100 percent and at other
   scale factors (B5).
6. Update rate and visible behaviour of a once-per-second bound-text update on an on-screen reminder,
   and `Update` after the X (B1, B3).

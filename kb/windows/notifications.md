<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Windows toasts -- what one can show, and what it silently will not

What the Windows notification surface does with a toast this product would raise,
measured by raising real ones. It is here because the answer changed a design: the
update toast was drawn with five buttons, and five buttons cannot be read.

**Versions.** Windows **11 Pro 10.0.26200**, x64, at **3840x2160**. Raised through
`Windows.UI.Notifications.ToastNotificationManager` from Windows PowerShell 5.1
under that shell's own application id. *Corrected 2026-09-24 (previously "from
PowerShell 7 under the shell's own application id")*: PowerShell 7.6.6 cannot
load the WinRT type at all, and answers
`[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]`
with *Unable to find type*, while Windows PowerShell 5.1.26100.9549 loads it from
`Windows.UI` -- measured 2026-09-24 by loading the type in each shell and raising
nothing. The rig's `show.ps1` passes Windows PowerShell's own id, which is why
the identity line below reads *Windows PowerShell*. Renderings, XML and rig:
[`docs/design/toast-2026-09-24`](../../docs/design/toast-2026-09-24/README.md).

## Every button is laid out in ONE row, and the row never wraps -- measured 2026-09-24

`[STABLE]` on the platform, `[MACHINE]` for the pixel scale.

⭐ **Five labelled buttons truncate to their first word.** A toast offering
*Review the open sessions*, *Ask again in 10 minutes*, *Ask again in 6 hours*,
*Not before the next reboot* and *Skip this version* rendered as **Revie**,
**Ask**, **Ask**, **Not**, **Skip** -- each button's leading emoji drawn as a
small icon above the word. Windows divides the available width by the number of
buttons and clips; it does not wrap to a second row, it does not shrink the font,
and **nothing reports that it happened**. The toast is valid, it is shown, and it
communicates nothing.

**Two buttons fit a real label** at this width: *Review 27 sessions* and *Ask me
later* both rendered whole. Three was not measured.

⭐ **The way to offer more than two choices is a selection input.** An
`<input type="selection">` is laid out **full width**, above the buttons, with its
own title, and an action bound to it with `hint-inputId` receives the chosen value.
That is how four snooze choices and two buttons fit in one toast, and it is what
the update toast uses.

## Three smaller properties, measured the same day

- **The identity line belongs to the application id, not to the XML.** A toast
  raised under the shell's id reads *Windows PowerShell* with PowerShell's icon.
  A `<text placement="attribution">` is a **second**, separate line under the body,
  and both are shown -- so a mock-up raised from a script shows the wrong identity
  above the right attribution, and the rendering is not wrong, it is somebody
  else's. `[STABLE]`
- **Button icons are all or none.** Windows draws an icon on a button only when
  every button in the toast declares one, each a 16 px white PNG on transparency.
  Adding one to a single button silently draws none. `[STABLE]`
- **`scenario="reminder"` is what keeps a toast on screen** until it is acted on
  instead of expiring into the Action Center, which is the behaviour an update
  prompt wants. `[STABLE]`

⚠️ **What is NOT established here is what any of this looks like at another scale
factor or on another Windows build.** Every rendering was taken on one display at
one resolution. The one-row property is a layout rule and is not expected to move;
*how many* buttons fit a label before clipping obviously does.

**Re-establish it** with `show.ps1` from the design directory, handing it each of
the three XML files in turn. It writes nothing, installs nothing and clears only
the calling shell's own toast history. *Added 2026-09-24*: run it with
`powershell`, Windows PowerShell 5.1, and not with `pwsh`, which cannot load the
toast type.

## The update toast from BrowserAI's own binaries -- measured 2026-09-24

**Versions.** Windows 11 Pro 10.0.26200 at 3840x2160 · Windows SDK 10.0.26100.0,
whose headers the interface ids were read from and nothing was linked against ·
.NET SDK 10.0.401 · Velopack 1.2.158. Raised by hand-written prototypes under
scratch app ids and, once, under the real one; every registration and every toast
was removed afterwards. The rigs, the raw lines
and the screenshots are the
[2026-09-24 evidence batch](../../docs/evidence/2026-09-24-toast-design/README.md);
its `MEASUREMENTS.txt` is the only copy of most activation lines, and every path
named below without a directory of its own is in that batch.

⚠️ **The toast's design is not decided here.** These are the facts the questions
put to the maintainer on 2026-09-24 rest on (Q262 to Q277), and what BrowserAI
does with them is his decision.

### The app id is already set, by Velopack, on the shortcut and in every installed process

**Measured 2026-09-24 @ Velopack 1.2.158.** The real Start Menu `BrowserAI.lnk`,
read without write access (`GPS_DEFAULT`), carries `System.AppUserModel.ID` =
`velopack.BrowserAI.app` and no `System.AppUserModel.ToastActivatorCLSID`, and its
modified time did not move through the research (`lnk/real-shortcut.txt`). The id
is `<shortcutAumid>` in the install's `current\sq.version`, which `vpk` fills
with `--aumid` when one is given and with `velopack.<packId>` otherwise
(`WindowsPackCommandRunner.cs:47-49`); this product passes none. `shortcuts.rs`
sets it on every shortcut it creates and on every update rewrites it on each
shortcut pointing into the install (`:222`, `:146`), and writes no activator
anywhere. `VelopackApp.Run()` hands the same id to
`SetCurrentProcessExplicitAppUserModelID` whenever the locator has one
(`VelopackApp.cs:190-196`), which is every installed process: the test pack's
install hook, its configuration app and its installed server each logged
`Setting current process explicit AppUserModelID to 'velopack.BrowserAI.app.test'`
on 2026-09-24, and the two uninstalled copies logged no such line
([the Velopack rows' batch](../../docs/evidence/2026-09-24-velopack-rows/README.md),
`logs/velopack-*-excerpt.txt`). A test toast raised under `velopack.BrowserAI.app`
wore **BrowserAI** and the product's icon (`shots/real-aumid-toast.png`). `[FLOATS]`

So a toast from BrowserAI needs no app id of its own and no edit to the shortcut.
What it lacks is an activator, which is what the click entry below is about.

**Re-establish it** by reading the shortcut's property store with
`lnk/read-lnk.ps1`, which opens it read-only, and by searching Velopack's per-app
log for `AppUserModelID`. [Re-verification row 156](../re-verification.md).

### A NativeAOT binary raises a toast through hand-written WinRT calls and nothing generated

**Measured 2026-09-24 @ .NET SDK 10.0.401.** A prototype using only blittable
`DllImport`s into `combase`, `ole32` and `user32`, with no CsWinRT and no
generated interop, published with NativeAOT with no warning in any of its four
publish logs and raised a toast (`shots/nativeaot-toast.png`); the binary from
the last publish is 1,625,600 bytes. The chain: `RoActivateInstance` on
`Windows.Data.Xml.Dom.XmlDocument`, then `IXmlDocumentIO.LoadXml` (slot 6);
`RoGetActivationFactory` on `Windows.UI.Notifications.ToastNotification`, then
`IToastNotificationFactory.CreateToastNotification` (slot 6);
`IToastNotification2` for `Tag`, `Group` and `SuppressPopup` (slots 6, 8, 10);
`IToastNotificationManagerStatics.CreateToastNotifierWithId` (slot 7), then
`IToastNotifier.Show` (slot 6). The event handlers are hand-built COM objects that
aggregate the free-threaded marshaler. `[FLOATS]`

The interface ids, each re-read on 2026-09-24 against the header line named, in
`Include\10.0.26100.0\winrt\windows.ui.notifications.idl` unless another file is
given; the prototype carries the same values at `proto/Program.cs:26-71`:

| Interface | IID | Line |
|---|---|---|
| `IToastNotificationManagerStatics` | `50AC103F-D235-4598-BBEF-98FE4D1A3AD4` | 1410 |
| `IToastNotificationFactory` | `04124B20-82C6-4229-B109-FD9ED4662B53` | 1331 |
| `IToastNotifier` | `75927B93-03F3-41EC-91D3-6E5BAC1B38E7` | 1445 |
| `IToastNotification` | `997E2675-059E-4E60-8B06-1760917C8B80` | 1262 |
| `IToastNotification2` | `9DFB9FD1-143A-490E-90BF-B9FBA7132DE7` | 1278 |
| `IToastActivatedEventArgs` | `E3BF92F3-C197-436F-8265-0625824F8DAC` | 1194 |
| `IToastActivatedEventArgs2` | `AB7DA512-CC61-568E-81BE-304AC31038FA` | 1202 |
| `IToastDismissedEventArgs` | `3F89D935-D9CB-4538-A0F0-FFE7659938F8` | 1246 |
| `IToastNotificationHistory` | `5CADDC63-01D3-4C97-986F-0533483FEE14` | 1339 |
| `IToastNotificationHistory2` | `3BC3D253-2F31-4092-9129-8AD5ABF067DA` | 1353 |
| `IScheduledToastNotificationFactory` | `E7BED191-0BB9-4189-8394-31761B476FD7` | 1045 |
| `IXmlDocument` | `F7F3A506-1E87-42D6-BCFB-B8C809FA5494` | `winrt\windows.data.xml.dom.idl:275` |
| `IXmlDocumentIO` | `6CD0E74E-EE65-4489-9EBF-CA43E87BA637` | `winrt\windows.data.xml.dom.idl:314` |
| The `Activated`, `Dismissed` and `Failed` handler delegates | `AB54DE2D-97D9-5528-B6AD-105AFE156530`, `61C2402F-0ED0-5A18-AB69-59F4AA99A368`, `95E3E803-C969-5E3A-9753-EA2AD22A9A33` | `winrt\windows.ui.notifications.h:2206`, `:2244`, `:2283` |
| `INotificationActivationCallback` | `53E31837-6600-4A81-9395-75CFFE746F94` | `um\NotificationActivationCallback.idl:21` |
| `PKEY_AppUserModel_ID` and `PKEY_AppUserModel_ToastActivatorCLSID` | property ids 5 and 26 of `9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3` | `um\propkey.h:8569`, `:8640` |

**Re-establish it** by publishing `proto/` and running its `show` mode with one of
the XML files under a scratch id; the ids by reading the lines the table names. ⚠️
A `show` without `suppress` puts a toast on the screen.
[Re-verification row 157](../re-verification.md).

### A click carries the dropdown's value only to a COM activator

**Measured 2026-09-24**, each by a UI Automation click on a real toast, with the
raiser gone unless the row says otherwise. `[STABLE]`

| Path | What arrived | Click to launch |
|---|---|---|
| Protocol activation, `activationType="protocol"`, the scheme registered at `HKCU\Software\Classes\<scheme>` | The button's `arguments` text exactly, `browserai-q254test:snooze`. **The dropdown's value is lost** | 118 ms |
| A COM activator: `HKCU\Software\Classes\AppUserModelId\<id>\CustomActivator = {CLSID}`, and `HKCU\Software\Classes\CLSID\{CLSID}\LocalServer32 = "<exe>" -ToastActivated` | COM started `"<exe>" -ToastActivated -Embedding`, appending `-Embedding` itself, and `INotificationActivationCallback::Activate` received the app id, the button's `arguments` and the dropdown, `[snooze='10m']`. A click on the body delivered the toast's `launch` value, and the dropdown too | 21 to 40 ms |
| No activator, the raiser still running | Its `Activated` event: the arguments and `{snooze='reboot'}` | 20 ms |
| No activator, the raiser gone | **Nothing is started and the toast is consumed**: the choice is lost, for a background and a foreground button alike | none |

**The `CustomActivator` key works beside a shortcut that carries no activator
property**, so Velopack's shortcut can stay as it is; and it works for an app id
that exists only in the registry, with a `DisplayName` and an `IconUri` and no
shortcut at all (`shots/registry-only-aumid-toast.png`). With an activator
registered AND the raiser listening, both received the same click, 4 ms apart.

Not measured, because each needs a real click: a click from the Notification
Centre after the X, protocol activation from a click on the body, whether a
process the activator started may take the foreground, and which identity wins
when a shortcut and a registry `DisplayName` name the same id.

**All four were measured later the same day**, in a second screen window the
maintainer gave the research from 11:33Z to 11:39Z. *Corrected 2026-09-24 by
addition (previously the paragraph above was the end of the section).* `[STABLE]`
unless a line says otherwise.

- **A window opened by a real click on a toast button, through the COM
  activator, may take the foreground; one started in the background may not.** A
  real mouse click on *Review* (the cursor moved to the button's own rectangle,
  pressed, and put back) started the activator at 11:37:14.20Z with
  `invokedArgs='action=review'` and the dropdown `[snooze='6h']`; the window it
  then opened called `SetForegroundWindow` and got **1**, and held the foreground at
  0, 300, 1,000 and 2,000 ms. The control, the same window opened by a process
  started from a background shell at 11:36:49Z, got **0** and never held it. So a
  window the update toast's *Review* opens comes to the front, which is what a
  person who clicked it asked for.
- **A click in the Notification Centre after the X reaches the activator, with the
  dropdown's value.** There the toast is collapsed: a `ListItem` with
  `ExpandCollapse`, *Expand this notification*, whose buttons exist only once it is
  expanded (`uia/nc-group-dump.txt`). Expanded, *Ask me later* was invoked at
  11:35:59.84Z with the raiser long gone; COM started the activator with
  `-ToastActivated -Embedding` 37 ms later and it received the registry id,
  `action=snooze` and `[snooze='6h']`, and the history then read 0. So the choice
  survives the X for as long as the toast sits in the Notification Centre.
- **Under `AppUserModelId\<id>`, the registry's display name and icon win over the
  shortcut's.** A shortcut *BrowserAI Q254 test* carrying the id and the
  prototype's icon, with `HKCU\Software\Classes\AppUserModelId\<id>` naming
  `DisplayName` *BrowserAI Q254 REGISTRY NAME* and `IconUri` pointing at
  `BrowserAI.ico`: the toast's header read *BrowserAI Q254 REGISTRY NAME* with the
  BrowserAI icon (`shots/item4-identity.png`).
- **A body click through protocol activation drops the dropdown's value**, as a
  button click does. With the dropdown set to *After the next reboot*, a click on
  the body started the prototype 75 ms later with the toast's `launch` URI verbatim,
  `browserai-q254test:review?from=body`, and nothing else.
- **Whether a suppressed toast lights the taskbar's badge is inconclusive on this
  machine**, because no toast shows a badge here: the notification area read the
  same before, after and once the suppressed toast was removed, and the positive
  control -- an ordinary toast moved to the Notification Centre by the X --
  showed none either; the clock button's accessible name carried no count in any
  of them. `[MACHINE]`
- **Another display scale was not measured.** All three monitors read 96 DPI, 100
  percent (`boot/dpi.txt`), toasts render on
  the main display only, and changing the scale would have moved other agents'
  windows on a shared desktop.
- **Changing `LocalServer32` to another executable did not take effect for the next
  activation**: an activation at 11:36:43Z still started the path the key had
  named before, and the research then copied its new build over that path. Read
  once and not explained; a registration that changes the activator's path should
  expect the old one to be started at least once. `[MACHINE]`

Evidence: the second window's section of `MEASUREMENTS.txt` and the files the
[batch README](../../docs/evidence/2026-09-24-toast-design/README.md) lists
beside it. The activator's own log lines for 11:35:59Z and 11:37:14Z were
overwritten when the build was copied over the old path, so for those two the
transcription in `MEASUREMENTS.txt` is the only copy.

**Re-establish it** with `reg/register.ps1` and the prototype's `show` mode, then a
click; `reg/cleanup.ps1` removes what `register.ps1` wrote. ⚠️ **It needs a click
on a real toast**, which is a person or UI Automation on the developer's screen.

### The X is heard only by a raiser that is still running

**Measured 2026-09-24.** The X's accessible name is *Move this notification to
Notification Centre* (`DismissButton`). A raiser that is alive gets `Dismissed`
with `UserCanceled` 7 ms after the click, and the toast **stays in the
Notification Centre**: the history still held it 4 s later. A COM activator never
hears the X, because `INotificationActivationCallback` has `Activate` and nothing
else (`NotificationActivationCallback.idl:24-31`). So once the raiser has exited,
nothing is told that the X was pressed. `[STABLE]`

### A scheduled toast is delivered with no process alive

**Measured 2026-09-24.** A `ScheduledToastNotification` was created at
09:57:40.81Z, due 20 s later with its popup suppressed, by a process that exited
at once: the history read 0 at 09:57:40.85Z, no scratch process was alive at the
due time, and at 09:58:14.64Z the history held it, `tag=sch1`, with its whole XML
(`proto/events-sch1.log`). It was then removed by tag and group. `[STABLE]`

**Re-establish it** with the prototype's `schedule` mode and `suppress`, then its
`history` mode once the due time has passed.

### A suppressed toast shows nothing and takes no focus

**Measured 2026-09-24**, three runs under a scratch app id -- the registry-only
one by the researcher's report; the kept lines do not record which. With
`SuppressPopup` set, no *New notification* window appeared (`shots/suppress-s1.png`,
taken 6 s later), the raiser got `Dismissed` with `TimedOut` 16 to 32 ms after
`Show`, and the foreground window, sampled every 25 ms for 3 s after `Show`, held
**one value** in each of the two sampled runs. The first run's one foreground
change, 1.5 s after `Show`, was pid 9576, gone before it could be named; the
Velopack rows' `D1` installer ran as pid 9576 for 0.9 s at 09:41:37Z
([its batch](../../docs/evidence/2026-09-24-velopack-rows/README.md),
`logs/D1-same-version-cancel-dialog.txt`), and the kept lines do not record when
the first run was. A control with the popup NOT
suppressed put ShellExperienceHost's *New notification* window up and did not
move the foreground either. The history returns a suppressed toast with its whole
XML through `IToastNotification.get_Content`, and `History.Remove(tag, group)`
removes it -- for a shown popup it takes the popup off the screen too. `[STABLE]`

Not measured: whether a suppressed toast lights the taskbar's notification badge.

**Re-establish it** with the prototype's `show` mode and `suppress`;
`reg/history-headless.ps1` reads the history back with no window.

### `get_Setting` fails until an app id's first toast

**Measured 2026-09-24.** `IToastNotifier.get_Setting` returned `0x80070490`,
`ERROR_NOT_FOUND`, for a scratch app id that had never shown a toast, and `0`,
*Enabled*, after its first. The first toast is also what creates
`HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\<id>`: that
key was absent for `velopack.BrowserAI.app` before one test toast under the id,
present after it with `PeriodicNotificationCount` 1, and removed again. So
whether notifications are enabled for an app id cannot be asked before that id
has raised one. `[STABLE]`

### Boot time, logon time, and what .NET 11 does to `Environment.TickCount64`

**Measured 2026-09-24 @ .NET SDK 10.0.401**, after the machine's restart at
07:13Z (`boot/results.txt`). `[MACHINE]`

| Reading | Value | Cost |
|---|---|---|
| Now minus `GetTickCount64` | 07:13:44.22Z, an 18 ms spread over 100,000 reads | 21 µs a read, PowerShell included |
| WMI `Win32_OperatingSystem.LastBootUpTime` | 278 ms later than the line above | 551 ms |
| `WTSQuerySessionInformation`, the session's logon time | 07:38:52.27Z | 5 ms |

Fast Startup is off on this machine (`HiberbootEnabled` 0), so whether a
Fast-Startup power-on moves the boot time was not measured.

**`Environment.TickCount64` changes meaning in .NET 11**, read 2026-09-24 on
Microsoft Learn: on Windows it moves from `GetTickCount64`, which counts sleep and
hibernation, to `QueryUnbiasedInterruptTime`, which does not
([the breaking change, .NET 11 Preview 1](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/environment-tickcount-windows-behavior)).
So a boot time computed as now minus `Environment.TickCount64` is right on .NET
10 and wrong by every sleep since boot on .NET 11, while one computed from
`GetTickCount64` itself is right on both. `[FLOATS]`

**Re-establish it** with `boot/boot.ps1` and `boot/logon.ps1`, and the .NET half by
reading the Learn page against the target framework in force.
[Re-verification row 158](../re-verification.md).

### What a reader can do to a live marker, and what tears

The sessions page the toast opens would read the live markers `LiveInstances`
holds, so this is what a reader of one can and cannot do. **Measured 2026-09-24 @
.NET SDK 10.0.401**, against a 4,096-byte marker held the way
`LiveInstances.cs:314` holds one: create-new, read-write, sharing read
(`share/results.txt`). `[STABLE]`

| What the reader did | What happened |
|---|---|
| `File.ReadAllText` | A sharing violation, `0x80070020` |
| An open for read that shares `ReadWrite`, or `ReadWrite` and `Delete` | It opened and the content parsed |
| The census probe, read-write and sharing read, while the holder lives | A sharing violation, with or without a reader open: the marker reads as held |
| An open for read that shares only read, while held | A sharing violation, so held-or-free can be told without asking for write access |
| The holder dies while a reader sharing `ReadWrite` and `Delete` is open | The census probe succeeds and the reclaim's delete succeeds: the reader changed nothing |
| The same with a reader sharing `ReadWrite` alone | The census probe succeeds and **the reclaim's delete fails** with `0x80070020`, and the name stays listed |
| 20,000 reads while the holder rewrites one 4,096-byte block in place | **5 torn** |
| 20,000 reads while the holder truncates and rewrites | **915 empty and 4 torn** |
| `File.Move` of a replacement over the marker while a reader holds it | `0x80070005`, with or without `Delete` sharing |
| Timestamps while the holder rewrites every 200 ms | `GetFileAttributesEx` and the handle followed it, age 99 ms; the directory listing did not, age 1,546 ms |

So a record kept in a marker needs a checksum and a reader that retries, the file
must never be truncated, and a reader must share `ReadWrite` and `Delete`, which
are the flags `LockFile.Read` already opens with.

**Re-establish it** with `share/`, built and run as an ordinary console program; it
opens no window.

### A named stop event ends a process in about 60 ms

**Measured 2026-09-24 @ .NET SDK 10.0.401, NativeAOT**, six runs of a prototype
that waits on a named event and exits through a 50 ms stand-in for a server's
disposals (`stopevent/results.txt`): names under `Local\` and `Global\` were both
created without elevation; signal to callback took **0.17 to 0.23 ms** and signal
to process exit **60.5 to 65.6 ms**; the exit code was 0 every time; and once the
process had exited, `OpenExisting` on the name failed, because the object dies
with its last handle, so a late signal reaches nothing. `[MACHINE]`

**Re-establish it** with `stopevent/`, published NativeAOT; it opens no window.

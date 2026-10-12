<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- what comes to the front when a folder link is clicked

Establishes
[A folder link comes to the front when the process the link starts opens it](../../../kb/windows/loopback-page.md#a-folder-link-comes-to-the-front-when-the-process-the-link-starts-opens-it----measured-2026-10-11).
Evidence:
[`docs/evidence/2026-10-11-link-start/`](../../evidence/2026-10-11-link-start/README.md).

**Why it exists.** The page's folder buttons opened Explorer behind the browser, and the
maintainer's 55 was answered *"55 a"*: a folder button becomes a link the browser hands to
BrowserAI, measured first in his own browser. The rig measured, in his installed Firefox
157.0.1 with a scratch profile, which of five shapes brings a folder window or Windows'
folder picker to the front, and his 75 was asked from what it found.

## What is here

| File | What it does |
|---|---|
| `probe/Program.cs` | One program with a mode per first argument: `register` and `unregister` the scheme `browserai-probe` under `HKCU\Software\Classes`, the second only while its command names this file; `prepare` the folders the links open; `observe` the foreground window and the shell's window events until the run ends; `serve` the probe page on `127.0.0.1`; `standin`, a stand-in for the background on a named pipe that opens a folder or shows the picker when asked; `handle`, what the browser starts for a link; `selftest`; and `analyze`, which writes the report. Every long-running mode ends when `stop.flag` appears in the run folder, or after 45 minutes |
| `probe/LinkProbe.csproj`, `probe/Directory.Build.props`, `probe/Directory.Build.targets`, `probe/Directory.Packages.props` | A Windows program with no package and no product file in it, kept apart from the repository's build settings |
| `start.ps1` | Snapshots Firefox's `Launcher` value names and the probe's key, registers the scheme, prepares the folders and starts the observer and the two page servers, recording each pid and start time |
| `stop.ps1` | Writes `stop.flag`, waits for every process it recorded and every handler a browser started, each looked up by pid and start time, unregisters the scheme, snapshots again and runs `analyze` |
| `focus.ps1` | Meant to bring the scratch Firefox to the front before each click; it failed on every call, because it declares `GetCurrentThreadId` from `user32.dll` |
| `auto/driver.mjs`, `auto/package.json`, `auto/package-lock.json` | Starts Firefox with the scratch profile over WebDriver BiDi through `puppeteer-core` 25.13.0, clicks each link inside the page and writes `steps.json` |
| `STEPS.txt` | The steps as written for the maintainer, before the run was made without him |

⚠️ **One file the run used is not here: `close-probe-windows.ps1`.** The driver ran it
after each click to close the folder window or cancel the picker the click opened, by
sending `WM_CLOSE` to that one window. It finds the pickers' owners through a WMI query
filtered on the image name `LinkProbe.exe`, which is a selection by name, and in this
directory the one file that selects by name is `2026-09-14-firstrun/observe.ps1`, kept
by the decision of 2026-09-17 in [the index](../README.md), where a second is a new
decision; the real scan, run over a copy of it on 2026-10-12, named it. It is held for the
maintainer, with its digest in
[the evidence batch](../../evidence/2026-10-11-link-start/README.md); the observations
were taken before it ran, and it closed nothing a person had open.

## What keeps it off the rest of the machine

- **A scratch Firefox profile**, `ff-profile` beside the rig, with the probe's scheme
  allowed in it so that no prompt appears; the maintainer's own profile is never named.
- **One registry key of its own.** `register` refuses to write over a key that exists, and
  `unregister` removes it only while its command still names this probe; `start.ps1` and
  `stop.ps1` snapshot Firefox's `Launcher` value names on either side.
- **No real input.** The clicks are WebDriver BiDi clicks inside the page, and nothing
  moves the mouse or presses a key. The windows the run opens are the probe's folders
  inside the run folder and the probe's own pickers.
- **Processes are found by pid and start time.** `stop.ps1` ends nothing: every process
  ends itself at `stop.flag`, and `stop.ps1` checks each pid it recorded against its
  start time. The observer reads a window's owner by full image path and records the
  title only of the rig's own windows.

## Running it

1. From `probe`, build the program into the rig's `bin` folder, where `start.ps1` looks
   for it: `dotnet publish -c Release -o ..\bin`. The run's build log shows a build and a
   publish into `bin`; the command itself was not recorded.
2. From `auto`, `npm install`, and set `HERE` in `driver.mjs` to the rig's folder.
3. `start.ps1`, then `node driver.mjs`, then `stop.ps1`, from a shell with no window of
   its own in front. The report is `report.txt` in the run folder.

## How it was run, and how the stored copy differs

One run on 2026-10-11, started 16:19Z; the driver's first pass ended on a navigation
timeout after one click, and its second pass made five clicks, A, C, E, B and D, before
the sixth, which opened the page at a new path, timed out. When `stop.flag` appeared the
observer had already removed the scheme, so `unregister` found nothing, and `stop.ps1`'s
snapshot read the key absent and the same 35 `Launcher` value names as before the run.

- **Four phrases are reworded**, each naming who asked for or authorised the run: one
  each in `STEPS.txt`, `start.ps1`, `focus.ps1` and `auto/driver.mjs`.
- **One cast in `focus.ps1` has a space in it**, `[ref]([uint32] 0)`, because the
  repository's link scan read `[ref]([uint32]0)` as a Markdown link to a file that does not
  exist. PowerShell reads both the same.
- Nothing else in any file changed. The digests of the files as they ran are in the
  evidence batch.
- `close-probe-windows.ps1` is held out, as above.

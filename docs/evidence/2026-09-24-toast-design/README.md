<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- the update toast and the sessions page, researched

**What this is.** The research behind Q254's update toast and the sessions page
it opens, taken on 2026-09-24 between 09:23Z and 10:02Z by the files' own times,
on Windows 11 Pro 10.0.26200 at 3840x2160, with the .NET SDK 10.0.401, the Windows SDK 10.0.26100.0
headers and Velopack 1.2.158: three NativeAOT or console prototypes, the raw
activation lines of every toast that was clicked, dismissed or scheduled, the
real Start Menu shortcut's property store read without write access, the boot
and logon time readings, the live-marker sharing matrix, the stop-event timings,
and four screenshots. **62 files with this README, 0.5 MB.**

⚠️ **The design these facts feed is not decided.** The questions it raises were
put to the maintainer as Q262 to Q277 on 2026-09-24, and nothing here is product
code. `REPORT.md` is the researcher's whole report, recommendations included;
the recommendations are one researcher's, and the facts are what
[the kb](../../../kb/windows/notifications.md) records.

⚠️ **Some of this put things on the maintainer's screen.** The clicks in
`MEASUREMENTS.txt` were made by UI Automation against real toasts, and one of
them invoked the Notification Centre's per-app settings button by mistake; the
report says so under *What I stopped*. Everything the research registered was
removed afterwards: the 122 keys under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings` matched
the snapshot taken before (`reg/notif-settings-before.txt` against
`reg/notif-settings-after3.txt`), and `reg/created.txt` lists every key and
shortcut that was created.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: notifications](../../../kb/windows/notifications.md#the-update-toast-from-browserais-own-binaries----measured-2026-09-24) | Every fact under *The update toast from BrowserAI's own binaries* |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 156, 157 and 158 |
| [`docs/design/toast-2026-09-24`](../../design/toast-2026-09-24/README.md) | The correction of which PowerShell raised its renderings |

## What is here

| Path | What it holds |
|---|---|
| `REPORT.md` | The researcher's report, verbatim, written to disk by the root session because the researcher's own write was refused; its first line says so |
| `MEASUREMENTS.txt` | The raw activation, dismissal, suppression and scheduling lines. The prototype truncated its event log before each run, so for every run but the last this file is the only copy |
| `proto/` | The NativeAOT toast prototype: `Program.cs`, its project, its four publish logs, and `events-sch1.log`, the event log of its last run, which is the scheduled toast |
| `share/` | The live-marker sharing probe and `results.txt`: which opens a held marker admits, which census and reclaim steps survive a reader, the torn reads, the timestamp lag and the rename failure |
| `stopevent/` | The named stop-event prototype, NativeAOT, and `results.txt`: six runs, `Local\` and `Global\` |
| `boot/` | The boot-time and logon-time readers and `results.txt` |
| `lnk/` | The real shortcut's property store, read with `GPS_DEFAULT`, and the reader |
| `reg/` | The scratch registrations, their removal, the headless history reader, `LnkWriter.cs`, and the four `Notifications\Settings` key lists |
| `shots/` | The four screenshots and the two scripts that took and cropped them |
| `uia/` | The UI Automation drivers and window readers, and the one tree dump kept |
| `xml/` | The three toast payloads the prototype raised |
| `Directory.Build.*`, `Directory.Packages.props`, `.editorconfig` | The scratch isolation that kept the prototypes from importing this repository's own build settings |

## What was left out

- **The published binaries and every build output.** The toast prototype as last
  published at 09:57:31Z, `ToastProto.exe`, 1,625,600 bytes, SHA-256
  `be6ec53611737baf2e069a37d9ccb0098a05d6c6ec05b485a2ed4062844114d4`, and its
  8,540,160-byte `.pdb`; `StopEvent.exe`, 1,729,024 bytes, SHA-256
  `ad7745b733386a63c4296d99686e96431f5cb66d84c812a92f82d450d2d8196f`, and its
  `.pdb`; the sharing probe's framework-dependent output; and every `bin/` and
  `obj/`. ⚠️ **The 1,288,192 bytes the report and `MEASUREMENTS.txt` give for the
  prototype is the researcher's reading of an earlier publish.** Four publishes
  are logged, the binary was overwritten by each, and the one on disk is the
  last; its source is `proto/Program.cs`.
- **The sharing probe's and the stop-event probe's scratch run directories**, which
  held the marker files and handshake files the probes created and deleted, and
  nothing the results do not already state.

## Two departures from the bytes as taken

- **29 files gained the repository's two-line SPDX header**: every `.ps1`, every
  `.cs` and `REPORT.md`, because this tree requires one on each of those kinds.
  Nothing else in them moved.
- **Line endings are this repository's**, per
  [the directory's own note](../README.md): captures written with CRLF are stored
  with LF.

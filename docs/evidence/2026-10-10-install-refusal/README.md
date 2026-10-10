<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- an install hook that refuses, under Velopack 1.2.161

What the kb entry
[A hook that refuses does not stop an install](../../../kb/packaging/velopack.md#a-hook-that-refuses-does-not-stop-an-install-and-a-refused-folder-takes-the-shared-entry----measured-2026-10-10)
was read from, for the maintainer's decision of 2026-10-10, verbatim: *"21 refusing
installing into a non-standard folder so the project specific setups always resolve on
every dev's pc."* Taken on this machine from 13:12:20Z to 13:13:42Z, with the suite's
test pack, pack id `BrowserAI.app.test`, built from `db50964e` with a scratch patch to its
install hook, installed twice with `--silent` into scratch folders and uninstalled twice,
every child windowless; Windows 11 Pro 10.0.26300.9550, Velopack 1.2.161, .NET SDK
10.0.401. Nothing reached the screen, and the PATH, the Task Scheduler, the toasts'
activator, the Start Menu, the Add/Remove entries and the real install's entry read the
same before and after.

| Path | What it is |
|---|---|
| `findings.txt` | The account written when it was measured: the source readings at the 1.2.161 tag with their lines, every measurement with its numbers and exit codes, and what could not be established |
| `rig/velopack-startup-probe.diff.txt` | The scratch patch over `src/BrowserAI.Core/Updates/VelopackStartup.cs` at `db50964e`: the install hook writes a record of what it can see and exits 5, and the uninstall hook does nothing |
| `rig/measure.ps1.txt` | The driver: the suite lock and the installer lock, the snapshots before and after, the two installs and the two uninstalls, each windowless |
| `rig/commands.txt` | Every command, in order |
| `setup-1.log`, `setup-2.log` | Velopack's Setup's own `--log` of the two installs |
| `hook-records/*.json` | What the install hook recorded from inside each install: its image and arguments, the install root and pack id, the data root and update source the installer named, and its parent's command line |
| `results.trimmed.json` | Everything the driver recorded, each run and both snapshots, with the snapshots' lists of this machine's software cut to BrowserAI's own entries, a count and a digest |
| `measure.log`, `publish.log`, `pack.log` | The driver's progress, the publish and the test pack's pack |
| `left-out.sha256` | The SHA-256 and byte count of the installer, left out |
| `originals.sha256` | The SHA-256 and byte count of every file as taken that is stored here changed: the two rig scripts under their own names, `rig/commands.txt`, `results.json` and `findings.txt` |

## How these bytes depart from the ones taken

- **The rig's two scripts are stored as text** under a `.txt` name, so that nothing in the
  suite that reads the tree's code reads them as this repository's own.
- **Four lines are re-spelled**, one each in `rig/commands.txt`, `rig/measure.ps1.txt` and
  `rig/velopack-startup-probe.diff.txt` and the owner label `rig/measure.ps1.txt` wrote
  into the suite lock, because they named the part of the build that ran the measurement;
  and the opening line of `findings.txt` says whose account it is in the same terms.
- **`results.json` is stored as `results.trimmed.json`.** Its two snapshots listed the
  software on this machine: the user PATH, the Add/Remove entries, the Run key, the
  scheduled tasks, the toast app ids, the class ids, and every Start Menu and desktop
  shortcut. Each such list keeps BrowserAI's own entries, its count and the SHA-256 of
  the list as recorded, as UTF-8 JSON with sorted keys and no spaces, or of the PATH as
  the text it was, so that *before* and *after* can still be compared and nothing else
  of the machine is listed. Every run's own record is as taken.
- **The installer is not here**: it is 62 MB and rebuilds from the patch in about four
  minutes, and a refusing installer left on disk could be run by mistake; `findings.txt`
  and `left-out.sha256` give its size and SHA-256.
- Everything else is as written, line endings aside, which this repository stores as LF,
  as [the index](../README.md) says of every batch, and `originals.sha256` gives the
  SHA-256 of every file above as taken.

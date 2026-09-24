<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- the coordinator lifecycle: what an apply does to many servers, sign-in, and a single instance

**What this is.** The measurements behind the design Q280 b set in motion and the five
questions it raised, Q282 to Q286: what Velopack 1.2.158's `Update.exe` does to every
process under an install root while it applies a package, measured against real
BrowserAI servers installed from the suite's test pack; what this machine starts first
after sign-in; and how a single-instance program hands a second start to the first, and
who may then take the foreground. The researcher's runs were taken on 2026-09-24 between
11:46Z and 12:11Z on Windows 11 Pro 10.0.26200, AMD Ryzen 9 5950X (32 logical), 128 GB,
.NET SDK 10.0.401, Velopack and `vpk` 1.2.158, under `.work/installer.lock`, with the
install and data roots in a scratch directory under the profile that was removed
afterwards. The writer's three reads under `writer/` were taken the same afternoon.
**102 files with this README, about 1.7 MB.** The maintainer's words it answered, verbatim
from his Q280 b: *"Research how we can reliably start earlier than vscode starting
dozens of servers simultainiously. Also research how velopack handles process kills on
updates because I have a hard time believing it only monitors the process calling
update. That would mean it kills all instances of a multi instance app!?"*

**Nothing real was touched, and the batch says so in its own lines.** Both rigs read
the real install's uninstall key, its Start Menu shortcut, the BrowserAI entry in
`~/.claude.json` and the test pack's Velopack temporary directory before and after, and every
`CLEARANCE after` line in `logs/events.txt` and `logs2/events.txt` matches its
`CLEARANCE before` line. The installer's registration hook ran against
`sandbox/client/`, the configuration directory the rigs handed the client, never the
real one. Every process the rigs started, the installer and `Update.exe` included, was
created with `CreateNoWindow`.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: what an apply does to every process under the root](../../../kb/packaging/velopack.md#what-an-apply-does-to-every-process-under-the-root----read-and-measured-at-12158-2026-09-24) | The two runs, the apply logs, and the source reading at Velopack's tag |
| [kb: sign-in, a logon task and the foreground](../../../kb/windows/processes.md#what-starts-first-after-sign-in-and-what-a-task-started-process-may-do----measured-2026-09-24) | `writer/sign-in.txt`, the three task-started probe logs, and the prototype's numbers |
| [kb: the logon sweep task, corrected](../../../kb/windows/detection.md#the-logon-sweep-task) | `writer/task-registration.out.txt` |
| [`DECISIONS.md`](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client) | Q280 b's design, Q282 to Q286, and the 0.39 s handshake median in Q286 b's row, from `writer/startup-to-serving.out.txt` |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The same median, in the row for a server killed before its handshake |
| [re-verification rows 159 and 160](../../../kb/re-verification.md) | How to re-establish the apply's kill set and its wait |

## What is here

| Path | What it holds |
|---|---|
| `kill-measure.ps1.txt`, `Rig.cs` | Run 1's rig: install the test pack, start three servers, stage 1.1.1, start `Update.exe --waitPid` on the first, keep starting servers every 400 ms while it applies, and record who died, when, with what exit code and from which image |
| `logs/events.txt`, `logs/run1.out.txt` | Run 1's timeline, the same lines twice: the rig's own file and its console |
| `logs/update-apply.trimmed.log` | Run 1's `Update.exe` log: the wait, the hooks, both kill passes and the swap |
| `logs/setup.trimmed.log`, `logs/uninstall.trimmed.log`, `procs/setup.stdout.trimmed.txt`, `procs/uninstall.stdout.trimmed.txt`, `procs/update-apply.stdout.trimmed.txt` | Run 1's install, uninstall and apply as Velopack logged them and as they wrote to standard output |
| `procs/srvA.stderr.txt` to `procs/srvC.stderr.txt`, `procs/late01.stderr.txt` to `procs/late18.stderr.txt` | Each server's own log: the ones the kill ended stop mid-startup, and the ones started after the swap report `manifestVersion=1.1.1` |
| `logs/before-arp-real.reg`, `logs/after-arp-real.reg` | The real uninstall key, exported before and after |
| `linger-measure.ps1.txt`, `logs2/`, `procs2/` | Run 2: the asking server never exits and two applies wait on it at once. `logs2/update-apply-1.log` is the apply the other one killed, whole |
| `sandbox/client/` | The client configuration the installer's hook wrote to, as it was after the uninstall. `.claude.trimmed.json` is its `.claude.json` with two installation identifiers cut, Q298 b; see [What was cut](#what-was-cut) |
| `vpk-pack.log`, `vp-files.txt` | How the 1.1.1 package was packed, and the list of Velopack source files the researcher read |
| `proto/` | The single-instance prototype: `Program.cs`, its project and three build files, `measure.ps1.txt`, the primary's log and the harness's spawn times for twenty second starts, and the three task-started probes' one-line logs |
| `writer/sign-in.ps1.txt`, `writer/sign-in.txt` | The writer's read-only re-reading of this morning's sign-in from the System and Shell-Core logs, the running processes, the task scheduler and BrowserAI's process log, with the maintainer's own programs counted and never named |
| `writer/task-registration.ps1.txt`, `writer/task-registration.out.txt` | The writer's re-measurement of which logon trigger a non-elevated token may register, both tasks removed in the same pass |
| `writer/startup-to-serving.py`, `writer/startup-to-serving.out.txt` | The writer's read of how long an installed server took from its first log record to serving stdio, with the cut-off that gives the 414 starts `DECISIONS.md` quotes, and the same read re-run with none |

## What was cut

⚠️ **Every `.trimmed.` file dropped the lines of Velopack's shortcut scan that name the
maintainer's own shortcuts.** The predicate: a line containing `.lnk` that contains
neither `BrowserAI (suite)` nor `*.lnk`. So the search patterns and every line about the
suite's own shortcut stay, and nothing else was removed.

| Original | Lines | Cut | SHA-256 of the original |
|---|---:|---:|---|
| `logs/setup.log` | 1,018 | 280 | `eee780b4df71126947926c62e2ef6dd3bb8b545d4ee9dc4626248f0a3970ad5d` |
| `logs/uninstall.log` | 323 | 280 | `bcf0a1b3d7be47d9ba9063a5deab94011d299d1cc6a0530efdb79e0b0bf999a8` |
| `logs/update-apply.log` | 1,039 | 280 | `b0e05eec4bdb5c97a83763592077023d58bc2be43807d88807e3b8ae1544e2ca` |
| `logs2/setup.log` | 1,018 | 280 | `0c3fa214428f950de02d18b9758d0ad33912a6712c054b1cf123d7821002eb9d` |
| `logs2/uninstall.log` | 323 | 280 | `17588348af18454bb62a6c10c9903cd0b2ab549dbd42610f6e49708fea14be80` |
| `logs2/update-apply-2.log` | 1,026 | 280 | `ff3053d1663101cbcb194824a01fdc70ed1899e4b0e63b4a54aa8dca474dff5c` |
| `procs/setup.stdout.txt` | 876 | 140 | `05b51c3e18e2d5980856dc841f67579391b7d9b4b675b91080bccc744085b6b2` |
| `procs/uninstall.stdout.txt` | 183 | 140 | `df3fde4d292b15b0dca9084fbb9e16eaf561e08a4e57c824162f5d8b7401cd13` |
| `procs/update-apply.stdout.txt` | 898 | 140 | `8dd414c7d44881bbbb69faf6fdb33bb42df62048f5409bf71e607345c21a574e` |
| `procs2/setup.stdout.txt` | 876 | 140 | `56b05ac914bffd00dc64b50b2f1506a1e7d40d96cbf25a2bb9077bcc3cf1677f` |
| `procs2/uninstall.stdout.txt` | 183 | 140 | `96b68cf1bcf6806208399718844a8b3c4db5d06db7344e5c81083376031aecf5` |
| `procs2/update-apply-2.stdout.txt` | 885 | 140 | `673157e5ac590ebc6bc85481c78ab74daa03fe8d31d6c7f8828bef345702e4d0` |

`logs2/update-apply-1.log` and `procs2/update-apply-1.stdout.txt` had no such line and
are kept whole.

⚠️ **The sandbox client's two installation identifiers are cut -- Q298, decided
2026-09-25 by the maintainer, in his words: *"Q298 b"*.** *Added by addition; the phase 1
writer had kept the file whole (N105) because it is the record that the installer hook's
registration went to the sandbox and was gone after the uninstall.* That record is its
`"mcpServers": {}`, which stays, as does every other member. The two values Claude Code
wrote to identify the installation, `machineID` and `userID`, each 64 hexadecimal
characters, are replaced by a sentence saying so, and the file is here as
`sandbox/client/.claude.trimmed.json`. No account or organisation identifier was in it.

| Cut | SHA-256 of the value, as UTF-8 |
|---|---|
| `machineID` | `237d176ca65b217d32d0ce237451cd0c5c3be0b00e9432f0c8d836e1b4fe08b8` |
| `userID` | `73c16c4a7b4cb8b813a4ad354887d921f50b46209426fda7561890f78e8cf085` |

The original `sandbox/client/.claude.json`, 519 bytes, had the SHA-256
`de55cda8a5c55e2584636d1609ee4c692fb919382048fe249f106ad7bea1a844`. **The commits before this cut still carry both values**, since this
directory was committed whole on 2026-09-24; cutting them from the tree does not remove
them from its history.

| Left out | Why | SHA-256 of what it was |
|---|---|---|
| `vp/`, 37 files, 387,781 bytes | Copies of Velopack's own source. Upstream is the record: **tag `1.2.158`, commit `3c7f52c1bf17d10ad21b794b006d5ebd1a879a3b`**, read through `gh api` on 2026-09-24, and the nine files the kb cites were compared with that commit byte for byte and matched | -- |
| `feed/BrowserAI.app.test-1.1.1-full.nupkg`, 55,022,766 bytes | The package run 1 and run 2 applied; `vpk-pack.log` rebuilds it from `packdir/` | `efef897997ee2b939dcf7651614e54e0de21a25f387cc949ec00d34c38730499` |
| `feed/BrowserAI.app.test-win-Setup.exe`, 62,585,518 bytes | The installer `vpk` built beside it, never run: both rigs installed the suite's own `Releases\test-pack\BrowserAI.test-installer.exe` | `ec9812dd18e94ff0f1800e8abccbf6db00b24f356dfb8b29905dd8e8837b2cdb` |
| `feed/BrowserAI.app.test-win-Portable.zip`, 54,984,012 bytes | Built beside it, never used | `9a589a16950822e52ae7680c90f81ed36b50f5a0df2c9fde1c9c852603c332c9` |
| `feed/RELEASES`, `feed/assets.win.json`, `feed/releases.win.json` | The feed files beside the package; the rigs staged the package by path and read no feed | `3c36efa0e22de0fd019232de6149e31e61aec074a54de38a1e3d50668bfb7efa`, `7b3ca070a2de79bb7eaa5eb062f9088f73c3a04db051e564cabef35fc74241d8`, `167b3f637f532dce04b9d026d8ec68f6f20b3785534f23934d24038048bbac22` |
| `packdir/`, 206 files, 143,719,408 bytes | What `vpk` packed as 1.1.1: an installed test pack's files, whose binaries report assembly version 1.1.0 in every survivor's log. `BrowserAI.Server.exe` and `BrowserAI.exe` are the two that ran | `8eb6641f71212ea23b49978008443893cfb94c6f45679ea768fd4019061d8f4f` and `2d7fe2f8e695cc93de389ca8553899c9fc02c9675365d289e9c4e607233476a0` |
| `proto/out/SingleProbe.exe`, 1,544,704 bytes | The prototype as published; `proto/` rebuilds it | `4275e1b5be925873f1877f0311e94949cba83d3e4af5a8bf1774642d6161af39` |
| `proto/out/SingleProbe.pdb`, 8,409,088 bytes | Its symbols | `237c3a6826259b67ee0694c7a5c5372c801943e75d4d8da3fa53e45a870295fd` |
| `proto/bin/`, `proto/obj/`, 219 files | Build outputs and restore state | -- |
| `cwd/`, `emptyfeed/`, `sandbox/codex/`, `sandbox/client/backups/` | Empty directories: the servers' working directory, the empty feed they were pointed at, and two the client never wrote to | -- |

⚠️ **Four kinds of change to what was kept, and nothing else moved.** `Rig.cs` and
`proto/Program.cs` carry the two-line SPDX header this repository requires of every
source file it tracks, prepended when the batch was cut. The three rig scripts are
stored as `.ps1.txt` with their bytes as taken, because this repository's link check
reads every `.ps1` for relative paths and a PowerShell cast such as `[int]` followed by
a parenthesis reads to it as a Markdown link. `proto/probe-noarg.log` was
`probe-noarg-[$(Arg0)].log`, renamed for the same reason; its one line is as taken.
And the `.trimmed.` files above.

⚠️ **The researcher's own report is not here.** It went to the session that dispatched
it, and what it concluded is in that session's ledger and in `DECISIONS.md`. **One of
its figures could not be re-read from a file in this batch or from the machine**: *"a
per-user logon task ran at +1.25 s"*. The writer's re-reading in `writer/sign-in.txt`
finds the process a third-party task started at +1.249 s, and that task's trigger is
**for any user**, not for the signing-in one; the only task with a trigger scoped to the
user ran at +16 s, after a 15-second delay of its own. The kb records the re-reading.

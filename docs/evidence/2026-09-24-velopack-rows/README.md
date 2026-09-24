<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- the four Velopack rows, re-run at 1.2.158

**What this is.** The re-measurement of re-verification rows **123**, **124**,
**126** and **130**, which had been taken at Velopack 1.2.0 and were owed since the
build moved to **1.2.158** on 2026-09-22. Taken on 2026-09-24 between 09:33Z and
09:54Z on Windows 11 Pro 10.0.26200, by a read-only researcher, against **the
suite's test pack** -- id `BrowserAI.app.test`, title `BrowserAI (suite)`, version
1.1.0, installer `Releases\test-pack\BrowserAI.test-installer.exe`, 62,585,518
bytes, SHA-256 `8f18747305ef7b1973819f36439f0a6ea0ce47cf652d2d254ed5335ddecc85a0` --
installed into scratch roots under `.work\velopack-rows\` and never into the
maintainer's own install. **88 files with this README, 1.7 MB** -- *corrected 2026-09-24, later the same day (previously "73 files with this README, 1.3 MB"), when the second screen window's runs `I1` to `I3b` were added, taken between 11:34Z and 11:41Z with the maintainer's consent to UI.*

⚠️ **Nothing here touched the maintainer's own state, and the readings say so.**
Every install and uninstall ran with `CLAUDE_CONFIG_DIR`, `CODEX_HOME` and
`BROWSERAI_ROOT` pointed at scratch, because the install hooks register with both
clients. **All 19 clearance readings agree**: the real `BrowserAI.app`
Add/Remove key exported to the same 1,424 bytes with the same SHA-256 in every one,
the Start Menu `BrowserAI.lnk` hashed the same, and no Velopack temp directory was
left behind. The clients' own configurations were deliberately not read, because
reading one through its CLI can write it.

⚠️ **Some of these runs put a window on the maintainer's screen.** The non-silent
install `A1` started the configuration app, whose window came up at 09:36:21Z;
the overwrite dialogs `D1` to `D5` are modal task dialogs, and `D2`'s was up from
09:42:01Z to 09:47:01Z; the stub run `S1` opened the configuration window at
09:48:16Z and closed it with `WM_CLOSE`. The maintainer reported a focus grab at
about that time and then allowed UI for thirty minutes, and the session's ledger
records which run fell where.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: re-verification](../../../kb/re-verification.md) | Rows 123, 124, 126 and 130, replaced by the re-run's text; rows 124 and 130 again the same day, from `I3`/`I3b` and `I2` |
| `HouseRuleTests.EveryProcessLaunchInTheTreeSuppressesTheConsoleWindow` | `logs/I1-hidden-pwsh.txt`: the gate drivers' `Start-Process pwsh -WindowStyle Hidden` shape leaves no visible window and hands nothing to Windows Terminal, which is why that scan accepts it |
| [kb: two installs of one app id](../../../kb/packaging/velopack.md#two-installs-of-one-app-id-share-one-uninstall-key----measured-2026-09-14) | The re-established paragraph: both uninstall orders under the test id |
| [kb: the non-silent install](../../../kb/packaging/velopack.md#a-non-silent-install-starts-the-app-in-a-console-window-and-nobody-is-on-the-other-end-of-it----measured-2026-09-14) | The creation flags, the variable line and the 26.9 ms |
| [kb: the v1.0.0 re-measurement](../../../kb/packaging/velopack.md#re-measured-2026-09-15-against-the-published-v100-and-the-paragraph-above-was-half-wrong) | `Startup[9]` against `Startup[8]`, and the consequence for the server's installer branch |
| [kb: the overwrite dialog](../../../kb/packaging/velopack.md#setup-will-not-install-over-an-existing-install-without-being-told-to-and-on-a-same-version-re-ship-the-button-says-repair----measured-2026-09-16) | The 300 s timeout and the three bodies |
| `upstream-review.json`, the Velopack entry | The four verdicts, and item B's live ARP key |

## The runs, in the order they were taken

Times are the machine's local time, UTC+2, as the captures print them.

| Run | Row | What it was | Files |
|---|---|---|---|
| `A1` | 124, 126 | Non-silent install into `root-a` from a `DETACHED_PROCESS` parent, `--verbose --log` | `logs/A1-nonsilent-setup.log`, `logs/A1-nonsilent-observe.jsonl` |
| `D1` | 130 | Same-version installer over `root-a`, dialog read through UIA, then `WM_CLOSE` | `logs/D1-same-version-cancel-*` |
| `D2` | 130 | The same dialog left alone: gone 299.9 s after it appeared, exit 0 | `logs/D2-same-version-leave-*` |
| `D3` | 130 | `current\sq.version` in `root-a` set to 1.0.0, an older install | `logs/D3-installed-older-*` |
| `D4` | 130 | `current\sq.version` set to 9.9.9, a newer install | `logs/D4-installed-newer-*` |
| `D5` | 130 | A directory holding one text file and no install, `root-c` | `logs/D5-nonempty-noinstall-*` |
| `S1` | 126 | The root stub `BrowserAI (suite).exe`, which runs `Update.exe start` | `logs/S1-stub-stub.txt` |
| `R126` | 126 | The installed server and a byte-identical copy outside any install, each with and without `VELOPACK_FIRSTRUN=true`, through the orphan-console rig | `logs/R126-*.txt`, `sandbox/data-*` |
| `B1`, `U1`, `U2` | 123 | Silent install into `root-b` over a live `root-a`; uninstall `root-b` (the key's root); uninstall `root-a` (no key left) | `logs/B1-silent-*`, `clearance/C-*-B1-*`, `clearance/C-after-U1-*`, `clearance/C-after-U2-*` |
| `A2`, `B2`, `U3`, `U4` | 123 | The other order: install `root-a`, then `root-b`; uninstall `root-a` (the root the key did NOT name); uninstall `root-b` (no key left) | `logs/A2-silent-*`, `logs/B2-silent-*`, `clearance/C-*-A2-*`, `clearance/C-after-B2-*`, `clearance/C-after-U3-*` |
| suite | 124, 126 | `InstallerHandoffTests`, filtered, once from each shell, against a fresh published slice: 9 of 9 each | `logs/suite-InstallerHandoffTests-*.log` |
| `I1` | the scan | The gate drivers' own launch shape, `Start-Process pwsh -WindowStyle Hidden ... -RedirectStandardOutput`, a 20 s sleep, watched for 23 s: its console window stayed invisible, no Windows Terminal or OpenConsole process started for it, 0 visible windows. Every other `conhost` the file lists is some other process's, started in those seconds | `logs/I1-hidden-pwsh.txt`, `rig/item1-hidden-pwsh.ps1` |
| `I2` | 130 | A silent install into `root-d` (211 files), then the same installer non-silently over it and a real mouse click on the dialog's `Cancel`: exit 0, `user cancelled overwrite`, 211 files as before | `logs/I2-*`, `rig/item2-cancel-click.ps1` |
| `I3`, `I3b` | 124 | A probe pack with `--mainExe BrowserAI.Server.exe` under a third id, `BrowserAI.app.r124`, installed without `--silent`: a visible `CASCADIA_HOSTING_WINDOW_CLASS` window of the running Windows Terminal, titled with the server's path; the server logged `Startup[78]` and `Startup[9]` and exited 0 | `logs/I3-*`, `logs/I3b-*`, `logs/velopack_BrowserAI.app.r124.log`, `rig/item3-pack.cmd`, `rig/install-observe-r124*.ps1` |

The four uninstalls are in `logs/velopack-test-id-log-excerpt.txt`: `update:71988`
at 11:50:56, `update:72012` at 11:51:11 with the `os error 2` line, `update:85312`
at 11:51:51, and `update:87968` at 11:52:05 with the second `os error 2` line. The
hooks each install and uninstall ran are in `sandbox/data/logs/`, and the last
registration outcome is `sandbox/data/mcp-registration.json`.

## What is here

| Path | What it holds |
|---|---|
| `logs/*-setup.log` | Setup's own `--verbose` log for each install and dialog run |
| `logs/*-observe.jsonl` | The launch, every process and every top-level window the driver saw, with kernel creation and exit times |
| `logs/*-dialog.txt` | The dialog runs: the window, its UIA tree, and what happened to it |
| `logs/R126-*.txt`, `logs/S1-stub-stub.txt` | The orphan-rig runs and the stub run, each with the product's own log records inline |
| `logs/velopack-*-excerpt.txt` | The lines this session wrote into Velopack's per-app log for the test id, and into the machine-shared `velopack.log` from the two uninstalled copies, each with a header naming where it was cut from |
| `logs/suite-InstallerHandoffTests-*.log` | The two filtered suite runs, with their coverage blocks |
| `logs/detached-probe*.txt`, `logs/dotnet-probe*.txt` | The rig's own probes of how a detached shell behaves; see below |
| `clearance/*.txt` | The 19 clearance readings, each tagged and timed |
| `clearance/C00-baseline-arp-BrowserAI.app.reg` | The real key's export, once; see below |
| `clearance/bracket/` | Two readings from the same day's earlier clearance snapshots, which bracket when the real key was rewritten; see below |
| `sandbox/` | The scratch `BROWSERAI_ROOT`s: the hooks' log and registration record, and one log per orphan-rig run |
| `rig/` | The drivers that took all of the above, `Rig.cs` included, and the two scratch inputs: `sq.version.orig`, the test pack's manifest before `D3` and `D4` edited it, and `root-c/not-an-install.txt` |

## The key the live install carries, bracketed

`clearance/bracket/codex-check-2.txt` was written at **06:57:31Z** and
`clearance/bracket/item3-baseline.txt` at **09:01:46Z**, both on 2026-09-24, read
from the files' modification times before they were copied (git keeps no times).
The first reads the real `BrowserAI.app` key as `EstimatedSize QWord = 140209`,
`InstallDate 20260917`, `DisplayVersion 1.0.0`; the second as
`EstimatedSize DWord = 140351`, `InstallDate 20260924`, `DisplayVersion 1.1.0`,
which is also what all 19 readings of this batch show. The install applied its
update to 1.1.0 between the two, and a `REG_DWORD` is what 1.2.158's
`registry.rs` writes and 1.2.0's cannot.

## What was left out, and what it is cited by instead

- **The source clones**, 210 MB and 1.6 MB, read with `git show` and never checked
  out. Velopack at tag **1.2.158**, commit `3c7f52c1bf17d10ad21b794b006d5ebd1a879a3b`,
  and tag **1.2.0**, commit `f2edcbcafb81da5b3c884aaea330e225ad91d8b6`, from
  <https://github.com/velopack/velopack>. xdialog **3.1.9**, the version 1.2.158's
  `Cargo.lock` resolves: the version bump is commit
  `85d31b631ed8669fcae3c58da3cab9422fa6ad3f`, and the clone's head
  `4473a9e7e0bda284d9c09031223c4ac7a1eb9ba7` differs from it in `Cargo.lock` alone,
  from <https://github.com/velopack/xdialog>. Every file and line the rows cite
  can be read at those commits.
- **Two binaries.** `Squirrel-from-testpack.exe`, the `Update.exe` the test pack
  carries, 4,025,856 bytes, SHA-256
  `6de0cc82a2b03a9e0cdc000f076099ca94084ce49f75fdece91f3358b37e0c5c`; and the copy
  of the test pack's `BrowserAI.Server.exe` that `R126` ran outside any install,
  19,267,072 bytes, SHA-256
  `8eb6641f71212ea23b49978008443893cfb94c6f45679ea768fd4019061d8f4f`.
- **Eighteen of the nineteen registry exports.** Each reading's own text carries
  the export's length and SHA-256, and all nineteen are
  `len=1424 sha256=4AC81C23A1963B8D85FE38A3154D1D32C07B4CEE3692E06C0AE6BDA31ECC788F`,
  so one byte-identical copy stands for all of them.
- **The scratch client configuration** the hooks registered into: a
  `.claude.json` the client wrote on its first start, carrying a machine id and a
  user id it generated, and nothing that was measured.
- **Three screenshots and one empty file from the second window.** `I1-screenshot.png` was deleted by the root session before this cut, because it showed the maintainer's private windows, and was never hashed here. `I3b-r124-nonsilent-console-1.png`, 3,054,421 bytes, SHA-256 `3EC29A328971645228680EEE63E61B89498CE94064DBB9827782124262AD90F5`, is left out for the same reason: the Terminal window it was taken for is framed by the maintainer's own windows and taskbar. `I3b-r124-nonsilent-console-3.png`, 120 bytes, SHA-256 `E1FB82735B66A2D44E94F53290ED2325A3308F262B2B131458A6105FB821AE03`, is an empty crop. `I1-hidden-pwsh-stdout.log` is 0 bytes, SHA-256 `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`, the sleep writing nothing.
- **The researcher's working files**: the four original rows and the replacement
  text, which are in git history and in `kb/re-verification.md` verbatim, and the
  script that assembled the replacement.

## Three departures from the bytes as taken

- **`logs/I3b-r124-nonsilent-observe.trimmed.jsonl` is the observation with one line cut**: its line 15, a second `CASCADIA_HOSTING_WINDOW_CLASS` window of the same Terminal process, which is one of the maintainer's own terminals and whose title is his work. 62 of 63 lines are kept; the original is SHA-256 `651B54D0DF3FFE153C24BAD39421637CE4F8B032631FCABC99269EB6963E7462`.
- **Every `.ps1` and `Rig.cs` under `rig/` gained the repository's two-line SPDX
  header**, because this tree requires one on every script it holds, and
  `item3-pack.cmd` gained the same two lines as `@rem`. Nothing else in them moved.
- **Line endings are this repository's**, per
  [the directory's own note](../README.md): captures written with CRLF are stored
  with LF, and `rig/item3-pack.cmd` keeps CRLF because `.gitattributes` gives
  every `.cmd` that.

## A note on method the probes carry

`rig/run-detached.ps1` starts its scripts through `Rig.StartHidden`, a console
of its own with no window (`CREATE_NO_WINDOW`), and not through
`Rig.StartDetached`. The researcher reported that **pwsh 7.6.6 started with
`DETACHED_PROCESS` exited without running its script**, which is why. The kept
probe files show only the case that worked -- `detached-probe4.txt` is a hidden
start that ran -- so the negative half is the researcher's report and is not
established by anything here. It is not recorded in `kb/` for that reason.

<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- what comes to the front when a folder link is clicked

What the kb entry
[A folder link comes to the front when the process the link starts opens it](../../../kb/windows/loopback-page.md#a-folder-link-comes-to-the-front-when-the-process-the-link-starts-opens-it----measured-2026-10-11)
was read from, taken for the maintainer's 55. The rig is
[`docs/probes/2026-10-11-link-start`](../../probes/2026-10-11-link-start/README.md). One
run, its folder `run-20261011T161900Z`.

| Path | What it is |
|---|---|
| `report.txt` | What the rig's `analyze` wrote: per click, the started process, its parent, the time from the click, its round trip with the stand-in, and every window event the observer recorded after it |
| `steps.json` | The driver's record of the second pass: each click's time, its link, the failed focus call and the window `close-probe-windows.ps1` closed after it; the last entry is the navigation timeout |
| `observe-74560.trimmed.jsonl` | The observer's record: the foreground window, the shell's window events, the windows the run opened, and the last-input tick, never what was typed |
| `handle-*.jsonl` | Each process Firefox had started for a link, six of them: its start, its parent, what it asked the stand-in, the right it handed over and the foreground after its action |
| `standin-98104.jsonl` | The stand-in for the background: what it was asked, whether it held the right to come forward, and what it opened |
| `serve-26132.jsonl`, `serve-90932.jsonl` | The two page servers, on ports 53117 and 53118 |
| `register-41072.jsonl`, `unregister-90388.jsonl`, `prepare-81180.jsonl`, `processes.json`, `urls.txt` | The scheme as registered, the unregister that found it gone, the folders made, the pids `start.ps1` recorded and the three page addresses |
| `snapshot-before.trimmed.txt`, `snapshot-after.trimmed.txt` | Firefox's `Launcher` value names and the probe's key, before and after the run |
| `originals.sha256` | The digest and size of each trimmed file as written, and the digest of each rig file as it ran, `close-probe-windows.ps1` included |

## How these bytes depart from the ones taken

- **Three files are trimmed.** The observer's record and the two snapshots are stored under
  `.trimmed.` names with the user profile path, in executable paths, replaced by
  `%USERPROFILE%`: 16 times in the observer's record and 30 in each snapshot.
  `originals.sha256` carries each original's digest and size. Nothing else in them changed.
- The other files are as written, line endings aside: they name no path under the profile.
- The folders the links opened, the scratch Firefox profile and the probe's build are not
  kept: they hold nothing the entry reads.

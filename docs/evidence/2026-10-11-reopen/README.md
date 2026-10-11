<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- whether a relay's standard handles can be reopened overlapped

What the kb entry
[Both clients hand a server synchronous pipe ends, and no call makes them overlapped](../../../kb/windows/processes.md#both-clients-hand-a-server-synchronous-pipe-ends-and-no-call-makes-them-overlapped----measured-2026-10-11)
was read from, taken for the maintainer's 76 b. The rig is
[`docs/probes/2026-10-11-reopen`](../../probes/2026-10-11-reopen/README.md).

| Path | What it is |
|---|---|
| `standins-20261011T232100Z.json` | The rig's first `standins` run: every handle kind, what each `ReOpenFile`, open by name, completion-port binding and mode change answered, each round trip, and on each synchronous read end the calls made while a read was parked, the `OVERLAPPED` handed to `ReadFile` and `CancelSynchronousIo` |
| `standins-20261011T232253Z.json` | The second run, with the overlapped control's calls while its read was pending added; every other outcome is the first run's |
| `relays-20261011T232330Z.trimmed.json` | The `relays` run: the rig's own ancestry, and each of the eleven processes running from the installed `BrowserAI.exe`, with its parent and its three standard handles |
| `originals.sha256` | The digest and size of the relays file as it was written |

## How these bytes depart from the ones taken

- **The relays file is trimmed.** It is stored under a `.trimmed.` name with the user
  profile path, in the executable paths it names, replaced by `%USERPROFILE%`, twelve
  times; `originals.sha256` carries the original's digest and size. Nothing else in it
  was changed but the line endings.
- The two `standins` files are as written, line endings aside: they carry no path.

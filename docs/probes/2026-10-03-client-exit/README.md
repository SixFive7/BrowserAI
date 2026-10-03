<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- a dummy stdio server, a client that ends, and a clock on both

Establishes
[What each client does to a stdio server when the session ends](../../../kb/mcp/protocol.md#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03)
and re-verification row 169. Evidence:
[`docs/evidence/2026-10-03-client-exit/`](../../evidence/2026-10-03-client-exit/README.md).

**Why it exists.** Q356 a: before deciding how BrowserAI should end its
browsers when a client goes, measure what the client does to the server first.
An earlier kb sentence said the client kills the server tree at exit; this rig
asks when, after what, and how long a server has.

## What is here

| Path | What it is |
|---|---|
| `ExitRig/` | One .NET 10 console app with several modes. `server` is the dummy MCP server with one tool, `ping`: it logs on a QPC clock and in UTC, starts a stand-in child in a `KILL_ON_JOB_CLOSE` job of its own, and on end of file waits `--delay-ms` and exits 77. `standin` sits in that job. `run` and `batch` are the harness: they start the client with `CreateNoWindow` or inside a ConPTY, follow its descendants by parent pid with a toolhelp walk about every millisecond, open a handle on each, and record each exit's time and code. `fakeclient` plants the controls; `keeper` and `launcher` probe a process started outside the server's job; `ttyprobe` checks the ConPTY plumbing |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` | Empty or near-empty files beside `ExitRig/`, so a build of the rig does not inherit this repository's own build rules |
| `anthropic-stub.js`, `openai-stub.js` | Local stand-ins for the two APIs: a `tool_use` or a `function_call` to `ping` until a result exists, then a final turn |
| `vscodehost.js` | A node stand-in for the VS Code extension host's transport, written from a reading of its `extension.js`: the SDK's `close()`, and the host exiting |
| `tools/gen*.ps1` | Write a batch: scenarios, delays and repetitions, one spec per run |
| `tools/analyze.py`, `summarize.py`, `ranges.py`, `appserver_close.py`, `stdin_close_to_exit.py` | One TSV row per server instance per run, the per-scenario table, and the timing ranges |
| `tools/ctx.py`, `tools/slice.py` | Cut an excerpt out of a client bundle around a needle or an offset; that is how `code/INDEX.txt` in the evidence was read |

## What keeps it off the rest of the machine

- Every client ran with `CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `APPDATA`,
  `LOCALAPPDATA`, `TEMP` and `TMP` in scratch, with the base URL pointed at a
  local stub and a key that is not a key. No model was called.
- The clients ran from copies of their binaries in scratch. Their digests are
  in the evidence's `binaries.trimmed.tsv`.
- **It selects no process by image name.** The harness walks the processes
  descending from the client it started, by parent pid, and labels each one by
  its image file name for the log; `fakeclient` runs `taskkill /PID`. A search
  of the files for the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on finds `szExeFile` declared in `ExitRig/Native.cs` for that walk and
  the word `taskkill` in `Harness.cs` and `analyze.py`, and nothing that picks a
  process by its name. ⚠️ **The real scan flags `Harness.cs` and `Native.cs`
  anyway**, measured by copying the rig under `build/` and running
  `NeverByImageNameTests`: `taskkill` on lines without `/PID`, and the
  `szExeFile` declaration. Both are false positives;
  [the directory's README](../README.md) records them.
- Nothing shows a window: the interactive scenarios run inside a pseudo console
  the harness owns.

## Running it

Nobody ran it when this record was written; what follows is read from the
files.

```
dotnet build docs\probes\2026-10-03-client-exit\ExitRig\ExitRig.csproj -c Release
node docs\probes\2026-10-03-client-exit\anthropic-stub.js
pwsh -NoProfile -File docs\probes\2026-10-03-client-exit\tools\gen.ps1 -Batch cc2 -Scenarios b1,b2,c1,c2
<ExitRig.exe> batch <the batch file gen.ps1 wrote>
python docs\probes\2026-10-03-client-exit\tools\analyze.py
```

**The paths are compiled in.** The generators name the scratch root as
`C:\Source\SixFive7\BrowserAI\.work\client-exit`, the client copies under its
`bin\`, the rig executable under `rig\bin\` and node under `C:\Program Files`.
A run from anywhere else needs those changed, and is a new measurement with a
date of its own.

**Run `controls` first.** Each of the five plants one signature the analysis
reads: a clean close, a self-exit, `taskkill`, `TerminateProcess` and the two
together.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every `.cs`, `.js`, `.ps1` and `.py` file.
This directory stands for the rig's own folder: `ExitRig/` and the three build
files keep the places they had relative to each other, and the stubs and
`vscodehost.js`, which ran from beside them, sit here too.
`tools/scan_text.py`, the researcher's text check for the upstream drafts, is
not here, because the drafts are not either.

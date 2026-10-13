<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 - a real Claude Code starting BrowserAI's relay again, before and after a list differs

**What it re-establishes.** The maintainer's 127 c, measured the way his 151 b of
2026-10-13 asks, in his words verbatim *"151 b"*: once, end to end, with a scratch
`CLAUDE_CONFIG_DIR` and `CODEX_HOME`, and never against the installed BrowserAI. A
real Claude Code starts the published relay itself, with nothing between the two, and
lists its tools; a stand-in background ends that relay after its first call with
`browserai/end`, the way an update ends a relay; the client starts a second relay and
calls before it lists. In the `same` mode the second relay meets the record the first
one wrote and has to pass the call on; in the `differ` mode the stand-in first writes
another digest into that record, and the second relay has to refuse the call once, as
Q261 b does. Cited by [re-verification row 149](../../../kb/re-verification.md) and the
kb section on what a client does when its server exits,
[`kb/mcp/protocol.md`](../../../kb/mcp/protocol.md).

**What it touches.** Nothing of the installed BrowserAI and nothing of the person's
own client configuration: each run makes a client configuration, a Codex home and a
project folder of its own inside its output folder, and a data root for the relays
under `%LOCALAPPDATA%\BrowserAI-127c-measure\<run>`, under the profile because a relay
refuses a data root outside it, which the run copies into its output folder and then
removes. The relays reach the stand-in through a pipe of the run's own. Every process
is started windowless, the stand-in and the model stub end themselves when a file
appears, and nothing is stopped from outside. **Run it under the suite lock**: it
starts BrowserAI and claude processes.

## Files

| File | What it is |
|---|---|
| `measure.ps1` | Every pair of a client mode and a record mode, three rounds by default, one run at a time; writes `runs.tsv` and `summary.txt` |
| `run.ps1` | One run: the scratch configuration, the registration, the model stub, the stand-in and the client |
| `summarize.ps1` | What one run did, read from the stand-in's log, the model stub's request log, the relays' own logs and the records; writes `summary.json` |
| `standin.js` | The stand-in background on the relays' pipe |
| `driver.js` | Starts one Claude Code and sees it through one turn, `claude -p` or the stream-json transport |
| `modelstub.js` | The local model stub, scripting four calls of `browserai_list` and then a sentence, taken from [the Q261 rig](../2026-09-24-q261/README.md) |

## Run it

From a worktree whose slice is published (`dotnet publish src/BrowserAI/BrowserAI.csproj
-c Release -r win-x64 --self-contained`), with the suite lock held:

```
pwsh -NoProfile -File docs/probes/2026-10-13-127c-redial/measure.ps1 `
    -Out <an empty folder> `
    -Relay <worktree>\src\BrowserAI\bin\Release\net10.0-windows\win-x64\publish\BrowserAI.exe `
    -Node <worktree>\payload\node\node.exe `
    -PrintClaude <the installed claude.exe> `
    -StreamClaude <the VS Code extension's resources\native-binary\claude.exe>
```

⚠️ *Changed 2026-10-13 after its first attempt, at `2fb8932d`*: the stream-json runs set no
`CLAUDE_CODE_ENTRYPOINT`, because with the extension's own `claude-vscode` the extension's
binary answered *"Not logged in"* to a scratch configuration given an API key and never
called the model stub; and `summarize.ps1` reads a run the stub never heard from as one that
holds nothing. Both attempts are in
[the evidence](../../evidence/2026-10-13-127c-redial/README.md).

A run holds when, in `same`, two relays greeted the stand-in, both naming the client
the driver started and one creation time for it, the model was handed no stale-list
refusal and at least one answer from the second connection, and a relay logged
`Relay[27]`; and in `differ`, two relays greeted it, the record was rewritten, the
model was handed the refusal exactly once and an answer from the second connection, and
no relay logged `Relay[27]`.

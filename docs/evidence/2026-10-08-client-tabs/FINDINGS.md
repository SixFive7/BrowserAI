<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

The Write tool refused a findings file, so the full findings are below. The evidence and scripts are under `C:\Source\SixFive7\BrowserAI\.work\client-tabs\`.

# Which conversation started a server

Measured 2026-10-08, 22:30Z to 23:00Z, after the 17:29Z pause.

**Answer.** Several Claude Code tabs in one VS Code window can be told apart, and each can be labelled with the text on its tab.
- **What works:** the server reads Claude Code's per-process registry file, `<config>\sessions\<parent pid>.json`, to get the conversation's current session id. It then reads that session's transcript, `<config>\projects\<slug>\<id>.jsonl`, and applies the same title rule as the VS Code extension. This named all 21 emulated tabs correctly at the end of every run (through `/clear`, renames and restores), and all 18 terminal runs.
- **What fails:** the environment variable `CLAUDE_CODE_SESSION_ID` tells tabs apart from the first instant, but it goes stale after `/clear` (6 of 6). The protocol carries no conversation id for Claude Code (0 of 54 servers).
- **Codex is the reverse:** there is no file to look up by process, but every `tools/call` carries the thread id in `_meta` (24 of 24), and the thread's name is stored on disk.

## Versions, runs and method
- **Clients:**
  - Claude Code 2.1.295 (npm `latest`/`next`, read 22:4xZ; `stable` is 2.1.286) for the terminal and `-p` runs.
  - Claude Code 2.1.292, the VS Code extension's own bundled binary and the one the live tab runs, for the tab runs.
  - Codex 0.162.0 (npm `latest`, read 22:4xZ).
  - I read the extension's `extension.js` and `webview\index.js` (2.1.292) without changing them. Both carry a local patch; it only adds a BOM and disables `startClaudeGroup`, which doesn't matter here.
- **Stub:** the client-id rig's stub, extended (`rig\TabRig\IdStub.cs`, `Views.cs`). It logs every MCP message with its `_meta` and answers `tools/call`. It also takes a "view" of the client's records: at start, before each `tools/call` answer, at end, and whenever a once-a-second read changes. A view covers:
  - the registry file for its parent pid, with `procStart` checked against the parent's creation time;
  - the transcripts for the environment id and the registry id;
  - for Codex: the rollouts, `session_index.jsonl` and the thread table.
- **Model:** local stubs, so no real model turns. They call `ping` once per prompt and answer a title request with the title the prompt names.
- **VS Code tabs: 3 reps × 2 windows. 21 tabs, 21 servers, 27 tool calls.**
  - One driver process per window stands in for that window's extension host. It is the parent of every tab in the window.
  - Each tab runs the extension's binary with the argument list read off live tabs (`--resume=<id>` added for restored tabs) and the environment `extension.js` sets. I added this session's extension-host variables, minus `VSCODE_PID`, `VSCODE_IPC_HOOK` and `CHROME_CRASHPAD_PIPE_NAME`.
  - The driver speaks the extension's control protocol: initialize, prompts, `generate_session_title {persist:true}`, and rename done the extension's two ways (append a custom-title record, then send `rename_session {source:"host"}`).
  - Window 1: tabs A, B, C opened fresh; C renamed; B closed and restored as B2 with `--resume=<B>`; `/clear` typed in A.
  - Window 2: tab D opened fresh, then closed and resumed in two tabs at once (D1, D2).
- **Terminal: 18 runs, 18 servers, 21 tool calls.** Run in a pseudoconsole with no window, with `--dangerously-skip-permissions`, which is the launch shape of every live terminal session on this machine. Six arms, 3 runs each:
  - plain `claude`: prompt, `/rename`, `/clear`, prompt;
  - `--session-id U`;
  - `--resume U`;
  - `--continue`;
  - `--resume U --fork-session`;
  - `-n "<name>"`.
- **`claude -p`: 15 runs, 15 servers.** Five arms, 3 runs each: `--session-id V`, `--resume V`, `--continue`, fork, plain.
- **Codex: 18 runs, 21 servers, 24 tool calls.** Six arms, 3 runs each:
  - `exec`;
  - `exec resume <id>`;
  - `app-server` with two threads;
  - terminal UI with `--no-daemon`;
  - terminal UI as it starts by default (through its background app-server);
  - `app-server` with `thread/name/set` between two turns.
- **Live observation (read-only):** `Win32_Process` command lines at about 17:20Z (before the pause), then at 22:17Z, 22:44Z, 22:46Z and 22:57Z. Registry and transcripts read for structure only. Window captions compared in memory; only booleans were written.
- **Not counted:** three one-tab shake runs, and a first terminal attempt (stopped; see Deviations).

## Per strategy

### 1. Environment variables
**Claude Code** sets `CLAUDE_CODE_SESSION_ID` for every server it starts (54 of 54).
- **Tells conversations apart: yes, except two processes on one session.**
  - 12 fresh tabs across 3 reps and 2 windows got 12 different ids.
  - Each id equals the one the client reports on its own output stream (21 of 21).
  - Two tabs resuming one session at once share the id (D1 = D2, 3 of 3).
- **Stability:** fixed when the server starts.
  - It equals `--resume` (VS Code 9 of 9, terminal 3 of 3) and `--session-id` (terminal 3 of 3, `-p` 3 of 3).
  - Terminal `--continue` gets the continued session's id (3 of 3). A fork gets a new id (6 of 6).
- **`/clear`:**
  - The server is not restarted (6 of 6), and the variable keeps the old id (6 of 6).
  - So it then names the previous conversation: VS Code tab A reported "Apples tab" while the tab showed "After clear tab" (3 of 3).
- **`claude -p --continue`:** the server starts with a throwaway id (3 of 3). The registry switches to the right one later.
- **Available:** before the first message.

**Codex** passes only an allowlist of variables, and none identifies the thread (21 of 21). The binary contains `CODEX_SESSION_ID`, but no server ever received it.

### 2. The parent's command line
Tells conversations apart **only for resumed conversations**:
- **VS Code fresh tabs:** no id on the command line (12 of 12 emulated; 4 of 12 live tabs before the pause).
- **VS Code restored tabs** (after a window reload) carry `--resume=<id>`:
  - live before the pause: 8 of 8 tabs, each matching its registry;
  - live now: 1 of 1. Tab 38916's id was the registry id of fresh tab 40232 before VS Code restarted.
- **Terminal:**
  - `--resume`/`-r` and `--session-id` carry the id.
  - Plain `claude` and `--continue` carry nothing.
  - With `--fork-session`, the id shown is not the new conversation's.
  - `-n "<name>"` carries a name the person chose (3 of 3 became the registry name, source `user`).
- **Codex:** only `exec resume <id>` (3 of 3).

It is fixed at launch, so it goes stale after `/clear`. As a label source at the end of the runs it was right for 9 of 21 VS Code tabs and 6 of 18 terminal runs.

### 3. The client's own records, and the tab title

**Registry: `<config>\sessions\<pid>.json`.** `<config>` is `CLAUDE_CONFIG_DIR`, otherwise `%USERPROFILE%\.claude`. I saw it from versions 2.1.288 through 2.1.295.
- **Fields:** `pid`, `sessionId`, `cwd`, `startedAt`, `procStart`, `version`, `kind`, `entrypoint`, `name`, `nameSource`, `nameSince`, `status` (idle/busy), `updatedAt`, `messagingSocketPath`, `peerProtocol`, `peerFeatures`, `pidDomain`. A `.key` file sits beside it; never read that.
- **`procStart`** is the process creation time. It matched the parent in 284 of 284 scratch views, and the live tabs to the millisecond (15 of 15 before the pause) and to the microsecond (now).
- **Who writes it:** every Claude Code mode, including `-p`; all have `kind: interactive` (54 of 54). It is deleted when the client exits (18 of 18).
- **`sessionId` follows the live conversation:** it moved to the new id within the one-second poll after `/clear` (6 of 6), and equals the resumed id (12 of 12).
- **`name`:**
  - By default it is derived: `<folder>-<2 hex>`, with a random suffix per process (D1 and D2 differ, 3 of 3).
  - It becomes the person's name with `nameSource: user` on a rename (6 of 6) or with `-n` (3 of 3).
  - AI titles never change it (21 of 21 tabs, plus the live tab).
  - A name the person gave carries into the conversation after `/clear` (3 of 3).
- **Read safety:** 54 servers polled it once a second with 0 parse failures.

**Transcript: `<config>\projects\<slug>\<sessionId>.jsonl`.**
- **No file until the first prompt** (24 of 24 new conversations). For a resumed conversation the title is there when the server starts (VS Code 9 of 9, terminal 6 of 6).
- **AI title:** written when the first prompt is submitted, by a separate model request that runs alongside the turn. It was already present at the first tool call in 12 of 12 VS Code tabs and 8 of 12 terminal sessions; in the other 4 it appeared within 0.7 s. With a real model this is a race, not a guarantee.
- **Custom title:** written on a rename (two records: one from the extension, one from the CLI) and with `-n`.
- **Kept near the end:** title records are written again at the end of the file on every turn. The live tab's 24 MB transcript has 257 AI-title records, the last 292 bytes from the end.
- **`-p` sessions get no AI title** (15 of 15 here; 0 of 38 real `-p` transcripts from the last 14 days).

**Where the VS Code tab title comes from** (extension 2.1.292):
- A new panel shows "Claude Code"; a restored session shows its title from the extension's session list.
- On the first prompt the webview shows the prompt text, then asks the CLI for a title and shows the answer. The CLI answered within 1 ms, with the AI title it had already written.
- A rename shows the custom title.
- The tab shows at most 25 characters: a longer title is cut to 24 plus an ellipsis.
- The session list reads only the first and last 64 KB of a transcript. It takes, in order: custom title (tail, then head), AI title (tail, then head), last prompt, summary, first real prompt, then "Image" or "Document".

**Terminal title.** Claude Code sets the terminal's own title to a status glyph plus the session title:
- "claude", then "Claude Code", then the AI title after the first prompt, then the new name after `/rename`.
- A resumed session starts with its stored title.
- In 18 of 18 runs the last terminal title equals the label read through the registry.

The Codex terminal UI's title is "BrowserAI" (the repository root's folder name) plus a spinner, never the thread (6 of 6).

**Reading without races or harm:**
- Open each file read-only, sharing read, write and delete, and close it at once. Never hold a handle.
- Retry a failed parse once.
- Accept the registry only if `procStart` matches the parent's creation time.
- Read only the first and last 64 KB of the transcript.
- Checked against the maintainer's 138 transcripts from the last 14 days (485 MB, structure only):
  - All 82 that have a title had it within the last 64 KB (furthest 33,411 bytes from the end; largest file 80 MB).
  - Reading only the first and last 64 KB gave the same answer as reading the whole file in 138 of 138.
  - Titled by entry point: VS Code 37 of 42, terminal 45 of 57, `-p` 0 of 38, Claude desktop 0 of 1. None had a custom title.

### 4. What the protocol carries
**Claude Code:** no session id anywhere in incoming messages (0 of 54 servers). As a positive control, the same search finds the per-call tool-use ids in 54 of 54.
- The `server/discover` `_meta` has the protocol version, client info and capabilities; `initialize` has client info.
- The `roots/list` answer gives the folder.
- `tools/call` `_meta` holds only `claudecode/toolUseId` and `progressToken` (63 of 63 calls).
- `/clear` and renames send the server nothing.

**Codex:**
- `initialize` and `tools/list` carry nothing (21 of 21).
- Every `tools/call` `_meta` carries `threadId`, `sessionId` (equal to it), `windowId` (`<thread>:0`), `callId`, `itemId` and `x-codex-turn-metadata` (24 of 24). The turn metadata includes `thread_id`, `turn_id`, `codex_version`, `thread_source` and `turn_trigger`.
- The thread's name never appears in `_meta` (6 of 6 calls after naming).

### 5. The VS Code window
- **Extension host:** each window has one. It is a `Code.exe` utility process (`node.mojom.NodeService`), and it is the parent of every `claude.exe` that window's tabs start.
  - Live before the pause: 12 tabs under 3 extension hosts (9, 1 and 2 tabs).
  - Now: 1 window, 1 tab.
  - In the emulation: the same grandparent for every tab in a window (21 of 21, by construction).
- **Environment:** nothing differs per window. `VSCODE_PID` and the crashpad pipe name the main process; everything else is constant.
- **Folder:** `CLAUDE_PROJECT_DIR`, the working folder and the `roots/list` answer all name the window's folder (21 of 21 consistent).
- **`~\.claude\ide\<port>.lock`:** one per window, holding the workspace folders, the main process's pid (not the extension host's), and an auth token the relay must not read.
- **Window caption:** readable without showing or focusing anything. Reading it changed nothing on screen; the foreground window was unchanged (1 of 1). It contained the folder name but not the tab title, because it names the active editor. Linking a caption to an extension host is only possible by matching the folder name, which is a heuristic.

### 6. Codex
- **Thread id from `_meta`** at every `tools/call`:
  - `exec` 3 of 3; `exec resume` 3 of 3 (same id as the command line);
  - `app-server` 6 of 6 (each thread's own id);
  - terminal UI 3 of 3; default terminal UI through its background server 3 of 3.
- Nothing identifies the thread before the first call.
- **`app-server` runs one server process per thread:** two threads started two servers, each seeing only its own thread (3 of 3).
- **Default terminal UI:** the server's parent is the background app-server, not the terminal (3 of 3).
- **Thread names:**
  - In the scratch homes, `state_5.sqlite` has a `threads` table: `title` is the first user message (15 of 15) and `name` is the name set with `thread/name/set` (3 of 3).
  - Naming also writes `session_index.jsonl` with `{id, thread_name, updated_at}` (3 of 3).
  - The real `~\.codex` (desktop app, version 0.159 alpha) has `session_index.jsonl` with 4 named threads (I read keys only) and no `state_5.sqlite`.
- **`CODEX_HOME` never reaches the server**, so a relay has to assume `%USERPROFILE%\.codex`.

### 7. Live observation
- **Before the pause:**
  - 15 `claude.exe`: 12 VS Code tabs (2.1.288) under 3 extension hosts, and 3 terminal sessions started from bash.
  - Each had exactly one `BrowserAI.Server.exe` child and a registry file.
  - Restored tabs' `--resume` matched the registry (8 of 8); 4 fresh tabs had no id; every name was derived (15 of 15).
- **After the pause:**
  - One VS Code tab, pid 38916 (2.1.292, `--resume=3220505d…`), with its server 38952. Registry and resume id match.
  - One terminal session, 33736 (2.1.295), gone by 22:44Z; it apparently exited at 22:28Z, not through me.
  - No `codex.exe`.

## Recommended approach for the relay

**Claude Code (all three modes):**
1. **Registry:** read the parent pid and its creation time, open `<config>\sessions\<ppid>.json`, and accept it only if `procStart` matches.
2. **Label:** from the registry `sessionId`, read the first and last 64 KB of the transcript and apply the extension's rule. Cut to 25 characters for a VS Code tab; show the full text for a terminal. With no transcript yet, show "new conversation in <folder>"; the tab itself shows "Claude Code".
3. **When to read:** on demand, when the dashboard or toast is drawn, and at each `tools/call`. Never hold the files open.
4. **Fallbacks, in order:** the environment id (stale after `/clear`), then `--resume`/`--session-id` from the parent's command line, then "Claude Code in <folder>".
5. **Windows:** group VS Code tabs by the parent's parent pid (the extension host) and name the group by its folder.

**Codex:**
1. Keep `_meta.threadId` from the first `tools/call`; one server serves one thread.
2. Label it with `thread_name` from `session_index.jsonl`, falling back to the thread's first message.
3. Before the first call, show "Codex in <folder>".

## Decisions for the maintainer
The project rules reserve these for the maintainer. Each question has its own options, results and recommendation.

**1. Which source should identify a Claude Code conversation?** The relay has three sources (environment, registry, command line), and they disagree after `/clear`.
- a. Environment id only: right for 18 of 21 tabs at the end, wrong after every `/clear`, wrong for `-p --continue`.
- b. Registry by parent pid: right for 21 of 21 tabs and 18 of 18 terminal runs, but it is an undocumented file (seen in 2.1.288 to 2.1.295).
- c. b, falling back to a, then to the command line.
- d. Protocol only: impossible, 0 of 54 carry an id.
- e. Ask the session over its messaging pipe: needs a secret token; not measured, and I'd rule it out.

I recommend c.

**2. Which text should the relay show?** The person recognises a conversation by its tab or terminal title.
- a. The extension's own rule over the first and last 64 KB of the transcript: right for 21 of 21 tabs at the end.
- b. Registry `name`: useful only when the person named the session; otherwise it shows `<folder>-xx`, which nobody has ever seen.
- c. First prompt only: wrong once a title exists.
- d. Window caption: covers only the active editor, and only by folder matching.

I recommend a. It already covers b's case, because a rename also writes the transcript.

**3. When should the relay refresh the label?** A fresh tab has no title when its server starts. The title appears at the first prompt and changes on rename and `/clear`.
- a. Once at startup: fresh tabs never get a title, and `/clear` and renames are missed.
- b. At every `tools/call`: current whenever the conversation uses BrowserAI, but misses changes in between.
- c. On demand, when the dashboard or toast is drawn: always current when a person looks.
- d. A file watcher on `<config>\sessions` plus c.

I recommend c, plus b if the dashboard keeps a cache.

**4. How should Codex conversations be named?** Nothing names the thread until the model calls a BrowserAI tool.
- a. `_meta.threadId` from the first `tools/call`, with the name from `session_index.jsonl`, else the first message.
- b. The kind only ("Codex"): never wrong, never specific.
- c. `exec resume <id>` from the command line: covers only resumed exec runs.

I recommend a.

**5. Should VS Code tabs be grouped by window?**
- a. Group by extension host pid, label by folder: exact grouping, but two windows on one folder share a label.
- b. Group by folder only: merges two windows on one folder.
- c. Match window captions to folders: a heuristic.
- d. No grouping.

I recommend a.

## What cannot be told
- **A fresh Claude Code conversation before its first prompt** has no title anywhere; VS Code itself shows "Claude Code".
- **Two processes on one session** (D1/D2, 3 of 3) share the id and the title; only pid and start time differ. The extension refuses to open one session in two tabs, but a terminal and a tab can share one.
- **The AI title can arrive after the first tool call** (4 of 12 terminal sessions).
- **Which VS Code window**, in terms the person sees: the extension host pid is exact but invisible, and caption matching is a heuristic.
- **Codex before its first tool call:** the thread is unknown.
- **`-p --continue`** has the wrong environment id at startup.
- **Remote and cloud Claude Code sessions** don't write the registry (gated in code); not measured.
- **Hosts I did not run:** JetBrains, Claude desktop, Agent SDK hosts, the Codex IDE extension.
- **None of these files is a contract:** the registry, the transcript layout, `state_5.sqlite` and `session_index.jsonl` can change in any release.

## Other findings
- **The repository's `.mcp.json` leaks into child folders.** Claude Code finds it by walking up from any folder below the repository, so a client started there connects the repository's four project servers. Only a refusal in user settings stopped it; the refusal list in `.claude.json` did not.
- **Codex's `pid-update-loop` helper survives `daemon stop`** (3 of 3), matching the earlier client-id finding.
- **Codex records `app-server` threads as `source: vscode`**, whatever the host calls itself.

## Deviations
- **VS Code itself was not driven**, because of the no-window rule; the tabs are the emulation described above. The real webview asks for a title right after the first prompt, while my driver asked after the turn. So only end states are scored for correctness, and title timing comes from the transcripts.
- **Two shake runs (`vs-shake`, `vs-shake2`) connected the repository's project MCP servers:** microsoft-learn and context7 over HTTPS, and nuget through `dotnet dnx` from the existing package cache (no new package folder). github failed to connect. From `vs-shake3` on, every counted run connected only the stub.
- **The first terminal batch stalled on a permission prompt.** I checked its 4 processes (59404, 78028, 68764, 77284) by path and start time, stopped them, deleted the runs and re-ran with `--dangerously-skip-permissions` plus an allow rule.
- **A size scan of the real `~\.claude\projects` ran over 2 minutes,** so I stopped it and used the 14-day structural survey instead.
- **Codex 0.162.0 replaced the earlier rig's 0.161.0.**
- **No sub-agents, no commits, no pushes, no repository files touched.**

## Decisions I took for review
- Used the current npm `latest` releases: Codex 0.162.0, and Claude Code 2.1.295 for the CLI runs.
- Gave terminal runs `--dangerously-skip-permissions`, the live sessions' own launch shape.
- Limited the real-transcript survey to the 138 transcripts modified in the last 14 days.
- Deleted the Codex background-server package copies (3 × 442 MB) and the npm cache. Kept the client binaries (about 930 MB) so the runs can be repeated.
- Did not try the messaging pipe, because it needs a token.

## What is left
- Cutting this evidence into `docs/evidence` and `kb` is your call.
- The hosts not run, and how the real webview times its title against the first tool call.

## Cleanup proof
- **Processes: none left.**
  - A query for `client-tabs` in any process's command line or image path returns 0. During a run, the same query found the runner, driver, tab and stub.
  - The 3 Codex helpers (72548, 71988, 58668) were stopped by pid after checking their image was under scratch and they started after the phase began.
  - Every batch logged `STUB_STOP exited=True` for its model stub.
  - The only .NET build processes running predate my builds and belong to others.
- **Configuration: all of it stayed under `.work\client-tabs`.** Every `CLAUDE_CONFIG_DIR` and `CODEX_HOME` I set lives there (`runs\*\r*\cfg`, `runs\cx\r*\home`, `h\d1..d3`, `cfg-unused`).
  - The real `~\.claude.json`, `~\.claude\settings.json`, `~\.codex\config.toml` and `~\.codex\session_index.jsonl` contain 0 of my markers; the same search finds 1 in each scratch copy.
  - No real project folder is named after `client-tabs` (scratch has 2), and no real Codex rollout is newer than 22:30Z (scratch has 15).
  - `~\.claude.json` changed at 22:28:03Z, the same second the real `sessions` folder changed when your terminal session ended. That attribution is an inference: the file holds none of my markers.
- **Disk:** 1.1 GB remains under `.work\client-tabs`.
- **Locks:** none taken, since nothing started a browser or BrowserAI.

## Files
Everything is in `C:\Source\SixFive7\BrowserAI\.work\client-tabs\`:
- Stub and harness: `rig\TabRig\` (`IdStub.cs`, `Views.cs`)
- VS Code driver and batch scripts: `rig\vsdrive.js`, `rig\genvs.js`, `rig\runvs.js`
- Terminal, `-p` and Codex batch scripts: `rig\gentui.js`, `rig\genprint.js`, `rig\gencx.js`
- Analysis: `rig\anavs.js`, `rig\anaruns.js`, `rig\ana-score.js`
- Live probes: `rig\live-procs.ps1`, `rig\live-sessions.js`, `rig\live-windows.ps1`, `rig\live-windows.js`
- Per-run tables: `runs\vs\tabs.tsv`, `runs\tui\runs.tsv`, `runs\print\runs.tsv`, `runs\cx\runs.tsv`
- End-state scores: `runs\score.txt`
- Live results: `live\procs-*.json` and `live\sessions-*.json` (structure only), `live\windows-1.json` (booleans only)

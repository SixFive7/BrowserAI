<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# One windowless binary: the plan, before anything is built

**A plan for the maintainer to take apart, written 2026-10-04.** Nothing in it is
built. What he has already decided is quoted and marked as decided. Everything else
is a proposal with its alternatives beside it, and every decision it needs is
numbered D1 to D14 near the end, so it can be answered by number. Each number
says where it was measured or read; a claim that was read in code or in a binary
and never run says so, and so does one nobody has established.

**The direction, in his words of 2026-10-03 and 2026-10-04:**

> "I favor simplicty and less code complexity over process crash isolation. I mean,
> if we have a dying coordinator we already have a serious problem. The solution
> should be in stable code and exp backoff auto restarts and self healing over blast
> radius isolation by creating separate processes."
>
> "I really don't like how the relay is serving the client directly by itself, the
> old way, as a fallback. I want one system that works. I don't want to have
> fallbacks into different systems. That only adds complexity."
>
> "I'd argue that the relay always answers the tool list from the binary. I see no
> reason why it would ever defer to Playwright, as the Playwright version is bound to
> that binary version is it not?"
>
> "at some point the agent's request to the relay will time out. Before that point, I
> think it would be good if the relay could clearly communicate that it is having
> trouble starting the background process. That should be named in such a way that
> it's clear that somebody needs to look at the logs to figure out why it keeps
> dying."
>
> "12 a+b and maybe c. Let's wait for the 11 a design to discuss. Because I want to
> discuss the whole architecture ones you have worked it out in a plan and try to
> shoot holes into it that we need to rethink before building it all."
>
> "13 Why is the window the playwright startup time? Did we not decide not to wait on
> that? I thought we would always return the tools directly from browserai and always
> have playwright lazy load? With lazy load I mean, it starts asap, but we do not wait
> for it. I do not mean we only start starting playwright on the first actual
> toolcall. Though, thinking about it. I also do not want to have a playwright
> instance for every claude code or codex session that has the browserai mcp server
> loaded even when it is not using it. Or should we have just 1 per system on
> standby? Let's fold that into our planned discussion."

**Decided already, and kept as it is:**

- No fallbacks: one way of serving a client, and nothing behind it.
- The tool list is generated at build time from the pinned Playwright and checked
  against the live child.
- The resume rules: a resume of a live session is a no-op answered "the session is
  already live", unless a passed setting conflicts; a conflict is refused, naming the
  parameters, saying to close and then resume, and saying that this closes and
  re-opens the Playwright browser.
- Every clean close has the one cap of a minute; a reopen waits for a close in
  flight, and only a destroy cuts one short.
- An unknown argument gets a syntax-error refusal carrying the tool's definition; an
  unknown tool gets the tool names with one line each.
- The reason of every close is recorded and told to the agent.
- Hidden Chromium sends the headed user agent.
- Chromium's full-page screenshots get the check for the 16,384 px repeat.
- `browserai_catch_up` names every file that can hold sensitive data.
- When a client goes, its sessions keep running; an update closes those kept
  sessions cleanly, as built.
- The branch model: one branch, `master`, which takes every commit as soon as it
  exists. Only a release is gated, and only a release has to be stable.

## The short version

1. **One file, `BrowserAI.exe`, with no console and no window of its own.** Today
   there are two programs: `BrowserAI.Server.exe`, the MCP server a client starts,
   and `BrowserAI.exe`, the coordinator behind the browser tab. They become one
   program that the clients, the Task Scheduler, the installer and a person all
   start, and its command line says which job it does.
2. **Three kinds of process while it runs.**
   - **A relay per client.** Every Claude Code or Codex process that has BrowserAI
     loaded starts one. It answers the handshake and the tool list itself, at once,
     from a list built into the binary, and passes every tool call to the
     background.
   - **One background process per user and install.** Only the per-user scheduled
     task starts it, so no client's kill reaches it. It holds every session, every
     browser, the browser tab and the update.
   - **One Playwright `node` per open session**, started when the session opens, as
     today. None for the tool list, none per idle client, none on standby.
3. **No fallback.** When the background cannot be reached, nothing serves the
   client in-process. The relay holds the model's call, starts the background again
   with pauses that grow, registers the scheduled task again if it is missing, and
   before the client gives up it answers with an error that names the failure and
   says somebody has to read the log.
4. **One process holds every session**, as the maintainer chose: a fault in the
   background ends every browser on the machine at once. The relays survive it and
   start a new background, and the sessions come back through `browserai_resume`.
5. **BrowserAI's settings are command-line arguments, and a client's facts travel
   over the pipe.** A process the Task Scheduler starts never sees a client's
   environment (measured), so nothing may depend on it.
6. **What goes:** serving a client in-process; the session host and the coordinator
   as two processes; the Playwright child that only answered the tool list; the
   front's 15 s wait for a host; the 500 ms coordinator call limit and the arm that
   went red on it six times; and, if the tab reads the background's own memory, the
   pipe every server serves and the live markers.
7. **What an idle client costs.** With 1.1.0 installed, each Claude Code or Codex
   process with BrowserAI loaded holds a server of about 25 MiB private bytes and
   its own Playwright `node` of about 97 MiB, used or not. Read on this machine on
   2026-10-04 from its ten live 1.1.0 servers: about 1.2 GiB private in all, each
   server with the one Playwright that answers its tool list and none for a session.
   Under the plan an idle client costs one relay process, whose size nobody has
   measured; a whole 1.1.0 server process is the upper bound.
8. **What does not change:** the session folder is the identity; the tool surface,
   the verdicts and every refusal; the close cap and the ordering rule; kept
   sessions; and an update that waits until no client's BrowserAI runs.

It is built in eight steps, each useful on its own and each checked by the
two-shell gate after it lands; the steps are in "The steps" below.

```
the client's process tree                       the user's scheduled task
-------------------------                       -------------------------
Claude Code or Codex                            Task Scheduler
  |                                               |
  +-- BrowserAI.exe --mcp  ======= pipe ======>   +-- BrowserAI.exe --background
      the relay: one per client,                      one per user and install:
      ends with its client                            sessions, the tab, the update
                                                        |
                                                        +-- job -- node (@playwright/mcp) -- browser
                                                        +-- job -- node (@playwright/mcp) -- browser
                                                            one per open session
```

## The weakest points, first

1. **One process holds every session, and its fault is a hard kill of every browser
   on the machine.** The maintainer chose this trade in the words quoted above. What
   a hard kill costs is measured and recorded in [DECISIONS](../../../DECISIONS.md):
   Chromium keeps a cookie only once it is about 30 s old and `localStorage` about
   5 s; a Chromium profile killed before its first Preferences write restores
   nothing; and with `--hide-crash-restore-bubble` the tabs come back, 11 of 11 that
   had them on disk. Lane c's session host on `master` already holds every session
   in one process. The plan adds the tab, the update and the apply loop to that
   process, which reverses the 2026-09-15 cut that kept SQLite, the MCP SDK and the
   sessions out of the window process. Not established: whether anything in the
   tab's listener or the update lane can bring the process down; no fault has been
   injected into either.
2. **A background that hangs goes unnoticed.** A crash closes the pipe, and every
   relay starts a new background. A hang, a deadlock or a stuck thread pool, keeps
   the pipe open, and calls wait until the client's own limit: Codex 300 s by
   default (read in its source at 0.155 and 0.160), Claude Code 30 minutes (read in
   its binary). This plan's first version detects nothing of the kind; D10.
3. **The Task Scheduler becomes the only way BrowserAI runs.** The start through it
   is measured: 14 of 14 in 514 to 674 ms (the one-binary measurement). Not
   measured: the Task Scheduler service unavailable, a policy that forbids user
   tasks, the task disabled by a person, its End command, and sign-out. One case
   follows from the task's definition with no measurement at all. The task runs with
   `InteractiveToken`, and Microsoft documents that such a task "will be run only in
   an existing interactive session"
   ([logonType](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-logontype-simpletype)).
   So an agent started over SSH, as a service or by a CI runner, for a user who is
   not signed in at the desktop, gets no BrowserAI at all, where today the server
   serves it in-process. While the same user is signed in at the desktop, the task
   should run in that session and an SSH-started relay should reach it; that is not
   measured either.
4. **The two measurements this plan stands on are in scratch only.**
   `.work\onebinary-measure\` and `.work\startup-measure\` are gitignored, and
   neither is in `kb/` or `docs/evidence/`. Step 0 persists them, together with this
   plan's own readings (the Claude Code binary's text, the Codex source, the memory
   figures).
5. **The relay has to understand the protocol.** Lane c's front copies bytes and
   parses nothing. This relay reads the method and the id of every frame the client
   sends: it answers `initialize`, `ping` and `tools/list` itself, holds a
   `tools/call` while it has no background, answers held calls when the start
   fails, and replays the client's `initialize` to every new background. That is new
   code on the path every call takes, which is one of the reasons lane c turned its
   direction a down ([the coordinator-owned design](../coordinator-owned-browsers/README.md)).
   Once connected, frames still pass byte for byte.
6. **B of Q369.4 does not hold up.** Read for this plan: `codex mcp add` at
   0.155.0-alpha.9.2 writes `required: false` and offers no flag to change it
   (`codex-rs/cli/src/mcp_cmd.rs`). So a required entry means editing
   `config.toml`, which RegisterAI's contract rules out ("It never edits a client's
   configuration file", its README) and which DECISIONS refuses for a client that
   has a command of its own. Claude Code 2.1.288 describes `alwaysLoad` in its own
   binary: "When true, all tools from this server are always included in the prompt
   and never deferred behind tool search ... As a side effect, true also blocks
   startup until the server is connected (capped at the standard 5s connect
   timeout)". So it would put every BrowserAI tool definition into every
   conversation's prompt. With A built, the relay answers the handshake within its
   own start, and BrowserAI's own start work before Playwright took 36 to 63 ms
   (startup measurement); B then buys nothing anyone has measured. D6.
7. **A new file name breaks every registration that names `BrowserAI.Server.exe`.**
   The update hook rewrites the user-scope entries. Nothing rewrites a client that
   kept the old command line across the update, a Claude Code project entry
   committed in a repository (`${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe`),
   or a Codex project entry, which names `BrowserAI.Server.exe` bare
   (`RegistrationClient`). D7; keeping two files with their roles moved would avoid
   it, at the price of the one file.
8. **An update still waits for every client.** An idle Claude Code window keeps its
   relay running from the install root for as long as it is open, so an update
   applies only when every client with BrowserAI loaded has gone, or a person closes
   them from the tab. That answers his Q383: it is not ten minutes of inactivity
   plus a minute of closing. D4.
9. **The update check moves with the background.** Today every server start asks the
   feed once (Q225 b). A background that asked only when it starts, and was kept
   alive for weeks by an editor that is never closed, would never learn of an
   update. D9.
10. **The design point has never run through one process.** The charter's design
    point, about 100 concurrent BrowserAI processes, runs in `SaturationTests` as 100
    server processes, 8 of them with browsers. Under lane c and under this plan the
    same load is up to 100 sessions in one process, and nobody has run that.
11. **A headed window opened by a task-started process is not measured.** The
    Task Scheduler's process holds no right to the foreground (measured three times
    on 2026-09-24, in [DECISIONS](../../../DECISIONS.md)), and every headed run in the
    reports of 2026-10-03 and 2026-10-04 was in-process or on a hidden desktop.
    Whether such a window
    shows in front, behind, or takes the focus is not known; lane c's host on
    `master` already has the same gap.
12. **Proxy settings now come from the user's own environment.** A session's
    Playwright inherits `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`, `ALL_PROXY` and
    `NODE_EXTRA_CA_CERTS` (`ChildEnvironment`). Today a client's environment
    provides them; under the plan the background's does, which is the environment the
    Task Scheduler builds for the user. A proxy set only in one terminal no longer
    reaches a browser download. Whether anyone relies on that is not known.
13. **The Claude Code first-turn figures may describe tool search switched off.**
    Lane stale recorded on 2026-10-03 that tool search was off in every stand-in run
    from 2026-09-23 on, and the startup measurement does not say how it ran. With
    tool search on, Claude Code defers every server's tools by default, in its own
    words quoted in point 6, and what a slow server costs the first turn then is not
    measured. The plan does not depend on it, because the relay answers at once
    either way.
14. **The size of the one binary and the memory of a relay are not measured.** After
    the 2026-09-15 split the server was 19,180,032 bytes and the app 10,382,848.

## The processes at run time

| Process | How many | Started by | When | Ends | Holds |
|---|---|---|---|---|---|
| The relay, `BrowserAI.exe --mcp` | one per client process with BrowserAI loaded | the client | when the client starts its MCP servers: a Claude Code session, a Codex `exec` or thread | when the client ends it: Claude Code's `taskkill /T /F` or the end of its input, Codex ending its job (measured, windowless and console forms alike) | the client's connection and its held calls |
| The background, `BrowserAI.exe --background` | one per user, install root and data root | the scheduled task only: its logon trigger, or a run asked for by a relay or a person's start | at sign-in, or when a relay or a person finds none | a minute after its last relay, session and tab have gone; or an update, an uninstall, a crash, the task's End command, sign-out | every session with its lock, record and child; the tab's listener; the update |
| Playwright, `node.exe` with `@playwright/mcp` | one per open session | the background, in a kill-on-close job of its own | `browserai_init`, or a resume that opens the session | the session's close, idle close, release or destroy; an update; the background's end | the session's browser, which it starts at the first browser call |

**Containment is unchanged.** Each session's Playwright and browser sit in a
kill-on-close job that only the background holds, so the background's death, however
it dies, ends them all. A process the Task Scheduler starts sits in a job the
scheduler made, with no limit flags, and kill-on-close jobs of our own nest under it,
measured three levels deep ([the coordinator-owned design](../coordinator-owned-browsers/README.md)).

| Alternative to one background process | What it costs |
|---|---|
| Lane c as built: the coordinator, and a session host it starts | Two processes, two pipes and a bounded call between them; the tab and the update are kept apart from the sessions, which is the isolation he set aside |
| A host per client, kept when its client goes (lane c's direction d) | A relaunched client has no link to the host it had, so a kept session can only be reached by handing its lock and its child across processes |
| Sessions in the background and the tab in a process of its own | One more process to start, and the sessions page still asks the background for everything it shows |

**One background per pipe name, and the name says which.** The pipe is named for the
install root and the data root, and it is created as every pipe of ours is: its DACL
admits the current user alone, remote clients are refused, and
`FILE_FLAG_FIRST_PIPE_INSTANCE` keeps a second background off the name, so two task
runs at once leave one background and the other exits. A background that has decided
to stop refuses new connections with a sentence saying so, as the coordinator does
today, and the relay that meets it starts the next one once it has gone.

### How many Playwright processes, and when

**Playwright here means a `node.exe` running the payload's `@playwright/mcp`.** One
starts in 0.35 to 0.52 s with a warm disk cache and 1.08 to 1.6 s with a cold one
(startup measurement), and an idle one holds about 97 MiB private (read on
2026-10-04 from ten of them). BrowserAI needed one for two things: answering the
tool list, and driving a session's browser. With the list in the binary, only the
second is left.

**A Playwright cannot be started ahead of time for a session.** Each one is launched
with the session's own configuration file, its working directory in the session's
output folder and its own `TEMP` and `TMP` (`ChildLaunch`), and that configuration
names the session's profile and output folders. A standby process would have to be
ended and replaced by the session's own, so the only thing it could ever warm is the
disk cache.

| | 1.1.0, installed today | Lane c, on `master`, not released | This plan |
|---|---|---|---|
| For the tool list | one per client process, started before the handshake is answered | one, in the session host | none |
| Per open session | one | one, in the host | one, in the background |
| Per idle client | one, about 97 MiB private | none | none |
| What waits for a Playwright start | every client's handshake: 0.45 s median and 1.86 s p90 over 673 real starts | a cold host's handshake | only the call that opens a session |

His words, "it starts asap, but we do not wait for it", land on the background: the
relay starts it the moment the client starts BrowserAI, and nothing waits for it but
a call that needs it. The choices are D5.

## The modes, and how each is chosen

**A windowless start with no standard handles reads end of input at once.** Started
that way, as the Task Scheduler, the installer and a double-click start a program,
the windowless build got `Stream.Null` for stdin and exited in 52 to 78 ms, 6 of 6
(the one-binary measurement). Every client gave its server a pipe on stdin, in all 54
runs. So the relay can never be what a start with no argument does.

| Mode | Command line | Started by | How it is recognised |
|---|---|---|---|
| Relay | `BrowserAI.exe --mcp` | a client, from its registration | the argument, and stdin must be a pipe; with no pipe it writes one log record and exits |
| Background | `BrowserAI.exe --background`, its settings as further arguments | the scheduled task only | the argument |
| Installer hooks | `--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, `--veloapp-uninstall` | Velopack | served first, by Velopack's own `Run()`, as the app serves them today |
| A person's start | no argument, or `--sessions` for the sessions page; the suite adds `--write-address` so that nothing opens | the Start Menu, `Setup.exe` after a non-silent install, `Update.exe start`, a double-click, the update toast once built | no argument, or only those two |
| Report | `--report <path>` | a person, or a support request | the argument |
| Sweep | `--sweep` | one re-verification row in `kb/` | the argument |

**The registration carries the argument.** RegisterAI passes everything after `--` to
the client unchanged (its README), so the hooks register
`<install root>\current\BrowserAI.exe --mcp`, where today they register the server
with no argument, and RegisterAI itself needs no new release for it.
`RegistrationTarget`'s check that the registered file is a console binary turns into
a check that it is a windowless one.

| Alternative to one file | What it costs, and what it buys |
|---|---|
| **Two files with the roles moved**: `BrowserAI.Server.exe` becomes the relay alone, and `BrowserAI.exe` the background with the sessions, the tab and the update | The strongest alternative. Every file name, subsystem and registration stays as it is, so D7 disappears, and the relay carries no session code. It costs two publishes and two files to keep in step, packed and replaced together, so they still cannot be of two versions; and it is not the one file of his direction |
| Three files: relay, background, and the program a person starts | Three of everything, for no process the plan does not already have |

| Alternative to choosing by argument | Why not |
|---|---|
| Tell a client's start by its stdin alone: a pipe means a relay | A hook whose stdin Velopack pipes, or a person piping into the binary, would become a relay. The argument says what was meant; the pipe check stays as a second condition |
| Two file names for one binary, the name deciding | Two names to keep in step; D7 is the one place a second name may still earn its keep |
| An environment variable | Refused by the settings rule below |

## Every flow

### A client starts

1. The client starts `current\BrowserAI.exe --mcp` with pipes on all three standard
   handles.
2. First, Velopack's start with automatic apply switched off, as in both programs
   today.
3. The relay opens the process log, takes stdout through `StdioChannel`, and reads
   the client. Everything slower, the judgement of its roots included, runs beside
   the answer below and never in front of it.
4. **It answers `initialize` at once, from the binary**: `{"tools":{}}`, the server's
   name and version, and the server instructions. Nothing it needs comes from
   another process.
5. **At the same moment it looks for the background** on the background's pipe, and
   runs the scheduled task when there is none ("The background will not start",
   below).
6. `tools/list` and `ping` are answered from the binary too.

What the client sees is a server that answers its handshake within the relay's own
start time, which nobody has measured for this binary. A stand-in that answered at
once had its tools in every first turn, 78 of 78 runs (startup measurement). What a
person sees in `/mcp` is BrowserAI connected, even while its background cannot
start. That was accepted with A: the failure shows on the first call, in words.

| Alternative to a relay that answers by itself | Why not |
|---|---|
| The relay copies bytes, as lane c's front does, and the background answers the handshake from the binary | Every handshake waits for the background to be reached, about half a second through the task when none runs; and when none can start, the client sees a server that failed to start, so no sentence can ever reach the model, because only a tool result does |
| The relay answers `initialize` and forwards `tools/list` | The first turn still waits for the background |
| The relay also answers what it can judge alone: unknown tools, argument syntax | Two doors to keep in step; the door stays in one place, the background |

**The update window of Q369.1 shrinks with it.** A server killed before it answers
`initialize` is a failed server for that conversation. Today the window is
Playwright's start, 0.45 s median and 1.86 s p90; under the plan it is the relay's
own start. His answer to Q369.1 was 1, keep Q296 c, to be reviewed again with Q369.4,
and Q296 c's in-process server during an update no longer exists under the plan.

### The first call

1. **Connected:** the relay forwards the frame and copies the answer back byte for
   byte.
2. **Not connected yet:** the call is held, in arrival order, and released when the
   background has answered the relay's first message.
3. **Cancelled while held:** dropped, with no answer, as MCP has it.
4. **The start keeps failing:** each held call is answered with the named error,
   before the client's own limit (D8).

Measured with a stand-in: Claude Code 2.1.288 and Codex 0.155 and 0.160 waited 1 to
90 s for a held call and delivered it, 78 of 78; no client cancelled one; Claude Code
sends a `tool_progress` heartbeat every 30 s while it waits; and a failure sentence
returned as an `isError` result reaches the model in both (startup measurement).

### The tool list

**Built from the payload at build time.** `build/upstream-snapshots.mjs` already
starts the payload's own `node` and Playwright with every capability, asks
`tools/list` and writes the answer to `upstream-snapshots/tools-list.json`: 72 tools
and 76,137 bytes on `master` today, compared with the payload on every build
(`UpstreamSnapshotTests`). The plan compiles that file into the binary. The payload
and the binary are packed together and replaced together in `current\`, so the list
and the Playwright it describes cannot come from two versions.

**Rewritten at run time, as today.** `SessionToolSurface.Rewrite` puts BrowserAI's
eight tools first, adds `session` and `why` to every forwarded tool, drops the `deny`
rows and appends the BrowserAI notes. 72 tools reach the model today, 64 of
Playwright's and 8 of ours. The rewrite reads `tool-verdicts.json`, which ships in
the payload today and is a startup failure when it is missing; the relay has to answer
before anything can fail that way, so the plan compiles the verdicts into the binary
beside the list, and the row in `AGENTS.md` that ships them in the payload changes
with it.

**Checked against the live child, once per session.** Each session's Playwright is
asked `tools/list` right after its handshake, before it has opened a page, and the
answer is compared byte for byte with the built-in list. On 0.0.83 the two were equal
for 70 of 70 tools (startup measurement, before lane q371 denied six more). A
difference means a broken install: the session does not open, and the answer says so
and names the first tool that differs. A page's own tools never take part, because no
page exists when the question is asked.

**The schema rule's new words** (decided): schemas come from the child's
`tools/list` at build time, from the same pinned payload, and are checked against the
live child at run time. `LosslessPassthroughTests` and the row in `AGENTS.md` change
with it.

| Alternative | Why not |
|---|---|
| A file in the payload beside `tool-verdicts.json` | One more file the relay reads before its first answer, and one more that can be missing |
| A list the background writes when it first meets a new version | Stale or absent exactly when it is needed, at the first start after an update (the Q369 follow-up's direction 2) |
| Ask a live child each time, as today | The wait this plan exists to remove |
| A one-shot check child at the background's start, in place of the check per session | A process and a lifetime of its own, where each session's child is starting anyway |

### Session open, close, idle, resume and takeover

**Lane c's behaviour as built and amended on 2026-10-04, now inside the
background.**

- **Open** (`browserai_init`): the lock, the record, the session's Playwright with the
  list check, then the answer. The browser starts at the first browser call.
- **Close** (`browser_close`, or `browserai_stop` if D1 takes it): the clean close with
  the minute's cap; "the agent's own close", or the other client's name when another
  client sent it, written to the session's log as the reason; every later call
  refused until resume, with that reason.
- **Idle**: a headless session with a browser up and no call for ten minutes gets the
  same close, reason "idle for ten minutes"; a headed one never does.
- **A person closes a headed window**: recorded as the person's close, and every
  later call refused until resume with that reason (decided).
- **Resume**: the decided rules; it waits for a close in flight; it reopens through
  the browser's own session restore; with remembered settings, D2 decides what the
  model has to say first.
- **Takeover**: a kept session goes to the next call that names it, from any client; a
  second client is refused, by name, while the first is still connected.
- **Destroy**: cuts a close short, then deletes.

### A client goes, and kept sessions

**In plain words.** A *client* is one Claude Code or Codex process, and its relay is
its BrowserAI. A *session* is a session folder with its Playwright and its browser.
When a client ends, because it was closed, killed, or VS Code restarted, its relay
dies with it and the background sees that client's pipe close. Each session that
client was driving is then judged once: with no browser up, it is released at once;
with a headless browser up, it is **kept**, its idle timer still running from the last
call; with a headed browser up, it is **kept** for as long as its window is open,
looked at every 15 s. A kept session is a browser still running with nobody driving
it, waiting for any client to name it again.

This is lane c's design, measured with the real programs: after each of four client
exits, the coordinator, the host and the browser were alive 6 s later, and the next
client took the session over with the page still loaded, 12 of 12 (the one-binary
measurement).

### Update

1. **The check**: the background asks the feed; D9 says when.
2. **The download and the stage**: in the background, as the coordinator does today.
3. **The gate**: the background holds a handle on every process whose image is under
   the install root, relays included, and waits on them and on its pipe. When it is
   the only one left, it closes every kept session at once, each within the minute's
   cap, writes "an update" as the close reason, hands the package to
   `Update.exe apply --silent --norestart --waitPid <its own pid>`, and exits.
   Velopack then ends what is left under the root and swaps `current\`.
4. **A person in a hurry**: the tab's install button asks every relay to end its
   client's connection, then closes the sessions and applies. A person's click is not
   BrowserAI ending itself, so the rule of 2026-09-24 still holds.
5. **A relay that starts during the apply** finds the install's `Update.exe`
   running, by its full image path, the detection Q286 b built. It starts no
   background, and answers its held calls with the existing update sentence. Started
   before the swap, it is ended by Velopack's kill pass; started after it, it starts
   the new background once the updater has gone.
6. **After the swap**, the `--veloapp-updated` hook registers the new binary with both
   clients, registers the task and keeps the PATH, as today, and starts nothing. The
   next relay starts the new background.

**Kept sessions do not hold an update, and the update closes them cleanly
(decided). A connected client does hold it**, because its relay runs from the install
root. D4 is what that means for when an update can apply.

**The test pack's downgrade** (risk 6 of the one-binary measurement) is closed by one
comparison: the update client applies no package whose pack id is not the installed
one. Velopack 1.2.161 picks the newest full package in the feed without looking at
its pack id, and BrowserAI allows downgrades, so a test pack on a release version
would be offered the production 1.1.0 (read in code and in the feed, not run).

### The background dies: backoff, and the named failure

1. **The pipe closes**, and every relay sees it at once. A call in flight is answered
   "BrowserAI's background process stopped while this call was running, so it may
   have partly happened; check before repeating it", the in-flight shape the update
   refusal already has.
2. **Every browser died with the background**, through the jobs, and the kernel
   released the sessions' locks.
3. **The relay starts a new background** through the task: the first time at once,
   then after pauses that double, each start waiting for the pipe to appear. The
   numbers are D8.
4. **Once it is up**, the relay sends its first message again and replays the
   client's `initialize`. A later call naming an old session is refused with "call
   browserai_resume" and the reason the new background reads in the session's log,
   where the last rows show no close: "BrowserAI's background process ended without
   closing this browser (a crash, a sign-out or a kill), so the newest changes may be
   missing".
5. **When it keeps failing**, each held call is answered before the client's own
   limit, with a sentence like this draft, whose wording is his to approve as the
   q371 texts were:

> BrowserAI's background process keeps stopping. It was started {n} times in the
> last {s} seconds and stopped each time, most recently with exit code {code}.
> Nothing was run. A person needs to read BrowserAI's log, {log path}, to find out
> why it keeps stopping; until then every BrowserAI call fails the same way.

Its catalogue name would be `BackgroundKeepsStopping`. It has siblings, each a row of
its own: the Task Scheduler refused to run the task, with its `HRESULT`; the task is
disabled (D12); the background stopped answering (D10); and a build that is not
installed found no background (D11).

**Where the reason can be found.** The background writes a Critical record for an
unhandled exception before it exits, as both programs do today. A crash that leaves
no record, such as a fail-fast or an access violation, still leaves its exit code with
the relay, which holds a handle on the background once connected, and in the task's
last run result. What a NativeAOT fail-fast writes, and where, has not been looked
at.

| Alternative | Why not |
|---|---|
| The Task Scheduler's own restart on failure | Its shortest interval is one minute ([Microsoft's schema](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-interval-restarttype-element)), and whether it counts a non-zero exit as a failure is not read |
| The background restarts itself | A dead process restarts nothing |
| Every relay restarts it with no pause | Twenty clients would start twenty backgrounds in one moment; the pipe keeps one and the others exit |

### The background will not start: the task missing, disabled or refused

1. The relay finds no pipe and asks the Task Scheduler to run the task named for its
   pack id and install root (`SignInTask.NameFor`), with the start's reason in
   `$(Arg0)`, the mechanism Q283 a measured.
2. **Missing** (`TaskChange.NotRegistered`): the relay registers it again from the
   definition the install hook writes (`SignInTask.DefinitionFor`: the current
   user's SID, `current\BrowserAI.exe`, the same settings), logs that it did, and
   runs it.
3. **Disabled**: D12.
4. **Refused**, by an unavailable service, a denied access or a policy: held calls get
   the Task Scheduler sibling of the named error at once, with the `HRESULT`, and the
   next call asks again.
5. **Started, but no pipe appears**: the start waits up to its bound (D8), reads the
   task's last run result, and counts a failed start.

**What ends the background that the plan cannot prevent**: the task's End command
and `schtasks /end`, which Microsoft documents as stopping "only the instances of a
program started by a scheduled task"
([schtasks end](https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-end));
how it stops one, and whether that is a hard kill, is not measured. Sign-out, since an
interactive-token task lives in the user's session; not measurable on this machine,
where signing out is not allowed while other agents run. And a reboot. Each ends
every browser with the background, through the jobs.

**The task's settings, read in `SignInTask.DefinitionFor`**: instances run in
parallel; it starts and keeps running on battery; no execution time limit (`PT0S`);
normal priority; `InteractiveToken` at least privilege; no idle condition. Its
description changes with its role: it starts BrowserAI now, and deleting or disabling
it stops BrowserAI until a relay registers it again or a person enables it.

| Alternative to the task as the only start | What it costs | Status |
|---|---|---|
| A COM out-of-process server, registered under the user's own classes and started by activation | A registration to install and remove, and a start path of its own; COM's launcher would start it outside the client's job | not measured |
| WMI's `Win32_Process.Create` | A process created by WMI's provider host, outside the client, by a route security tools watch | not measured |
| A Windows service | Elevation to install, and one per machine | ruled out by the per-user install |
| The relay starts it as a child that breaks away from the job | Codex's job allows no breakaway and Claude Code kills the whole tree | measured not to work (lane c) |
| The first relay serves every other client as the background | It ends with its own client, by the same tree kill or job, and takes every other client's sessions with it | follows from the same measurement |
| A resident started from the `Run` key | Starts only at sign-in and late, from +20.8 s on the morning measured, and a background that dies cannot be started again from outside a client's tree | the start order is measured |

### Sign-in

1. The logon trigger runs the task. `$(Arg0)` stays literal at sign-in, as it does
   today, which is how the background knows the trigger started it.
2. The background runs the sign-in step: a staged package with nothing else running
   from the install is applied, as the coordinator does today. Measured on
   2026-09-24: a logon task's process started 1.249 s after sign-in, before
   Explorer, and that day's first BrowserAI server came at +104.8 s.
3. It sweeps the strays a previous sign-in left.
4. It stays a minute for relays and then exits, unless D6 keeps it resident.

### A person's start, the tab and the hooks

- **A person's start**, with no argument, finds the background's pipe, hands over
  `show`, opens the address of a new tab and exits; with no background it runs the
  task first. The background serves the tab exactly as the coordinator does today.
  D13 is whether a person's start may become the background itself.
- **The installer's hooks** run in the one binary as they run in the app today. Install
  and update register with both clients through RegisterAI, now with `--mcp`,
  register the task, and put `current\` on the user's PATH. Uninstall asks a running
  background to close its sessions and exit, then removes the task, the registrations
  and the PATH entry. Velopack gives the uninstall hook 60 s (kb), the same as one
  close's cap, so at an uninstall a slow close can be cut short by Velopack's kill
  pass.

## Settings travel as arguments and over the pipe

**The rule.** A BrowserAI setting is a command-line argument of the process that uses
it; a fact about a client travels in the relay's first message on the pipe; and no
running BrowserAI process reads a `BROWSERAI_` variable. The reason is measured: the
task-started host wrote to the default data root although the installer had
`BROWSERAI_ROOT` set to scratch (the one-binary measurement, risk 1).

**The installer's hooks are the one exception**, because Velopack gives a hook no
channel but its environment and its own fixed arguments. The install and update hooks
read the installer's environment once and write what they find into the task's
action and into the registration's arguments, as plain arguments and not inside
`$(Arg0)`.

| Today | Read by | Under the plan |
|---|---|---|
| `BROWSERAI_ROOT`, the data root | `LocalAppDataPaths.Overridden`, in both programs and in the hooks | `--data-root` on the background, from the task's action, and on the relay, from the registration's arguments; part of the background's pipe name |
| `BROWSERAI_UPDATE_FEED` | `UpdateConfiguration.Resolve` | `--update-feed` on the background, from the task's action |
| `VELOPACK_FIRSTRUN`, `VELOPACK_RESTART` | the app's `Main` | unchanged: Velopack's own way of telling the main program why it was started |
| The coordinator's whole environment, copied into the host it starts | `SessionHostKeeper.EnsureStarted` | deleted with the keeper |
| What a session's Playwright inherits: `PATH`, `TEMP`, the proxy and CA names, `PWTEST_SERVER_REGISTRY` | `ChildEnvironment`, from the server's own environment | the same mechanism, now fed by the background's environment, which the Task Scheduler builds for the user (weakest point 12) |

**The relay's first message** carries its build, its process id and its client's, the
client's name and version from `initialize`, its working directory (the project the
client runs in, which the tab shows today through each server's pipe), and the data
root it was registered for. The background answers with its own build and process id,
or refuses, with a sentence for each: a different build, a different data root, or an
update installing.

**How tests keep their data apart.**

- **Each data root has its own background**, because the root is part of the pipe's
  name, so a test's scratch root never meets the real install's background.
- **A build that is not installed starts no background** (D11). The harness starts
  one with `--background --data-root <scratch>`, outside every client's tree, and the
  relays it drives find it by name.
- **The suite's installer** has its own pack id, install root, task name and pipe; its
  data root reaches the task-started background through the hook's one read, which
  the real-scheduler arm proves.
- **A test hook that is a variable of a child**, `PWTEST_SERVER_REGISTRY`, keeps
  working for a background the harness starts, by inheritance. A task-started
  background never has it, and no arm needs it there.
- **A tree scan fails** when a file under `src/`, outside the hooks' path, reads a
  `BROWSERAI_` variable; planted red first, because a rule a scan can hold is not left
  to a habit.

## The five risks of the one-binary measurement, and the downgrade

| # | Risk, as reported on 2026-10-04 | What the plan does | What is left |
|---|---|---|---|
| 1 | Mode choice: a windowless start with no handles reads end of input and exits within 0.1 s | Every mode is an argument; no argument is a person's start; the relay also requires a pipe on stdin | A client that starts the binary without the argument gets a person's start, which the registration prevents |
| 2 | Environment: a task-started process never sees the client's environment | Settings are arguments; the hooks read the installer's environment once; a scan holds the rule | The proxy variables (weakest point 12) |
| 3 | The task as a single point of failure, its failures not measured | The relay registers a missing task again and names every failure; nothing serves in-process | An unavailable service, a policy, a disabled task, no interactive session: named, not healed (weakest point 3, D12) |
| 4 | What ends the task's job besides the coordinator's own exit, not measured | Read: no time limit, no stop on battery, no idle condition; read and not run: the End command ends it | Measure the End command; sign-out cannot be measured on this machine |
| 5 | Every new front's stray sweep warned about 7 or 8 browser processes it could not attribute while a session was kept | Only the background sweeps, once at its own start, before it holds a session; relays never sweep | A second install's background, the suite's, still meets the first install's browsers and leaves them alone as today, because their locks are held |
| 6 | A tagged test pack could roll itself back to the shipping release (read in code and the feed) | The update client applies no package with another pack id | Not run; a planted-red arm with a feed carrying another pack id |

## What moves, what is deleted, what stays

Line counts are `wc -l` on `master` at `5bf02f48`, comments included.

**Stays, and moves into the background mode:**

- The session machinery of `src/BrowserAI`: `Sessions/`, `Storage/`, `Runtime/`, the
  door in `Proxy/BrowserProxy.cs`, `Proxy/ChildConnection.cs`, and the child's
  transports in `Protocol/`.
- Lane c's host core: `Proxy/SessionHost.cs`, `SessionHostServer.cs`,
  `CallerConnection.cs` and `Protocol/PipeServerTransport.cs` become the background's
  connections. The attachment, detach, keep and takeover in `LiveSession` and
  `SessionManager` stay as built, and so do the close cap and the ordering rule.
- From the app: `Page/` (the tab), `Coordinator.cs` (the sign-in step and the apply
  loop, without the host's keeper), `AppState`, `ClientState` and `StatusReport` (the
  report mode), and `Interop/`.
- From the library: `Hosting/`, `Interop/` (the job launcher, named pipes, the task
  scheduler), `Logging/`, `Registration/`, `Sessions/`, and `Updates/` without a
  server's own apply.

**Changes:**

- **One project and one `Main`**, dispatching on its arguments with the hooks served
  first, published as one Windows-subsystem NativeAOT file. The app's `Main` runs on
  a single-threaded apartment for the shell calls the tab makes, and the server's is
  asynchronous; the background keeps one such thread for the shell, and the relay
  needs none.
- `BrowserProxy`: no `tools/list` reaches a child, and the stale-list refusal of
  Q261 b moves to the relay, which is what knows whether its client has listed.
- `SessionToolSurface` and `ToolVerdicts`: the upstream half of the list and the
  verdicts come from the binary, compiled in from `upstream-snapshots/tools-list.json`
  and `tool-verdicts.json`.
- `RegistrationTarget`, `RegistrationClient` and `McpRegistrar`: the file name of D7,
  the `--mcp` argument, and a check for a windowless file.
- `SignInTask`: the action's arguments and its description; the relay registers the
  task through it.
- The update client: the pack-id check, and the check's timing of D9.

**Deleted, or candidates for deletion, each confirmed by its step's tests:**

| Today | Lines | Why |
|---|---|---|
| In-process serving in `Program.cs`: the surface child, a client's own server, Q296 c's serving during an update | most of 1,117 | The fallback; the relay answers in its place |
| `Program.Host.cs`: the front's byte relay and its search for a host | most of 458 | Replaced by the relay |
| `Coordination/SessionHostAccess.cs` | 210 | The relay's own start path replaces it |
| `Coordination/SessionHostKeeper.cs` | 486 | The background is the host |
| `Coordination/CoordinatorWake.cs` | 123 | No relay stages an update |
| `Coordination/ServerPipeClient.cs`, `ServerPipeProtocol.cs`, `ServerDescription.cs`; `Proxy/ServerPipeResponder.cs`, `ServerActivity.cs`; the pipe per server in `ServerPipe.cs`, whose loop may stay as the background pipe's | up to 1,826 | If the tab reads the background's memory, no process needs a pipe of its own |
| `Updates/LiveInstances.cs` and the live markers | 984 | Its two readers go: a server's own apply, and the tab's list of servers. The apply's gate is the path scan, which is Velopack's own kill set |
| `Page/CensusPageSessions.cs` | 232 | The tab reads sessions and connections in memory |
| The `host` and `recheck` verbs and the 500 ms limit, in `CoordinatorClient.cs`, `CoordinatorPipe.cs`, `CoordinatorInbox.cs` and `CoordinatorProtocol.cs` | part of 889 | One pipe, the background's; `show` and `sessions` stay |
| `BrowserConfiguration.ForSurface`, and the surface child's place in `InstanceDirectory` | -- | No child answers the tool list |

**The tests that go or are rewritten with them:** `SessionHostAccessTests`;
`CoordinatorWakeTests`, with the arm that went red six times on the 500 ms;
`ServerPipeTests` and the census arms; `UpdateInProgressTests` and
`StaleToolListTests`, rewritten for the relay; `InstallerHandoffTests` and
`AppBinaryTests`, for the modes and the subsystem; `VerticalSliceTests`, for a
handshake that starts no child; `LosslessPassthroughTests`, for the schema rule's new
words; `ModelSurfaceTests`, read off the relay's wire; `SaturationTests`, as 100
relays and one background; `RealInstallerTests` and `SessionHostProcessTests`, for
the one binary and the background.

## The steps

**How a step lands.** The branch model is one branch, `master`, which takes every
commit as soon as it exists; only a release is gated and has to be stable. So each
step below is pushed as its commits are made, every behaviour change in it is planted
red first, the ordinary two-shell gate runs after the push, and a red is fixed
forward. Each step carries its own records.

| Step | What it does | What it is worth alone |
|---|---|---|
| 0 | No product code. Persist the two measurements of 2026-10-04 and this plan's readings to `kb/` and `docs/evidence/`. Measure what changes the design: the task's End command on a running background, a missing and a disabled task, the relay's memory with a stub, a headed browser started by a task-started process. Answer D1 to D14 | The facts stop living in scratch |
| 1 | The built-in tool list, in today's two programs: the list and the verdicts compiled in, answered without starting a child, checked against every session's child; the surface child deleted; the schema rule's new words | No process starts a Playwright for the tool list any more, and every server that answers its own handshake answers it at once |
| 2 | One file: the two projects merged into one windowless binary with a mode per argument; the name of D7; the registrations with `--mcp`; the hooks; the subsystem checks turned round | One thing to build, sign and ship; no behaviour changes yet |
| 3 | One background: the coordinator and the session host as one process, with the tab, the update and the only stray sweep; the census and the per-server pipes go if the tab reads memory. Fronts still relay bytes and still fall back | The coordinator call limit and its red arm go; one process to watch |
| 4 | The relay: the handshake, the list and `ping` from the binary; held calls; starts with backoff; the task registered again when missing; the named failures; the in-process fallback deleted; the real-scheduler arm in the suite | His "one system that works", and the first turn for installed clients |
| 5 | Settings as arguments: the data root and the feed; the hooks' one read; the scan that holds it; the pack-id check | Tests that cannot reach the real install's data, and no downgrade from a test pack |
| 6 | What D1, D2, D10 and D12 decided: a stop of BrowserAI's own, remembered settings with their acknowledgement, a hang detector, the disabled task | Each is separable and can move later |
| 7 | The records as a whole: ARCHITECTURE, DECISIONS (the two binaries of 2026-09-15, the schema rule, and lane c's decisions 1 and 2, all reversed by this plan), HAZARDS, TESTING, the kb, README, CHANGELOG and TODO | The documents say what the product is |

## Testing

**Every behaviour change is planted red first**, and the exceptions the house rules
name stay the only ones.

- **In process.** The background's core, as lane c's `SessionHostTests` and
  `CloseOrderingTests` drive it today. The relay against a scripted background: the
  handshake and the list from the binary with no background; held calls in order; a
  cancelled held call; the budget; each named failure; `initialize` replayed to a new
  background; the stale-list refusal; `ping`. The task's path through the
  `ILogonTasks` seam: missing, registered and run; refused; disabled.
- **The published binary.** A relay and a background the harness starts with
  `--background --data-root`, against a real Chromium: a session kept across a relay
  killed the Claude Code way and the Codex way; a background killed, its relays
  starting another, and a resume restoring the tabs. The modes: a start with no handles
  and no argument never reads end of input as a relay; `--mcp` with no pipe on stdin
  logs and exits; no mode puts a window on the screen (`WindowWatch`), and the file's
  subsystem is read out of it (`AppBinaryTests`).
- **The built-in list.** `UpstreamSnapshotTests` on every build; an arm comparing the
  compiled list with a real child; a doctored payload whose session open is refused
  by name.
- **Settings.** The scan for `BROWSERAI_` reads; a first message carrying another data
  root, refused; a test's background and the real install's never meeting.
- **The real scheduler, end to end** (lane c's open question 2, D14). Install the
  suite's pack with the real `Setup.exe`, as `RealInstallerTests` does, and drive its
  relay over stdio as a client would. The background's parent is the Task Scheduler's
  service and it runs inside the scheduler's job; a session outlives its relay's kill
  and is taken over; the task deleted is registered again by the next relay; the task
  disabled gives its named error; a background made to fail at start gives the named
  error before the budget runs out. That last one needs no switch for the suite: a
  data root `InstallRootScope` refuses is a real failure path. Uninstall leaves
  nothing, by the clearance snapshot. All of it under the installer lock, with the
  task removed by the arm.
- **The design point**: 100 relays and one background with 100 sessions.
- **What no suite here can reach**, said and not implied: sign-out, the Task
  Scheduler service stopped, a group policy, a reboot, a user with no interactive
  session.

## The decisions this plan needs

Each has a primer, the directions with what they cost, and a recommendation. D1 to D4
are his open questions; D5 and D6 are his questions 13 and 12; D7 to D14 are this
plan's own.

### D1. A stop of BrowserAI's own, or Playwright's close, before a resume

**Primer.** Switching a session between headless and a window, or putting its browser
away while keeping the session, is two calls today: Playwright's `browser_close`, then
`browserai_resume`. It keeps everything, 13 of 13 runs in both browsers: the cookie
login, `localStorage`, IndexedDB, `sessionStorage` per tab, typed text, history and
every tab (the lifetime review). What is in question is whose words describe it.
**Why a stop of our own reads better, which is what he asked:**
`browser_close`'s name and first sentence are Playwright's, and they say "Close the
page"; in Playwright's own server the next call opens a new browser by itself. In
BrowserAI the same call ends the browser and its Playwright, and every later call is
refused until a resume. An upstream name has come to mean something upstream does not
do, and a model learns that only from the refusal afterwards. BrowserAI may not
rename or reword an upstream tool; since lane q371 it may append a note. A tool of its
own carries BrowserAI's meaning under BrowserAI's name: what it keeps, what a resume
does next, and the reason the log records.

| | Direction | Cost |
|---|---|---|
| a | Keep `browser_close` and add a BrowserAI note: it closes the session's whole browser and not one page, the session keeps its profile and tabs, and `browserai_resume` opens it again | One note; the name still says "page" |
| **b** | **`browserai_stop`, paired with `browserai_resume`, and `browser_close` denied** | One authored tool, one deny row; a model that reaches for `browser_close` from Playwright's documentation gets the unknown-tool refusal with the names, as decided |
| c | `browserai_restart`: one explicit call that closes and reopens with new settings | No locked state between two calls; a new tool whose failure halfway needs its own sentence |
| d | b, with `browser_close` kept and its note pointing at the stop | Two ways to do one thing |
| e | As today | A model keeps learning it from the refusal |

**Recommendation: b**, the lifetime review's, because an upstream name should keep
upstream's meaning.

### D2. How a model acknowledges remembered settings before a window opens

**Primer.** `headed`, `transcript` and the other per-run settings are forgotten at
every close today, so a bare resume after an idle close, an update or a new
background reopens a headed session headless. He liked recording the last settings in
the session's own record so that they survive (his 7 c), and then asked: a new agent
resuming such a session would not know what it starts, a window that may take the
focus, or a transcript that stores typed passwords, so how does the model first
acknowledge the impact? Under the plan the browser starts at the first browser call
after a resume, never at the resume itself, so the resume's answer is always there
before a window can open.

| | Direction | Cost |
|---|---|---|
| a | **Say it, ask nothing.** The resume answer leads with the remembered settings and what each does: "headed: a window opens on the person's screen at the next browser call, and may take the focus" | No extra call, and the house rule holds that a confirmation whose whole content can be returned as fact is a question that did not need asking; a model can read past it |
| **b** | **Restate to proceed.** A resume that would reopen with a remembered setting a person can see or that stores secrets, `headed` or `transcript`, is refused unless the call passes that setting itself, the same value or another. The refusal names each one, its value, when it was set, and what it does | The model writes `headed: true` itself, so it cannot open a window it did not name; one more call, only when such a setting is remembered; it is the syntax error's way of teaching. It is a confirmation in all but name, against the rule `ModelSurfaceTests.NoAuthoredToolAsksTheCallerToConfirmAnything` holds |
| c | Remember only what nobody sees: the viewport and the like; never `headed` or `transcript` | Nothing to acknowledge; a headed window still does not survive an update, the reason he liked c |
| d | The first browser call after the resume is refused once, with the impact; the second goes ahead | Teaches at the moment it matters, on a call about something else |
| e | Ask the person: the tab or a toast asks before a remembered headed session opens a window | The person decides about their own screen; nobody may be at it |

**Recommendation: b**, because it is the only direction in which the model has to
state the impact itself, which is what he asked for. It needs his word that the
no-confirmation rule gives way here.

### D3. Each pipe call's limit, from its caller's own budget

**Primer, in plain words.** When one BrowserAI process asks another a question over a
pipe, the asker waits a limited time for the answer. Today most of those waits are
500 ms, a number taken from how fast a healthy answer is on a quiet machine. "The
caller's own budget" means: set each wait from how long the asker itself can afford to
wait before whoever is waiting on it gives up, which is a person who clicked, a client
waiting for a handshake, or a model waiting for a tool call. Under the plan most of
today's calls disappear with the second process.

| Call under the plan | Who waits on the asker | Proposed limit, and where it comes from |
|---|---|---|
| Relay to background: connect and first message | the model's client | the hold budget of D8, from Codex's 300 s tool limit |
| Relay to background: a forwarded call | the client | none of BrowserAI's own: the client's tool limit applies, and the background bounds its own work (a page tool 60 s, a close the minute's cap) |
| Relay to background: a liveness probe, if D10 takes one | the client | a hang detector: 10 s, `HandOutBound`, the one a person's start already uses |
| A person's start to background: `show` | the person | 10 s, `HandOutBound`, as today |
| Uninstall hook to background: close and exit | Velopack, which gives the hook 60 s | what is left of the hook's 60 s after its own work |
| Gone with the second process | -- | `host`, `recheck`, and every `describe` and `stop` sent to a server's own pipe: the 500 ms `CallBound` has no caller left |

| | Direction | Cost |
|---|---|---|
| **a** | **Each limit from its caller's budget, as in the table** | Each number names its source, and a test holds it there |
| b | One limit for every call, such as 10 s | Simple; too long for some callers and too short for the relay |
| c | Keep 500 ms where calls remain | The measured 1.2 s machine stall still exceeds it |
| d | No limits, trusting the pipe to close | A peer that hangs keeps its caller forever |

**Recommendation: a.**

### D4. Kept sessions and updates: what holds an update

**Primer.** Plain words for "kept" are in "A client goes, and kept sessions" above.
**His question, answered:** under lane c and under this plan, an update does not apply
after "ten minutes of inactivity plus a minute of closing". An update can apply only
when no process runs from the install root except the one applying it, because
Velopack ends every such process (measured, 2026-09-24). Every client with BrowserAI
loaded keeps its relay, or under lane c its front, running from the root for as long as
the client is open, used or not: on 2026-09-23 there were 22 live on this machine, 14
of them never used. Inactivity closes idle headless browsers; it ends no relay. So
the bound is the time until the last such client exits, plus at most a minute for the
clean closes of the kept sessions, which do not hold the update (decided).

| | Direction | Cost |
|---|---|---|
| **a** | **As decided: every client's relay holds the update; kept sessions do not; the tab, and the toast once built, ask the person** | An update waits for every client; nothing is lost silently |
| b | A relay with no session attached and no call for the idle period ends itself when an update is staged | Reverses "nothing exits itself to let an update in" (2026-09-24); a terminal Claude Code then needs `/mcp` reconnect and a Codex thread a reload |
| c | The relay lives outside the install root, survives the apply and reconnects to the new background | The relay of 2026-09-24 kept the connection through an update in both clients, 3 of 3 each; costs a relay copy with an update lane of its own, and a relay and background of different builds talking, with the client holding the old build's list |
| d | Apply only at sign-in | Updates wait for the next sign-in |
| e | Kept headed sessions hold the update too | A forgotten window holds updates for as long as it stays open |

**Recommendation: a.** The only direction that ends the wait without a cost to a
person is c, and it is a project of its own.

### D5. How many Playwright processes, and when (his question 13)

**Primer.** In "How many Playwright processes, and when" above: under the plan
Playwright is needed only for an open session, and one cannot be prepared ahead for a
session.

| | Direction | Cost |
|---|---|---|
| **a** | **One per open session, started when the session opens; none otherwise** | The first `browserai_init` after a cold start waits for one Playwright start, 1.08 to 1.6 s cold |
| b | a, plus a one-shot check at the background's start: start the payload's `node` once, compare its list, end it | Finds a broken payload before the first session and warms the disk cache for it; one start per background start |
| c | a, plus one warm standby per system while the background runs | About 97 MiB held for as long as the background lives, for a disk cache, since a standby cannot become a session's child |
| d | One per client, started as soon as BrowserAI starts and not waited for | About 97 MiB per idle client, 968 MiB for the ten read on 2026-10-04; buys nothing once the list is built in |
| e | Open the browser at `browserai_init` too, and not at the first browser call | A browser for every session opened, used or not |

**Recommendation: a.** Add b only if a measurement shows the first open after a cold
start is slow enough to matter.

### D6. The first turn: A, B and C of Q369.4

**Primer.** A conversation's first request goes out about 1.3 s (Codex) or 2 s
(Claude Code, registered at user scope as BrowserAI is) after BrowserAI starts, and
carries only the tools of servers that were ready (startup measurement). He chose
"12 a+b and maybe c". With A, the relay answers the handshake and the list from the
binary within its own start, so the first turn has the tools. B is weakest point 6:
Codex's `required` cannot be written through `codex mcp add`, and Claude Code's
`alwaysLoad` puts every tool definition in every prompt. By the Q365 page's count, the
72 upstream definitions came to 8,213 tokens and `session` and `why` added 9,380
across the 70 forwarded ones, about 17,600 in all, before lane q371 denied six more
tools; that count has not been taken again since. C,
keeping the background up from sign-in, saves at most the background's own start on
the first call after a cold start: the whole task path, a Playwright start included,
took 606 to 710 ms.

| | Direction | Cost |
|---|---|---|
| **a** | **A alone** | Nothing beyond A |
| b | A, plus Codex `required` written by editing `config.toml` | Reverses RegisterAI's contract and a DECISIONS row, and needs a new RegisterAI release; a relay that cannot start stops Codex opening any conversation (24 of 24 measured with a stand-in) |
| c | A, plus Claude Code `alwaysLoad` | Every conversation carries the whole list in tokens, tool search or not; RegisterAI writes through `claude mcp add`, and whether that command can set the key was not read, so this may need a new RegisterAI release too |
| d | A, plus C | One resident background per signed-in user all day, size not measured |
| e | A, plus Claude Code's per-tool `_meta` key for `alwaysLoad` on `browserai_init` and `browserai_resume` only | Its own text names only the `false` direction of that key; not run |

**Recommendation: a.**

### D7. The file's name, and the registrations that name the old one

**Primer.** Velopack's main executable decides five things at once: the start after
an install, the stub's name at the root, what `Update.exe start` runs, what every hook
runs on, and what the Start Menu shortcut points at. That is why `BrowserAI.exe` went
to the program a person starts on 2026-09-15. Registrations name
`BrowserAI.Server.exe`, user-scope ones absolutely, and project ones in files that are
committed to repositories.

| | Direction | Cost |
|---|---|---|
| **a** | **`BrowserAI.exe` for everything**; the update hook rewrites user-scope entries; the tab lists project entries that name the old file | Project files and clients running across the update break once; AGENTS.md knows of no install beyond the maintainer's own |
| b | Keep `BrowserAI.Server.exe` as the one name and make it Velopack's main executable | No registration changes; what Velopack 1.2.161 does to the old stub and shortcut when the main executable's name changes in an update is neither read nor measured |
| c | `BrowserAI.exe`, plus a hard link `current\BrowserAI.Server.exe` the install and update hooks make | One file, two names; made again after every update, so the old name is missing from the swap until the updated hook has run |
| d | Two copies in the package for one or more releases | One more binary in every download |
| e | Two files with the roles moved, the strongest alternative to one file (in "The modes" above) | No migration at all; it leaves his one-file direction |

**Recommendation: a**, if one file is the direction; e is the one to weigh against it.

### D8. How long the relay holds a call, and how it backs off

**Primer.** The relay holds a model's call while it has no background, and must
answer before the client gives up. The shortest limit among the clients is Codex's
300 s for a tool call by default (read in source at both tags; a 75 s call was
measured to complete); Claude Code's is 30 minutes (read), with a heartbeat every
30 s. One start through the task took 514 to 674 ms.

| | Direction | Cost |
|---|---|---|
| **a** | **Hold for half of Codex's 300 s; pauses doubling from about the measured start, 0.5 s, so about nine starts fit; answer at once when waiting cannot change the cause: the Task Scheduler refused, the task is disabled, or a build that is not installed found no background** | The numbers name their sources; a model on a broken machine waits up to two and a half minutes |
| b | Stop after a fixed count of identical failures, then answer | Answers sooner; the count is a chosen number |
| c | Hold per client: the client's own limit, less a margin, by the name in `initialize` | Numbers per client to keep current |
| d | No hold: answer "starting" and let the model call again | Gives up what A measured: a held call is answered, not failed |

**Recommendation: a.**

### D9. When the background asks the update feed

**Primer.** Q225 b: once per server start of an installed, non-pre-release build, and
never on any other schedule; a machine-wide stamp to skip checks was offered and
declined. The background lives far longer than one client's server.

| | Direction | Cost |
|---|---|---|
| **a** | **Once per relay connection, one check shared while it runs** | The same number of checks as today, one per client start, and the same meaning |
| b | Once per background start | A background held up for weeks by an open editor never checks |
| c | Once per relay connection, at most once an hour, remembered in memory only | Fewer requests; the stamp he declined, in a smaller form |
| d | A timer, such as daily | Reverses "never on any other schedule" |

**Recommendation: a.**

### D10. A background that hangs

**Primer.** Weakest point 2: a hung background keeps its pipe open, and nothing
restarts it.

| | Direction | Cost |
|---|---|---|
| a | Nothing: the client's own limit ends each call | No code; the model waits up to 300 s and learns nothing |
| **b** | **The relay probes the background while calls are outstanding, and answers them with a named error after the hang detector of D3, ending nothing** | A probe answered on a thread of its own, and one more sentence |
| c | b, then the relay ends the background by its process id, checked against its image path under the install root, and starts another | Self-healing in his sense; every browser is killed hard, by a judgement made from outside |
| d | The background watches itself and exits when its own work stops moving | Needs a definition of "stops moving" that a long page tool does not meet |

**Recommendation: b now; c or d once something has been seen to hang.**

### D11. How a build that is not installed gets a background

**Primer.** A checkout, `dotnet run` and every arm of the suite run a binary that is
not installed, which has no task. Today such a server serves its client in-process,
the fallback he does not want.

| | Direction | Cost |
|---|---|---|
| **a** | **It never starts one. The harness or a developer starts `BrowserAI.exe --background --data-root <root>`; a relay with no background answers with the named error that says so** | One start path in the product; a developer runs two commands |
| b | It starts the background as its own child | A second start path, inside the client's tree, so kept sessions die with the client |
| c | It registers a task for its own root, runs it, and removes it later | Tasks left behind by deleted worktrees |
| d | It serves in-process | The fallback |

**Recommendation: a.**

### D12. A disabled task, and whether the task keeps both its jobs

**Primer.** Today the task applies a staged update at sign-in, and lets a blocked
server, or a front with no host, start the coordinator. Under the plan it is how
BrowserAI runs at all. A person who disables "BrowserAI sign-in" probably meant to
stop a start at sign-in, not to break BrowserAI.

| | Direction | Cost |
|---|---|---|
| a | One task; the relay enables a disabled one again and logs it | Always works; overrides somebody's decision |
| **b** | **One task; a disabled one is named in the error, with how to enable it, and left as it is** | BrowserAI stops until a person acts; the person's choice stands |
| c | Two tasks: the logon one, which a person may disable, and one with no trigger that relays run | Disabling the sign-in start no longer stops BrowserAI; two tasks to register and remove |
| d | No logon trigger: the background starts only on demand | No apply before the first client at sign-in, and a relay running from the root holds any apply |

**Recommendation: b**, unless he wants the sign-in start to be something a person
may switch off on its own, which is c.

### D13. A person's start: through the task, or the background itself

**Primer.** A person's start is the Start Menu, the start `Setup.exe` makes after an
install, or a click on the update toast once it is built; each opens a tab. Today,
when no coordinator runs, the app a person started becomes the coordinator itself.

| | Direction | Cost |
|---|---|---|
| **a** | **Through the task, then hand over `show`** | One start path; about half a second more for a first tab |
| b | A person's start becomes the background itself when none runs, as the app becomes the coordinator today | A faster first tab, and a second start path, with the background's parent Explorer or a terminal |

**Recommendation: a.**

### D14. The real-scheduler test: inside the suite, or a rig

**Primer.** Lane c asked whether an arm may start a coordinator through the real logon
task, and recommended a probe rig first. Under the plan the task is the only way an
installed BrowserAI runs.

| | Direction | Cost |
|---|---|---|
| a | No such arm | The one path the product has goes untested |
| **b** | **Inside the suite, with the suite's own pack and task, removed by the arm, under the installer lock** | A real task on the machine for the arm's length, which the clearance snapshot reads |
| c | A probe rig outside the suite | Run by hand, so not on every gate |

**Recommendation: b.**

## Where the facts come from

**In the tree:** [the coordinator-owned design](../coordinator-owned-browsers/README.md),
[ARCHITECTURE](../../../ARCHITECTURE.md), [DECISIONS](../../../DECISIONS.md),
[TESTING](../../../TESTING.md), [HAZARDS](../../../HAZARDS.md) and the code on
`master` at `5bf02f48`.

**In scratch, not in the tree** (weakest point 4), under `C:\Source\SixFive7\BrowserAI\.work\`:

- `reports-2026-10-04\onebinary-measure.md`, with its evidence in `onebinary-measure\`:
  the windowless server under both clients, 54 runs, and the start through the real
  Task Scheduler, 12 counted runs.
- `reports-2026-10-04\startup-measure.md`, with `startup-measure\`: held calls, first
  turns, Codex `required`, the coordinator call under load.
- `reports-2026-10-04\lifetime.md`, `lane-c.md`, `lane-closes.md`, `lane-tab.md` and
  `lane-q371.md`; `q369-followup\REPORT.md`; and `STATE.md` from 2026-10-03T18:14Z,
  which carries every decision of the night in his words.

**Read for this plan on 2026-10-04, and how to read them again:**

- Claude Code 2.1.288's text for `alwaysLoad`: a text search of the client binary kept
  at `.work\q369\bin\cli\claude.exe` for "When true, all tools from this server";
  the binary was not run.
- `codex mcp add`: `codex-rs/cli/src/mcp_cmd.rs` at `rust-v0.155.0-alpha.9.2`, in
  `.work\client-exit\code\codex-src`, where the new entry is built with
  `required: false` and the arguments offer no flag for it. The 0.160.0 copy in
  scratch has no `cli` folder, so 0.160 was not read.
- Microsoft's Task Scheduler documentation:
  [logonType](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-logontype-simpletype),
  [the restart interval](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-interval-restarttype-element)
  and [schtasks end](https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-end).
- The memory of the ten live 1.1.0 servers and their `node` children, read with
  `Get-Process` at about 13:40Z for every process whose image is under
  `%LOCALAPPDATA%\BrowserAI.app\current`, starting and stopping nothing: servers 16.5
  to 25.5 MiB private, nine of them 24.6 to 25.5; each one's `node` 96.2 to 97.3 MiB.

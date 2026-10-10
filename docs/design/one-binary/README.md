<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# One windowless binary: the decided design

**Decided 2026-10-08, and being built.** The maintainer answered D1 to D5 on
2026-10-07 and D6 to D14 on 2026-10-08, together with the questions those answers
raised, and then started the build in his words: "do the things that disturb me now
then go with everything" (2026-10-08T13:46:28Z). Every decision is folded in below
where it belongs, marked **Decided** with his words and its date. The alternatives
stay beside each choice, because that is what [`docs/design`](../README.md) is for,
and what a decision replaced is kept in a dated *previously* clause. Each number
still says where it was measured or read; a claim that was read in code or in a
binary and never run says so, and so does one nobody has established.

*Changed 2026-10-08 by addition (previously the title "One windowless binary: the
plan, before anything is built", and "**A plan for the maintainer to take apart,
written 2026-10-04.** Nothing in it is built. What he has already decided is quoted
and marked as decided. Everything else is a proposal with its alternatives beside
it, and every decision it needs is numbered D1 to D14 near the end, so it can be
answered by number.")*

## How to read the decisions

- **Each decision has a label.** D1 to D14 are this plan's own. He answered D1 to
  D5 on 2026-10-07 by the numbers of the list he was given on 2026-10-04, so his
  "1" is D1, "2" is D2, "3" is D5, "4" is D3 and "5" is D4. The questions his
  answers raised were lettered as they came: E1 and E2 are his own two points of
  2026-10-07; F1 to F5, H1, H2, H1-T, P, R, S, T, U1 and U2 were put to him on
  2026-10-07 and 2026-10-08.
- **His words are quoted verbatim**, typos included, with the time of the message
  in UTC. Where a quotation leaves text out, it says "[...]".
- **Where an answer could be read two ways, the root session chose a reading and
  stated it to him**, and he did not object. Those are marked *settled by the root
  session, 2026-10-08*; a code lane that finds one wrong reports it and does not
  guess.
- **Texts still to be approved.** The hold-back text of F2, the warning of E2, the
  errors for a background that is not running, a hang, a disabled or missing task
  and a build that is not installed, and the final toast texts are drafted by the
  code lanes and collected into one file for his approval before the deploy, as the
  q371 texts were. Two texts are already his: the crash error (R) and the "already
  live" answer (F2).
- **This file holds what was chosen, and what it was chosen from.** What the build
  implements is recorded where the code lanes record it, in
  [`DECISIONS.md`](../../../DECISIONS.md), [`ARCHITECTURE.md`](../../../ARCHITECTURE.md)
  and the [kb](../../../kb/README.md).

## The decisions at a glance

| Label | Decided | What | Status |
|---|---|---|---|
| D1 | 2026-10-07, "1 b" | BrowserAI's own close; Playwright's `browser_close` denied | stands, amended by F1 |
| D2 | 2026-10-07, "2" | The agent states the settings that affect the person; a call is held back once | stands, amended by F2 |
| D3 | 2026-10-07, "4 2" | Each wait between processes from its caller's budget | stands; the values are R's table |
| D4 | 2026-10-07, "5 a" | Connected clients hold an update; kept sessions do not | replaced by H1 |
| D5 | 2026-10-07, "3 a" | One Playwright per open session, no standby | stands |
| D6 | 2026-10-08, "d6 a" | The relay answers the handshake, the tool list and `ping` from the binary | stands |
| D7 | 2026-10-08, "d7 a" | One file, `BrowserAI.exe`; registrations rewritten; project files break once | stands |
| D8 | 2026-10-08, "d8 a" | Calls held up to 150 s while there is no background | stands in part: R and S removed the starts and the retry pauses |
| D9 | 2026-10-08, his own | Only the background checks for updates, on a timer, at most every 10 minutes, the time kept on disk | stands |
| D10 | 2026-10-08, his own | A hung background is reported, nothing is restarted | stands; 150 s per R |
| D11 | 2026-10-08, "d11 a" | A build that is not installed never starts a background | stands |
| D12 | 2026-10-08, "d12 b" | A disabled task is named in an error and left disabled | stands |
| D13 | 2026-10-08, "d13 a" | A person's start goes through the task, then opens the dashboard | stands, amended by R |
| D14 | 2026-10-08, "d14 b" | The real Task Scheduler path is tested inside the suite | stands |
| E1 | 2026-10-07, his own | Teach agents that switching between visible and hidden loses nothing | stands, realised by F5 |
| E2 | 2026-10-07, his own | Visible windows close after an idle hour; agents may lengthen either default behind a warning | stands, refined by F2 and F4 |
| F1 | 2026-10-08, "f1 a" | One tool, `browserai_close`; a resume switches settings itself | stands; the refusal for `browser_close` is the generic one |
| F2 | 2026-10-08, "f2 d" | Four mandatory settings; a hold-back only on a difference from the last run; every call restarts the countdown | stands, amended twice |
| F3 | 2026-10-08, "f3 a" | A numbers index, held against the code both ways | stands; built 2026-10-09 |
| F4 | 2026-10-08, "f4 a" | A person's input in a visible window counts as activity; no title-bar countdown | stands, amended twice |
| F5 | 2026-10-08, "f5 a" | The hint goes in the `headed` description and in answers that open a window | stands |
| H1 | 2026-10-08, "h1 a" with his changes | Browsers and active relays hold an update; relays end only by a two-phase agreement | stands |
| H1-T | 2026-10-08, "H1-T a" | Terminal Claude Code sessions break after an update like Codex conversations, and are named before it | stands |
| H2 | 2026-10-08, "h2 a" | Dev builds arrive from a local update folder | stands |
| P | 2026-10-08, "p a" | One relay per client and one background; its idle load measured before release | stands, one detail changed by S |
| R | 2026-10-08, "R I like option 1", "r ok" | No automatic restarts after a crash or a hang; only the person restarts | stands |
| S | 2026-10-08, "s a" | The background is resident from sign-in to sign-out; relays never start it | stands |
| T | 2026-10-08, his own | Four toasts, none with a timeout | stands |
| U1 | 2026-10-08, his own | Every client message except `ping` is relay activity | stands |
| U2 | 2026-10-08, "u2 looks good" | "An update is installing" only in the window between the agreement and the new version | stands |
| The protocol pin | 2026-10-08, by no objection | Offer only MCP revision 2025-11-25, with a test of the new opening request | stands |

G1, the drift-check paragraph of `AGENTS.md` without its count of upstreams, was
decided the same night ("g1 2", 2026-10-08T00:16:18Z) and is a records decision,
not a part of this design.

## The direction

**His words of 2026-10-03 and 2026-10-04, which the plan started from:**

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

**His words of 2026-10-08, which reverse the first quotation's restarts**
(2026-10-08T11:54:58Z, his answer to D10):

> "d10 Relays should report a hung background. But on a forgiving timeout (part of
> the client side call limit as the background starts?). If it does not respond there
> should be an error towards the user. I want no crash monitoring or autoamtic
> restarts. If something makes it unresponsive or crash it I do not want it to get
> into a crash retry restart loop or anything. I want the user to be informed to
> check the logs and report the bug so we solve the root issue."

The first quotation's "stable code" over process isolation still stands: one
process holds every session. Its "exp backoff auto restarts and self healing" does
not: R, decided 2026-10-08, has nothing restart BrowserAI but the person at the
computer. The fourth quotation's "it keeps dying" became R's crash error and D10's
hang error, and "having trouble starting the background process" became S's
"not running" error, because no relay starts a background any more.

## Decided already on 2026-10-04, and what became of each

*Changed 2026-10-08 (previously the heading "Decided already, and kept as it is").*

- **No fallbacks**: one way of serving a client, and nothing behind it. *Stands.*
- **The tool list is generated at build time** from the pinned Playwright and checked
  against the live child. *Stands; built in step 1.*
- **The resume rules.** *Changed 2026-10-08 by F1 and F2 (previously "a resume of a
  live session is a no-op answered "the session is already live", unless a passed
  setting conflicts; a conflict is refused, naming the parameters, saying to close
  and then resume, and saying that this closes and re-opens the Playwright
  browser").* A resume that matches the session's last run goes through, answered
  "already live" on a live session; one that differs is held back once, and the
  identical second call closes and reopens the browser with the new settings itself.
- **Every clean close has the one cap of a minute**; a reopen waits for a close in
  flight, and only a destroy cuts one short. *Stands.*
- **An unknown argument gets a syntax-error refusal** carrying the tool's
  definition; an unknown tool gets the tool names with one line each. *Stands, and
  F1 confirmed that a denied tool, `browser_close` included, gets the same answer as
  an unknown one.*
- **The reason of every close is recorded and told to the agent.** *Stands.*
- **Hidden Chromium sends the headed user agent.** *Stands; built 2026-10-04.*
- **Chromium's full-page screenshots get the check for the 16,384 px repeat.**
  *Stands; built 2026-10-04.*
- **`browserai_catch_up` names every file that can hold sensitive data.** *Stands;
  built.*
- **When a client goes, its sessions keep running.** *Stands.* *Changed 2026-10-08
  by H1 in its second half (previously "an update closes those kept sessions
  cleanly, as built"):* a kept session now holds an update until its idle countdown
  runs out.
- **The branch model**: one branch, `master`, which takes every commit as soon as it
  exists. Only a release is gated, and only a release has to be stable. *Stands.*

## The short version

1. **One file, `BrowserAI.exe`, with no console and no window of its own** (D7 a).
   Today there are two programs: `BrowserAI.Server.exe`, the MCP server a client
   starts, and `BrowserAI.exe`, the coordinator behind the browser tab. They become
   one program that the clients, the Task Scheduler, the installer, Velopack's
   restart and a person all start, and its command line says which job it does.
2. **Three kinds of process while it runs.**
   - **A relay per client** (D6 a). Every Claude Code or Codex process that has
     BrowserAI loaded starts one. It answers the handshake, the tool list and `ping`
     itself, at once, from a list built into the binary, and passes every tool call
     to the background. **It never starts anything** (S a).
   - **One background process per user and install, resident from sign-in to
     sign-out** (S a). Only the Task Scheduler starts it, so no client's kill
     reaches it, and only three things ask the Task Scheduler to: the sign-in
     trigger, Velopack's restart after an install, and a person's start from the
     Start Menu. The task never runs a second copy while one runs, and the
     background's pipe admits only one. It holds every session, every browser, the
     dashboard tab, the update and its check.
   - **One Playwright `node` per open session** (D5 a), started when the session
     opens, as today. None for the tool list, none per idle client, none on
     standby.

   *Changed 2026-10-08 by S (previously "**One background process per user and
   install.** Only the per-user scheduled task starts it, so no client's kill
   reaches it. It holds every session, every browser, the browser tab and the
   update.").*
3. **No fallback, and no restarts.** When the relay finds no background, nothing
   serves the client in-process. It holds the model's call for up to 150 s while one
   appears, at sign-in or after an update, and then answers with a named error: not
   running, start it from the Start Menu, naming a disabled or a missing task when
   the Task Scheduler says so (D8, S, D12). A recorded crash is answered at once
   (R). A background that is connected but hangs is reported after 150 s with no
   answer to a liveness question, and nothing is killed (D10).
   *Changed 2026-10-08 by D8, R and S (previously "**No fallback.** When the
   background cannot be reached, nothing serves the client in-process. The relay
   holds the model's call, starts the background again with pauses that grow,
   registers the scheduled task again if it is missing, and before the client gives
   up it answers with an error that names the failure and says somebody has to read
   the log.").*
4. **One process holds every session**, as the maintainer chose: a fault in the
   background ends every browser on the machine at once. **Nothing restarts it
   automatically** (R). The crash is recorded, every call is answered at once with
   an error that tells the person at the computer to read the log, report the bug
   and start BrowserAI from the Start Menu, and only that start clears the record and
   starts a new background. The sessions come back through `browserai_resume`.
   *Changed 2026-10-08 by R (previously "The relays survive it and start a new
   background, and the sessions come back through `browserai_resume`.").*
5. **BrowserAI's settings are command-line arguments, and a client's facts travel
   over the pipe.** A process the Task Scheduler starts never sees a client's
   environment (measured), so nothing may depend on it. His own install takes its
   updates from a folder on this machine, named by an argument the install hook
   writes (H2 a).
6. **What goes:** serving a client in-process; the session host and the coordinator
   as two processes; the Playwright child that only answered the tool list; the
   front's 15 s wait for a host; the 500 ms coordinator call limit and the arm that
   went red on it six times; and, if the tab reads the background's own memory, the
   pipe every server serves and the live markers.
7. **What an idle client costs** (P a). With 1.1.0 installed, each Claude Code or
   Codex process with BrowserAI loaded holds a server of about 25 MiB private bytes
   and its own Playwright `node` of about 97 MiB, used or not: about 1.2 GiB private
   for the ten servers read on 2026-10-04, and about 3.0 GiB for the 25 the root
   session read on 2026-10-08. Under the decided design an idle client costs one
   relay: a stand-in that held the parsed tool list and waited on its input used
   3.5 MiB, 5 of 5, read by the root session on 2026-10-08. The build measures the
   real relay and a test holds it under a limit; the background's idle memory and
   CPU from sign-in are measured before release and shown to him.
   *Changed 2026-10-08 by P (previously "Under the plan an idle client costs one
   relay process, whose size nobody has measured; a whole 1.1.0 server process is
   the upper bound.").*
8. **What does not change:** the session folder is the identity; the tool surface,
   the verdicts and every refusal, apart from F1's `browserai_close` and the denied
   `browser_close`; the close cap and the ordering rule; and kept sessions.
   *Changed 2026-10-08 by F1 and H1 (previously "the tool surface, the verdicts and
   every refusal; the close cap and the ordering rule; kept sessions; and an update
   that waits until no client's BrowserAI runs.").*
9. **An update installs by itself once BrowserAI has been idle** (H1, U1, U2, D9, T).
   Three kinds of thing hold it, each with a countdown that activity restarts: hidden
   browsers (10 minutes), visible windows (1 hour, the person's own input counting),
   both changeable by the agent behind a warning, and relays (10 minutes after the
   last client message other than `ping`, fixed). A relay ends only through a
   two-phase agreement, so none ends for an update that cannot install. Four toasts,
   none with a timeout, and a dashboard page show what holds it and let a person
   install at once. *Added 2026-10-08.*
10. **Sessions** (F1, F2, E2, F4, F5): one close tool, `browserai_close`; four
    mandatory settings, and a call held back once when it differs from the session's
    last run; every call that names a live session restarts its countdown; a
    person's input in a visible window counts as activity; and the `headed`
    description and every answer that opens a window say that switching to hidden
    loses nothing. *Added 2026-10-08.*

It is built in the steps of 2026-10-08, 0 to 8 and then the deploy, each pushed to
`master` as it lands and checked by the two-shell gate after it; they are in "The
steps" below. *Changed 2026-10-08 (previously "It is built in eight steps, each
useful on its own and each checked by the two-shell gate after it lands").*

```
the client's process tree                       the user's scheduled task
-------------------------                       -------------------------
Claude Code or Codex                            Task Scheduler: sign-in, Velopack's restart,
  |                                               |  a person's start; never a second copy
  +-- BrowserAI.exe --mcp  ======= pipe ======>   +-- BrowserAI.exe --background
      the relay: one per client;                      one per user and install, resident
      ends with its client, or for an                 from sign-in to sign-out:
      update by agreement; starts nothing             sessions, the tab, the update
                                                        |
                                                        +-- job -- node (@playwright/mcp) -- browser
                                                        +-- job -- node (@playwright/mcp) -- browser
                                                            one per open session
```

*Changed 2026-10-08: the drawing of 2026-10-04 showed the same processes, with the
task started "by a relay or a person" and a background that left a minute after its
last client.*

## The weakest points, first

Each point is as written on 2026-10-04, with what the decisions made of it.

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
   **Now:** accepted. The root session listed it on 2026-10-08T00:21:41Z among the
   risks to accept unless he objected, and he did not; that list said "the relay
   restarts the background", which R then removed, so a fault now also stops
   BrowserAI until the person starts it from the Start Menu.
2. **A background that hangs goes unnoticed.** A crash closes the pipe, and every
   relay starts a new background. A hang, a deadlock or a stuck thread pool, keeps
   the pipe open, and calls wait until the client's own limit: Codex 300 s by
   default (read in its source at 0.155 and 0.160), Claude Code 30 minutes (read in
   its binary). This plan's first version detects nothing of the kind; D10.
   **Now:** decided by D10 and R. While a call waits, the relay asks the background
   a liveness question that it answers apart from its other work; with no answer in
   150 s the call gets a named error, and nothing is killed or restarted.
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
   **Now:** accepted by no objection ("BrowserAI won't run for agents started over
   SSH or CI without a signed-in desktop", the root session, 2026-10-08T00:21:41Z).
   A disabled task is named and left disabled (D12 b), a missing one is named and
   registered again only by the hooks and a person's start, and no relay runs it
   (S a). Measured in step 0 on 2026-10-08, with stand-ins: twenty run requests at
   the same moment started exactly one instance under `IgnoreNew`, 10 of 10 rounds,
   and none while one ran, every requester getting `S_OK` either way; a missing task
   answers `0x80070002` and a disabled one `0x80041326`, and neither starts anything;
   and the scheduler's own share of a start is about 4 ms to the process's creation,
   so almost all of the 514 to 674 ms above is the program starting.
4. **The two measurements this plan stands on are in scratch only.**
   `.work\onebinary-measure\` and `.work\startup-measure\` are gitignored, and
   neither is in `kb/` or `docs/evidence/`. Step 0 persists them, together with this
   plan's own readings (the Claude Code binary's text, the Codex source, the memory
   figures).
   **Now:** persisted in step 0 on 2026-10-08, into the kb with their dates,
   versions and how to re-establish them, and into
   [`2026-10-04-onebinary-measure`](../../evidence/2026-10-04-onebinary-measure/README.md)
   and [`2026-10-04-startup-measure`](../../evidence/2026-10-04-startup-measure/README.md);
   "Where the facts come from" below names each entry.
5. **The relay has to understand the protocol.** Lane c's front copies bytes and
   parses nothing. This relay reads the method and the id of every frame the client
   sends: it answers `initialize`, `ping` and `tools/list` itself, holds a
   `tools/call` while it has no background, answers held calls when the start
   fails, and replays the client's `initialize` to every new background. That is new
   code on the path every call takes, which is one of the reasons lane c turned its
   direction a down ([the coordinator-owned design](../coordinator-owned-browsers/README.md)).
   Once connected, frames still pass byte for byte.
   **Now:** stands, and the relay also keeps its activity countdown and takes part in
   the update's two-phase agreement (H1). It offers only MCP revision 2025-11-25,
   whose handshake it answers itself (the protocol pin).
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
   **Now:** D6 a, A alone.
7. **A new file name breaks every registration that names `BrowserAI.Server.exe`.**
   The update hook rewrites the user-scope entries. Nothing rewrites a client that
   kept the old command line across the update, a Claude Code project entry
   committed in a repository (`${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe`),
   or a Codex project entry, which names `BrowserAI.Server.exe` bare
   (`RegistrationClient`). D7; keeping two files with their roles moved would avoid
   it, at the price of the one file.
   **Now:** D7 a. A read-only search by the root session on 2026-10-08 found no
   `.mcp.json` under `C:\Source`, three levels deep, that names the old file; the only
   two registrations naming it are the user-scope ones, which the update hook
   rewrites.
8. **An update still waits for every client.** An idle Claude Code window keeps its
   relay running from the install root for as long as it is open, so an update
   applies only when every client with BrowserAI loaded has gone, or a person closes
   them from the tab. That answers his Q383: it is not ten minutes of inactivity
   plus a minute of closing. D4.
   **Now:** replaced by H1. A relay holds an update only while its client was active
   in the last 10 minutes, and ends for one only by agreement.
9. **The update check moves with the background.** Today every server start asks the
   feed once (Q225 b). A background that asked only when it starts, and was kept
   alive for weeks by an editor that is never closed, would never learn of an
   update. D9.
   **Now:** D9 in his version: a timer, at most once per 10 minutes, the time kept
   on disk.
10. **The design point has never run through one process.** The charter's design
    point, about 100 concurrent BrowserAI processes, runs in `SaturationTests` as 100
    server processes, 8 of them with browsers. Under lane c and under this plan the
    same load is up to 100 sessions in one process, and nobody has run that.
    **Now:** accepted by no objection, and it becomes a test ("100 sessions in one
    process have never been tried; that becomes a test", the root session,
    2026-10-08T00:21:41Z).
11. **A headed window opened by a task-started process is not measured.** The
    Task Scheduler's process holds no right to the foreground (measured three times
    on 2026-09-24, in [DECISIONS](../../../DECISIONS.md)), and every headed run in the
    reports of 2026-10-03 and 2026-10-04 was in-process or on a hidden desktop.
    Whether such a window
    shows in front, behind, or takes the focus is not known; lane c's host on
    `master` already has the same gap.
    **Now:** measured in step 0 on his own screen, with his leave: "do the things
    that disturb me now" (2026-10-08T13:46:28Z). A launcher started through a
    scheduled task with the sign-in task's principal and settings, and the same
    launcher started from a shell, each started a browser directly: Chromium's window
    came to the front and took the keyboard focus about 0.3 s after the launch, 6 of
    6, three from each starter; Firefox's came to the front 2 of 6 and opened behind
    the person's window 4 of 6, one of three from each starter. How the launcher was
    started made no difference. BrowserAI's own chain, through Node and Playwright,
    was not measured.
12. **Proxy settings now come from the user's own environment.** A session's
    Playwright inherits `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`, `ALL_PROXY` and
    `NODE_EXTRA_CA_CERTS` (`ChildEnvironment`). Today a client's environment
    provides them; under the plan the background's does, which is the environment the
    Task Scheduler builds for the user. A proxy set only in one terminal no longer
    reaches a browser download. Whether anyone relies on that is not known.
    **Now:** accepted by no objection ("Proxy settings come from your account's
    environment, not from one terminal's", the root session, 2026-10-08T00:21:41Z).
13. **The Claude Code first-turn figures may describe tool search switched off.**
    Lane stale recorded on 2026-10-03 that tool search was off in every stand-in run
    from 2026-09-23 on, and the startup measurement does not say how it ran. With
    tool search on, Claude Code defers every server's tools by default, in its own
    words quoted in point 6, and what a slow server costs the first turn then is not
    measured. The plan does not depend on it, because the relay answers at once
    either way.
    **Now:** stands as written.
14. **The size of the one binary and the memory of a relay are not measured.** After
    the 2026-09-15 split the server was 19,180,032 bytes and the app 10,382,848.
    **Now:** a relay stand-in is measured (3.5 MiB, point 7 of the short version);
    the real relay is measured by the build, and P puts the background's idle memory
    and CPU in front of him before any release.

## The processes at run time

| Process | How many | Started by | When | Ends | Holds |
|---|---|---|---|---|---|
| The relay, `BrowserAI.exe --mcp` | one per client process with BrowserAI loaded | the client | when the client starts its MCP servers: a Claude Code session, a Codex `exec` or thread | when the client ends it: Claude Code's `taskkill /T /F` or the end of its input, Codex ending its job (measured, windowless and console forms alike); or for an update, through the two-phase agreement, once its activity countdown has run out (H1) | the client's connection, its held calls, and its activity countdown: 10 minutes after the last client message other than `ping`, fixed (U1) |
| The background, `BrowserAI.exe --background` | one per user, install root and data root | the Task Scheduler only, asked by its sign-in trigger, by Velopack's restart after an install, or by a person's start (S a, D13 a); never by a relay | at sign-in, after an update, or when a person starts BrowserAI and none runs | sign-out or shutdown, which Windows announces to a hidden top-level window it keeps; an update, after the agreement; an uninstall; a crash; or a person's Start Menu start that finds it hung (R). Never the task's End command | every session with its lock, record and child; the tab's listener; the update and its check |
| Playwright, `node.exe` with `@playwright/mcp` | one per open session | the background, in a kill-on-close job of its own | `browserai_init`, or a resume that opens the session | the session's close (`browserai_close`, its idle countdown, the person closing its window, an update), release or destroy; the background's end | the session's browser, which it starts at the first browser call |

*Changed 2026-10-08 by S, R, H1 and U1 (previously, for the relay, "Ends: when the
client ends it: Claude Code's `taskkill /T /F` or the end of its input, Codex ending
its job (measured, windowless and console forms alike)" and "Holds: the client's
connection and its held calls"; for the background, "Started by: the scheduled task
only: its logon trigger, or a run asked for by a relay or a person's start", "When:
at sign-in, or when a relay or a person finds none" and "Ends: a minute after its
last relay, session and tab have gone; or an update, an uninstall, a crash, the
task's End command, sign-out"; for Playwright, "Ends: the session's close, idle
close, release or destroy; an update; the background's end").*

**The task's End command is never how BrowserAI stops its background** (settled by
the root session, 2026-10-08, from the step-0 research): Microsoft documents that
End sends `WM_CLOSE` and then calls `TerminateProcess`, and step 0 measured it, 31 of
31 with stand-ins: End and `IRunningTask::Stop` send the task process's top-level
windows two `WM_COMMAND` messages and a `WM_CLOSE`, and terminate the process about
one second after the request, exit code `0x42B`, unless it has exited by then; a
process with no top-level window gets no notice at all. That ends every browser
through the jobs without a clean close. The uninstall hook and an update stop the
background through its pipe.

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

**Decided 2026-10-08: P a, one relay per client and one background.** His question
(2026-10-08T11:54:58Z): "You state "One background per user, started only through
the per-user scheduled task.". I am concerned about the passive RAM and CPU load on
my system. What is the expected load? And what could be alternative designs?" His
answer (2026-10-08T13:02:04Z):

> "p a - but when we reach the release worthy point and all other outstanding issues
> have been resolved, so basically when we know it will be stable. I want a
> meassurement of how much idle RAM and CPU option a is consuming on the background
> from windows start to be reviewed by me."

The clause is a release task, not a condition on the choice (settled by the root
session, 2026-10-08): when the build is release-worthy, the background's idle memory
and CPU from Windows sign-in are measured and shown to him before any release. The
figures the choice was made on, read by the root session on 2026-10-08 with nothing
started or stopped: 25 BrowserAI 1.1.0 servers were running, one per open Claude Code
session with BrowserAI loaded, the oldest five days old, each about 25 MiB plus its
own Playwright at about 97 MiB, about 3.0 GiB together, and 0.1 to 1.5 s of CPU each
over up to five days. A stand-in relay used 3.5 MiB, 5 of 5; the background was
estimated at 25 to 35 MiB with no sessions, and is not measured. S a then changed one
detail of P a: the background no longer exits a minute after its last client.

| Alternative to P a, as offered on 2026-10-08 | What it costs |
|---|---|
| **a) As planned: one relay per client, plus one background** | *Chosen.* Its background was to exit a minute after its last client, session and dashboard tab; S a made it resident |
| b) A background only while there is browser work | Saves the background's 25 to 35 MiB while nobody browses; the first call after it exits pays about 0.6 s to start it again; update checks and installs happen only while it runs, or at sign-in |
| c) No relays: the background serves BrowserAI on a local web address from sign-in | Saves the relays; the background is always resident; a background that is not running shows as a failed server and no sentence reaches the model; the address needs protection from other programs; BrowserAI loses each client's project folder and process; RegisterAI registers only commands today; whether Codex supports such a server was not checked |
| d) A separate tiny relay program | Saves a few MiB per client, and costs a second file, against D7 a |
| e) Today's design | About 120 MiB per idle client |

**One background per pipe name, and the name says which.** The pipe is named for the
install root and the data root, and it is created as every pipe of ours is: its DACL
admits the current user alone, remote clients are refused, and
`FILE_FLAG_FIRST_PIPE_INSTANCE` keeps a second background off the name, so two task
runs at once leave one background and the other exits. A background that is stopping
for an update refuses new connections with a sentence saying so, and a relay that
meets it gives its client the update sentence (U2). *Changed 2026-10-08 by S
(previously "A background that has decided to stop refuses new connections with a
sentence saying so, as the coordinator does today, and the relay that meets it starts
the next one once it has gone.").*

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

| | 1.1.0, installed today | Lane c, on `master`, not released | This design |
|---|---|---|---|
| For the tool list | one per client process, started before the handshake is answered | one, in the session host | none |
| Per open session | one | one, in the host | one, in the background |
| Per idle client | one, about 97 MiB private | none | none |
| What waits for a Playwright start | every client's handshake: 0.45 s median and 1.86 s p90 over 673 real starts | a cold host's handshake | only the call that opens a session |

His words, "it starts asap, but we do not wait for it", land on the background, which
is resident from sign-in (S a), and nothing waits for it but a call that needs it.
*Changed 2026-10-08 by S (previously "the relay starts it the moment the client
starts BrowserAI, and nothing waits for it but a call that needs it").* The choices
are D5, **decided 2026-10-07: a**, his "3 a" (2026-10-07T14:53:32Z).

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
| After an update | an argument carrying the version the apply was meant to install, passed after Velopack's `--` | Velopack's restart at the end of an apply, which runs after a failed apply too | the argument; it compares the version it carries with its own, raises the installed or the failed toast, and asks the Task Scheduler for the background (T, settled by the root session) |
| Installer hooks | `--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, `--veloapp-uninstall` | Velopack | served first, by Velopack's own `Run()`, as the app serves them today; none of them starts anything |
| A person's start | no argument, or `--sessions` for the sessions page; the suite adds `--write-address` so that nothing opens | the Start Menu, `Setup.exe` after a non-silent install, `Update.exe start`, a double-click, a toast's button | no argument, or only those two |
| Report | `--report <path>` | a person, or a support request | the argument |
| Sweep | `--sweep` | one re-verification row in `kb/` | the argument |

*Changed 2026-10-08 by T and the step-0 research (added the after-update mode, "none
of them starts anything" for the hooks, and "a toast's button" where the plan had
"the update toast once built").* The spelling of each new argument is the code's,
and [`ARCHITECTURE.md`](../../../ARCHITECTURE.md) names it.

**The registration carries the argument** (D7 a, decided 2026-10-08). RegisterAI
passes everything after `--` to the client unchanged (its README), so the hooks
register `<install root>\current\BrowserAI.exe --mcp`, where today they register the
server with no argument, and RegisterAI itself needs no new release for it.
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
   name and version, and the server instructions, offering only MCP revision
   2025-11-25 (the protocol pin). Nothing it needs comes from another process.
5. **At the same moment it looks for the background** on the background's pipe. It
   never runs the scheduled task and never starts anything (S a); with no background
   it holds calls ("The first call", below). *Changed 2026-10-08 by S (previously
   "and runs the scheduled task when there is none ("The background will not start",
   below)").*
6. `tools/list` and `ping` are answered from the binary too.
7. Every message the client sends, `ping` alone excepted, restarts the relay's
   activity countdown (U1): opening the connection, `tools/list`, calls and their
   cancellations. *Added 2026-10-08.*

What the client sees is a server that answers its handshake within the relay's own
start time, which nobody has measured for this binary. A stand-in that answered at
once had its tools in every first turn, 78 of 78 runs (startup measurement). What a
person sees in `/mcp` is BrowserAI connected, even while its background is not
running. That was accepted with A: the failure shows on the first call, in words.

**The client's start limit does not depend on the background at all** (the root
session, 2026-10-08T13:05:55Z, answering his "do we need to account for the mcp init
timeout as well?"): the relay answers the handshake from its own binary before it
looks for the background, so only the relay's own start counts, and nothing slow may
run before that answer.

| Alternative to a relay that answers by itself | Why not |
|---|---|
| The relay copies bytes, as lane c's front does, and the background answers the handshake from the binary | Every handshake waits for the background to be reached, about half a second through the task when none runs; and when none can start, the client sees a server that failed to start, so no sentence can ever reach the model, because only a tool result does |
| The relay answers `initialize` and forwards `tools/list` | The first turn still waits for the background |
| The relay also answers what it can judge alone: unknown tools, argument syntax | Two doors to keep in step; the door stays in one place, the background |

**The update window of Q369.1 shrinks with it.** A server killed before it answers
`initialize` is a failed server for that conversation. Today the window is
Playwright's start, 0.45 s median and 1.86 s p90; under this design it is the relay's
own start. His answer to Q369.1 was 1, keep Q296 c, to be reviewed again with Q369.4,
and Q296 c's in-process server during an update no longer exists under this design;
U2 says what a relay started during an install answers.

### The first call

1. **Connected:** the relay forwards the frame and copies the answer back byte for
   byte.
2. **No background yet:** the call is held, in arrival order, for up to 150 s while a
   background appears, at sign-in or after an update, and released when it has
   answered the relay's first message (D8 a, narrowed by R and S).
3. **Cancelled while held:** dropped, with no answer, as MCP has it.
4. **No background:** each held call is answered with a named error. A recorded crash
   is answered at once, with R's crash text. Anything else is answered when the hold
   runs out: no background is running, the person at the computer should start
   BrowserAI from the Start Menu, and if it keeps happening, read the log and report
   the bug (S a). The error names a disabled task with how to enable it (D12 b) and a
   missing one, which the relay learns from a read-only query and never repairs
   (settled by the root session, 2026-10-08). A build that is not installed says
   that no background runs for that build (D11 a).
5. **Connected, but no answer:** while a call waits, the relay asks the background a
   liveness question that it answers apart from its other work. With no answer within
   150 s the call gets a named error that names a hang and sends the person to the
   log and the bug report. Nothing is killed (D10, R).

*Changed 2026-10-08 by D8, D10, R and S (previously "2. **Not connected yet:** the
call is held, in arrival order, and released when the background has answered the
relay's first message." and "4. **The start keeps failing:** each held call is
answered with the named error, before the client's own limit (D8).").*

Measured with a stand-in: Claude Code 2.1.288 and Codex 0.155 and 0.160 waited 1 to
90 s for a held call and delivered it, 78 of 78; no client cancelled one; Claude Code
sends a `tool_progress` heartbeat every 30 s while it waits; and a failure sentence
returned as an `isError` result reaches the model in both (startup measurement).

**Every wait fits inside both clients' limits** (the root session's table of
2026-10-08T13:05:55Z, answered "r ok"). One set for both, derived from the stricter
client, Codex, so nothing differs per client:

| Our number | Value | The client limit it must fit in | Claude Code | Codex |
|---|---|---|---|---|
| The relay answers the handshake | at once, from the binary | starting the server | 30 s | 10 s |
| A call held while the background comes up | up to 150 s | each tool call | 30 min | 300 s |
| A hung background reported | after 150 s with no answer | each tool call | 30 min | 300 s |
| A close | at most 60 s | each tool call | 30 min | 300 s |
| A page tool | at most 60 s | each tool call | 30 min | 300 s |

A person who lowers a client's own limit below ours in its settings has the client
give up first, and BrowserAI's explanation never arrives; BrowserAI cannot see those
settings. Each of these numbers is a row of the numbers index (F3). *Corrected
2026-10-09 by addition:* Codex's start limit is 30 s by default, measured on
2026-10-03 ([kb](../../../kb/mcp/protocol.md#registering-with-codex-and-what-its-startup-timeout-costs----measured-2026-09-24)),
and the table's 10 s is the figure the kb carried before that measurement; the table
is left as the root's of 2026-10-08 had it.

### The tool list

**Built from the payload at build time.** `build/upstream-snapshots.mjs` already
starts the payload's own `node` and Playwright with every capability, asks
`tools/list` and writes the answer to `upstream-snapshots/tools-list.json`: 72 tools
and 76,137 bytes on `master` on 2026-10-04, compared with the payload on every build
(`UpstreamSnapshotTests`). The design compiles that file into the binary. The payload
and the binary are packed together and replaced together in `current\`, so the list
and the Playwright it describes cannot come from two versions.

**Rewritten at run time, as today.** `SessionToolSurface.Rewrite` puts BrowserAI's
own tools first, adds `session` and `why` to every forwarded tool, drops the `deny`
rows and appends the BrowserAI notes. 72 tools reached the model on 2026-10-04, 64 of
Playwright's and 8 of ours; F1 adds `browserai_close` to ours and denies
`browser_close`, and the published tool-surface counts move with it, held by
`RecordedCountTests`. The rewrite reads `tool-verdicts.json`, which ships in the
payload today and is a startup failure when it is missing; the relay has to answer
before anything can fail that way, so the design compiles the verdicts into the
binary beside the list, and the row in `AGENTS.md` that ships them in the payload
changes with it.

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
| Ask a live child each time, as today | The wait this design exists to remove |
| A one-shot check child at the background's start, in place of the check per session | A process and a lifetime of its own, where each session's child is starting anyway; D5's direction b, not taken |

### The protocol revision: only 2025-11-25, for now

**Decided by no objection, 2026-10-08.** The relay offers only MCP revision
2025-11-25, as the installed stopgap does, so Claude Code falls back to the handshake
the relay is built on, and a test sends Claude Code's new opening request and requires
the refusal, since the suite's own client runs never see the new method. Revision
2026-07-28 is implemented in a later build, and `TODO.md` carries it (settled by the
root session, 2026-10-08). The stopgap it copies is v1.1.0 rebuilt with
`ProtocolVersion` set to "2025-11-25", which a probe on 2026-10-04 answered with
`-32022` and the supported list `[2025-11-25]` for `server/discover` at 2026-07-28,
then `initialize` at 2025-11-25 and 79 tools.

The root session stated it three times and he never answered it with a letter: "The
new version must still offer only the older protocol version (2025-11-25)"
(2026-10-07T15:01:31Z); "**Protocol:** offers only MCP 2025-11-25, like the stopgap. A
test sends Claude Code's new opening request and requires the refusal. The new
protocol version gets implemented later." (2026-10-08T00:21:41Z); and step 1 of the
build list (2026-10-08T13:34:04Z). The nearest he came (2026-10-07T14:53:32Z):

> "9 leave it for now. I want to finish the next gen browserai with you today asap.
> We will then deploy and everything is good. Going forward I do not mind running the
> dev version locally for everything. It actualyl can help dev. I do not expect the
> future to have these massive jumps. I expect super small changes and I also expect
> the MCP version mismatch we are seeing now to not ever happen again this large. So
> the entire problem will disappear I think."

| Alternative | What it costs |
|---|---|
| **Offer only 2025-11-25 in this build, 2026-07-28 later** | *Chosen.* The relay's own answers to `initialize` and `ping`, which 2026-07-28 drops, stay valid |
| Implement 2026-07-28 as part of this build (the root's recommendation of 2026-10-04, "e as part of the one-binary build") | A second protocol on the relay's path in the same build that introduces the relay |
| `null` upward, whatever the caller asks for, as `ARCHITECTURE.md` had it | Claude Code's new opening request then reaches a relay that does not implement it |

### Session open, close, idle, resume and takeover

**Lane c's behaviour as built and amended on 2026-10-04, now inside the background,
with the session decisions of 2026-10-07 and 2026-10-08** (D1, D2, E1, E2, F1, F2,
F4, F5). *Changed 2026-10-08 (previously "Lane c's behaviour as built and amended on
2026-10-04, now inside the background.").*

- **Open** (`browserai_init`): `headed`, `transcript`, `captureNetwork` and the idle
  setting are mandatory; `viewport`, `locale`, `timezone`, `ignoreHTTPSErrors` and
  `debug` stay optional, with this machine's defaults (F2 d). Nothing is held back at
  init, because there is no last run, except an idle close longer than its mode's
  default, which is held once with a strong warning that updates wait for this
  session (E2). Then the lock, the record, the session's Playwright with the list
  check, then the answer. The browser starts at the first browser call.
- **Close** (`browserai_close`, F1 a): the clean close with the minute's cap; "the
  agent's own close", or the other client's name when another client sent it,
  written to the session's log as the reason; every later call refused until resume,
  with that reason. Playwright's `browser_close` is denied, and a call to it gets the
  generic answer for a tool BrowserAI does not have, which lists every tool with one
  line each (F1, 2026-10-08T11:14:19Z). *Changed 2026-10-08 by F1 (previously
  "**Close** (`browser_close`, or `browserai_stop` if D1 takes it)").*
- **Idle**: every session has an idle countdown, 10 minutes for a hidden browser and
  1 hour for a visible one by default, set per session by the mandatory idle setting
  as a whole number of minutes or `never`, for both kinds (E2; settled by the root
  session, 2026-10-08). It restarts on every call that names the live session,
  whatever the answer, a held call, any other refusal and a resume included (F2), and
  for a visible window on the person's own keyboard or mouse input in it while it is
  in front (F4). When it runs out, the browser gets the same clean close, with the
  idle close as its reason. *Changed 2026-10-08 by E2, F2 and F4 (previously
  "**Idle**: a headless session with a browser up and no call for ten minutes gets
  the same close, reason "idle for ten minutes"; a headed one never does.").*
- **A person closes a headed window**: recorded as the person's close, and every
  later call refused until resume with that reason (decided).
- **Resume**: the session stores the settings its last run used (F2 d). A resume that
  matches them goes through; on a live session it is answered "already live" with the
  text he accepted, which names no time:

  > "The session is already live, so nothing changed: its browser, its tabs and its
  > settings are as they were. This call started its idle countdown again, as every
  > call that names the session does."

  A resume that differs is held back once ("The hold-back", next), and the identical
  call after it goes through; on a live session the browser then closes and reopens
  with the new settings and nothing is lost (F1 a). A resume waits for a close in
  flight, and reopens through the browser's own session restore. *Changed 2026-10-08
  by F1 and F2 (previously "**Resume**: the decided rules; it waits for a close in
  flight; it reopens through the browser's own session restore; with remembered
  settings, D2 decides what the model has to say first.").*
- **The hold-back** (D2 b, F2 d, E2). Every per-run setting is compared with the last
  run's: `headed`, `transcript`, `captureNetwork`, the idle setting, `viewport`,
  `locale`, `timezone`, `ignoreHTTPSErrors` and `debug`; an optional setting the call
  leaves out counts as its default, and the text says so: "(the default, because the
  call left it out)" (settled by the root session, 2026-10-08). A call that differs
  is held back once. The text names each differing setting with the last run's value
  and the asked value, says that nothing in the call is wrong, and names the three
  ways on: the last run's set goes through at once; the same changed set as the call
  held back just before, on the same connection and for the same session, goes
  through at once; any other set is held once again. Another connection meets its
  own hold-back (settled by the root session). An idle value above the mode's default
  carries the strong warning about updates when it is set at init or differs from the
  last run; one identical to the last run passes with no warning (settled by the
  root session). The text is drafted by the session lane and is his to approve.
- **The hint** (E1, F5 a): the `headed` description says "A visible window takes the
  person's screen and focus and holds updates back. Switching between visible and
  hidden keeps logins, cookies, storage, tabs and history.", and every answer that
  opens a visible window ends with "When the part that needs the person is done,
  resuming with headed: false keeps everything." Nothing goes in the server
  instructions. The description has to fit `ModelSurfaceTests`' 2,048-character
  budget.
- **Takeover**: a kept session goes to the next call that names it, from any client; a
  second client is refused, by name, while the first is still connected.
- **Destroy**: cuts a close short, then deletes.

### A client goes, and kept sessions

**In plain words.** A *client* is one Claude Code or Codex process, and its relay is
its BrowserAI. A *session* is a session folder with its Playwright and its browser.
When a client ends, because it was closed, killed, or VS Code restarted, its relay
dies with it and the background sees that client's pipe close. Each session that
client was driving is then judged once: with no browser up, it is released at once;
with a browser up, hidden or visible, it is **kept**, its idle countdown still
running from the last call or, for a visible window, the person's last input in it
(E2, F4). A kept session is a browser still running with nobody driving it, waiting
for any client to name it again, and it holds an update until its countdown runs out
(H1). *Changed 2026-10-08 by E2 and H1 (previously "with a headless browser up, it
is **kept**, its idle timer still running from the last call; with a headed browser
up, it is **kept** for as long as its window is open, looked at every 15 s. A kept
session is a browser still running with nobody driving it, waiting for any client to
name it again.").*

This is lane c's design, measured with the real programs: after each of four client
exits, the coordinator, the host and the browser were alive 6 s later, and the next
client took the session over with the page still loaded, 12 of 12 (the one-binary
measurement).

### Update

**Decided 2026-10-08: H1 a with his changes, U1, U2, D9, T, H1-T a and H2 a**, with
the step-0 research's reading of Velopack 1.2.161's restart (settled by the root
session, 2026-10-08). *Changed 2026-10-08 (previously six steps, kept below).*

1. **The check** (D9). The background alone asks the feed, on a timer, at most once
   per 10 minutes, with the time of the last check kept on disk so that a crash or a
   restart adds no checks. Relays never check. Velopack has no timer, debounce or
   stored check time of its own (read in its source at 1.2.161 in step 0). His
   install reads a folder on this machine (H2 a) and every other install GitHub; the
   source is fixed at install time by an argument the hooks write, never by an
   environment variable at run time (settled by the root session, 2026-10-08).
2. **The download and the stage**: in the background, as the coordinator does today.
   No package whose pack id is not the installed one is ever applied (the downgrade,
   below).
3. **What holds it** (H1). Three kinds of thing, each with a countdown that activity
   restarts:
   - a hidden browser: 10 minutes after the last call naming its session, unless the
     agent set another time;
   - a visible window: 1 hour after the last call naming its session or the person's
     last input in it, unless the agent set another time;
   - a relay: 10 minutes after the last message its client sent other than `ping`,
     fixed (U1). Requests and notifications alike count: `initialize`, `tools/list`,
     `tools/call` and cancellations (settled by the root session, 2026-10-08).

   When a browser's countdown ends, the browser closes, which frees its memory and
   stops it holding the update. When a relay's countdown ends, nothing happens to the
   relay; it only stops holding the update.
4. **The two-phase agreement** (H1, the root session's text of 2026-10-08T13:23:37Z,
   accepted by no objection). With no browser open and every relay's countdown run
   out, the background asks every relay "ready to end?". A relay says yes only with
   its countdown run out and no call in flight, and from then on holds what its
   client sends. Any no or silence calls the update off: every relay passes on what
   it held, and nothing has ended. A message that reaches a relay after its yes and
   before every relay has said yes is activity, and calls the update off for everyone
   (settled by the root session). Only when every relay has said yes is the end sent:
   each relay ends, then the background, then the install runs.
5. **The update sentence, in one short window** (U2). A message that reaches a relay
   after the end was sent, and every message to a relay a client starts while the
   install still runs, is answered "BrowserAI is installing an update; nothing was
   run; call again in a few seconds" (the wording is his to approve). Once the new
   version is in place, a fresh relay holds calls until the new background is up, and
   then passes them on.
6. **The toasts** (T). Four, none with a timeout:
   - **Ready**: the update installs by itself once BrowserAI has been idle; a live
     countdown and what holds it; **Install now**, which opens the dashboard's update
     page, and **Wait for inactivity**, which closes the toast.
   - **Installing**: raised by the background just before it hands over to Velopack.
     A toast belongs to Windows once it is on screen, so it stays even when
     BrowserAI's processes end or are killed.
   - **Installed**, with **Changelog**, a dashboard page that shows the changelog
     shipped inside the build and so works for dev builds too, and **Dismiss**.
   - **Failed**, with the log's path.

   "No timeout" means Windows' reminder kind, which stays on screen until the person
   acts on it and needs a button that activates in the background, so every toast
   carries one, and the installing toast gets **Dismiss** (settled by the root
   session, 2026-10-08). Measured in step 0 on his screen: a reminder stayed on
   screen for the 62 s until it was replaced; its countdown, updated in place once a
   second, moved 60 of 60 times with no new popup and no sound; "Install now" and
   "Wait for inactivity" both showed whole; and a replacement with the same tag kept
   one entry in the Notification Centre but slid a new popup in, with the sound, 4
   of 6 times, so the installing and installed toasts can sound again.
7. **A person in a hurry** (T, H1-T a). **Install now** opens the dashboard's update
   page, which explains the risks of installing now, shows who still uses BrowserAI,
   and holds the real install button; that button closes every session and
   connection cleanly and installs at once. The toast, the page and the dashboard name,
   before the update, the sessions that will need a reconnect after it: terminal
   Claude Code sessions ("/mcp, BrowserAI, Reconnect") and Codex conversations (a new
   conversation).
8. **The handover.** The background hands the package to Velopack's `Update.exe`
   silently and with a restart, the target version in the restart's arguments
   (`WaitExitThenApplyUpdates(asset, silent: true, restart: true, restartArgs)`), and
   exits. Velopack waits up to 60 s for it, ends whatever is left under the root and
   swaps `current\` (the step-0 research, read in Velopack's source at 1.2.161).
   `ApplyUpdatesAndRestart` is not used, because it is not silent: it shows
   Velopack's progress window.
9. **After the swap.** The update hook, `--veloapp-updated`, rewrites the user-scope
   registrations to `current\BrowserAI.exe --mcp` (D7 a), registers the task and
   keeps the PATH, and starts nothing: Velopack's kill pass after the hook ends
   anything the hook started from `current\`, and the hook has 15 s. Velopack's
   restart then starts the after-update mode, in the new version after a success and
   in the old one after a failure, with the same arguments either way. The mode
   compares the version it was handed with its own: the same is "installed" and
   raises toast 3, another is "failed" and raises toast 4, naming
   `%LOCALAPPDATA%\velopack\velopack_<pack id>.log`. Then it asks the Task Scheduler
   for the background, and exits (settled by the root session, 2026-10-08, from the
   step-0 research).
10. **What the clients see after it.** Claude Code's VS Code extension and
    `claude -p` start BrowserAI again at their next call, from the new version. A
    terminal Claude Code session and a Codex conversation stay broken until the person
    reconnects it or starts a new one (H1, H1-T a, measured 2026-10-03 and recorded
    in [the kb](../../../kb/mcp/protocol.md)).

**The plan's update, as written on 2026-10-04 and replaced on 2026-10-08 by H1, U2,
D9, S and the step-0 research**, kept here:

> 1. **The check**: the background asks the feed; D9 says when.
> 2. **The download and the stage**: in the background, as the coordinator does today.
> 3. **The gate**: the background holds a handle on every process whose image is under
>    the install root, relays included, and waits on them and on its pipe. When it is
>    the only one left, it closes every kept session at once, each within the minute's
>    cap, writes "an update" as the close reason, hands the package to
>    `Update.exe apply --silent --norestart --waitPid <its own pid>`, and exits.
>    Velopack then ends what is left under the root and swaps `current\`.
> 4. **A person in a hurry**: the tab's install button asks every relay to end its
>    client's connection, then closes the sessions and applies. A person's click is not
>    BrowserAI ending itself, so the rule of 2026-09-24 still holds.
> 5. **A relay that starts during the apply** finds the install's `Update.exe`
>    running, by its full image path, the detection Q286 b built. It starts no
>    background, and answers its held calls with the existing update sentence. Started
>    before the swap, it is ended by Velopack's kill pass; started after it, it starts
>    the new background once the updater has gone.
> 6. **After the swap**, the `--veloapp-updated` hook registers the new binary with both
>    clients, registers the task and keeps the PATH, as today, and starts nothing. The
>    next relay starts the new background.
>
> **Kept sessions do not hold an update, and the update closes them cleanly
> (decided). A connected client does hold it**, because its relay runs from the install
> root. D4 is what that means for when an update can apply.

**The test pack's downgrade** (risk 6 of the one-binary measurement) is closed by one
comparison: the update client applies no package whose pack id is not the installed
one. Velopack 1.2.161 picks the newest full package in the feed without looking at
its pack id, and BrowserAI allows downgrades, so a test pack on a release version
would be offered the production 1.1.0 (read in code and in the feed, not run). It
matters more under H2, because his local folder holds dev packs.

### The background dies: a recorded crash, and no restart

*Changed 2026-10-08 by R (previously the heading "The background dies: backoff, and
the named failure").*

1. **The pipe closes**, and every relay sees it at once. A call in flight is answered
   "BrowserAI's background process stopped while this call was running, so it may
   have partly happened; check before repeating it", the in-flight shape the update
   refusal already has.
2. **Every browser died with the background**, through the jobs, and the kernel
   released the sessions' locks.
3. **It is recorded as crashed**, unless it ended cleanly: for an update after the
   agreement, for an uninstall, or at a sign-out or a shutdown, which Windows
   announces to the hidden top-level window the background keeps for the purpose
   (settled by the root session, 2026-10-08, from the step-0 research, which read
   about 5 seconds for that in Microsoft's documentation).
4. **From then on every call is answered at once**, with the text he accepted on
   2026-10-08 ("r ok", 13:21:17Z):

   > "BrowserAI's background process crashed at {time} (exit code {code}). Nothing
   > was run. The person at this computer needs to read {log path}, report the bug at
   > {issues URL}, and then start BrowserAI from the Start Menu. Only that person can
   > restart it: do not start BrowserAI yourself, and do not retry this call until
   > they have."

   Nothing technically stops an agent from running the program from a shell; the text
   is the guard (the root session, 2026-10-08T13:05:55Z).
5. **Only the person's Start Menu start** clears the record and starts a new
   background through the task (D13 a, R). A later call naming an old session is then
   refused with "call browserai_resume" and the reason the new background reads in
   the session's log, where the last rows show no close: "BrowserAI's background
   process ended without closing this browser (a crash, a sign-out or a kill), so the
   newest changes may be missing".
6. **No toast for a crash in this build**: the agent's error carries it, addressed to
   the person at the computer (settled by the root session, 2026-10-08).

**The plan's steps 3 to 5, as written on 2026-10-04 and replaced on 2026-10-08 by R**,
kept here:

> 3. **The relay starts a new background** through the task: the first time at once,
>    then after pauses that double, each start waiting for the pipe to appear. The
>    numbers are D8.
> 4. **Once it is up**, the relay sends its first message again and replays the
>    client's `initialize`. A later call naming an old session is refused with "call
>    browserai_resume" and the reason the new background reads in the session's log,
>    where the last rows show no close: "BrowserAI's background process ended without
>    closing this browser (a crash, a sign-out or a kill), so the newest changes may be
>    missing".
> 5. **When it keeps failing**, each held call is answered before the client's own
>    limit, with a sentence like this draft, whose wording is his to approve as the
>    q371 texts were:
>
> > BrowserAI's background process keeps stopping. It was started {n} times in the
> > last {s} seconds and stopped each time, most recently with exit code {code}.
> > Nothing was run. A person needs to read BrowserAI's log, {log path}, to find out
> > why it keeps stopping; until then every BrowserAI call fails the same way.
>
> Its catalogue name would be `BackgroundKeepsStopping`. It has siblings, each a row of
> its own: the Task Scheduler refused to run the task, with its `HRESULT`; the task is
> disabled (D12); the background stopped answering (D10); and a build that is not
> installed found no background (D11).

**A relay that outlives its background** still sends its first message again and
replays the client's `initialize` to the next background it reaches, which is the
one the person starts.

**Where the reason can be found.** The background writes a Critical record for an
unhandled exception before it exits, as both programs do today. A crash that leaves
no record, such as a fail-fast or an access violation, still leaves its exit code with
the relay, which holds a handle on the background once connected, and in the task's
last run result. What a NativeAOT fail-fast writes, and where, has not been looked
at.

| Alternative to R's option 1 | Why not |
|---|---|
| **R's option 1: no restarts after a crash; only the person restarts** | *Chosen* |
| R's option 2: one start per call, no loop. A one-off crash heals at the next call; a background that crashes at every start shows the error on every call | An automatic restart, which his D10 answer refuses |
| R's option 3: backoff as originally planned, the relay restarting the background with doubling pauses for up to 150 s | His "exp backoff auto restarts" of 2026-10-03, which his D10 answer of 2026-10-08 reversed: "I do not want it to get into a crash retry restart loop" |
| The Task Scheduler's own restart on failure | Its shortest interval is one minute ([Microsoft's schema](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-interval-restarttype-element)), whether it counts a non-zero exit as a failure is not read, and it is a restart all the same |
| The background restarts itself | A dead process restarts nothing |
| Every relay restarts it with no pause | Twenty clients would start twenty backgrounds in one moment; the pipe keeps one and the others exit |

### The background will not start: the task missing, disabled or refused

**Decided 2026-10-08 by S a and D12 b; how a relay learns of it was settled by the
root session the same day.**

1. **No relay runs or registers the task** (S a). A relay with no background holds
   calls for up to 150 s and then answers ("The first call").
2. **Missing**: the relay's answer names it, from a read-only query of the Task
   Scheduler. The task is registered again only by the install and update hooks and
   by a person's start (`SignInTask.DefinitionFor`: the current user's SID,
   `current\BrowserAI.exe`, the same settings).
3. **Disabled**: D12 b. The answer names the entry and says how to enable it again,
   and BrowserAI leaves it disabled.
4. **Refused**, by an unavailable service, a denied access or a policy: only the
   starts that ask the Task Scheduler can meet it, which are the sign-in trigger,
   Velopack's restart and a person's start. A person's start says so, with the
   `HRESULT`; a relay finds no background and says that none runs.
5. **Started, but no pipe appears**: a person's start says so when its wait runs
   out. How long it waits is the code's to choose, and a row of the numbers index.

**The plan's five steps, as written on 2026-10-04 and replaced on 2026-10-08 by S**,
kept here:

> 1. The relay finds no pipe and asks the Task Scheduler to run the task named for its
>    pack id and install root (`SignInTask.NameFor`), with the start's reason in
>    `$(Arg0)`, the mechanism Q283 a measured.
> 2. **Missing** (`TaskChange.NotRegistered`): the relay registers it again from the
>    definition the install hook writes (`SignInTask.DefinitionFor`: the current
>    user's SID, `current\BrowserAI.exe`, the same settings), logs that it did, and
>    runs it.
> 3. **Disabled**: D12.
> 4. **Refused**, by an unavailable service, a denied access or a policy: held calls get
>    the Task Scheduler sibling of the named error at once, with the `HRESULT`, and the
>    next call asks again.
> 5. **Started, but no pipe appears**: the start waits up to its bound (D8), reads the
>    task's last run result, and counts a failed start.

**What ends the background that the design cannot prevent**: the task's End command
and `schtasks /end`, which Microsoft documents as stopping "only the instances of a
program started by a scheduled task"
([schtasks end](https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-end)),
by sending `WM_CLOSE` and then, with `AllowHardTerminate` at its default of true,
calling `TerminateProcess` (the step-0 research, from Microsoft's documentation).
Measured in step 0, 31 of 31 with stand-ins: the close request reaches only top-level
windows, the termination follows about one second after the request, and neither
touches the process's children or its job; the browsers end only because the
background's own kill-on-close jobs go with it. Sign-out, since an interactive-token task lives in the user's session; it
gives the background about 5 s, and only through a hidden top-level window, which is
far below the minute's cap of one clean close, so a sign-out ends every browser
without one. And a reboot. Each ends every browser with the background, through the
jobs. *Changed 2026-10-08 by addition (previously "how it stops one, and whether that
is a hard kill, is not measured. Sign-out, since an interactive-token task lives in
the user's session; not measurable on this machine, where signing out is not allowed
while other agents run.").*

**The task's settings, read in `SignInTask.DefinitionFor`**: instances run in
parallel; it starts and keeps running on battery; no execution time limit (`PT0S`);
normal priority; `InteractiveToken` at least privilege; no idle condition. **Under S a
the task never runs a second copy while one runs**, which the step-0 brief named as
`IgnoreNew`. *Corrected 2026-10-09 by addition: the opening sentence of this paragraph is the
definition as it was read before S a was built; since step 3 `SignInTask.DefinitionFor` writes
`IgnoreNew`, which `SignInTaskTests` holds.* What `Run` returns under it is not documented; step 0 measured it with
stand-ins: twenty requests at the same moment started one instance, 10 of 10 rounds,
and none while one ran, 10 of 10, every requester getting `S_OK` and a fresh instance
id, so only the returned running-task object tells the request that started it from
the ones ignored. The background's pipe admits only one background either way. The
scheduler also starts every task's process in one job it shares across the session,
which allows no breakaway, and it does not end a child the task's process leaves
behind when it exits: 15 of 15 such children were alive 150 s later, while the task
read Ready with no instance. That is the condition the handover to `Update.exe`
needs. Its description changes with its role: it starts BrowserAI
now, and deleting or disabling it stops BrowserAI until a person enables it or starts
BrowserAI from the Start Menu. *Changed 2026-10-08 by S (previously "Its description
changes with its role: it starts BrowserAI now, and deleting or disabling it stops
BrowserAI until a relay registers it again or a person enables it.").*

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
4. **It stays resident until sign-out** (S a), checking for updates on its timer, so
   that updates also install overnight with nothing open. *Changed 2026-10-08 by S
   (previously "It stays a minute for relays and then exits, unless D6 keeps it
   resident.").*
5. It keeps a hidden top-level window for `WM_QUERYENDSESSION` and `WM_ENDSESSION`,
   so that a sign-out or a shutdown is recorded as a clean end and not as a crash
   under R (settled by the root session, 2026-10-08, from the step-0 research: a
   message-only window receives no broadcasts, and console control events do not
   reach an interactive process at sign-out). *Added 2026-10-08.*

### A person's start, the tab and the hooks

- **A person's start** (D13 a, with R; the details settled by the root session,
  2026-10-08), with no argument, finds the background's pipe, hands over `show` with
  its existing bound, `HandOutBound` (10 s), opens the address of a new tab and
  exits. With no background it asks the Task Scheduler to run the task, registering
  it first if it is missing, waits for the pipe, and does the same. **It is also the
  only thing that restarts BrowserAI** (R). With a crash recorded, it clears the
  record and starts a background through the task. When `show` gets no answer within
  its bound, it judges the background hung, ends it by its process id after
  verifying that id's image path under the install root, clears the record and starts
  a new one through the task. That is the person's action, not an automatic one.
  The background serves the tab exactly as the coordinator does today. *Changed
  2026-10-08 by D13 and R (previously "with no background it runs the task first. The
  background serves the tab exactly as the coordinator does today. D13 is whether a
  person's start may become the background itself.").*
- **The installer's hooks** run in the one binary as they run in the app today.
  Install and update register with both clients through RegisterAI, now as
  `current\BrowserAI.exe --mcp`, the update hook rewriting the user-scope
  registrations that name the old file (D7 a); project files that name
  `BrowserAI.Server.exe` break once, and nothing lists them (settled by the root
  session, 2026-10-08). Both register the task and put `current\` on the user's PATH,
  and neither starts anything. Uninstall asks a running background through its pipe
  to close its sessions and exit, never through the task's End, then removes the
  task, the registrations and the PATH entry. Velopack gives the uninstall hook 60 s
  (kb), the same as one close's cap, so at an uninstall a slow close can be cut short
  by Velopack's kill pass. *Changed 2026-10-08 by D7 and S (previously "Install and
  update register with both clients through RegisterAI, now with `--mcp`, register
  the task, and put `current\` on the user's PATH. Uninstall asks a running
  background to close its sessions and exit, then removes the task, the registrations
  and the PATH entry.").*

## Settings travel as arguments and over the pipe

**The rule.** A BrowserAI setting is a command-line argument of the process that uses
it; a fact about a client travels in the relay's first message on the pipe; and no
running BrowserAI process reads a `BROWSERAI_` variable. The reason is measured: the
task-started host wrote to the default data root although the installer had
`BROWSERAI_ROOT` set to scratch (the one-binary measurement, risk 1). No decision of
2026-10-07 or 2026-10-08 changed this rule.

**The installer's hooks are the one exception**, because Velopack gives a hook no
channel but its environment and its own fixed arguments. The install and update hooks
read the installer's environment once and write what they find into the task's
action and into the registration's arguments, as plain arguments and not inside
`$(Arg0)`.

| Today | Read by | Under the design |
|---|---|---|
| `BROWSERAI_ROOT`, the data root | `LocalAppDataPaths.Overridden`, in both programs and in the hooks | `--data-root` on the background, from the task's action, and on the relay, from the registration's arguments; part of the background's pipe name |
| `BROWSERAI_UPDATE_FEED` | `UpdateConfiguration.Resolve` | `--update-feed` on the background, from the task's action; his install's local folder (H2 a) reaches the background this way, fixed at install time. *Corrected 2026-10-09 by addition: the code names the argument `--update-source` (`UpdateSource.Argument`), and since step 5 an update's hook, which has no installer's environment, reads it back from the definition the install saved.* |
| `VELOPACK_FIRSTRUN`, `VELOPACK_RESTART` | the app's `Main` | unchanged: Velopack's own way of telling the main program why it was started. A restart after a failed apply sets `VELOPACK_RESTART` exactly as one after a success does (the step-0 research), so the after-update mode tells the two apart by the version it carries |
| The coordinator's whole environment, copied into the host it starts | `SessionHostKeeper.EnsureStarted` | deleted with the keeper |
| What a session's Playwright inherits: `PATH`, `TEMP`, the proxy and CA names, `PWTEST_SERVER_REGISTRY` | `ChildEnvironment`, from the server's own environment | the same mechanism, now fed by the background's environment, which the Task Scheduler builds for the user (weakest point 12) |

**The relay's first message** carries its build, its process id and its client's, the
client's name and version from `initialize`, its working directory (the project the
client runs in, which the tab shows today through each server's pipe, and which the
dashboard now shows beside each relay's countdown), and the data root it was
registered for. The background answers with its own build and process id, or refuses,
with a sentence for each: a different build, a different data root, or an update
installing.

**The update check's stamp** (D9) is a file the background keeps under its data root,
read at its start and written after each check, so a crash or a restart adds no check.

**How tests keep their data apart.**

- **Each data root has its own background**, because the root is part of the pipe's
  name, so a test's scratch root never meets the real install's background.
- **A build that is not installed starts no background** (D11 a, decided
  2026-10-08). The harness starts one with `--background --data-root <scratch>`,
  outside every client's tree, and the relays it drives find it by name.
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

| # | Risk, as reported on 2026-10-04 | What the design does | What is left |
|---|---|---|---|
| 1 | Mode choice: a windowless start with no handles reads end of input and exits within 0.1 s | Every mode is an argument; no argument is a person's start; the relay also requires a pipe on stdin | A client that starts the binary without the argument gets a person's start, which the registration prevents |
| 2 | Environment: a task-started process never sees the client's environment | Settings are arguments; the hooks read the installer's environment once; a scan holds the rule | The proxy variables (weakest point 12), accepted |
| 3 | The task as a single point of failure, its failures not measured | Every failure is named and none is healed: a missing task is registered again only by the hooks and a person's start, a disabled one is left disabled, nothing serves in-process (S a, D12 b) | An unavailable service, a policy, no interactive session: named, not healed (weakest point 3) |
| 4 | What ends the task's job besides the coordinator's own exit, not measured | Read: no time limit, no stop on battery, no idle condition. Measured in step 0: End closes the process's top-level windows and terminates it about a second later, touching neither its children nor the job, so BrowserAI never uses it; and the scheduler ends no child the task's process leaves behind | Sign-out, which cannot be measured on this machine |
| 5 | Every new front's stray sweep warned about 7 or 8 browser processes it could not attribute while a session was kept | Only the background sweeps, once at its own start, before it holds a session; relays never sweep | A second install's background, the suite's, still meets the first install's browsers and leaves them alone as today, because their locks are held |
| 6 | A tagged test pack could roll itself back to the shipping release (read in code and the feed) | The update client applies no package with another pack id | Not run; a planted-red arm with a feed carrying another pack id |

*Changed 2026-10-08 by S and step 0 (previously, for risk 3, "The relay
registers a missing task again and names every failure; nothing serves in-process"
and "An unavailable service, a policy, a disabled task, no interactive session:
named, not healed (weakest point 3, D12)"; for risk 4, "Read: no time limit, no stop
on battery, no idle condition; read and not run: the End command ends it" and
"Measure the End command; sign-out cannot be measured on this machine").*

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
  asynchronous; the background keeps one such thread for the shell and for the hidden
  top-level window of the sign-out, and the relay needs none.
- `BrowserProxy`: no `tools/list` reaches a child, and the stale-list refusal of
  Q261 b moves to the relay, which is what knows whether its client has listed.
- `SessionToolSurface` and `ToolVerdicts`: the upstream half of the list and the
  verdicts come from the binary, compiled in from `upstream-snapshots/tools-list.json`
  and `tool-verdicts.json`.
- `RegistrationTarget`, `RegistrationClient` and `McpRegistrar`: the file name of D7,
  the `--mcp` argument, and a check for a windowless file.
- `SignInTask`: the action's arguments, its description, and never a second copy;
  the hooks and a person's start register the task through it, and no relay does.
  *Changed 2026-10-08 by S (previously "`SignInTask`: the action's arguments and its
  description; the relay registers the task through it.").*
- The update client: the pack-id check; D9's timer and its stamp on disk; H2's local
  folder; and the silent apply with a restart that carries the target version.
  *Changed 2026-10-08 by D9, H2 and the step-0 research (previously "The update
  client: the pack-id check, and the check's timing of D9.").*

**Deleted, or candidates for deletion, each confirmed by its step's tests:**

| Today | Lines | Why |
|---|---|---|
| In-process serving in `Program.cs`: the surface child, a client's own server, Q296 c's serving during an update | most of 1,117 | The fallback; the relay answers in its place |
| `Program.Host.cs`: the front's byte relay and its search for a host | most of 458 | Replaced by the relay |
| `Coordination/SessionHostAccess.cs` | 210 | Gone with the front; no relay starts anything |
| `Coordination/SessionHostKeeper.cs` | 486 | The background is the host |
| `Coordination/CoordinatorWake.cs` | 123 | No relay stages an update |
| `Coordination/ServerPipeClient.cs`, `ServerPipeProtocol.cs`, `ServerDescription.cs`; `Proxy/ServerPipeResponder.cs`, `ServerActivity.cs`; the pipe per server in `ServerPipe.cs`, whose loop may stay as the background pipe's | up to 1,826 | If the tab reads the background's memory, no process needs a pipe of its own |
| `Updates/LiveInstances.cs` and the live markers | 984 | Its two readers go: a server's own apply, and the tab's list of servers. The apply's gate is the countdowns and the agreement, and Velopack's own kill set does the rest |
| `Page/CensusPageSessions.cs` | 232 | The tab reads sessions and connections in memory |
| The `host` and `recheck` verbs and the 500 ms limit, in `CoordinatorClient.cs`, `CoordinatorPipe.cs`, `CoordinatorInbox.cs` and `CoordinatorProtocol.cs` | part of 889 | One pipe, the background's; `show` and `sessions` stay |
| `BrowserConfiguration.ForSurface`, and the surface child's place in `InstanceDirectory` | -- | No child answers the tool list |

*Changed 2026-10-08 by S and H1 (previously, for `SessionHostAccess.cs`, "The relay's
own start path replaces it", and for `LiveInstances.cs`, "The apply's gate is the path
scan, which is Velopack's own kill set").*

⚠️ *Added 2026-10-10 by addition:* what the build left of these rows and nothing called
was deleted that day, by the maintainer's decision *"9 a"*: `ServerPipeProtocol.cs`
whole; the JSON a description was written as, on `ServerDescription.cs`, whose record
stays as the page's model; the census half of `LiveInstances.cs`, whose reclaim of the
markers older builds left stays, run by the stray sweep; and the verbs left in
`CoordinatorInbox.cs` and `CoordinatorProtocol.cs`, which keep the wake, the hand-out
bound and the three page arguments. [DECISIONS](../../../DECISIONS.md#the-next-build-decided-2026-10-07-and-2026-10-08)
has the row.

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

**Decided 2026-10-08**: the root session's order of 2026-10-08T13:34:04Z, started by
his "go with everything" (13:46:28Z). Each step is pushed to `master` as it lands and
checked by both shells' runs; steps 6 and 7 run partly beside steps 2 to 5.

| Step | What it does | Who builds it |
|---|---|---|
| 0 | No product code. Persist the two measurements of 2026-10-04 to `kb/` and `docs/evidence/`. Read Velopack's restart and failure paths, toasts and the Task Scheduler (the step-0 research). Measure the Task Scheduler's cases and the cost of the input reads; Velopack's local source, its restart after a success and after a failed apply, and the update hook; and, on his screen with his leave, a toast with a live countdown and a visible window opened by a task-started process | the root session's measurement agents, and lane REC for the records |
| 1 | The built-in tool list, and offering only the protocol revision the stopgap uses, 2025-11-25, with a test | lane S1 |
| 2 | One file, `BrowserAI.exe`, with a mode per argument, and the registrations updated | lane ARCH |
| 3 | One background, running from sign-in, with the Task Scheduler never starting a second copy | lane ARCH |
| 4 | The relay: the handshake and the tool list at once; calls held while the background comes up; the crash error and no restarts; a hung background detected after 150 s; the activity countdown; the two-phase update agreement | lane ARCH |
| 5 | Settings as arguments, his local update folder, the 10-minute update timer, and the check that refuses another product's package | lane ARCH |
| 6 | Sessions: `browserai_close`; the mandatory settings and the hold-back; the idle countdowns and their override; a person's input in a visible window as activity; the "already live" answer and every call restarting the countdown; the hint about visible windows | lane SESS |
| 7 | Updates: the dashboard's update page and the four toasts | lane UI |
| 8 | The records: the decisions log, the architecture, the hazards, the testing guide, the kb, the README, the changelog, `TODO.md`, the numbers index, and `AGENTS.md` without the upstream count | each lane for what it builds; lane REC for this document, the measurements, the numbers index and the last pass |
| Deploy | His local update folder, and the new build installed over the stopgap | the root session |

**How a step lands.** The branch model is one branch, `master`, which takes every
commit as soon as it exists; only a release is gated and has to be stable. So each
step below is pushed as its commits are made, every behaviour change in it is planted
red first, the ordinary two-shell gate runs after the push, and a red is fixed
forward. Each step carries its own records.

**The steps as planned on 2026-10-04, replaced on 2026-10-08 by the order above**,
kept here; their step 4's "starts with backoff" and "the task registered again when
missing", and step 6's "a stop of BrowserAI's own", were overtaken by R, S and F1:

> | Step | What it does | What it is worth alone |
> |---|---|---|
> | 0 | No product code. Persist the two measurements of 2026-10-04 and this plan's readings to `kb/` and `docs/evidence/`. Measure what changes the design: the task's End command on a running background, a missing and a disabled task, the relay's memory with a stub, a headed browser started by a task-started process. Answer D1 to D14 | The facts stop living in scratch |
> | 1 | The built-in tool list, in today's two programs: the list and the verdicts compiled in, answered without starting a child, checked against every session's child; the surface child deleted; the schema rule's new words | No process starts a Playwright for the tool list any more, and every server that answers its own handshake answers it at once |
> | 2 | One file: the two projects merged into one windowless binary with a mode per argument; the name of D7; the registrations with `--mcp`; the hooks; the subsystem checks turned round | One thing to build, sign and ship; no behaviour changes yet |
> | 3 | One background: the coordinator and the session host as one process, with the tab, the update and the only stray sweep; the census and the per-server pipes go if the tab reads memory. Fronts still relay bytes and still fall back | The coordinator call limit and its red arm go; one process to watch |
> | 4 | The relay: the handshake, the list and `ping` from the binary; held calls; starts with backoff; the task registered again when missing; the named failures; the in-process fallback deleted; the real-scheduler arm in the suite | His "one system that works", and the first turn for installed clients |
> | 5 | Settings as arguments: the data root and the feed; the hooks' one read; the scan that holds it; the pack-id check | Tests that cannot reach the real install's data, and no downgrade from a test pack |
> | 6 | What D1, D2, D10 and D12 decided: a stop of BrowserAI's own, remembered settings with their acknowledgement, a hang detector, the disabled task | Each is separable and can move later |
> | 7 | The records as a whole: ARCHITECTURE, DECISIONS (the two binaries of 2026-09-15, the schema rule, and lane c's decisions 1 and 2, all reversed by this plan), HAZARDS, TESTING, the kb, README, CHANGELOG and TODO | The documents say what the product is |

## Testing

**Every behaviour change is planted red first**, and the exceptions the house rules
name stay the only ones.

- **In process.** The background's core, as lane c's `SessionHostTests` and
  `CloseOrderingTests` drive it today. The relay against a scripted background: the
  handshake and the list from the binary with no background; held calls in order; a
  cancelled held call; the 150 s hold; each named failure, the crash answered at
  once; the hang after 150 s, with nothing killed; `initialize` replayed to a new
  background; the stale-list refusal; `ping`, which restarts no countdown; the
  activity countdown; and the two-phase agreement, in which a relay with a call in
  flight says no, silence calls the update off, and a message after a relay's yes
  calls it off for everyone. The task's path through the `ILogonTasks` seam, for a
  person's start: missing, registered and run; refused; disabled; and a relay's
  read-only query that names a disabled or missing task and changes nothing.
- **The published binary.** A relay and a background the harness starts with
  `--background --data-root`, against a real Chromium: a session kept across a relay
  killed the Claude Code way and the Codex way; a background killed, every relay then
  answering with the crash text and starting nothing, a person's start bringing a new
  one, and a resume restoring the tabs. The modes: a start with no handles and no
  argument never reads end of input as a relay; `--mcp` with no pipe on stdin logs
  and exits; no mode puts a window on the screen (`WindowWatch`), and the file's
  subsystem is read out of it (`AppBinaryTests`).
- **The built-in list and the protocol pin.** `UpstreamSnapshotTests` on every build;
  an arm comparing the compiled list with a real child; a doctored payload whose
  session open is refused by name; and Claude Code's new opening request, which must
  be refused.
- **Sessions.** Each hold-back case of F2 in both directions; the idle countdown
  restarted by every call naming a live session; the "already live" text; `never`;
  the E2 warning; `browser_close` answered as a tool BrowserAI does not have.
- **Settings.** The scan for `BROWSERAI_` reads; a first message carrying another data
  root, refused; a test's background and the real install's never meeting.
- **The real scheduler, end to end** (lane c's open question 2, D14 b). Install the
  suite's pack with the real `Setup.exe`, as `RealInstallerTests` does, and drive its
  relay over stdio as a client would. The background's parent is the Task Scheduler's
  service and it runs inside the scheduler's job; a session outlives its relay's kill
  and is taken over; the task deleted is named by the next relay's answer and
  registered again by a person's start; the task disabled gives its named error; a
  background that fails at start is recorded as crashed and every call gets the crash
  text at once. That last one needs no switch for the suite: a data root
  `InstallRootScope` refuses is a real failure path. Uninstall leaves nothing, by the
  clearance snapshot. All of it under the installer lock, with the task removed by the
  arm.
- **The design point**: 100 relays and one background with 100 sessions.
- **What a number costs.** The relay's memory, measured by the build and held under a
  limit by a test (P); the input check's cost, measured and recorded, where any
  measurable lag removes the feature (F4); and the numbers index held against the code
  in both directions, with a scan that refuses a literal duration outside the named
  classes (F3).
- **Before release, not in the suite**: the background's idle memory and CPU from
  Windows sign-in, measured once the build is release-worthy and shown to him (P).
- **What no suite here can reach**, said and not implied: sign-out, the Task
  Scheduler service stopped, a group policy, a reboot, a user with no interactive
  session.

*Changed 2026-10-08 by R, S, F1, F2, F3, F4, H1, P and the protocol pin (previously,
among the published-binary arms, "a background killed, its relays starting another,
and a resume restoring the tabs"; among the real-scheduler arms, "the task deleted is
registered again by the next relay" and "a background made to fail at start gives the
named error before the budget runs out"; and in process, "the budget" and "The task's
path through the `ILogonTasks` seam: missing, registered and run; refused;
disabled.").*

## The decisions this plan needed

*Changed 2026-10-08 (previously the heading "The decisions this plan needs").* Each
has its primer, the directions with what they cost, and the recommendation, as put to
him on 2026-10-04, and then what he decided. D1 to D4 were his open questions; D5 and
D6 his questions 13 and 12; D7 to D14 this plan's own.

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

**Decided 2026-10-07: b, then F1 a on 2026-10-08.** His words (2026-10-07T14:53:32Z):

> "1 b - but think through if browserai_stop and browserai_close could then not just
> become a single thing."

They became one: F1 a names the one tool `browserai_close`, has the resume switch
settings itself after one hold-back, and gives `browser_close` the generic answer for
a tool BrowserAI does not have ("F1" below). `tool-verdicts.json` gains a `deny` row
for `browser_close`. It contradicts the older row of `DECISIONS.md`, "Instance
teardown" ("**There is no close tool, and that is the decision.**"), which the session
lane records as replaced.

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

**Decided 2026-10-07: b, with every setting the agent states, then narrowed by F2 d
on 2026-10-08.** His words (2026-10-07T14:53:32Z):

> "2  What if we make all the init and resume parameters mandetory and then go
> withpattern b. But do make sure to communicate clearly to the llm that the first
> call did not work but that the second call will work. I want to prevent the calling
> agent from thinking the parameters are wrong on the first refusal and it thinking it
> should work differently. It should be very clear that. Maybe something like an
> intent of "if this is expected, make this call again and it will work". Not that
> wording, but that intent. What do you think?"

His answer is the word the recommendation said it needed: the no-confirmation rule
gives way here. `ModelSurfaceTests.NoAuthoredToolAsksTheCallerToConfirmAnything`
scans for confirmation-flag names and no flag is added, so it stays green; the rule's
prose has to carry the exception. F2 d then made four settings mandatory and not all
of them, and holds a call back only when it differs from the session's last run ("F2"
below).

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

**Decided 2026-10-07: a.** He answered option 2 of the list of 2026-10-04, which is
this table's a (2026-10-07T14:53:32Z):

> "4 2 - Maybe we should start tracking all magical numbers used in this project in an
> index of sorts so that we can at a later date re-check the data by using the
> provenance of these records to re-determine if the number is still accurate?"

The second half became F3, the numbers index. The values that survive are the root
session's table of 2026-10-08, answered "r ok" ("The first call", above). In the table
here the liveness row is 150 s, not 10 s, by D10 and R, and the "15 s for a host" of
the 2026-10-04 list went with lane c's front; the person's start (`show`, 10 s) and
the uninstall hook are unchanged.

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

**Decided 2026-10-07: a, with a dashboard list; replaced on 2026-10-08 by H1.** His
words (2026-10-07T14:53:32Z):

> "5 a - Let's add a feature to the dashboard we will be making on the
> coordinator/configurator interface that we list all the sessions that are blocking
> an update together with a countdown timer until they are automatically closed.
> Clearly signalling to the user that interactive windows should be closed manually
> before the update can proceed."

The root session then showed the clash (2026-10-08T00:21:41Z): "Your 5 a says every
connected client's relay holds an update, and the update closes leftover sessions
cleanly, visible ones included. Your dashboard countdowns and the 1-hour visible close
assume something else: that sessions hold the update until they close, and that
visible windows wait for you." H1 replaced both halves of a: active relays and open
browsers hold the update, and an update no longer closes kept sessions. His dashboard
list survives inside H1's dashboard.

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

**Decided 2026-10-07: a.** His words (2026-10-07T14:53:32Z): "3 a". It answers his
question of 2026-10-04, "Or should we have just 1 per system on standby?", with no
standby.

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

**Decided 2026-10-08: a.** His words (2026-10-08T11:54:58Z): "d6 a". It narrows his
"12 a+b and maybe c" of 2026-10-04 to A alone. Direction d was not chosen for the
first turn, but S a made the background resident anyway, for the update's sake; D6
does not depend on it.

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

**Decided 2026-10-08: a, without the tab's list.** His words (2026-10-08T11:54:58Z):
"d7 a". He answered the short form he was given on 2026-10-08T00:21:41Z: "One
`BrowserAI.exe`; the update hook rewrites your user-level registrations, and project
files naming the old file break once (recommended)." That short form is the whole
decision, so nothing lists the project entries that name the old file (settled by the
root session, 2026-10-08). The update hook rewrites the user-scope registrations to
`current\BrowserAI.exe --mcp`; this repository's own `.mcp.json` changes in the build;
and a read-only search of `C:\Source`, three levels deep, found no project file naming
`BrowserAI.Server.exe` on 2026-10-08. It reverses `DECISIONS.md`'s "How many
executables ship" ("TWO since 2026-09-15").

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

**Decided 2026-10-08: a, then narrowed by R and S the same day.** His words
(2026-10-08T11:54:58Z): "d8 a - What do you mean with "Retry pauses double from
0.5 s"?" The root session's answer (12:00:28Z):

> "**What the retry pauses meant:** when a relay can't reach the background, it asks
> the Task Scheduler to start one. If that one doesn't come up, or dies, the relay
> waits 0.5 s and tries again, then 1 s, 2 s, 4 s and so on, until 150 s have passed
> (half of Codex's 300 s limit per call). Then it answers with the named error."

R's option 1 then made it one start per held call, never a second, and S a took the
starts away altogether: "**Relays never start anything.** With no background, they
hold calls for up to 150 s while one appears (sign-in, an update), then answer with an
error: not running, start it from the Start Menu, and if it keeps happening, read the
log and report the bug." (13:05:55Z, accepted "s a" 13:21:17Z). What stands of a is
the hold of up to 150 s, half of Codex's 300 s. The retry pauses are gone, and with
them the "about nine starts". Under S the relay holds 150 s for every absence and
names a disabled or missing task in its answer from a read-only query (settled by
the root session, 2026-10-08); a recorded crash is answered at once (R).

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

**Decided 2026-10-08, in his own words, closest to d with a stored stamp.**
(2026-10-08T11:54:58Z):

> "d9 Managed from the background only on a timer and at max once per 10 min. even
> across crashes and restarts. But does velopack not already handle this? Relays should
> not be concerned with update checking."

and (2026-10-08T13:02:04Z): "d9 Does velopack not do the debounce for us?" The root
session's answer (13:05:55Z): "No. Velopack has no rate limit and keeps no stored time
for checks, at least none that I could find in its documentation; every check reads
the update source. With a resident background, the 10-minute timer is the debounce,
and the time kept on disk covers restarts." The step-0 research confirmed it in
Velopack's source at 1.2.161: no timer, no debounce and no stored time; the only state
a check writes is `packages\.betaId`. It reverses Q225 b, and adopts in another form
the stored stamp Q225 b declined.

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

**Decided 2026-10-08, in his own words: b's report, a forgiving timeout, and no
restarts.** His words are under "The direction" above. The root session had
recommended b with 10 s on 2026-10-08T00:21:41Z; his answer kept b's report and its
"kills nothing", asked for a forgiving timeout, and forbade restarts. R made the
timeout 150 s, the same half of Codex's limit as the hold, so the model hears
BrowserAI's explanation before the client gives up. c, and "c or d once something has
been seen to hang", are ruled out by "I want no crash monitoring or autoamtic
restarts".

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

**Decided 2026-10-08: a, after he asked for the question again.** His words
(2026-10-08T11:54:58Z): "d11 Explain the question better and ask me again." The
question again (12:00:28Z):

> "**D11. A build that isn't installed.** After H2, your own BrowserAI is always an
> installed build, and it starts its background through the installer's entry. A
> build that isn't installed has no entry. That's a fresh build in a checkout, which
> is what the test suite runs, or a debugging session. When its relay finds no
> background:
> - a) It never starts one. It answers with an error saying no background is running
>   for that build. The test suite, or a developer, starts one with a command.
> - b) It starts the background as its own child. That background dies with the
>   client, sessions included.
> - c) It registers an entry of its own for its folder, runs it, and removes it
>   later. Entries are left behind when a checkout is deleted.
> - d) It serves the client itself, in its own process. That's the fallback you
>   rejected."

His answer (13:02:04Z): "d11 a". The question's first sentence, that an installed
build "starts its background through the installer's entry", was overtaken by S: no
relay of any build starts a background now.

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

**Decided 2026-10-08: b, after he asked what the schedule was.** His words
(2026-10-08T11:54:58Z): "d12 What schedule? The schedule I want to review? Let's get
back to this question after we answer that." The root session's answer (12:00:28Z):

> "**There's no schedule in it.** A "scheduled task" is Windows' name for any entry in
> its Task Scheduler. BrowserAI registers one per user at install, called "BrowserAI
> sign-in". It starts BrowserAI's background in two cases: when you sign in to
> Windows, and when BrowserAI itself asks for a run. Nothing runs on a clock."

His answer (13:02:04Z): "d12 b". Under S no relay runs the task, so a relay learns of
a disabled task by a read-only query and names it after its hold (settled by the root
session, 2026-10-08).

### D13. A person's start: through the task, or the background itself

**Primer.** A person's start is the Start Menu, the start `Setup.exe` makes after an
install, or a click on the update toast once it is built; each opens a tab. Today,
when no coordinator runs, the app a person started becomes the coordinator itself.

| | Direction | Cost |
|---|---|---|
| **a** | **Through the task, then hand over `show`** | One start path; about half a second more for a first tab |
| b | A person's start becomes the background itself when none runs, as the app becomes the coordinator today | A faster first tab, and a second start path, with the background's parent Explorer or a terminal |

**Recommendation: a.**

**Decided 2026-10-08: a, after he asked what "through the task" meant; amended by R
the same day.** His words (2026-10-08T11:54:58Z): "d13 What do you mean "through the
task?" Explain the question better." The question again (12:00:28Z):

> "**D13. Starting BrowserAI yourself**, from the Start Menu or from the start the
> installer makes after installing. This opens the dashboard in your browser. The
> dashboard is served by the background, so if none is running, the program you
> started has to get one going:
> - a) It asks the Task Scheduler to run BrowserAI's entry, the way relays do. It
>   waits for the background to answer, tells it to open the dashboard, and exits.
>   There's one way for the background to come into existence.
> - b) The program you started becomes the background itself. The first dashboard
>   opens about half a second sooner. But there are then two ways the background can
>   come into existence, and one started from a terminal ends with that terminal."

His answer (13:02:04Z): "d13 a". "The way relays do" was overtaken by S: relays no
longer ask. R adds that this start is the only thing that ends a hung background,
clears the crash record and starts a new one. The root session settled how it judges
a hang: `show` with no answer within `HandOutBound` (10 s), then the background ended
by its process id after verifying that id's image path under the install root
("A person's start, the tab and the hooks", above).

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

**Decided 2026-10-08: b.** His words (2026-10-08T11:54:58Z): "d14 b". Two of its arms
changed with S and R ("Testing", above): a deleted task is named by the next relay
and registered again by a person's start, and a background that fails at start is a
recorded crash.

## The decisions taken after the plan

His answers to D1 to D14 raised questions the plan did not have. Each is here with
the options he was given, his words and what it decided. P is under "The processes
at run time" and the protocol pin under "The protocol revision", above.

### E1 and F5. Teaching agents that a window can be switched off

**His point** (E1, 2026-10-07T14:53:32Z):

> "- I want to clearly communicated to the agent that browserai can end a session and
> resume it again without dataloss with different parameters. Basically I want to hint
> towards the flow of having only a small section of the browseruse be interactive
> with the user (say the login) and teach the model that it can then immediately after
> make it a headless session. I see in the first version of browserai that models tend
> to keep the interactive mode. How could we hint towards that without steering the
> (for us completely unknown) use cases to much?"

**F5, the options** (the root session, 2026-10-07T15:01:31Z):

| | Option | Cost |
|---|---|---|
| **a** | **Say it where the choice is made.** The `headed` description states the cost and the fact: "A visible window takes the person's screen and focus and holds updates back. Switching between visible and hidden keeps logins, cookies, storage, tabs and history." Every answer that opens a visible window ends with: "When the part that needs the person is done, resuming with headed: false keeps everything." | States facts and leaves the timing to the agent; "the part that needs the person" names no use case |
| b | a, plus one sentence in the server instructions | Reaches every model, and spends space every model has to read |
| c | A reminder in results after some number of calls in a visible session | Noisier, and has to guess when |
| d | No text; rely on the 1-hour close | Nothing teaches it |

**Decided 2026-10-08: a.** His words (2026-10-08T00:16:18Z): "f5 a". The same message
ends with a line "f5" alone, a leftover with no further meaning (settled by the root
session, 2026-10-08). Our client-behaviour runs can then measure whether models
switch.

### E2 and F4. An idle hour for visible windows, and the person's own input

**His point** (E2, 2026-10-07T14:53:32Z):

> "- Right now browsers automatically close after 10 min. of inactivity and
> interactive windows never do. What if we change the never to 1 hour and then allow
> the calling agent to change this default behaviour with a parameter? This would
> allow critical interactive user processes to remain indefinite but have a sensical
> timeout default for interactive sessions that can be restarted. Basically unlocking
> automatic updates overnight. Changing the default timeout to something longer than
> 10 min or 1 hour for headless and headed should come with a warning for the agent.
> Maybe using the same pattern of refusal with clear instructions that re-calling with
> the same parameters will work if the agent is really sure. This warning should be
> strong about blockign updates."

The root session agreed (2026-10-07T15:01:31Z) and found one hole: "idle" means no
tool call, and someone working in the window, during a long sign-in, a two-factor wait
or while reading, makes none, so the window could close under their hands at the
hour.

**F4, the options** (2026-10-07T15:01:31Z):

| | Option | Cost |
|---|---|---|
| **a** | **The person's keyboard and mouse input in that window, while it is in front, counts as activity**, with a check every few seconds, only for visible sessions, plus the dashboard countdown | A timer in the background while a visible window is open |
| b | Calls only, but a Windows notification 5 minutes before a visible window closes | A notification to dismiss, and a window that still closes under a person who missed it |
| c | Calls only, with no warning | The hole stays |

**Decided 2026-10-08: a, amended twice.** His words (2026-10-08T00:16:18Z):

> "f4 a - but only if this is easy. If it requires much complexity or code drop this
> feature and report back to me. Also, can we steer the title bar? And if so, can we
> put a 1 min. resolution (and within the 1 min a 1 sec. resolution) countdown in
> there? Again: if this requires much complexity or code drop this feature and report
> back to me."

The root session's report (00:21:41Z): the input check is kept, "It's two standard
Windows calls, with no hooks into your input"; the **title-bar countdown is dropped**,
because Chromium and Firefox both draw their tabs where the title bar would be, so a
window title shows only in the taskbar and in Alt+Tab, and the only way to change what
the window shows is the page's title, which changes the page the agent reads. The
countdown stays on the dashboard. Then (2026-10-08T11:14:19Z): "f4 Make sure the
keyboard and mouse input check does not lag the system." and (11:56:09Z, mid-turn):

> "f4 Make sure these reads happen at the same pace regardless of how many visible
> windows there are. No 100x fold checks just because there are 100 windows open."

**So:** no input hooks; one timer for the whole background, ticking every few seconds
and only while a visible window is open; each tick makes one read of the window in
front and one of the time of the last input, however many visible windows are open,
and matches that window to the session that owns it. The build measures what the
check costs and records it, and any measurable lag removes the feature. The interval
is a row of the numbers index. Step 0 measured the reads with stand-ins on
2026-10-08: one tick of the three calls took a median of 50 ns in a tight loop and 2.2
to 2.4 microseconds at one tick every 2 s, and over five minutes at that pace the calls
cost about 0.7 ms of CPU, on top of about 2.5 ms for waking every 2 s at all. The step-0 research adds how to read them: a
coalescable timer, the idle time as a 32-bit difference against `GetTickCount` that
survives the 49.7-day wrap, and a missing foreground window or a zero thread id taken
as no activity.

**The kinds, in his words** (2026-10-08T13:21:17Z): "From the three kinds both the
hidden and visible browser sessions have timeouts that can be overriden (with a
warning) by the agent. The timeout from the relay is fixed." **The idle setting**
(settled by the root session, 2026-10-08) is mandatory on init and resume, a whole
number of minutes or `never`, and both modes may take `never`; the description names
the defaults, 10 minutes hidden and 60 minutes visible; a value above the mode's
default is held back once with the strong warning that updates wait for this
session; a long value identical to the last run passes with no warning, and one that
differs from it, or is set at init, is held. It reverses `DECISIONS.md`'s "A headed
session is never closed for idleness" (Q326 a, 2026-10-03), with its switching-off of
the one-hour upstream idle timeout for headed launches.

### F1. One close tool

**The options** (2026-10-07T15:01:31Z), after his "1 b - but think through if
browserai_stop and browserai_close could then not just become a single thing.":
underneath there is one operation, the browser ends and the session stays, whether
the agent asks, the idle timer runs out, the person closes the window or an update
comes; and with the hold-back of D2, a resume can do the close and reopen itself.

| | Option | Cost |
|---|---|---|
| **a** | **One tool, `browserai_close`, and a resume switches settings itself after the hold-back** | "Closed" is already the word in every recorded reason, every refusal and the dashboard |
| b | The same, named `browserai_stop` | A second word for the one operation |
| c | One tool, keeping the earlier close-then-resume rule | Two calls to switch settings, and the refusal in between |

**Decided 2026-10-08: a, with the generic refusal for `browser_close`.** His words
(2026-10-08T00:16:18Z): "f1 a - what do you mean "hidden browser_close's refusal"?",
then (11:14:19Z):

> "f1 - I remember that for the other tools on the deny list we just send the generic
> "unknown command" response and that we do not have tailored answers. If I remember
> correctly do not make an exception. Otherwise proceed."

He remembered correctly: since 2026-10-04 a call to a denied tool gets the answer a
tool BrowserAI does not have gets, which lists every tool with one line each, and
`browserai_close` simply appears in that list. The root's "the hidden
`browser_close`'s refusal will point to it" (2026-10-07T15:01:31Z) is withdrawn. It
reverses the half of `DECISIONS.md`'s "A resume of a live session changes nothing when
no setting conflicts, and is refused by name when one does" (2026-10-03) that refuses
a conflict, and keeps "A call naming a tool BrowserAI does not have is told so
plainly, and so is a call naming a denied one" (2026-10-04, 1 a).

### F2. The mandatory settings and the hold-back

**The options** (2026-10-07T15:01:31Z), after his D2 answer. `init` and `resume` take
`headed`, `transcript`, `captureNetwork`, `viewport`, `locale`, `timezone`,
`ignoreHTTPSErrors` and `debug`, and none is remembered between runs. Making the ones
that affect the person mandatory is right; for `locale`, `timezone` and `viewport` it
backfires, because the defaults are this machine's own, a model would invent values
like `en-US` that change what sites serve, and `viewport` sets what every screenshot
costs.

| | Option | Cost |
|---|---|---|
| a | Mandatory: `headed`, `transcript`, `captureNetwork` and the new idle setting; the rest optional with this machine's defaults. Hold back once on anything the person notices: a visible window, the transcript, network capture, an idle close later than the default, or switching a live session | A hold-back on every such call, the same value as last time included |
| b | Everything mandatory, with `default` accepted as a value for the machine-dependent ones | A model writes `default` it does not understand |
| c | Everything mandatory, with no `default` | A model invents a locale and a viewport |
| **d** | **Mandatory as in a, but hold back only when a value differs from the session's last run** | The session stores its last run's settings |

**Decided 2026-10-08: d, amended twice.** His words (2026-10-08T00:16:18Z):

> "f2 d - so we need to store this in the session. Also, explain in the hold text
> what parameter is different, what the previous values was and what the newly
> requested value was. The agent can then do 1 of 3 things: request the
> original/lastrun parameter set with an instant ok, request the same changed
> parameter set as the last call with an instant ok, or request a new parameter set
> with a similar single refuse message again."

Then (11:14:19Z):

> "f2 resuming an already live session with the same settings should reset the idle
> timer for that session and respond back to the calling agent that the session is
> "already live". Just accepting it and not returning the call "already live" signal
> prevents us from teaching the calling agent how the timeout works. Also, any type of
> call, even if refused once because the settings are different should reset the
> countdown timer on live sessions."

and (11:58:28Z, mid-turn):

> "f2 Either remove the specific amount of timeout in these responses. Or make sure
> these values are correct based on the possible overrided values set on init or
> resume. But given the possible values and different wording I'd opt for not
> communicating the specific values. Just generic "the timeout has been reset" or
> something alike. Think of your own text."

**So:** the rule in "Session open, close, idle, resume and takeover", above. Every call
that names a live session restarts its countdown, whatever the answer, which reverses
the code's rule that only a forwarded browser call restarts it ("The one timer, reset
here and nowhere else", in `BrowserProxy`). The "already live" text names no time; the
draft of 11:15:29Z that named "10 minutes" and "1 hour" is replaced. The root session
settled four readings on 2026-10-08: every per-run setting is compared, not only the
four mandatory ones; an optional setting left out counts as its default; "the same
changed set as the last call" means the call held back just before on the same
connection, for the same session; and a long idle value that repeats the last run
passes with no warning.

The draft text of the hold-back that carried his intent, written before d
(2026-10-07T15:01:31Z), for the case of a window: "Not done yet, and nothing in this
call is wrong. BrowserAI holds back the first call that opens a window on the user's
screen, so that it is a choice and not an accident. If that is what you want, send
exactly the same call again and it will go through. Nothing else needs to change."
The text for d is drafted by the session lane and is his to approve.

### F3. The numbers index

**The options** (2026-10-07T15:01:31Z), after his "Maybe we should start tracking all
magical numbers". The product rests on many chosen or measured numbers: the 10-minute
idle close, the 60-second close limit, Chromium's 30-second cookie save, the 15 s and
10 s waits, the 16,384 px screenshot limit, the 2,048-character description limit.
Some have their measurements written up with dates, and nothing lists the numbers
themselves or makes a new one say where it came from.

| | Option | Cost |
|---|---|---|
| **a** | **One index with a row per number: the value, what it governs, where it came from (measured, derived, chosen or set by an upstream), the evidence, the date and versions, and how to re-check it. A test holds the index and the code against each other in both directions; every tunable number lives in a few named classes, and a scan refuses a hard-coded duration anywhere else** | A test, a scan and a sweep of the stray literals |
| b | Each constant names its source in the code itself, with no separate index | Nothing lists them, and nothing checks a new one |
| c | Only the numbers tied to upstream versions get re-check rows | The chosen numbers stay unexplained |
| d | A written rule, with nothing enforcing it | A habit |

**Decided 2026-10-08: a.** His words (2026-10-08T00:16:18Z): "f3 a", and in R
(13:02:04Z): "Add them to our numbers list we discussed earlier." It starts with this
build; the client limits go in it, and so does every number the decisions named: the
10-minute hidden idle, the 1-hour visible idle, the fixed 10-minute relay countdown,
the update check at most every 10 minutes, the input check's interval, the 150 s
hold, the 150 s hang, the 60 s close, the 60 s page tool, and the clients' limits of
30 s and 10 s to start and 30 minutes and 300 s per call. `AGENTS.md` cannot take a
new top-level file, so the index is a kb article; lane REC builds it once the code
lanes have landed their numbers.

**Built 2026-10-09**, *added by addition*: [`kb/numbers.md`](../../../kb/numbers.md),
the six named classes it names, and `NumbersIndexTests`, which holds the index and the
code in both directions, refuses a literal duration outside the named classes, and
holds R's question as a test: every wait a tool call can meet ends inside the stricter
client's limit. ⚠️ **One figure above was already out of date when it was written**:
Codex's default limit on a server's start is 30 s and not 10 s, measured on 2026-10-03
at 0.155 and 0.160
([kb](../../../kb/mcp/protocol.md#registering-with-codex-and-what-its-startup-timeout-costs----measured-2026-09-24)),
a day before the timeout table in "The first call" quoted the figure the kb carried
until then. The index carries 30 s. No number of BrowserAI's is derived from it,
because the relay answers the handshake from the binary.

### H1. What holds an update

**The first options** (2026-10-08T00:21:41Z). An update can install only when nothing
runs from the install folder. Under his 5 a, one open Claude Code window holds every
update indefinitely, so the countdowns never unlock overnight updates, and with dev
builds on his machine updates become a daily thing.

| | Option | Cost |
|---|---|---|
| a | The update waits for every open session to close, and for every Codex client to go. Claude Code relays step aside and reconnect at the next call; the dashboard shows each countdown, marks visible windows "close this to let the update proceed", lists Codex clients, and has an "install now" button | A Claude Code agent may get one "retry" refusal after an update |
| b | As 5 a: every client holds the update, and the update closes leftover sessions, visible ones included | One open Claude Code window holds every update |
| c | As a, but the update closes hidden sessions at once | Applies sooner; interrupts an agent mid-task, which has to resume |
| d | Relays run from outside the install folder and survive updates | A project of its own |

His words (11:54:58Z): "h1 Explain the issue and explain the options both in more
details." **The same options in detail** (12:00:28Z) explained how Velopack installs,
what runs from the folder under the plan, and what each client does when its server
goes away: Claude Code starts a new one at its next call, and Codex does not, so that
conversation answers "Transport closed" until a new one starts. Option a was then
"Sessions and Codex conversations hold the update; Claude Code relays step aside"; b
"Your 5 a as it stands"; c "As a, but hidden sessions don't hold the update"; d
"Relays copied outside the program folder".

**Decided 2026-10-08: a, with his changes.** His words (13:02:04Z):

> "h1 a - But I want some changes. I want relay agents to also track activity and
> have a similar 10 min. idle count. So any relay that has been active in the last 10
> min. also holds up the update. These timers I then also want to clearly see in the
> gui of the coordinator. So the coordinator can show what is holding up the update
> clearly split by browsers, relays, etc... Also another change: I want the same
> timeouts for codex logins. So no special treatment for codex. If that means codex
> sessions get broken then so be it. But as a sideproject make sure to research if
> there a new or future plans on how to have codex restart the relay nicely. Add this
> to the long running todo. Also, now that we have a full h1 plan. Explain to me how
> that integrates nicely with the toast message (which I like very much!). I'd like
> for that toast message to clearly show what is happening, communicate that it will
> resolve itself in x time and allow the user to not wait for it and force the update
> instantly.
> Tell me if this plan is unclear anywhere."

and his adjustments (13:21:17Z):

> "h1: Some adjustments or checks:
> - From the three kinds both the hidden and visible browser sessions have timeouts
> that can be overriden (with a warning) by the agent. The timeout from the relay is
> fixed.
> - When the countdown on the hidden and visible browsers end the browsers close to
> save resources and unlock any updates. But when the countdown on the relay expires
> the relay is NOT terminated. It is kept alive so both claude code and codex do NOT
> have to restart the mcp server. The reasoning is that the relay is super lightweight
> and that there is a delay cost (and for codex a broken state cost) that we do not
> want to pay. The only thing an expired timeout on a relay drives is whether an
> update may in fact ask the relay to terminate nicely if and only if the update logic
> in the coordinator determines that there is nothing holding back the update. So that
> 10 min. inactivity countdown (reset on any mcp activity in the relay) is there only
> to prevent updates. And no relay is terminated unless the update will actually pass.
> I do not want relays killed only for an update that cannot pass because another
> thing is holding back the update."

**So:** the update flow above, steps 3 and 4. The two-phase agreement is the root
session's answer to "no relay is terminated unless the update will actually pass"
(13:23:37Z), and he raised nothing against it. The dashboard shows what holds the
update split into hidden browsers, visible windows and relays, each with its
countdown; relays are labelled with their client and project folder, and visible
windows are marked "close this to let the update proceed". The Codex side project came
back on 2026-10-08: no released or in-progress Codex restarts a stdio server whose
process has ended, and the watch item is in `TODO.md` (`890f499b`). H1 reverses
`DECISIONS.md`'s "Nothing exits itself to let an update in" (2026-09-24), and the
root session records the new entry beside the old one.

### H1-T. Terminal Claude Code sessions after an update

**The question** (2026-10-08T13:34:04Z). H1 assumed every Claude Code session gets
BrowserAI back at its next call after an update ends its relay. Measured on
2026-10-03: the VS Code extension and `claude -p` reconnect by themselves; Claude Code
in a terminal shows BrowserAI as failed and refuses the next call itself, 9 of 9,
until the person runs `/mcp`, chooses BrowserAI and clicks Reconnect. So an update
leaves every terminal session idle for 10 minutes or more broken, as a Codex
conversation is.

| | Option | Cost |
|---|---|---|
| **a** | **Accept it, as for Codex, and show who will need a reconnect**: the toast and the dashboard name the terminal sessions and Codex conversations that will need one ("2 terminal sessions will need /mcp Reconnect") | Both Install now and waiting are informed choices; the reconnect is a person's |
| b | Clients that cannot reconnect by themselves hold the update until they close | Needs a reliable way to tell a terminal session from the VS Code extension, which was not checked to exist |
| c | Run relays from a copy outside the program folder, so an update never ends them | Nothing breaks; an old relay talks to a new background with its old tool list; a project of its own |

**Decided 2026-10-08: a.** His words (13:41:42Z): "H1-T a", followed by "can you do all
the measurements right now before the go? and if so, how long will that take?"

**Measured the same day, for naming those sessions** (41 runs at Claude Code 2.1.294 and
2.1.292 and codex-cli 0.161.0 and 0.159.0-alpha.12.1, in
[the kb](../../../kb/mcp/protocol.md)): a stdio server tells the three Claude Code modes
and the two Codex ones apart only by `clientInfo` together with its parent's command
line, 41 of 41; the environment alone gets 15 of 41, and misreads a terminal session
that inherited `CLAUDE_CODE_ENTRYPOINT` as the VS Code extension, the direction that
tells a person no reconnect is needed where one is. Codex 0.161.0's terminal UI also
keeps its servers alive about 30 s after `/quit`, in a shared background server.
⚠️ Which test the relay applies is not decided; it reads undocumented client flags,
and the choice is his.

### H2. How dev builds reach his machine

**The options** (2026-10-08T00:21:41Z). Today an installed build checks GitHub for
updates, and a pre-release build never checks at all.

| | Option | Cost |
|---|---|---|
| **a** | **A local update source: the install points its update source at a folder on this machine; each build is packed into it and arrives through the normal update path, applying when H1 allows; pre-release builds may check a local source** | The pack-id check matters more, since the folder holds dev packs |
| b | Run each build's installer by hand | Immediate; competes each time with whatever runs from the install folder |
| c | Publish dev builds as GitHub pre-releases on a separate channel | Public, and slower |

**Decided 2026-10-08: a.** His words (11:54:58Z): "h2 a". His install takes updates
from that folder only, and other installs keep GitHub; the source is fixed at install
time by an argument the hooks write, never by an environment variable at run time
(settled by the root session, 2026-10-08). The first deploy replaces the stopgap,
which is v1.1.0 rebuilt with the protocol pin. Velopack reads a local folder through
its `SimpleFileSource`, which has no pre-release switch: the highest full version above
the installed one is offered (the step-0 research, Velopack 1.2.161). It changes, for
his install, `DECISIONS.md`'s "Where the feed and the package are hosted" (Q237 f) and
Q225 b's guard that a pre-release build never checks.

### R. After a crash or a hang: no automatic restarts

**The options** (2026-10-08T12:00:28Z), after his D8 question and his D10 answer, which
contradicted his direction of 2026-10-03:

| | Option | Cost |
|---|---|---|
| **1** | **No restarts after a crash.** A background that ends without a clean exit is recorded as crashed, and every call is then answered at once with an error that sends the person to the log, the bug report and the Start Menu. A hang gets the same kind of error after 150 s with no answer to a liveness question, and nothing is killed. The person's Start Menu start ends a hung background, clears the record and starts a new one | One crash stops BrowserAI until the person acts |
| 2 | One start per call, no loop: every call that finds no background starts one, once | A one-off crash heals at the next call; an automatic restart all the same |
| 3 | Backoff as originally planned | The restart loop his D10 answer refuses |

**Decided 2026-10-08: option 1, with his addition.** His words (13:02:04Z):

> "R I like option 1 and the call response. Maybe add that only the user may restart
> from the start menu and not the agent? About the timeouts: Do all these timeouts
> work within both the claude or codex timeouts? And if they differ do the timeouts
> differ? Add them to our numbers list we discussed earlier. Also, do we need to
> account for the mcp init timeout as well? Or is that dependent on the coordinator?"

and (13:21:17Z): "r ok". **So:** the crash text with his addition, in "The background
dies", above; the timeout table, in "The first call", above, one set for both clients;
and the client's start limit, which depends on the relay's own start alone. Option 1's
line "A background that ended cleanly (idle, or for an update) is started by the next
call that needs it. That's a start, not a restart." was then replaced by S. The root
session settled two details on 2026-10-08: how the Start Menu start judges a hang
(`show` unanswered within `HandOutBound`, 10 s), and that a crash or a hang is told to
the person through the agent's error, with no toast in this build.

### S. The background is resident

**His question** (2026-10-08T13:02:04Z):

> "Also, if we start the background coordinator after a clean stop (so a start not a
> restart); how do we prevent it from starting multiple times in a multi relay race
> situation? And do we then even need it running on windows startup? And if we need
> the resident windows startup instance for updates and the like, why do we even need
> a clean startup? That scenario should never happen then does it not?"

**The options** (13:05:55Z):

| | Option | Cost |
|---|---|---|
| **a** | **Resident: it starts at sign-in and runs until sign-out.** After an update the update step asks the Task Scheduler for the new one, and the Start Menu starts it if it is not running. Relays never start anything; with no background they hold calls for up to 150 s while one appears, then answer with an error. The three starters all go through the Task Scheduler, told never to run a second copy, and the pipe admits only one background. Update checks run all day, so updates also install overnight | The background's idle memory all day, which his release measurement (P) judges |
| b | On demand, as planned: it exits a minute after its last client, session and tab; relays start it through the Task Scheduler, a race of relays handled by the same setting and the pipe; sign-in starts it briefly to install a waiting update | Start code in every relay, with its races; no update checks while nothing is connected |

**Decided 2026-10-08: a.** His words (13:21:17Z): "s a". It changes one detail of P a,
the background no longer leaving a minute after its last client. It reverses two rows
of `DECISIONS.md`: "One hidden coordinator, started only when it is needed, owns the
apply and the conversation with the person" (2026-09-24, "**It is not resident.**")
and "The hidden process lives while a tab is connected, and one minute after the last
one closes" (Q336 a, 2026-10-03). The "update step" became Velopack's restart into the
after-update mode, because anything the update hook starts from `current\` is ended by
Velopack's kill pass (the step-0 research).

### T. The toasts

**The options** (2026-10-08T13:05:55Z). The toast chosen on 2026-09-24 had "Review N
sessions" and "Ask me later", a dropdown to ask again in 10 minutes, in 6 hours, after
the next reboot or not for this version, and the X meaning 6 hours. With an update
that installs by itself, asking later has no job left.

| | Option | Cost |
|---|---|---|
| a | **Install now** and **Details**, with no dropdown | The root's recommendation |
| b | The same, plus a "Not for this version" choice | One more button in the one row |
| c | The 2026-09-24 toast with Install now added as a third button | Windows lays buttons in one row and shortens labels to fit, so they may be cut off |

**Decided 2026-10-08, in his own words, which chose none of the three.** In his H1
answer (13:02:04Z): "Explain to me how that integrates nicely with the toast message
(which I like very much!). I'd like for that toast message to clearly show what is
happening, communicate that it will resolve itself in x time and allow the user to not
wait for it and force the update instantly." Then (13:21:17Z):

> "t I like the live countdown of the toast. I'd opt for two buttons. Install now and
> wait for inactivity. Where the install now button takes you to the browser interface
> gui of the coordinator where it can better explain what the risks of forcing the
> update now are together with an overview of who is still using it. Also, make sure
> there is also a toast that when the update starts in the form of "installing update
> x now". How would that work if the process gets killed? And/or a toast saying
> browserai version x has been installed with a nice button to the changelog (and a
> dismiss)."

and (13:31:14Z): "u2 looks good. Make sure the toasts have no timeout." **So:** the
four toasts of the update flow, above, as the root session built them on his answer
(13:23:37Z), with the after-update mode raising the installed and failed toasts. The
whole set is approved: after it he said "do the things that disturb me now then go
with everything" (settled by the root session, 2026-10-08). "No timeout" is the
reminder kind, on screen until the person acts, which needs a button that activates in
the background, so the installing toast gets **Dismiss** (settled by the root
session). His 13:02 wish to "force the update instantly" from the toast is replaced by
his own 13:21 answer: **Install now** goes to the dashboard's update page first. It
replaces `DECISIONS.md`'s "A blocked update asks once, from the tray, and the X means
the dropdown" (2026-09-24), and changes "The toast's Review opens a new tab the way a
Start Menu click does" (Q339, 2026-10-01). The renderings of 2026-09-24 it replaces
are in [`toast-2026-09-24`](../toast-2026-09-24/README.md). With dev builds going in
several times a day he sees the sequence for each one; toasts 2 and 3 can be dropped
for dev builds later if that gets noisy.

### U1. What counts as relay activity

**The root session's first proposal** (2026-10-08T13:05:55Z): a tool call through the
relay, refused ones included; opening the connection, tool-list requests and pings
would not count, since clients send those on their own. His words (13:21:17Z):

> "u1 Anythign that signals the model is busy. So opening the conenction and the
> tool-list requests also indicate work that is busy or is about to happen. Pings I am
> unsure about. Does an idle session that has long since finished work keep sending
> pings?"

The root session's answer (13:23:37Z): everything the client sends counts, except
pings; BrowserAI's own process log for 2026-10-05 to 2026-10-08 shows 58 new
connections and not one ping, so clients do not ping an idle server today, and pings
stay out so that a client that one day pings idle servers to keep them alive cannot
hold every update forever.

**Decided 2026-10-08.** His words (13:31:14Z):

> "u1 everything except pings counts as activity. So opening the connection,
> tool-list requests, calls and their cancellations all count as activity and reset
> the countdown."

It narrows his 13:21 "reset on any mcp activity in the relay" by the one exception.
Requests and notifications alike count (settled by the root session, 2026-10-08).

### U2. "An update is installing", in one window

**The root session's first U2** (2026-10-08T13:05:55Z): a call that arrives during the
swap reconnects, finds no background, and its relay holds the call until the new
background is up, which replaces today's "an update is installing" refusal. His words
(13:21:17Z): "u2 unsure. How could the "an update is installing" fit in everything
above?" **The second U2** (13:23:37Z): the sentence stays for one short window, a
message that arrives after the relays have said yes and before the new version is in
place, and for a relay a client starts while the install still runs; once the new
version is in place, a fresh relay holds calls until the new background is up. So a
model sees that sentence at most once per update, and only if it calls during those
seconds.

**Decided 2026-10-08: the second U2.** His words (13:31:14Z): "u2 looks good. Make
sure the toasts have no timeout." A message between a relay's own yes and the last
relay's yes is activity, and calls the update off (settled by the root session,
2026-10-08). It narrows `DECISIONS.md`'s "A call an update meets is refused with a
sentence, never dropped" (Q286 b, 2026-09-24) to that window.

## Where a later answer changed an earlier one

Each line: the earlier position, the later one, and which stands.

| Subject | Earlier | Later | Stands |
|---|---|---|---|
| Restarts | His "exp backoff auto restarts and self healing" (2026-10-03, sent 2026-10-04T00:47:33Z) | His D10 "I want no crash monitoring or autoamtic restarts" (2026-10-08T11:54:58Z), then R | No automatic restarts; only the person restarts |
| D8 | Hold 150 s, starting the background again at doubling pauses (11:54:58Z) | R option 1, one start per held call (13:02:04Z); S a, relays never start anything (13:21:17Z) | Hold up to 150 s, no starts, no pauses |
| D10's timeout | The root's 10 s | His "forgiving timeout"; R's 150 s | 150 s |
| The background's lifetime | `DECISIONS.md` 2026-09-24, "It is not resident"; Q336 a, a tab plus a minute; the plan, a minute after the last relay, session and tab; P a | S a | Resident from sign-in to sign-out |
| What holds an update | D4 a and his 5 a (2026-10-07): every connected client; kept sessions closed by the update | H1 a with his changes, the two-phase agreement, U1 | Browsers and active relays, by countdown; relays end only by agreement |
| Codex and the update | The root's H1 a: Codex conversations hold the update until they end | His "no special treatment for codex" | No special treatment |
| How relays end for an update | The root's H1 a: every Claude Code relay ends, one with a call in flight finishing it first | His "no relay is terminated unless the update will actually pass" | The two-phase agreement |
| Relay activity | The root: tool calls only (13:05:55Z) | His "everything except pings" (13:31:14Z) | Everything except `ping` |
| "An update is installing" | The root's first U2: dropped | The second U2 | Kept for one short window |
| Forcing the update from the toast | His 13:02 "force the update instantly" | His 13:21 T: Install now goes to the dashboard first | T |
| The toast's buttons | 2026-09-24: Review N sessions, Ask me later, the dropdown; the root's T a: Install now and Details | His T: Install now and Wait for inactivity, then no timeout | His T, with no timeout |
| Stop or close | Answer 1 b, `browserai_stop` | F1 a, one `browserai_close`, the resume switching settings | F1 a |
| The refusal for `browser_close` | The root: it "will point to" `browserai_close` | His "do not make an exception" | The generic refusal |
| Mandatory parameters | His answer 2: all of them | F2 d: four | F2 d |
| A live resume with other settings | `DECISIONS.md` 2026-10-03: refused, close and then resume | F1 a and F2 d: held once, then the identical call closes and reopens | F1 and F2 |
| What restarts a session's countdown | The code and `DECISIONS.md`: only a forwarded browser call | F2: every call naming a live session; F4: the person's input in a visible window | F2 and F4 |
| The "already live" text | The root's draft naming 10 minutes or 1 hour | His "name no time" | The text with no time |
| Headed idle | `DECISIONS.md` Q326 a (2026-10-03): never closed for idleness | E2: 1 hour, changeable | E2 |
| The title-bar countdown | His F4 ask | Dropped as costly, which his "drop this feature and report back" allowed | Dropped |
| Update checks | Q225 b: once per server start, never a schedule, no stored time; the plan's D9 a: once per relay connection | His D9: the background, a timer, at most every 10 minutes, the time on disk | His D9 |
| Update exits | `DECISIONS.md` 2026-09-24: "Nothing exits itself to let an update in" | H1 | H1, with no relay ending for an update that cannot pass |
| The protocol revision | `ARCHITECTURE.md`'s "`null` upward"; the root's 2026-10-04 "e as part of the one-binary build" | The pin to 2025-11-25 | The pin, by no objection |

## Where the facts come from

**In the tree:** [the coordinator-owned design](../coordinator-owned-browsers/README.md),
[ARCHITECTURE](../../../ARCHITECTURE.md), [DECISIONS](../../../DECISIONS.md),
[TESTING](../../../TESTING.md), [HAZARDS](../../../HAZARDS.md) and the code on
`master` at `5bf02f48`.

**The decisions of 2026-10-07 and 2026-10-08** were read from the root session's
transcript of those days and its running record, `.work\STATE.md`, gathered into
`.work\onebinary\DECISIONS-2026-10-08.md` (35 labelled decisions and 18 readings left
open) and settled in `.work\onebinary\RESOLUTIONS-2026-10-08.md`, all under
`C:\Source\SixFive7\BrowserAI\`. Those files are scratch; what they hold that a
decision rests on is quoted above, his words verbatim. *Added 2026-10-08.*

**The step-0 research of 2026-10-08**, read-only: Velopack 1.2.161's restart, its
failure paths, its hooks and its local source, read in its source at tag `1.2.161`
(commit `92d6a1c91716729d449034df5c50307dcce39493`) and its documentation; toasts that
update in place or stay on screen, from Microsoft's documentation and the Windows SDK
10.0.26100.0 headers; the Task Scheduler's instance policy, End, the interactive token
and the foreground; and what the input reads cost. Its facts that the build rests on
go to the [kb](../../../kb/README.md) with their sources. *Added 2026-10-08.*

**Persisted in step 0, 2026-10-08.** Each measurement the design stands on is a kb
entry with its date, its versions and how to re-establish it, and its raw data is a
batch in `docs/evidence`:

| Measurement | The kb entry | The batch |
|---|---|---|
| A windowless server under both clients, 2026-10-04 | [protocol](../../../kb/mcp/protocol.md), "A windowless server under both clients"; [processes](../../../kb/windows/processes.md), "A windowless program started with no standard handles reads end of input at once" | [`2026-10-04-onebinary-measure`](../../evidence/2026-10-04-onebinary-measure/README.md) |
| The real programs through the real Task Scheduler, 2026-10-04 | [processes](../../../kb/windows/processes.md), "The real programs through the real Task Scheduler" | the same |
| A test pack offered the production release, read 2026-10-04 | [Velopack](../../../kb/packaging/velopack.md), "A test pack would be offered the production release" | the same |
| The first turn, a held call, Codex's `required`, 2026-10-04 | [protocol](../../../kb/mcp/protocol.md), "When each client's first turn goes out, and a call held behind it" | [`2026-10-04-startup-measure`](../../evidence/2026-10-04-startup-measure/README.md) |
| What a server's start costs, and 673 real starts, 2026-10-04 | [provisioning and timings](../../../kb/playwright/provisioning-and-timings.md), "What a server's start costs before its first answer" | the same |
| The live tool list against the snapshot, 2026-10-04 | [tools and artifacts](../../../kb/playwright/tools-and-artifacts.md), "The upstream snapshot is the list a live child answers, 70 of 70" | the same |
| A pipe call idle and under load, 2026-10-04 | [processes](../../../kb/windows/processes.md), "A pipe call between two of BrowserAI's processes, idle and under a full suite's load" | the same |
| Velopack's local source, restart, failed apply and hooks, 2026-10-08 | [Velopack](../../../kb/packaging/velopack.md), "A local folder, a silent apply with a restart, a failed apply, and the update hook" | [`2026-10-08-step0`](../../evidence/2026-10-08-step0/README.md) |
| The toasts, 2026-10-08 | [notifications](../../../kb/windows/notifications.md), "A toast that counts down in place, stays until acted on, and is replaced" | the same |
| The Task Scheduler, End, a child left behind, and sign-out read, 2026-10-08 | [processes](../../../kb/windows/processes.md), "What the Task Scheduler does with a second run, a missing or disabled task, End, and a child left behind" | the same |
| The input reads, 2026-10-08 | [processes](../../../kb/windows/processes.md), "Reading the window in front and the time of the last input" | the same |
| A browser window started by a task-started process, 2026-10-08 | [processes](../../../kb/windows/processes.md), under "A process a task starts may not take the foreground" | the same |
| What a stdio server can see of its client, 2026-10-08 | [protocol](../../../kb/mcp/protocol.md), "What a stdio server can see of the client that started it" | [`2026-10-08-client-id`](../../evidence/2026-10-08-client-id/README.md) |

The relay stand-in's 3.5 MiB (P) was read by the root session on 2026-10-08 and is not
in the kb; the build measures the real relay. *Added 2026-10-08.*

**In scratch on 2026-10-04, and persisted in step 0** (weakest point 4), under
`C:\Source\SixFive7\BrowserAI\.work\`:

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

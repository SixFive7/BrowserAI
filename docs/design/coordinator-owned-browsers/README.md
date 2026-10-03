<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Coordinator-owned browsers -- option c, built (Q366 b, Q364 c)

**Decided 2026-10-03 by the maintainer, in his words verbatim:** *"Q366 b - lets go
with a fully build option c. If the server crashes and the coordinator loses the
pipe, keep the browser around with the already running activity timeout timer
active. This allows restarting vscode, the claude code plugin or soemthing without
losing the state. And the timer logic for cleaning up inactive sessions already
exsits. Of course if a close or destroy is called explicitly then do clean it up."*
and *"Q364 Lets add option c"*, with his standing words from P7: *"it all needs to
be done in a super safe way so we don't permanently leak stuff."*

This directory is the design that was built for it, the directions it was chosen
from, what was measured before any code, and the plan the build followed. The
decision of record is the row in [`DECISIONS.md`](../../../DECISIONS.md#the-zoom-out-of-2026-09-25-and-what-followed-it);
where this file and that row disagree, the row is right.

## Why

Measured 2026-10-03, before this design
([kb: the client at exit](../../../kb/mcp/protocol.md#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03),
[kb: a hard kill's window](../../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03)):

- Claude Code starts `taskkill /T /F` on the server before it closes its stdin;
  the kill lands 0.53 to 1.15 s after the end of input.
- A Claude Code that is itself killed, as when VS Code closes, takes its server
  within 19 ms through the job it was started in.
- Codex ends its per-server job at once, in every exit, with no breakaway.
- So the browser, which lives in a job of the server's own under the server's
  tree, dies with the server, and a hard kill loses what Chromium had not yet
  committed: a cookie needs about 30 s on disk.
- A keeper started by the server survived Claude Code's kills and died under
  Codex, because the job allows no breakaway.

The coordinator is started by Windows' Task Scheduler through the per-user logon
task, so it is outside every client's tree and every client's job. The one thing
not yet measured was whether that holds for a process the coordinator starts. It
was measured first, below, and it holds.

## The measurement taken before any code

**A process started through the Task Scheduler keeps everything in its own job
through every way a client ends its server: 21 of 21 runs, seven exits, three
each.** Evidence:
[`docs/evidence/2026-10-03-coordinator-survival`](../../evidence/2026-10-03-coordinator-survival/README.md);
rig: [`docs/probes/2026-10-03-coordinator-survival`](../../probes/2026-10-03-coordinator-survival/README.md);
[kb](../../../kb/windows/processes.md#a-process-the-task-scheduler-starts-keeps-its-jobs-processes-through-every-clients-exit----measured-2026-10-03).

The rig is lane c's copy of the client-exit rig with three stand-ins added: a
*coordinator*, started by a scratch per-user task carrying the sign-in task's own
settings and run on demand the way a blocked server runs it; a *host*, which that
coordinator starts inside a kill-on-close job of its own; and a *browser*, which
the host starts in a further kill-on-close job, nested in the first. Each run's
dummy MCP server, started by the real client, asks the host for a browser, and
also starts the stand-in it always started, in a job of its own as today's
BrowserAI does.

| Exit | Client | Server | Its own stand-in | The host's browser after the settle | 3 s later | Released |
|---|---|---|---|---|---|---|
| `b1` stdin closed, so `taskkill /T /F` | Claude Code 2.1.288 | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |
| `b2` the client terminated | Claude Code 2.1.288 | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |
| `e5` the VS Code host exits | Claude Code 2.1.287, the extension's binary | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |
| `d1` `codex exec` | 0.155.0-alpha.9.2 | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |
| `d2` app-server, stdin closed | 0.155.0-alpha.9.2 | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |
| `n1` `codex exec` | 0.160.0 | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |
| `n2` app-server, stdin closed | 0.160.0 | killed 3/3 | dead 3/3 | **alive 3/3** | **alive 3/3** | died 3/3 |

**And the containment half, which is the one that keeps it safe:** with the
batch done, the host was asked for two browsers more and the coordinator stand-in
was then terminated by its recorded pid and creation time. Both browsers exited,
code 0, seen through handles opened before the kill: the watcher read both exits
690 ms after it started and about when the kill landed, on a clock of its own, so
the batch places them at the kill and no closer. The host answered nothing
afterwards.

**One finding nobody asked for.** The task-started process is in a job: the Task
Scheduler put it in one it shares with the other processes this user's tasks
started at sign-in, limit flags `0x0`, so no kill on close and no breakaway rule.
Nesting a kill-on-close job of our own under it worked, three levels deep.

## The shape that was built

```
Task Scheduler ── BrowserAI.exe (the coordinator, hidden, one per install root)
                    └─ job J_c, KILL_ON_JOB_CLOSE, held by the coordinator alone
                         └─ BrowserAI.Server.exe --host (the session host)
                              ├─ node.exe @playwright/mcp  [the tool list's own child]
                              └─ node.exe @playwright/mcp  [one per session] ── browser
                                   (each in its own kill-on-close job, nested in J_c)

MCP client ──stdio── BrowserAI.Server.exe (the front: relays bytes) ──pipe── the host
```

**The front.** The `BrowserAI.Server.exe` a client starts. When it is an install,
it finds the session host, starting it through the coordinator when there is
none, and from then on copies bytes: stdin to the host's pipe and the pipe to
stdout, parsing nothing. It still joins the live census, still serves its own
pipe and still runs the update lane and the stray sweep. When there is no install,
or the host cannot be reached inside its bound, it serves in-process exactly as
before; that is also how the suite's published slice runs unless a test asks for a
host.

**The session host.** The same binary, started by the coordinator with `--host`
inside the coordinator's job. It is today's server minus the stdio front: one
child for the tool list, every session's lock, record, child and idle timer, the
verdicts, provisioning. It serves one MCP conversation per pipe connection, all
of them over one set of sessions.

**The coordinator.** Starts the host when a front asks for it, holds it in a
kill-on-close job of its own, stays alive while the host runs, and before it
applies an update has the host close every browser cleanly.

## The directions it was chosen from

| | Direction | What it costs | Why not |
|---|---|---|---|
| **a** | **A byte keeper in the coordinator.** The coordinator launches `node` itself and relays the child's stdio to the server, which keeps the lock, the record and its MCP client | The MCP handshake lives in the child: a second server cannot `initialize` it again, so the coordinator has to cache and replay the handshake, rewrite request ids per attachment and drop answers to a server that died. The lock has to move to the coordinator anyway, or another server takes the profile while the browser is still on it, and the idle close, its row, the registry reap and the restore all move with it into a binary that has no SQLite and no session code | Most of the server moves into the coordinator either way, and what is left is a protocol-aware relay that is new code on the hot path |
| **b** | **A session host in the coordinator's job (built).** The server binary in a second mode holds every session; a client's server relays its whole MCP conversation | One more process per user. All sessions share one process, so a crash of it ends all of them at once | -- |
| **c** | **The coordinator links the session machinery.** `BrowserAI.exe` holds the sessions itself | The cut of 2026-09-15 kept SQLite, the MCP SDK and the session code out of the app, and the browser tab is adding Kestrel to it; a window process and a session process become one, so a fault in either ends both | It reverses a recorded architecture decision for no gain over b |
| **d** | **A host per client.** The coordinator starts one server per front and keeps it when the front dies | A relaunched client has no identity linking it to the server it had, so a session held by an orphaned server is reached from a new one only by handing the lock, the child's pipes and the job across processes | The handover is the hard part and b has none |
| **e** | **What was built on the night: e1 and e2.** Chromium flushes `localStorage` within a second and a shutdown closes every browser first | No new process | It cannot help when there is no time at all, which is every Codex exit and a killed Claude Code; it stays, and still covers the fallback |

**The relay of 2026-09-24 is a different thing.** That one sat between a client
and a server it re-spawned across an update, and was dropped for its update lane,
its at-least-once resend and its two processes per session
([DECISIONS](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client)).
The front here re-spawns nothing and resends nothing: when the host is gone the
front ends, the client sees its server end, and Claude Code starts one again on
its next call.

## The relay protocol

- **One pipe connection per front, carrying the client's MCP stream byte for
  byte.** Newline-delimited JSON-RPC, as on stdio. No envelope, no session id in a
  frame: every call already names its `session`, so the conversation is already
  multiplexed at the protocol the host speaks.
- **Backpressure is the pipe's.** Each connection is its own instance with its own
  buffers, so a client that stops reading stalls its own conversation and nobody
  else's.
- **Sessions are not bounded by a pipe-instance count.** A connection carries any
  number of sessions, and connections are one per running client. The host's
  pipe is created with `PIPE_UNLIMITED_INSTANCES`, which Microsoft documents as
  limited only by system resources, and which held 2,000 connected instances of
  one name on this machine on 2026-10-03, with a cap of 254 as the positive
  control
  ([kb](../../../kb/windows/processes.md#a-pipe-created-with-pipe_unlimited_instances-holds-more-than-255-callers----measured-2026-10-03)).
  Each connection costs the host one instance and no thread while it is idle, so
  what bounds the relay is the requests in flight and not the sessions held.
- **The name is the install root's**, `\\.\pipe\BrowserAI-Host-` and the root's
  key, the key the census gate, the coordinator's pipe and the logon task end in.
  The DACL admits the current user alone and remote clients are refused, as on
  every pipe of ours.
- **The host reads with asynchronous I/O**, because a few hundred idle
  connections must not hold a few hundred threads.

## Who owns a session, and when

**The host holds `browserai.lock` for a session's whole life**, so the guard's
six properties are unchanged and a session's owner record names the host. Which
client drives a session is an attachment inside the host, with three states:

| A call naming a session the host holds | What happens |
|---|---|
| attached to this connection | It goes ahead |
| attached to another connection that is still open | Refused, naming that client, the way a lock another process holds is refused |
| detached, because its client went | **This connection takes it over and the call goes ahead.** No restore and no resume: the browser never closed |

`browserai_resume` takes over a detached session the same way and then applies
its existing rules (Q324). A session the host does not hold answers as it always
did, and the refusal-until-resume flow applies only after a real close.

**When a connection ends, each session attached to it is detached and judged
once:**

| The session | What the host does | What ends it later |
|---|---|---|
| closed by the idle timer or by `browser_close` | releases it at once | -- |
| its child has gone, or no browser is up | releases it at once: there is no state to keep | -- |
| headless, browser up | keeps it, the idle timer still running from the last call | the idle close; then it is released |
| headed, browser up | keeps it, and looks every 15 s whether the browser is still up | the person closing the window; the coordinator ending |

**Every session the host keeps has a holder and a way to end.** The lock names the
host. A headless session's end is its idle timer; an idle close that finds nothing
to close releases a detached session too, which covers a browser that died after
its client went. A headed session has no timer by Q326 a, and its recorded reason
is the window watch, which is a check and not a close.

**`browser_close` and `browserai_destroy` still clean up at once**, attached or
not: the close is the caller's own (P3 b), and destroy tears the session down and
deletes it.

## Headed sessions whose client went

Q326 a keeps a headed session out of the idle timer, because a window holds what a
person was doing. A detached headed session therefore stays until the person closes
its window, when the next look finds no browser up and the host releases it, or
until the coordinator ends. **Decided here for review**; the alternatives were
giving it the idle timer once detached, which closes a window a person may be using,
and closing it at detach, which loses what the restart was meant to keep.

## The idle close and the shutdown close

**The idle close is unchanged and runs in the host.** It ends the whole child
(P4 b) and, from Q367 a, sends the browser its own `browser_close` first with a
30 s cap, built the same day in `LiveSession`, which the host runs as every server
does, so nothing of it had to move. A session that is idle-closed while detached is
released after the close.

**A host shutdown closes every browser at once**, as the server's shutdown does
since 2026-10-03, with a generous cap: the client's kill window that set the
one-second cap does not reach the host.

## Updates

The host, its children and the coordinator all run from the install root, so an
apply must stop them. **The coordinator's scan now leaves out the processes in its
own job.** When nothing else runs from the install, it asks the host to close every
browser with its own `browser_close`, in parallel, capped at 30 s like the idle
close, waits for the host to exit, and applies. Sessions come back through
`browserai_resume` and the session restore built on 2026-10-03. Headed windows are
closed too, which is the brief's direction and is recorded for review; the
alternative is that a headed window with its browser up holds the apply.

**The sign-in step applies nothing while the host runs.** The scan leaves the host
out because the loop stops it before an apply, so a host a server asked for during
the sign-in pass would read as nothing running; the step reports that it is not
alone, and the loop, which closes every browser first, applies.

## The coordinator's own lifetime

Q336 a keeps the coordinator one minute after the last tab closes. It now also
stays while its host runs, and the host stays while it holds a session or a
connection, plus one minute, so a client restart does not race the host's exit.

## Every bound, and what it is derived from

The maintainer, 2026-10-03, about the idle close's cap: *"I need a motivation. Also,
I do not like magic numbers."* So each bound this design added is derived from a
setting it names, and `SessionHostBoundsTests` holds it there.

| Bound | Value | Derived from |
|---|---|---|
| A front's wait for a host, `SessionHostAccess.StartBound` | 15 s | Half of the 30 s Claude Code 2.1.288 and codex-cli 0.155 and 0.160 give a server to start, measured for Codex and read in Claude Code's binary; the other half is left for the in-process start a front falls back to |
| The host's close of each browser before an update, `SessionHostProtocol.ShutdownCloseBudget` | 30 s | The idle close's own cap, `LiveSession.IdleCloseBudget` (Q367 a): the same close with nobody waiting |
| The coordinator's wait for the host to end, `SessionHostProtocol.StopBound` | 60 s | Twice the close: the browsers' closes, then each child ended through its stdin with its transport's five seconds |
| The host's stay with nothing to do, `SessionHostServer.Linger` | 1 min | The coordinator's own minute after its last tab (Q336 a), for the same kind of return |
| How often the host looks at itself and at a kept headed window, `SessionHostServer.LingerLook`, `LiveSession.DetachedWindowLook` | 15 s | A quarter of the linger, which is how far past it an empty host can run |
| The host pipe's buffer, `NamedPipes.StreamBufferBytes` | 64 KiB | The server pipe's own reply buffer; a larger frame waits for its reader, which is the backpressure |

## Codex

Measured above: Codex terminating its server's job leaves the host's browser
alive, 6 of 6 across 0.155 and 0.160. **What it still costs a Codex thread is
unchanged**: Codex never starts a stdio server again on the failure path, so the
thread loses MCP (the hazard row on that is open), while the session and its
browser now outlive it for a new thread to resume.

## The plan, and the gates

1. **Measure first**: the table above. Done 2026-10-03.
2. **The host's core, in process**: one set of sessions served to several
   connections, attachment and detach, planted red first through the in-process
   rig. `SessionHostTests`.
3. **The host mode and the relay**: `--host`, the host pipe with asynchronous
   connections, the front's relay and its in-process fallback. The front's search
   for a host, through the coordinator or the logon task, in process with a
   scratch scheduler: `SessionHostAccessTests`. The published host and front
   against a real Chromium: `SessionHostProcessTests`.
4. **The coordinator**: the `host` verb and the `--start-host` start, the job, the
   lifetime, the update close and the sign-in step. `SessionHostCoordinatorTests`,
   with a scripted host and windowless stand-ins in a real job.
5. **Containment**: the host killed takes every `node` and every browser it
   started, against the published binaries, and the keeper's close ends the host
   and what it started, with stand-ins.
6. **The records**: ARCHITECTURE, DECISIONS, HAZARDS, the kb and TODO.

**What is not built**: an arm that installs a test pack and lets an installed
server reach its host through the real logon task; it would start a coordinator
through the scheduler from inside the suite, which no arm does yet, and that is a
question before it is a test.

Each step: every behaviour change planted red first; the suite lock for any run
that starts the server, the app, a browser or an installer; the ordinary
two-shell gate before each merge; merges by fast-forward per the overnight rules.

## Decisions taken for review

Numbered so they can be answered by number; the build report carries the same
list with what each one cost.

1. **A session host in the coordinator's job, and not the coordinator itself**
   (direction b above).
2. **The front falls back to serving in-process** when it is not an install, when
   the host cannot be reached inside its bound, or when an update is installing.
3. **A detached session is taken over by the next call that names it**, from any
   client, the way a released lock is taken today.
4. **Headed sessions whose client went stay until their window closes**, watched
   every 15 s.
5. **The update closes headed windows too.**
6. **The host lingers one minute** after its last session and connection.
7. **The host is one process for all sessions**, so a fault in it ends all of
   them, which today ends only one client's.
8. **A session the host kept holds no update.** Once no server a client started
   runs from the install, the update closes the browsers of the sessions whose
   clients went; a session a connected client drives still holds the update,
   because its client's server runs from the install. The alternative is that a
   kept session holds the update too, which keeps its state across the restart at
   the price of an update that waits for every idle close. Put to the root session
   as a question, because it narrows the recorded rule that nothing exits itself to
   let an update in.

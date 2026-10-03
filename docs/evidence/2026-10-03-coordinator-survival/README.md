<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- a process the Task Scheduler starts keeps its job's processes through every client's exit (Q366 b)

**What this is.** 21 runs, taken 2026-10-03 between 13:38:40Z and 13:41:19Z, of
whether a process that a per-user scheduled task starts, and everything it starts
inside a kill-on-close job of its own, survives each way a client ends its stdio MCP
server; and then whether killing that process takes everything in its job with it.
Taken before any of option c was built, because option c rests on the answer. Seven
exits, three runs each, at **Claude Code 2.1.288** (the CLI), **2.1.287** (the binary
the VS Code extension ships), **codex-cli 0.155.0-alpha.9.2** and **0.160.0**, on
Windows 11 Pro 10.0.26300, every client under a scratch configuration against a
local API stub. **150 files beside this README, 778,066 bytes.** The rig is a probe
record at
[`docs/probes/2026-10-03-coordinator-survival`](../../probes/2026-10-03-coordinator-survival/README.md),
lane c's copy of [the client-exit rig](../../probes/2026-10-03-client-exit/README.md)
with three stand-ins added.

[`summary-survival.tsv`](summary-survival.tsv) is the result in eight lines.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: processes](../../../kb/windows/processes.md#a-process-the-task-scheduler-starts-keeps-its-jobs-processes-through-every-clients-exit----measured-2026-10-03) | Every number in the section |
| [kb: re-verification](../../../kb/re-verification.md) | The row on it |
| [design: coordinator-owned browsers](../../design/coordinator-owned-browsers/README.md#the-measurement-taken-before-any-code) | The table, and the containment half |

## What was measured

Three stand-ins, all one binary, `ExitRig.exe`, in three modes:

- **The coordinator**, started by a scratch per-user task registered with the
  settings of BrowserAI's own sign-in task and run on demand, the way a blocked
  server runs that task. It is built for the Windows subsystem, as `BrowserAI.exe` is.
- **The host**, which the coordinator starts inside a kill-on-close job of the
  coordinator's own, with no handle inherited: created suspended and assigned to the
  job before its first instruction, the client-exit rig's way. BrowserAI's own
  launcher passes the job in `PROC_THREAD_ATTRIBUTE_JOB_LIST` instead; either way
  the process runs no instruction outside the job.
- **A browser**, which the host starts on request in a further kill-on-close job,
  nested in the first, one per run.

Each run's dummy MCP server, started by the real client, asks the host over a pipe
for a browser named after the run, and also starts the stand-in it always started,
in a job of its own as BrowserAI's server does. The client is then ended the
scenario's way. After the harness's settle the host is asked whether that run's
browser is alive, again 3 s later, and then it is released and watched to its end.

| Exit | What ends the server | Server | Its own stand-in | The host's browser after the settle | 3 s later | Released |
|---|---|---|---|---|---|---|
| `b1` | Claude Code CLI, stdin closed, so its own `taskkill /T /F` | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |
| `b2` | Claude Code CLI terminated | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |
| `e5` | the VS Code extension host exits, through the stand-in `vscodehost.js` | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |
| `d1` | `codex exec` 0.155.0-alpha.9.2 finishing | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |
| `d2` | codex app-server 0.155.0-alpha.9.2, stdin closed | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |
| `n1` | `codex exec` 0.160.0 finishing | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |
| `n2` | codex app-server 0.160.0, stdin closed | killed 3/3 | dead 3/3 | alive 3/3 | alive 3/3 | ended 3/3 |

"Killed" is the server's exit code not being 77, the code it exits with when it
finishes on its own; "ended" is the released browser's exit seen through a handle,
code 0 every time.

**The containment half.** With the batch done, the host was asked for two browsers
more, `final-a` and `final-b`, and a watcher opened handles to both, checked each
against the creation time recorded for it, and began waiting. The driver issued the
kill about 700 ms after starting the watcher: the coordinator stand-in, terminated
by its recorded pid and creation time. **Both browsers exited, code 0, 690 ms into
the watch**, and the host no longer answered on its pipe 2 s later
([`driver.log`](driver.log), [`final-watch.out`](final-watch.out)). The watch's clock
and the kill's were not one clock, so this places the exits at the kill and not
closer than that.

**A finding nobody asked for.** The task-started process is itself in a job, one
the Task Scheduler put it in with six other processes, limit flags `0x0`: no kill on
close and no breakaway rule. Nesting kill-on-close jobs of our own under it worked,
three levels deep: the scheduler's, the coordinator's (`0x2000`, read back by the
host) and each browser's (`0x2000`). The readings are the first lines of
[`coord-44784.log`](coord-44784.log) and [`host-72468.log`](host-72468.log); which
processes the six others were was not recorded.

## What is here

| Path | What it is |
|---|---|
| `summary-survival.tsv` | Per scenario: runs, server killed, its own stand-in dead, the host's browser alive after the settle and 3 s later, released and ended |
| `analysis-survival.tsv` | One row per run: the client's exit, the server's, the stand-in's, whether a `taskkill` was seen, and the host's three answers about that run's browser |
| `driver.log` | The driver: the task registered and run, the coordinator's identity, the batch, the final pairs, the kill, the watch and the task removed |
| `coord-44784.log`, `host-72468.log` | The coordinator stand-in's and the host stand-in's own logs, named by pid: the job each created and every browser the host started and released |
| `standin-70808.log`, `standin-71192.log` | The two browsers the coordinator's kill took |
| `coord-identity.txt`, `final-watch.out` | The coordinator's recorded pid and creation time, and what the watcher saw |
| `task-actions.log` | Every scratch task this lane registered, ran and removed that day, four of them, the first three the rig's own trials |
| `binaries.tsv` | The SHA-256 of each client binary; three are the binaries [`2026-10-03-client-exit`](../2026-10-03-client-exit/README.md) digested |
| `runs/<scenario>-r<n>/` | All 21 runs without the client's own logs: `harness.log`, `server-<pid>.log`, `standin-<pid>.log`, `result.json`, and the configuration the client was handed |
| `left-out.sha256` | What was left out, with each file's SHA-256 and size |

## What was cut, and what was left out

- **The client-side logs**: 51 files, 15,983,429 bytes of `claude-debug.log`,
  `client-stdout.log` and `client-stderr.log`, most of it the Codex app-server's
  trace output. Each digest is in `left-out.sha256`.
- **The rig's build output**, the client binaries, the npm install of codex-cli
  0.160.0 and the scratch client homes, which are the client-exit rig's and are
  described there. The binaries' digests are in `binaries.tsv`.
- **Nothing was changed.** No file here had a profile path, a name or a key in it,
  searched for before the cut; the client configurations carry the stub's key by the
  name of a variable and not its value.

The scratch task's name was `BrowserAI.c-probe coordinator <stamp>`, registered for
the current user alone and removed at the end of each run, each removal in
`task-actions.log`. Nothing outside the scratch directory was written, and no real
client configuration was touched: every client ran with `CLAUDE_CONFIG_DIR` and
`CODEX_HOME` pointed into scratch.

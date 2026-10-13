<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 -- a resumed session's first start, after the server of its last page has ended

⚠️ **Written 2026-10-13 and not run yet. No number in this file comes from it.** It is the
measurement the open row of [the hazard index](../../../HAZARDS.md) asks for, the row of a
Firefox session start that waited out Playwright's 180 s, which the maintainer's 125 b of
2026-10-12 opened in his words, *"125 your recommendation"*, and which
[`TODO.md`](../../../TODO.md) carries for after the release. Its runs will be cut into a
batch named `docs/evidence/<date>-restore-stall/`, and what they establish goes into
[the timings article](../../../kb/playwright/provisioning-and-timings.md), beside the
entry on the other 180 s Firefox stall, safe mode.

**Why it exists.** Twice on 2026-10-12 a headless Firefox 156.0 session of the installed
BrowserAI, resumed after its idle close, answered its first browser call with
`TimeoutError: async initializeServer: Timeout 180000ms exceeded.`, each time a session
whose last page had been on a `127.0.0.1` port that nothing listened on any more, and each
call log ending on Firefox's jump list failing to fetch a favicon for that page
([the two calls](../../evidence/2026-10-12-firefox-restore-stall/README.md)). Two fresh
sessions in the same minutes started at once. Nothing was run to separate the restored
tab from the profile's other state. This rig runs it.

## What it measures

Every repetition makes a session through BrowserAI, opens a page a local server serves,
waits for BrowserAI to close the browser on its idle time, ends the server or keeps it,
resumes the session and makes one browser call, which is the call that starts the browser.
The outcome per repetition is whether that start waited out Playwright's 180 s, and how
long it took either way. Four arms, interleaved arm by arm in every round:

| Arm | Browser | The page's answers | The server at the resume | What it separates |
|---|---|---|---|---|
| `ff-dead` | Firefox | the dashboard's headers | ended | the condition both sightings had |
| `ff-alive` | Firefox | the dashboard's headers | still answering | the same resume with nothing ended: the control |
| `ff-dead-plain` | Firefox | no headers of its own | ended | whether what the page's answers carried matters, or only the address |
| `cr-dead` | Chromium | the dashboard's headers | ended | whether it is Firefox's, as `TODO.md` asks |

**The page is shaped like the one the sightings had open**, because the sessions of
2026-10-12 were on the dashboard and nothing has said which of its properties matters: a
token in the path and `?tab=1`, an external script and style sheet, a clock ticking every
second and an event stream sending every second, and in three arms the answer headers of
`PageListener.SecurityHeaders` at `449fc340`, copied into `lib.mjs`: `no-store`, a content
security policy of `'self'` only, and `Cross-Origin-Opener-Policy: same-origin` among them.
Firefox writes each history entry's policy container, the content security policy inside
it, into the session it restores (`SessionHistory.sys.mjs` lines 256 to 258 and 555 to 563
in firefox-1553's `omni.ja`, read and not run), and `lib.mjs` records per entry whether one
was written, so the plain arm is the one that tells the address from the answers.

**The idle close is BrowserAI's own** and not a `browserai_close`, at `idleMinutes` 1, the
shortest the setting takes (`IdleSetting.Of`). The sightings closed after the default ten
minutes; the close itself is the same path whatever the period
(`LiveSession.CloseForIdleAsync`: the child's `browser_close`, up to thirty seconds for its
answer, then the child ended). Every session runs with `debug: true`, which raises
BrowserAI's own log level for the session and changes nothing in the child's configuration
(`SessionManager` opens the session's log at `LogLevel.Debug` with it, and the generator
does not read it).

### How many, and why

**Five rounds, so five resumes per arm, in the first batch.** Five is the smallest equal
count at which a complete separation, five stalls against none, is significant beyond 0.01
by the exact two-sided Fisher test: 2 in 252, p = 0.008. At four it is 2 in 70, p = 0.029,
and at three 2 in 20, p = 0.1. The two sightings were two stalls in two such resumes, so a
cause that holds every time shows as five in five.

**Then one rule, decided here and not after reading the counts:**

- **Every `ff-dead` resume stalls, or none of the arms' do: the counts stand**, and the
  replays below say where it waits.
- **`ff-dead` stalls in one to four of five:** the stall is not every time, and `ff-dead` and
  `ff-alive` each go to twenty resumes, with fifteen more rounds of both. Twenty is what
  puts an exact 95% interval at about 0.2 either side of a middling rate (10 of 20 gives
  0.27 to 0.73), enough to tell *usually* from *sometimes* from *rarely*, which is the
  difference the fix directions turn on.
- **No `ff-dead` resume stalls:** three resumes of `ff-dead` at the sightings' own ten
  minutes of idle time, since that is the one way this batch departs from them. Three,
  because a cause that holds at least two times in three, as one that held in both
  sightings may, shows at least once in three tries more than nineteen times in twenty:
  1 - (1/3)^3 = 0.96.

**One warm-up per browser before the first round**, a fresh session opened and destroyed and
kept out of the counts: the browsers run from a new copy, and the first start from a new
copy pays for the copy alone.

**After a stalled first call, a second call**, for the first two stalls of an arm only. A
failed start lets the next call start the browser again, and whether that one gets through
is what a retry would buy; two say whether it is the same both times, and the rest would
only cost three minutes each.

### What it reads

- **The client's side.** Each call's start, answer and duration, and BrowserAI's answer,
  whose call log is Playwright's own: the browser's command line and everything the browser
  printed.
- **BrowserAI's side.** The background's standard error, where every session's own records
  go, which is how the rig sees the idle close end (`IdleLog`'s events 60, 62 and 63); and the
  config BrowserAI wrote for the resumed child, copied out of the background's instance folder.
- **The browser's side, without touching it.** A census of every member of the step's job
  every five seconds: pid, parent, creation time, processor time, working set, and the image
  and command line of each process the first time it appears. The session's browser is the
  process whose command line names the session's profile and its descendants by parent pid,
  so the summary can say whether a stalled browser had a content process started with
  `-safeMode` (the signature of the other 180 s stall), and whether it was busy or idle.
  The profile's file count and size every five seconds while the first call is in flight,
  and Firefox's session files decoded before the resume and after the call, which say what
  it was going to restore.

**Where a stalled start waits is read, not guessed, and BrowserAI cannot show it.** A
session's child refuses `DEBUG` and is started with an allowlist that has no `MOZ_LOG` in it
(`ChildEnvironment`). So for every stalled resume, `replay.mjs` starts the payload's own
`@playwright/mcp` child the way BrowserAI starts one, with the config BrowserAI wrote for that
resume and the session's paths moved into the replay's folder, on a fresh copy of the
profile taken after the idle close and before the resume, and makes the same first call,
with:

- `DEBUG=pw:protocol,pw:browser,pw:api`: every juggler message in both directions, timed;
- Firefox's session store logging at every level (`browser.sessionstore.loglevel` and its
  appenders, read in `SessionLogger.sys.mjs` and `LogManager.sys.mjs` of firefox-1553's
  `omni.ja`), into the browser output Playwright logs and into `sessionstore-logs`;
- for the replay that changes nothing, `MOZ_LOG` with `DocLoader`, `nsDocShell`,
  `SessionHistory`, `BrowsingContext` and `ProcessIsolation`, each name looked for in
  `xul.dll` first and the finding recorded, so that a module that logged nothing can be told
  from a name this build does not have.

**What the protocol log can tell apart, read in the source and not run.** Playwright's
persistent launch ends in `loadDefaultContextAsIs` (`coreBundle.js` lines 53112 to 53125 at
`playwright-core` 1.64.0-alpha-1790635538000), which waits three times: for a first page,
for that page to initialise, and for its main frame's `load`. Its Firefox page initialises on
juggler's `Page.ready` (line 46354) and learns of `load` from `Page.eventFired` (line 46474),
so the log shows which of the three was still open. BrowserAI drops Playwright's own blank
page (`BrowserConfiguration.DefaultPageArgument` in `ignoreDefaultArgs`), so with the restore
on, the restored tab is the first page. In firefox-1553's juggler, a main-frame navigation
that fails emits `pageReady` and no `load` (`FrameTree.js` lines 298 to 308, `PageAgent.js`
lines 293 to 302), and `load` comes only from the document's own load event or, for a page
already loaded when juggler attached, only when it is `about:blank` (`PageAgent.js` lines 84
to 95 and 251 to 264). None of that has been seen happening; the replays are for seeing it.

**The replays, per stalled snapshot of an arm, up to three snapshots:** `as-is`, nothing
changed; `revived`, a server answering on the same address with the same token, which is
the one difference from `as-is`. **And for the first stalled snapshot only**, three
replays that each try one thing a fix could change: `no-restore`, the browser restores
nothing; `blank-first`, Playwright's own blank page is put back; `lazy`, Firefox restores
a tab only when it is selected. Each is a measurement of a direction, not a fix.

## What is here

| File | What it does |
|---|---|
| `run.ps1` | The launcher. Checks the rig's own files (`Add-Type` of `RigJob.cs`, `node --check` of the scripts) before anything else; takes the suite lock by creating its directory, writes `owner.txt` and reads it back for this script's pid before it runs anything; copies the browser revisions the published payload pins out of the provisioned browsers root into the scratch data root; runs each step as one node process in a `KILL_ON_JOB_CLOSE` job of its own and writes the job's census; ends the job; takes out the Firefox launcher values the run added and reports every other registry change; releases the lock, on failure too |
| `RigJob.cs` | The job objects, the census of a job's members, and the lock directory's atomic create, for `run.ps1` through `Add-Type`. Kernel queries only: nothing in it waits |
| `rig.mjs` | The measure stage: the published `BrowserAI.exe --background` on a pipe of the rig's own and a relay `BrowserAI.exe --mcp` beside it, the warm-ups, and the repetitions of every arm, one session at a time. Writes `results.jsonl`, `warmup.jsonl`, `events.jsonl`, the background's and the relay's standard error, the configs and the profile snapshots |
| `replay.mjs` | One replay of a stalled snapshot through the payload's own child, with Playwright's and Firefox's logs on, as described above. Writes `result.json`, `config.json`, `child.stderr.txt` and Firefox's log files |
| `lib.mjs` | What the two share: the MCP client, the page server, the port probe, Firefox's session files decoded, the walk of a profile, and the two environment blocks |
| `summarize.mjs` | The tables: per arm, the resumes, the stalls with an exact 95% interval and the Fisher p against `ff-dead`, the answered and stalled start times, the second calls, whether the session files named the page, and safe mode; per replay, the outcome and which of the three waits was still open, with the page's juggler events in order |

## What keeps it off the rest of the machine

- **It runs under the suite's lock and never beside a suite run**, because it starts
  browsers and a BrowserAI, and it proves its take before it starts either.
- **It never touches the installed BrowserAI.** Its BrowserAI is the one published from
  this tree, which is not installed, so it starts no scheduled task, checks for no update
  (`BackgroundUpdates` stops at its not-installed record) and raises no toast (a process
  under no application id gets no toast surface, `WindowsToastSurface.ForThisProcess`), all
  read at `449fc340`; its background serves a pipe the rig names, under a data root of its own. That
  root is `%USERPROFILE%\Downloads\tmp-restore-stall\data-root` and not under `.work\`,
  because `InstallRootScope` refuses a data root outside the user's profile; the sessions
  and every output are under `.work\restore-stall\`.
- **Its browsers are copies.** The revisions the payload pins are copied out of the
  provisioned browsers root, which is only read, so no browser of the rig runs from that
  root and BrowserAI's stray sweep, which matches the full image paths under its own data
  root, has only the copies to look at. `PWTEST_SERVER_REGISTRY` sends Playwright's server
  registry, and BrowserAI's reaping of it, to scratch.
- **Every process it starts is in a job it created, from birth**: the node process of a step
  waits for a go file the launcher writes only once that process is in the job, and starts
  nothing before. What a step leaves running ends with the job; the relay and the replay's
  child are told to end by their input closing first. **Nothing is ended by pid, and nothing
  is selected by image name**: the census reads the members of the rig's own job. A search of
  the six files for the eight spellings `ProcessSelection` keys on found none, where the
  same search finds `GetProcessesByName` in `2026-09-14-firstrun/observe.ps1`.
- **Nothing shows a window.** Every browser is headless; every node launch passes
  `windowsHide: true` with its standard streams piped; the one `Start-Process` passes
  `-NoNewWindow`.
- **The client's own variables do not reach BrowserAI**: the `CLAUDE`, `CODEX`, `VSCODE`,
  `ANTHROPIC` and `OPENAI` variables are dropped from the block the rig hands it, and
  `CLAUDE_CONFIG_DIR` and `CODEX_HOME` point into scratch.
- ⚠️ **Firefox records every executable path it runs from** under
  `HKCU\Software\Mozilla\Firefox\Launcher`, so the copy adds values there. The launcher
  reads that key and three beside it before a stage, takes out afterwards only the launcher
  values that name the scratch folder and were not there before, and reports every other
  addition and change in `registry-<stage>.txt` without touching it.

## Running it

Not before the release runs of 2026-10-13 have ended. In this worktree, with the payload
copied in and the server published, from PowerShell, detached, with the output in a file:

```
robocopy <the payload folder> payload /E
dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64 --self-contained
pwsh -NoProfile -NonInteractive -File docs\probes\2026-10-13-restore-stall\run.ps1 -Stage measure -Name first
<payload>\node\node.exe docs\probes\2026-10-13-restore-stall\summarize.mjs .work\restore-stall\runs\first
```

Then whichever of these the rule above names, and the replays:

```
run.ps1 -Stage measure -Name extend -Arms ff-dead,ff-alive -Rounds 15 -NoWarmup
run.ps1 -Stage measure -Name fidelity -Arms ff-dead -Rounds 3 -IdleMinutes 10 -NoWarmup
run.ps1 -Stage replay -Name replay -From first
```

**What it costs.** One session at a time: a repetition whose first start is answered takes
about ninety seconds, sixty of them the idle wait, and one that stalls about four and a half
minutes, seven and a half when its second call stalls too. The first batch is twenty
resumes and two warm-ups: about half an hour if nothing stalls, about fifty minutes if every
`ff-dead` and `ff-dead-plain` resume does. The extension would add up to ninety minutes,
the ten-minute batch about thirty-five, the replays up to half an hour. The load is one
headless browser, BrowserAI's two processes and a node child at a time, mostly idle during
the waits; the copy of the browsers is about 800 MB of disk once.

## How the stored copies differ from the ones that ran

These are the files as written on 2026-10-13, and none of them has run yet.

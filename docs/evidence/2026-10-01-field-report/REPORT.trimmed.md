<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# E: findings on the BrowserAI field report (research only, nothing changed)

## Answer

There is an issue: two defects, one design gap, and a smaller record defect the report waved through as harmless. None is fixed. v1.1.0 (d3aabf1), master (234f8ff) and next (ecfbab3) behave the same on all four.

The report's account of what BrowserAI did holds up against the code and against two logs the agent did not write. Its evidence section is stale, one timeline detail is wrong, the sign-out is not explained, and four of its eight proposed changes run into decisions already recorded in this repository.

## 0. State of the evidence (differs from the brief)

- **Both sessions are gone.** `%USERPROFILE%\AppData\Local\BrowserAI\logs\browserai-20260929-000.log` lines 412 and 415: "Session destroyed at ...\2026-09-24-<site>-session; 0 item(s) could not be removed." at 15:43:47.902Z, and the same for `2026-09-29-<site>-session` at 15:43:48.430Z, both by pid 73608. Neither directory exists and the session index holds neither. No live session was near this investigation, and there was no `browserai.data` to copy.
- **You were given an earlier draft.** The final is `C:\Source\<private repository>\.work\2026-09-29-browserai-feedback\browserai-resume-and-idle-close.txt` (17:44 CEST, 29 Sep). Its EVIDENCE and CURRENT STATE sections say both sessions were destroyed "at the maintainer's request" after export, with 83 and 192 rows.
- **What I used instead:** the agent's four TSV exports in `...\2026-09-29-browserai-feedback\evidence\`, written 17:43:40 CEST, seven seconds before the destroy. They carry all seven columns of the `log` table.
- **Cross-check against sources the agent did not write.** Claude Code's own MCP call log (`%LOCALAPPDATA%\claude-cli-nodejs\Cache\<private repository>\mcp-logs-browserai\`) and the global log agree with the exports:
  - 24 Sep: every client call has a row (46/46 `browser_evaluate`, 5/5 `browser_tabs`, 2/2 `browser_navigate`, 1/1 `browserai_init`).
  - 29 Sep: every tool count matches, except one unknown-session refusal (global log line 167) and seven calls I attribute to a third session created 09:31:06Z and destroyed 09:37:37Z.
  - Holder pid and creation time in the exported statements equal the global log's `pid=84568@134347374148952543` and `pid=73608@134351433091302372`.
  - So id, time, tool and outcome in the exports are reliable. The `why` and `failure` text is not cross-checked.
- **Arguments are not in the record** (the `log` table has no arguments column). I took them from the Claude Code transcript `%USERPROFILE%<the session's Claude Code transcript>` and its subagents. I extracted only tool names, argument keys, the values of `headed`, `viewport` and `locale`, BrowserAI's own answer lines, and user-agent product tokens.
- **Handling.** I called no BrowserAI tool, started no server or browser, opened no profile or cookie store, and wrote only under `C:\Source\SixFive7\BrowserAI\.work\e-field-report\`. I read ten `why` strings (session A rows 72 to 82) to understand the sequence and quote none.

## 1. What the report is about

An agent used BrowserAI 1.1.0 to keep a signed-in marketplace session a person had opened by hand. It met two behaviours:

- `browserai_resume` cannot change a session's per-run settings (here: headless to a visible window) while the same server process already holds the session, and it answers success anyway.
- After ten idle minutes BrowserAI closes the browser; the next call lands on a blank page and nothing says so.

## 2. Claim by claim

| Claim | Verdict | Evidence |
|---|---|---|
| F1: resume ignores every per-run argument on a session this process holds | Confirmed | Code, all three refs (section 3). Transcript: resume at 10:13:49.862Z with `headed: true`, `viewport: 1280x800` answered "NOTE: This session is already open in this BrowserAI; nothing was changed." with "viewport: 1280x720", no error flag, in 11 ms; the same at 10:35:56.415Z in 14 ms. The next result's user agent was HeadlessChrome/154 (10:18:05Z). |
| F1: dead-child relaunch reuses `session.Launch`; a session leaves `_live` only by destroy or shutdown | Confirmed | `RelaunchAsync`; the only two `TryRemove` sites. |
| F1: "a sub-agent called browserai_resume without headed" (row 56) | Wrong in the detail | The sub-agent passed `headed: false` and `viewport: 1280x720` explicitly (transcript, 10:01:10.100Z). BrowserAI did what that call asked. |
| F1: the viewport line was the only sign | Confirmed | The answer has no headed or headless line at all. |
| F2: idle close after ten minutes, recorded only in the session log | Confirmed | Rows 55, 80, 83 (A) and 187, 192 (B) carry the idle-close sentence, each 600.00 to 600.01 s after the previous row settled. No client-side call corresponds to any of them. |
| F2: the next call ran on about:blank and nothing said why (row 188) | Confirmed | Row 188 is `failed`. Its payload contains "TypeError", "Failed to parse URL from", "Page URL" and "about:blank", and none of "idle", "closed", "relaunch". |
| F2: a close loses pages and tabs, sessionStorage, session cookies | Two of three | Pages and tabs: confirmed by row 188. sessionStorage: agrees with kb. Session cookies: not established; the kb measured a `max-age=3600` cookie and says so (`kb/playwright/provisioning-and-timings.md` line 1037). |
| F3: only forwarded calls reset the timer, so a window can close under a person | Confirmed by code, not observed | One arming site. The workaround is visible: 15 gaps of 234 to 239 s in session B (rows 122 to 128, 162 to 172). The longest quiet stretch while the window was first open was 410.7 of the 600 s (B rows 3 to 4); that this was the hand sign-in is inferred. |
| F4: three wrong conclusions caused by the answers | Partly | The answers do withhold both facts. The third conclusion (idle close signed the user out) contradicts the row, which says nothing was lost. Whether the agent told you these things is checked by keyword and timestamp only (first "restart" at 10:15:08Z, first "idle close" at 10:40:05Z). |
| F4: "nothing in any answer prompted" `browserai_catch_up` | True of answers, false of the surface | The server instructions and the tool's description both say to call it on arriving at a session you did not create. The sub-agent that resumed session A did not. |
| Idle close after the caller's own `browser_close`: "harmless but noisy" | Confirmed, and understated | Row 79 (caller's close) settled 12:19:33.174; row 80 says BrowserAI closed the browser at 12:29:33.184, with nothing forwarded between. See problem 4. |
| Sign-out after five days: "probably not BrowserAI" | Not established | Two things changed at once: five days, and headed to headless (user agent Chrome/154 to HeadlessChrome/154, which `TODO.md` lines 226 to 232 record as server-visible). The agent tried a user-agent override in headless (row 78) and never got a real window on that profile, because of F1. The profile is destroyed. |
| Context: 1.1.0, chromium 154.0.8037.0 r1246, pids 84568 and 73608, start and exit times | Confirmed | Section 4 and the global logs (24 Sep log lines 151 and 10036; 29 Sep log lines 58 and 545). |

## 3. Root causes (next line numbers, v1.1.0 in brackets)

**F1. The already-open short circuit predates per-run arguments and was never revisited.**
- `src/BrowserAI/Sessions/SessionManager.cs` 809-816 [744-751] parse the arguments. Line 829 [764] takes the `_live` branch. Line 884 [819] answers "nothing was changed" with `IsError: false`.
- Only the not-held path reaches `OpenAsync` (960-977 [878-895]), where the config is generated (2329 [2226]) and written for a child that reads it once.
- `RelaunchAsync` 2493-2510 [2390-2407] reuses `session.Launch`.
- History: the branch was written 2026-08-16 (88b1014). `headed` became per-run on 2026-08-20 (6c1995a), with six more arguments the same day (d9150f9). The branch was reopened on 2026-09-17 for the dead child only (5d0d04f).
- No test passes a per-run argument to `browserai_resume`. I searched every test file naming resume, 12 lines after each mention; the scan did match an init's `["headed"] = "yes"` nearby, so it can find one.

**F2. The timer's safety argument was measured with a navigation as the follow-up call.**
- The kb measurement (lines 928 to 968) and the real-browser test both follow the close with `browser_navigate`, which works from any page.
- The row text `LiveSession.IdleCloseWhy` (`src/BrowserAI/Sessions/LiveSession.cs` 154-156 [130-132]) says "Nothing was lost", which is true of the profile and false of pages and tabs.

**F3. Headedness never reaches the timer.**
- It is armed at `src/BrowserAI/Proxy/BrowserProxy.cs` 1064 [706] and nowhere else.
- `LiveSession` builds it identically for every launch (98 [89]).

**Problem 4. The timer is re-armed by every forwarded call, including the caller's own close.**
- `BrowserIdleTimer.cs` 171-180 and 340-347 arm at both ends of a call.
- `CloseBrowserAsync` (`LiveSession.cs` 431-441 [337-347]) reads the job's process count, then writes the row and sends the close without using it.

## 4. Is it fixed in our version? No.

| | v1.1.0 (d3aabf1) | master (234f8ff) | next (ecfbab3) |
|---|---|---|---|
| Resume on a held session | drops arguments | same | same |
| Idle close: period, silence, row text | 10 min, silent, "Nothing was lost" | same | same |
| Timer and headedness | not distinguished | same | same |
| Row after the caller's own close | written | same | same |

- **v1.1.0 to next:** the resume handler changes only in wording and in the Q261 version note on the not-held path. `RelaunchAsync` changes by one dash in a comment. `BrowserIdleTimer.cs` changes in wording only. `LiveSession.cs` gains the registry reap and `BrowserIsOpen`.
- **master to next:** one comment word in `SessionManager.cs`; every other relevant file is identical.
- **The report's line numbers:** its `SessionManager.cs` numbers (809-816, 829, 884, 2493-2510, 1564, 670) match master and next, not v1.1.0 (744-751, 764, 819, 2390-2407, 1471, 605). Its `BrowserIdleTimer.cs` numbers match all three. The agent read the working tree and called it 1.1.0; the logic is the same, so its conclusions carry.
- **Installed version: 1.1.0, the published build.**
  - `sq.version` says 1.1.0 and FileVersion is 1.1.0.0; both server starts logged "BrowserAI 1.1.0 started".
  - `packages\BrowserAI.app-1.1.0-full.nupkg` has SHA-256 `438d0d7b...2ef621`, equal to the digest GitHub publishes for the v1.1.0 asset (`gh release view`, read today).
  - `current\BrowserAI.Server.exe` (`8eb6641f...`) is byte-identical to the entry inside that package.
  - The binary carries v1.1.0's wording (the em-dash form of the idle-close sentence, "rather than" in the instructions) and lacks the Q261 note.

## 5. By design or defect

**The descriptions promise what the report says, and more.** Quotes are from next; v1.1.0 has an em dash where next has `--`. All are in `src/BrowserAI/Sessions/SessionToolSurface.cs` unless stated.

- Line 668 (resume): "Every per-run argument init takes is accepted here too and none is read back from last time -- a session created headless is resumed headed, at a different viewport, with network capture on, without being destroyed first."
- Line 677 [676] (resume `headed`): "Defaults to false, and it is NOT read back from what the session was last time -- every run says what it wants. Accepted here as well as on init because the case that matters is a session created headless that now needs a human to sign in."
- Line 678 [677] (resume `debug`, not cited by the report): "Accepted here as well as on init, because the interesting case is almost always a session that is already running badly." A session already running badly is one this process holds.
- Lines 656 and 680 [679] (`captureNetwork`, not cited): "It takes effect at the NEXT BROWSER LAUNCH ... a session whose browser is already up must be closed and reopened first." That is the sequence the agent followed twice.
- Line 646 (init): "all per-run, none is recorded, and the same arguments are accepted again on browserai_resume."
- Line 653 (init `headed`): "a property of THIS launch and is not recorded: the same session can be resumed headed tomorrow and headless the day after".
- `src/BrowserAI/Proxy/ServerInstructions.cs` 193: "Nothing chosen at init binds a later call: 'headed: true' opens a window and 'tracing: true' records the run, both per-run, not bound to the directory."

The internal documents say "regenerated at every child launch" (`README.md` 180, `ARCHITECTURE.md` 71-74, `DECISIONS.md` 733), which is true of the code. The model-facing text dropped that qualifier. No model-facing string, and nothing in `README.md`, mentions the idle close.

**Verdicts.**

- **F1 is a defect, not a decision.**
  - `DECISIONS.md` 733 gives the purpose of per-run `headed` as "a session created headless and later needed headed had to be destroyed and recreated". That is what happened here.
  - `BrowserConfiguration.cs` 564-570 says a caller "should not have to destroy it first".
  - Init's own comment (`SessionManager.cs` 714-722) says who holds a directory must not be something a caller has to model.
  - The one test on the branch, `DeadChildTests.AResumeOfALiveSessionStillChangesNothing` (line 125), passes no arguments.
- **F2: the silence is by design and pinned; the sentence is a defect.**
  - `BrowserIdleTimerTests.EveryToolCallResetsTheTimerAndOnlyASessionThatGoesQuietIsClosed` (305-310) asserts that no frame about the close reaches the client.
  - `AnIdleSessionLosesItsBrowserKeepsItsNodeChildAndTheNextCallStillWorks` (628-642) asserts the next answer carries no closed-browser wording.
  - "Nothing was lost" is the same class of defect as `HAZARDS.md` 318, which was closed 2026-09-22 for the relaunch sentence.
  - `DECISIONS.md` 737 already makes the report's argument, for the dead child: a lazy relaunch was rejected because "a call that silently starts a browser and answers normally hides the gap, and the gap is the thing a reader needs".
- **F3 is a gap nobody decided.**
  - The only place headedness and idle meet is `BrowserConfiguration.cs` 243-247, about config uniformity.
  - Upstream's own option text reads "Defaults to one hour for headless browsers, never for headed ones" (installed `playwright-core\lib\coreBundle.js` line 74986).
- **Settled decisions the report's proposals touch.**
  - `DECISIONS.md` 702 and 841: "There is no close tool, and that is the decision"; "Timer values are settled, not open".
  - `ARCHITECTURE.md` 217-232: forwarded answers are byte-identical, with one named exception.
  - `BrowserIdleTimerTests.TheShippedIdlePeriodIsTenMinutesAndNothingInTheProductChangesIt` (line 70).

## 6. What the report got wrong, overstated or missed

**Wrong**
- "Session A kept as evidence, session B still in use": false since 17:43 CEST on 29 Sep.
- "82 rows": 83.
- "Without headed": the sub-agent passed `headed: false` explicitly.
- Line numbers labelled 1.1.0 are the working tree's.
- "Harmless but noisy" (problem 4).

**Overstated**
- Session-cookie loss is stated as fact and is unmeasured here.
- "All three mistakes would have been prevented by answers": the third ran against what the row said.
- "Probably not BrowserAI": the headless relaunch is not excluded, and the agent's own override test is not mentioned.
- "The window opened" at init: the browser starts on the first forwarded call (86 s later in session B).

**Missed**
- The `debug`, `captureNetwork`, init and instruction sentences quoted above.
- The answer has no headed line and reports success.
- "Nothing was changed" is also false the other way: the branch appends a passed `purpose` first (`SessionManager.cs` 839-842).
- Suggestion 1 as written would turn any bare resume into "headless at 1920x1080", because omitted arguments collapse to defaults (`SessionManager.cs` 3179-3186, 814). Today nothing can take a window from a person that way.
- Suggestions 3, 4, 6 ("never") and 7 each reopen a recorded decision. That makes them yours to decide, not fixes.
- Upstream's "never for headed".

## 7. Options per confirmed problem

### Problem 1: per-run arguments on a session this process already holds

Primer: window or not, viewport, locale, time zone, TLS and capture settings are written to a config file when BrowserAI starts a session's node child, which reads it once. A resume on a session the same process holds starts no child, so it drops those arguments and answers success. One server process serves a whole Claude Code conversation including sub-agents. Whoever opens a session first fixes its settings until the client restarts or the session is destroyed with its profile.

| | Direction | Trade-off |
|---|---|---|
| 1a | Say so, apply nothing. Name each explicitly passed argument that was not applied, with both values and the route that applies it. Make the answer error-shaped when one differs (precedent: destroy's survivor arm, `SessionManager.cs` 1652-1658). Add a headed line. Qualify the six sentences. | Smallest and safe. The stated purpose of per-run `headed` stays unmet inside one conversation; the route it names is a client restart. |
| 1b | Apply on resume, always. Regenerate the config, start a replacement child, swap with `ReplaceChildAsync`, keep the lock. | Delivers the promise. A resume can now destroy live pages or close a window a person is using. It needs "omitted means keep" parsing first, and a graceful close before the swap (the 2026-09-22 measurement: an unclean browser death loses recent cookie and localStorage writes). `debug` lives in the logging stack and needs its own swap. |
| 1c | Apply only when no browser is up (after the caller's `browser_close` or an idle close). Otherwise apply nothing and say: close first, then resume. | Two calls and a state to learn. Needs the browser-up predicate (on next, not in v1.1.0) and a guard against a call arriving mid-swap. No resume can take a page or window away, and the existing `captureNetwork` sentence becomes true. Both of the agent's attempts (rows 74 to 75, 80 to 81) would have worked. |
| 1d | A tool that lets go of a session without deleting it. | Simple semantics. Reopens `DECISIONS.md` 702 and 841, adds a ninth authored tool, and releases the directory lock between two calls, which is the hand-away the 2026-09-17 decision rejected. |

Recommendation: 1a whatever else is chosen, then 1c.

### Problem 2: the idle close is silent and its record says nothing was lost

Primer: after ten minutes without a forwarded call BrowserAI sends upstream's `browser_close`. The browser goes, the node child stays, and the next call starts a new browser on a blank page. This returns about 380 MB and is silent on purpose. The safety argument was measured with a navigation as the next call. Any call that depends on the page the caller left runs on about:blank instead (row 188).

| | Direction | Trade-off |
|---|---|---|
| 2a | Correct the words: the row text, the two design notes, and one sentence on `browserai_init`. | No behaviour change. By my estimate from source, init has about 172 characters of headroom and resume about 430; the instructions have 22. A caller still learns nothing at the moment it matters. |
| 2b | Refuse the first forwarded call after an idle close, once, at the door beside the dead-child check (`BrowserProxy.cs` 953). | Follows two existing decisions (`DECISIONS.md` 737 and 805) and keeps answers byte-identical. Costs a turn after every idle gap, including before a navigation that would have worked. The next-call test must be rewritten, and its worry (a model reading "closed" gives up) has to be answered in the wording. |
| 2c | Append a note to the first answer after an idle close (the report's proposal). | No lost turn and the most informative. It is a second exception to the byte-identical rule. |
| 2d | Close less: exempt headed launches (problem 3). | Removes the loss where a person is involved. Headless sessions keep it. |
| 2e | Say it on `browserai_resume` and `browserai_catch_up` only. | Inside the Q261 precedent. Does nothing for a caller in mid-work, which is the case observed. |

Recommendation: 2a, then 2b. 2c is the better experience for a model and is yours to grant as a named exception.

### Problem 3: a person using a headed window is not activity

Primer: a window opened for a hand sign-in closes ten minutes after the agent's last call, whatever the person is doing in it. Here the agent polled about every four minutes to prevent it.

| | Direction | Trade-off |
|---|---|---|
| 3a | Never arm the timer on a headed launch. | Matches upstream and the stated purpose of `headed`. A forgotten window holds its memory until the client exits, the caller closes it, or the session is destroyed. |
| 3b | A longer period for headed, such as upstream's hour. | A second number where the timer value is settled. Still a cliff. |
| 3c | Count window input or focus as activity. | BrowserAI cannot see page input without driving the browser, which the charter forbids, or a new window-watching mechanism. |
| 3d | Say when: put the deadline in the `headed` description and the answers. | Words only. The caller still has to poll. |
| 3e | A per-run "keep open" or "never". | Reopens the settled timer value and the test at `BrowserIdleTimerTests.cs` line 70. |

Recommendation: 3a, with 3d's sentence for headless.

### Problem 4: the timer closes a browser that is already closed and records that it closed one

Primer: the caller's own `browser_close` re-arms the timer. Ten minutes later the timer writes a row saying BrowserAI "closed this session's browser itself" and sends the close anyway (row 80).

By upstream's code, a browser is created before any tool call is dispatched (`coreBundle.js` 73306-73333, 73444, 75036-75052). So that close starts a browser in order to close it, which in a headed session would be a window appearing and vanishing. This is read, not measured. The supporting evidence is weak: row 80 took 604 ms against 210 to 319 ms for five of the six closes that found a browser up (the sixth took 644 ms), and the kb records 156 to 514 ms for a close with no browser open.

| | Direction | Trade-off |
|---|---|---|
| 4a | When the job holds only the node child, write no row and send nothing. | The count is already read (`LiveSession.cs` 436) and already trusted for the reap (line 110). |
| 4b | Do not re-arm on the caller's own `browser_close`. | A by-name branch. Misses other ways to be armed with no browser. |
| 4c | Keep sending; settle the row as "no browser was open". | Fixes the record, keeps the launch. |
| 4d | Leave it. | The record keeps a false row. |

Recommendation: 4a.

## 8. Not verified, and why

1. The original records: destroyed. `why` and `failure` text in the exports is not cross-checked.
2. Session cookies across an idle close: no measurement exists in this repository.
3. The launch-to-close reading in problem 4, and any window flash.
4. Why session A was signed out.
5. "No top-level window existed" after row 76: that was the agent's own check. The HeadlessChrome/154 user agent is what I can confirm.
6. Per-close process counts: that log line has gone to the session's stderr only since 2026-08-26, and Claude Code kept one stderr line per connection.
7. What the agent told you, beyond keywords and timestamps; and "at the maintainer's request".
8. Description lengths: estimated from source, not measured off the wire.

Measurements that would settle 2 and 3, and the capture overwrite under "Tangential" (not run):
- Drive a raw `@playwright/mcp` child the way kb line 970 describes, headless, in a scratch profile.
- Set one cookie with no expiry and one with `max-age`, close, navigate back, read both.
- Send `browser_close` twice, sampling processes under the browsers root during the second.
- With capture on: navigate, close, navigate, close, then compare the archive.

For problem 1 the red test is: init headless, resume with `headed: true` in the same rig, assert a second child whose config is not headless. Today it stops at "nothing was changed".

## Tangential

- **Stale "last used".** The held-session answer printed 12:01:10 at 12:13:49 and again at 12:35:56. The cached record refreshes on statements, not on log rows.
- **Stale durability comment.** `BrowserIdleTimer.cs` 16-19 still says a resume after killing the node child loses only sessionStorage. The 2026-09-22 measurement (`HAZARDS.md` 318) says Chromium also loses the cookie and localStorage.
- **Network capture across a browser relaunch (inferred).** The archive name is fixed per child launch (`BrowserConfiguration.cs` 622-639). By that comment's own premise, a browser relaunch inside one child overwrites the capture taken before it.
- **Reap skipped on next.** The reap after an idle close is skipped when the count is 1 (`LiveSession.cs` 105-113), on the premise that no browser was bound. The problem 4 reading contradicts that.
- **`browserai_catch_up` writes no row.** The brief said it does. The code appends nothing, though it can leave a `-shm` file beside a record whose holder died.
- **Working tree.** `git status` shows `drift-check.json` modified; that is the other agent's.

## Decisions for the maintainer

1. Problem 1: 1a plus 1c, or another of 1a to 1d?
2. Problem 2: after 2a, refuse once (2b) or grant a second exception to byte-identity (2c)?
3. Problem 3: exempt headed launches (3a)?
4. Problem 4: 4a?
5. Take the three measurements in section 8 before deciding 2 and 4?

## Supporting data

All in `C:\Source\SixFive7\BrowserAI\.work\e-field-report\`, with no `why` or `failure` text:
- `session-A-timeline.tsv` (83 rows)
- `session-B-timeline.tsv` (192 rows)
- `crosscheck-client-log-vs-exports.tsv`
- `code-references.tsv` (49 rows of file and line at v1.1.0 and next)
<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - lane q371: what a transcript holds, how arguments were handled, and how much of a refusal a client hands its model

**What this is.** Two measurements lane q371 took while it built the maintainer's
decisions of 2026-10-03 (Q371 c, the resume rule, Q365.4, the unknown-argument
and unknown-tool refusals, Q371.5 b, Q369.3 c with Q371.6 c, the catch_up `why`,
the instructions rewrite b, the `browserai_change_purpose` rename and Q365.2 a).

1. **`transcript/`: one session driven end to end through a published
   BrowserAI, before and after.** Headless Chromium against a page the driver
   served on `127.0.0.1` and nowhere else, with a user name and a password that
   exist only in the driver (`q371-sample-user`, `q371-typed-password-SAMPLE`)
   and a cookie the page set (`Q371-SAMPLE-COOKIE-VALUE`). No real site, no real
   credential, no window.
   - **Before**: 2026-10-03, 23:19:54Z to 23:20:01Z, BrowserAI 1.1.1-alpha.0.188
     published at `d8a0101a`, `@playwright/mcp` 0.0.83, the session started with
     `tracing: true`. The tool list carried 78 tools and the instructions were
     2,041 characters.
   - **After**: 2026-10-04, 00:10:55Z to 00:10:59Z, BrowserAI
     1.1.1-alpha.0.193 published from this lane's tree, the session started
     with `transcript: true` after the same call with `tracing: true` was
     refused. The tool list carried 73 tools in 110,386 characters and the
     instructions were 833 characters. One more tool was denied after this run,
     `browser_set_storage_state`, so a build of the final tree advertises 72.
   - The same calls both times: an argument no schema has, on `browserai_list`
     and on `browser_navigate`; a snapshot; `browser_start_tracing`, a second
     navigation while the trace recorded, two `browser_type` calls (the user name
     and the password), a snapshot, a `browser_click` on a reference that is not
     on the page, a screenshot, `browser_stop_tracing` and
     `browser_storage_state`; `browserai_catch_up`; and `browserai_destroy`. The
     after run also called `browserai_set_purpose`, `browser_verify_value` and
     an invented `browser_frobnicate`.

2. **`sizes/`: 74 runs of what Claude Code and Codex hand a model when a tool
   result is long.** 2026-10-03, 23:24Z to 23:34Z. A stub MCP server
   (`bigserver.js`) whose one tool answered with a text of a chosen length, made
   by repeating a tools array cut from BrowserAI's real `tools/list`
   (`template.json`) between a start marker and an end marker, as an error
   result (`isError: true`, the way a refusal is sent) or as an ordinary one.
   The clients were **Claude Code 2.1.288** through `claude -p` (`ccP`) and
   through the stream-json transport with the VS Code extension's environment
   (`ccS`), and **codex-cli 0.160.0** (`cx160`) and **0.155.0-alpha.9.2**
   (`cx155`) through `codex exec`, each with its configuration in a scratch
   folder and a local API stub (`ccmodel.js`, `cxmodel.js`, derived from lane
   q369's) recording what the model was sent. Never BrowserAI.

## What was found

| Finding | Where it is read |
|---|---|
| **Before, an argument no schema has was dropped without a word**, by an authored tool and by a forwarded one, and the call went ahead | `transcript/before-report.trimmed.json`, steps `authoredUnknown` and `forwardedUnknown` |
| **After, both are refused as a syntax error naming the argument, with the tool's whole definition**, and so is `tracing` on `browserai_init` | `transcript/after-report.trimmed.json`, steps `authoredUnknown`, `initWithOldName` and `forwardedUnknown` |
| **A name BrowserAI does not have is answered plainly**: the old `browserai_set_purpose`, a denied `browser_verify_value` and an invented name get the same sentence | `after-report.trimmed.json`, steps `oldPurposeTool`, `deniedTool` and `inventedTool` |
| **`session.md` holds each browser tool call that succeeded**, with its arguments as upstream received them (BrowserAI's own `session` and `why` are removed before a call is forwarded) and its parsed result: the page, the snapshot, the events, and the screenshot itself as base64 PNG. **The typed user name and password are in it as plain text. The failed `browser_click` is not in it.** It was `output\session-1791069599122\session.md` before and `output\session-1791072657892\session.md` after: one folder per run, named for the time in milliseconds | `transcript/session-before.md.txt`, `transcript/session-after.md.txt`, and `secretsByFile` in both reports |
| **The trace's action log (`.trace`) holds the typed text too, and its network log (`.network`) holds the cookie** in the request that sent it back. Before, no navigation happened while the trace recorded and the network log was 0 bytes; after, it was 2,394 bytes and carried the cookie | `secretsByFile` in both reports |
| **The saved login `browser_storage_state` wrote was 278 bytes for one origin and one cookie**, both times, and it carries the cookie's value | `secretsByFile` in both reports |
| **After, `browserai_catch_up` named the saved login, the trace folder and the transcript**, each as login data in clear text | `after-report.trimmed.json`, step `catchUp` |
| **Claude Code hands a model an error result of up to 10,100 characters whole, and cuts one of 12,000 or more to 10,039 to 10,041**: the first and the last 5,000 or so, with `... [N characters truncated] ...` between them. 9 runs whole and 18 cut, through both transports | `sizes/runs/*/results.json`; the marker in `sizes/runs/limits1/ccP-12000-r1/model.seen.jsonl` and `limits2/ccS-12000-r1/model.seen.jsonl` |
| **An ordinary result reaches a Claude Code model whole up to 50,000 characters**, and at 60,000 and 120,000 it is replaced by a file and a preview of about 2,000 characters inside `<persisted-output>` | `results.json` rows `ccP-ok-*` and `ccS-ok-*`; `sizes/runs/limits1/ccP-ok-60000-r1/model.seen.jsonl` |
| **Codex hands a model up to 10,100 characters whole, error or not, and cuts 12,000 or more to 12,018 including its own 34-character prefix**, keeping both ends with a marker that counts tokens, the same at 0.155.0-alpha.9.2 and 0.160.0 | `results.json` rows `cx160-*` and `cx155-*`; `sizes/runs/limits1/cx160-20000-r1/model.seen.jsonl` |

**So a refusal that appended BrowserAI's whole current tool list could not reach
a model whole through either client.** The list was 110,386 characters on
2026-10-04 with 73 tools, and the largest refusal a model is handed whole is
about 10,000 characters through Claude Code and about 12,000 through Codex. That
is why the list was not appended (Q369.3 c and Q371.6 c are stopped and
reported, as the maintainer asked for exactly this case), and why
`ClientTruncationBudget.ErrorResultCharacters` is 10,000.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: configuration](../../../kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like) | What `session.md` holds, measured |
| [kb: protocol](../../../kb/mcp/protocol.md) | How much of a tool result each client hands its model, and what an argument no schema has did before and does now |
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md) | Which files in a session's output hold login data in clear text |
| [kb: re-verification](../../../kb/re-verification.md) | The rows those three entries add |
| [`DECISIONS.md`](../../../DECISIONS.md#the-zoom-out-of-2026-09-25-and-what-followed-it) | The rows on the transcript, on arguments and names BrowserAI does not have, and on the tool list a refusal could not carry |
| `ClientTruncationBudget.ErrorResultCharacters`, `UnrecognisedArgumentTests`, `ToolSignatures`, `SessionInventory`, `SessionToolSurface` | The budget a refusal is held to, the before measurement, and the facts the transcript description and catch_up's lines rest on |

## What is here

| Path | What it holds |
|---|---|
| `transcript/drive.cjs.txt` | The driver as it ran, `node drive.cjs <exe> <scratch> <before or after> <report>` |
| `transcript/before-report.trimmed.json`, `transcript/after-report.trimmed.json` | Every step's arguments, error flag, text and time; the instructions; the whole tool list; the output folder listing; and which files carried the password, the user name and the cookie |
| `transcript/session-before.md.txt`, `transcript/session-after.md.txt` | The two transcripts, whole, under a `.txt` suffix so the repository's header rule for Markdown does not touch their bytes |
| `sizes/rig/` | The stub server, the batch driver, the two model stubs and the tools array, as they ran |
| `sizes/runs/<batch>/results.json`, `progress.log`, `policy.json` | Every run's length served, length received, error flag, and the first and last 300 characters the model was sent |
| `sizes/runs/<batch>/<run>/` | For four runs, what the model was sent (`model.seen.jsonl`) and what the stub served (`big.log`) |
| `originals.sha256`, `cut_q371.py` | Every original's digest beside the name it is stored under, and the script that cut this batch |

**Re-establish** the transcript half by publishing BrowserAI and running
`drive.cjs` with a scratch folder; it serves its own page and needs nothing
else. The sizes half needs the two clients' binaries and their configuration in a
scratch folder: `node sizes.js <batch> <clients> <sizes> <repeats>` starts both
stubs and writes `results.json`.

## What was cut

The user-profile path in the two reports, replaced by `%USERPROFILE%`; those two
are stored under `.trimmed.` names, with the originals' digests in
`originals.sha256`. The rig's scripts and the two transcripts are stored with a
`.txt` suffix and are otherwise the files as they were written. **Left out by directory**: each of the other 70
size runs' own folders (the clients' scratch configuration, their logs and every
request body), the client binaries, and both sessions, which `browserai_destroy`
removed at the end of each run.

## Privacy

Nothing here names the user, the user profile or the machine. The password, the
user name and the cookie value in the transcripts and reports are the driver's
own sample values and sign in to nothing.

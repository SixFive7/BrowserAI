<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 - lane q371: which files in a session hold something sensitive, and the size of the refusals that name every tool

**What this is.** Two measurements lane q371 took on 2026-10-04 for the maintainer's
answers of that day, in his words: *"1 a / 2 b / 3 a / 4 a - is there not also
sessions.md or other logs? Name everythign sensitive."*

1. **`sensitive/`: every file in a session, searched for sample values (4 a).**
   One headless session per family, Chromium 1247 and Firefox 1553, driven through
   BrowserAI 1.1.1-alpha.0.211 published from the lane's tree at `@playwright/mcp`
   0.0.83, against pages the driver served on `127.0.0.1` and nowhere else, from
   15:26:59Z to 15:27:13Z. The page set a cookie, wrote local and session storage
   and a console message, fetched an address with a token in its query and a
   bearer token in its headers, and offered a download; the driver typed a user
   name, a password and a note. Every value is the driver's own and signs in to
   nothing. The session was started with `transcript` and `captureNetwork`, and the
   driver called every tool that writes a file: snapshots, the console and network
   tools with `filename`, a request's headers and a response body, an evaluation
   saved to a file, a screenshot, a PDF (Chromium), a video, a trace, a download and
   a saved login. After `browser_close` it read every file under the session, the
   profile included, for each value as text and as UTF-16, and inside Firefox's
   compressed session store, and then called `browserai_catch_up`.
2. **`refusal-sizes/`: the two refusals that name every tool (2 b)**, read off the
   build that made them, 1.1.1-alpha.0.206, at 14:02:18Z: a call before any tool
   list, from a client calling itself `claude-code`, and a call naming a tool
   BrowserAI does not have. No browser was started.

## What was found

| File | Chromium | Firefox |
|---|---|---|
| Page snapshots, `page-<moment>.yml` and one saved by name, after typing | user name, **password**, note, page text | the same |
| The transcript, `session-<milliseconds>\session.md` | user name, **password**, note, page text, the token in the address | the same |
| The trace's action log, `.trace` | user name, **password**, note, local storage, console, the API's answer, page text, the token | the same |
| The trace's network log, `.network` | the token | **the cookie** and the token |
| The trace's saved resources | | the download |
| The HTTP Archive | **the cookie**, **the bearer token**, the API's answer, page text, storage values, the token | the same |
| The saved login, `storage-state-<moment>.json` | **the cookie**, local storage | the same |
| A request's headers saved by name | **the bearer token**, the token | **the cookie**, **the bearer token**, the token |
| A response body, an evaluation and the download, saved by name | the API's answer, local storage, the download | the same |
| The console log, `console-<moment>.log`, and the console and network lists saved by name | the console message, the token | the same |
| The record, `browserai.data` and its journal | the `why` | the same |
| The profile | the cache (page, API answer, bearer token, download), the history (the token), local and session storage, and the session restore files with the **typed user name and note and not the password**. The cookie store is encrypted | the cache (page, **cookie**, bearer token), `cookies.sqlite` (**the cookie, in the clear**), the history, local storage, and `sessionstore.jsonlz4` with the typed user name and note and not the password |

Nothing in `downloads\` was left after the browser closed in either family:
Playwright keeps a download there while the browser is up, and the driver's copy
was saved into `output\`. The screenshot, the PDF and the video held no sample value
as text, which says nothing about what they show.

**What `browserai_catch_up` said about it**, in the build of the same day, is the
`catchUp` field of each report: one line per kind, every file above named, and the
profile and the trace each named whole.

**The two refusals**, with 72 tools in the list: 5,454 characters for the tool
BrowserAI does not have, and 6,002 for the call before any list, in Claude Code's
wording.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#tools-that-reach-credentials) | Which files hold what |
| [kb: protocol](../../../kb/mcp/protocol.md#how-much-of-a-tool-result-each-client-hands-its-model----measured-2026-10-03) | The two refusals' sizes |
| [`DECISIONS.md`](../../../DECISIONS.md#the-zoom-out-of-2026-09-25-and-what-followed-it) | The rows on catch_up's files and on the refusals that name every tool |
| `SessionInventory`, `SessionManager.Sensitive`, `CatchUpTests` | The kinds, their wording and the arm that holds them |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in the three reports, which are
stored under `.trimmed.` names with the originals' digests in `originals.sha256`.
The two drivers are stored with a `.txt` suffix and are the files that ran, with
this repository's line endings. `cut_sensitive.py` is the script that cut them. **Left out:** both sessions,
which `browserai_destroy` removed at the end of each run.

**One driver step failed and is kept as it ran**: `browser_find` was called with an
argument named `query`, which its schema does not have, and the refusal the lane
built answered it. Nothing else in the runs depends on it.

## Privacy

Nothing here names the user, the user profile or the machine. Every sample value
is the driver's own.

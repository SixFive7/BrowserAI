<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- a picture of a headless session, taken beside an agent's call

Establishes
[A picture of a headless session beside an agent's call](../../../kb/playwright/tools-and-artifacts.md#a-picture-of-a-headless-session-beside-an-agents-call----measured-2026-10-03).
Evidence:
[`docs/evidence/2026-10-03-look/`](../../evidence/2026-10-03-look/README.md).

**Why it exists.** Q317 c, the maintainer's words verbatim: *"Q317 c"*. The
sessions page was to show a view-only picture of a headless session on request,
taken by the process that holds the session, after three measurements: the
picture's effect on an agent call that is running, whether a picture written
over one file leaves anything else behind, and what one picture costs on
Chromium and on Firefox.

## What is here

| File | What it does |
|---|---|
| `look-rig.mjs` | Starts the published `BrowserAI.Server.exe` over stdio, lists its tools the way a client does, opens a headless session of one family, and serves three pages of its own on `127.0.0.1`: one with rows to draw, one that answers four seconds late, and a file to download. Then: takes 22 pictures with `browser_take_screenshot` and one fixed file name, keeping the last 20 for the cost, and lists the session's folder, its browser profile left out, before and after them; runs four agent calls, each alone and then with a picture taken a second into it; runs an agent call during which the page logs to the console, retitles itself and starts a download, alone and with a picture a second into it; and schedules the same events between two agent calls, with and without a picture between them. For every answer it records which of the page, console and download reports the answer carried. Writes `results-<family>.json` and prints a summary |

## What keeps it off the rest of the machine

- The browser is headless and is the session's own, provisioned in the shared
  browsers root the suite uses, so a run takes the suite lock like any run that
  starts the server. The pages it serves are on `127.0.0.1` alone.
- It starts the server it was given by path, with `windowsHide`, and nothing
  else. **It selects no process at all**: none of the six literal spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on is in the file, by a search that found one in
  `2026-09-14-firstrun/observe.ps1`, the positive control. One line compares a
  `name` with `!==`, and it is the name of an entry in a directory listing.
- The session is destroyed at the end; its directory, with the pictures and the
  downloads, is under the scratch folder the run was given.

## Running it

With the payload's own `node.exe`, under the suite lock, one run per family:

```
node look-rig.mjs <published BrowserAI.Server.exe> <scratch folder> chromium 3
node look-rig.mjs <published BrowserAI.Server.exe> <scratch folder> firefox 3
```

Snapshot `HKCU\Software\Mozilla\Firefox\Launcher` before the Firefox run and
compare it after. A run is a new measurement with a date of its own.

<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-12 -- a Firefox start that waited out Playwright's 180 s

Cited by [the hazard index](../../../HAZARDS.md), the row of a Firefox session start that
waited the full 180 s, which the maintainer's 125 b of 2026-10-12 opened in his words,
*"125 your recommendation"*. Seen twice that day while the home's screenshots were taken
through the installed BrowserAI into headless Firefox 156.0 (`firefox-1553`), each in a
session under `.work\dash-home-scratch` whose page a scratch rig served on `127.0.0.1`.
**Nothing here was measured for it**: these are the two calls that failed and their
answers, as BrowserAI returned them to the client that made them.

| File | What it is |
|---|---|
| `first-call.json`, `first-answer.trimmed.txt` | Session `home-shots`, made at 15:09:04Z, which opened a rig's home on port 53610 at 15:09:09Z. BrowserAI closed its browser at 15:19:25Z after ten minutes with no call, that rig ended, and the session was resumed at 15:28:22Z. The first browser call after it, a navigation to a second rig on port 59327 at 15:28:26.750Z, started Firefox and was answered at 15:31:26.787Z: `TimeoutError: async initializeServer: Timeout 180000ms exceeded.` The call log's last line is Firefox's jump list failing to fetch a favicon for the first rig's address, port 53610 |
| `second-call.json`, `second-answer.trimmed.txt` | Session `home-shots-2`, made at 15:31:49Z, which opened the second rig's home at 15:31:54Z; BrowserAI closed its browser at 15:45:22Z after ten minutes with no call, and that rig ended. Resumed at 17:33:38Z; the first browser call, a colour-scheme emulation at 17:33:41.788Z, started Firefox and was answered at 17:36:41.820Z with the same timeout. The call log's last line is the same favicon fetch, for the second rig's address, port 59327 |

**What is common to the two, and what is not established.** Each start was the first after a
resume of a session whose last page was on a `127.0.0.1` port nothing listened on any more,
and each call log names that page's address. Two fresh sessions made in the same minutes
started Firefox and answered their first browser call within 3 s: `home-shots-2`, asked at
15:31:54.714Z and answered at 15:31:57.662Z, and `home-shots-3`, asked at 17:36:58.182Z and
answered at 17:37:00.163Z. That the tab Firefox restores is what holds the start is a
suspicion the two logs support and do not show; nothing was run to separate it from the
profile's other state.

**How these bytes depart from the ones taken.** Each answer is the tool result's text with
its terminal colour escapes cut, `ESC [2m` and `ESC [22m` on every line of the call log,
which the repository's text may not carry; it is stored under a `.trimmed.` name, and
`originals.sha256` gives each original's SHA-256 and size. Nothing else in them was changed.
Each call is the request's tool name, its arguments and the two times, cut from the record of
the session that made them. Line endings are what `.gitattributes` normalises. Both sessions
were destroyed the same day, with their profiles and BrowserAI's own log of them.

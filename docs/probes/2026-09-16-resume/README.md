<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-16 - what a resume costs and what it preserves

The re-establishment named by
[Resume costs 336 ms and 367 ms](../../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead),
which is [re-verification row 38](../../../kb/re-verification.md).

`mcp.js` is a minimal newline-delimited JSON-RPC client for a real
`BrowserAI.Server.exe` over stdio — the framing `StdioChannel` owns.
`page-server.js` serves a one-page origin on `127.0.0.1`, which is a
potentially-trustworthy origin and therefore one a service worker will register
against; a `data:` URL has no storage at all and cannot be used here.

**Two probes, and the difference between them IS the finding.**

- `resume-probe2.js` is the one the measurement comes from. Server A creates the
  session, fills cookie, `localStorage`, `sessionStorage`, IndexedDB,
  CacheStorage and a service worker, and confirms all six are set. A closes its
  stdin, its node children are confirmed **gone by pid against a path BrowserAI
  owns**, and then **server B** — a different process — resumes the directory.
  The resume is timed at the client, and every store is read back.
- `resume-probe.js` is the one-server shape the old procedure literally
  described: kill the node child under a **live** BrowserAI and resume in the
  same process. **It does not produce a resume.** `browserai_resume` answers
  *"This session is already open in this BrowserAI; nothing was changed"* in
  7.8 ms, and the next browser call against that session had not returned after
  3 min 8 s. It is kept because a procedure that reads plausibly and does not
  work is worth being able to re-run.

## What it touches

The session directory it is given, and the product's shared data root — the
session index and live markers — which is the same state the suite drives. It
destroys its session through `browserai_destroy` on the way out, so it leaves no
index entry. It never touches `%LocalAppData%\BrowserAI.app`.

⚠️ **`resume-probe.js` wedges a session and leaves it wedged.** Recovering
needs the server killed by pid and the session destroyed by a later process;
both are in the transcript of the 2026-09-16 run.

| Trips `NeverByImageNameTests` | Yes — `Win32_Process` and `Get-Process`, both keyed on a pid and filtered on an executable path BrowserAI owns, never on a name |
|---|---|

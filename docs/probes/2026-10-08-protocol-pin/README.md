<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- the opening Claude Code sends, and what it did to the installed 1.1.0

Re-establishes [the kb entry](../../../kb/mcp/protocol.md#the-new-opening-request-and-the-one-revision-browserai-offers----measured-2026-10-08)
and re-verification row 198.

| File | What it does |
|---|---|
| `count.py` | Counts Claude Code's connections to the installed BrowserAI from the client's own MCP logs, `%LOCALAPPDATA%\claude-cli-nodejs\Cache\*\mcp-logs-browserai\*.jsonl`, in a window given as two UTC timestamps: how many, in how many project folders, and each one's outcome, server version, era and negotiated revision |
| `clientver.py` | Maps each of those connections to the Claude Code version its session transcript under `~\.claude\projects` records at that time |
| `opening.py` | Sends a BrowserAI server the two openings Claude Code sends since 2026-09-30, `server/discover` at `2026-07-28` and then `initialize` at `2025-11-25` with `tools/list`, and prints what came back |

⚠️ **The two counting scripts read the person's own logs and transcripts and
write nothing.** They print counts only and never a project folder's name,
because those folders name the person's own projects, and nothing they print may
go into this repository with a name in it.

**What they printed on 2026-10-08**, `count.py` over three windows:

| Window | Connections | Folders | Outcome |
|---|--:|--:|---|
| 2026-09-16T00:00Z to 2026-09-30T10:52Z | 443 | 13 | 441 worked and 2 failed otherwise, every one opened legacy at `2025-11-25` |
| 2026-09-30T10:52Z to 2026-10-04T18:00Z | 153 | 12 | 145 failed with the `resultType` error, every one having negotiated `2026-07-28`; 2 failed otherwise, also at `2026-07-28`; 6 worked, opened legacy at `2025-11-25` |
| 2026-10-04T18:13Z to 2026-10-08T14:00Z | 60 | 8 | 55 worked, opened legacy at `2025-11-25`; 5 failed otherwise, all while the client was shutting down |

One log of the second window holds both halves: its connection failed at
13:36Z, and the same session reconnected at 18:13:44Z, a minute after the
stopgap was installed, opened legacy and served every call it made. `clientver.py` over
the second window matched 73 of the 153 to a transcript: 68 name 2.1.285 to
2.1.289, five carry older stamps, and 80 have no transcript left.

`opening.py` against the published binary of the tree that pinned the
revision, under the suite lock:

```
server/discover: {"error": {"code": -32022, "message": "Unsupported protocol version '2026-07-28'.", "data": {"supported": ["2025-11-25"], "requested": "2026-07-28"}}, "id": 1, "jsonrpc": "2.0"}
initialize protocolVersion: 2025-11-25 error: None
tools/list: 72 tools; error: None
exit 0
```

⚠️ **`opening.py` starts a BrowserAI server**, which starts its own child and
sweeps the shared app root for strays, so on this machine it runs under the
suite lock. It starts no browser.

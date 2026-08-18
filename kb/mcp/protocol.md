<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The MCP protocol, and the client at the other end

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · `ModelContextProtocol` 2.2.0 · MCP protocol revision `2025-11-25` · Windows 11 Pro 26200.
Measured on [the reference machine](../README.md#the-reference-machine).

## The protocol split

**`@playwright/mcp` 0.0.79 caps at protocol `2025-11-25`.** The child never
*rejects* a version — it caps or echoes silently: verified, offering
`1999-01-01` returned `2025-11-25` with no error, so a mis-negotiation produces
nothing to catch and the negotiated value must be asserted. `[FLOATS]`

> `Verified 2026-08-16 @ @playwright/mcp 0.0.79 / playwright-core
> 1.63.0-alpha-2026-08-05.` Re-measured from the other side as well as the
> original one: offering the deliberately-future `2999-01-01` returned
> `2025-11-25`, and offering `2025-06-18` returned `2025-06-18` — so it caps a
> newer revision and echoes an older one, and does neither with an error. Both
> probes are now part of the snapshot generator, so the ceiling is recorded in
> [`upstream-snapshots/tools-list.json`](../../upstream-snapshots/tools-list.json)
> and a move is a diff.
>
> **The product half landed 2026-08-16, with the first published-AOT vertical
> slice.**
> `BrowserProxy.ConnectAsync` pins `McpClientOptions.ProtocolVersion`, logs
> `requested=… negotiated=…`, and throws if the two differ; `ProtocolSplitTests`
> asserts the logged pair against the ceiling the snapshot recorded, so the pin
> and the measurement can no longer drift apart silently.

**The two halves of the split are distinguishable by a method, not only by a
version string.** Measured 2026-08-16 by sending `server/discover` as the first
frame to each end of the running proxy: **BrowserAI answers `-32602`** — *"The
`server/discover` request requires per-request metadata declaring a supported
protocol version"* — while **the child answers `-32601` Method not found**. The
method exists on one side and not on the other, which a version string cannot
show, because a version can be echoed. Re-establish by running
`ProtocolSplitTests.TheServerReachesARevisionTheChildDoesNotImplement`, which
asks both ends the same question in the same run. `[FLOATS]`

> The same run also confirms the downward pin is independent of the caller:
> offering `2025-06-18` to BrowserAI returns `2025-06-18` while the child
> session in that same process is at `2025-11-25`. A server that merely
> forwarded the child's answer would return `2025-11-25` there.

**The current spec is `2026-07-28`, a breaking rewrite.** It removes `initialize`
and `notifications/initialized`, adds `server/discover`, replaces server→client
requests with the MRTR retry pattern, and deprecates Roots, Sampling and Logging.
**SEP-2567 removed protocol-level sessions outright**, and *Tools § Capabilities*
states the tool set "MAY change over time … but MUST NOT vary per-connection or
as a side effect of other requests on the connection." `ping` was removed at
`2026-07-28`. SEP-2567 also names `destroy_*` and `list_*` as the documented
companions to a creation tool. `[STABLE]` — a published revision does not move.

**The .NET SDK implements every revision from `2024-11-05` through `2026-07-28`**
and shipped `2026-07-28` support on the spec's release date. `[FLOATS]`

**`DiscoverProbeTimeout` is 5 seconds by default.** With the client version left
unpinned, the client probes the child with `server/discover` first; if the child
drops the unknown method rather than answering, **every child spawn costs a flat
5 s against a ~300 ms baseline**, presenting as "browser automation got slow"
with no error anywhere. The SDK's own test base class pins it explicitly, citing
[csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701)
— CI slowness tripped the probe there. `[FLOATS]`

> **Asserted rather than remembered since 2026-08-16.**
> `FakeChildHarnessTests.TheClientPinIsWhatSkipsTheDiscoverProbe` reads the 5 s
> default off `McpClientOptions` and then proves the mechanism from three sides
> against an in-process double: pinned, **no** `server/discover` is sent;
> unpinned, it is; unpinned against a double that drops the method, the connect
> pays the whole timeout. Our `TestDefaults` pins the probe **short** (250 ms),
> the opposite of upstream's fixtures, because every peer in that layer is a
> double that answers instantly — so a probe running to its timeout is a defect
> to surface fast rather than latency to tolerate. That is also why the row's
> wall-clock half is now [row 16a](../re-verification.md) and stays
> manual: a deliberately short pin cannot measure the ~300 ms production
> baseline.

## The client: Claude Code

`[FLOATS]` on a client version this project does not control.

**Tool names and server `instructions` load eagerly; schemas are deferred.** So
`instructions` is the only channel that reaches the model before it calls
anything.

**Server `instructions` and every tool description are truncated silently at
2 KB.** The tail simply does not exist and nothing a server can see reports it.
[What *"2KB each"* means is measured below](#what-2kb-each-means--measured-2026-08-18--claude-code-21234).

> **What BrowserAI actually spends of that, re-measured 2026-08-18 off the
> shipped binary's own `initialize` response: 1,261 characters and `1,276`
> bytes, leaving 772.** The three mode lines cost **106, 121 and 92 bytes**
> apiece, plus a newline each, measured from the same emitted string.
>
> ⚠️ **Corrected 2026-08-18 (previously "measured 2026-08-16 at build-order step
> 13: 1,613 characters and `1,628` bytes, leaving 420 … the difference is almost
> entirely the mode lines carrying what each mode *refuses* as well as what it
> grants — which is the half a model needs to choose correctly … **Planting a
> fourth mode measured its cost at 223 bytes**, so the headroom absorbs exactly
> one more mode and a fifth would need the lines shortened").** The `refuses`
> half was rendered from the `(tool, mode)` permission policy, and that policy
> was removed — it was never a boundary against the caller, who chooses the
> session directory and reads the profile inside it as the same Windows user. So
> the string lost 352 bytes and the mode lines now say only what a mode **is**.
> **The 223-byte figure is not carried forward**: it was measured against a line
> shape that no longer exists, and an adjusted number is indistinguishable from a
> measured one. What replaces it is the per-line costs above, taken from the
> emitted string, which is the same question asked in a form anybody can re-ask.
>
> Re-establish by running
> `ModelSurfaceTests.TheInstructionsStringFitsTheClientsSilentTruncationBudget`,
> which measures in **characters**, because that is the unit the client counts.
> *Corrected 2026-08-18 (previously "which measures in **bytes**: the string
> carries `·` (2 bytes) and `—` (3 bytes), so a character count under-reports
> precisely the string that uses them").* The byte figure is still printed and is
> still the larger of the two; it is simply not the one that is capped — see the
> measurement below. `[FLOATS]` on our own wording rather than on a client
> version.

### What *"2KB each"* means — measured 2026-08-18 @ Claude Code 2.1.234

The documented sentence is *"Claude Code truncates tool descriptions and server
instructions at 2KB each"*, and **"each" does not say each what.** Two readings
were live: per string, or per whole serialized tool — name, description and the
entire `inputSchema` in one bucket. This project read it per string and
[said in the constant that it was an assumption](../../src/BrowserAI/Proxy/ClientTruncationBudget.cs);
a maintainer of another MCP server had reported trimming a description and fixing
nothing, which is what the per-tool reading predicts. **It is per string.**
`[FLOATS]` on a client version this project does not control.

**Method — the client's own outbound request, never a model's recollection.**
Claude Code honours `ANTHROPIC_BASE_URL`, so it was pointed at a local HTTP
server on `127.0.0.1` that records the request body and answers with a minimal
SSE stream; `ANTHROPIC_AUTH_TOKEN` was set to a throwaway string, so no real
credential and no real API call was involved and the experiment costs nothing.
A probe MCP stdio server published strings of exact length carrying unique
end-markers, registered with `claude mcp add --scope user` against a **scratch
`CLAUDE_CONFIG_DIR`**. The `tools` array in the captured
`POST /v1/messages?beta=true` body is then byte-for-byte what the model receives,
so every figure below is read rather than inferred. **Reproduced twice, against
`sonnet` and `haiku`, identical both times** — the cut is client-side and
model-independent.

| Question | Answer | The probe that settles it |
|---|---|---|
| Per string, or per serialized tool? | **Per string.** There is no per-tool bucket | A tool whose whole entry was **4,578 B** — a 1,500-character description plus four 700-character parameter descriptions, every string under the cap — arrived **intact**. Entries of **17,411 B** and **20,172 B** arrived intact too |
| Bytes, or characters? | **UTF-16 characters. A byte count is never consulted** | A 2,048-character description weighing **6,004 bytes** of em dashes arrived **whole**, while a 2,600-character ASCII one was cut |
| Where exactly? | The predicate is **`length > 2048`**, and the cut is to 2,048 | 2,047 intact · 2,048 intact · 2,049 cut, published as a triple in one run |
| Code units, or code points? | **UTF-16 code units**, and the cut is surrogate-aware | 1,539 code points spread over 3,000 units was cut. Where unit 2,048 would split a surrogate pair the cut backs off to **2,047**, and the delivered string is well-formed |
| Are `inputSchema.properties[*].description` strings truncated? | **No. Not at all** | A parameter description of **20,000** characters arrived whole, on a tool whose own description was 39 characters |
| Is there a total budget across `tools/list`? | **No** | **202 tools totalling 348,314 B** of tool entries went in one request (body 392,983 B): nothing dropped, nothing cut, every end-marker present |
| What does truncation look like? | A hard positional cut with **`… [truncated]`** appended — U+2026, a space, `[truncated]`; 13 characters — so a truncated string arrives at **2,061** | Every cut string in every run ended in exactly that, and the surviving prefix was identical to the published one |

⚠️ **The suffix is visible to the model and invisible to the server.** It is added
after the JSON-RPC response has left the server, so nothing a server can observe
reports it — **a server cannot detect its own truncation**, which is why the gate
is a build failure rather than a run-time check. A *model* can see it, so
*"did this arrive whole?"* is answerable by asking and unanswerable by logging.

**Server `instructions` are capped the same way and delivered somewhere else than
the obvious place.** They arrive inside a `<system-reminder>` block in the
**`messages`** array, under a `## <server-name>` heading alongside every other
connected server's, rather than in the `system` prompt — cut at 2,048 characters
with the same suffix. A 2,600-character probe `instructions` string lost
everything past 2,048.

**What it means for BrowserAI: nothing is truncated, and nothing ever was.**
Measured against the same capture with the shipped binary registered, the surface
sends **65 tools totalling 51,149 B** of tool entries and not one of them is cut.
The largest description is `browserai_init` at **1,623 characters** of 2,048; its
whole entry is **3,360 B**, which is over 2,048 and irrelevant, because the bucket
it would have overflowed does not exist. `instructions` is **1,261 characters**.
*This retires the standing worry that `browserai_init` was silently truncated on
every session.*

**Re-establish it** in about fifteen minutes, with no cost and no credential:

1. Write a capture server: an HTTP listener on `127.0.0.1` that writes each
   request body to a file and replies to `/v1/messages` with a minimal
   `message_start` … `message_stop` SSE sequence, and to `count_tokens` with
   `{"input_tokens":1}`. Redact `authorization` before writing anything.
2. Write a probe MCP stdio server — raw JSON-RPC over stdin/stdout answering
   `initialize`, `tools/list` and `tools/call` — publishing descriptions of exact
   length whose final token is a unique marker, at sizes straddling 2,048.
3. `claude mcp add <name> --scope user -- <node> <probe.js>` with
   `CLAUDE_CONFIG_DIR` pointed at a scratch directory. **Never the operator's
   own.**
4. Run `claude -p "Reply with the single word OK."` with
   `ANTHROPIC_BASE_URL=http://127.0.0.1:<port>` and a throwaway
   `ANTHROPIC_AUTH_TOKEN`. `ANTHROPIC_API_KEY` does **not** work — the client
   reports *"Not logged in"* against an unapproved key, and the bearer-token
   variable is the one that bypasses that.
5. Read `tools` out of the largest captured body and diff each string against
   what the probe published.

The scripts as run live in `.work/truncation/` on the machine that ran them and
are deliberately untracked — they are scratch, not product. The complete recipe,
written for a project that has never heard of BrowserAI, is in
`.work/truncation-prompt-for-sibling-project.md`.

**`notifications/tools/list_changed` handling changed, and the charter's citation
is stale.** *"Claude Code registers no handler"* was accurate at **2.0.65**
(Dec 2025) — issues
[#13646](https://github.com/anthropics/claude-code/issues/13646) and
[#4118](https://github.com/anthropics/claude-code/issues/4118). At **2.1.231 it
is false**: measured twice, the client re-listed in **1–2 ms** and the model
called a tool that appeared only in the second list. This does **not** unlock a
per-connection tool list — SEP-2567 stands — but the cited issues need re-dating.

## Registering BrowserAI with the client

Measured 2026-08-16 @ **Claude Code 2.1.233** (`claude.exe`, native install at
`%USERPROFILE%\.local\bin`), while building
[BrowserAI's own client registration](../../ARCHITECTURE.md#the-mcp-server). Every run below wrote into a
**scratch `CLAUDE_CONFIG_DIR`**, never the operator's real configuration.
`[FLOATS]` on a client version this project does not control.

**`claude mcp add --scope user` writes `mcpServers.<name>` into
`$CLAUDE_CONFIG_DIR\.claude.json`** — one entry, `{type, command, args, env}` —
and prints the file it modified. Unset, that directory is `%USERPROFILE%`. The
override is what makes a real registration testable without touching the file
the operator's own client is using.

**No elevation.** Both `add` and `remove` succeeded from a **non-elevated,
non-administrator** token (`WindowsPrincipal.IsInRole(Administrator)` = false) —
they write the invoking user's own file. Worth stating because
[the logon-task assumption was wrong the same way](../windows/detection.md#the-logon-sweep-task):
`schtasks` and the Task Scheduler COM API both answer `Access is denied` from
that same token, so *"a per-user operation needs no elevation"* is not something
this machine grants for free.

**Timings, three runs each:** `add` **613 / 645 / 636 ms**, `remove` **671 / 668 /
646 ms**. Against the fast-exit hook budgets — `--veloapp-install` 30 s,
`--veloapp-updated` 15 s, `--veloapp-uninstall` 60 s
([kb](../packaging/velopack.md#nativeaot-hooks-and-vpk-output)) — that is 15×
headroom on the tightest one. `[MACHINE]`

⚠️ **`add` is not idempotent, and every failure it has exits 1.** A second `add`
of the same name exits **1** printing *"MCP server browserai already exists in
user config"*; a `remove` of a name that is not there exits **1** printing *"No
MCP server named \"browserai\" in user scope"*. There is no exit code that
distinguishes either from a real failure, so **the words are the only
discriminator there is** — which is why `McpClientRegistration` matches on them
and why `RegistrationTests.TheClientStillSaysWhatTheExitCodesCannot` asserts both
against the real client on every run that has one. Getting it wrong is safe in
one direction only: an unrecognised wording reports the pass as *failed*, which
is loud, rather than reporting a registration that did not happen as done.

**`claude mcp get` starts the server.** It health-checks, so it reported
*"✘ Failed to connect"* for a path that does not exist — **and still exited 0**,
which is the `list`/`get` behaviour already recorded below. It is therefore
unusable as a presence check inside an install hook: it is slow, it has a side
effect, and against a real registration it would spawn BrowserAI from inside
BrowserAI's own installer.

### The whole lane, against a real installer — 2026-08-16

`Setup.exe --silent --installto <scratch>` at 0.9.0, updated to 0.9.1, rolled
back to 0.9.0, uninstalled. `CLAUDE_CONFIG_DIR` was pointed at a scratch
directory for every process in the chain, and the operator's real
`~\.claude.json` was **SHA-256-identical before and after** the whole run. That
hash comparison is the assertion, not a courtesy: an installer that registered
itself into the wrong file would otherwise pass every test in this table.
`[MACHINE]`

| Step | What the registration did | Time in the hook |
|---|---|---|
| Install 0.9.0 | `Registered`, command = `<root>\current\BrowserAI.exe` | **1.41 s** of a 30 s budget |
| Stub vs. registered binary | **392,704 b** at the root against **17,911,808 b** in `current\` — the registered path is the second | — |
| Update 0.9.0 → 0.9.1 | `AlreadyRegistered`; the entry and its path **unchanged** | **0.67 s** of a 15 s budget |
| Rollback 0.9.1 → 0.9.0 (`rollback=True deltas=0`) | `AlreadyRegistered`; unchanged again | 0.67 s |
| Uninstall | `mcpServers` is `{}` | whole uninstall **1.78 s** of a 60 s budget |

**Why an update changes nothing is the finding, not an omission.** The registered
path is `<root>\current\BrowserAI.exe`; an update replaces that directory
wholesale and the path is identical either side, so there is nothing to correct —
and the client's configuration lives outside the install root entirely, where no
update can reach it. The hook still runs, and what it buys is the *repair* case: a
registration somebody removed comes back.

**A hook must write its own log inside itself.** `VelopackApp.Run()` exits the
process once it has served a hook, so anything buffered for later replay is
discarded — `Program.Main`'s replay of Velopack's own records can never run on a
hook path. Confirmed by reading `<root>\logs\browserai-*.log` after the install:
all three registration records are on disk, written by the hook's own pid.

⚠️ **A clientless machine could not be simulated and the gap is named.** The
fallback directory resolves from the process token rather than from
`%USERPROFILE%`, so it cannot be redirected
([kb](../windows/processes.md#the-win32-interop-surface)), and moving the
operator's installed `claude.exe` aside was refused as too destructive to run. What *was* measured on the real
installed binary: the hook exits **0 in 1,367 ms** with `PATH` stripped to
`system32`, registering through the fallback. The absent-client path is exercised
through the `IRegistrationCommand` seam instead.

## Tooling around the protocol

**`claude mcp list` and `claude mcp get` exit 0 even when the server is dead** —
unusable as a CI gate without grepping stdout for `✘`. **The official MCP
conformance suite is HTTP-only** (`--url`), so it needs a test-only listener or a
small bridge to reach a stdio server. **The Inspector CLI cannot spawn `.cmd`
shims on Windows** — same root cause as
[#58510](https://github.com/anthropics/claude-code/issues/58510) — so address
`cli.js` by absolute path; its **exit code 5 means the tool reported `isError`**,
which is the signal `claude mcp` does not give you. `[FLOATS]`

<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The MCP protocol, and the client at the other end

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · `ModelContextProtocol` 2.2.0 · MCP protocol revision `2025-11-25` · Windows 11 Pro 26200.
Measured on [the reference machine](../README.md#the-reference-machine).

## The protocol split

**`@playwright/mcp` 0.0.79 caps at protocol `2025-11-25`.** The child never
*rejects* a version -- it caps or echoes silently: verified, offering
`1999-01-01` returned `2025-11-25` with no error, so a mis-negotiation produces
nothing to catch and the negotiated value must be asserted. `[FLOATS]`

> `Verified 2026-08-16 @ @playwright/mcp 0.0.79 / playwright-core
> 1.63.0-alpha-2026-08-05.` Re-measured from the other side as well as the
> original one: offering the deliberately-future `2999-01-01` returned
> `2025-11-25`, and offering `2025-06-18` returned `2025-06-18` -- so it caps a
> newer revision and echoes an older one, and does neither with an error. Both
> probes are now part of the snapshot generator, so the ceiling is recorded in
> [`upstream-snapshots/tools-list.json`](../../upstream-snapshots/tools-list.json)
> and a move is a diff.
>
> **The product half landed 2026-08-16, with the first published-AOT vertical
> slice.**
> `BrowserProxy.ConnectAsync` pins `McpClientOptions.ProtocolVersion`, logs
> `requested=... negotiated=...`, and throws if the two differ; `ProtocolSplitTests`
> asserts the logged pair against the ceiling the snapshot recorded, so the pin
> and the measurement can no longer drift apart silently.

**The two halves of the split are distinguishable by a method, not only by a
version string.** Measured 2026-08-16 by sending `server/discover` as the first
frame to each end of the running proxy: **BrowserAI answers `-32602`** -- *"The
`server/discover` request requires per-request metadata declaring a supported
protocol version"* -- while **the child answers `-32601` Method not found**. The
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
states the tool set "MAY change over time ... but MUST NOT vary per-connection or
as a side effect of other requests on the connection." `ping` was removed at
`2026-07-28`. SEP-2567 also names `destroy_*` and `list_*` as the documented
companions to a creation tool. `[STABLE]` -- a published revision does not move.

**The .NET SDK implements every revision from `2024-11-05` through `2026-07-28`**
and shipped `2026-07-28` support on the spec's release date. `[FLOATS]`

**`DiscoverProbeTimeout` is 5 seconds by default.** With the client version left
unpinned, the client probes the child with `server/discover` first; if the child
drops the unknown method instead of answering, **every child spawn costs a flat
5 s against a ~300 ms baseline**, presenting as "browser automation got slow"
with no error anywhere. The SDK's own test base class pins it explicitly, citing
[csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701)
-- CI slowness tripped the probe there. `[FLOATS]`

> **Asserted, not remembered, since 2026-08-16.**
> `FakeChildHarnessTests.TheClientPinIsWhatSkipsTheDiscoverProbe` reads the 5 s
> default off `McpClientOptions` and then proves the mechanism from three sides
> against an in-process double: pinned, **no** `server/discover` is sent;
> unpinned, it is; unpinned against a double that drops the method, the connect
> pays the whole timeout. Our `TestDefaults` pins the probe **short** (250 ms),
> the opposite of upstream's fixtures, because every peer in that layer is a
> double that answers instantly -- so a probe running to its timeout is a defect
> to surface fast, not latency to tolerate. That is also why the row's
> wall-clock half is now [row 16a](../re-verification.md) and stays
> manual: a deliberately short pin cannot measure the ~300 ms production
> baseline.

## The client: Claude Code

`[FLOATS]` on a client version this project does not control.

**Tool names and server `instructions` load eagerly; schemas are deferred --
measured 2026-09-23 @ Claude Code 2.1.281, from inside a session with this
server connected and no tool of it called.** The `instructions` string was
present in full and **all 79 tool names** arrived as a bare list carrying
the client's own sentence *"Their schemas are NOT loaded -- calling them
directly will fail with InputValidationError"*; no description and no
`inputSchema` existed for any of them until one was fetched on demand, which
then returned the whole definition. ⚠️ **Two corrections to what this used
to imply. The split is per TOOL, not per server** -- three tools of another
server arrived with full schemas unasked while five of that same server
arrived deferred -- **and `instructions` is not the only channel that
reaches the model**: the names reach it too, and an eagerly-loaded tool's
description reaches it as well. What is true is narrower and is what the
design rests on: **for a tool the client defers, nothing but its name
reaches the model until something fetches the schema**, so `instructions` is
the only channel *this server* can count on. `[FLOATS]` on a client version
this project does not control.

**Server `instructions` and every tool description are truncated silently at
2 KB.** The tail simply does not exist and nothing a server can see reports it.
[What *"2KB each"* means is measured below](#what-2kb-each-means----measured-2026-08-18--claude-code-21234).

> **What BrowserAI actually spends of that, re-measured 2026-09-21 off the
> published binary's own `initialize` response: 2,026 characters and `2,036`
> bytes, leaving 22.** *Corrected 2026-09-21 (previously "re-measured
> 2026-08-18 ... 1,261 characters and `1,276` bytes, leaving 772. The three mode
> lines cost **106, 121 and 92 bytes** apiece, plus a newline each, measured from
> the same emitted string").* The mode lines were deleted on 2026-08-20 and six
> further changes have landed in the string since; the 2026-08-18 reading was
> true when it was taken and none of them came back here. The figure is reported
> and not gated -- `ModelSurfaceTests` gates the 2,048 cap -- which is why
> nothing went red while it aged.
>
> ⚠️ **Corrected 2026-08-18 (previously "measured 2026-08-16 at build-order step
> 13: 1,613 characters and `1,628` bytes, leaving 420 ... the difference is almost
> entirely the mode lines carrying what each mode *refuses* as well as what it
> grants -- which is the half a model needs to choose correctly ... **Planting a
> fourth mode measured its cost at 223 bytes**, so the headroom absorbs exactly
> one more mode and a fifth would need the lines shortened").** The `refuses`
> half was rendered from the `(tool, mode)` permission policy, and that policy
> was removed -- it was never a boundary against the caller, who chooses the
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
> carries `·` (2 bytes) and `-` (3 bytes), so a character count under-reports
> precisely the string that uses them").* The byte figure is still printed and is
> still the larger of the two; it is simply not the one that is capped -- see the
> measurement below. `[FLOATS]` on our own wording, not on a client
> version.

### What *"2KB each"* means -- measured 2026-08-18 @ Claude Code 2.1.234

The documented sentence is *"Claude Code truncates tool descriptions and server
instructions at 2KB each"*, and **"each" does not say each what.** Two readings
were live: per string, or per whole serialized tool -- name, description and the
entire `inputSchema` in one bucket. This project read it per string and
[said in the constant that it was an assumption](../../src/BrowserAI/Proxy/ClientTruncationBudget.cs);
a maintainer of another MCP server had reported trimming a description and fixing
nothing, which is what the per-tool reading predicts. **It is per string.**
`[FLOATS]` on a client version this project does not control.

**Method -- the client's own outbound request, never a model's recollection.**
Claude Code honours `ANTHROPIC_BASE_URL`, so it was pointed at a local HTTP
server on `127.0.0.1` that records the request body and answers with a minimal
SSE stream; `ANTHROPIC_AUTH_TOKEN` was set to a throwaway string, so no real
credential and no real API call was involved and the experiment costs nothing.
A probe MCP stdio server published strings of exact length carrying unique
end-markers, registered with `claude mcp add --scope user` against a **scratch
`CLAUDE_CONFIG_DIR`**. The `tools` array in the captured
`POST /v1/messages?beta=true` body is then byte-for-byte what the model receives,
so every figure below is read, not inferred. **Reproduced twice, against
`sonnet` and `haiku`, identical both times** -- the cut is client-side and
model-independent.

| Question | Answer | The probe that settles it |
|---|---|---|
| Per string, or per serialized tool? | **Per string.** There is no per-tool bucket | A tool whose whole entry was **4,578 B** -- a 1,500-character description plus four 700-character parameter descriptions, every string under the cap -- arrived **intact**. Entries of **17,411 B** and **20,172 B** arrived intact too |
| Bytes, or characters? | **UTF-16 characters. A byte count is never consulted** | A 2,048-character description weighing **6,004 bytes** of em dashes arrived **whole**, while a 2,600-character ASCII one was cut |
| Where exactly? | The predicate is **`length > 2048`**, and the cut is to 2,048 | 2,047 intact · 2,048 intact · 2,049 cut, published as a triple in one run |
| Code units, or code points? | **UTF-16 code units**, and the cut is surrogate-aware | 1,539 code points spread over 3,000 units was cut. Where unit 2,048 would split a surrogate pair the cut backs off to **2,047**, and the delivered string is well-formed |
| Are `inputSchema.properties[*].description` strings truncated? | **No. Not at all** | A parameter description of **20,000** characters arrived whole, on a tool whose own description was 39 characters |
| Is there a total budget across `tools/list`? | **No** | **202 tools totalling 348,314 B** of tool entries went in one request (body 392,983 B): nothing dropped, nothing cut, every end-marker present |
| What does truncation look like? | A hard positional cut with **`... [truncated]`** appended -- U+2026, a space, `[truncated]`; 13 characters -- so a truncated string arrives at **2,061** | Every cut string in every run ended in exactly that, and the surviving prefix was identical to the published one |

⚠️ **The suffix is visible to the model and invisible to the server.** It is added
after the JSON-RPC response has left the server, so nothing a server can observe
reports it -- **a server cannot detect its own truncation**, which is why the gate
is a build failure, not a run-time check. A *model* can see it, so
*"did this arrive whole?"* is answerable by asking and unanswerable by logging.

**Server `instructions` are capped the same way and delivered somewhere else than
the obvious place.** They arrive inside a `<system-reminder>` block in the
**`messages`** array, under a `## <server-name>` heading alongside every other
connected server's, and not in the `system` prompt -- cut at 2,048 characters
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

⚠️ **Both figures in that paragraph are the 2026-08-18 reading and both have
moved. Re-measured 2026-09-21** off the published binary's own wire, through
`ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget`,
which writes every length to `.work/description-budget.txt` on a run that
passes: `instructions` is **2,026 characters / 2,036 B** and `browserai_init` is
**1,877 characters / 1,889 B**, the largest description in the
surface. The paragraph above is left standing because what it establishes -- that
there is no per-tool bucket and nothing is truncated -- is unaffected by either
number; what has aged is the two measurements inside it.

**Re-establish it** in about fifteen minutes, with no cost and no credential:

1. Write a capture server: an HTTP listener on `127.0.0.1` that writes each
   request body to a file and replies to `/v1/messages` with a minimal
   `message_start` ... `message_stop` SSE sequence, and to `count_tokens` with
   `{"input_tokens":1}`. Redact `authorization` before writing anything.
2. Write a probe MCP stdio server -- raw JSON-RPC over stdin/stdout answering
   `initialize`, `tools/list` and `tools/call` -- publishing descriptions of exact
   length whose final token is a unique marker, at sizes straddling 2,048.
3. `claude mcp add <name> --scope user -- <node> <probe.js>` with
   `CLAUDE_CONFIG_DIR` pointed at a scratch directory. **Never the operator's
   own.**
4. Run `claude -p "Reply with the single word OK."` with
   `ANTHROPIC_BASE_URL=http://127.0.0.1:<port>` and a throwaway
   `ANTHROPIC_AUTH_TOKEN`. `ANTHROPIC_API_KEY` does **not** work -- the client
   reports *"Not logged in"* against an unapproved key, and the bearer-token
   variable is the one that bypasses that.
5. Read `tools` out of the largest captured body and diff each string against
   what the probe published.

The scripts as run are in
[`docs/probes/2026-08-18-truncation/`](../../docs/probes/2026-08-18-truncation/README.md),
and the complete recipe, written for a project that has never heard of
BrowserAI, is [`RECIPE.md`](../../docs/probes/2026-08-18-truncation/RECIPE.md)
beside them. *Corrected 2026-09-16 (previously "live in `.work/truncation/` on
the machine that ran them and are deliberately untracked -- they are scratch, not
product").*

**`notifications/tools/list_changed` handling changed, and the charter's citation
is stale.** *"Claude Code registers no handler"* was accurate at **2.0.65**
(Dec 2025) -- issues
[#13646](https://github.com/anthropics/claude-code/issues/13646) and
[#4118](https://github.com/anthropics/claude-code/issues/4118). At **2.1.231 it
is false**: measured twice, the client re-listed in **1-2 ms** and the model
called a tool that appeared only in the second list. This does **not** unlock a
per-connection tool list -- SEP-2567 stands -- but the cited issues need re-dating.

## Registering BrowserAI with the client

Measured 2026-08-16 @ **Claude Code 2.1.233** (`claude.exe`, native install at
`%USERPROFILE%\.local\bin`), while building
[BrowserAI's own client registration](../../ARCHITECTURE.md#the-mcp-server). Every run below wrote into a
**scratch `CLAUDE_CONFIG_DIR`**, never the operator's real configuration.
`[FLOATS]` on a client version this project does not control.

**`claude mcp add --scope user` writes `mcpServers.<name>` into
`$CLAUDE_CONFIG_DIR\.claude.json`** -- one entry, `{type, command, args, env}` --
and prints the file it modified. Unset, that directory is `%USERPROFILE%`. The
override is what makes a real registration testable without touching the file
the operator's own client is using.

**No elevation.** Both `add` and `remove` succeeded from a **non-elevated,
non-administrator** token (`WindowsPrincipal.IsInRole(Administrator)` = false) --
they write the invoking user's own file. Stated because
[the logon-task assumption was wrong the same way](../windows/detection.md#the-logon-sweep-task):
`schtasks` and the Task Scheduler COM API both answer `Access is denied` from
that same token, so *"a per-user operation needs no elevation"* is not something
this machine grants for free.

**Timings, three runs each:** `add` **613 / 645 / 636 ms**, `remove` **671 / 668 /
646 ms**. Against the fast-exit hook budgets -- `--veloapp-install` 30 s,
`--veloapp-updated` 15 s, `--veloapp-uninstall` 60 s
([kb](../packaging/velopack.md#nativeaot-hooks-and-vpk-output)) -- that is 15×
headroom on the tightest one. `[MACHINE]`

⚠️ **`add` is not idempotent, and every failure it has exits 1.** A second `add`
of the same name exits **1** printing *"MCP server browserai already exists in
user config"*; a `remove` of a name that is not there exits **1** printing *"No
MCP server named \"browserai\" in user scope"*. There is no exit code that
distinguishes either from a real failure, so **the words are the only
discriminator there is** -- which is why `McpClientRegistration` matches on them
and why `RegistrationTests.TheClientStillSaysWhatTheExitCodesCannot` asserts both
against the real client on every run that has one. Getting it wrong is safe in
one direction only: an unrecognised wording reports the pass as *failed*, which
is loud; it never reports a registration that did not happen as done.

**`claude mcp get` starts the server.** It health-checks, so it reported
*"✘ Failed to connect"* for a path that does not exist -- **and still exited 0**,
which is the `list`/`get` behaviour already recorded below. It is therefore
unusable as a presence check inside an install hook: it is slow, it has a side
effect, and against a real registration it would spawn BrowserAI from inside
BrowserAI's own installer.

### The whole lane, against a real installer -- 2026-08-16

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
| Stub vs. registered binary | **392,704 b** at the root against **17,911,808 b** in `current\` -- the registered path is the second | - |
| Update 0.9.0 → 0.9.1 | `AlreadyRegistered`; the entry and its path **unchanged** | **0.67 s** of a 15 s budget |
| Rollback 0.9.1 → 0.9.0 (`rollback=True deltas=0`) | `AlreadyRegistered`; unchanged again | 0.67 s |
| Uninstall | `mcpServers` is `{}` | whole uninstall **1.78 s** of a 60 s budget |

**Why an update changed nothing was the finding of the day, and both halves of
it have since stopped being true.** ⚠️ *Corrected 2026-09-16 (previously "The
registered path is `<root>\current\BrowserAI.exe`; an update replaces that
directory wholesale and the path is identical either side, so there is nothing to
correct").* The registered path is
**`<root>\current\BrowserAI.Server.exe`** since the two-binary split of
2026-09-15 -- `BrowserAI.exe` is the configuration app now -- and *"nothing to
correct"* was the premise that split destroyed: every registration written before
that day names a file the update deletes, and leaving it alone gives a person a
window where they asked for a server. `McpRegistrar.Repair` exists for exactly
that case and re-points an entry of ours that no longer resolves. The half that
survives is the last one: the client's configuration lives outside the install
root entirely, where no update can reach it. **The measurements in the table above
are unchanged** -- they were taken against the layout of their own date and are
what that day's `AlreadyRegistered` cost.

**A hook must write its own log inside itself.** `VelopackApp.Run()` exits the
process once it has served a hook, so anything buffered for later replay is
discarded -- `Program.Main`'s replay of Velopack's own records can never run on a
hook path. Confirmed by reading `<root>\logs\browserai-*.log` after the install:
all three registration records are on disk, written by the hook's own pid.

⚠️ **A clientless machine could not be simulated and the gap is named.** The
fallback directory resolves from the process token, not from
`%USERPROFILE%`, so it cannot be redirected
([kb](../windows/processes.md#the-win32-interop-surface)), and moving the
operator's installed `claude.exe` aside was refused as too destructive to run. What *was* measured on the real
installed binary: the hook exits **0 in 1,367 ms** with `PATH` stripped to
`system32`, registering through the fallback. The absent-client path is exercised
through the `IRegistrationCommand` seam instead.

### The SDK refuses EVERY protocol version but the pinned one -- measured 2026-09-23

`[FLOATS]` ModelContextProtocol 2.2.0.

**Against a fake server echoing a chosen `protocolVersion`, with the client
pinned to BrowserAI's own `2025-11-25`, seven arms:** the exact echo **connected**;
`2025-06-18`, `2024-11-05`, `2026-07-28`, `1999-01-01` and an explicit `null` all
threw `ModelContextProtocol.McpException` -- *"Server protocol version mismatch.
Expected 2025-11-25, got X"* -- out of `McpClient.CreateAsync`; and omitting the
field threw `JsonException` for a missing required property.

⚠️ **So it is not a downgrade check, it is an equality check -- and the
CONTRACT is the weaker one.** The SDK's own XML doc for
`McpClientOptions.ProtocolVersion` promises only that the client *"refuses to
downgrade below it"*, so an upward or lateral echo is unpoliced by contract even
though 2.2.0 polices it in fact. `ChildConnection` keeps its explicit check for
that reason and says in place that it cannot currently fire.

**Re-establish** by driving `McpClient.CreateAsync` at a stdio server that answers
`initialize` with a chosen `protocolVersion`, one arm per value, with the exact
echo as the control. The rig is `.work/assumed-2026-09-23/src/probes/`; the output
is `probe-C-negotiation.txt`.

## Registering with Codex, and what its startup timeout costs -- measured 2026-09-24

`[FLOATS]` codex-cli **0.155.0-alpha.9.2**, Windows 10.0.26200, BrowserAI's published
NativeAOT server. Every reading below was taken with `CODEX_HOME` forced at a
scratch directory; the real `~/.codex` was never written.

**The CLI is not on PATH for a desktop-app install, and that is the finding the
discovery order exists for.** On this machine `codex` resolves through
`%LOCALAPPDATA%\OpenAI\Codex\chrome-native-hosts-v2.json`, whose
`entries[].paths.codexCliPath` names
`~\.codex\plugins\.plugin-appserver\codex.exe`. `command -v codex` finds
nothing. A product that looked only at PATH would report no client on a machine
with Codex installed, which reads as *BrowserAI does not support Codex*.

**What `mcp add` writes**, read straight back off disk:

```
[mcp_servers.browserai]
command = 'C:\x\BrowserAI.Server.exe'
```

A TOML **literal** string, so a Windows path needs no escaping and BrowserAI never
has to produce one.

**What `mcp list --json` answers** is a bare array, and it carries more than the
command:

```
[{"name":"browserai","enabled":true,"disabled_reason":null,
  "transport":{"type":"stdio","command":"C:\\x\\BrowserAI.Server.exe","args":[],
               "env":null,"env_vars":[],"cwd":null},
  "startup_timeout_sec":null,"tool_timeout_sec":null,"auth_status":"unsupported"}]
```

That is why BrowserAI asks the client instead of reading the TOML: the JSON answers
the ownership question directly, and a client that changed its own file format costs
this product nothing.

**Idempotent in both directions, confirmed first-hand.** Three consecutive
`mcp add browserai -- <cmd>` calls each exit **0**; `mcp remove browserai` exits
**0**; and a second `mcp remove` of a server that is no longer there also exits
**0**. So there is no already-exists and no nothing-to-remove failure to recognise,
which is the opposite of Claude Code on both counts.

**Project scope is the same command with `CODEX_HOME` moved.**
`CODEX_HOME=<repo>\.codex codex mcp add ...` exits 0 and writes
`<repo>\.codex\config.toml`. The directory must exist first.
⚠️ *The `tmp\arg0` residue an earlier run of this rig recorded did NOT appear
here*: the only file under `<repo>\.codex` afterwards was `config.toml`. So the
residue is not reliably produced, and anything that cleans it up has to tolerate its
absence.

⚠️ **`codex mcp add` ACCEPTS NOTHING THAT PERSISTS A STARTUP TIMEOUT.** Its whole
option set is `-c key=value`, `--env KEY=VALUE`, `--enable FEATURE`, `--url` and
`--bearer-token-env-var`. `startup_timeout_sec` is a `config.toml` key
(`mcp_servers.<id>.startup_timeout_sec`, default **10** seconds) and there is no flag
for it. And `-c` is not a way in: `codex mcp add browserai -c
mcp_servers.browserai.startup_timeout_sec=30 -- <cmd>` **fails** with *"failed to
load configuration ... invalid transport in `mcp_servers.browserai`"* and writes
nothing, because the override creates a partial server table the loader then rejects.
So a product that does not write the TOML cannot set this key, and BrowserAI does not
write the TOML.

**Which is fine, because nothing needs it.** Measured over three rounds against the
published `BrowserAI.Server.exe`, driving `initialize` and then `tools/list` over
stdio and timing from `spawn`:

| | round 1 | round 2 | round 3 |
|---|---|---|---|
| `initialize` answered | **342.4 ms** | **340.1 ms** | **332.0 ms** |
| `tools/list` answered, 79 tools | 350.4 ms | 347.7 ms | 339.3 ms |

The handshake is **three hundred and forty milliseconds against a ten-second
budget**, and the surface child's own `tools/list` is inside it -- the 79 tools come
from a node child that was spawned, handshaken and answered within that figure.

⚠️ **WHAT WAS NOT MEASURED, and it is the only path that could approach the
budget.** These rounds are warm: the binary had just been published and the payload
was in the file cache. A genuinely cold first start reads a 19 MB server, a 93 MB
`node.exe` and a ~117 MB payload from disk for the first time, and nothing here
establishes what that costs on a slow or contended disk. **The first-run browser
download is NOT this path**: provisioning is 207.3 MB and ~10.8 s, and it happens on
the first browser call and not during startup, so it cannot reach a startup timeout
-- it reaches a TOOL timeout instead, which Codex defaults to 60 s.

**Re-establish** by pointing a stdio driver at the published server, writing
`initialize` and timing the first framed answer, then `tools/list`; and by reading
`codex mcp add --help` for the option set. The probe is
`.work/startup-probe.mjs` in the batch that produced this entry.

## What a client does when the server exits, and what the pipe decides -- measured 2026-09-24

`[FLOATS]` **Claude Code 2.1.281** and **codex-cli 0.155.0-alpha.9.2**, Windows
10.0.26200. Measured against a purpose-built dummy MCP server with one tool and a
scripted exit, driven through a local API stub so the model's tool calls are
deterministic, with **every request body captured byte for byte**. Three rounds
per scenario; every count below is out of three. Rigs, captures and logs:
[`docs/evidence/2026-09-23-client-reconnect`](../../docs/evidence/2026-09-23-client-reconnect/README.md).

⚠️ **This section answers a product question and not a curiosity.** BrowserAI's
update applies only when it is the last live instance, and every client session
holds one server alive for its whole life, so what a client does with a server
that ends is what decides whether an update can ever be let in. The decision it
fed is [Q254 and Q261](../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client).

### Claude Code re-launches a dead server transparently, and never re-lists its tools

**A stdio server that exits cleanly is started again on the next tool call, 3/3 in
every scenario measured**: inside one session registered with `--mcp-config`,
inside one registered with `claude mcp add --scope user`, after an idle exit that
happened before any tool call, across `claude -p --continue`, and in a fresh
`claude -p`. The model sees a clean result; nothing in the transcript says a
process died. Each run's `*.launches.log` carries one `LAUNCH` line per server
process, which is how *a new process answered* is established and not inferred.

⭐ **The re-launched server is sent `initialize` and `tools/call`, and never
`tools/list`.** Its `instructions` are read and discarded: the session keeps the
tool list and the instructions it took at first connect. **That is the whole
reason a rename or a removal across an update is dangerous** -- the model goes on
calling a name the new server no longer has, and the error it gets back reads to
it as its own mistake. Held over the captured request bodies, which are what the
model actually saw.

**It does honour `notifications/tools/list_changed`, 3/3**, and says so in its own
`--debug-file`: *Received tools/list\_changed notification, refreshing tools*. One
frame is enough; nothing else measured moves that cached list.

⚠️ **A failed re-launch is sticky for the rest of the session.** When the server
cannot start, the tool result is an error reading *MCP server "probe" is not
connected*, and **no second dial is attempted** -- a third call, 3/3, produced no
new `LAUNCH` line. So a window during which the binary is unstartable is not a
window that heals itself.

**Two channels a server might hope to reach the model with do not.**
`notifications/message` is dropped outright, because the client declares no
logging capability at `initialize`; **stderr reaches `--debug-file` and nothing
else**, and the 42 zero-byte `*.stderr.txt` files in the batch are that result.
The only channel to the model is a tool result.

**At exit the client kills the server tree**, `taskkill /T /F` on the server's
pid, which is why nothing of ours runs after the client goes.

**What the documentation says and what the code does are not the same thing, and
the difference is load-bearing.** `code.claude.com/docs/en/mcp` says a stdio
server is *not* reconnected automatically, and that is true of the transport: the
client does not reconnect, it **re-dials**. The `onclose` handler clears the
cached client, so the next call constructs a new one. Read in the client's own MCP
module at offset 230,400,000 of `claude.exe`, in `ensureConnectedClient`, and
confirmed by the launch lines.

### Codex never re-launches on the failure path, and does on the next refresh

⚠️ ***Corrected 2026-09-24, and the maintainer was right to push back.*** The
first round of this measurement concluded *Codex never re-launches a dead stdio
server*, on three scenarios that all sat inside one turn. That is false as a
general claim. **What is true is narrower: Codex re-launches on the next
REFRESH, and nothing on the failure path fires one.**
`reusable_client` rejects a client whose transport is closed on any refresh --
`connection_manager.rs:93-107`, identical at tag `rust-v0.155.0-alpha.9.2`
(commit `4607249e430dac1c961df4dc615beae88e33cec8`) and on `main`
(`5f371ba30ae4a4bf4527eb0729ab6b36e3a0c123`, read 2026-09-23T21:35Z) -- and
`Op::RefreshMcpServers` has **no production caller**.

**Measured, 3/3 each. Does NOT recover:** a new tool call; a new turn in the same
thread; `/mcp`, which reports the server's `runtimeStatus` as *failed* and dials a
throwaway probe to find that out; an edit to `config.toml` on disk, because
nothing watches the file. The model is told *Transport closed*.

**Recovers:** `config/mcpServer/reload` from an app-server host, which is what an
IDE or the desktop app is; a settings change on the thread, `cwd` or permissions,
which the TUI reaches through `/cd` and `/permissions`; and a new thread. **A
crash exit behaves exactly like a clean one** -- the exit code changes nothing.

**Codex ignores `notifications/tools/list_changed`**, 3/3: one line in its own
tracing log, *MCP server tool list changed*, and nothing else. On `main` as well.
**It does not need to honour it**, and that is why this is recorded and not filed
as a defect: a Codex thread's tool list is taken at first connect, and a restart
or a new thread lists fresh by construction. The server's log notification and its
stderr reach Codex's tracing log only. At exit it hard-kills the server.

**Both behaviours are known upstream and open**: `openai/codex` **#16899** is
exactly this, and **#4955** asks for a restart command.

### Both clients decide from the PIPE, not from the process

⭐ **Nothing on either side watches the child exit.** Claude Code's transport
reports closed on Node's `close` event after the streams shut; Codex's `rmcp`
transport reports the same thing from its own reader. **So a process that keeps
the pipes open keeps the session alive, whatever happens behind it.**

**A relay holds a transport across a full swap.** A process spawned by the client
that never exits, forwards stdio to a child server and re-spawns that child from
the replaced location: **3/3 Claude Code in one session, 3/3 `codex exec` in one
turn, 3/3 Codex app-server on the second turn of the same thread** with no reload,
no new thread and no user action. Swap **308-365 ms**.

⚠️ **The handover shape is the one that looks equivalent and is not.** A server
that hands its inherited pipes to a helper and exits works 3/3 under Codex, whose
parent is a Rust/tokio process -- and **collapses 3/3 under Claude Code**, where
the helper's inherited **stdin** reports EOF the moment the original exits. The
minimal Windows experiments underneath that are in the batch: with `'inherit'` and
no `detached`, the helper is killed with the parent's job object; with `detached`,
**stdout survives** the parent's exit (`exit` fires on the parent, `close` does
not) and **stdin still EOFs**, in all three of `destroy`, `pause` and doing
nothing, and the parent's later write is lost.

⚠️ **An in-flight request across the swap is recovered only by re-sending the
frame**, 3/3 in both clients. That is at-least-once delivery, and a tool with side
effects cannot have it. This is the measurement that priced the relay out of
[the update-lane decision](../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client),
and the relay prototype is kept in the batch as the direction not taken.

**Re-establish it** with `rig/server.js` and `rig/apistub.js` (or
`rig/openaistub.js` for Codex) out of the batch: point `CLAUDE_CONFIG_DIR` or
`CODEX_HOME` at a scratch directory, set `PROBE_MODE` on the dummy server to
`exit-after-first`, and read `logs/<run>.launches.log` for the process count and
`logs/<run>.requests*.jsonl` for what the model was sent. ⚠️ **Never against the
real client configuration** -- every run here wrote inside its own scratch home,
and the runs that omitted `--strict-mcp-config` also connected this repository's
own committed `.mcp.json` servers, which is noted in the batch because it is
visible in the captures.

## Tooling around the protocol

**`claude mcp list` and `claude mcp get` exit 0 even when the server is dead** --
unusable as a CI gate without grepping stdout for `✘`. **Re-measured 2026-09-23 @
Claude Code 2.1.281** against two synthetic user-scope servers, a command that does
not exist and a node process that starts and never speaks MCP: both commands exit
**0** while printing `✘ Failed to connect`, and every failure the client does
report exits **1** -- a duplicate `add` and a nothing-to-remove `remove` are
indistinguishable by code, which is why `McpClientRegistration` reads the English.
⚠️ **Exit 0 also covers a broken CONFIG and not only a broken server**: a
`.mcp.json` that is not valid JSON exits 0 printing `MCP config diagnostics ✘`.
*This entry carried no client version until today, which is what the hazard row
pointing at it said it owed.* `[FLOATS]` **The official MCP
conformance suite is HTTP-only** (`--url`), so it needs a test-only listener or a
small bridge to reach a stdio server. **The Inspector CLI cannot spawn `.cmd`
shims on Windows** -- same root cause as
[#58510](https://github.com/anthropics/claude-code/issues/58510) -- so address
`cli.js` by absolute path; its **exit code 5 means the tool reported `isError`**,
which is the signal `claude mcp` does not give you. `[FLOATS]`

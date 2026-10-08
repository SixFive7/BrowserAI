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
`ProtocolSplitTests.TheServerKnowsTheNewOpeningMethodAndTheChildDoesNot`, which
asks both ends the same question in the same run. `[FLOATS]` *Renamed 2026-10-08
(previously `ProtocolSplitTests.TheServerReachesARevisionTheChildDoesNotImplement`)*:
BrowserAI no longer serves that revision, and refuses a request naming it
([below](#the-new-opening-request-and-the-one-revision-browserai-offers----measured-2026-10-08)).

> The same run also confirms the downward pin is independent of the caller:
> offering `2025-06-18` to BrowserAI returns `2025-06-18` while the child
> session in that same process is at `2025-11-25`. A server that merely
> forwarded the child's answer would return `2025-11-25` there.
>
> ⚠️ `Corrected 2026-10-08 @ ModelContextProtocol 2.2.0 (previously "offering
> 2025-06-18 to BrowserAI returns 2025-06-18")` -- **true of every build until
> the pin, and not since.** BrowserAI offers exactly `2025-11-25`, and the SDK
> answers a caller offering `2025-06-18` with it;
> `ProtocolSplitTests.ACallerOfferingAnOlderRevisionIsAnsweredWithTheOneBrowserAiOffers`
> holds the new answer, and was watched red against the unpinned build, which
> still answered `2025-06-18`. The two negotiations stay separate in the code; they
> happen to name one revision.

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

> ⚠️ *Narrowed 2026-10-03 @ Claude Code 2.1.288 by addition.* **The handler is
> registered only for a server that declares `capabilities.tools.listChanged`.**
> Read in the 2.1.288 bundle at all three places Claude Code registers one, the
> headless and SDK client, the terminal UI and the device bridge, each gated on
> that capability; and measured: against a server answering `"tools":{}`, which is
> what BrowserAI declares, the notification was ignored 9 of 9 on 2026-10-03,
> through `claude -p` and the stream-json transport, and 3 of 3 on 2026-09-25;
> with `"listChanged":true` declared it re-listed at once, 3 of 3 and 6 of 6. So a
> re-list is a property of the pair, and this product's half of it is not there.
> [Evidence](../../docs/evidence/2026-10-03-q369-tool-list-refresh/README.md).

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
discriminator there is** -- which is why RegisterAI reads the entry before every
write, so it never asks for an `add` that exists or a `remove` that does not, and
reads it back after, and why its own `RealClientTests` holds both texts against
the real client. *Corrected 2026-10-03 (previously "which is why
`McpClientRegistration` matches on them and why
`RegistrationTests.TheClientStillSaysWhatTheExitCodesCannot` asserts both against
the real client on every run that has one. Getting it wrong is safe in one
direction only: an unrecognised wording reports the pass as failed, which is
loud; it never reports a registration that did not happen as done"), when
BrowserAI's registration moved to RegisterAI.*

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
⚠️ *Added 2026-09-24 at 08:20Z, by addition: a later run DID produce it, and it is
two DIRECTORIES.* An `mcp add`, an `mcp list --json` and an `mcp remove` under a
scratch project home left `tmp\` and `tmp\arg0\`, both empty, beside a
`config.toml` of 0 bytes; the CLI's own warning line calls what it tried to put
there *PATH aliases*. Both readings are true of their moment. The registrar removes
the two innermost first and only while they are empty, and never the `config.toml`
-- see [what Codex hands a stdio server](#what-codex-hands-a-stdio-server-and-how-it-ends-one----measured-2026-09-24).
⚠️ *Corrected 2026-09-24 @ codex-cli 0.155.0-alpha.9.2, by addition (previously "So
the residue is not reliably produced").* **The Q288 rig produced it every time: 18 of 18
project setups**, each an `mcp add` and an `mcp get --json` with `CODEX_HOME` at a
scratch project's `.codex`, listed `config.toml`, `tmp` and `tmp\arg0` there afterwards
([evidence](../../docs/evidence/2026-09-24-codex-expansion/README.md),
`files.projectDotCodexListing` in each run's `setup.json`). The same runs show what it
is for: every server an app-server started carried `<CODEX_HOME>\tmp\arg0\codex-arg0`
and a random suffix as the FIRST entry of its PATH, unless its own entry set a PATH. The
one earlier run that left nothing is unexplained and stays recorded above; the cleanup
still tolerates an absent residue, which costs nothing.

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
download is NOT this path**: provisioning is 208.8 MB and 13 to 17 s (re-measured 2026-10-03 at chromium 1247, previously 207.3 MB and ~10.8 s), and it happens on
the first browser call and not during startup, so it cannot reach a startup timeout
-- it reaches a TOOL timeout instead, which Codex defaults to 60 s.

> ⚠️ *Corrected 2026-10-03 @ codex-cli 0.155.0-alpha.9.2 and 0.160.0, by
> addition (previously "default **10** seconds", "a ten-second budget" and
> "which Codex defaults to 60 s").* **Codex's default startup timeout is 30 s,
> and its default tool timeout is more than 75 s.** Measured with a
> `config.toml` that sets neither key, against a stand-in server that answers
> late, with `CODEX_HOME` in scratch and no window. Through `codex app-server`, a
> server answering `initialize` after 15 s was reported `ready` at 15.1 s, and one
> answering after 35 s was reported `failed` at 30.0 s with Codex's own *"MCP
> client for `browserai` timed out after 30 seconds. Add or adjust
> `startup_timeout_sec` in your config.toml"*, at both versions. Through `codex
> exec`, a tool call answered after 75 s completed and the answer reached the
> model, at both versions. Codex's source names 30 s and 300 s at both tags
> (`rmcp_client.rs:103-104` and `connection_manager.rs:332-338` at 0.155,
> `rmcp_client.rs:105-106` at 0.160); the 300 s was read there and not measured.
> So BrowserAI's 340 ms handshake sits against thirty seconds, and the 13 to 17 s
> first-run download against a tool timeout no measured provisioning reaches.
> ⚠️ **And `codex exec` does not wait for a slow server at all.** With
> `initialize` answered after 15 s or 35 s, its first turn went to the model
> about 1.2 s after the server started, with no BrowserAI tool in the list, and
> the run ended about 2 s after it began, 4 of 4 over the two versions; a server
> that answered at once was in the list and was called, 2 of 2. Under `exec`, a
> server that is still starting a second in misses that turn, whatever the
> startup timeout says. The runs and the rig:
> [the batch](../../docs/evidence/2026-10-03-codex-default-timeouts/README.md).

**Re-establish** by pointing a stdio driver at the published server, writing
`initialize` and timing the first framed answer, then `tools/list`; and by reading
`codex mcp add --help` for the option set. The probe is
`.work/startup-probe.mjs` in the batch that produced this entry.

## What Codex hands a stdio server, and how it ends one -- measured 2026-09-24

`[FLOATS]` codex-cli **0.155.0-alpha.9.2**, Windows 10.0.26200, measured while
building the Codex hooks and window (Q258 steps 2 to 4). **Every call ran under a
scratch `CODEX_HOME`**; the real `~/.codex` was never written.

**Four findings, and each one changed code or a test.**

**1. The environment a stdio server gets is an allowlist, 3/3.** A stand-in server
that recorded its own environment (`envdump.js` in
[the Q261 rig](../../docs/probes/2026-09-24-q261/README.md)) was handed exactly
**20** variables: `APPDATA`, `COMSPEC`, `HOMEDRIVE`, `HOMEPATH`, `LOCALAPPDATA`,
`PATH`, `PATHEXT`, `PROGRAMDATA`, `PROGRAMFILES`, `PROGRAMFILES(X86)`,
`PROGRAMW6432`, `SHELL`, `SYSTEMDRIVE`, `SYSTEMROOT`, `TEMP`, `TMP`, `USERDOMAIN`,
`USERNAME`, `USERPROFILE` and `WINDIR`. A `BROWSERAI_ROOT` and a marker variable set
on the `codex app-server` process were not among them, in any round. **So a
variable reaches a Codex-started BrowserAI only through the registration's own
`env` table** (`codex mcp add ... --env K=V`). `LOCALAPPDATA` is on the list, which
is why a Codex-hosted BrowserAI finds its ordinary app root; `BROWSERAI_ROOT` in a
person's environment does nothing to it.

**2. A home that does not exist is refused, by every verb.** With `CODEX_HOME` at a
missing directory, `mcp list --json`, `mcp remove` and `mcp add` each exit **1**
with *CODEX_HOME points to "...", but that path does not exist*, followed by
*failed to resolve CODEX_HOME* or *failed to load configuration*, and create
nothing. **The registrar's first project registration asked about a repository
before creating its `.codex`, read that exit as UNREADABLE, and refused** -- so a
repository could never be registered the first time. The real-client arm found it
and the double did not, because the double answered regardless; the double
models the refusal now, and `CodexRegistryView.ReadProject` answers *absent* for a
home that is not there instead of asking.

**3. The project-scope residue is two empty directories**, `tmp\` and `tmp\arg0\`
-- recorded above as a correction to the earlier reading that saw none.

**4. Codex ends a server by TERMINATING it, not by closing its stdin, 3/3.** The
published BrowserAI server was registered in a scratch home, started by a real
`codex app-server`, and served one `browserai_list`; then the driver ended the
app-server's stdin and waited ten seconds before killing anything:

| | round 1 | round 2 | round 3 |
|---|---|---|---|
| app-server exits after its stdin EOF, exit code 0 | 56 ms | 50 ms | 47 ms |
| BrowserAI server gone, polled every 100 ms from the EOF by a copy of the driver that also watched the pid | 101 ms | 115 ms | 100 ms |

**The server's own log carries no end-of-stream line and no client-exit line**,
and its `live\<pid>-<guid>.live` marker was left behind -- the shape of a process
that was ended from outside before it could tear down. ⭐ **That marker is not
held**: it opened exclusively in all four runs, so the census in `LiveInstances`
does not count it and an update is not blocked by a Codex-hosted server whose host
has gone. The suite holds this half on every run where Codex is installed:
`ClientReconnectTests.ABrowserAiRegisteredInCodexServesACallAndLeavesNothingThatHoldsAnUpdate`,
with the positive control inside the arm -- the same reclaim pass finds the marker
HELD while the server is serving.

⚠️ **What this does not establish.** It says nothing about how long a desktop-app
thread keeps its server; nothing written down measures that, and the census after
the 2026-09-24 reboot found the desktop app-server holding no BrowserAI server to
measure. And no run had a browser open, so what a termination does to a session's
browser tree was not exercised here.

**Re-establish** finding 1 with `envdump.js` as its rig row describes, finding 2 by
running any `codex mcp` verb with `CODEX_HOME` at a directory that is not there,
and finding 4 with `appserver.js` and `DRIVER_KILL_AFTER_MS=10000`: the driver log
carries `APPSERVER EXIT` before `KILLING`, and the server's pid is read off its own
start line under the scratch app root. The millisecond row above came from a copy of
the driver that polled that pid every 100 ms after the EOF, which is one
`setInterval` around `process.kill(pid, 0)`.

## Codex expands nothing in a server's command, and finds a bare name on the server's PATH -- measured 2026-09-24

`[FLOATS]` codex-cli **0.155.0-alpha.9.2**, Windows 10.0.26200, measured 2026-09-24
between 13:41Z and 13:57Z for Q288 under a scratch `CODEX_HOME` per run
([evidence](../../docs/evidence/2026-09-24-codex-expansion/README.md)). **The question
was whether a Codex project entry can be committed in a form that resolves on every
machine**, the way `${LOCALAPPDATA}/...` does for Claude Code
([the registration section](#registering-browserai-with-the-client)). **It cannot.**

| Spelled in `command` | Started |
|---|---|
| `${LOCALAPPDATA}`, `$LOCALAPPDATA`, `%LOCALAPPDATA%` and `~`, each with `/` and with `\`, at user and at project scope, three rounds | **0 of 48**, every one *"MCP startup failed: The system cannot find the path specified. (os error 3)"* |
| An absolute path, the controls | 45 of the 45 Codex loaded |
| A bare name, `probe-stub.exe`, with the entry's own `env.PATH` naming its directory | 3 of 3 |

The same four spellings in `args` reached the server as written, 24 of 24, and
`codex mcp add` stores whatever it is handed: 78 of 78 adds read back byte for byte
through `codex mcp get --json`. **So a variable in a Codex entry is text**, and an
entry written the Claude Code way is a registration Codex cannot start.

**The source agrees, at the tag the CLI was built from**, `rust-v0.155.0-alpha.9.2`,
commit `4607249e430dac1c961df4dc615beae88e33cec8`:
`codex-rs/rmcp-client/src/stdio_server_launcher.rs:263-285` builds the server's
environment, resolves the configured program with
`program_resolver::resolve(program, &envs, &cwd)` and starts what that returns;
`program_resolver.rs:41-65` is `which::which_in` over the `PATH` in that environment,
falling back to the text as given; and `utils.rs:16-57`,
`create_env_for_mcp_server`, reads each allowlisted variable, `PATH` among them, from
Codex's own environment and lays the entry's `env` table over it. Nothing on that
path expands anything. On `main` at `282cd7b0` (2026-09-24T13:09:49Z) the resolver
and the environment builder are byte-identical once line endings are set aside, and
`launch_server` still resolves and starts the program the same way. Expansion is
[openai/codex#2680](https://github.com/openai/codex/issues/2680), *"Support environment
variable expansion"*, **open since 2025-08-25** (read 2026-09-24).

⚠️ **What the bare-name row does and does not show.** It was measured with the PATH in
the entry's own `env`. The route BrowserAI takes -- no `env` in the entry, the install's
`current\` folder on the user's PATH, and Codex's own inherited `PATH` carrying it to the
resolver -- is the same resolver reading a different `PATH`, and is **read from the
source above, not measured**. It also means **a Codex process started before the install
has no such entry in its PATH**, so it cannot find the server until it is started again;
that is inferred from the same source and is not measured either. `which` resolves a
leading `~` in a PATH entry as well (`finder.rs:242` in `which` 8.0.0): the same case
with `env.PATH` spelled `~\AppData\Local\...` started 3 of 3.

✅ *Added 2026-10-03 by addition: both halves are measured now, 2026-09-25 @
codex-cli 0.155.0-alpha.9.2, through the suite's test installer* (Q304,
[evidence](../../docs/evidence/2026-09-25-client-behaviour/README.md)). A project
entry written by `codex mcp add` as the bare `BrowserAI.Server.exe`, with no PATH
in it: **an app-server started AFTER the install started the server from the
install's `current\` folder, 3 of 3**, its log naming that image and the
app-server as its parent; **one started BEFORE the install never did, 3 of 3**,
answering *"MCP startup failed: program not found"* on the old thread, on a new
thread and after `config/mcpServer/reload`. Each app-server ran with the
environment Windows builds for a new process of the user at its start, which is
how a Codex launched after the install gets the new PATH. The user PATH read back
byte-identical after the uninstall. The Codex desktop app and a Codex started from
a terminal were not driven.

**What BrowserAI does with it is Q294, decided 2026-09-24 by the maintainer, verbatim:
*"Q294 b"*: a Codex project entry names the server by its bare file name, and the
install puts its own `current\` folder on the user's PATH.** Claude Code's project entry
keeps its `${LOCALAPPDATA}` spelling, which that client expands. **And the ownership
check expands nothing for Codex** (`CodexRegistryView.Classify`): an entry spelled with a
variable is not one BrowserAI writes, so it reads as another install's and is never
touched. Until this measurement the check borrowed Claude Code's expansion, and such an
entry read as ours and present while Codex could not start it.

**Re-establish** with the rig in the evidence batch: `rig/matrix.sh` runs every case three
times under scratch homes, and `rig/summarize.js` prints the table above from the
result files. Against a newer CLI the first thing to look at is the `_cmd` rows: one
that starts means Codex has begun expanding.

## Every status list starts one more copy of each Codex server -- measured 2026-09-24

`[FLOATS]` codex-cli **0.155.0-alpha.9.2**, Windows 10.0.26200, from the same Q288 runs
([evidence](../../docs/evidence/2026-09-24-codex-expansion/README.md)). **A
`mcpServerStatus/list` does not ask the servers a thread already holds. It starts each
configured server again, asks it, and lets it go.** The driver sent two lists per run
after the thread's servers were up, one with the thread's id and one without:

| | |
|---|---|
| launches of each server that started, per run | **3**, in all 31 app-server runs: the thread's own and one per list |
| the pid the list without a thread reported, against the list with one | different in **84 of 84** rows |
| the pid either list reported, against the pid that answered the thread's own tool call | different in **86 of 86** rows |
| the copies still running when the driver next took a census, after its tool calls | **none**, in 31 of 31 runs |

**What that costs BrowserAI, read from its own startup and not measured under Codex:**
every copy is a whole `BrowserAI.Server.exe` start. It joins the live census, opens its
pipe, starts the stray sweep in the background, starts its Playwright child to learn the
tool surface it reports, and on an installed build that is not a pre-release starts its
one feed check once it is serving. How much of that a copy finishes before Codex ends it
was not measured. The Codex desktop app and `/mcp` both ask for this list; how often the
desktop app does was not measured either. See [the hazard index](../../HAZARDS.md#hazard-index).

**Re-establish** with the evidence batch's rig: `result.json` of any run carries both
lists' `serverInfo` and every tool call's own launch record, and `summary.json`'s rows
put the pids side by side.

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

> ⚠️ *Corrected 2026-10-03 @ Claude Code 2.1.288 by addition (previously the
> heading above and its first sentence, read as true of Claude Code as a
> whole).* **It holds for `claude -p` and for the stream-json transport the VS
> Code extension drives, and NOT for the terminal UI.** Every run below was `-p`.
> Driven in a pseudoconsole with no window, the terminal UI never started a dead
> stdio server again: the server was shown as failed and the next call was
> refused by Claude Code itself, 9 of 9, whether the server ended with
> `TerminateProcess` and exit code 1, with a clean exit 0, or holding the real
> tool list; `/mcp`, the server, Reconnect brought it back every time. Automatic
> reconnection after a close is skipped for stdio in the client's own code (read
> at 2.1.288), and the transparent re-launch is the headless client's
> `ensureConnectedClient`. `-p` and the stream-json transport re-launched on the
> next call, 6 of 6 and 12 of 12, and never re-listed, as below.
> [Evidence](../../docs/evidence/2026-10-03-q369-tool-list-refresh/README.md).
>
> ⚠️ *Added 2026-10-03 @ Claude Code 2.1.288 by addition.* **These runs had
> tool search off, and a first-party session has it on.** A custom
> `ANTHROPIC_BASE_URL` turns it off, and the batch's own debug logs say so:
> `[ToolSearch:optimistic] disabled: ANTHROPIC_BASE_URL=http://127.0.0.1:<port> is not a first-party Anthropic host`.
> So the model here was handed every
> tool's full definition from the first turn, where a session against
> Anthropic's own API is handed only the deferred tools' names until a ToolSearch
> call loads them ([the client](#the-client-claude-code)). What crossed the pipe
> and what the client did hold either way; what the model was shown before it
> searched does not.

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

> ⚠️ *Narrowed 2026-10-03 @ Claude Code 2.1.288 by addition.* **The dummy server
> here declared `capabilities.tools.listChanged`** (`rig/server.js`, line 86 of
> the batch), and that is what the 3/3 rests on. Against `"tools":{}`, which is
> what BrowserAI declares, Claude Code ignored the notification 9 of 9 on
> 2026-10-03 and 3 of 3 on 2026-09-25; see
> [the narrowing above](#the-client-claude-code).

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

⚠️ *Corrected 2026-10-03 @ Claude Code 2.1.288 by addition (previously "At exit
the client kills the server tree, `taskkill /T /F` on the server's pid, which is
why nothing of ours runs after the client goes").* **The kill is real and the
conclusion was too strong.** Measured over 168 graceful Claude Code exits, the
server's stdin reaches end of file first, every time, and the tree kill lands
**0.53 to 1.15 s** later; a server that finishes inside about half a second
gets out by itself. When `claude` is itself killed there is no end of file at
all, and the server dies within 19 ms. No capture behind the sentence above was
found in the batch it sits beside. See
[what each client does to a stdio server when the session ends](#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03).

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

## What each client does when `tools/list` fails at the first connection -- measured 2026-09-25

`[FLOATS]` **Claude Code 2.1.282** and **codex-cli 0.155.0-alpha.9.2**, BrowserAI
1.1.1-alpha.0.130 published from the tree, Windows 11. A stand-in server answered
`initialize` normally, with `"tools":{}` as BrowserAI declares it, and answered
`tools/list` with a JSON-RPC error carrying BrowserAI's own updating sentence
(Q296); the real server in its real updating mode was run once per client
beside it. Every client ran under a scratch configuration against a local API
stub, three runs per arm unless stated, and every request body the model was
sent was captured. [Evidence](../../docs/evidence/2026-09-25-client-behaviour/README.md),
[rig](../../docs/probes/2026-09-25-client-behaviour/README.md).

**Claude Code keeps a server that failed its first `tools/list` connected and
toolless for the rest of the session.** It sent `tools/list` four times, with
retries at 250, 500 and 1,000 ms, logged *"Failed to fetch tools"* to its debug
log, offered the model no BrowserAI tool in any of four turns, and did not start
the server again when it ended, 3 of 3; a server that ended inside the retries
and one that ended 91.5 s earlier changed nothing. Each call the model tried
answered *"No such tool available"*, while the server's instructions, which
still say to call `browserai_init` first, stayed in its context. `claude -p
--continue`, a new process, listed and served normally. The real server
behaved the same.

**Codex marks the server failed and terminates it.** `codex exec` sent
`tools/list` once, logged *"MCP startup failed"* with the sentence, terminated
the server and offered the model no `mcp__browserai` namespace, 3 of 3; `exec
resume --last` started it again and served. The app-server reported
`mcpServer/startupStatus/updated` *failed* with the sentence, the server was gone
within 0.57 s, and a direct tool call answered a JSON-RPC error; a
`config/mcpServer/reload` or a new thread started it again and served, 3 of 3.

**Neither client showed the model the sentence**: it is in 0 of the captured
request bodies. Only Codex's host sees it, in the startup status.

⚠️ *Added 2026-10-03 @ Claude Code 2.1.288 by addition.* **The Claude Code runs
here had tool search off, and a first-party session has it on.** A custom
`ANTHROPIC_BASE_URL` turns it off, and the batch's own Claude Code debug logs
carry `[ToolSearch:optimistic] disabled: ANTHROPIC_BASE_URL=http://127.0.0.1:<port> is not a first-party Anthropic host`.
So what the model was offered in each turn is the full-definition list, where a
session against Anthropic's own API is offered the deferred tools' names until a
ToolSearch call loads them ([the client](#the-client-claude-code)). The wire, the
retries and what each client did hold either way.

**The shapes measured beside it, all with the stand-in, 3 of 3 each:**

| What the updating server does | Claude Code | Codex |
|---|---|---|
| Real `tools/list`, calls refused with the sentence, ends when the updater does | the model saw the refusal; after the end the next call started the server again, without a new `tools/list`, and was served | the model saw the refusal; after the end every call answered *"Transport closed"* until a reload, a new thread or a resume |
| The same, and keeps serving after the updater ends | served by the same process, no relaunch | served by the same process, no reload |
| Holds `tools/list` until the updater ends, 1.3 to 2.8 s | waited, then served | waited, inside its 10 s startup timeout, then served. *Corrected 2026-10-03 by addition: the default is 30 s, measured at 0.155.0-alpha.9.2 and 0.160.0 ([above](#registering-with-codex-and-what-its-startup-timeout-costs----measured-2026-09-24)); under `codex exec` a server still starting after about a second misses the first turn* |
| Keeps the error, then sends `notifications/tools/list_changed` | with `"tools":{}` the notification was ignored and the session stayed toolless; with `"listChanged":true` it listed again and served | had already terminated the server |

**Two client habits found on the way.** `codex --version` writes
`<CODEX_HOME>\tmp\arg0` like any other verb, and a scratch Codex run clones
the curated plugins from GitHub and calls `chatgpt.com` at its start. Claude
Code rewrites a `tool_use` id that a conversation reuses across `--continue` to
*"[Tool use interrupted]"*.

⚠️ **Not established:** the Codex desktop app and an interactive Claude Code
with `/mcp`, which were not driven; and every shape in the table against a
BrowserAI built to do it, because only the stand-in did.

**Re-establish** with the rig: the run scripts drive each client against the
stand-in and the stubs, `runs-index.json` in the evidence lists all 51 runs, and
`texts.json` holds every sentence byte for byte.

## What Q261's refusal does at the other end -- measured 2026-09-24

`[FLOATS]` **Claude Code 2.1.281** and **codex-cli 0.155.0-alpha.9.2**, Windows
10.0.26200, against the **published** BrowserAI -- 1.1.1-alpha.0.64 for the runs
that established the behaviour and 1.1.1-alpha.0.65 for the three that re-took it
against the wording they corrected. Eleven runs, three rounds per claim; a local
API stub scripts the model's moves, so no credential is used and no inference
happens anywhere. Rig:
[`docs/probes/2026-09-24-q261`](../../docs/probes/2026-09-24-q261/README.md).
Captures:
[`docs/evidence/2026-09-24-q261`](../../docs/evidence/2026-09-24-q261/README.md).

⚠️ **The section above establishes the DEFECT; this one establishes what the
answer to it actually achieves.** They are separate measurements against separate
servers: that one drove a dummy server, this one drives the product.

⚠️ *Added 2026-10-03 @ Claude Code 2.1.288 by addition.* **The Claude Code runs
here had tool search off, and a first-party session has it on.** A custom
`ANTHROPIC_BASE_URL` turns it off, and the batch's own debug logs carry
`[ToolSearch:optimistic] disabled: ANTHROPIC_BASE_URL=http://127.0.0.1:<port> is not a first-party Anthropic host`.
So *"the model was offered 79 tool definitions on every turn"* below is a
property of that setting: a session against Anthropic's own API is offered the
deferred tools' names until a ToolSearch call loads them
([the client](#the-client-claude-code)). The frames, the refusal and the retry
are the wire's and hold either way; how the refusal reads to a model that had to
search for the tool first was not measured.

### The refusal fires on exactly the connection it was designed for, 3/3

**A Claude Code session whose BrowserAI exits after answering one call re-dials and
sends `initialize`, `notifications/initialized` and `tools/call` -- and no
`tools/list`.** So the per-connection flag is unset when that call arrives, the
call is refused once, `notifications/tools/list_changed` goes out ahead of the
refusal on the same pipe, and **the next call is forwarded and answered**. 3/3 in
`CCQ7`-`CCQ9`, read off the shim's wire log, which carries one line per frame with
the pid of the shim it passed through -- so *two server processes* is counted and
not inferred.

⭐ **The exact sentence reaches the model, once, as `is_error: true`.** Read out of
the API request bodies, which are literally what the model was sent: the refusal
appears as a `tool_result` in turn 3 and the ordinary answer to the same tool
appears after it in turn 4. It is also in the client's own `--debug-file` twice,
once at `[ERROR]` and once as `Tool 'browserai_list' failed after 0s`. The model
was offered **79** tool definitions on every turn of every run, unchanged --
which is the control that says nothing about the surface actually moved between
the two servers, so what the arms measure is the mechanism and not a real rename.

⚠️ **Two corrections from the independent re-measurement of 2026-09-24, both by
addition.** *(1)* **The 79 is BrowserAI's own list and not the turn's total, and
the turn's total is configuration-specific** -- *previously "the model was offered
**79** tool definitions on every turn", which reads as a property of the run.* A
re-run from a working directory inside this repository was offered **121**,
because the repository's committed `.mcp.json` servers connected as well;
`browserai`'s own contribution was 79 on every connection in both. The control
the sentence above rests on is unaffected -- what it needs is *unchanged across
the two servers*, which held -- but a reader comparing a future run's total
against 79 would be comparing two different configurations. *(2)* ⭐ **The premise
these runs are built on is simulated by process identity and never by a real
surface difference.** *"A tool the old server advertised has gone"* is what makes
the refusal worth having, and no rig here ever removed a tool: what every run
establishes is that a **second server process** answered the call, read off the
shim's per-frame pid. A measurement of a real rename would need two BrowserAI
builds with different surfaces, which nothing here has yet done -- so the
mechanism is measured and its premise is assumed.

### The list-changed notification does NOTHING on a re-dialled connection, 3/3

⚠️ **This is the finding that corrected the product's own wording before it
shipped.** `CCQ4`-`CCQ6` left **5.1 s of idle connection deliberately between the
refusal and the retry** -- the only window in which a refresh could land and still
be the thing that fixed the turn -- and **no `tools/list` reached the re-dialled
server at all**, in any of the three. `CCQ3` left the same window after the retry,
with the same result. The client's debug log carries
`Cleared connection cache for reconnection` and **no** refresh line; the string
`refreshing tools` appears **0** times in any of these runs.

**That does not contradict the 3/3 refetch measured on 2026-09-23** -- those runs
sent the notification to a connection the client had established and listed from,
unprompted, mid-session. A **transparently re-dialled** connection is a different
connection and behaves differently. Both are true; the second is the one Q261's
path meets.

**The consequence is the useful half.** On the one path this mechanism exists for,
the notification alone recovers nothing -- so the refusal is not belt-and-braces
beside it, it is the only thing that reaches the model. BrowserAI still sends the
notification, because it costs one frame and a client that honours it is helped,
and the refusal's wording now says *do not assume it refreshed anything* and no
longer promises a refresh.

### A Codex thread lists before it calls, so it never meets the refusal, 3/3

**`initialize`, `notifications/initialized`, `tools/list`, `tools/call`** -- in
that order, every time, driven through `codex app-server` with
`mcpServer/tool/call` and no model at all. The flag is therefore set before the
first call arrives, **no refusal is ever made and no notification is ever sent**,
and a second call on the same thread is answered normally. `CXQ1`-`CXQ3`. This is
why Codex needs the *new thread* remedy and not a retry: it is not that a
Codex thread meets the refusal and cannot recover, it is that a Codex thread never
meets it, and the case the remedy addresses is a thread whose server was replaced
and which Codex will not re-dial at all.

**Re-establish it** with `cc-q261.sh <run> <port> 5` and `cx-q261.sh <run>` out of
the rig, after `dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64
--self-contained`. Read `logs/<run>.wire.jsonl` for the frame order and the server
count, and `logs/<run>.requests.jsonl` for what the model was handed. ⚠️ **Never
against the real client configuration or the default app root** -- every run here
wrote inside its own scratch `CLAUDE_CONFIG_DIR` or `CODEX_HOME`, and every server
ran with `BROWSERAI_ROOT` at a scratch app root, because the stray sweep is
machine-wide and the default root is shared with an installed BrowserAI.

## What each client does to a stdio server when the session ends -- measured 2026-10-03

`[FLOATS]` **Claude Code 2.1.288** (the CLI) and **2.1.287** (the binary the VS
Code extension ships), **codex-cli 0.155.0-alpha.9.2** and **0.160.0**,
Windows 11. 491 runs of a purpose-built .NET server with one tool, which logs
its end of file and its own death on a QPC clock, delays its exit after end of
file by 0, 100, 1,000, 3,000, 6,000 or 15,000 ms, and keeps a stand-in child in
a `KILL_ON_JOB_CLOSE` job of its own; a harness opens a handle on every
descendant and records how and when each one died. Every client ran under a
scratch configuration against a local API stub, so no model was called. Q356 a.
[Evidence](../../docs/evidence/2026-10-03-client-exit/README.md),
[rig](../../docs/probes/2026-10-03-client-exit/README.md).

| How the session ended | Runs | End of file reached the server | How the server died, when it did not exit first |
|---|--:|---|---|
| Claude Code, every graceful end: `-p` finishing, stream-json input closed (CLI and VS Code binary), `/exit`, Ctrl+C twice, the terminal closed, an in-flight call interrupted, the SDK's `close()` | 168 | **168 of 168** | `taskkill /T /F`, exit code 1, **533 to 1,154 ms after the end of file**; per-scenario medians 681 to 911 ms |
| `claude` itself killed (CLI and VS Code binary) | 72 | **0 of 72** | with `claude`'s own job, exit code 0, **2.2 to 18.9 ms** after `claude` |
| The VS Code extension host exiting, through a stand-in for it | 18 | 0 of 18 | `claude` died 2.0 to 5.9 ms after the host and the server 0.0 to 7.7 ms after `claude`, by job |
| `codex exec` finishing | 24 | **0 of 24** | its own job terminated, exit code 1, **111 to 348 ms before** `codex exec` itself exited |
| `codex app-server`, its stdin closed | 24 | **0 of 24** | its own job terminated, exit code 1, 2.4 to 10.7 ms after the close |
| `codex app-server` killed | 12 | 1 of 12 | with the app-server's job, exit code 0, 5.4 to 49.5 ms after it |

⭐ **In Claude Code's graceful exits a server has about half a second.** A server
that exits within 0 or 100 ms of the end of file got out by itself in every run;
one that needed 1,000 ms was killed in most runs and got out in four; every
server that needed 3,000 ms or more was killed. **The order is the client's
own:** read at offset 238,874,235 of the 2.1.288 bundle, its MCP cleanup starts
`taskkill.exe /PID <pid> /T /F` without waiting for it and only then closes the
transport, which is the end of file. The measured runs agree: in all 166 runs
where the harness saw it, the `taskkill` process was created 5.2 to 112.6 ms
before the server saw its end of file. The SDK transport's
own close, which would wait 2 s before a `SIGTERM`, is never what ends the
server. The 2.1.287 binary carries the same cleanup.

⭐ **Codex never closes a server's stdin. It terminates the server's job, at
once.** Read in `openai/codex` at `rust-v0.155.0-alpha.9.2`: each stdio server
starts in a job created with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
(`codex-rs/utils/pty/src/win/job.rs:62-64`), and shutdown calls
`TerminateJobObject(job, 1)` (`:218`) through
`stdio_server_launcher.rs:139-140` and `:442`. 0.160.0 measured the same. This
is consistent with
[what Codex hands a stdio server](#what-codex-hands-a-stdio-server-and-how-it-ends-one----measured-2026-09-24),
whose fourth finding saw the BrowserAI server gone about 100 ms after the
app-server's own end of file.

**What it means for BrowserAI, read and not measured here:** a clean shutdown
starts with end of file, closes node's stdin and waits up to 5 s for it. Under
Claude Code that wait is cut at about half a second by the tree kill, and under
Codex and a killed Claude Code it never starts. What a browser loses when it is
killed and not closed is
[a separate measurement](../playwright/provisioning-and-timings.md#a-browser-server-that-ends-itself-loses-the-same-stores-as-one-that-is-killed----measured-2026-09-22).

⚠️ **Not established.** The VS Code extension itself was not driven: a node
stand-in for its host was written from a reading of its `extension.js`. The Codex
desktop app was not driven either. Only one delay grid was run, so where between
100 and 1,000 ms a Claude Code server stops getting out is a range and not a
figure.

**Re-establish** with the rig: `tools\gen.ps1` writes a batch for the scenarios
and delays, `ExitRig.exe run` drives it, `tools\analyze.py` writes one row per
server instance and `tools\summarize.py` and `tools\ranges.py` print the table.
The positive controls in `runs\controls` are the first thing to run: each
signature the analysis reads, planted by a fake client.

## The clients' own registration commands in a sandbox, and the tools that would register for them -- measured 2026-09-25

`[FLOATS]` **Claude Code 2.1.282** and **codex-cli 0.155.0-alpha.9.2**, copied into
scratch and run under a sandbox home named `Zoë O'Brien`, to put a space, an
apostrophe and a non-ASCII letter in every path, with a seeded configuration
holding comments, trust tables and another install's entries. No `claude mcp
get` or `list`, which start servers. Taken for the zoom-out's track A, whether a
library or tool should register BrowserAI; the answer was no, and on 2026-10-01
the maintainer chose a small command-line program of his own (Q330 to Q333 in
[`DECISIONS.md`](../../DECISIONS.md)).
[Evidence](../../docs/evidence/2026-09-25-registrar-survey/README.md),
[rig](../../docs/probes/2026-09-25-registrar-survey/README.md).

**The clients' own commands, which BrowserAI drives:** Claude Code's `add` and
`remove` took 242 to 365 ms each, and Codex's `add`, `remove`, `list` and `get`
79 to 128 ms, against the 613 to 671 ms recorded at Claude Code 2.1.233
([above](#registering-browserai-with-the-client)). Claude Code refused a second
`add` with *"already exists"* and a second `remove` with *"No MCP server named"*,
and kept a project entry's `${LOCALAPPDATA}` as written; Codex succeeded every
time and stored the apostrophe path as a `'''...'''` literal that read back
intact. ⚠️ **Codex's own `add` rewrites the server tables**: the comment above
another server's table, a comment at the end of one of its lines and the project
file's header comment were gone, and `startup_timeout_sec = 20` came back as
`20.0`; the comments at the top of the file survived. ⚠️ **Claude Code resets a
configuration it cannot parse**: with `~/.claude.json` not valid JSON it moved the
file to `backups\.claude.json.corrupted.<ms>` and started a fresh one.

⛔ **What the third-party registrars did is kept as why none was adopted.** All
three were run against the same sandbox:

| Tool | What it did |
|---|---|
| add-mcp 2.4.0 | Correct JSON and TOML escaping, and a second add left the files byte-identical. But it ignored `CLAUDE_CONFIG_DIR`, so Claude Code then reported no such server; it rewrote the Codex file from scratch, comments gone, with a plain write; it refused a mixed-type array, which is valid TOML 1.0 and which Codex reads; its command line turned both of BrowserAI's project spellings into `npx -y <spelling>`; with no `~/.claude` folder its list returned nothing for Claude Code and its remove left the entry and reported success; and it replaced another install's `browserai` entry silently |
| APM 0.31.0 | Refused a path with a space on its command line; started Git Credential Manager, which put sign-in windows on the desktop, until git's credential helpers were disabled; rewrote the whole `~/.claude.json`, writing `ë` as `ë`; kept Codex's comments but added `id = ""` and an empty `env` table; looked the name up at `api.mcp.github.com` on every install; and reported another install's entry as already configured |
| install-mcp 1.10.2 | Split the path at the space in both clients and reported success, and on the TOML 1.0 file wiped the Codex configuration down to one broken `browserai` table |

add-mcp **2.4.1** still ignored `CLAUDE_CONFIG_DIR` against Claude Code 2.1.288 on
2026-10-03 ([evidence](../../docs/evidence/2026-10-03-upstream-reports/README.md));
that is [neon-solutions/add-mcp#130](https://github.com/neon-solutions/add-mcp/issues/130).

**Read and not run, on 2026-10-01, in a second look that tried to find the
wheel:** kurir 0.3.0 registers but has no remove and no presence check;
agent-install 0.0.8 shares add-mcp's gaps; the newest self-installing products,
Blender's and Serena's among them, drive `claude mcp add` and `codex mcp add`
and read the files only to classify what is there; and no MCP SDK ships a
registrar. Neither client offers an all-users registration of a local
executable, so *system wide* means per user.

⚠️ **Not established:** whether Claude Code launches an entry add-mcp wrote
without a `type`; a write racing a running Claude Code on `~/.claude.json`;
codex-cli 0.157.0, which was read and not run; and APM's project writes after
`apm init`.

**Re-establish** with the rig: `sandbox-env.sh` builds the sandbox home,
`seed.sh` writes the seeded configuration, and `snap.sh` copies every
configuration file after each step; each step's output and the copies are in the
evidence's `first-pass/sandbox/`.

## How much of a tool result each client hands its model -- measured 2026-10-03

`[FLOATS]` **Claude Code 2.1.288** through `claude -p` and through the
stream-json transport with the VS Code extension's environment, and
**codex-cli 0.160.0** and **0.155.0-alpha.9.2** through `codex exec`, Windows 11.
74 runs, 2026-10-03 from 23:24Z to 23:34Z, of a stub MCP server whose one tool
answered with a text of a chosen length, made from BrowserAI's real tools array
between a start marker and an end marker, either as an error result
(`isError: true`, the form a refusal takes) or as an ordinary one. Every client
ran with its configuration in a scratch folder against a local API stub that
recorded what the model was sent, so no model was called. Lane q371, for the
maintainer's Q369.3 c and Q371.6 c.
[Evidence](../../docs/evidence/2026-10-03-q371-refusals-and-transcript/README.md).

| Client | Error result | Ordinary result |
|---|---|---|
| Claude Code, both transports | **Whole up to 10,100 characters**, 9 of 9 runs from 9,000 to 10,100. **12,000 and more are cut to 10,039 to 10,041**: the first and the last 5,000 or so, with `... [N characters truncated] ...` between them, 18 of 18 runs from 12,000 to 120,000 | **Whole up to 50,000**, 12 of 12 runs. At 60,000 and 120,000 the result is written to a file and the model gets `<persisted-output>` with the file's path and a preview of about 2,000 characters, 3 of 3 |
| Codex, 0.160.0 and 0.155.0-alpha.9.2 alike | **Whole up to 10,100**, after its own 34-character `Wall time ... Output:` prefix. **12,000 and more are cut to 12,018 characters, prefix included**, keeping both ends around a marker that counts tokens | The same as an error result: Codex does not tell the two apart in what it sends |

⚠️ **This is a different budget from [the 2,048 characters per
description](#what-2kb-each-means----measured-2026-08-18--claude-code-21234)**:
that one applies to the `instructions` and to each tool description; this one to
what a call returns. **What it decided:** a refusal that appended BrowserAI's
whole current tool list, 110,386 characters for 73 tools on 2026-10-04, would
reach a model with its middle cut out through both clients, so it was not built,
and the maintainer has the measured limits instead. Every refusal BrowserAI
writes is held under 10,000 characters, `ClientTruncationBudget.ErrorResultCharacters`,
and the largest, the refusal of an argument a schema does not have with the
tool's whole definition, is checked against it for every tool by
`UnrecognisedArgumentTests`.

⚠️ *Added 2026-10-04 by addition.* **What the maintainer chose instead, 2 b**:
the two refusals name every current tool with one line saying what it does.
Measured through the published binary that day, BrowserAI 1.1.1-alpha.0.206 with
72 tools: the refusal for a tool BrowserAI does not have was **5,454 characters**,
and Q261 b's refusal, in the wording a client named `claude-code` gets, **6,002**.
`ToolListInRefusalsTests` holds the longest spelling of both under the budget.

⚠️ **Not established.** The exact cut points: Claude Code's lies
somewhere between 10,100 and 12,000 for an error result and between 50,000 and
60,000 for an ordinary one, and Codex's at or just below 12,000; no run sat
between. Whether the cut is counted in characters or in tokens. The VS Code
extension itself and the Codex desktop app were not driven.

**Re-establish** with the batch's rig: `node sizes.js <batch> <clients> <sizes>
<repeats>` starts both API stubs, runs each client against `bigserver.js` at each
size, and writes `results.json` with the length each model was sent and its first
and last 300 characters. Row 192 carries it.

## An argument a tool's schema does not have -- measured 2026-10-03

**Before 2026-10-04 it was dropped without a word, and the call ran.** Measured
2026-10-03 at 23:19Z through BrowserAI 1.1.1-alpha.0.188 published at
`d8a0101a`, `@playwright/mcp` 0.0.83: `browserai_list` with an extra
`bogusArgument` answered as if it had not been sent, and so did
`browser_navigate`, whose page loaded. BrowserAI forwarded the argument as it
came, and upstream's schema validation dropped it, which is what a zod object
does with a key it does not declare. A caller that misspelled an argument, or
sent one a later build had renamed, was never told.
[Evidence](../../docs/evidence/2026-10-03-q371-refusals-and-transcript/README.md).

**Since 2026-10-04 it is refused before anything runs.** The maintainer's
decision of 2026-10-03, in his words: *"I'd expect that any call carrying any
parameter or argument that we do not recognize would be refused actively with a
syntax error. This would teach the LLM it has somethign wrong. Also, I do not
like us keeping history and translating certen arguments for historical sake.
The product is what it is and the llm needs to learn to use it."* BrowserAI
checks every argument name a call carries against the tool's schema as its own
`tools/list` serves it, the session and why it adds included, and answers an
error result that names every argument the schema does not have and gives the
tool's whole current definition, Q371.5 b. Measured again on 2026-10-04 at 00:10Z
through the build that does it: both calls above, and `browserai_init` with the
old `tracing`, were refused that way. Upstream's own validation is not changed
and is now not reached for an unknown name. What a call leaves out is not
checked: upstream marks some arguments that have a default as required, five of
them in the snapshot read 2026-10-04, `browser_take_screenshot`'s `scale` among
them, and refusing those would refuse calls upstream accepts.
`UnrecognisedArgumentTests` holds the refusal for an authored tool and for a
forwarded one, that the list the caller was given decides, and the refusal's
size against the budget above for every tool.

## The new opening request, and the one revision BrowserAI offers -- measured 2026-10-08

`[FLOATS]` Claude Code **2.1.285 to 2.1.289** for the failures, **2.1.288 and
2.1.294** for the code read below, `ModelContextProtocol` **2.2.0**, codex-cli
**0.155.0-alpha.9.2 and 0.160.0** with `rmcp` **3.2.0**, the installed BrowserAI
**1.1.0**, Windows 11. [Rig](../../docs/probes/2026-10-08-protocol-pin/README.md).

**From 2026-09-30T10:52Z Claude Code opened its stdio servers with
`server/discover` at `2026-07-28`, and the installed 1.1.0 accepted a revision it
could not serve.** BrowserAI set `McpServerOptions.ProtocolVersion` to `null`,
which offers every revision the SDK implements, and 2.2.0 implements
`2026-07-28`. So the client chose it, and BrowserAI's own `tools/list` answer,
rewritten from a child pinned at `2025-11-25`, carried no `resultType`. The
client's log reads *"tools/list failed (Invalid result for tools/list: missing
required resultType ... servers implementing protocol revision 2026-07-28 MUST
include it ...)"*, retries at 250, 500 and 1,000 ms and ends at *"Failed to fetch
tools"*: the session keeps BrowserAI's instructions and has no BrowserAI tool.
Counted from the client's own MCP logs, read and never written: of the **153**
connections to the installed server that started from 2026-09-30T10:52:31Z to
2026-10-04T17:56:02Z, in 12 project folders, **145 failed with the `resultType`
error**, 2 failed otherwise and 6 worked. Every failure had negotiated
`2026-07-28`; the six that worked had opened with `initialize` at `2025-11-25`.
The 443 connections from 2026-09-16 up to the first failure all opened with
`initialize` at `2025-11-25`. Where a connection's session transcript could be
matched, 73 of the 153, it names 2.1.285 to 2.1.289 for 68; five carry older
stamps and 80 have no transcript left. First counted 2026-10-04 at about 18:00Z
and re-counted 2026-10-08 to the same figures.

**No run of the suite could see it.** Every Claude Code the suite starts runs
under a scratch configuration folder, and those opened with `initialize`
throughout, 2026-10-04 included. The client carries switches named
`tengu_mcp_protocol_negotiation_stdio`, `_http`, `_claudeai` and `_ccr`, present
in 2.1.294, which reads as a rollout a scratch configuration does not receive.
That is an inference from the names and is not established.

**Since 2026-10-08 the caller-facing server offers exactly `2025-11-25`.** With
`ProtocolVersion` set, 2.2.0's supported list is that one revision
(`GetConfiguredSupportedProtocolVersions` in `McpServerImpl.cs` at `v2.2.0`), and
a request whose per-request `_meta` names another revision is refused with
`-32022`, `UnsupportedProtocolVersionError`, with `data.supported` set to
`["2025-11-25"]` and `data.requested` to what was asked. `initialize` then
answers `2025-11-25` whatever the client offered: the handler returns the
configured revision and echoes the client's only when none is configured. The
2025-11-25 lifecycle lets a server answer the one revision it supports, and the
client decides whether to go on.
`ProtocolSplitTests.TheNewOpeningRequestIsRefusedWithTheOneRevisionBrowserAiOffersAndTheOldOneListsTheTools`
sends Claude Code's opening frames to the published binary and holds all of it;
against the unpinned build the same frame was answered with a `server/discover`
result offering `2026-07-28`, and an `initialize` at `2025-06-18` with
`2025-06-18`.

**Claude Code falls back to `initialize` on that refusal, read in its own code.**
A text search of the 2.1.288 and 2.1.294 binaries finds the same handler for an
error answer to `server/discover`. On `-32022` it reads `data.supported`: with no
list it falls back; with a revision of `2026-07-28` or later that it speaks, it
retries at that revision; with only later revisions it does not speak, it fails;
and with no revision of `2026-07-28` or later it falls back to `initialize` when
it speaks an earlier one, which it does. Every other error code falls back too,
which is why a server with no `server/discover` at all works. **So the list has
to be exactly `["2025-11-25"]`**: naming `2026-07-28` in it would send the client
straight back to the revision BrowserAI cannot serve.

**The installed stopgap carries the same pin on 1.1.0 since 2026-10-04T18:12Z.**
Of the 60 connections to it that started from 2026-10-04T19:26Z to
2026-10-08T12:05Z, in 8 project folders, 55 opened with `initialize` at
`2025-11-25` and listed the tools, and 5 did not, every one of them while the
client itself was shutting down. One log of the failure window holds both
halves: its connection failed at 13:36Z on 2026-10-04, and the same session
reconnected at 18:13:44Z, opened with `initialize` and served every call.

**Codex is not affected, read in its own code.** For a stdio server codex-cli
0.155.0-alpha.9.2 and 0.160.0 send `initialize` at `2025-06-18` unless the
server's own entry sets `CODEX_MCP_PROTOCOL_VERSION` to `2026-07-28` and the
client's modern mode is on (`protocol_mode.rs` in `codex-rs/rmcp-client`), and
`rmcp` 3.2.0's `legacy_startup` keeps whatever revision the server answers
without checking it (`crates/rmcp/src/service/client.rs` at `rmcp-v3.2.0`). Its
own log of 2026-10-02 shows 1.1.0 answering it `2025-06-18`; the pinned build
answers `2025-11-25`. ⚠️ **Its modern mode would fail**: `rmcp`'s automatic
lifecycle treats `-32022` as a version negotiation and gives up when the list
holds no revision it prefers, so a Codex in that mode would not fall back. No
BrowserAI registration opts in to it.

**Implementing `2026-07-28` is [`TODO.md`](../../TODO.md)'s**, and the condition is
[a hazard row](../../HAZARDS.md#hazard-index): a protocol revision BrowserAI
offers but does not implement.

**Re-establish** with [the rig](../../docs/probes/2026-10-08-protocol-pin/README.md):
`count.py` counts the client's own logs in a window and `clientver.py` matches
each connection to its transcript, both printing counts only; `opening.py` sends
the two openings to any BrowserAI server; and the test above is the standing
check on the server's half.

## A windowless server under both clients -- measured 2026-10-04

`[FLOATS]` on the clients' releases: Claude Code **2.1.288** (`-p`, and the VS Code
extension's stream-json transport driven as the client-exit rig drives it), codex-cli
**0.155.0-alpha.9.2** and **0.160.0** (`exec` and `app-server`), Windows 11 Pro
10.0.26300. Taken 2026-10-04 between 01:19Z and 01:56Z with a stub and not BrowserAI's
binaries, every client under a scratch configuration against local API stubs.
Everything it was read from, with the stub and the rig:
[`docs/evidence/2026-10-04-onebinary-measure`](../../docs/evidence/2026-10-04-onebinary-measure/README.md).
It answers whether one program with no console of its own can be the server a client
starts. **It can, and each client ends it as it ends a console one.**

**The stub** was BrowserAI's own stdio code, `StdioChannel`, `JsonLinesTransport`,
`JsonLines`, `DirectStdioServerTransport` and `VerbatimPayload`, copied byte for byte
from `e30380ac` and checked by SHA-256, under `McpServer.Create` from
`ModelContextProtocol` 2.2.0, with stderr through the same console logger line as
BrowserAI's process log. It was published NativeAOT twice, as a Windows-subsystem
program (PE subsystem 2) and as a console one (3), with `Microsoft.Extensions.Logging`
10.0.12 and ILCompiler 10.0.12 under SDK 10.0.401. Its `big` tool answers exactly
70,000 bytes of UTF-8 with 2-, 3- and 4-byte characters. After the end of its input it
waited 3 s and exited with code 42, so every run shows whether the client let it leave
or ended it.

**Every cell, 3 of 3, both forms:** `initialize`, `tools/list` and `tools/call big`
were served; the 70,000 bytes reached the client with their SHA-256 unchanged; stderr
reached the client; nothing was left running after the client; and no window
appeared and the server owned none. `initialize` arrived 8 to 14 ms after the server
started, and the 70 KB answer left it in 3 to 7 ms.

| Client, and how it ended | Form | Client start to server start | Ended by | Timing | Exit code |
|---|---|---|---|---|---|
| Claude `-p`, normal | windowless | 710 to 798 ms | `taskkill /PID /T /F`, then stdin closed (the end of input read 5.4 to 6.8 ms after the taskkill started) | killed 533 to 580 ms after the taskkill started | 1 |
| | console | 736 to 847 ms | the same | 510 to 620 ms | 1 |
| Claude VS Code transport, stdin closed | windowless | 688 to 764 ms | the same | killed 491 to 587 ms after stdin closed | 1 |
| | console | 664 to 748 ms | the same | 559 to 612 ms | 1 |
| Claude VS Code transport, client terminated | windowless | 628 to 656 ms | Claude Code's job closing | 2.4 to 4.0 ms after the client died | 0 |
| | console | 604 to 738 ms | the same | 2.6 to 4.3 ms | 0 |
| `codex exec` 0.155 | windowless | 446 to 464 ms | the job ended, stdin never closed | 133 to 142 ms before Codex exited | 1 |
| | console | 432 to 497 ms | the same | 119 to 137 ms | 1 |
| `codex exec` 0.160 | windowless | 446 to 506 ms | the same | 127 to 136 ms | 1 |
| | console | 436 to 807 ms | the same | 116 to 141 ms | 1 |
| app-server 0.155, stdin closed | windowless | 462 to 507 ms | the job ended | 2.4 to 6.3 ms after its stdin closed | 1 |
| | console | 459 to 722 ms | the same | 1.4 to 3.0 ms | 1 |
| app-server 0.160, stdin closed | windowless | 441 to 471 ms | the same | 1.6 to 1.9 ms | 1 |
| | console | 455 to 533 ms | the same | 2.1 to 11.3 ms | 1 |
| app-server 0.155, terminated | windowless | 427 to 503 ms | Codex's job closing | 27 to 44 ms after it died | 0 |
| | console | 445 to 600 ms | the same | 27 to 32 ms | 0 |
| app-server 0.160, terminated | windowless | 440 to 548 ms | the same | 11 to 18 ms | 0 |
| | console | 414 to 511 ms | the same | 12 to 17 ms | 0 |

No console event is involved in any of these ends.

**How each client starts its server**, from the stub's own report of its
`STARTUPINFO`, its handles and its console:

- **Claude Code** passes `STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES` with `SW_HIDE`.
  The console form got a console of its own with no window and a `conhost` child,
  which is consistent with `CREATE_NO_WINDOW`; the flag itself was not read.
- **Codex 0.155** passes `STARTF_USESTDHANDLES` alone, and its console form shares
  Codex's own console.
- **Codex 0.160** passes the same flags, and its console form got a console of its
  own.
- **The windowless form, under all three**, has no console and no `conhost`, its
  console code pages read 0, and `Console.OpenStandardInput` and
  `Console.OpenStandardOutput` return a `WindowsConsoleStream` over the pipes, as the
  console form's do.

**stderr.** Codex logged every stderr line it received, in both forms. Claude Code
put one `[ERROR] "Server stderr"` entry in its debug log carrying only the first
chunk, in both forms.

**What the model was handed of the 70,000 bytes**, the same in both forms: Claude
Code saved the result to `tool-results\*.json` and handed the model a 2,519-byte
preview, and Codex handed it 12,021 bytes.

**No window, and how that was checked.** Every 25 ms every top-level window on the
desktop was compared with a baseline of 129, because with Windows Terminal as the
default terminal a console's window belongs to Windows Terminal, and watching the
run's own processes would miss it; every window the run's tree owned was recorded,
visible or not. A hidden window the rig created was found and read as not visible at
the start of every batch, and the desktop scan also caught one real foreign window.
Over 54 runs and 7,862 scans: no window from any run, none owned by a server, and no
`conhost` under a windowless server.

**What it does not establish.** The one binary itself, which did not exist yet;
either client's terminal UI; the real VS Code window, which was emulated; and a
console-less parent starting the console form, the one case only the windowless form
avoids a window in, which was not run because it would have put a window on the
screen.

**Re-establish it** with the batch's `stub/` and `rig/`: the stub published both ways,
and the client-exit rig with the four files it gained here (`WindowWatch`, `RootWatch`,
`Feedback`, `Launch`), one cell at a time, every client from that rig's copies under a
scratch configuration; compare against the batch's `runs/m1/cells.tsv` and
`runs/m1k/cells.tsv`.

## When each client's first turn goes out, and a call held behind it -- measured 2026-10-04

`[FLOATS]` Claude Code **2.1.288** (`-p`, and the VS Code stream-json transport),
codex-cli **0.155.0-alpha.9.2** and **0.160.0** (`exec` and `app-server`), against a
stand-in server and never BrowserAI, every client under a scratch configuration and
against local API stubs, its model a script. Taken 2026-10-04 between 00:26Z and
02:07Z. Everything it was read from:
[`docs/evidence/2026-10-04-startup-measure`](../../docs/evidence/2026-10-04-startup-measure/README.md).

### The first turn waits about 1.3 s in Codex and about 2 s in Claude Code

A conversation's first request to the model carries only the tools of the servers that
had answered by then. How long each client waits for them:

| Client, and how the server is registered | When the first request went out | What it carried |
|---|---|---|
| Codex 0.155 and 0.160, `exec` and `app-server`, the server not `required` | 1.23 to 1.36 s after the server started | none of the tools of a server that answered after 3 s, 12 of 12 |
| Claude Code 2.1.288 at user scope, in `~/.claude.json`, which is how RegisterAI registers BrowserAI; `-p` and the VS Code transport | 1.91 to 1.98 s after the server started | none of the tools of a server that needed 3 s or 10 s, 12 of 12 |
| Claude Code 2.1.288 through `--mcp-config` | up to 30 s | the tools of a server that took 3 s or 10 s, 12 of 12; none from one that took 35 s, 6 of 6, the first request at 29.9 s |

**Read in the clients:** Claude Code's binary carries `var woe=2000` at byte
211,693,688, near "MCP startup is otherwise non-blocking by default" at byte
205,419,229; Codex's `DEFAULT_OPTIONAL_MCP_STARTUP_GRACE` is one second
(`codex-mcp/src/mcp/mod.rs:193` at 0.155, `:195` at 0.160), counted from the turn's
start, which is about 0.3 s after the server is spawned. **How a server is registered
decides how long Claude Code waits for it**, so a stand-in registered through
`--mcp-config`, as earlier rigs here registered theirs, overstates how patient Claude
Code is with BrowserAI as it is really registered. The Codex half extends the
`codex exec` reading of 2026-10-03 above, 4 of 4, to `app-server` and to 12 runs.

### A held call: both clients wait for it, and deliver it

A stand-in that answered `initialize` and `tools/list` at once and held the first
`tools/call`:

| Held for | Claude Code 2.1.288, `-p` and the VS Code transport | Codex 0.155 and 0.160, `exec` and `app-server` |
|---|---|---|
| 1 s | waited and delivered, 6 of 6 | 12 of 12 |
| 5 s | 6 of 6, and 6 of 6 more at user scope | 12 of 12 |
| 30 s | 6 of 6 | 12 of 12 |
| 90 s | 6 of 6 | 12 of 12 |

In all 78 runs the first request carried the tools, and **no client cancelled a
call**: no `notifications/cancelled` frame in any stand-in run, against 176
`tools/call` frames as the positive control. **Claude Code sends a `tool_progress`
heartbeat every 30 s** while it waits (at 30, 60 and 90 s), and the VS Code extension
receives them; read in its binary, a stdio call is abandoned after 30 minutes with no
answer, and the default hard limit is 100,000,000 ms. **Codex** shows the call as in
progress, and the model then reads "Wall time: 90.02 seconds" ahead of the output; its
default tool timeout is 300 s (`rmcp_client.rs:104` at 0.155, `:106` at 0.160).

### What the model is shown when a held call fails

| The server answers with | Claude Code, 6 runs each | Codex, 12 runs each |
|---|---|---|
| an `isError` result | the sentence, with `is_error: true` | "Wall time: ... / Output: " and then the sentence; the item is marked `failed` |
| a JSON-RPC error | the sentence, with `is_error: true` | "tool call error: tool call failed for `browserai/browserai_list` / Caused by: / Mcp error: -32603: " and then the sentence |
| nothing, because the server exits | "Connection closed", with `is_error: true` | "... Caused by: Transport closed" |

So a server that answers a held call with a sentence is heard by the model in both
clients, and one that dies tells the model nothing.

### Codex with `required = true`

| The server | `codex exec` | `codex app-server` |
|---|---|---|
| answers after 3 s | waits; the first turn has the tools, 6 of 6 | the same, 6 of 6 |
| dies on `initialize` | exits with code 1 before any model request: "Error: thread/start: thread/start failed: error creating thread: Fatal error: Failed to initialize session: required MCP servers failed to initialize: browserai: handshaking with MCP server failed: connection closed: initialize response", 6 of 6 | `thread/start` returns that error and no thread is created, 6 of 6 |
| answers after 35 s | exits with code 1 at 30 s: "timed out handshaking with MCP server after 29.9999955s", 6 of 6 | fails the same way, 6 of 6 |
| answers at once, the control | normal, 6 of 6 | normal, 6 of 6 |

`required = true` trades a first turn without the server for a Codex that opens no
conversation at all while the server is broken, or while an update ends it during its
start.

### Read, and not run, on 2026-10-04

- **Claude Code 2.1.288's own text for `alwaysLoad`**, found by a text search of the
  client binary: "When true, all tools from this server are always included in the
  prompt and never deferred behind tool search ... As a side effect, true also blocks
  startup until the server is connected (capped at the standard 5s connect
  timeout)". The per-tool `_meta` form of the key is described only in its `false`
  direction.
- **`codex mcp add` writes `required: false`** at `rust-v0.155.0-alpha.9.2`, and its
  arguments offer no flag for it (`codex-rs/cli/src/mcp_cmd.rs`). The 0.160.0 copy
  read had no `cli` folder, so 0.160 was not read.
- **Codex 0.160's `startup_readiness = "catalog"`** (`config/src/mcp_types.rs:219-226`
  and `:248-256`) uses a cache that lives in one process
  (`tool_catalog_cache.rs:33-38`), so it cannot help `codex exec`'s first turn; 0.155
  has no such key, and whether 0.155 accepts it was not checked.

**What it does not establish.** Claude Code's interactive terminal with a slow server,
the real VS Code window and the Codex TUI and desktop app; what a real model does with
the failure sentences; and the server half of a lazy start, which was not built.

**Re-establish it** with the batch's `rig/` (`lazysrv.js.txt` is the stand-in,
`gen-cc.js.txt`, `ccmodel.js.txt`, `cx-run.js.txt` and `cxmodel.js.txt` the drivers and
model stubs, `ccsum.py.txt` and `cxsum.py.txt` the tables), one batch at a time with
each client under a scratch home; compare against each batch's `table.tsv` under
`runs/`.

## What a stdio server can see of the client that started it -- measured 2026-10-08

`[FLOATS]` on the clients' releases: Claude Code **2.1.294**, the installed CLI, and
**2.1.292**, the binary the newest installed VS Code extension bundles; codex-cli
**0.161.0**, npm `latest` that day, and **0.159.0-alpha.12.1**, the binary the running
Codex desktop app uses as its app-server. Windows 11 Pro 10.0.26300. Taken 2026-10-08
with a stub server and local API stubs, every client under a scratch configuration,
no window shown: **41 counted runs**, 21 of Claude Code and 20 of Codex, at least three
per client and more on the weak spots. Everything it was read from, with the stub and
the rig: [`docs/evidence/2026-10-08-client-id`](../../docs/evidence/2026-10-08-client-id/README.md).
It answers what H1-T of [the one-binary design](../../docs/design/one-binary/README.md)
needs before an update: **which of a relay's clients will need a person to reconnect
it.** ⚠️ It records what can be told apart and how well; **which test the relay uses
is the maintainer's choice and was not made** when this was written.

| Signal | (1) Claude Code, terminal UI | (2) Claude Code, VS Code transport | (3) `claude -p` | (4) `codex exec` | (5) `codex app-server` |
|---|---|---|---|---|---|
| `clientInfo` name and title | `claude-code`, `Claude Code`, 4 of 4 | the same, 4 of 4 | the same, 10 of 10 | `codex-mcp-client`, `Codex`, 4 of 4 | the same, 8 of 8 |
| `clientInfo` version | 2.1.294 | 2.1.292, the extension's own binary | 2.1.294 | 0.161.0 | 0.161.0 and 0.159.0-alpha.12.1 |
| Protocol revision and capabilities | `2025-11-25`, roots and elicitation, the same in all 21 Claude Code runs | the same | the same | `2025-06-18`, `codex/auth-change` and elicitation, the same in all 20 Codex runs | the same |
| What it sends | a `server/discover` probe, then `initialize` and `tools/list`, 21 of 21; `roots/list` answered with the working directory | the same | the same | `initialize` and `tools/list`, no roots | the same |
| `CLAUDE_CODE_ENTRYPOINT` | `cli`, 4 of 4; a value already set is kept: `claude-vscode`, 3 of 3 | `claude-vscode`, 4 of 4, which the extension sets | `sdk-cli`, 7 of 7; started from inside a VS Code session's shell, `claude-vscode`, 3 of 3 | not set | not set |
| Variables the client adds | `AI_AGENT=claude-code_<version>_harness`, `CLAUDECODE=1`, the session id, a messaging pipe and its token, `CLAUDE_PROJECT_DIR` and `SHELL`, the same in (1), (2) and (3) | the same | the same | none: only an allowlist of 19 passes, and a marker variable was dropped 20 of 20 | the same |
| The environment it was started with | passed through whole, the marker 21 of 21 | the same, with the extension host's own variables | the same | not passed | not passed |
| The parent process and its arguments | `claude.exe` with no argument, 7 of 7 | `claude.exe --output-format stream-json --verbose --input-format stream-json ...`, 4 of 4 | `claude.exe -p ...`, 10 of 10 | `codex.exe exec ...`, 4 of 4 | `codex.exe app-server`, 8 of 8 |
| Console, start flags, job and pipes | no console window in any run, 41 of 41; Claude Code: flags `0x101` with the window hidden, the server in `claude.exe`'s job, named pipes `\uv\N-<pid of claude>`, stdin closed at the end, 21 of 21 | the same | the same | flags `0x100`, a kill-on-close job of its own per server, unnamed pipes, the server terminated with no end of input, 20 of 20 | the same |

**What does not tell them apart.** (1), (2) and (3) are identical on the wire, 21 of 21;
the version differs only because the extension bundles its own binary. (4) and (5) are
identical on the wire and in the environment, 20 of 20, and the host's own
`clientInfo` is not passed to the server, 8 of 8. `CLAUDE_CODE_ENTRYPOINT` tells the
three Claude Code modes apart in the 15 clean runs and reads `claude-vscode` in 6 of 6
runs that inherited it, because Claude Code keeps a value already set and rewrites only
an inherited `cli` to `sdk-cli` (read in the 2.1.294 bundle). For a terminal session
that is the harmful direction: it would say no reconnect is needed where one is. **The
environment alone classifies 15 of the 41 runs correctly.**

**What does, 41 of 41.** `clientInfo`'s name, then the parent process: its pid, kept
only if it was created before the server, and its command line read through
`NtQueryInformationProcess` class 60 with limited-query access, split by Windows'
argument rules. For `claude-code`: none of `-p`, `--print`, `--input-format`,
`--output-format`, `--sdk-url` and `--init-only` is the terminal UI; `-p` or `--print`
is (3); otherwise an entrypoint of `claude-vscode` is (2), and any other is another
host, named by its entrypoint. For `codex-mcp-client`: the first argument that is not
an option, skipping the values of `-c`, `--enable`, `-m`, `-p`, `-C` and the like, is
`exec`, `e` or `review` for (4) and `app-server` for (5), unless `--managed-daemon` is
present, which marks the Codex terminal UI's shared background server; no subcommand
is the Codex terminal UI started with `--no-daemon`. The same test, run against two
live processes on the machine, named the VS Code session's own `claude.exe`
(extension 2.1.288) as (2) and the running Codex desktop app's server as (5); the
latter's real command line is `-c features.code_mode_host=true app-server
--analytics-default-enabled -c ...`, so its subcommand is its third argument.

**Three more findings of the same runs.**

- ⭐ **Codex 0.161.0's terminal UI keeps its servers alive after it closes.** By default
  it runs them inside a shared background server it installs under `CODEX_HOME`; after
  `/quit` the stub stayed alive 30.1 to 30.2 s, until `codex app-server daemon stop`, 3
  of 3. With `--no-daemon` the server ended 0.11 to 0.14 s before the terminal UI did,
  3 of 3. A helper, `codex app-server daemon pid-update-loop`, survived even
  `daemon stop`, 4 of 4. Only 30 s were measured. A long `CODEX_HOME` path breaks the
  background server with "path must be shorter than SUN_LEN", which is the one run not
  counted.
- **Claude Code 2.1.292 and 2.1.294 probe with `server/discover` first**, revision
  `2026-07-28` with `clientInfo` in `_meta`, and fall back to `initialize`, 21 of 21;
  [the protocol pin above](#the-new-opening-request-and-the-one-revision-browserai-offers----measured-2026-10-08)
  is what BrowserAI answers it with.
- ⚠️ **Every Claude Code server is handed a messaging pipe and its token**,
  `CLAUDE_CODE_MESSAGING_SOCKET` and `CLAUDE_CODE_MESSAGING_TOKEN`, so a server that
  logs its environment logs the token.

**What it does not establish.** Hosts not run: the Agent SDK, the Claude desktop app,
JetBrains and the Codex IDE extension. (2) was not driven through VS Code, because
nothing could show a window: the extension's own 2.1.292 binary ran with the argument
list read off a live extension session and the environment its `extension.js` sets. The
test assumes the server's parent is the client, which held in 37 of 41: under the
Codex terminal UI the parent is the shared background server, so a server there cannot
tell which terminal session is attached. And the flags it reads are not a contract;
any release can change them.

**Re-establish it** with the batch's `rig/` (`IdRig/IdStub.cs.txt` is the stub,
`gen.trimmed.js.txt` writes the batches, `classify.trimmed.js.txt` is the test run over
every run, and `summarize.js.txt` the tables), every client under a scratch configuration and against
the local API stubs; compare against `runs/all-runs.tsv` and
`runs/all-arms-summary.txt`.

## Tooling around the protocol

**`claude mcp list` and `claude mcp get` exit 0 even when the server is dead** --
unusable as a CI gate without grepping stdout for `✘`. **Re-measured 2026-09-23 @
Claude Code 2.1.281** against two synthetic user-scope servers, a command that does
not exist and a node process that starts and never speaks MCP: both commands exit
**0** while printing `✘ Failed to connect`, and every failure the client does
report exits **1** -- a duplicate `add` and a nothing-to-remove `remove` are
indistinguishable by code, which is why BrowserAI read the English until
2026-10-03, and RegisterAI, which registers it since, reads the entry back
instead *(corrected 2026-10-03, previously "which is why `McpClientRegistration`
reads the English")*.
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

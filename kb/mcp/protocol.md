<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The MCP protocol, and the client at the other end

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
> **The product half landed 2026-08-16 at
> [build-order step 7](../../plan/build-order.md#7-vertical-slice-a-published-aot-binary-proxies-a-real-child).**
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

## The client: Claude Code

`[FLOATS]` on a client version this project does not control.

**Tool names and server `instructions` load eagerly; schemas are deferred.** So
`instructions` is the only channel that reaches the model before it calls
anything.

**Server `instructions` and every tool description are truncated silently at
2 KB.** The tail simply does not exist and nothing reports it.

**`notifications/tools/list_changed` handling changed, and the charter's citation
is stale.** *"Claude Code registers no handler"* was accurate at **2.0.65**
(Dec 2025) — issues
[#13646](https://github.com/anthropics/claude-code/issues/13646) and
[#4118](https://github.com/anthropics/claude-code/issues/4118). At **2.1.231 it
is false**: measured twice, the client re-listed in **1–2 ms** and the model
called a tool that appeared only in the second list. This does **not** unlock a
per-connection tool list — SEP-2567 stands — but the cited issues need re-dating.

## Tooling around the protocol

**`claude mcp list` and `claude mcp get` exit 0 even when the server is dead** —
unusable as a CI gate without grepping stdout for `✘`. **The official MCP
conformance suite is HTTP-only** (`--url`), so it needs a test-only listener or a
small bridge to reach a stdio server. **The Inspector CLI cannot spawn `.cmd`
shims on Windows** — same root cause as
[#58510](https://github.com/anthropics/claude-code/issues/58510) — so address
`cli.js` by absolute path; its **exit code 5 means the tool reported `isError`**,
which is the signal `claude mcp` does not give you. `[FLOATS]`

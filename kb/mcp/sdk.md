<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The .NET MCP SDK, as a proxy has to drive it

## SDK behaviours a proxy must work around

All read from the shipped `ModelContextProtocol` package. `[FLOATS]`

**`StdioClientTransport` prepends `cmd.exe /c` to every non-cmd command on
Windows, unconditionally.** That inserts a shell layer and an extra process
between BrowserAI and `node`, plus cmd.exe quoting semantics. `IClientTransport`
is two members (`Name`, `ConnectAsync`); the replacement is ~120 lines.

**`ListToolsAsync(RequestOptions?, ct)` silently drops any tool whose
`x-mcp-header` annotations fail SEP-2243 validation.** The raw
`ListToolsAsync(ListToolsRequestParams, ct)` overload returns the server's result
unfiltered. Using the wrong one shrinks the exposed surface with no error.

**The `ContentBlock` converter silently drops unknown properties** — the SDK has
tests asserting exactly that, which is correct forward-compatibility for a client
and data loss for a proxy — **and throws on unknown content *types***, failing
the whole call at deserialization before any proxy code runs. The escape is a
message filter: it operates on `JsonRpcMessage`, never touches `ContentBlock`, and
sees `JsonRpcResponse.Result` as a raw `JsonNode?`.

> ⚠️ **Corrected 2026-08-15: the filter is not `WithMessageFilters`.** This
> paragraph named it, and it is [a hosting-package DI extension, not
> Core](#measured-by-spike-2026-08-15). A Core/AOT proxy uses
> `McpServerOptions.Filters.Message.IncomingFilters` / `OutgoingFilters`
> directly. The correction is stated here, at the point of first mention, because
> a retraction thirty lines further down is one a reader can act on the wrong side
> of — and this is the file [the plan cites as
> authoritative](../../PLAN.md#nine-places-where-the-sdk-must-be-deviated-from).

## Measured by spike, 2026-08-15

A throwaway proxy was built on `ModelContextProtocol` 2.2.0 and driven against
both a scriptable fake child and the real `@playwright/mcp` 0.0.79, on CoreCLR
and as a published NativeAOT binary. Everything here is observed. `[FLOATS]`

**NativeAOT works, with nothing of ours required.** `PublishAot=true`, win-x64,
self-contained: **zero trim/AOT warnings, no `JsonSerializerContext` of our own,
10,233,856 bytes (9.76 MiB)**. The published binary drove a real child: 24 tools
through the proxy, `handle` injected into every schema, `browser_navigate` to a
`data:` URL returning a non-error result, recorded node PID gone after dispose.
`IsReflectionEnabledByDefault=false` confirmed at runtime. Even
`CallToolAsync(name, Dictionary<string,object?>)` worked. One AOT trap, in *our*
code rather than the SDK: `JsonArray.Add(x)` binds to the generic overload, which
is `RequiresDynamicCode` + `RequiresUnreferencedCode`; casting to the `JsonNode`
overload clears both.

**Passthrough is semantically lossless, not byte-identical, through the SDK's own
server transport.** Unknown content `type`, unknown properties on a known block,
unknown top-level result members, base64 image with `annotations`, and a
1,000,039-byte payload all survived byte-identically with key order preserved and
numeric forms unchanged. The one mutation is **string escaping**:
`McpJsonUtilities.JsonContext` sets no `Encoder`, so `JavaScriptEncoder.Default`
re-escapes on the way out. Backticks, apostrophes, angle brackets and every
non-ASCII character become escape sequences — measured on real
`browser_navigate` output, and a unicode case grew **154 to 218 bytes**.
`StreamServerTransport.cs:75` hard-codes the context with no options seam, so
byte-identity is unobtainable without our own server-side `ITransport`.

**`WithMessageFilters` is a hosting-package DI extension, not Core.** The Core
equivalent, and what an AOT proxy wants, is
`McpServerOptions.Filters.Message.IncomingFilters` / `OutgoingFilters`
(`IList<McpMessageFilter>`). `JsonRpcResponse.Result` and `JsonRpcRequest.Params`
are both `JsonNode?`, asserted by reflection at runtime. An incoming filter that
never calls `next` and answers via `ctx.Server.SendMessageAsync` short-circuits
cleanly: typed handlers that throw were never reached.

**Typed `ListToolsResult` discards unknown tool-level members.** `Tool` has no
`[JsonExtensionData]`. `inputSchema` keywords survive because it is a
`JsonElement`; a top-level `x-tool-extension` does not. On `JsonNode`, injecting
`handle` into `properties` + `required` preserved `$schema`, nested vendor
keywords, property-level hints and the top-level extension, with ordering stable
across repeated calls.

**The convenience-overload trap, reproduced exactly.** Same client, same child:
`ListToolsAsync(new ListToolsRequestParams(), ct)` returned 5 tools;
`ListToolsAsync(cancellationToken: ct)` returned 4, silently. Two details the
charter omitted: the drop **is** logged at `Warning` (`Tool '{ToolName}' excluded
from tools/list: {Reason}`), visible only if an `ILoggerFactory` is supplied, and
the `ToolRejected` hook is `internal` with no public event. `AddKnownTools`
**throws** on the same input rather than dropping.

**`cmd.exe` wrapping is worse than "an extra process".** Verified via a node
probe reporting `process.ppid`. Two argument-fidelity failures beyond the shell
layer: `%USERNAME%-literal` reached node as the expanded value, and an argument
containing whitespace **and** `&` caused the child to fail to start entirely
(exit 1, `'C:/Program' is not recognized`), because `EscapeArgumentString` skips
caret-escaping for whitespace-bearing arguments and cmd then splits the command
path — which contains a space in the stock Node install location. Metacharacters
alone round-trip fine. The replacement is **164 lines / 136 non-blank**, without
the stderr ring buffer or `ILogger` plumbing the SDK version carries:
`IClientTransport` is two members, but its session classes are `internal`, so the
`ITransport` half must be written against public `TransportBase`.

**Cancellation is never relayed.** A caller's `notifications/cancelled` cancels
the proxy's handler token in ~2 ms and `SendRequestAsync` throws, but **nothing
is emitted downstream**. Isolated away from the proxy: a plain `McpClient` over a
plain transport, cancelling both raw and typed call paths, produced nothing the
child could see within 6 s, on CoreCLR and AOT alike. `McpSessionHandler` has the
machinery (`RegisterCancellation`), but its registration is disposed as
`tcs.Task.WaitAsync(ct)` unwinds; CTS callbacks run LIFO, so `WaitAsync`'s
callback wins. **Remedy proven in the same run:** assign `JsonRpcRequest.Id`
yourself (it reaches the wire verbatim) and send the notification from your own
`ct.Register`.

**JSON-RPC errors are lossy above the transport.** `code` and `data` survive; the
message is prefixed by `CreateRemoteProtocolExceptionFromError`, so
`"upstream exploded"` arrives as `"Request failed (remote): upstream exploded"`,
and `data` is destructured into `Exception.Data`. A child dying mid-call surfaces
as `-32603`, an error rather than a hang — but only once its stdout reaches EOF.

**`McpClientOptions` has no `Filters`.** All filter APIs are server-side, so
wildcard observation of child-to-proxy traffic needs an `ITransport` decorator
(~30 lines). With one, an unknown notification reached the caller with
byte-identical `params`.

**`RequestHandlers` contradicts its own documentation.**
`McpServerOptions.RequestHandlers` (`[Experimental("MCPEXP002")]`) is documented
as taking precedence over built-in handlers. It does not:
`ConfigureCustomRequestHandlers` runs last and **throws
`InvalidOperationException`** for a method already handled. Using it for
`tools/list` requires leaving `Capabilities.Tools` unset, which then requires
re-injecting `capabilities.tools` into the `initialize` result from an outgoing
filter. The `IncomingFilters` short-circuit needs no such surgery.

**An unanswered `server/discover` costs the full `DiscoverProbeTimeout` per
connect** — 30 s per rig against a fake child until it returned `-32601`. Real
`@playwright/mcp` 0.0.79 handles it, so this bites our own test doubles rather
than production.

**Not tested, not claimed:** HTTP transports, resumption, pagination cursors on
the real child, `structuredContent` on a real tool, stderr back-pressure under
load, ordering of concurrent in-flight `tools/call`s.

**The typed client flattens JSON-RPC `error.data` to primitives**, losing nested
error structures. Protocol errors only — tool failures travel as `isError: true`
data.

**`McpServerToolCreateOptions` has `OutputSchema` but no `InputSchema`**, so the
obvious factory API always reflects the schema from the .NET signature — unusable
for a proxy, and the first one reached for.

**Roughly half of [§E](../../PLAN.md#e-lifecycle-and-observability)'s
observability is already in the SDK:**
`StandardErrorLines` wired before `Start()`, a rolling stderr tail, and a
`StdioClientCompletionDetails { ProcessId, ExitCode, StandardErrorTail }` type.
The SDK also carries a `beforeDispose` callback commented *"to read ExitCode
before Dispose() invalidates it"* — upstream hit
[the same `ExitCode` trap](../windows/processes.md#stdio-exit-codes-and-process-startup).

**`IsAotCompatible=true` is declared by both Velopack (net8.0+) and
`ModelContextProtocol`** — verified in-source **2026-08-14**, set on every target
except `netstandard2.0`, at both `v1.4.1` and `v2.2.0`. **A declaration is the
author's claim about their code, not a proof for our usage.**

**The SDK's test fixtures are 1,082 lines** (`ClientServerTestBase` +
`tests/Common/Utils/*`), Apache-2.0, **unpublished to NuGet**, and they wire a
single client↔server pipe pair where a proxy needs two hops. `NodeHelpers.cs` is
a further 577 lines of `npm install` machinery for the conformance suite.
**Disposal order in that harness is load-bearing:** cancel the token → complete
*both* pipe writers → await the server task → dispose the provider; any other
order hangs or throws.

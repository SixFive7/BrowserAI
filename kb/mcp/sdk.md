<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The .NET MCP SDK, as a proxy has to drive it

**Versions in force** unless an entry says otherwise: `ModelContextProtocol` **2.2.0** (1.4.1 where an entry says so) · `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · .NET SDK 10.0.400, runtime and ILC 10.0.11 · Windows 11 Pro 26200.
Measured on [the reference machine](../README.md#the-reference-machine).

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
> Core](#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation). A Core/AOT proxy uses
> `McpServerOptions.Filters.Message.IncomingFilters` / `OutgoingFilters`
> directly. The correction is stated here, at the point of first mention, because
> a retraction thirty lines further down is one a reader can act on the wrong side
> of — and this is the file [the plan cites as
> authoritative](../../STACK.md#nine-places-where-the-sdk-must-be-deviated-from).

## Driving the whole SDK: AOT, passthrough, filters and cancellation

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

**Roughly half of the [observability this product requires](../../ARCHITECTURE.md#process-containment-and-observability)
is already in the SDK:**
`StandardErrorLines` wired before `Start()`, a rolling stderr tail, and a
`StdioClientCompletionDetails { ProcessId, ExitCode, StandardErrorTail }` type.
The SDK also carries a `beforeDispose` callback commented *"to read ExitCode
before Dispose() invalidates it"* — upstream hit
[the same `ExitCode` trap](../windows/processes.md#stdio-exit-codes-and-process-startup).

**`IsAotCompatible=true` is declared by both Velopack (net8.0+) and
`ModelContextProtocol`** — verified in-source **2026-08-14**, set on every target
except `netstandard2.0`, at both `v1.4.1` and `v2.2.0`. **A declaration is the
author's claim about their code, not a proof for our usage.**

## ILC, the native toolchain and source-generated JSON

Established 2026-08-16 on other projects, and dated separately so nothing here is
mis-attributed to the run above. All three are about what a **clean** publish does
and does not prove.

**A NativeAOT publish can exit 0 while ILC reports that a code path will always
throw.** Measured 2026-08-16 against an unrelated NativeAOT console project of
the author's, called the probe project below:
`dotnet publish -c Release -r win-x64` **exited 0** and produced a working
**11,220,992-byte** exe (plus a **47,239,168-byte** pdb; ~60 s against ~10 s for a
JIT build of the same project), while emitting

> `ILC: Method '[Spectre.Console.Cli]OpenCli.OpenCliParser.Parse(Stream,CancellationToken)' will always throw because: Failed to load assembly 'NJsonSchema'`

ILC substitutes a throwing body for a method whose dependency it cannot resolve
and **carries on**. The message is neither a warning nor an error, so it is
invisible to `TreatWarningsAsErrors`, to `NoWarn`, and to the exit code — the
three things a build gate normally reads. Corroborated mechanically the same day:
the assembly ILC could not load appears nowhere in the 273-line
`obj\Release\<tfm>\<rid>\native\<project>.ilc.rsp`, which is exactly why the
load failed. **That response file is emitted by any `PublishAot` build**, so this
half is reproducible against any project.

> ⚠️ **This qualifies [the spike's own "zero trim/AOT warnings"
> claim](#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation).** That result is not retracted — it was
> measured, and it remains the right thing to require. But **zero warnings plus
> exit 0 is not sufficient evidence that the published binary is sound**, and the
> spike's phrasing invites reading it as if it were. [Re-verification row
> 27](../re-verification.md) has been amended accordingly: the check
> now includes grepping the publish output for `will always throw`.

Two further traps visible in the same project. Its csproj carries
`<NoWarn>$(NoWarn);IL2104;IL3050;IL3053;IL3000</NoWarn>` with a comment
justifying it, so "clean" there is partly suppression rather than
soundness — worth knowing before treating another project's zero-warning claim as
comparable to ours. And the published artifacts on disk carry an mtime of
**2026-08-14**, so the byte counts above are a re-reading of that publish rather
than a fresh one; the sizes and the ILC message are what was verified today, not
the wall-clock of the run. Re-establish by publishing and **grepping the build
output for `will always throw`**, never by reading the exit code. `[FLOATS]` for
ILC's behaviour; `[MACHINE]` for the sizes and timings.

**Full ILC needs the MSVC native toolchain — `link.exe`, discovered via
`vswhere` — and its absence presents as a library problem.** Recorded in a
shipping in-house application's own AOT notes, written 2026-07-07: *"Full ILC `PublishAot`:
blocked by the environment, not the code… requires the MSVC native toolchain
(link.exe, discovered via vswhere), which this development machine does not have
installed."* That project shipped self-contained-single-file instead. The mechanism
is confirmed here rather than merely quoted, and **that half anyone can
reproduce**: after any `PublishAot` build,
`obj\Release\<tfm>\<rid>\native\link.rsp` is an MSVC linker response file whose
`/LIBPATH` entries point under
`C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231`.
Worth recording because the failure is routinely misdiagnosed as an SDK or package
incompatibility, which sends you rewriting code that was never the problem.

> ⚠️ **The environment half of that quote is stale, checked 2026-08-16.** The MSVC
> toolchain **is** installed on this machine: `vswhere -latest -property
> installationPath` returns `C:\Program Files\Microsoft Visual Studio\18\Community`,
> and `VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\link.exe` is present with an
> mtime of **2026-07-24** — after that note was written. That resolves what would
> otherwise be a flat contradiction with [the 2026-08-15
> spike](#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation) and the probe project's publish above,
> both of which completed full ILC here. **Never carry an "the environment lacks X" claim
> forward without re-checking it:** unlike an upstream fact, it can be falsified by
> an install that nobody records and no version bump announces. `[STABLE]` for the
> toolchain requirement, which is Microsoft's own and reproducible by uninstalling
> the C++ workload; `[MACHINE]` and now **superseded** for the environment claim
> that was carried forward.

**An enum serialised as an integer by a source-generated JSON context fails to
parse on the way back in, and silently reverts the setting on every restart.**
`System.Text.Json` writes enums as numbers by default; code reading the value back
as a string then fails, falls back to the record default, and the user's choice
appears to "revert" — with no error raised at any point. The fix is
`UseStringEnumConverter = true` on the context's `[JsonSourceGenerationOptions]`.
Shipped bug, read 2026-08-16 in an in-house Velopack deployment's own troubleshooting notes
(*Enum Serialization as Integers — Fixed in 1.0.3*): `settings.json` held
`"Channel": 0` rather than `"Channel": "Stable"`, and note how it was filed —
under the symptom *"`Channel` resets to `Stable` after restart"*, i.e. reported as
a settings bug for as long as it took to find the serializer. **Relevant here
because a source-generated context is mandatory under AOT**, so this is the
default path rather than an unusual one. `[STABLE]`

**The SDK's test fixtures are 1,082 lines** (`ClientServerTestBase` +
`tests/Common/Utils/*`), Apache-2.0, **unpublished to NuGet**, and they wire a
single client↔server pipe pair where a proxy needs two hops. `NodeHelpers.cs` is
a further 577 lines of `npm install` machinery for the conformance suite.
**Disposal order in that harness is load-bearing:** cancel the token → complete
*both* pipe writers → await the server task → dispose the provider; any other
order hangs or throws.

> ⚠️ **The last clause was tested on our own two-hop rig on 2026-08-16 and is
> right about the consequence, wrong about the mechanism.** The two steps are
> independent, not sequential; see
> [the four-way table](#error-shape-and-teardown-seen-from-an-in-process-harness). The
> line-count and licence facts above are unaffected and were not re-read.

## Writing replacement transports against the public surface

What the SDK's public surface does and does not offer somebody replacing its two
transports: which members are reachable from another assembly, what the stock
client does to a command line, and where the server's escaping is decided.
Established 2026-08-16 against `ModelContextProtocol` **2.2.0**, read from the
shipped source at `refs/tags/v2.2.0` and, where stated, measured against running
code.

**The `cmd.exe /c` wrapping is still there, and the predicate is exact.**
`StdioClientTransport.ConnectAsync` rewrites the command whenever
`RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` and
`Path.GetFileName(command)` is not `cmd.exe`, to `cmd.exe` with
`["/c", command, ..arguments]`. There is no option, no environment variable and
no subclass hook. **Now checked rather than remembered:**
`SdkStdioClientTransportTests.TheSdkTransportStillPutsCmdExeBetweenUsAndTheChild`
starts a child through the SDK's transport and asserts, via
`NtQueryInformationProcess`, that the child's parent image is `cmd`.
[Re-verification row 31](../re-verification.md) is automated by it.
`[FLOATS]`

**The server transport's escaping is `Utf8JsonWriter`'s, not the contract's —
which is why it is fixable without a `JsonSerializerContext` of our own.**
`StreamServerTransport.SendMessageAsync` calls
`JsonSerializer.SerializeToUtf8Bytes(message,
McpJsonUtilities.JsonContext.Default.JsonRpcMessage)`, with no options seam
anywhere on the path. Escaping, however, is performed by the writer from its own
`JsonWriterOptions.Encoder`, so serialising the SDK's own `JsonTypeInfo` into a
`Utf8JsonWriter` configured with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
produces the same JSON with the escaping removed. **Measured on the same
message** — a `JsonRpcResponse` whose result text is
``Page URL: `x` it's <b>&amp;</b> café — ünïcødé`` — the frame is **127 bytes
through ours and 190 through the SDK's, +49.6%**. Re-establish by running
`DirectStdioServerTransportTests.TheSdkServerTransportStillEscapesTheSameResult`,
which asserts the direction of that inequality on every build. `[FLOATS]`

**`McpJsonUtilities.JsonContext` is `internal`.** The public route to the
message contract from outside the SDK's assembly is the extension method
`McpJsonUtilities.GetTypeInfo<T>(this JsonSerializerOptions)` applied to
`McpJsonUtilities.DefaultOptions` — which is itself built from
`JsonContext.Default.Options` with MEAI's resolver chained on, so the contract
is the same one. `[FLOATS]`

**`TransportBase.Logger` and `LogTransportSendingMessageSensitive` are
`private protected`.** A transport written against the public `TransportBase`
from another assembly can reach `Name`, `IsConnected`, `MessageReader`,
`SessionId`, `SetConnected`, `SetDisconnected` and `WriteMessageAsync`, and
**not** the logger or the sensitive-message logging helper — so it carries its
own `ILogger` and takes the `ILoggerFactory` twice. This one cannot fail
silently, which is why it has no re-verification row: a change makes the build
red or makes a field redundant. That is
[the stated exemption](../re-verification.md#a-floats-entry-with-no-row-the-one-rule),
claimed here in place rather than left as a gap. `[FLOATS]`

**A `RequestContext<T>.Params` is nullable and the tool handlers are two
delegates.** `McpServerOptions.Handlers` is an `McpServerHandlers` with
`ListToolsHandler` and `CallToolHandler` among fifteen properties, and declaring
`Capabilities.Tools` is what makes `initialize` advertise tools at all — with it
unset, a server carrying handlers still tells the caller it has none. `[FLOATS]`

**`StreamServerTransport` never sets `JsonRpcMessage.Context`.** The
`RelatedTransport` field exists for the stateless HTTP path; a session-based
stdio transport leaves it null, and a replacement should too. `[FLOATS]`

**The SDK answers a frame it cannot parse.** `StreamServerTransport` walks the
top-level object with a `MaxDepth = int.MaxValue` reader looking only for `id`,
and if it finds one replies `-32700` so the caller fails instead of waiting.
That is worth knowing because a transport that merely drops the frame leaves the
caller hanging with nothing but a log line to explain it. BrowserAI's does drop
it, deliberately and loudly, until the lossless-passthrough layer
owns error shaping. `[FLOATS]`

## The published NativeAOT binary, with all of it in one exe

The result of putting all of it in one NativeAOT binary — the SDK, two custom
transports and a `[LibraryImport]` job-object launcher — and driving a real
`@playwright/mcp` 0.0.79 child and a real Chromium with it. Measured 2026-08-16.
The interesting number is that nothing of ours was needed to make AOT work, and
the interesting trap is one that fails an ordinary build as well as a publish.

**NativeAOT is clean with all of it in one binary, and the number is ours rather
than a spike's.** `dotnet publish -c Release -r win-x64 --self-contained`:
**zero trim/AOT warnings, no `will always throw` in the output, exit 0,
10,399,744 bytes (9.92 MiB)**. That is with `ModelContextProtocol` 2.2.0, both
custom transports, `JobObject`/`JobLauncher`, the rolling file sink and the
`Utf8JsonWriter`-based config generator, and **still no `JsonSerializerContext`
of our own on any path**. The published binary negotiated `2025-11-25` with the
child, forwarded a 24-tool `tools/list`, and returned a non-error
`browser_navigate` result. `[MACHINE]` for the byte count, `[FLOATS]` for the
warning-free claim. Re-establish with the publish command and
`VerticalSliceTests`.

**The `JsonArray.Add(x)` trap reproduces, and it fails `dotnet build` as well as
`dotnet publish`.** Planted in our own code on 2026-08-16 — `new JsonArray()`
then `Add(someInt)`, which binds to `Add<T>(T)` — and the result is **two**
errors at the same call site, `IL2026` (`RequiresUnreferencedCode`) and `IL3050`
(`RequiresDynamicCode`), on `dotnet build` at the analyzer stage and again on
`dotnet publish`. The spike recorded it as a publish-time trap; because
`EnableAotAnalyzer` and `EnableTrimAnalyzer` are set unconditionally rather than
under a publish-only condition, an everyday build catches it too. The cast to
`(JsonNode)` clears both. Reverted. `[FLOATS]`

> This matters more than a one-line trap sounds, because
> the passthrough layer rewrites
> `tools/list` on `JsonNode` — that is the file where this call shape is most
> likely to be written, and where it was planted for exactly that reason.

## Error shape and teardown, seen from an in-process harness

What a caller receives when something goes wrong two hops away, and what actually
tears the hops down. Measured 2026-08-16 with a test client, the proxy and a
scriptable double joined by two pipe pairs, with no process and no Node anywhere.
Everything here is observed through `FakeChildHarnessTests`, which runs on every
build — so this whole section has a route.

**An exception escaping a `CallToolHandler` becomes a JSON-RPC *success*
carrying `isError: true`, and the cause is erased from the body.** Measured from
two unrelated causes, both producing the identical frame:

```
{"result":{"content":[{"type":"text","text":"An error occurred invoking 'browser_navigate'."}],"isError":true},"id":2,"jsonrpc":"2.0"}
```

The first cause was `JsonException: Unknown content type: 'x-browserai-unknown'`
thrown by `ContentBlock.Converter.Read` while the client deserialised the child's
result; the second was `IOException: The server shut down unexpectedly` from a
child that closed its stdout mid-call. **Nothing in the answer distinguishes
them, and nothing in it names a cause at all.** The real exception is written
once, by the SDK's server, at `Error`, as `"browser_navigate" threw an unhandled
exception` — so it exists only if an `ILoggerFactory` was supplied.

> This is **the founding failure shape arriving from our own dependency**: a
> success envelope, every transport-level signal green, and the single bit that
> says *broken* buried in the body. It is the reason
> lossless passthrough was built as a piece of work
> in its own right rather than as a detail of the vertical slice. It also **qualifies the 2026-08-15 spike's
> note** that *"a child dying mid-call surfaces as `-32603`, an error rather than
> a hang"* — that describes what the **client** throws; what the **caller of the
> proxy** receives, once the exception has passed through a typed
> `CallToolHandler`, is the frame above. Both are true at different layers, and
> only the second is what a model sees. Not re-measured here: whether the SDK's
> own stdio client transport still yields `-32603` for the same death, since ours
> raised `IOException` instead.

**A JSON-RPC error from the child, by contrast, keeps almost everything.** A
child answering `-32000` with `data` of `{"reason":"programmed"}` reaches the
caller as code **-32000**, `data` **`{"reason":"programmed"}` verbatim and
unflattened**, and message `Request failed (remote): the fake child refused this
navigation`. So the prefix is real and still needs stripping — but **`data`
arrives reconstructed with the proxy doing nothing**, which is more than
the passthrough design assumed when it
asked for it to be rebuilt from `Exception.Data`. Re-establish with
`FakeChildHarnessTests.TheFakeChildInjectsAJsonRpcError`, which asserts all three
by exact equality. `[FLOATS]`

**`McpClientOptions.DiscoverProbeTimeout` is 5 seconds at 2.2.0, and the pin is
what skips the probe — measured from both sides rather than read.** With
`ProtocolVersion` pinned to `2025-11-25`, a double that records every method it
is asked for sees **no `server/discover` at all**. With `ProtocolVersion` left
null, the same double sees the probe. And against a double that *drops* the
method, the connect costs the whole timeout and reports nothing — the
30-seconds-per-rig failure of the 2026-08-15 spike, reproduced in 250 ms because
`TestDefaults` pins the timeout short rather than long. Upstream's own fixtures
pin it *longer*, citing
[csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701);
that is the right direction against a real peer over CI latency and the wrong one
against in-process doubles, where a probe that ever runs to its timeout is a
defect to surface fast. `[FLOATS]`

**The harness teardown order: what each step actually does.** The SDK's own
fixture note says *"cancel the token → complete both pipe writers → await the
server task → dispose the provider; any other order hangs or throws."* Removing
one step at a time from `McpTestHarness.DisposeAsync` and running the whole
suite, 2026-08-16:

| Cancel the token | Complete both writers | Server task ended | Pipes closed | Suite |
|---|---|---|---|---|
| yes | yes | yes | yes | 88 / 88 |
| **no** | yes | yes | yes | 88 / 88 |
| yes | **no** | yes | **no** | 78 / 88 |
| **no** | **no** | **no** | **no** | 78 / 88 |

So the two steps are **not** a sequence in which the first enables the second.
Each independently ends `McpServer.RunAsync` — cancellation because it is the
token it was started with, completion because EOF is what it returns on — and
**only completing both writers closes the hop.** The consequence the note
describes is real; the mechanism it implies is not. Re-establish by commenting
out step 1 or step 2 of `McpTestHarness.DisposeAsync` and running the suite: the
rig's own liveness assertion turns ten tests red rather than hanging, because the
wait on the server task is bounded and the check is read **before** anything is
disposed. `[FLOATS]`

> **The first version of that check was dead and the experiment is what found
> it.** It read `_serverTask.IsCompleted` *after* disposing the server, and
> disposing the server ends the task — so the arm could never fire, in any
> configuration. It now records the state immediately after the bounded wait.
> A liveness check that cannot fail is worth less than none, because it reads as
> covered.

## Lossless passthrough: cancellation, notifications and error frames

What survives, what is reordered and what has to be rebuilt when the two tool
methods are forwarded through an incoming message filter and answered from the
child's own bytes, with no SDK contract type anywhere on the path. Measured
2026-08-16 against the in-process double unless stated otherwise; source citations
are from the shipped source at `refs/tags/v2.2.0`.

**The documented cancellation remedy does not work, and it fails for the same
reason it blames for the SDK's own failure.**
[The 2026-08-15 spike](#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation) prescribed *"assign
`JsonRpcRequest.Id` yourself and send the notification from your own
`ct.Register`"*. Built exactly that way, **the registration callback never
runs.** A registration scoped to the call it protects is disposed as that call
unwinds: `SendRequestAsync` waits on `tcs.Task.WaitAsync(ct)`, which registers
its own callback *after* ours, **CTS callbacks run LIFO**, so `WaitAsync`'s runs
first, the await throws, the `using` disposes ours, and ours is unregistered
before LIFO ever reaches it. Observed directly on 2026-08-16, with the proxy's
own logging: the token reported `CanBeCanceled` **true**, the call threw
`OperationCanceledException` with `IsCancellationRequested` **true**, the double
had already recorded the `tools/call` — and the callback logged nothing at all,
on either of its two paths.

**Announcing from the `catch (OperationCanceledException)` works and is better on
every axis**: awaited rather than fire-and-forget, incapable of firing before the
request it names has been sent, and reached by the one path that definitely
executes. **The first half of the remedy is unchanged and load-bearing** — the id
must be one we chose, or there is nothing to name in the notification.
Re-establish with
`LosslessPassthroughTests.CancellingACallIsObservedAtTheFakeChild`, which asserts
the double sees `notifications/cancelled`, that its `requestId` equals the id
BrowserAI actually sent, and that it sees **exactly one**. `[FLOATS]`

**A short-circuiting message filter keeps the request cancellable, because the
CTS is registered before the filter runs.** `ProcessMessageAsync` stores the
per-request `CancellationTokenSource` in `_handlingRequests[id]` **before**
calling `HandleMessageAsync`, and `HandleMessageAsync` is what invokes the
incoming filter chain. So a filter that never calls `next` still receives a token
the caller's `notifications/cancelled` can fire. This is not incidental — it is
what makes the whole short-circuit design viable, and the opposite arrangement
would have made cancellation unreachable for a proxy. `[FLOATS]`

**Forwarding a *named* child-to-caller notification needs no `ITransport`
decorator.** `McpSession.RegisterNotificationHandler(method, handler)` is
`public abstract` and `McpClient` inherits it, so the progress relay is public
API. The decorator the spike called for is needed for **wildcard** observation,
which is what it actually measured. What genuinely has no public route is the
live `ITransport` instance — `McpClient.CreateAsync` calls
`IClientTransport.ConnectAsync` itself and keeps the result private — so a proxy
that needs the child's raw bytes decorates **`IClientTransport`** instead.
`[FLOATS]`

**Inbound notifications are dispatched fire-and-forget, so a relay preserves
content but not order.** `ProcessMessagesCoreAsync` starts each message's
handling without awaiting it — *"Fire and forget the message handling to avoid
blocking the transport"* — and two `notifications/progress` written by the double
in order were observed reaching the caller as **2 then 1**. The `progressToken`
and the params survive intact. **This cannot be fixed from a notification
handler**: the reordering has already happened by the time the handler runs, so a
fix would need the `ITransport` decorator the deviation originally described.
Re-establish by running
`LosslessPassthroughTests.AChildProgressNotificationReachesTheCallerUnderTheCallersToken`
with logging at `Trace` and reading the order of the two `sending message` lines.
`[FLOATS]`

**A child's JSON-RPC error and its `data` both survive, and the prefix can be
avoided rather than stripped.** Re-confirming [the step-8
measurement](#error-shape-and-teardown-seen-from-an-in-process-harness) from the other
side: `McpProtocolException.Message` is
`Request failed (remote): <the child's message>`, `ErrorCode` is the child's, and
`Exception.Data` is non-empty. A proxy that answers from the child's own error
frame never reads any of them, so **deviation 8's reconstruction was solving a
problem that did not exist and its strip was solving one that need not arise**.
Re-establish with
`SdkErrorShapeTests.TheSdkStillPrefixesARemoteErrorMessageAndStillKeepsItsData`,
which drives a plain `McpClient` at the double precisely because the product no
longer travels that path. `[FLOATS]`

**`JsonRpcMessage` cannot be derived from outside the SDK, and `Context` can be
set from outside it.** The constructor is `private protected` with the comment
*"Prevent external derivations"*, so there is no subclass on which to hang a
proxy's own per-message state; `Context` is a public settable
`JsonRpcMessageContext?` whose `Items` bag is documented as flowing through the
filter pipeline. BrowserAI uses neither, keeping its verbatim payloads in a
`ConditionalWeakTable` keyed on the message — no SDK state written, and a
response that is never sent takes its payload with it rather than pinning a
megabyte of screenshot. Like
[the `TransportBase.Logger` entry](#writing-replacement-transports-against-the-public-surface),
this one has **no re-verification row on purpose**: a change here makes the build
red or makes a workaround redundant, and neither is silent — which is
[the stated exemption](../re-verification.md#a-floats-entry-with-no-row-the-one-rule),
claimed in place. `[FLOATS]`

**NativeAOT stays clean with the passthrough in it, including
`Utf8JsonWriter.WriteRawValue` and `Utf8JsonReader` token-offset slicing.**
`dotnet publish -c Release -r win-x64 --self-contained`: **zero trim/AOT
warnings, no `will always throw`, exit 0, 10,461,696 bytes (9.98 MiB)** — 61,952
bytes more than
[the step-7 binary](#the-published-nativeaot-binary-with-all-of-it-in-one-exe), and still no
`JsonSerializerContext` of our own. `[MACHINE]` for the byte count, `[FLOATS]`
for the warning-free claim; both re-established by the publish command plus
`VerticalSliceTests`, which is what
[row 27](../re-verification.md) already asks for.

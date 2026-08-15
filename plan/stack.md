<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Implementation stack

Verified 2026-08-13. **The versions below are provenance stamps, not targets** — see [Versioning policy](../README.md#versioning-policy-everything-floats-the-build-freezes-it). The build resolves the latest of each; these record what was current when the surrounding claims were checked, and carry the same convention as `playwright/README.md`: re-verify on every bump. Each lookup, with the date it was made, is in [kb: package provenance](../kb/packaging/dependencies.md#package-provenance-as-looked-up).

| Concern | Choice | Notes |
|---|---|---|
| MCP protocol | `ModelContextProtocol` (latest; **2.2.0** as of 2026-08-13) | Apache-2.0, 23.6M downloads. **Tier 1** SDK under the MCP project — which Anthropic donated to the Linux Foundation's **Agentic AI Foundation** on 2025-12-09, so "official" now means LF-governed, with day-to-day engineering by the Microsoft .NET team. Began as `PederHP/mcpdotnet` (now archived). Full `2026-07-28`. The main package's hosting dependency is abstractions-only — it does **not** drag in ASP.NET. `ModelContextProtocol.Core` alone is a viable smaller surface (`McpServer.Create` + `StdioServerTransport`, and the `[McpServerTool]` attributes already live there) at the cost of `AddMcpServer()` and assembly scanning — not worth it unless the hosting stack becomes a problem. Verified 2026-08-14. |
| Updates | **Velopack 1.2.0** + `vpk` 1.2.0 | MIT. See §G. |
| Node runtime | **v24.19.0 LTS**, `node.exe` only | v26 is Current, not LTS, and its `node.exe` is 10 MB larger. |
| Job objects | Hand-rolled `[LibraryImport]` | No credible NuGet wrapper exists — the candidates have <6K downloads and the newest was published in 2017. `dotnet/runtime` [#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in support and was closed as not planned. ~60 lines. `Microsoft.Windows.CsWin32` is the reasonable alternative once a seventh Win32 API is needed. |
| Parent PID | `NtQueryInformationProcess` | ~0.77 µs/call vs ~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. This is what `dotnet/runtime` itself uses. See [kb: interop](../kb/windows/processes.md#interop-and-the-toolchain). |
| Tests | **TUnit** (latest; 1.65.0 as of 2026-08-13) | MIT, source-generated, reflection-free, **MTP-native**. Matches `SixFive7/Jeeves`. 1.0 shipped 2025-11-05; ~623K downloads/mo and growing 2.24× YoY. Chosen over xUnit v3 because [we do not vendor the SDK's fixtures](testing.md#we-write-our-own-harness) — that was xUnit's only argument here. |
| Snapshots | `Verify.TUnit` (latest; **31.28.0** as of 2026-07-31) | Exact parity with `Verify.XunitV3` — same monorepo, same release, and Verify's own repo carries *more* test projects for the TUnit integration than the xUnit v3 one. |
| Assertions | TUnit built-ins | `await Assert.That(actual).IsEqualTo(expected)`. **Never add FluentAssertions** — it relicensed at exactly 8.0.0 to a bespoke non-SPDX licence with a commercial tier. Jeeves carries the identical prohibition for the identical reason. |
| External smoke | `@modelcontextprotocol/inspector` **2.2.0** | Language-independent CI check. Exit code **5** means the tool reported `isError` — the signal `claude mcp` does not give you. |

## Nine places where the SDK must be deviated from

> Was three. Six more were found by measurement on 2026-08-15, and **two of the original three were wrong in detail** — corrections are marked inline. A spike drove the real child through a published NativeAOT binary; everything below is observed rather than read.

**1. Write your own `IClientTransport`.** The SDK's `StdioClientTransport` prepends `cmd.exe /c` to every non-cmd command on Windows, unconditionally. That directly contradicts [§Windows process spawning](../README.md#windows-process-spawning): it adds a shell layer, an extra process between BrowserAI and `node` (complicating tree ownership and exit-code attribution), and cmd.exe quoting semantics. The interface is two members (`Name`, `ConnectAsync`) and the replacement is ~120 lines. Port `StdioClientTransportOptions`' stderr and shutdown handling rather than reinventing it.

**2. Use the raw `ListToolsAsync` overload.** The convenience overload `ListToolsAsync(RequestOptions?, ct)` **silently drops** any tool whose `x-mcp-header` annotations fail SEP-2243 validation. A proxy must call `ListToolsAsync(ListToolsRequestParams, ct)`, which returns the server's result unfiltered. Using the wrong one shrinks the exposed surface with no error anywhere — the same failure class as everything in [the README's opening table](../README.md#read-this-before-designing-anything).

**3. Proxy `tools/call` through `McpServerOptions.Filters.Message.IncomingFilters`, short-circuiting rather than calling `next`.** The `ContentBlock` converter **silently drops unknown properties** and **throws on unknown content *types***, failing the whole call at deserialization before any BrowserAI code runs. The filter sees `JsonRpcResponse.Result` as a raw `JsonNode?` and never touches `ContentBlock`.

> ⚠️ **Not `WithMessageFilters`.** That is a DI extension in the *hosting* package. A Core/AOT proxy uses `McpServerOptions.Filters.Message.IncomingFilters` / `OutgoingFilters` directly. Charter corrected 2026-08-15.

**4. Rewrite `tools/list` on `JsonNode` too — not typed.** An earlier draft of this section said "rewrite `tools/list` typed"; that is wrong. A typed `ListToolsResult` round-trip **silently discards unknown top-level tool members**, because `Tool` carries no `[JsonExtensionData]`. Schema keywords survive (`inputSchema` is a `JsonElement`), tool-level extensions do not. Measured both ways in the same run.

**5. Write our own *server*-side transport as well.** `StreamServerTransport` hard-codes `McpJsonUtilities.JsonContext` with no options seam, so every outgoing string is re-escaped by `JavaScriptEncoder.Default`. Decoded values are unchanged, but the bytes are not — every backtick, apostrophe, angle bracket and non-ASCII character becomes a `\uXXXX` escape. Measured on a real `browser_navigate` result, and on a unicode case that grew **154 → 218 bytes**. **Settled 2026-08-15: build the transport** and serialize with `UnsafeRelaxedJsonEscaping`. Two reasons, and the second is the stronger: passthrough becomes genuinely byte-exact, and the inflation this removes is **tokens in the model's context on every result**. The cost is small because it is the same `TransportBase` pattern deviation 1 already requires. ("Unsafe" refers to embedding JSON in HTML; we write to a pipe consumed by a JSON parser.)

**6. Cancellation does not work at all — hand-roll it.** Measured 2026-08-15, and isolated away from the proxy entirely: a plain `McpClient` over a plain transport, cancelling both the raw and typed call paths, **never emits `notifications/cancelled` downstream**. The machinery exists in `McpSessionHandler.SendRequestAsync`, but its registration is disposed as `tcs.Task.WaitAsync(ct)` unwinds — CTS callbacks run LIFO, so `WaitAsync`'s callback wins and the notification callback is cancelled before it can run. **Remedy, proven in the same run:** assign `JsonRpcRequest.Id` yourself (it survives to the wire verbatim) and send `notifications/cancelled` by hand from your own `ct.Register`. Without this, a cancelled `browser_navigate` leaves the child working.

**7. `McpClientOptions` has no `Filters`.** Every filter API is server-side, so observing and forwarding *child→caller* notifications needs an `ITransport` decorator (~30 lines). The caller→child direction is covered by `IncomingFilters`.

**8. JSON-RPC errors are lossy above the transport.** `code` and `data` survive, but the message is prefixed — `"upstream exploded"` arrives as `"Request failed (remote): upstream exploded"`, and `data` is destructured into `Exception.Data`. Reconstruct from `McpProtocolException` and strip the prefix, or forward a message that is not upstream's.

**9. Answer the `server/discover` probe.** A child that ignores it costs the full `DiscoverProbeTimeout` **per connect** — the spike burned 30 s per rig against a fake child until it returned `-32601`. Real `@playwright/mcp` 0.0.79 handles it, so this is a hazard for our own test doubles rather than for production.

These are places where the SDK's design goal (a forward-compatible *client*) and BrowserAI's (a lossless *proxy*) genuinely differ. **Every one of the failure modes above is silent** — dropped tools, dropped members, a cancellation that never arrives — which is the class this project exists to eliminate. All of it is measured, not inferred: see [kb: SDK behaviours](../kb/mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around).

> **NativeAOT is proven, not assumed.** `PublishAot=true`, win-x64, self-contained: **zero trim/AOT warnings, no `JsonSerializerContext` of our own required, 9.76 MiB binary.** The published binary drove a real `@playwright/mcp` child over stdio — 24 tools through the proxy, `handle` injected into every schema, `browser_navigate` returning a non-error result, child PID gone after dispose. One AOT trap, in *our* code rather than the SDK: `JsonArray.Add(x)` binds to the generic overload, which is `RequiresDynamicCode`; cast to `(JsonNode)` to clear it.

## Native compilation needs the MSVC toolchain

**Record this as a prerequisite, because its absence presents as the wrong diagnosis.** ILC's final step is a native link, so `PublishAot=true` requires the MSVC toolchain — `link.exe`, located through `vswhere` — installed by Visual Studio's *Desktop development with C++* workload. Without it the publish fails, and it fails in a way that reads as an SDK or library incompatibility rather than as a missing external tool. The wrong diagnosis leads straight to the wrong fix: pinning something back, or abandoning AOT.

**In-house evidence that this exact confusion is a real cost.** `C:\Source\ExoFabric\UCC\aot.md:30` records the resolution after the fact — *"Full ILC `PublishAot`: blocked by the environment, not the code. The codebase carries zero AOT-analyzer warnings, but running the ILC native compilation step on Windows requires the MSVC native toolchain (link.exe, discovered via vswhere), which this development machine does not have installed."* UCC shipped **self-contained single-file instead**, which is a different artifact with a different startup cost, decided by a workload that was not installed.

BrowserAI's own AOT spike succeeded, so the toolchain was present on the machine that ran it. That is a fact about that machine, not about the next one, which is exactly why it belongs in a prerequisite list rather than in a memory.

## Versions come from git tags

**Settled 2026-08-16. The version is derived, never typed.** A git tag of `1.2.0` makes that build `1.2.0`; five commits later, with no new tag, the build is automatically `1.2.1-alpha.0.5`. Three parts plus a pre-release suffix — the shape `vpk` accepts — and nothing in a project file to edit, forget, or get out of step with the tag.

**The house four-part `base.commitcount` convention cannot be carried here**, and this is a constraint rather than a preference: [`vpk` rejects four-part version numbers outright](../kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output) — semver2, three parts only. That hazard is already recorded as a build-pipeline failure; deriving the version in a shape `vpk` accepts is what stops it ever firing.

**A consequence worth stating, because it deletes a design question rather than answering it.** There is no need for a magic development-build version number — no `0.0.0`, no sentinel, no *"is this a real release"* flag to keep in sync. **An untagged build already carries the not-a-release suffix in its own version string**, so the rule reduces to one sentence: *never self-update from a build that is not a release.* The check is a pre-release-suffix test on the running version, and it cannot be forgotten on a build where it matters, because the suffix is generated by the same mechanism that produced the version.

## The build configuration this plan has never mentioned

**Settled 2026-08-16, and every item below serves the float rather than restraining it.** That framing is not decoration — read the wrong way, every one of these reads as a pin, and this plan's [versioning policy](../README.md#versioning-policy-everything-floats-the-build-freezes-it) forbids pins. They are listed here as **requirements**; creating the files is [the first step of the build](build-order.md), not of the pass that wrote this section.

| Requirement | What it is for | Why it is not a pin |
|---|---|---|
| **An SDK floor that rolls forward** | A machine that is behind fails loudly instead of building subtly differently | A floor with roll-forward takes the **newest installed** SDK. A ceiling would be a pin; a floor is the opposite — it forbids being stale, not being current |
| **The MTP runner setting TUnit requires** | TUnit is MTP-only; the runner entry is what makes `dotnet test` work at all | It is not a version. It names a test-platform mode |
| **One file declaring every package version, all floating, with transitive pinning** | **One place the float lives.** No stale number can hide in a project file, because no project file carries one | The declaration is the float. Transitive pinning makes that one file authoritative for indirect dependencies too, so the resolved set is complete rather than partly implicit |
| **Shared build properties declared once** | Language version, nullability, analyzer severity, warnings-as-errors, target framework — set in one place, inherited everywhere | Nothing here is a dependency version |
| **`longPathAware=true` in the application manifest** | Session directories are caller-chosen and unbounded; a path over `MAX_PATH` must not be a mystery failure inside a profile tree | Not a version at all |
| **`src/` and `tests/` layout** | Separates what ships from what proves it, which is what makes publish-only gating expressible | — |
| **Publish-only gating for native compilation** | Everyday builds stay fast; only a publish pays for ILC and the MSVC link | — |

**The one-file-for-versions requirement is the load-bearing one**, and it is worth being explicit about why it is the opposite of a pin. Floating means every dependency resolves to latest at build time and the *resolved* set is recorded ([rule 1](../README.md#the-five-rules-that-make-floating-safe)). That only works if there is exactly one place a version could be declared — otherwise a floating declaration in the shared file and a hard-coded one in some project file coexist, the hard-coded one wins for that project, and the artifact contains a version nobody chose while the lock file honestly records it. **The failure is not that the number is old. It is that nothing says it is old**, which is [why-reason 2](../README.md#2-the-version-chain-floats-and-it-breaks-silently) with the pin on our side of the boundary instead of upstream's.

**And updating is the first step of touching this project, not the last step before a release.** Re-resolve everything, fix what that breaks, then do the work that was asked for. A release may only be cut from a tree that has been fully re-resolved and is green throughout, and nothing is held at an old version without the maintainer knowing it was held. These files are what make that a two-command operation rather than a search.

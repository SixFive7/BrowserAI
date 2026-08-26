// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Tests;

/// <summary>
/// What the caller receives is what the child wrote — asserted on bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every failure this file guards against is silent.</b> A dropped tool, a
/// dropped property, a content block deleted because its <c>type</c> was
/// unfamiliar, a cancellation that never leaves the process, a result inflated
/// by a few hundred <c>\uXXXX</c> escapes: none of them raise anything, none
/// change an exit code, and each looks exactly like success from every angle a
/// conventional check can see.
/// </para>
/// <para>
/// So the assertions are on <b>byte spans</b>, taken with
/// <see cref="JsonSpan"/> — a reader written for the tests and sharing nothing
/// with the product's own slicing — and never on parsed objects. Comparing two
/// parses, or two <c>ToJsonString()</c>s, normalises away escaping, whitespace
/// and numeric form, which is precisely the set of differences a proxy must not
/// introduce.
/// </para>
/// <para>
/// ⚠️ <b>Tightened 2026-08-26 (previously two claims — <i>"a call BrowserAI
/// forwarded unchanged comes back unchanged"</i> and <i>"a call whose request
/// BrowserAI rewrote comes back with every byte the child wrote, in order, plus
/// one appended <c>content</c> element"</i>).</b> The second clause described
/// artifact routing, which no longer exists: nothing rewrites a
/// <c>filename</c>, nothing moves a file, nothing writes a note. So there is one
/// claim again, and it has no exceptions on this method:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>A <c>tools/call</c> answer reaches the caller as the exact bytes the child
/// wrote.</b> Every test in this file asserts it, and
/// <see cref="ACallThatNamesAFileComesBackByteIdenticalWithNothingAppended"/> is
/// the one that asserts it about the calls the old splice used to fire on.
/// </item>
/// </list>
/// <para>
/// ⚠️ <b>One payload is still rewritten and it is named rather than hidden:</b>
/// upstream's <c>install-browser</c> advice, gated on <c>isError</c>, which
/// <c>ProvisioningRemediationTests</c> owns end to end — including a real-child
/// canary over upstream's own wording, so a reword there is a red build rather
/// than a rewrite that silently stops firing.
/// </para>
/// </remarks>
internal sealed class LosslessPassthroughTests
{
    /// <summary>
    /// The two SDK client calls whose typed results are lossy. Neither may
    /// appear anywhere the product ships.
    /// </summary>
    private static readonly string[] LossyClientCalls = ["ListToolsAsync", "CallToolAsync"];

    /// <summary>The order the double serves its canned tools in.</summary>
    private static readonly List<string> ToolOrder = ["browser_navigate", "browser_click", "browser_snapshot"];

    /// <summary>
    /// One result carrying every character class the SDK's default encoder
    /// re-escapes, plus three numeric forms nothing normalises back.
    /// </summary>
    /// <remarks>
    /// The escaping in the other direction — a sequence the child chose to
    /// write, which a <c>JsonNode</c> round trip would decode and emit raw — is
    /// <c>AnEscapeTheChildChoseStaysAnEscape</c>, because that one cannot be
    /// written in a raw string literal.
    /// </remarks>
    private const string AwkwardResult =
        """{"content":[{"type":"text","text":"Page URL: `x` it's <b>&amp;</b> café — ünïcødé"}],"structuredContent":{"ratio":1.0e2,"count":1.500,"negative":-0,"depth":{"a":{"b":[1,2,3]}}},"x-browserai-unknown-member":true}""";

    [Test]
    public async Task AResultArrivesAtTheCallerAsTheExactBytesTheChildWrote()
    {
        var (child, caller) = await RoundTripAsync(AwkwardResult);

        await Assert.That(caller).IsEquivalentTo(child);

        // Said twice on purpose. The line above is the whole claim, and it is
        // also the line that would pass if both sides were empty; these name
        // the specific things the two encoders on the path were measured
        // changing in opposite directions.
        //
        // Characters the SDK's default encoder escapes, still raw: this is the
        // +49.6% inflation our own server transport removes.
        await Assert.That(Text(caller)).Contains("`x` it's <b>&amp;</b>");
        await Assert.That(Text(caller)).Contains("ünïcødé");

        // Numeric forms nothing normalises back once they have been through a
        // parser that keeps only the value.
        await Assert.That(Text(caller)).Contains("1.0e2");
        await Assert.That(Text(caller)).Contains("1.500");
        await Assert.That(Text(caller)).Contains("-0");
        await Assert.That(Text(caller)).Contains("x-browserai-unknown-member");
    }

    [Test]
    public async Task AnEscapeTheChildChoseStaysAnEscape()
    {
        // A verbatim string, so the backslash reaches the JSON rather than the
        // C# compiler: the child really writes the six bytes é.
        //
        // This is the case that separates byte-identical from
        // semantically-lossless, and the reason the payload is spliced rather
        // than round-tripped through a JsonNode. A node decodes this to 'é' and
        // writes it back out raw: same value, two fewer bytes, and a claim of
        // byte-identity that was only ever true of unescaped input.
        const string Escaped = @"{""content"":[{""type"":""text"",""text"":""caf\u00e9 \u2014 \/""}]}";

        var (child, caller) = await RoundTripAsync(Escaped);

        await Assert.That(caller).IsEquivalentTo(child);
        await Assert.That(Text(caller)).Contains(@"caf\u00e9");
        await Assert.That(Text(caller)).Contains(@"\u2014");
        await Assert.That(Text(caller)).Contains(@"\/");
    }

    [Test]
    public async Task AnUnknownContentTypeSurvivesTheTrip()
    {
        // Through the SDK's typed path this was measured being deleted outright:
        // ContentBlock's converter throws `Unknown content type`, the server
        // turns that into a JSON-RPC SUCCESS carrying isError, and the block is
        // simply gone. Success envelope, missing payload, nothing logged where a
        // caller can see it.
        const string Unknown =
            """{"content":[{"type":"x-browserai-unknown","payload":"still here"},{"type":"text","text":"and this"}]}""";

        var (child, caller) = await RoundTripAsync(Unknown);

        await Assert.That(caller).IsEquivalentTo(child);
        await Assert.That(Text(caller)).Contains("x-browserai-unknown");
        await Assert.That(Text(caller)).Contains("still here");
    }

    [Test]
    public async Task AnUnknownPropertyOnAKnownBlockSurvivesTheTrip()
    {
        // The other half, and the quieter one: the converter keeps the block and
        // drops the property. The SDK has tests asserting exactly that, which is
        // correct forward-compatibility for a client and data loss for a proxy.
        const string Extra =
            """{"content":[{"type":"text","text":"ok","x-browserai-hint":{"kept":true},"annotations":{"audience":["user"]}}]}""";

        var (child, caller) = await RoundTripAsync(Extra);

        await Assert.That(caller).IsEquivalentTo(child);
        await Assert.That(Text(caller)).Contains("x-browserai-hint");
    }

    [Test]
    public async Task AnImagePayloadArrivesByteIdentical()
    {
        var image = Convert.ToBase64String(Enumerable.Range(0, 4096).Select(value => (byte)value).ToArray());

        // Embedded raw rather than through JsonSerializer.Serialize, and the
        // difference is the test: base64's alphabet includes '+' and '/', which
        // JavaScriptEncoder.Default escapes and this path must not. Serialising
        // it here would have put the escapes in on the double's side and hidden
        // exactly what is being measured.
        //
        // Three '$' rather than two: the literal ends in two consecutive closing
        // braces of its own, and an interpolated raw string allows one fewer
        // than it has leading '$' characters.
        var result =
            $$$"""{"content":[{"type":"image","data":"{{{image}}}","mimeType":"image/png","annotations":{"priority":0.5}}],"isError":false}""";

        var (child, caller) = await RoundTripAsync(result);

        await Assert.That(caller).IsEquivalentTo(child);
        await Assert.That(image).Contains("+");
        await Assert.That(Text(caller)).Contains(image);
    }

    [Test]
    public async Task AnIsErrorBodyIsPreservedVerbatim()
    {
        // A tool failure is data, not a protocol error: it travels as a success
        // envelope carrying isError, and the body is upstream's own text. A
        // proxy that reshaped it would be rewriting what the model reads.
        const string Failure =
            """{"content":[{"type":"text","text":"Error: page.goto: net::ERR_NAME_NOT_RESOLVED at https://nope.invalid/"}],"isError":true,"x-browserai-cause":"dns"}""";

        var (child, caller) = await RoundTripAsync(Failure);

        await Assert.That(caller).IsEquivalentTo(child);
        await Assert.That(Text(caller)).Contains("\"isError\":true");
        await Assert.That(Text(caller)).Contains("x-browserai-cause");
    }

    [Test]
    public async Task AnOversizedPayloadArrivesByteIdentical()
    {
        const int Size = 2 * 1024 * 1024;
        var text = new string('a', Size);

        var (child, caller) = await RoundTripAsync(
            $$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}""");

        await Assert.That(caller.Length).IsGreaterThanOrEqualTo(Size);

        // Compared as spans rather than with IsEquivalentTo, which walks a
        // collection element by element: at 2 MiB that assertion alone took
        // 139 s and dominated the whole suite (measured 2026-08-16). The
        // offset is the assertion's subject so a mismatch still names where,
        // which is the only thing the slow version bought.
        await Assert.That(FirstDifference(child, caller)).IsEqualTo(-1);
    }

    /// <summary>
    /// A call that names a file comes back byte-identical too, with nothing
    /// appended and the file where the child put it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Tightened 2026-08-26 (previously
    /// <c>AnArtifactCallKeepsEveryByteTheChildWroteAndGainsExactlyOneBlock</c>,
    /// which asserted the child's bytes survived <b>plus exactly one appended
    /// <c>content</c> element</b>).</b> There is no appended element. Nothing
    /// between the two servers rewrites a <c>filename</c>, routes a file, sweeps
    /// an output root or writes a note, so the second arm of the old claim
    /// describes machinery that no longer exists — and the claim collapses back
    /// to the one it started as: <b>a <c>tools/call</c> answer reaches the
    /// caller as the exact bytes the child wrote</b>, with no exception for the
    /// calls that name a file.
    /// </para>
    /// <para>
    /// <b>The witness is deliberately a call that WOULD have been rewritten.</b>
    /// <c>browser_take_screenshot</c> with a <c>filename</c> is the exact shape
    /// the old splice fired on, so a proxy that still appended anything to it
    /// fails here rather than somewhere subtler.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallThatNamesAFileComesBackByteIdenticalWithNothingAppended()
    {
        // An escape the child chose, in the payload that used to be edited. A
        // JsonNode round trip would decode `café` and re-emit a raw `é`, which
        // is the difference between forwarding the bytes and rebuilding them.
        const string Escaped =
            @"{""content"":[{""type"":""text"",""text"":""- [Screenshot](café.png) — done""}],""x-browserai-unknown-member"":true}";

        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour
            {
                RawResult = Escaped,
                WritesArtifactBytes = 32,
            });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var response = await rig.Client.SendAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_take_screenshot",
            ["arguments"] = new JsonObject
            {
                [SessionToolSurface.SessionParameter] = rig.Session!,
                [SessionToolSurface.WhyParameter] = "the suite exercising this call",
                ["filename"] = "login.png",
            },
        });

        var child = JsonSpan.MemberOf(SentByChild(rig, "result"), "result");
        var caller = JsonSpan.MemberOf(response.Frame, "result");

        await Assert.That(caller).IsEquivalentTo(child);

        // Said separately, because "identical" is also satisfied by both being
        // empty: the escape the child chose is untouched and the member no
        // contract knows about is still there.
        await Assert.That(Text(caller)).Contains(@"café.png");
        await Assert.That(Text(caller)).Contains("x-browserai-unknown-member");

        // ⚠️ AND THE ARGUMENT REACHED THE CHILD AS THE CALLER SPELLED IT. A
        // proxy that still rewrote `filename` to an absolute path could satisfy
        // every byte assertion above, because the rewrite happens on the way IN.
        var forwarded = rig.Child.FramesReceived
            .Select(FrameChannel.TextOf)
            .Last(frame => frame.Contains("browser_take_screenshot", StringComparison.Ordinal));

        await Assert.That((string?)JsonNode.Parse(forwarded)!["params"]!["arguments"]!["filename"])
            .IsEqualTo("login.png");

        // Flat, and where the child resolved it: the output root itself, with
        // no typed folder anywhere above it.
        var expected = Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "login.png");

        await Assert.That(File.Exists(expected)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "page"))).IsFalse();
    }

    /// <summary>
    /// The index of the first differing byte, or <c>-1</c> when the two are
    /// identical. Length mismatches report at the end of the shorter span.
    /// </summary>
    private static int FirstDifference(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (expected.SequenceEqual(actual))
        {
            return -1;
        }

        var shared = Math.Min(expected.Length, actual.Length);

        for (var i = 0; i < shared; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return shared;
    }

    [Test]
    public async Task AChildJsonRpcErrorReachesTheCallerUnchanged()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                ErrorCode = -32000,
                ErrorMessage = "the fake child refused this navigation",
                RawErrorData = """{"reason":"programmed","nested":{"depth":2}}""",
            });

        var response = await rig.Client.SendAsync("tools/call", Call(rig, "browser_navigate"));

        await Assert.That(response.Error).IsNotNull();

        // The bytes, first: code, message and data all as the child wrote them.
        await Assert.That(JsonSpan.MemberOf(response.Frame, "error"))
            .IsEquivalentTo(JsonSpan.MemberOf(SentByChild(rig, "error"), "error"));

        // Then the two named claims, because "identical" would also be
        // satisfied by both being wrong in the same way.
        //
        // No `Request failed (remote): ` prefix. The SDK still adds one -- the
        // proxy simply never reads the message off the exception, so it never
        // meets it. SdkErrorShapeTests is what keeps that fact checked.
        await Assert.That(response.Error!["message"]!.GetValue<string>())
            .IsEqualTo("the fake child refused this navigation");

        await Assert.That(response.Error["code"]!.GetValue<int>()).IsEqualTo(-32000);
        await Assert.That(response.Error["data"]!.ToJsonString())
            .IsEqualTo("""{"reason":"programmed","nested":{"depth":2}}""");
    }

    [Test]
    public async Task CancellingACallIsObservedAtTheFakeChild()
    {
        // Held rather than delayed: a child parked inside its own dispatch
        // could not hear the notification this test exists to prove reaches it.
        using var release = new CancellationTokenSource();

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"too late"}]}""",
                HoldUntil = Task.Delay(Timeout.Infinite, release.Token),
            });

        var id = await rig.Client.BeginAsync("tools/call", Call(rig, "browser_navigate"));

        await WaitUntilAsync(() => rig.Child.MethodsReceived.Contains("tools/call"));

        // The caller cancels the way a caller does: a notification naming the
        // id it sent. Everything after this is about what BrowserAI does with
        // that, and the SDK's answer -- measured on both the raw and the typed
        // client paths -- is nothing at all.
        await rig.Client.NotifyAsync(
            "notifications/cancelled",
            new JsonObject { ["requestId"] = id, ["reason"] = "the test changed its mind" });

        await WaitUntilAsync(() => rig.Child.MethodsReceived.Contains("notifications/cancelled"));

        // Observed at the child, and naming the id BrowserAI actually sent --
        // not merely "a cancellation happened somewhere in the proxy".
        var cancelled = rig.Child.FramesReceived
            .Select(frame => JsonNode.Parse(FrameChannel.TextOf(frame)))
            .Single(node => node?["method"]?.GetValue<string>() == "notifications/cancelled");

        var call = rig.Child.FramesReceived
            .Select(frame => JsonNode.Parse(FrameChannel.TextOf(frame)))
            .Single(node => node?["method"]?.GetValue<string>() == "tools/call");

        await Assert.That(cancelled!["params"]!["requestId"]!.GetValue<string>())
            .IsEqualTo(call!["id"]!.GetValue<string>());

        // Exactly one, which is re-verification row 28's actual requirement: if
        // the SDK ever starts relaying cancellation itself, the hand-rolled
        // path becomes a double-send and nothing else in the suite would say so.
        await Assert.That(rig.Child.MethodsReceived.Count(method => method == "notifications/cancelled")).IsEqualTo(1);

        await release.CancelAsync();
    }

    [Test]
    public async Task AChildProgressNotificationReachesTheCallerUnderTheCallersToken()
    {
        const string Token = "caller-chose-this";

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"done"}]}""",
                ProgressUpdates = 2,
            });

        var parameters = Call(rig, "browser_navigate");
        parameters["_meta"] = new JsonObject { ["progressToken"] = Token };

        _ = await rig.Client.RoundTripAsync("tools/call", parameters);

        // The relay is asynchronous, so a notification may land after the
        // result to the call that provoked it. A client only sees what it
        // reads.
        await rig.Client.ReadUntilAsync(() => ProgressFrames(rig).Count >= 2);

        var relayed = ProgressFrames(rig);

        await Assert.That(relayed.Count).IsGreaterThanOrEqualTo(2);

        // The caller's own token, not one the proxy minted. A relay that
        // rewrote it would still satisfy a test that only counted frames, and
        // the caller would silently never match a progress report to its call.
        foreach (var frame in relayed)
        {
            await Assert.That(frame!["params"]!["progressToken"]!.GetValue<string>()).IsEqualTo(Token);
        }
    }

    [Test]
    public async Task AChildThatDiesMidCallProducesANamedErrorRatherThanASuccess()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { DieWithoutAnswering = true });

        var response = await rig.Client.SendAsync("tools/call", Call(rig, "browser_navigate"));

        // ⚠️ This is the assertion that changed at step 9, and the change is the
        // point. Measured at step 8: the same call produced a JSON-RPC SUCCESS
        // carrying isError and the text "An error occurred invoking
        // 'browser_navigate'." -- byte-identical to what an unknown content type
        // produced, naming neither cause. A success envelope whose only bad news
        // is inside the body is the founding failure shape, arriving from our
        // own dependency.
        await Assert.That(response.Result).IsNull();
        await Assert.That(response.Error).IsNotNull();
        await Assert.That(response.Error!["code"]!.GetValue<int>()).IsEqualTo((int)McpErrorCode.InternalError);

        // And the cause is named, rather than living only in the log. The
        // wording is transport-level on purpose: §H.4's model-facing catalogue
        // is step 13's, and inventing its text here would be writing a
        // catalogue entry nobody reviewed.
        await Assert.That(response.Error["message"]!.GetValue<string>()).Contains("tools/call");
        await Assert.That(response.Error["message"]!.GetValue<string>()).Contains("IOException");
        await Assert.That(rig.Child.HasStopped).IsTrue();
    }

    /// <summary>
    /// <c>tools/list</c> is the one answer that is deliberately rewritten, and
    /// everything about the child's own tools has to survive the rewrite.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This test asserted byte-identity until build-order step 12, and the
    /// assertion had to go rather than be relaxed.</b> Rewriting <c>tools/list</c>
    /// — the authored tools in front, a required <c>session</c> injected into
    /// every upstream schema — is in scope by the charter, so the old assertion
    /// was asserting the absence of a feature. What is kept is every property
    /// byte-identity was there to protect: upstream's names, upstream's order,
    /// and the two vendor extensions a typed round trip drops. Byte-identity
    /// itself now lives on <c>tools/call</c>, where nothing is rewritten.
    /// </remarks>
    /// <remarks>
    /// ⚠️ <b>Two injected parameters since 2026-08-20 (previously one).</b>
    /// <c>why</c> rides the same path <c>session</c> does — mutating the
    /// <see cref="JsonNode"/> the child sent rather than rebuilding it — so this
    /// test is what says the second one did not disturb the first: <c>url</c>
    /// still holds position 0, both are appended in order, and upstream's own
    /// <c>required</c> entry is still ahead of both.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ToolsListKeepsUpstreamsNamesOrderAndExtensionsThroughTheRewrite()
    {
        // Order is a requirement rather than an accident: the spec SHOULDs
        // deterministic ordering because callers cache prompts on it, and a
        // rewrite that reordered would cost a cache miss per call with nothing
        // failing.
        const string Tools =
            """{"tools":[{"name":"browser_navigate","description":"a","inputSchema":{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"url":{"type":"string","x-vendor-hint":"absolute"}},"required":["url"]},"x-tool-extension":{"kept":true}},{"name":"browser_click","description":"b","inputSchema":{"type":"object","properties":{}}},{"name":"browser_snapshot","description":"c","inputSchema":{"type":"object","properties":{}}}]}""";

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child => child.ToolsListResult = Tools);

        var response = await rig.Client.SendAsync("tools/list");

        var names = response.Result!["tools"]!.AsArray()
            .Select(tool => tool!["name"]!.GetValue<string>())
            .ToList();

        await Assert.That(names).IsEquivalentTo([.. SessionToolSurface.Names, .. ToolOrder]);

        // A typed ListToolsResult round trip drops these two, because Tool
        // carries no [JsonExtensionData]. inputSchema survives either way, being
        // a JsonElement; the top-level extension does not. The rewrite is done on
        // the JsonNode precisely so that both still arrive.
        await Assert.That(Text(JsonSpan.MemberOf(response.Frame, "result"))).Contains("x-tool-extension");
        await Assert.That(Text(JsonSpan.MemberOf(response.Frame, "result"))).Contains("x-vendor-hint");

        var navigate = response.Result["tools"]![SessionToolSurface.Names.Count]!;

        // `session` and `why` are appended, so `url` keeps its position, and
        // both are required alongside whatever upstream already required.
        await Assert.That(string.Join(",", navigate["inputSchema"]!["properties"]!.AsObject().Select(property => property.Key)))
            .IsEqualTo("url,session,why");

        await Assert.That(navigate["inputSchema"]!["required"]!.ToJsonString()).IsEqualTo("""["url","session","why"]""");

        // A tool that declared no properties at all still gains both, rather
        // than being skipped because there was nothing to append to.
        var click = response.Result["tools"]![SessionToolSurface.Names.Count + 1]!;
        await Assert.That(click["inputSchema"]!["properties"]!["session"]).IsNotNull();
        await Assert.That(click["inputSchema"]!["properties"]!["why"]).IsNotNull();
        await Assert.That(click["inputSchema"]!["required"]!.ToJsonString()).IsEqualTo("""["session","why"]""");
    }

    [Test]
    public async Task ThereIsNoTypedToolHandlerLeftToFallBackTo()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();
        var options = rig.Proxy.ServerOptions();

        // The lossy path is not merely unused, it is absent: if the filter ever
        // stopped short-circuiting, the caller would get -32601 rather than a
        // quietly re-serialised answer. A loud wrong answer can be found.
        await Assert.That(options.Handlers.ListToolsHandler is null).IsTrue();
        await Assert.That(options.Handlers.CallToolHandler is null).IsTrue();

        // And the filter that replaces them is registered where a Core/AOT
        // server can reach it -- not through WithMessageFilters, which is a DI
        // extension in the hosting package.
        await Assert.That(options.Filters.Message.IncomingFilters.Count).IsEqualTo(1);

        // Still advertised, because Capabilities.Tools is what decides that,
        // independently of whether any handler exists.
        await Assert.That(options.Capabilities?.Tools).IsNotNull();
    }

    [Test]
    public async Task TheProductNeverCallsTheOverloadThatDropsToolsSilently()
    {
        // Deviation 2 asked for the raw ListToolsAsync overload because the
        // convenience one drops tools whose x-mcp-header annotations fail
        // SEP-2243 validation, with no error anywhere. Step 9 goes further and
        // calls neither: tools/list is forwarded as a raw JsonRpcRequest, so the
        // trap is unreachable rather than avoided. This is what keeps that true.
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(LossyClientCalls
                .Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}: calls '{needle}'"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task AFrameThatFailsToParseIsAnsweredRatherThanOnlyLogged()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        // Valid JSON up to and including the id, then not. A transport that only
        // logs this leaves the sender waiting for a response that is never
        // coming, with the reason on a channel the sender cannot read.
        await rig.Client.SendRawAsync("""{"jsonrpc":"2.0","id":4242,"method":"tools/list","params":{"broken":}""");

        var answer = await rig.Client.AwaitAsync(4242, "the malformed frame");

        await Assert.That(answer.Error).IsNotNull();
        await Assert.That(answer.Error!["code"]!.GetValue<int>()).IsEqualTo((int)McpErrorCode.ParseError);
        await Assert.That(rig.Logs.Logged("could not be parsed and was dropped")).IsTrue();

        // The session survived it: one bad frame from a peer must not end a
        // conversation that is otherwise healthy.
        var tools = await rig.Client.RoundTripAsync("tools/list");
        await Assert.That(tools["tools"]!.AsArray().Count).IsEqualTo(SessionToolSurface.Names.Count + 2);
    }

    [Test]
    public async Task AFrameWithNoRecoverableIdIsDroppedLoudlyRatherThanAnswered()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        // Nothing to answer to. Inventing an id would be worse than silence: it
        // would resolve a request the caller never made.
        await rig.Client.SendRawAsync("""{"jsonrpc":"2.0","method":"tools/list","params":{""");

        var tools = await rig.Client.RoundTripAsync("tools/list");

        await Assert.That(tools["tools"]!.AsArray().Count).IsEqualTo(SessionToolSurface.Names.Count + 2);
        await Assert.That(rig.Logs.Logged("could not be parsed and was dropped")).IsTrue();
        await Assert.That(rig.Logs.Logged("answered -32700")).IsFalse();
    }

    /// <summary>
    /// One <c>tools/call</c>, naming the rig's own session.
    /// </summary>
    /// <remarks>
    /// <c>session</c> is mandatory since step 13: a call without one is refused
    /// by BrowserAI and never reaches a child, so every assertion in this file
    /// about what the child received would be an assertion about a refusal.
    /// </remarks>
    private static JsonObject Call(McpTestHarness rig, string tool)
    {
        var arguments = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>" };

        if (rig.Session is { } session)
        {
            arguments["session"] = session;
            arguments["why"] = "the suite exercising this call";
        }

        return new JsonObject { ["name"] = tool, ["arguments"] = arguments };
    }

    /// <summary>
    /// Neither injected parameter reaches the child, and everything the caller
    /// wrote beside them does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The child has never heard of <c>session</c> or <c>why</c>; BrowserAI
    /// put them in the schema.</b> Forwarding either would hand upstream's zod
    /// parse a property its own schema does not declare, and the failure mode is
    /// not a clean error — it is upstream's own decision about unknown keys,
    /// taken per tool, which this project does not control and must not depend
    /// on.
    /// </para>
    /// <para>
    /// <b>Asserted over the frame the double actually received</b>, rather than
    /// over the arguments object the test built: the strip happens on a clone
    /// inside the proxy, so a version that stripped the caller's own node
    /// instead would pass an in-process check and corrupt a request the SDK may
    /// still read.
    /// </para>
    /// <para>
    /// <b>And the positive control is the point.</b> A strip that removed
    /// everything would satisfy "neither is present" perfectly, so <c>url</c>
    /// and a vendor key nobody declared are both required to survive.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NeitherInjectedParameterReachesTheChildAndEverythingElseDoes()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        _ = await rig.Client.SendAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject
            {
                ["url"] = "data:text/html,<h1>ok</h1>",
                [SessionToolSurface.SessionParameter] = rig.Session!,
                [SessionToolSurface.WhyParameter] = "establishing that the page loads at all before anything else is tried",
                ["x-caller-extension"] = "kept",
            },
        });

        var forwarded = rig.Child.FramesReceived
            .Select(FrameChannel.TextOf)
            .Last(frame => frame.Contains("browser_navigate", StringComparison.Ordinal));

        var arguments = JsonNode.Parse(forwarded)!["params"]!["arguments"]!.AsObject();

        await Assert.That(arguments[SessionToolSurface.SessionParameter]).IsNull();
        await Assert.That(arguments[SessionToolSurface.WhyParameter]).IsNull();

        // The whole frame, not only the arguments object: a `why` left anywhere
        // in the forwarded params is a `why` upstream may act on.
        await Assert.That(forwarded).DoesNotContain("establishing that the page loads");

        // The positive control.
        await Assert.That((string?)arguments["url"]).IsEqualTo("data:text/html,<h1>ok</h1>");
        await Assert.That((string?)arguments["x-caller-extension"]).IsEqualTo("kept");
    }

    private static string Text(byte[] span) => Encoding.UTF8.GetString(span);

    private static List<JsonNode?> ProgressFrames(McpTestHarness rig) =>
        [.. rig.Client.FramesReceived
            .Select(frame => JsonNode.Parse(FrameChannel.TextOf(frame)))
            .Where(node => node?["method"]?.GetValue<string>() == "notifications/progress")];

    /// <summary>The last frame the double sent that carries the named member.</summary>
    private static byte[] SentByChild(McpTestHarness rig, string member) =>
        rig.Child.FramesSent.Last(frame => JsonSpan.Has(frame, member));

    /// <summary>
    /// Drives one <c>tools/call</c> and returns the <c>result</c> span from each
    /// end of the proxy.
    /// </summary>
    private static async Task<(byte[] Child, byte[] Caller)> RoundTripAsync(string rawResult)
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = rawResult });

        var response = await rig.Client.SendAsync("tools/call", Call(rig, "browser_navigate"));

        if (response.Error is { } error)
        {
            throw new InvalidOperationException($"The call failed instead of returning a result: {error.ToJsonString()}");
        }

        return (JsonSpan.MemberOf(SentByChild(rig, "result"), "result"), JsonSpan.MemberOf(response.Frame, "result"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestDefaults.InProcessHang;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition never became true within the rig's patience.");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>
/// The SDK behaviours step 9 routes around, kept under test so the reasons for
/// routing around them cannot age quietly.
/// </summary>
/// <remarks>
/// Same pattern as <c>SdkStdioClientTransportTests</c>: the product no longer
/// travels these paths, so nothing else in the suite would notice if upstream
/// changed them — and a deviation whose justification has silently expired is
/// how a component nobody needs survives a rewrite.
/// </remarks>
internal sealed class SdkErrorShapeTests
{
    [Test]
    public async Task TheSdkStillPrefixesARemoteErrorMessageAndStillKeepsItsData()
    {
        var hop = new PipeDuplex("sdk error hop");
        var child = new FakePlaywrightChild(hop);

        child.Tools["browser_navigate"] = new FakeToolBehaviour
        {
            ErrorCode = -32000,
            ErrorMessage = "the fake child refused this navigation",
            RawErrorData = """{"reason":"programmed"}""",
        };

        child.Start();

        var client = await McpClient.CreateAsync(
            new PipeClientTransport(hop),
            TestDefaults.ClientOptions(TestDefaults.ChildProtocolCeiling));

        try
        {
            var request = new JsonRpcRequest
            {
                Id = new RequestId("sdk-error-1"),
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = "browser_navigate",
                    ["arguments"] = new JsonObject(),
                },
            };

            var failure = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.SendRequestAsync(request));

            // The prefix deviation 8 asks to be stripped. It is still added, so
            // a proxy that read its message off this exception would still need
            // to strip it -- BrowserAI reads the child's frame instead.
            await Assert.That(failure!.Message).IsEqualTo("Request failed (remote): the fake child refused this navigation");
            await Assert.That((int)failure.ErrorCode).IsEqualTo(-32000);

            // And the half of deviation 8 that turned out not to need doing:
            // `data` is destructured into Exception.Data rather than lost.
            await Assert.That(failure.Data.Count).IsGreaterThan(0);
        }
        finally
        {
            await client.DisposeAsync();
            await hop.CompleteWritersAsync();
            await child.DisposeAsync();
        }
    }
}

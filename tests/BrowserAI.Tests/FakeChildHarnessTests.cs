// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace BrowserAI.Tests;

/// <summary>
/// The in-process layer: test client → BrowserAI → fake child, over pipes, in
/// milliseconds, with no process and no Node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every capability the double exists for has a test here, and that is the
/// point of the file.</b> A capability with no test is one the next step
/// discovers is missing — at the moment it is needed, in the middle of writing
/// something else. The capabilities are the ones
/// [TESTING.md](../../TESTING.md#we-write-our-own-harness) lists
/// for <c>FakePlaywrightChild</c>: a canned <c>tools/list</c>, a programmable
/// result, an injected error, a delay, mid-call death, an unknown content type
/// and an oversized payload.
/// </para>
/// <para>
/// <b>What these tests do not cover, said here rather than implied.</b> This
/// layer proves the framing, the serialisation and the proxy's handlers. It
/// proves nothing about process launch, job containment, stderr classification
/// or exit codes — those are steps 5, 6 and 7, against real processes, and
/// re-proving them here would need the processes back.
/// </para>
/// </remarks>
internal sealed class FakeChildHarnessTests
{
    private const string NavigateResult =
        """{"content":[{"type":"text","text":"Page URL: data:text/html,<h1>ok</h1>"}]}""";

    /// <summary>
    /// What this layer must not be able to reach, spelled in halves <b>so that
    /// this file does not match its own scan</b>. Same trade as
    /// <c>NeverByImageNameTests</c>: an exclusion list naming this file would
    /// create the one file in the layer the rule does not apply to.
    /// </summary>
    private static readonly string[] Forbidden =
    [
        "Job" + "Launcher",
        "Job" + "Object",
        "Launched" + "Process",
        "Process" + "StartInfo",
        "Raw" + "StdioClient",
        "Published" + "Slice",
        "Payload" + "Layout",
        "node" + ".exe",
    ];

    [Test]
    public async Task ATestClientDrivesTheProxyToTheFakeChildAndBack()
    {
        var stopwatch = Stopwatch.StartNew();

        await using (var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult }))
        {
            var tools = await rig.Client.RoundTripAsync("tools/list");
            var call = await rig.Client.RoundTripAsync("tools/call", Call(rig, "browser_navigate"));

            // The child's two, behind BrowserAI's five authored ones. Written as
            // a sum rather than as 7, so a sixth authored tool moves this number
            // by construction instead of by somebody remembering.
            await Assert.That(tools["tools"]!.AsArray().Count).IsEqualTo(SessionToolSurface.Names.Count + 2);
            await Assert.That(TextOf(call)).IsEqualTo("Page URL: data:text/html,<h1>ok</h1>");

            // Both hops really carried it: the double saw the call, and the
            // caller's answer came back through the server transport.
            await Assert.That(rig.Child.MethodsReceived).Contains("tools/call");
        }

        stopwatch.Stop();

        // Not a benchmark. The number this defends against is the SDK's
        // five-second DiscoverProbeTimeout, which an unanswered probe costs on
        // every connect with no error anywhere.
        await Assert.That(stopwatch.Elapsed).IsLessThan(TestDefaults.RigBudget);
    }

    [Test]
    public async Task TheFakeChildServesACannedToolsList()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.ToolsListResult =
                """{"tools":[{"name":"browser_take_screenshot","description":"Take a screenshot","inputSchema":{"type":"object","properties":{}}}]}""");

        var tools = await rig.Client.RoundTripAsync("tools/list");

        // Byte-for-byte upstream's name. Renaming is settled as forbidden, so
        // this asserts identity rather than exercising a map. It sits after the
        // five authored tools, which is the only thing the rewrite moves.
        await Assert.That(tools["tools"]!.AsArray().Count).IsEqualTo(SessionToolSurface.Names.Count + 1);
        await Assert.That(tools["tools"]![SessionToolSurface.Names.Count]!["name"]!.GetValue<string>())
            .IsEqualTo("browser_take_screenshot");
    }

    [Test]
    public async Task TheFakeChildReturnsAProgrammedResult()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_snapshot"] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"- heading \"ok\" [level=1]"}]}""",
            });

        var call = await rig.Client.RoundTripAsync("tools/call", Call(rig, "browser_snapshot"));

        await Assert.That(TextOf(call)).IsEqualTo("- heading \"ok\" [level=1]");

        // Success omits isError entirely; a proxy that materialised it as
        // false would already be changing the body.
        await Assert.That(call["isError"]).IsNull();
    }

    [Test]
    public async Task TheFakeChildInjectsAJsonRpcError()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                ErrorCode = -32000,
                ErrorMessage = "the fake child refused this navigation",
                RawErrorData = """{"reason":"programmed"}""",
            });

        var response = await rig.Client.SendAsync("tools/call", Call(rig, "browser_navigate"));

        // Measured 2026-08-16 against ModelContextProtocol 2.2.0, and recorded
        // here as exact equality rather than a containment check because each
        // part is a separate claim about what survives a JSON-RPC error's trip
        // through the proxy.
        await Assert.That(response.Error).IsNotNull();
        await Assert.That(response.Error!["code"]!.GetValue<int>()).IsEqualTo(-32000);

        // ⚠️ Updated at step 9. This line read
        // "Request failed (remote): the fake child refused this navigation"
        // until 2026-08-16, pinning the prefix the SDK adds. The SDK still adds
        // it -- SdkErrorShapeTests is what keeps that checked -- but BrowserAI
        // now answers from the child's own error frame and never reads the
        // message off the exception, so the caller sees what the child sent.
        await Assert.That(response.Error["message"]!.GetValue<string>())
            .IsEqualTo("the fake child refused this navigation");

        // `data` arrives verbatim and unflattened, as it did before step 9 --
        // which is why the reconstruction that deviation 8 asked for was never
        // built: it was solving a problem that did not exist.
        await Assert.That(response.Error["data"]!.ToJsonString()).IsEqualTo("""{"reason":"programmed"}""");
    }

    [Test]
    public async Task TheFakeChildDelaysAResult()
    {
        var delay = TimeSpan.FromMilliseconds(150);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult, Delay = delay });

        var stopwatch = Stopwatch.StartNew();
        var call = await rig.Client.RoundTripAsync("tools/call", Call(rig, "browser_navigate"));
        stopwatch.Stop();

        await Assert.That(stopwatch.Elapsed).IsGreaterThanOrEqualTo(delay);
        await Assert.That(TextOf(call)).IsEqualTo("Page URL: data:text/html,<h1>ok</h1>");
    }

    [Test]
    public async Task TheFakeChildDiesMidCall()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { DieWithoutAnswering = true });

        var response = await rig.Client.SendAsync("tools/call", Call(rig, "browser_navigate"));

        // A defined answer rather than a hang, which is the property a proxy
        // loses by default. The double really does die: this is what says the
        // capability is the double's rather than the rig's.
        await Assert.That(rig.Child.HasStopped).IsTrue();

        // ⚠️ Updated at step 9, and the change is the whole point of that step.
        // Until 2026-08-16 these three lines asserted a JSON-RPC *success*
        // carrying isError and the body "An error occurred invoking
        // 'browser_navigate'." -- byte-identical to what an unknown content type
        // produced, naming neither cause. That is the founding failure shape
        // arriving from the SDK, and the shape it is answered in now is
        // LosslessPassthroughTests'
        // AChildThatDiesMidCallProducesANamedErrorRatherThanASuccess.
        await Assert.That(response.Error).IsNotNull();
        await Assert.That(response.Result).IsNull();

        // The cause still reaches the log, and now it reaches the caller too.
        await Assert.That(rig.Logs.Logged("The child did not answer")).IsTrue();
    }

    [Test]
    public async Task TheFakeChildReturnsAnUnknownContentType()
    {
        const string Unknown =
            """{"content":[{"type":"x-browserai-unknown","payload":"still here"}]}""";

        // Arm one: the double really emits it, byte for byte, with nothing of
        // ours between the assertion and the wire.
        await using (var direct = await McpTestHarness.DirectToTheChildAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = Unknown }))
        {
            var response = await direct.Client.SendAsync("tools/call", Call(direct, "browser_navigate"));

            await Assert.That(response.Frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Unknown)) >= 0).IsTrue();
        }

        // Arm two: the same bytes through the proxy.
        //
        // ⚠️ Updated at step 9, deliberately handed over rather than deleted.
        // Until 2026-08-16 this arm asserted the *lossy* behaviour so that
        // fixing it would turn the test red: the SDK's typed ContentBlock
        // converter threw `Unknown content type: 'x-browserai-unknown'`, the
        // server turned that into a JSON-RPC SUCCESS carrying isError, and the
        // block was gone -- every conventional signal green and the payload
        // missing. The typed path is now unreachable, so the block survives.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = Unknown });

        var throughTheProxy = await rig.Client.SendAsync("tools/call", Call(rig, "browser_navigate"));

        await Assert.That(throughTheProxy.Error).IsNull();
        await Assert.That(throughTheProxy.Result!["isError"]).IsNull();
        await Assert.That(throughTheProxy.Frame.AsSpan().IndexOf("x-browserai-unknown"u8) >= 0).IsTrue();
    }

    [Test]
    public async Task TheFakeChildReturnsAnOversizedPayload()
    {
        const int Size = 2 * 1024 * 1024;
        var text = new string('a', Size);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(child =>
            child.Tools["browser_snapshot"] = new FakeToolBehaviour
            {
                RawResult = $$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}""",
            });

        var call = await rig.Client.RoundTripAsync("tools/call", Call(rig, "browser_snapshot"));

        // The size is what matters: a frame this large is the case a
        // byte-at-a-time reader over a pipe turns into a quadratic hang, and the
        // case a default 64 KiB pause threshold turns into a deadlock.
        await Assert.That(TextOf(call).Length).IsEqualTo(Size);
    }

    [Test]
    public async Task TheFakeChildAnswersServerDiscoverWithMethodNotFound()
    {
        await using var rig = await McpTestHarness.DirectToTheChildAsync();

        var response = await rig.Client.SendAsync("server/discover");

        // -32601, matching what @playwright/mcp 0.0.79 was measured answering on
        // 2026-08-16. BrowserAI's own end answers -32602, because the SDK
        // implements 2026-07-28 and the child does not; a double of the child
        // that answered -32602 would be doubling the proxy instead.
        await Assert.That(response.Error).IsNotNull();
        await Assert.That(response.Error!["code"]!.GetValue<int>()).IsEqualTo(-32601);
    }

    [Test]
    public async Task TheClientPinIsWhatSkipsTheDiscoverProbe()
    {
        // The SDK's default, read from the SDK rather than from a document.
        await Assert.That(new McpClientOptions().DiscoverProbeTimeout).IsEqualTo(TimeSpan.FromSeconds(5));

        // Pinned, as the product pins it: no probe is issued at all.
        await using (var pinned = await McpTestHarness.ThroughTheProxyAsync())
        {
            await Assert.That(pinned.Child.MethodsReceived).DoesNotContain("server/discover");
        }

        // Unpinned, against a double that answers: the probe IS issued, which is
        // what makes the line above evidence rather than a tautology.
        var (probedMethods, _) = await ConnectUnpinnedAsync(answersDiscover: true);
        await Assert.That(probedMethods).Contains("server/discover");

        // Unpinned, against a double that drops it: the connect pays the whole
        // probe timeout and reports nothing. This is the 30-seconds-per-rig
        // failure of the 2026-08-15 spike, reproduced in 250 ms because
        // TestDefaults pins the timeout short.
        //
        // ⚠️ The comparison carries a millisecond of slack, and it is not a
        // weakening. .NET's timer and `Stopwatch` read different clocks — the
        // timer wheel against QPC — so a 250 ms timeout can be *observed*
        // finishing at 249.57 ms. Measured on 2026-08-16: this assertion failed
        // by 0.43 ms, once, on a suite that had passed six times running, which
        // is a red build wearing a disguise. What is being asserted is that the
        // connect paid the whole timeout rather than returning promptly, and a
        // regression would come back in single-digit milliseconds.
        var (_, stalledFor) = await ConnectUnpinnedAsync(answersDiscover: false);
        await Assert.That(stalledFor).IsGreaterThanOrEqualTo(TestDefaults.DiscoverProbeTimeout - TimeSpan.FromMilliseconds(2));
    }

    [Test]
    public async Task TheRigDetectsAPipeNobodyClosed()
    {
        // The rig's own teardown assertion, exercised against a hop it is not
        // allowed to pass. Without this, "no test leaves a live pipe behind" is
        // a check that has never been seen to fail.
        var hop = new PipeDuplex("a hop nobody closed");

        await Assert.That(hop.WhatIsStillLive()).IsNotNull();

        await hop.CompleteWritersAsync();

        await Assert.That(hop.WhatIsStillLive()).IsNull();
    }

    [Test]
    public async Task TheCapturingProviderRecordsWhatTheProxyLogged()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        // The product's own narration, captured. Observability is a feature
        // requirement here, and a log line nothing asserts on is one that can
        // stop being written without anybody noticing.
        await Assert.That(rig.Logs.Logged($"negotiated={TestDefaults.ChildProtocolCeiling}")).IsTrue();
    }

    [Test]
    public async Task TheTUnitProviderRoutesRecordsIntoTheTestsOwnOutput()
    {
        var marker = $"tunit-logger-{Guid.NewGuid():N}";

        using (var provider = new TUnitLoggerProvider())
        {
            provider.CreateLogger("BrowserAI.Tests.Probe").LogProbe(marker);
        }

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains(marker);
    }

    [Test]
    public async Task NothingInThisLayerCanStartAProcessOrCreateAJob()
    {
        // "No live job and no live process" holds here because there is no way
        // to make one, and this is what makes that a mechanism rather than an
        // observation about today's code. The rig asserts the pipes; this
        // asserts the layer cannot reach a launcher at all.
        //
        // The files are named rather than globbed, so a rename fails this test
        // instead of silently dropping a file out of the scan.
        string[] layer =
        [
            "tests/BrowserAI.Tests/FakeChildHarnessTests.cs",
            "tests/BrowserAI.Tests/LosslessPassthroughTests.cs",
            "tests/BrowserAI.Tests/Harness/JsonSpan.cs",
            "tests/BrowserAI.Tests/Harness/McpTestHarness.cs",
            "tests/BrowserAI.Tests/Harness/FakePlaywrightChild.cs",
            "tests/BrowserAI.Tests/Harness/PipeClientTransport.cs",
            "tests/BrowserAI.Tests/Harness/PipeDuplex.cs",
            "tests/BrowserAI.Tests/Harness/RawPipeClient.cs",
            "tests/BrowserAI.Tests/Harness/FrameChannel.cs",
            "tests/BrowserAI.Tests/Harness/TestDefaults.cs",
            "tests/BrowserAI.Tests/Harness/CapturingLoggerProvider.cs",
            "tests/BrowserAI.Tests/Harness/TUnitLoggerProvider.cs",
        ];

        var offenders = new List<string>();

        foreach (var relative in layer)
        {
            var file = new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, relative));

            if (!file.Exists)
            {
                offenders.Add($"{relative}: missing, so this scan no longer covers the layer it names");
                continue;
            }

            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(Forbidden
                .Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{relative}: reaches '{needle}', which this layer must not"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
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
        }

        return new JsonObject { ["name"] = tool, ["arguments"] = arguments };
    }

    private static string TextOf(JsonObject result) =>
        result["content"]![0]!["text"]!.GetValue<string>();

    /// <summary>
    /// Connects an unpinned SDK client straight to a double, and reports what
    /// the double heard and how long it took.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Methods, TimeSpan Elapsed)> ConnectUnpinnedAsync(bool answersDiscover)
    {
        var hop = new PipeDuplex("probe hop");
        var child = new FakePlaywrightChild(hop) { AnswersDiscover = answersDiscover };
        child.Start();

        var stopwatch = Stopwatch.StartNew();

        var client = await McpClient.CreateAsync(
            new PipeClientTransport(hop),
            TestDefaults.ClientOptions(protocolVersion: null));

        stopwatch.Stop();

        await client.DisposeAsync();
        await hop.CompleteWritersAsync();
        await child.DisposeAsync();

        return (child.MethodsReceived, stopwatch.Elapsed);
    }
}

/// <summary>
/// A log record for the provider tests, written the way the product writes
/// them.
/// </summary>
internal static partial class ProbeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Probe record: {Marker}")]
    public static partial void LogProbe(this ILogger logger, string marker);
}

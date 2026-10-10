// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The relay engine, in this process, against a hand-written client and
/// hand-written backgrounds, on a clock the arms move.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is under test is the engine, whole.</b> Every arm drives it through its
/// two streams, as the client and the background would, and asserts on the frames
/// that come out: what the client is answered, what the background is sent, and in
/// what order. The handshake is the product's own <see cref="SdkHandshake"/>.
/// </para>
/// <para>
/// <b>And the census of <see cref="RelayErrors"/> runs the other way</b>, as
/// <c>ErrorCatalogueTests</c> holds <see cref="SessionErrors"/>: every row is matched by
/// an arm that provoked it through the engine, and
/// <see cref="EveryRelayErrorsRowWasProvokedByAnArmAbove"/> fails for a row nothing
/// provoked.
/// </para>
/// </remarks>
internal sealed partial class RelayTests
{
    /// <summary>The rows an arm has matched, gathered across the arms for the census.</summary>
    private static readonly HashSet<string> Provoked = new(StringComparer.Ordinal);

    private static readonly Lock CensusGate = new();

    /// <summary>
    /// <c>initialize</c>, <c>tools/list</c>, <c>ping</c> and the rest of what needs no
    /// background are answered with no background at all, and at once.
    /// </summary>
    /// <remarks>
    /// D6 a, decided 2026-10-08: the relay answers the handshake and the tool list
    /// from the binary, so a client's first turn has the tools whether or not a
    /// background exists. No clock moves in this arm.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHandshakeTheListAndPingAreAnsweredWithNoBackgroundAtAll()
    {
        await using var rig = RelayRig.Start();

        var initialize = await rig.InitializeAsync(KnownClients.ClaudeCode, "2.1.290");
        var result = initialize.Result!;

        await Assert.That(initialize.IdText).IsEqualTo("0");
        await Assert.That(Json.Text(result["serverInfo"], "name")).IsEqualTo("BrowserAI");
        await Assert.That(Json.Text(result["serverInfo"], "version")).IsEqualTo(BuildVersion.Current);
        await Assert.That(Json.Text(result, "instructions")).IsEqualTo(ServerInstructions.Text);
        await Assert.That(Json.Text(result, "protocolVersion")).IsEqualTo(TestDefaults.CallerProtocolVersion);
        await Assert.That(result["capabilities"]?["tools"]).IsNotNull();
        await Assert.That(result["capabilities"]!.AsObject().ContainsKey("logging")).IsFalse();

        // notifications/initialized is swallowed: the next frame is the list's answer.
        var list = await rig.ListAsync();

        await Assert.That(list.IdText).IsEqualTo("list-1");
        await Assert.That(Json.Same(list.Result, RelayRig.ToolList())).IsTrue();

        await rig.SendAsync("""{"jsonrpc":"2.0","id":"p1","method":"ping"}""");
        var pong = await rig.NextAsync();

        await Assert.That(pong.IdText).IsEqualTo("p1");
        await Assert.That(Json.Same(pong.Result, new JsonObject())).IsTrue();

        // Every other request is the SDK's to answer, and these two are methods
        // nothing serves.
        await rig.SendAsync("""{"jsonrpc":"2.0","id":"r1","method":"resources/list"}""");
        var resources = await rig.NextAsync();

        await Assert.That(resources.IdText).IsEqualTo("r1");
        await Assert.That((int?)resources.Error?["code"]).IsEqualTo(-32601);

        await rig.SendAsync("""{"jsonrpc":"2.0","id":"u1","method":"no/such-method"}""");
        var unknown = await rig.NextAsync();

        await Assert.That(unknown.IdText).IsEqualTo("u1");
        await Assert.That((int?)unknown.Error?["code"]).IsEqualTo(-32601);

        // No background was reached and none was asked about.
        await rig.SettledAsync();
        await Assert.That(rig.Finder.Explained.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A frame that is not a JSON-RPC message is answered with a parse error when its
    /// id can be found, and nothing is passed on.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFrameThatIsNotJsonRpcIsAnsweredWithAParseError()
    {
        await using var rig = RelayRig.Start();
        _ = await rig.InitializeAsync();

        // Cut off inside a string, so no JSON reader gets past it; the id comes first.
        await rig.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"broken");
        var answer = await rig.NextAsync();

        await Assert.That(answer.IdText).IsEqualTo("5");
        await Assert.That((int?)answer.Error?["code"]).IsEqualTo(-32700);

        Match(Json.Text(answer.Error, "message") ?? string.Empty, nameof(RelayErrors.UnreadableFrame), RelayErrors.UnreadableFrame());
    }

    /// <summary>
    /// <c>ping</c> is answered by the relay, never passed on, and never counts as
    /// activity; any other message moves the countdown and the background is told.
    /// </summary>
    /// <remarks>
    /// U1, decided 2026-10-08: everything the client sends counts except pings, because
    /// a client that pinged an idle server to keep it alive would otherwise hold every
    /// update for ever.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task PingIsAnsweredInPlaceNeverPassedOnAndIsNotActivity()
    {
        await using var rig = RelayRig.Start();
        var (background, hello) = await rig.ConnectedAsync();

        await Assert.That(Json.Text(hello.Params, "idleAt")).IsEqualTo(RelayRig.At(TimeSpan.FromMinutes(10)));

        _ = await rig.ListAsync();
        await rig.StepAsync(TimeSpan.FromMinutes(5));

        await rig.SendAsync("""{"jsonrpc":"2.0","id":"p1","method":"ping"}""");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("p1");

        // The background's next frame answers its own question, so neither the ping
        // nor a report of it went before; and the countdown has not moved.
        await background.SendAsync("""{"jsonrpc":"2.0","id":"r1","method":"browserai/ready-to-end"}""");
        var ready = await background.NextAsync();

        await Assert.That(ready.IdText).IsEqualTo("r1");
        await Assert.That(Json.Text(ready.Result, "idleAt")).IsEqualTo(RelayRig.At(TimeSpan.FromMinutes(10)));

        // A message that is not a ping moves it, and the background hears of it.
        _ = await rig.ListAsync();
        var report = await background.NextAsync();

        await Assert.That(report.Method).IsEqualTo("browserai/activity");
        await Assert.That(Json.Text(report.Params, "idleAt")).IsEqualTo(RelayRig.At(TimeSpan.FromMinutes(15)));
    }

    /// <summary>
    /// Every kind of message the client sends restarts the fixed countdown, and the
    /// countdown running out ends nothing.
    /// </summary>
    /// <remarks>
    /// U1 and H1 as the maintainer adjusted it on 2026-10-08: <i>"the relay is NOT
    /// terminated"</i> when its countdown runs out; it only stops holding updates.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryOtherClientMessageRestartsTheCountdownAndItsEndEndsNothing()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        // A notification the client sends of its own accord.
        await rig.StepAsync(TimeSpan.FromMinutes(1));
        const string Roots = """{"jsonrpc":"2.0","method":"notifications/roots/list_changed"}""";
        await rig.SendAsync(Roots);
        await ReportedAsync(background, TimeSpan.FromMinutes(11));
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(Roots);

        // A call.
        await rig.StepAsync(TimeSpan.FromMinutes(1));
        await rig.SendAsync(RelayRig.CallFrame("1"));
        await ReportedAsync(background, TimeSpan.FromMinutes(12));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");
        await background.SendAsync("""{"jsonrpc":"2.0","id":1,"result":{"content":[]}}""");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("1");

        // A cancellation.
        await rig.StepAsync(TimeSpan.FromMinutes(1));
        await rig.SendAsync(RelayRig.CancelFrame("99"));
        await ReportedAsync(background, TimeSpan.FromMinutes(13));
        await Assert.That((await background.NextAsync()).Method).IsEqualTo("notifications/cancelled");

        // An answer to something the background asked the client.
        await rig.StepAsync(TimeSpan.FromMinutes(1));
        await background.SendAsync("""{"jsonrpc":"2.0","id":"bg-1","method":"roots/list"}""");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("bg-1");
        const string Roots2 = """{"jsonrpc":"2.0","id":"bg-1","result":{"roots":[]}}""";
        await rig.SendAsync(Roots2);
        await ReportedAsync(background, TimeSpan.FromMinutes(14));
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(Roots2);

        // Past the countdown, the relay still answers, and the run goes on.
        await rig.StepAsync(TimeSpan.FromMinutes(11));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();
        await Assert.That((await rig.ListAsync()).IdText).IsEqualTo("list-2");
        await Assert.That(rig.Running.IsCompleted).IsFalse();
    }

    /// <summary>
    /// The first call of a connection that never asked for the tool list is refused
    /// once, after a list-changed notification; the next is passed on; a second
    /// handshake starts the question again.
    /// </summary>
    /// <remarks>
    /// Q261 b, moved from the server to the relay with the one-binary build, because
    /// the relay is what knows whether its client has listed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFirstCallOfAConnectionThatNeverListedIsRefusedOnce()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync(KnownClients.ClaudeCode);

        var expected = SessionErrors.ToolListPredatesThisServer(
            "browser_navigate",
            RelayRig.Facts.Build,
            KnownClients.ClaudeCode,
            ToolSignatures.From(RelayRig.ToolList()));

        await rig.SendAsync(RelayRig.CallFrame("1"));

        await Assert.That((await rig.NextAsync()).Method).IsEqualTo("notifications/tools/list_changed");

        var refused = await rig.NextAsync();

        await Assert.That(refused.IdText).IsEqualTo("1");
        await Assert.That(refused.IsToolError).IsTrue();
        await Assert.That(refused.ToolText).IsEqualTo(expected);

        // Once: the next call goes through, and it is the first thing the background
        // has been sent since its greeting.
        var second = RelayRig.CallFrame("2");
        await rig.SendAsync(second);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(second);

        // A second handshake on the same connection asks the question again.
        await rig.SendAsync(RelayRig.InitializeFrame(KnownClients.ClaudeCode, "2.1.290").Replace("\"id\":0", "\"id\":\"again\"", StringComparison.Ordinal));
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("again");

        await rig.SendAsync(RelayRig.CallFrame("3"));
        await Assert.That((await rig.NextAsync()).Method).IsEqualTo("notifications/tools/list_changed");
        await Assert.That((await rig.NextAsync()).ToolText).IsEqualTo(expected);

        var fourth = RelayRig.CallFrame("4");
        await rig.SendAsync(fourth);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(fourth);
    }

    /// <summary>
    /// A call and its answer pass byte for byte, and so does everything the background
    /// sends that is not the relay's own.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task CallsAnswersAndTheBackgroundsOwnTrafficPassByteForByte()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        // Spacing and escapes a re-serialiser would change.
        const string Call = """{ "jsonrpc" : "2.0", "id":7,"method":"tools/call","params":{"name":"browser_navigate","arguments":{"url":"https://example.invalid/café"}}}""";
        await rig.SendAsync(Call);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(Call);

        const string Progress = """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progressToken":7, "progress":1}}""";
        await background.SendAsync(Progress);
        await Assert.That((await rig.NextAsync()).Text).IsEqualTo(Progress);

        const string Answer = """{"jsonrpc":"2.0","id":7,"result":{"content":[{"type":"text","text":"Page URL: `x` it's café"}]} }""";
        await background.SendAsync(Answer);
        await Assert.That(SameBytes((await rig.NextAsync()).Bytes, Answer)).IsTrue();

        // A request the background makes of the client, and the client's answer.
        const string Ask = """{"jsonrpc":"2.0","id":"bg-1","method":"roots/list"}""";
        await background.SendAsync(Ask);
        await Assert.That((await rig.NextAsync()).Text).IsEqualTo(Ask);

        const string Reply = """{"jsonrpc":"2.0","id":"bg-1","result":{"roots":[]}}""";
        await rig.SendAsync(Reply);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(Reply);

        // A cancellation of a call in flight goes on as it was written.
        await rig.SendAsync(RelayRig.CallFrame("8"));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("8");

        var cancel = RelayRig.CancelFrame("8");
        await rig.SendAsync(cancel);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(cancel);

        // What is the relay's own stops at the relay: a notification and a request
        // under its prefix, and an answer to an id of its own. The marker after them
        // is the first thing the client sees.
        await background.SendAsync("""{"jsonrpc":"2.0","method":"browserai/something-new","params":{}}""");
        await background.SendAsync("""{"jsonrpc":"2.0","id":"q1","method":"browserai/what-is-this"}""");
        await background.SendAsync("""{"jsonrpc":"2.0","id":"browserai-relay-nothing","result":{}}""");

        const string Marker = """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progressToken":"marker","progress":2}}""";
        await background.SendAsync(Marker);
        await Assert.That((await rig.NextAsync()).Text).IsEqualTo(Marker);

        var unknown = await background.NextAsync();

        await Assert.That(unknown.IdText).IsEqualTo("q1");
        await Assert.That((int?)unknown.Error?["code"]).IsEqualTo(-32601);
    }

    /// <summary>The relay's numbers are the decided ones, each from its source.</summary>
    /// <remarks>
    /// D8 a, D10 and R (2026-10-08): the hold and the hang are half of Codex's 300 s per
    /// tool call; U1: the countdown is ten minutes, fixed; item 6 of the build brief:
    /// looks every 500 ms while holding and every 2 s otherwise.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRelaysNumbersAreTheDecidedOnes()
    {
        var codexToolCallLimit = TimeSpan.FromSeconds(300);

        await Assert.That(RelayConstants.HoldBound).IsEqualTo(codexToolCallLimit / 2);
        await Assert.That(RelayConstants.HangBound).IsEqualTo(codexToolCallLimit / 2);
        await Assert.That(RelayConstants.ProbeInterval).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(RelayConstants.LookWhileHolding).IsEqualTo(TimeSpan.FromMilliseconds(500));
        await Assert.That(RelayConstants.LookWhileIdle).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(RelayConstants.IdleCountdown).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(RelayConstants.ActivityReportGap).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// The handshake is the SDK's own server, built from the options every
    /// caller-facing server of the product is built from, and it refuses the two
    /// methods it must never see.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHandshakeIsTheSdksOwnAndNeverSeesTheToolMethods()
    {
        await using var handshake = SdkHandshake.Start();
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        var initialize = new WireFrame((await handshake.AnswerAsync(Utf8(RelayRig.InitializeFrame("BrowserAI.RelayTests", "1.0")), hang.Token))!);

        await Assert.That(Json.Text(initialize.Result, "protocolVersion")).IsEqualTo(TestDefaults.CallerProtocolVersion);
        await Assert.That(Json.Text(initialize.Result?["serverInfo"], "name")).IsEqualTo(BrowserProxy.CallerFacingOptions().ServerInfo!.Name);
        await Assert.That(initialize.Result!["capabilities"]!.AsObject().ContainsKey("logging")).IsFalse();

        await Assert.That(await handshake.AnswerAsync(Utf8("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""), hang.Token)).IsNull();

        // server/discover is answered, whatever the SDK and the options say to it.
        var discover = new WireFrame((await handshake.AnswerAsync(Utf8("""{"jsonrpc":"2.0","id":"d1","method":"server/discover","params":{}}"""), hang.Token))!);

        await Assert.That(discover.IdText).IsEqualTo("d1");
        await Assert.That(discover.Result is not null || discover.Error is not null).IsTrue();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handshake.AnswerAsync(Utf8("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""), hang.Token));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handshake.AnswerAsync(Utf8(RelayRig.CallFrame("3")), hang.Token));
    }

    /// <summary>The census: every row of <see cref="RelayErrors"/> was provoked by an arm above.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    [DependsOn(nameof(AFrameThatIsNotJsonRpcIsAnsweredWithAParseError))]
    [DependsOn(nameof(AHeldCallIsAnsweredAtItsDeadlineWithWhatTheFinderSaysAndNotATickBefore))]
    [DependsOn(nameof(ACrashIsAnsweredAtOnceAndSoIsEveryCallHeldBeforeIt))]
    [DependsOn(nameof(ARefusedRootIsAnsweredAtOnceWithWhatWasRefusedAndItsRemedy))]
    [DependsOn(nameof(ABuildThatIsNotInstalledIsAnsweredAtOnce))]
    [DependsOn(nameof(AnInstallingUpdateIsAnsweredAtOnceInEachClientsWords))]
    [DependsOn(nameof(ARefusedGreetingAnswersTheHeldCallsAndTheRelayLooksAgain))]
    [DependsOn(nameof(APipeThatClosesUnderACallAnswersItWithWhatTheFinderSays))]
    [DependsOn(nameof(AHungBackgroundIsReportedAfterTheHangBoundAndAnsweringAProbeClearsIt))]
    [DependsOn(nameof(TheCommitAnswersWhatTheRelayHeldWithTheUpdateSentenceAndEndsIt))]
    [DependsOn(nameof(ACommitWithNowCutsOffACallInFlight))]
    public async Task EveryRelayErrorsRowWasProvokedByAnArmAbove()
    {
        var rows = typeof(RelayErrors)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        List<string> unprovoked;

        lock (CensusGate)
        {
            unprovoked = [.. rows.Where(row => !Provoked.Contains(row)).Order(StringComparer.Ordinal)];
        }

        await Assert.That(string.Join(Environment.NewLine, unprovoked)).IsEmpty();

        // And the count, so a row deleted instead of provoked does not pass by
        // shrinking the question. Eleven since 2026-10-10 (previously ten), when a
        // refused root got its own row, RootRefused; twelve the same day, when a
        // background that never opened its pipe got NoPipe (the texts review's #115).
        await Assert.That(rows.Count).IsEqualTo(12);
    }

    /// <summary>Holds that a row came out as the catalogue writes it, and counts it for the census.</summary>
    /// <param name="observed">What the engine answered.</param>
    /// <param name="row">The row's name.</param>
    /// <param name="expected">The row, as the catalogue writes it for this case.</param>
    private static void Match(string observed, string row, string expected)
    {
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The condition provoked for {row} did not produce that row.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Observed: {observed}");
        }

        lock (CensusGate)
        {
            _ = Provoked.Add(row);
        }
    }

    /// <summary>Reads the background's next frame, which must be an activity report carrying this countdown.</summary>
    /// <param name="background">The background.</param>
    /// <param name="idleAt">The countdown, as time since the clock's start.</param>
    /// <returns>The assertion task.</returns>
    private static async Task ReportedAsync(FakeBackground background, TimeSpan idleAt)
    {
        var report = await background.NextAsync();

        await Assert.That(report.Method).IsEqualTo("browserai/activity");
        await Assert.That(Json.Text(report.Params, "idleAt")).IsEqualTo(RelayRig.At(idleAt));
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static bool SameBytes(byte[] observed, string sent) => observed.AsSpan().SequenceEqual(Utf8(sent));

    private static ClientReading Unclassifiable(string? clientName) =>
        throw new InvalidOperationException($"The suite's classifier refuses '{clientName}'.");
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Background;
using BrowserAI.Clients;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Relay;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The background's pipe, in this process: what a relay's greeting is answered with,
/// what reaches the session host behind it, what a person's start and a stop are
/// answered with, and what the roster the update core reads sees of each relay.
/// </summary>
/// <remarks>
/// <para>
/// <b>S a, the maintainer's words of 2026-10-08, verbatim: <i>"s a"</i></b>: one
/// resident background holds every session, the tab and the update, and every relay,
/// every person's start and the uninstall hook reach it over one pipe whose first
/// message says what the connection is. The design's "Settings travel as arguments and
/// over the pipe" names what a greeting carries and the three refusals a background
/// answers one with, and H1 and RESOLUTIONS 13 name what the roster tells the update
/// core.
/// </para>
/// <para>
/// <b>Every arm runs the product's own server</b> over a session host on the rig's
/// session doubles (<see cref="BackgroundServerRig"/>), on a pipe named for the arm,
/// and talks to it with the suite's own framing. Nothing is started: no published
/// binary, no browser and no task.
/// </para>
/// </remarks>
internal sealed class BackgroundServerTests
{
    /// <summary>A version an update would install, for the agreement's messages.</summary>
    private const string NextVersion = "9.9.10-background-tests";

    /// <summary>
    /// A relay's greeting is answered with the background's build and pid, the roster
    /// holds the relay as its greeting introduced it, and the relay's traffic then
    /// reaches the session host: the handshake, the list and a call that opens a session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The relay's pid is the one Windows reads off the pipe</b>, never the number its
    /// greeting carries, because the greeting is the relay's word and the pipe is the
    /// kernel's: the greeting below says 4242 and the roster must not believe it.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a background that answered the greeting
    /// with another pid than its own.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AGreetingIsAnsweredWithTheBuildAndPidAndTheRelaysTrafficThenReachesTheSessionHost()
    {
        await using var rig = BackgroundServerRig.Start();

        var idleAt = rig.Clock.GetUtcNow() + RelayConstants.IdleCountdown;
        using var relay = rig.Connect();

        await relay.SendAsync(rig.Hello(idleAt: idleAt));
        var hello = await relay.NextAsync();

        await Assert.That(hello.IdText).IsEqualTo(RelayWire.IdPrefix + "hello");
        await Assert.That(hello.Error).IsNull().Because(hello.Text);
        await Assert.That(Json.Text(hello.Result, "build")).IsEqualTo(BackgroundServerRig.Build);
        await Assert.That((int?)hello.Result?["pid"]).IsEqualTo(Environment.ProcessId);

        // The relay replays its client's handshake, and the MCP server over the
        // session host answers it, and the list.
        var initialize = await relay.InitializeAsync();

        await Assert.That(initialize.Error).IsNull().Because(initialize.Text);
        await Assert.That(Json.Text(initialize.Result?["serverInfo"], "name")).IsEqualTo("BrowserAI");
        await Assert.That(Json.Text(initialize.Result, "protocolVersion")).IsEqualTo(TestDefaults.CallerProtocolVersion);

        // The roster holds it as its greeting introduced it. It takes the relay after
        // the greeting's answer is written and before the MCP server behind the link
        // exists, so the handshake's answer is what says it has.
        var (greeting, heldIdleAt, callInFlight) = rig.Roster.Read().Single();

        await Assert.That(greeting.RelayPid).IsEqualTo(Environment.ProcessId).Because("the relay's pid is read off the pipe, and the greeting's own number is not believed");
        await Assert.That(greeting.ClientPid).IsEqualTo(BackgroundPipeClient.ClientPid);
        await Assert.That(greeting.ClientName).IsEqualTo(BackgroundPipeClient.ClientName);
        await Assert.That(greeting.ClientVersion).IsEqualTo(BackgroundPipeClient.ClientVersion);
        await Assert.That(greeting.Folder).IsEqualTo(BackgroundPipeClient.Folder);
        await Assert.That(greeting.Reconnect).IsEqualTo(RelayReconnect.McpReconnect);
        await Assert.That(heldIdleAt).IsEqualTo(idleAt);
        await Assert.That(callInFlight).IsFalse();

        var state = rig.OnlyRelay();

        await Assert.That(state.Id).IsEqualTo(greeting.Id);
        await Assert.That(state.IdleAt).IsEqualTo(idleAt);
        await Assert.That(state.Reconnect).IsEqualTo(RelayReconnect.McpReconnect);

        var list = await relay.ListAsync();
        var names = (list.Result?["tools"] as JsonArray ?? []).Select(tool => Json.Text(tool, "name")).ToList();

        await Assert.That(names).Contains(SessionToolSurface.Init).Because(list.Text);
        await Assert.That(names).Contains("browser_navigate");

        // A call reaches the host's sessions: the session it opens is the host's.
        var directory = Path.Combine(rig.Sessions.Root, "opened-through-the-pipe");
        var opened = await relay.RequestAsync("tools/call", Init(directory));

        await Assert.That(opened.IsToolError).IsFalse().Because(opened.ToolText);
        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull();

        // The relay goes, and the roster lets it go.
        relay.Dispose();

        await BackgroundServerRig.WaitUntilAsync(() => rig.Roster.Count is 0, "the roster still holds a relay whose connection closed");
    }

    /// <summary>
    /// Each refusal of a greeting carries its own sentence and its kind in
    /// <c>data.refusal</c>, an update or a stop outranks a wrong build, the connection is
    /// closed after it, and no refused relay is ever admitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "Settings travel as arguments and over the pipe"</b>: the
    /// background answers with its own build and pid, or refuses, with a sentence for
    /// each: a different build, a different data root, or an update installing; and the
    /// background's "stopping" beside them. The relay hands the sentence to the model,
    /// and reads the kind to know an update from the rest.
    /// </para>
    /// <para>
    /// <b>The positive control is a root spelled another way</b>, upper case and with a
    /// trailing separator, which is the same root by its key and is served.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a background that judged the build before
    /// its own state, which answered a relay of an older build with the build's sentence
    /// while an update was installing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachRefusalCarriesItsSentenceAndItsKindAndNoRefusedRelayIsAdmitted()
    {
        await using var rig = BackgroundServerRig.Start();
        using var elsewhere = ScratchDirectory.Create("background-other-data-root");

        const string Older = "9.9.8-an-older-build";

        var build = await RefusedAsync(rig, rig.Hello(build: Older));

        await Assert.That(Kind(build)).IsEqualTo(RelayProtocol.RefusedForTheBuild);
        await Assert.That(Sentence(build)).Contains(BackgroundServerRig.Build);
        await Assert.That(Sentence(build)).Contains(Older);
        await Assert.That(Sentence(build)).Contains("Start Menu");

        var root = await RefusedAsync(rig, rig.Hello(dataRoot: elsewhere.Path));

        await Assert.That(Kind(root)).IsEqualTo(RelayProtocol.RefusedForTheDataRoot);
        await Assert.That(Sentence(root)).Contains(rig.Identity.DataRoot);
        await Assert.That(Sentence(root)).Contains(elsewhere.Path);

        var none = await RefusedAsync(rig, BackgroundPipeClient.Hello(BackgroundServerRig.Build, dataRoot: null, rig.Clock.GetUtcNow()));

        await Assert.That(Kind(none)).IsEqualTo(RelayProtocol.RefusedForTheDataRoot);

        // The positive control: the same root in another spelling is served.
        using (var sameRoot = rig.Connect())
        {
            await sameRoot.SendAsync(rig.Hello(dataRoot: rig.Identity.DataRoot.ToUpperInvariant() + Path.DirectorySeparatorChar));

            var served = await sameRoot.NextAsync();

            await Assert.That(served.Result).IsNotNull().Because(served.Text);

            // Its handshake's answer says the roster took it, so the wait below is for
            // its leaving and not for its arrival.
            _ = await sameRoot.InitializeAsync();
        }

        await BackgroundServerRig.WaitUntilAsync(() => rig.Roster.Count is 0, "the roster still holds the relay that was served and has gone");

        // An update outranks the build, so a relay of another build is told the update.
        rig.Verbs.State = BackgroundState.Updating;

        var updating = await RefusedAsync(rig, rig.Hello(build: Older));

        await Assert.That(Kind(updating)).IsEqualTo(RelayProtocol.RefusedForAnUpdate);
        await Assert.That(Sentence(updating)).Contains("installing an update");

        rig.Verbs.State = BackgroundState.Stopping;

        var stopping = await RefusedAsync(rig, rig.Hello(build: Older));

        await Assert.That(Kind(stopping)).IsEqualTo(RelayProtocol.RefusedWhileStopping);
        await Assert.That(Sentence(stopping)).Contains("stopping");

        // Four kinds, four sentences, and every one of them an invalid request.
        string?[] sentences = [Sentence(build), Sentence(root), Sentence(updating), Sentence(stopping)];

        await Assert.That(sentences.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(4);

        foreach (var refusal in new[] { build, root, none, updating, stopping })
        {
            await Assert.That((int?)refusal.Error?["code"]).IsEqualTo(-32600).Because(refusal.Text);
        }
    }

    /// <summary>
    /// A person's <c>show</c> is answered with the address the background's page hands
    /// out, a refusal with the page's own sentence, a <c>stop</c> calls the background's
    /// stop, and anything else first is refused; each is answered once and closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D13 a and the uninstall hook</b>: a person's start hands over <c>show</c> and
    /// opens the address it gets back; the uninstall hook asks the background to stop
    /// through its pipe. A first message that is a notification asks nothing and is
    /// closed with no answer, since there is no id to answer.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a background that answered a stop and never
    /// stopped.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShowIsAnsweredWithThePagesAddressAndAStopCallsTheBackgroundsStop()
    {
        await using var rig = BackgroundServerRig.Start();

        var sessions = await AskAsync(rig, BackgroundPipe.Show, new JsonObject { ["page"] = "sessions" });

        await Assert.That(Json.Text(sessions.Result, "address")).IsEqualTo(FakeBackgroundVerbs.DefaultAddress).Because(sessions.Text);
        await Assert.That((int?)sessions.Result?["pid"]).IsEqualTo(Environment.ProcessId);

        var status = await AskAsync(rig, BackgroundPipe.Show, parameters: null);

        await Assert.That(Json.Text(status.Result, "address")).IsEqualTo(FakeBackgroundVerbs.DefaultAddress).Because(status.Text);
        await Assert.That(string.Join(" | ", rig.Verbs.Pages.Select(page => page ?? "(status)"))).IsEqualTo("sessions | (status)");

        // A page that cannot be handed out says why, in the page's own words.
        rig.Verbs.Address = null;

        var refused = await AskAsync(rig, BackgroundPipe.Show, parameters: null);

        await Assert.That((int?)refused.Error?["code"]).IsEqualTo(-32603).Because(refused.Text);
        await Assert.That(Sentence(refused)).IsEqualTo(rig.Verbs.Refusal);
        await Assert.That(Kind(refused)).IsEqualTo("show");

        // A stop is answered, and the background's stop has run by the time the
        // connection closes.
        var stop = await AskAsync(rig, BackgroundPipe.Stop, parameters: null);

        await Assert.That((bool?)stop.Result?["stopping"]).IsTrue().Because(stop.Text);
        await Assert.That((int?)stop.Result?["pid"]).IsEqualTo(Environment.ProcessId);
        await Assert.That(rig.Verbs.Stops).IsEqualTo(1);

        // Anything else first is refused, by name.
        var unknown = await AskAsync(rig, BackgroundPipe.MethodPrefix + "what-is-this", parameters: null);

        await Assert.That((int?)unknown.Error?["code"]).IsEqualTo(-32601).Because(unknown.Text);
        await Assert.That(Kind(unknown)).IsEqualTo("method");

        // A notification first asks nothing: closed, with no answer and no verb called.
        using (var notifier = rig.Connect())
        {
            await notifier.SendAsync($$"""{"jsonrpc":"2.0","method":"{{BackgroundPipe.Show}}"}""");

            await Assert.That(await notifier.ClosedAsync()).IsTrue();
        }

        await Assert.That(rig.Verbs.Pages.Count).IsEqualTo(3);
        await Assert.That(rig.Verbs.Stops).IsEqualTo(1);
    }

    /// <summary>
    /// A connection that never sends its first message, and one that sends half of it,
    /// hold up no other: a person's start and a relay are both served while they wait,
    /// the half-sent one is served once it is finished, and the silent one is closed at
    /// the first message's bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The listener hands every connection to a task of its own</b> and makes the
    /// next instance first, so nothing one connection does or fails to do reaches
    /// another. A listener that read each first message itself would serve the second
    /// start only after the first had given up.
    /// </para>
    /// <para>
    /// <b>The silent one is closed at <see cref="BackgroundServer.FirstFrameBound"/></b>,
    /// a hang detector of the product's own, and the wait for that close is the suite's
    /// <see cref="TestDefaults.InProcessHang"/>: nothing asserts how soon it came. The
    /// other answers have to arrive within that bound of the silent connection's accept,
    /// which they do by several orders of magnitude.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a listener that served each connection on
    /// the listener's own thread before accepting the next: the half-sent first message
    /// could no longer be finished, because the background had given up on it while the
    /// start behind it waited.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AConnectionThatNeverSendsItsFirstMessageHoldsUpNoOther()
    {
        await using var rig = BackgroundServerRig.Start();

        using var silent = rig.Connect();
        using var halfway = rig.Connect();

        await halfway.SendPartAsync("""{"jsonrpc":"2.0","id":"browserai-start-half","method":""");

        // A start behind both is answered.
        var shown = await AskAsync(rig, BackgroundPipe.Show, parameters: null);

        await Assert.That(Json.Text(shown.Result, "address")).IsEqualTo(FakeBackgroundVerbs.DefaultAddress).Because(shown.Text);

        // And so is a relay.
        using (var relay = rig.Connect())
        {
            await relay.SendAsync(rig.Hello());

            var greeted = await relay.NextAsync();

            await Assert.That(greeted.Result).IsNotNull().Because(greeted.Text);
        }

        // The half-sent first message, finished now, is served as any other.
        await halfway.SendAsync("\"" + BackgroundPipe.Show + "\"}");

        var late = await halfway.NextAsync();

        await Assert.That(late.IdText).IsEqualTo("browserai-start-half");
        await Assert.That(Json.Text(late.Result, "address")).IsEqualTo(FakeBackgroundVerbs.DefaultAddress).Because(late.Text);

        // The one that never wrote anything is closed by the background.
        await Assert.That(await silent.ClosedAsync()).IsTrue();
        await Assert.That(rig.Verbs.Pages.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A second background on the same pipe name fails as it is made, with the access
    /// denied that <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> answers, and the first serves on;
    /// once the first has gone, the name can be taken again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one-background rule, beside the task's own <c>IgnoreNew</c> (S a)</b>: the
    /// name is taken in the constructor, so a second background fails before it has
    /// served anybody, and <c>Program.RunTheBackground</c> reads exactly this
    /// <c>HResult</c> to exit quietly.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a pipe created without the first-instance
    /// flag, where the second constructor took the name beside the first.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASecondBackgroundOnTheSameNameFailsWithAccessDeniedAndTheFirstServesOn()
    {
        await using var rig = BackgroundServerRig.Start();

        var second = TryToServe(rig, new FakeBackgroundVerbs(), out var refusal);

        if (second is not null)
        {
            await second.DisposeAsync();
        }

        await Assert.That(refusal).IsNotNull().Because("a second background took a name the first still serves");
        await Assert.That(refusal!.HResult).IsEqualTo(NamedPipes.HResultFromWin32(NamedPipes.ErrorAccessDenied));

        var first = await AskAsync(rig, BackgroundPipe.Show, parameters: null);

        await Assert.That(Json.Text(first.Result, "address")).IsEqualTo(FakeBackgroundVerbs.DefaultAddress).Because(first.Text);

        // The positive control: with the first gone, the name is free again.
        await rig.Server.DisposeAsync();

        var successor = new FakeBackgroundVerbs { Address = "http://127.0.0.1:47111/the-successor" };
        var third = TryToServe(rig, successor, out var thirdRefusal);

        await Assert.That(thirdRefusal).IsNull();

        await using (third!)
        {
            third!.Start();

            var answered = await AskAsync(rig, BackgroundPipe.Show, parameters: null);

            await Assert.That(Json.Text(answered.Result, "address")).IsEqualTo(successor.Address).Because(answered.Text);
        }
    }

    /// <summary>
    /// The roster hears a relay's activity and its withdrawn yes, each told to the update
    /// core at once and the second by the relay's name; what else of BrowserAI's own the
    /// relay sends goes no further than the link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>H1 and U1</b>: a relay's countdown holds an update, and the relay tells the
    /// background each time it moves; <b>RESOLUTIONS 13</b>: a relay that said yes and
    /// then heard from its client withdraws it, and the update core must know which.
    /// </para>
    /// <para>
    /// <b>No arm waits on the clock</b>: a <c>tools/list</c> sent after a notification is
    /// answered by the MCP server only after the link has handled the notification,
    /// because the link reads the relay's frames in order and hands the roster each
    /// notice before it reads the next.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a roster that recorded a withdrawn yes as
    /// activity and never told the update core which relay withdrew.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRosterHearsARelaysActivityAndAWithdrawnYesAndNothingElseOfItsOwnGoesFurther()
    {
        await using var rig = BackgroundServerRig.Start();

        var greeted = rig.Clock.GetUtcNow() + RelayConstants.IdleCountdown;
        using var relay = await rig.ConnectARelayAsync(greeted);
        var id = rig.OnlyRelay().Id;
        var told = rig.Changes;

        // Activity moves the countdown, and the update core is told.
        var moved = greeted + RelayConstants.IdleCountdown;

        await relay.SendAsync(Notification(RelayProtocol.Activity, moved));
        _ = await relay.ListAsync();

        await Assert.That(rig.OnlyRelay().IdleAt).IsEqualTo(moved);
        await Assert.That(rig.Changes).IsGreaterThan(told);
        await Assert.That(rig.Withdrawn).IsEmpty();

        // A withdrawn yes is told by the relay's name, and moves the countdown too.
        var withdrawn = moved + RelayConstants.IdleCountdown;

        await relay.SendAsync(Notification(RelayProtocol.Withdraw, withdrawn));
        _ = await relay.ListAsync();

        await Assert.That(string.Join(" | ", rig.Withdrawn)).IsEqualTo(id);
        await Assert.That(rig.OnlyRelay().IdleAt).IsEqualTo(withdrawn);

        // A notification of BrowserAI's own that the background does not read is
        // dropped, and a request is refused by the link itself.
        await relay.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = BackgroundPipe.MethodPrefix + "something-new", ["params"] = new JsonObject() }.ToJsonString());

        var refused = await relay.RequestAsync(BackgroundPipe.MethodPrefix + "what-is-this", parameters: null);

        await Assert.That((int?)refused.Error?["code"]).IsEqualTo(-32601).Because(refused.Text);
        await Assert.That(Sentence(refused)).Contains("does not serve");

        // And the MCP server behind the link goes on answering.
        var list = await relay.ListAsync();

        await Assert.That(list.Result).IsNotNull().Because(list.Text);
        await Assert.That(rig.Withdrawn.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The update core's question, <i>ready to end?</i>, travels on the relay's own
    /// connection under an id of the background's, and the relay's answer is read as it
    /// gave it: a yes, a no with a call in flight, and an error, which is a no.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RESOLUTIONS 13</b>: the background asks every relay before it installs, and the
    /// answer is the relay's, read here and never judged.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a roster that read the relay's
    /// <c>ready</c> the wrong way round.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReadyToEndQuestionTravelsOnTheRelaysConnectionAndItsAnswerIsReadAsGiven()
    {
        await using var rig = BackgroundServerRig.Start();

        var greeted = rig.Clock.GetUtcNow() + RelayConstants.IdleCountdown;
        using var relay = await rig.ConnectARelayAsync(greeted);
        var id = rig.OnlyRelay().Id;

        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        // A yes.
        var asking = rig.Roster.AskReadyToEndAsync(id, NextVersion, hang.Token);
        var question = await relay.NextAsync();

        await Assert.That(question.Method).IsEqualTo(RelayProtocol.ReadyToEnd);
        await Assert.That(question.IdText).StartsWith(RelayLink.OwnIdPrefix);
        await Assert.That(Json.Text(question.Params, "version")).IsEqualTo(NextVersion);

        var ranOut = greeted - RelayConstants.IdleCountdown;

        await relay.SendAsync(Frames.Result(question.IdText!, new JsonObject { ["ready"] = true, ["idleAt"] = RelayWire.Instant(ranOut) }));

        await Assert.That(await asking).IsEqualTo(new RelayReadiness(Ready: true, ranOut, CallInFlight: false));

        // A no, with a call in flight.
        asking = rig.Roster.AskReadyToEndAsync(id, NextVersion, hang.Token);
        question = await relay.NextAsync();

        await relay.SendAsync(Frames.Result(question.IdText!, new JsonObject { ["ready"] = false, ["idleAt"] = RelayWire.Instant(greeted), ["callInFlight"] = true }));

        await Assert.That(await asking).IsEqualTo(new RelayReadiness(Ready: false, greeted, CallInFlight: true));

        // An error is a no, with the countdown the roster already holds.
        asking = rig.Roster.AskReadyToEndAsync(id, NextVersion, hang.Token);
        question = await relay.NextAsync();

        await relay.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = question.IdText,
            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "This relay does not serve it." },
        }.ToJsonString());

        await Assert.That(await asking).IsEqualTo(new RelayReadiness(Ready: false, greeted, CallInFlight: false));

        // A relay the roster does not hold is a no at once, and nothing is sent.
        var unknown = await rig.Roster.AskReadyToEndAsync("no-such-relay", NextVersion, hang.Token);

        await Assert.That(unknown).IsEqualTo(new RelayReadiness(Ready: false, rig.Clock.GetUtcNow(), CallInFlight: false));

        // And the answers went to the roster and not to the MCP server: the next frame
        // is the answer to the list asked now.
        var list = await relay.ListAsync();

        await Assert.That(list.Result).IsNotNull().Because(list.Text);
    }

    /// <summary>
    /// The update core's call-off and its end reach the relay as notifications carrying
    /// the version, and the end is done only once the relay has closed its connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RESOLUTIONS 13 and H1</b>: called off, every relay goes back to normal; ended,
    /// each answers what it holds and closes its end, and the background waits for that,
    /// within the update core's own bound.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a roster whose end was done the moment the
    /// relays had been told.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallOffAndAnEndReachTheRelayAndTheEndWaitsForItsConnectionToClose()
    {
        await using var rig = BackgroundServerRig.Start();

        using var relay = await rig.ConnectARelayAsync();

        rig.Roster.CallOff(NextVersion);

        var calledOff = await relay.NextAsync();

        await Assert.That(calledOff.Method).IsEqualTo(RelayProtocol.CalledOff);
        await Assert.That(calledOff.IdText).IsNull();
        await Assert.That(Json.Text(calledOff.Params, "version")).IsEqualTo(NextVersion);

        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        var ending = rig.Roster.EndAllAsync(NextVersion, now: true, hang.Token);

        var end = await relay.NextAsync();

        await Assert.That(end.Method).IsEqualTo(RelayProtocol.End);
        await Assert.That(end.IdText).IsNull();
        await Assert.That((bool?)end.Params?["now"]).IsTrue();
        await Assert.That(Json.Text(end.Params, "version")).IsEqualTo(NextVersion);

        // The relay is still connected and served, and the end is not done.
        var list = await relay.ListAsync();

        await Assert.That(list.Result).IsNotNull().Because(list.Text);
        await Assert.That(ending.IsCompleted).IsFalse().Because("the end was done while the relay was still connected");

        // The relay closes its end, and the end is done.
        relay.Dispose();

        await ending.WaitAsync(TestDefaults.InProcessHang);
        await BackgroundServerRig.WaitUntilAsync(() => rig.Roster.Count is 0, "the roster still holds a relay the end let go");
    }

    /// <summary>
    /// What a relay the update tells to end drove records the update as its reason when
    /// its connection closes, and a relay that goes on its own still lets its session go
    /// as its client's going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-10 for the texts review's #24</b>, the reader's refinement: an
    /// update ends every relay before it closes the sessions, and a session with no
    /// browser up is let go the moment its relay's connection closes, which recorded
    /// <i>because the client driving it went away</i> about a client that had not gone.
    /// The positive control is a relay that does go on its own, whose session still says
    /// so.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against the background's relay connection without
    /// its <c>EndsFor</c>: the session the update's end let go recorded
    /// <see cref="SessionCloseCause.Released"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelayTheUpdateEndsLeavesWhatItDroveRecordingTheUpdate()
    {
        await using var rig = BackgroundServerRig.Start();

        // ---- The control: a relay that goes on its own.
        var gone = Path.Combine(rig.Sessions.Root, "let-go-when-its-relay-went");

        using (var leaving = await rig.ConnectARelayAsync())
        {
            var opened = await leaving.RequestAsync("tools/call", Init(gone));

            await Assert.That(opened.IsToolError).IsFalse().Because(opened.ToolText);
        }

        await BackgroundServerRig.WaitUntilAsync(() => rig.Host.Sessions.Find(gone) is null, "the session of a relay that went was never let go");

        await Assert.That(SessionLock.ReadRecord(SessionPath.For(gone))!.LastClose!.Value.Cause).IsEqualTo(SessionCloseCause.Released);

        // ---- The relay the update ends.
        var ended = Path.Combine(rig.Sessions.Root, "let-go-when-the-update-ended-its-relay");

        using var relay = await rig.ConnectARelayAsync();

        var init = await relay.RequestAsync("tools/call", Init(ended));

        await Assert.That(init.IsToolError).IsFalse().Because(init.ToolText);

        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        var ending = rig.Roster.EndAllAsync(NextVersion, now: true, hang.Token);

        var end = await relay.NextAsync();

        await Assert.That(end.Method).IsEqualTo(RelayProtocol.End);

        // The relay answers what it holds and closes its end, as the end asks.
        relay.Dispose();

        await ending.WaitAsync(TestDefaults.InProcessHang);
        await BackgroundServerRig.WaitUntilAsync(() => rig.Host.Sessions.Find(ended) is null, "the session of the relay the update ended was never let go");

        var close = SessionLock.ReadRecord(SessionPath.For(ended))!.LastClose!;

        await Assert.That(close.Value.Cause).IsEqualTo(SessionCloseCause.Updating);
        await Assert.That(CloseReasons.Of(close)).EndsWith("to install an update.");
    }

    /// <summary>
    /// A relay whose connection fails in a way other than a broken pipe keeps no relay
    /// after it from being told to end, and a call-off that cannot reach it is recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found by lane ARCH's helper T1 reading the code on 2026-10-09</b>, and closed
    /// 2026-10-10 with the maintainer's 9 a: the end caught only an
    /// <see cref="IOException"/> for each relay, so a link already disposed stopped the
    /// loop, and the call-off discarded what it started, so a failure was never seen.
    /// The failing relay here is a double of the roster's own seam,
    /// <see cref="IRelayLink"/>, which throws what a link whose lock was disposed under
    /// it throws; the relay after it is a real one, over the background's pipe.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against the roster as it was: the end faulted with
    /// the double's exception before it reached the real relay, and the call-off wrote
    /// no record.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelayWhoseLinkFailsKeepsNoOtherFromBeingToldToEnd()
    {
        await using var rig = BackgroundServerRig.Start();

        // First in the roster's order, ahead of the relay the background accepts next.
        var failing = new FailingRelayLink(new ObjectDisposedException("the failing relay's connection"));
        var greeting = new RelayGreeting("4343-1", 4343, null, BackgroundPipeClient.ClientName, BackgroundPipeClient.ClientVersion, BackgroundPipeClient.Folder, RelayReconnect.McpReconnect);

        rig.Roster.Add(greeting, failing, rig.Clock.GetUtcNow());

        using var relay = await rig.ConnectARelayAsync();

        // The call-off: the real relay hears it, and the one that could not is recorded.
        rig.Roster.CallOff(NextVersion);

        var calledOff = await relay.NextAsync();

        await Assert.That(calledOff.Method).IsEqualTo(RelayProtocol.CalledOff);
        await Assert.That(failing.Told).IsEqualTo(1);
        await Assert.That(rig.Logs.Records.Count(record => record.EventId.Id is 31 && record.Message.Contains(greeting.Id, StringComparison.Ordinal)))
            .IsEqualTo(1).Because("a call-off that could not reach a relay was never seen");

        // The end: the failing relay costs itself alone, and the real one is told.
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        var ending = rig.Roster.EndAllAsync(NextVersion, now: false, hang.Token);

        await Assert.That(ending.IsFaulted).IsFalse().Because(ending.Exception?.ToString() ?? "faulted");

        var end = await relay.NextAsync();

        await Assert.That(end.Method).IsEqualTo(RelayProtocol.End);
        await Assert.That(failing.Told).IsEqualTo(2);
        await Assert.That(rig.Logs.Records.Count(record => record.EventId.Id is 31)).IsEqualTo(2);

        relay.Dispose();

        await ending.WaitAsync(TestDefaults.InProcessHang);
    }

    /// <summary>
    /// A connection the background accepts while it stops is waited for before the stop
    /// is done, and is closed by then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found by lane ARCH's helper T1 reading the code on 2026-10-09</b>, and closed
    /// 2026-10-10 with the maintainer's 9 a: the stop took its list of connections and
    /// only then joined the listener, so a connection the listener had accepted in
    /// between was not on the list and ran on past the stop's own events.
    /// </para>
    /// <para>
    /// <b>The listener is held by the product's seam</b>,
    /// <see cref="BackgroundServer.Accepted"/>, between taking the connection and serving
    /// it, until the stop is waiting for the listener: the one moment that ordering is
    /// about. The stop runs on a thread of its own, because it joins the listener
    /// thread and blocks there; the stop's own record says how many connections it
    /// waits for, which is what the order decides.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a stop that took its list before it joined
    /// the listener, which waited for none.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AConnectionAcceptedWhileTheBackgroundStopsIsWaitedFor()
    {
        using var accepted = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim(initialState: false);

        await using var rig = BackgroundServerRig.Start(accepted: () =>
        {
            _ = accepted.Release();
            _ = release.Wait(TestDefaults.InProcessHang);
        });

        using var client = rig.Connect();

        await Assert.That(await accepted.WaitAsync(TestDefaults.InProcessHang)).IsTrue();

        var stopping = new Thread(() => rig.Server.DisposeAsync().AsTask().GetAwaiter().GetResult())
        {
            IsBackground = true,
            Name = "BrowserAI suite stop",
        };

        try
        {
            stopping.Start();

            // The stop has stopped the listener's loop and is waiting for the thread
            // the seam is holding.
            await BackgroundServerRig.WaitUntilAsync(
                () => (stopping.ThreadState & ThreadState.WaitSleepJoin) is not 0,
                "the stop never came to wait for its listener");
        }
        finally
        {
            release.Set();
        }

        await Assert.That(stopping.Join(TestDefaults.InProcessHang)).IsTrue().Because("the stop never finished");

        var waited = rig.Logs.Records.Where(record => record.EventId.Id is 9).Select(record => record.Message).ToList();

        await Assert.That(string.Join(" | ", waited))
            .IsEqualTo("The background's pipe is closed; it waits for the 1 connection(s) on its list to end before it stops.");
        await Assert.That(await client.ClosedAsync()).IsTrue();
    }

    /// <summary>
    /// A <c>tools/call</c> is in flight from the moment the link reads it until its answer
    /// goes out, or until the client cancels it, whichever comes first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>H1</b>: a relay with a call in flight holds an update, so the update core must
    /// read it while the call runs and not after. The calls here are held by the
    /// session's own double until the arm lets them go, so the count is read while they
    /// are genuinely running; MCP has a cancelled request go unanswered, which is why a
    /// cancellation ends the count by itself.
    /// </para>
    /// <para>
    /// <b>Every read is behind a barrier</b>: a <c>tools/list</c> sent after a frame is
    /// answered only after the link has counted that frame.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a link that never uncounted a call when its
    /// answer went out.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallIsInFlightUntilItsAnswerGoesOutOrTheClientCancelsIt()
    {
        var navigate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { HoldUntil = navigate.Task };
                child.Tools["browser_snapshot"] = new FakeToolBehaviour { HoldUntil = snapshot.Task };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false);

        await using var rig = BackgroundServerRig.Start(sessions: sessions);

        // The holds are let go before the background and its sessions are disposed,
        // which is the order the declarations above give.
        try
        {
            using var relay = await rig.ConnectARelayAsync();

            var directory = Path.Combine(sessions.Root, "held-calls");
            var opened = await relay.RequestAsync("tools/call", Init(directory));

            await Assert.That(opened.IsToolError).IsFalse().Because(opened.ToolText);
            await Assert.That(rig.OnlyRelay().CallInFlight).IsFalse().Because("the call that opened the session was answered");

            // A call the session's child holds is in flight while it is held...
            await relay.SendAsync(Call("held-1", directory, "browser_navigate"));
            _ = await relay.ListAsync();

            await Assert.That(rig.OnlyRelay().CallInFlight).IsTrue();
            await Assert.That(rig.Roster.Read().Single().CallInFlight).IsTrue();

            // ...and not once its answer has gone out.
            navigate.SetResult();

            var (answer, _) = await relay.AnswerToAsync("held-1");

            await Assert.That(answer.IsToolError).IsFalse().Because(answer.ToolText);
            await Assert.That(rig.OnlyRelay().CallInFlight).IsFalse().Because("the call was answered and is still counted");

            // A cancelled call stops counting at its cancellation, answered or not.
            await relay.SendAsync(Call("held-2", directory, "browser_snapshot"));
            _ = await relay.ListAsync();

            await Assert.That(rig.OnlyRelay().CallInFlight).IsTrue();

            await relay.SendAsync("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":"held-2","reason":"the suite"}}""");
            _ = await relay.ListAsync();

            await Assert.That(rig.OnlyRelay().CallInFlight).IsFalse().Because("the client cancelled the call and it is still counted");
        }
        finally
        {
            // Nothing may stay held past the arm, whatever it asserted.
            _ = navigate.TrySetResult();
            _ = snapshot.TrySetResult();
        }
    }

    /// <summary>
    /// The sessions page reads the background and every relay from memory: the background
    /// with the sessions it holds, each relay with its client and its folder and the
    /// sessions its client drives; and a relay is not closed from the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "What moves", 2026-10-08</b>: the tab reads sessions and connections
    /// in memory, so no process needs a pipe of its own to be described. The background
    /// stands where the session host stood, and each relay where a front stood, with its
    /// client's name and folder and no session of its own; the page draws a session under
    /// the relay its client drives it through.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a page that described the background and
    /// left every relay out.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSessionsPageDescribesTheBackgroundAndEachRelayFromMemory()
    {
        await using var rig = BackgroundServerRig.Start();
        using var relay = await rig.ConnectARelayAsync();
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        var directory = Path.Combine(rig.Sessions.Root, "on-the-page");
        var opened = await relay.RequestAsync("tools/call", Init(directory));

        await Assert.That(opened.IsToolError).IsFalse().Because(opened.ToolText);

        var page = new BackgroundPageSessions(rig.Host, rig.Roster, rig.Clock);
        var snapshot = await page.ReadAsync(hang.Token);

        await Assert.That(snapshot.Servers.Count).IsEqualTo(2).Because(string.Join(" | ", snapshot.Servers.Select(server => server.Marker)));

        var background = snapshot.Servers.Single(server => server.IsHost);
        var relayed = snapshot.Servers.Single(server => server.IsRelay);

        await Assert.That(background.Marker).IsEqualTo(BackgroundPageSessions.BackgroundMarker);
        await Assert.That(background.Description.ProcessId).IsEqualTo(Environment.ProcessId);
        await Assert.That(background.Sessions.Count(session => SamePath(session.Directory, directory))).IsEqualTo(1);

        await Assert.That(relayed.Marker).IsEqualTo(BackgroundPageSessions.RelayMarkerPrefix + rig.OnlyRelay().Id);
        await Assert.That(relayed.Description.ProcessId).IsEqualTo(Environment.ProcessId).Because("a relay is named by the pid Windows read off its pipe");
        await Assert.That(relayed.Description.Client?.Name).IsEqualTo(BackgroundPipeClient.ClientName);
        await Assert.That(relayed.Description.WorkingDirectory).IsEqualTo(BackgroundPipeClient.Folder);
        await Assert.That(relayed.Sessions.Count(session => SamePath(session.Directory, directory))).IsEqualTo(1).Because("the session its client drives is not drawn under the relay");

        // Corrected 2026-10-10 (previously a close asked of the page was refused with
        // "ends with the client itself"): the maintainer's 17 a took the page's close
        // away, so the page has nothing to ask, and a relay ends with its client.
        await Assert.That(rig.Roster.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A Claude Code relay's conversation is named from its client's own records when the
    /// roster is drawn and at no other time, again at every draw, with the window its
    /// greeting named; and the log says where each was found and never what.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all your
    /// recommendations"</i></b>: 1.1 c, the client's file for its process accepted by its
    /// <c>procStart</c>; 1.2 a, the extension's title rule; 1.3 c, read when drawn and
    /// never held; 1.5 a, the window. And from the brief: no log record carries a title,
    /// a prompt or a session id, only which source answered.
    /// </para>
    /// <para>
    /// <b>The records are the suite's, in scratch</b>, at the pid the rig's greeting names
    /// for its client.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a roster that named its relays in
    /// <see cref="RelayRoster.Connected"/>, which the update core decides by, against one
    /// that kept the first name it read, and against a log record that named the title.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClaudeCodeRelaysConversationIsNamedFromItsClientsRecordsWhenTheRosterIsDrawn()
    {
        const string Session = "aaaaaaaa-1111-4111-8111-111111111111";
        const long Started = 134360637732277608;
        const string Window = "1200-134360600000000000";

        using var scratch = ScratchDirectory.Create("background-conversation");
        var config = Path.Combine(scratch.Path, "claude-config");
        var project = Path.Combine(config, "projects", ClaudeCodeRecords.Slug(BackgroundPipeClient.Folder));

        _ = Directory.CreateDirectory(Path.Combine(config, "sessions"));
        _ = Directory.CreateDirectory(project);

        await File.WriteAllTextAsync(
            Path.Combine(config, "sessions", BackgroundPipeClient.ClientPid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json"),
            new JsonObject { ["pid"] = BackgroundPipeClient.ClientPid, ["sessionId"] = Session, ["cwd"] = BackgroundPipeClient.Folder, ["procStart"] = Started.ToString(System.Globalization.CultureInfo.InvariantCulture) }.ToJsonString());

        var record = Path.Combine(project, Session + ".jsonl");

        await File.WriteAllTextAsync(record, """
            {"type":"user","message":{"role":"user","content":"weigh the apples"}}
            {"type":"ai-title","aiTitle":"Apple scales"}

            """);

        await using var rig = BackgroundServerRig.Start();
        using var relay = rig.Connect();

        await relay.SendAsync(Hello(rig, BackgroundPipeClient.ClientName, new ConversationFacts(config, Started, null, null, null), Window));
        await Assert.That((await relay.NextAsync()).Result).IsNotNull();
        _ = await relay.InitializeAsync();

        // The update core's own read names nothing and reads nothing.
        await Assert.That(rig.OnlyRelay().Label).IsNull();
        await Assert.That(rig.OnlyRelay().Conversation).IsNull();

        // A draw reads the records.
        var drawn = rig.Roster.ConnectedWithNames().Single();

        await Assert.That(drawn.Conversation).IsEqualTo(Session);
        await Assert.That(drawn.Label).IsEqualTo(new ConversationName("Apple scales", IsTitle: true));
        await Assert.That(drawn.Window).IsEqualTo(new ClientWindow(Window, BackgroundPipeClient.Folder));

        // And again at the next draw: a rename is seen with no message from anybody.
        await File.AppendAllTextAsync(record, """{"type":"custom-title","customTitle":"Apples, renamed"}""" + "\n");

        await Assert.That(rig.Roster.ConnectedWithNames().Single().Label?.Text).IsEqualTo("Apples, renamed");

        // The sessions page reads the same, with the window.
        var page = await new BackgroundPageSessions(rig.Host, rig.Roster, rig.Clock).ReadAsync(CancellationToken.None);
        var entry = page.Servers.Single(server => server.IsRelay);

        await Assert.That(entry.Conversation).IsEqualTo(new ConversationName("Apples, renamed", IsTitle: true));
        await Assert.That(entry.Window?.Key).IsEqualTo(Window);

        // The log says where each was found, each time that moved, and never what.
        var found = rig.Logs.Records.Where(log => log.Category.EndsWith(nameof(RelayRoster), StringComparison.Ordinal)).Select(log => log.Message).ToList();

        await Assert.That(string.Join(" | ", found)).IsEqualTo(
            $"Relay {drawn.Id}'s conversation was found by ProcessFile and named by AiTitle. | Relay {drawn.Id}'s conversation was found by ProcessFile and named by CustomTitle.");

        foreach (var secret in new[] { Session, "Apple scales", "Apples, renamed", "weigh the apples" })
        {
            await Assert.That(rig.Logs.Records.Any(log => (log.Message + log.Exception).Contains(secret, StringComparison.Ordinal)))
                .IsFalse().Because($"a log record carries '{secret}'");
        }
    }

    /// <summary>
    /// A Codex relay is <i>Codex in &lt;folder&gt;</i> until a call names its thread, and
    /// then the name Codex's index gives the thread of the first call that named one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.4 a, decided 2026-10-10</b>: every Codex call's <c>_meta</c> carries
    /// <c>threadId</c>, 24 of 24 in the measurement, and nothing names the thread before
    /// the first call. The link reads it off the call on its way to the session host and
    /// passes the call on unchanged.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a link that took the thread of every call,
    /// so a later call naming another thread renamed the relay.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACodexRelayIsNamedByTheThreadItsFirstCallNamed()
    {
        const string Thread = "dddddddd-4444-7444-8444-444444444444";
        const string Other = "eeeeeeee-5555-7555-8555-555555555555";

        using var scratch = ScratchDirectory.Create("background-codex");
        var home = Path.Combine(scratch.Path, "codex-home");

        _ = Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "session_index.jsonl"), $$"""
            {"id":"{{Thread}}","thread_name":"Lemon list","updated_at":"2026-10-08T22:49:45Z"}
            {"id":"{{Other}}","thread_name":"Another thread","updated_at":"2026-10-08T22:50:45Z"}

            """);

        await using var rig = BackgroundServerRig.Start();
        using var relay = rig.Connect();

        await relay.SendAsync(Hello(rig, "codex-mcp-client", new ConversationFacts(null, null, null, null, home), window: null));
        await Assert.That((await relay.NextAsync()).Result).IsNotNull();
        _ = await relay.InitializeAsync();
        _ = await relay.ListAsync();

        var before = rig.Roster.ConnectedWithNames().Single();

        await Assert.That(before.Label).IsEqualTo(new ConversationName("Codex in BackgroundTests", IsTitle: false));
        await Assert.That(before.Conversation).IsNull();

        // A call naming its thread, as every Codex call does; its answer, a refusal for
        // naming no session, says the link has read it.
        _ = await relay.RequestAsync("tools/call", CodexCall(Thread));

        var after = rig.Roster.ConnectedWithNames().Single();

        await Assert.That(after.Conversation).IsEqualTo(Thread);
        await Assert.That(after.Label).IsEqualTo(new ConversationName("Lemon list", IsTitle: true));

        // A later call naming another thread changes nothing: one server, one thread.
        _ = await relay.RequestAsync("tools/call", CodexCall(Other));

        await Assert.That(rig.Roster.ConnectedWithNames().Single().Label?.Text).IsEqualTo("Lemon list");
    }

    /// <summary>
    /// A Codex relay whose thread Codex's index does not name is called by the thread's
    /// first message, and the log says only where the name was found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.4 a as the maintainer approved it on 2026-10-10</b>: the index's name, else the
    /// first message, else <i>Codex in &lt;folder&gt;</i>. And from the brief: no log
    /// record carries a title, a prompt or an id, and a first message is a prompt.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a log record that carried the name.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACodexRelayWhoseThreadHasNoNameIsCalledByItsFirstMessageAndTheLogSaysOnlyWhere()
    {
        const string Thread = "dddddddd-4444-7444-8444-444444444444";
        const string FirstMessage = "Count the lemons in the crate";

        using var scratch = ScratchDirectory.Create("background-codex-first");
        var home = Path.Combine(scratch.Path, "codex-home");
        var day = Path.Combine(home, "sessions", "2026", "10", "09");

        _ = Directory.CreateDirectory(day);
        await File.WriteAllTextAsync(Path.Combine(day, $"rollout-2026-10-09T00-50-42-{Thread}.jsonl"), """
            {"type":"session_meta","payload":{"id":"THREAD","cli_version":"0.162.0"}}
            {"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"MESSAGE"}]}}
            {"type":"event_msg","payload":{"type":"item_completed","thread_id":"THREAD","item":{"type":"UserMessage","content":[{"type":"text","text":"MESSAGE","text_elements":[]}]}}}

            """.Replace("THREAD", Thread, StringComparison.Ordinal).Replace("MESSAGE", FirstMessage, StringComparison.Ordinal));

        await using var rig = BackgroundServerRig.Start();
        using var relay = rig.Connect();

        await relay.SendAsync(Hello(rig, "codex-mcp-client", new ConversationFacts(null, null, null, null, home), window: null));
        await Assert.That((await relay.NextAsync()).Result).IsNotNull();
        _ = await relay.InitializeAsync();
        _ = await relay.ListAsync();
        _ = await relay.RequestAsync("tools/call", CodexCall(Thread));

        var drawn = rig.Roster.ConnectedWithNames().Single();

        await Assert.That(drawn.Label).IsEqualTo(new ConversationName(FirstMessage, IsTitle: true));

        var found = rig.Logs.Records.Where(log => log.Category.EndsWith(nameof(RelayRoster), StringComparison.Ordinal)).Select(log => log.Message).ToList();

        await Assert.That(found).Contains($"Relay {drawn.Id}'s conversation was found by CodexCall and named by CodexRollout.");

        foreach (var secret in new[] { Thread, FirstMessage })
        {
            await Assert.That(rig.Logs.Records.Any(log => (log.Message + log.Exception).Contains(secret, StringComparison.Ordinal)))
                .IsFalse().Because($"a log record carries '{secret}'");
        }
    }

    /// <summary>A relay's greeting from the rig, for another client and with the conversation's facts and a window.</summary>
    /// <param name="rig">The background.</param>
    /// <param name="clientName">What the client calls itself.</param>
    /// <param name="facts">Where the client keeps its conversation.</param>
    /// <param name="window">The VS Code window, or <see langword="null"/>.</param>
    /// <returns>The frame.</returns>
    private static string Hello(BackgroundServerRig rig, string clientName, ConversationFacts facts, string? window)
    {
        var hello = JsonNode.Parse(rig.Hello())!.AsObject();
        var parameters = hello["params"]!.AsObject();

        parameters["client"]!["name"] = clientName;
        parameters[ConversationFacts.Member] = facts.ToJson();

        if (window is not null)
        {
            parameters[ConversationFacts.WindowMember] = window;
        }

        return hello.ToJsonString();
    }

    /// <summary>A call's parameters as Codex sends them: a tool, and its thread in <c>_meta</c>.</summary>
    /// <param name="thread">The thread.</param>
    /// <returns>The parameters.</returns>
    private static JsonObject CodexCall(string thread) => new()
    {
        ["_meta"] = new JsonObject { ["threadId"] = thread, ["sessionId"] = thread, ["callId"] = "call_suite_1", ["progressToken"] = 1 },
        ["name"] = "browser_navigate",
        ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>" },
    };

    /// <summary>Opens a connection, sends one request first, and reads its one answer.</summary>
    /// <param name="rig">The background.</param>
    /// <param name="method">The request's method.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <returns>The answer.</returns>
    private static async Task<WireFrame> AskAsync(BackgroundServerRig rig, string method, JsonObject? parameters)
    {
        using var start = rig.Connect();

        await start.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = BackgroundPipe.IdPrefix + "start-1",
            ["method"] = method,
            ["params"] = parameters,
        }.ToJsonString());

        var answer = await start.NextAsync();

        // Answered once and closed, and closed only after whatever the request did.
        await Assert.That(await start.ClosedAsync()).IsTrue().Because($"the background kept a '{method}' connection open after answering it");

        return answer;
    }

    /// <summary>Sends a greeting the background must refuse, and reads the refusal.</summary>
    /// <param name="rig">The background.</param>
    /// <param name="hello">The greeting.</param>
    /// <returns>The refusal.</returns>
    private static async Task<WireFrame> RefusedAsync(BackgroundServerRig rig, string hello)
    {
        using var relay = rig.Connect();

        await relay.SendAsync(hello);

        var answer = await relay.NextAsync();

        await Assert.That(answer.Error).IsNotNull().Because(answer.Text);
        await Assert.That(answer.IdText).IsEqualTo(RelayWire.IdPrefix + "hello");
        await Assert.That(await relay.ClosedAsync()).IsTrue().Because("a refused relay's connection was left open");
        await Assert.That(rig.Roster.Count).IsEqualTo(0).Because("a refused relay was admitted to the roster");

        return answer;
    }

    /// <summary>A server on the rig's own name and host, or the reason it could not take the name.</summary>
    /// <param name="rig">The rig whose name, host and roster it takes.</param>
    /// <param name="verbs">Its verbs.</param>
    /// <param name="refusal">Why the name could not be taken.</param>
    /// <returns>The server, not yet accepting, or <see langword="null"/>.</returns>
    private static BackgroundServer? TryToServe(BackgroundServerRig rig, FakeBackgroundVerbs verbs, out IOException? refusal)
    {
        try
        {
            refusal = null;
            return new BackgroundServer(rig.Host, rig.Identity, new RelayRoster(rig.Clock), verbs, NullLoggerFactory.Instance);
        }
        catch (IOException taken)
        {
            refusal = taken;
            return null;
        }
    }

    /// <summary>
    /// A relay's connection that fails every notice it is asked to send, with the
    /// exception an arm gives it, and has gone already.
    /// </summary>
    /// <param name="failure">What every notice throws.</param>
    private sealed class FailingRelayLink(Exception failure) : IRelayLink
    {
        private int _told;

        /// <summary>How many notices it was asked to send.</summary>
        public int Told => Volatile.Read(ref _told);

        /// <inheritdoc />
        public bool CallInFlight => false;

        /// <inheritdoc />
        public Task Closed => Task.CompletedTask;

        /// <inheritdoc />
        public Task<ModelContextProtocol.Protocol.JsonRpcMessage> AskAsync(string method, JsonNode? parameters, CancellationToken cancellationToken) =>
            Task.FromException<ModelContextProtocol.Protocol.JsonRpcMessage>(failure);

        /// <inheritdoc />
        public Task TellAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _told);
            return Task.FromException(failure);
        }
    }

    /// <summary>The kind a refusal names in <c>data.refusal</c>.</summary>
    /// <param name="answer">The refusal.</param>
    /// <returns>Its kind.</returns>
    private static string? Kind(WireFrame answer) => Json.Text(answer.Error?["data"], "refusal");

    /// <summary>The sentence a refusal carries.</summary>
    /// <param name="answer">The refusal.</param>
    /// <returns>Its message.</returns>
    private static string? Sentence(WireFrame answer) => Json.Text(answer.Error, "message");

    /// <summary>A relay's notification of its own, carrying a countdown.</summary>
    /// <param name="method">The method.</param>
    /// <param name="idleAt">The countdown.</param>
    /// <returns>The frame.</returns>
    private static string Notification(string method, DateTimeOffset idleAt) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["method"] = method,
        ["params"] = new JsonObject { ["idleAt"] = RelayWire.Instant(idleAt) },
    }.ToJsonString();

    /// <summary>Whether two spellings name one directory.</summary>
    /// <param name="first">One.</param>
    /// <param name="second">The other.</param>
    /// <returns>Whether their full paths agree, case aside.</returns>
    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>The arguments of a <c>browserai_init</c> call opening one session.</summary>
    /// <remarks>
    /// <b>All four of the session's settings are named</b>, at today's defaults: lane
    /// SESS's batch C refuses an init that leaves one out.
    /// </remarks>
    /// <param name="directory">The session directory.</param>
    /// <returns>The call's parameters.</returns>
    private static JsonObject Init(string directory) => new()
    {
        ["name"] = SessionToolSurface.Init,
        ["arguments"] = new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session the background's pipe arms open",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = SessionTimes.HiddenIdleMinutes,
        },
    };

    /// <summary>A client's call naming a session, with a <c>url</c> only for the tool that takes one.</summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-10-10 (previously every call carried a <c>url</c>)</b>: a
    /// <c>browser_snapshot</c> with one is refused for an argument its schema does not
    /// have before it reaches the child, so the arm that holds a snapshot in the child
    /// raced that refusal against its own in-flight check, and lost it on a run of lane
    /// FIX's that day.
    /// </remarks>
    /// <param name="id">The request id.</param>
    /// <param name="directory">The session.</param>
    /// <param name="tool">The tool.</param>
    /// <returns>The frame.</returns>
    private static string Call(string id, string directory, string tool)
    {
        var arguments = new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite holding a call in flight",
        };

        if (tool is "browser_navigate")
        {
            arguments["url"] = "data:text/html,<h1>ok</h1>";
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = tool,
                ["arguments"] = arguments,
            },
        }.ToJsonString();
    }
}

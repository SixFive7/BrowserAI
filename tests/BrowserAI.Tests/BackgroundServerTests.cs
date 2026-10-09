// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Background;
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

        // A relay ends with its client, never from the page.
        var refusal = await page.CloseAsync(relayed, hang.Token);

        await Assert.That(refusal).Contains("ends with the client itself");
        await Assert.That(rig.Roster.Count).IsEqualTo(1);
    }

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

    /// <summary>A client's call naming a session.</summary>
    /// <param name="id">The call's id.</param>
    /// <param name="directory">The session.</param>
    /// <param name="tool">The tool.</param>
    /// <returns>The frame.</returns>
    private static string Call(string id, string directory, string tool) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = "tools/call",
        ["params"] = new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = new JsonObject
            {
                ["url"] = "data:text/html,<h1>ok</h1>",
                ["session"] = directory,
                ["why"] = "the suite holding a call in flight",
            },
        },
    }.ToJsonString();
}

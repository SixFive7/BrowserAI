// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>Calls the relay holds while it has no background, and how each is answered.</summary>
internal sealed partial class RelayTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(ManualClock.OneTick);

    /// <summary>
    /// Calls held while there is no background are passed on in the order they
    /// arrived, once the background has answered the greeting and the client's own
    /// <c>initialize</c>, replayed under an id of the relay's, whose answer never
    /// reaches the client.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task HeldCallsArePassedOnInArrivalOrderAfterTheGreetingAndTheReplay()
    {
        await using var rig = RelayRig.Start();
        rig.Finder.Absence = new BackgroundAbsence.Starting();

        _ = await rig.InitializeAsync(KnownClients.ClaudeCode, "2.1.290");
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        var first = RelayRig.CallFrame("1");
        var second = RelayRig.CallFrame("\"two\"");
        await rig.SendAsync(first);
        await rig.SendAsync(second);

        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var background = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);

        var hello = await background.NextAsync();
        await background.SendAsync(Frames.HelloAnswer(hello.IdText!, FakeBackground.Pid));

        // The client's own initialize, every byte of it, under the relay's id.
        var replay = await background.NextAsync();

        await Assert.That(replay.IdText).StartsWith(RelayWire.IdPrefix);
        await Assert.That(replay.Text).IsEqualTo(
            RelayRig.InitializeFrame(KnownClients.ClaudeCode, "2.1.290").Replace("\"id\":0", $"\"id\":\"{replay.IdText}\"", StringComparison.Ordinal));

        await background.SendAsync(Frames.ReplayAnswer(replay.IdText!));

        await Assert.That((await background.NextAsync()).Method).IsEqualTo("notifications/initialized");
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(first);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(second);

        await background.SendAsync("""{"jsonrpc":"2.0","id":1,"result":{"content":[]}}""");
        await background.SendAsync("""{"jsonrpc":"2.0","id":"two","result":{"content":[]}}""");

        // The replay's answer went nowhere: the client's next frames are the two answers.
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("1");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("two");
        await Assert.That(await rig.BarrierAsync()).IsEmpty();
    }

    /// <summary>A held call the client cancels is dropped, and is never answered or passed on.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHeldCallTheClientCancelsIsDroppedWithNoAnswer()
    {
        await using var rig = RelayRig.Start();
        rig.Finder.Absence = new BackgroundAbsence.Starting();

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        var kept = RelayRig.CallFrame("2");
        await rig.SendAsync(kept);
        await rig.SendAsync(RelayRig.CancelFrame("1"));

        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var background = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);
        _ = await background.GreetAsync();

        await Assert.That((await background.NextAsync()).Text).IsEqualTo(kept);

        await background.SendAsync("""{"jsonrpc":"2.0","id":2,"result":{"content":[]}}""");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("2");

        // Past the hold bound as well: the cancelled call has no answer coming at all.
        await rig.StepAsync(RelayConstants.HoldBound);
        await Assert.That(await rig.BarrierAsync()).IsEmpty();
    }

    /// <summary>
    /// A held call is answered at its deadline and not a tick before, with what the
    /// finder says then: the task disabled, missing or neither, a clean end, or a
    /// process that never opened its pipe.
    /// </summary>
    /// <remarks>
    /// RESOLUTIONS 9: relays hold up to 150 s and then answer, naming a disabled task
    /// with how to enable it and leaving it disabled (D12 b), or a missing task, and
    /// telling the person to start BrowserAI from the Start Menu.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHeldCallIsAnsweredAtItsDeadlineWithWhatTheFinderSaysAndNotATickBefore()
    {
        const string Tool = "browser_navigate";
        const string TaskName = FakeBackgroundFinder.TaskName;
        const string Detail = "The task last ran at sign-in and exited 0.";

        (BackgroundAbsence Absence, string Row, string Expected)[] cases =
        [
            (new BackgroundAbsence.NotRunning(TaskState.Disabled, TaskName, null), nameof(RelayErrors.NotRunning), RelayErrors.NotRunning(Tool, TaskState.Disabled, TaskName, null)),
            (new BackgroundAbsence.NotRunning(TaskState.Missing, TaskName, null), nameof(RelayErrors.NotRunning), RelayErrors.NotRunning(Tool, TaskState.Missing, TaskName, null)),
            (new BackgroundAbsence.NotRunning(TaskState.Ready, TaskName, Detail), nameof(RelayErrors.NotRunning), RelayErrors.NotRunning(Tool, TaskState.Ready, TaskName, Detail)),
            (new BackgroundAbsence.NotRunning(TaskState.Unknown, TaskName, null), nameof(RelayErrors.NotRunning), RelayErrors.NotRunning(Tool, TaskState.Unknown, TaskName, null)),
            (new BackgroundAbsence.CleanEnd(), nameof(RelayErrors.NotRunning), RelayErrors.NotRunning(Tool, TaskState.Unknown, string.Empty, null)),
            (new BackgroundAbsence.Starting(), nameof(RelayErrors.Hung), RelayErrors.Hung(Tool, wasPassedOn: false, RelayRig.Facts.LogPath)),
        ];

        foreach (var (absence, row, expected) in cases)
        {
            await answeredAtTheDeadlineAsync(absence, row, expected);
        }

        // A local function, so each case has a rig of its own and disposes it.
        static async Task answeredAtTheDeadlineAsync(BackgroundAbsence absence, string row, string expected)
        {
            await using var rig = RelayRig.Start();
            rig.Finder.Absence = absence;

            _ = await rig.InitializeAsync(KnownClients.Codex);
            _ = await rig.ListAsync();
            await rig.SettledAsync();

            await rig.SendAsync(RelayRig.CallFrame("1"));
            await Assert.That(await rig.BarrierAsync()).IsEmpty();

            await rig.StepAsync(RelayConstants.HoldBound - OneTick);
            await Assert.That(await rig.BarrierAsync()).IsEmpty();

            await rig.StepAsync(OneTick);
            var answered = await rig.NextAsync();

            await Assert.That(answered.IdText).IsEqualTo("1");
            await Assert.That(answered.IsToolError).IsTrue();
            Match(answered.ToolText, row, expected);

            // Asked when the call arrived, and again at its deadline.
            await Assert.That(rig.Finder.Explained.Count).IsGreaterThanOrEqualTo(2);
        }
    }

    /// <summary>
    /// A recorded crash is answered at once, with R's sentence word for word, and so is
    /// every call that was already held when the crash became known.
    /// </summary>
    /// <remarks>
    /// R, "r ok", 2026-10-08: from a crash on, every call is answered at once, because
    /// only the person's Start Menu start brings BrowserAI back.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACrashIsAnsweredAtOnceAndSoIsEveryCallHeldBeforeIt()
    {
        await using var rig = RelayRig.Start();
        rig.Finder.Absence = new BackgroundAbsence.Starting();

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var at = new DateTimeOffset(2026, 10, 8, 13, 21, 17, TimeSpan.Zero);
        const string Log = @"C:\Data\BrowserAI-relay-tests\logs\browserai-20261008.log";
        rig.Finder.Absence = new BackgroundAbsence.Crashed(at, -1073741819, Log);

        await rig.SendAsync(RelayRig.CallFrame("2"));

        var first = await rig.NextAsync();
        var second = await rig.NextAsync();

        await Assert.That(first.IdText).IsEqualTo("1");
        await Assert.That(second.IdText).IsEqualTo("2");

        // R's accepted text, written out here and not taken from the catalogue.
        await Assert.That(first.ToolText).IsEqualTo(
            "BrowserAI's background process crashed at 2026-10-08T13:21:17.0000000+00:00 (exit code -1073741819). Nothing was run. "
            + @"The person at this computer needs to read C:\Data\BrowserAI-relay-tests\logs\browserai-20261008.log, report the bug at https://github.com/SixFive7/BrowserAI/issues, and then start BrowserAI from the Start Menu. "
            + "Only that person can restart it: do not start BrowserAI yourself, and do not retry this call until they have.");

        Match(second.ToolText, nameof(RelayErrors.Crashed), RelayErrors.Crashed(at, -1073741819, Log));

        // A crash that recorded no exit code says so.
        rig.Finder.Absence = new BackgroundAbsence.Crashed(at, null, Log);
        await rig.SendAsync(RelayRig.CallFrame("3"));
        var third = await rig.NextAsync();

        await Assert.That(third.ToolText).Contains("(exit code unknown)");
        Match(third.ToolText, nameof(RelayErrors.Crashed), RelayErrors.Crashed(at, null, Log));
    }

    /// <summary>A build that is not installed is answered at once, with the command a developer runs.</summary>
    /// <remarks>D11 a, decided 2026-10-08: nothing will ever start a background for it.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABuildThatIsNotInstalledIsAnsweredAtOnce()
    {
        await using var rig = RelayRig.Start();
        const string Executable = @"C:\Source\BrowserAI\src\BrowserAI\bin\Debug\BrowserAI.exe";
        rig.Finder.Absence = new BackgroundAbsence.NotInstalled(Executable, RelayRig.Facts.DataRoot);

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        var answered = await rig.NextAsync();

        await Assert.That(answered.IdText).IsEqualTo("1");
        await Assert.That(answered.ToolText).Contains($"\"{Executable}\" --background --data-root \"{RelayRig.Facts.DataRoot}\"");
        Match(answered.ToolText, nameof(RelayErrors.NotInstalled), RelayErrors.NotInstalled("browser_navigate", Executable, RelayRig.Facts.DataRoot));
    }

    /// <summary>
    /// An update that is installing is answered at once, with U2's sentence and the
    /// recovery each client needs.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallingUpdateIsAnsweredAtOnceInEachClientsWords()
    {
        foreach (var client in new[] { KnownClients.ClaudeCode, KnownClients.Codex, "some-client-nobody-has-met" })
        {
            await answeredAtOnceAsync(client);
        }

        static async Task answeredAtOnceAsync(string client)
        {
            await using var rig = RelayRig.Start();
            rig.Finder.Absence = new BackgroundAbsence.UpdateInstalling();

            _ = await rig.InitializeAsync(client);
            _ = await rig.ListAsync();

            await rig.SendAsync(RelayRig.CallFrame("1"));
            var answered = await rig.NextAsync();

            await Assert.That(answered.ToolText).StartsWith("BrowserAI is installing an update; nothing was run; call again in a few seconds.");
            Match(answered.ToolText, nameof(RelayErrors.UpdateInstalling), RelayErrors.UpdateInstalling("browser_navigate", null, client));
        }
    }

    /// <summary>
    /// The relay looks for the background only once it has a handshake to replay: at
    /// once after <c>initialize</c>, then every 2 s, and every 500 ms while it holds a
    /// call.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRelayLooksAtOnceAfterInitializeThenEveryTwoSecondsAndEveryHalfSecondWhileHolding()
    {
        await using var rig = RelayRig.Start();

        await rig.StepAsync(TimeSpan.FromSeconds(10));
        await Assert.That(rig.Finder.Looks).IsEqualTo(0);

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();
        await Assert.That(rig.Finder.Looks).IsEqualTo(1);

        await rig.StepAsync(RelayConstants.LookWhileIdle - OneTick);
        await Assert.That(rig.Finder.Looks).IsEqualTo(1);

        await rig.StepAsync(OneTick);
        await Assert.That(rig.Finder.Looks).IsEqualTo(2);

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();
        await rig.SettledAsync();

        await rig.StepAsync(RelayConstants.LookWhileHolding - OneTick);
        await Assert.That(rig.Finder.Looks).IsEqualTo(2);

        await rig.StepAsync(OneTick);
        await Assert.That(rig.Finder.Looks).IsEqualTo(3);

        await rig.StepAsync(RelayConstants.LookWhileHolding);
        await Assert.That(rig.Finder.Looks).IsEqualTo(4);
    }

    /// <summary>
    /// A look whose timer fires before its moment by the relay's clock is armed again:
    /// the relay looks when the moment comes, and goes on looking after it.
    /// </summary>
    /// <remarks>
    /// <b>Found 2026-10-09 by the real-scheduler arm of <c>RealInstallerTests</c></b>,
    /// where a relay started before its background never reached it, and planted red
    /// that day against a look timer that looked only when its moment had come and was
    /// otherwise left unarmed. Most real timers fire early by that clock; the
    /// measurement is on <c>RelayEngine.OnLookTimer</c> and on
    /// <see cref="ManualClock.FireEarly"/>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALookWhoseTimerFiresEarlyIsArmedAgainAndTheRelayGoesOnLooking()
    {
        await using var rig = RelayRig.Start();

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();
        await Assert.That(rig.Finder.Looks).IsEqualTo(1);

        // A tick before the moment, the timer fires.
        await rig.StepAsync(RelayConstants.LookWhileIdle - OneTick);
        rig.Clock.FireEarly(OneTick);
        await rig.SettledAsync();
        await Assert.That(rig.Finder.Looks).IsEqualTo(1);

        // The moment comes, and the relay looks.
        await rig.StepAsync(OneTick);
        await Assert.That(rig.Finder.Looks).IsEqualTo(2).Because("a look whose timer fired early was never armed again");

        // And it goes on looking at its pace.
        await rig.StepAsync(RelayConstants.LookWhileIdle);
        await Assert.That(rig.Finder.Looks).IsEqualTo(3);
    }

    /// <summary>
    /// The greeting carries every fact the background needs about the client, and
    /// nothing it read from the environment.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheGreetingCarriesEveryFactTheBackgroundNeedsAboutTheClient()
    {
        await using var rig = RelayRig.Start();
        var (_, hello) = await rig.ConnectedAsync(KnownClients.Codex);
        var facts = hello.Params!;

        await Assert.That(hello.Method).IsEqualTo("browserai/hello");
        await Assert.That(hello.IdText).IsEqualTo(RelayWire.IdPrefix + "hello");
        await Assert.That(Json.Text(facts, "build")).IsEqualTo(RelayRig.Facts.Build);
        await Assert.That((int?)facts["relayPid"]).IsEqualTo(RelayRig.Facts.RelayPid);
        await Assert.That((int?)facts["clientPid"]).IsEqualTo(RelayRig.Facts.ClientPid);
        await Assert.That(Json.Text(facts["client"], "name")).IsEqualTo(KnownClients.Codex);
        await Assert.That(Json.Text(facts["client"], "version")).IsEqualTo("1.0");
        await Assert.That(Json.Text(facts, "reconnect")).IsEqualTo(nameof(RelayReconnect.McpReconnect));
        await Assert.That(Json.Text(facts, "folder")).IsEqualTo(RelayRig.Facts.Folder);
        await Assert.That(Json.Text(facts, "dataRoot")).IsEqualTo(RelayRig.Facts.DataRoot);
        await Assert.That(Json.Text(facts, "idleAt")).IsEqualTo(RelayRig.At(TimeSpan.FromMinutes(10)));

        // The classifier was asked about the client by the name it gave, once.
        await Assert.That(rig.Classified.Count).IsEqualTo(1);
        await Assert.That(rig.Classified[0]).IsEqualTo(KnownClients.Codex);

        // Eight members and no ninth: nothing else rides along.
        await Assert.That(facts.Count).IsEqualTo(8);
    }

    /// <summary>
    /// The greeting carries where the client keeps its conversation, and a VS Code tab's
    /// window, exactly as the classifier read them, and only the members it read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.1 c, 1.4 a and 1.5 a, decided 2026-10-10</b>: the background reads the
    /// client's records when it draws, from what the relay could see and it cannot, the
    /// client's environment and its parent, so the greeting is where those travel. A
    /// member the classifier did not read is left out, which is why a client BrowserAI
    /// reads no records of is greeted with the eight members of the arm above.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a greeting that carried the conversation and
    /// dropped the window.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheGreetingCarriesWhereTheClientKeepsItsConversationAndItsWindow()
    {
        var read = new ClientReading(
            RelayReconnect.None,
            new ConversationFacts(@"C:\Users\someone\.claude", 134360637732277608, "aaaaaaaa-1111-4111-8111-111111111111", null, null),
            "1200-134360600000000000");

        await using var rig = RelayRig.Start(readClient: _ => read);
        var (_, hello) = await rig.ConnectedAsync(KnownClients.ClaudeCode);
        var facts = hello.Params!;

        await Assert.That(Json.Text(facts, "reconnect")).IsEqualTo(nameof(RelayReconnect.None));
        await Assert.That(Json.Text(facts, ConversationFacts.WindowMember)).IsEqualTo("1200-134360600000000000");

        var conversation = facts[ConversationFacts.Member] as JsonObject;

        await Assert.That(conversation).IsNotNull().Because(hello.Text);
        await Assert.That(ConversationFacts.From(conversation)).IsEqualTo(read.Conversation);
        await Assert.That(Json.Text(conversation, "clientStarted")).IsEqualTo("134360637732277608").Because("a FILETIME travels as text");
        await Assert.That(conversation!.Count).IsEqualTo(3).Because("a member the classifier did not read rode along");
        await Assert.That(facts.Count).IsEqualTo(10);
    }

    /// <summary>A classifier that fails leaves the greeting saying Unknown, and the relay connects all the same.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClassifierThatFailsLeavesTheGreetingSayingUnknown()
    {
        await using var rig = RelayRig.Start(readClient: Unclassifiable);
        var (background, hello) = await rig.ConnectedAsync(KnownClients.ClaudeCode);

        await Assert.That(Json.Text(hello.Params, "reconnect")).IsEqualTo(nameof(RelayReconnect.Unknown));

        _ = await rig.ListAsync();
        var call = RelayRig.CallFrame("1");
        await rig.SendAsync(call);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(call);
    }

    /// <summary>
    /// A background that refuses the greeting has the held calls answered with its own
    /// sentence, or with the update sentence when it says it is installing one; its
    /// pipe is closed, and the relay looks again and connects to the next.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedGreetingAnswersTheHeldCallsAndTheRelayLooksAgain()
    {
        await using var rig = RelayRig.Start();
        rig.Finder.Absence = new BackgroundAbsence.Starting();

        _ = await rig.InitializeAsync(KnownClients.ClaudeCode);
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        // A different build.
        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var first = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);

        const string Sentence = "This BrowserAI background serves build 9.9.8, and this relay is build 9.9.9-relay-tests.";
        var hello = await first.NextAsync();
        await first.SendAsync(Frames.Refusal(hello.IdText!, Sentence, "build"));

        var refused = await rig.NextAsync();

        await Assert.That(refused.IdText).IsEqualTo("1");
        Match(refused.ToolText, nameof(RelayErrors.BackgroundRefused), RelayErrors.BackgroundRefused("browser_navigate", Sentence));
        await Assert.That(await first.ClosedAsync()).IsTrue();

        // An update installing.
        await rig.SendAsync(RelayRig.CallFrame("2"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var second = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);

        hello = await second.NextAsync();
        await second.SendAsync(Frames.Refusal(hello.IdText!, "BrowserAI is installing version 1.2.0.", "updating"));

        var updating = await rig.NextAsync();

        await Assert.That(updating.IdText).IsEqualTo("2");
        Match(updating.ToolText, nameof(RelayErrors.UpdateInstalling), RelayErrors.UpdateInstalling("browser_navigate", null, KnownClients.ClaudeCode));
        await Assert.That(await second.ClosedAsync()).IsTrue();

        // And the next one is greeted and served.
        var third = RelayRig.CallFrame("3");
        await rig.SendAsync(third);
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var accepting = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);
        _ = await accepting.GreetAsync();

        await Assert.That((await accepting.NextAsync()).Text).IsEqualTo(third);
    }

    /// <summary>
    /// A pipe that closes under a call in flight has the call answered once the finder
    /// has said whether the background crashed, asked with the pid the background gave.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APipeThatClosesUnderACallAnswersItWithWhatTheFinderSays()
    {
        var at = new DateTimeOffset(2026, 10, 8, 14, 0, 0, TimeSpan.Zero);
        const string Log = @"C:\Data\BrowserAI-relay-tests\logs\browserai-20261008.log";

        // A crash recorded: the call may have run in part, and only the person restarts.
        await using (var rig = RelayRig.Start())
        {
            var (background, _) = await rig.ConnectedAsync(pid: 9191);
            _ = await rig.ListAsync();

            await rig.SendAsync(RelayRig.CallFrame("1"));
            await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");

            rig.Finder.Absence = new BackgroundAbsence.Crashed(at, 3, Log);
            background.GoAway();

            var answered = await rig.NextAsync();

            await Assert.That(answered.IdText).IsEqualTo("1");
            Match(answered.ToolText, nameof(RelayErrors.CrashedDuringTheCall), RelayErrors.CrashedDuringTheCall("browser_navigate", at, 3, Log));
            await Assert.That(rig.Finder.Explained).Contains(9191);

            // Not connected any more, and the crash stands: the next call is answered at once.
            await rig.SendAsync(RelayRig.CallFrame("2"));
            Match((await rig.NextAsync()).ToolText, nameof(RelayErrors.Crashed), RelayErrors.Crashed(at, 3, Log));
        }

        // A clean end: the call may have run in part, and the next call is held.
        await using (var rig = RelayRig.Start())
        {
            var (background, _) = await rig.ConnectedAsync();
            _ = await rig.ListAsync();

            await rig.SendAsync(RelayRig.CallFrame("1"));
            await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");

            rig.Finder.Absence = new BackgroundAbsence.CleanEnd();
            background.GoAway();

            var answered = await rig.NextAsync();

            await Assert.That(answered.IdText).IsEqualTo("1");
            Match(answered.ToolText, nameof(RelayErrors.StoppedDuringTheCall), RelayErrors.StoppedDuringTheCall("browser_navigate"));

            await rig.SendAsync(RelayRig.CallFrame("2"));
            await Assert.That(await rig.BarrierAsync()).IsEmpty();
        }
    }
}

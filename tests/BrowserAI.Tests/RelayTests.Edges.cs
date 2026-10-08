// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Relay;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The states between the ordinary ones: a background still greeting, a pipe that
/// closes on an agreement, a client that cannot be written to, a finder that fails.
/// </summary>
internal sealed partial class RelayTests
{
    /// <summary>
    /// What the client sends that is neither a call nor answered by the relay is
    /// dropped while the relay is not connected, and never reaches a background that
    /// has not answered its greeting.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NotificationsAndAnswersAreDroppedUntilTheRelayIsConnected()
    {
        await using var rig = RelayRig.Start();
        var background = rig.Finder.Offer();
        _ = await rig.InitializeAsync();

        // The background holds its answer to the greeting, so the relay is greeting.
        var hello = await background.NextAsync();

        await rig.SendAsync("""{"jsonrpc":"2.0","method":"notifications/roots/list_changed"}""");
        await rig.SendAsync("""{"jsonrpc":"2.0","id":"bg-0","result":{"roots":[]}}""");
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        await background.SendAsync(Frames.HelloAnswer(hello.IdText!, FakeBackground.Pid));

        // The replay is the first thing after the greeting: neither went on.
        var replay = await background.NextAsync();

        await Assert.That(replay.Method).IsEqualTo("initialize");
        await background.SendAsync(Frames.ReplayAnswer(replay.IdText!));
        await Assert.That((await background.NextAsync()).Method).IsEqualTo("notifications/initialized");

        // Connected, the same notification goes on.
        const string Roots = """{"jsonrpc":"2.0","method":"notifications/roots/list_changed"}""";
        await rig.SendAsync(Roots);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(Roots);
    }

    /// <summary>
    /// A held call whose deadline comes while the background it found has not answered
    /// its greeting is answered with the hang sentence, and never passed on.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHeldCallWhoseDeadlineComesDuringTheGreetingIsAnsweredWithTheHangSentence()
    {
        await using var rig = RelayRig.Start();
        rig.Finder.Absence = new BackgroundAbsence.Starting();

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        // Look by look, so that the look that finds the background comes half a
        // second before the call's deadline.
        var looks = (int)((RelayConstants.HoldBound - RelayConstants.LookWhileHolding) / RelayConstants.LookWhileHolding);

        for (var look = 1; look < looks; look++)
        {
            await rig.StepAsync(RelayConstants.LookWhileHolding);
        }

        var background = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);

        var hello = await background.NextAsync();

        await Assert.That(hello.Method).IsEqualTo("browserai/hello");

        await rig.StepAsync(RelayConstants.LookWhileHolding - OneTick);
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        await rig.StepAsync(OneTick);
        var answered = await rig.NextAsync();

        await Assert.That(answered.IdText).IsEqualTo("1");
        Match(answered.ToolText, nameof(RelayErrors.Hung), RelayErrors.Hung("browser_navigate", wasPassedOn: false, RelayRig.Facts.LogPath));

        // Greeted late, the background is never handed the answered call: the next
        // frame after the handshake answers its own question.
        await background.AnswerGreetingAsync(hello);
        await Assert.That((await AskAsync(background, "r1")).IdText).IsEqualTo("r1");
    }

    /// <summary>
    /// A background that never answers its greeting is reported hung after the hang
    /// bound, and a call held for it is answered then, before its own deadline.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABackgroundThatNeverAnswersItsGreetingIsReportedHungAndItsHeldCallsAnswered()
    {
        await using var rig = RelayRig.Start();
        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        var background = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileIdle);
        await Assert.That((await background.NextAsync()).Method).IsEqualTo("browserai/hello");

        // Held, since the greeting has not been answered: its own deadline is a
        // second after the hang bound runs out.
        await rig.StepAsync(TimeSpan.FromSeconds(1));
        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();
        await rig.StepAsync(RelayConstants.ProbeInterval - TimeSpan.FromSeconds(1));

        var probes = (int)(RelayConstants.HangBound / RelayConstants.ProbeInterval);

        for (var probe = 2; probe < probes; probe++)
        {
            await rig.StepAsync(RelayConstants.ProbeInterval);
        }

        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        await rig.StepAsync(RelayConstants.ProbeInterval);
        var answered = await rig.NextAsync();

        await Assert.That(answered.IdText).IsEqualTo("1");
        Match(answered.ToolText, nameof(RelayErrors.Hung), RelayErrors.Hung("browser_navigate", wasPassedOn: false, RelayRig.Facts.LogPath));
    }

    /// <summary>
    /// A pipe that closes while the relay has agreed to end hands what it held back to
    /// the client's side: the call waits for the next background and goes to it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APipeThatClosesOnAnAgreementKeepsWhatItHeldForTheNextBackground()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        await rig.StepAsync(RelayConstants.IdleCountdown);
        await Assert.That((bool?)(await AskAsync(background, "r1")).Result!["ready"]).IsTrue();

        var call = RelayRig.CallFrame("1");
        await rig.SendAsync(call);
        await Assert.That((await background.NextAsync()).Method).IsEqualTo("browserai/withdraw");

        rig.Finder.Absence = new BackgroundAbsence.Starting();
        background.GoAway();

        // The relay closes its end once it has handled the closing, and has settled
        // once the call it held is held again.
        await Assert.That(await background.ClosedAsync()).IsTrue();
        await rig.SettledAsync();
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var next = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);
        _ = await next.GreetAsync();

        await Assert.That((await next.NextAsync()).Text).IsEqualTo(call);
    }

    /// <summary>A client whose output cannot be written has gone, and the relay ends.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientWhoseOutputCannotBeWrittenEndsTheRelay()
    {
        await using var rig = RelayRig.Start(gated: true);
        var (background, _) = await rig.ConnectedAsync();

        rig.Gate!.Break();
        await rig.SendAsync("""{"jsonrpc":"2.0","id":"p1","method":"ping"}""");

        await Assert.That((await rig.EndedAsync()).Reason).IsEqualTo(RelayEnding.ClientWentAway);
        await Assert.That(await background.ClosedAsync()).IsTrue();
    }

    /// <summary>
    /// A finder that fails is read as no background: a failed look is looked again,
    /// and a failed explanation reads as a task nobody could read.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFinderThatFailsIsReadAsNoBackgroundAndATaskNobodyCouldRead()
    {
        await using var rig = RelayRig.Start();
        rig.Finder.FailsToLook = true;
        rig.Finder.FailsToExplain = true;

        _ = await rig.InitializeAsync();
        _ = await rig.ListAsync();
        await rig.SettledAsync();
        await Assert.That(rig.Finder.Looks).IsEqualTo(1);

        await rig.StepAsync(RelayConstants.LookWhileIdle);
        await Assert.That(rig.Finder.Looks).IsEqualTo(2);

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        await rig.StepAsync(RelayConstants.HoldBound);
        var answered = await rig.NextAsync();

        await Assert.That(answered.IdText).IsEqualTo("1");
        Match(answered.ToolText, nameof(RelayErrors.NotRunning), RelayErrors.NotRunning("browser_navigate", TaskState.Unknown, string.Empty, null));
    }
}

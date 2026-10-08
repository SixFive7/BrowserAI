// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The relay's countdown, its reports to the background, the two-phase agreement an
/// update ends it through, and the ways it ends.
/// </summary>
internal sealed partial class RelayTests
{
    /// <summary>
    /// Activity reports are at least a second apart, and the last value of a burst is
    /// always sent.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ActivityReportsAreASecondApartAndTheLastValueIsAlwaysSent()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        // Two moves inside the second after the greeting, which told the countdown.
        await rig.StepAsync(TimeSpan.FromMilliseconds(300));
        _ = await rig.ListAsync();
        await rig.StepAsync(TimeSpan.FromMilliseconds(300));
        _ = await rig.ListAsync();

        // A tick short of the second: nothing reported yet, so the background's next
        // frame is the answer to its own question.
        await rig.StepAsync(TimeSpan.FromMilliseconds(400) - OneTick);
        await background.SendAsync("""{"jsonrpc":"2.0","id":"r1","method":"browserai/ready-to-end"}""");
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("r1");

        // At the second, one report, carrying the last move.
        await rig.StepAsync(OneTick);
        await ReportedAsync(background, TimeSpan.FromMilliseconds(600) + RelayConstants.IdleCountdown);

        // A move more than a second after it is reported at once.
        await rig.StepAsync(TimeSpan.FromMilliseconds(1500));
        _ = await rig.ListAsync();
        await ReportedAsync(background, TimeSpan.FromMilliseconds(2500) + RelayConstants.IdleCountdown);
    }

    /// <summary>
    /// The relay says yes to ending for an update only with its countdown run out and
    /// nothing in flight, and says which held it back otherwise.
    /// </summary>
    /// <remarks>H1 and RESOLUTIONS 13, 2026-10-08.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ReadyToEndIsYesOnlyWithTheCountdownRunOutAndNothingInFlight()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        // The countdown is running.
        var running = await AskAsync(background, "r1");

        await Assert.That((bool?)running.Result!["ready"]).IsFalse();
        await Assert.That((bool?)running.Result!["callInFlight"]).IsFalse();
        await Assert.That(Json.Text(running.Result, "idleAt")).IsEqualTo(RelayRig.At(RelayConstants.IdleCountdown));

        // The countdown has run out, and a call is in flight.
        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");
        await KeepAnsweringAsync(rig, background, RelayConstants.IdleCountdown);

        var busy = await AskAsync(background, "r2");

        await Assert.That((bool?)busy.Result!["ready"]).IsFalse();
        await Assert.That((bool?)busy.Result!["callInFlight"]).IsTrue();

        // The call answered: yes, and nothing else said.
        await background.SendAsync("""{"jsonrpc":"2.0","id":1,"result":{"content":[]}}""");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("1");

        var ready = await AskAsync(background, "r3");

        await Assert.That((bool?)ready.Result!["ready"]).IsTrue();
        await Assert.That(Json.Text(ready.Result, "idleAt")).IsEqualTo(RelayRig.At(RelayConstants.IdleCountdown));
        await Assert.That(ready.Result!.ContainsKey("callInFlight")).IsFalse();
    }

    /// <summary>
    /// After a yes, the relay holds what its client sends, and the first message sends
    /// one withdrawal; <c>ping</c> is still answered and withdraws nothing; when the
    /// update is called off, what was held goes on in order.
    /// </summary>
    /// <remarks>
    /// RESOLUTIONS 13: a message reaching a relay that has agreed, before every relay
    /// has, is activity and calls the update off for everyone.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnAgreedRelayHoldsWhatItsClientSendsAndItsFirstMessageWithdraws()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        await rig.StepAsync(RelayConstants.IdleCountdown);
        await Assert.That((bool?)(await AskAsync(background, "r1")).Result!["ready"]).IsTrue();

        var first = RelayRig.CallFrame("1");
        await rig.SendAsync(first);

        var withdraw = await background.NextAsync();

        await Assert.That(withdraw.Method).IsEqualTo("browserai/withdraw");
        await Assert.That(Json.Text(withdraw.Params, "idleAt")).IsEqualTo(RelayRig.At(RelayConstants.IdleCountdown * 2));

        // A ping is answered and withdraws nothing; a second call and a notification
        // are held without a second withdrawal.
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        var second = RelayRig.CallFrame("2");
        await rig.SendAsync(second);
        const string Roots = """{"jsonrpc":"2.0","method":"notifications/roots/list_changed"}""";
        await rig.SendAsync(Roots);
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        // Nothing held has gone on: the background's next frame answers its question.
        var held = await AskAsync(background, "r2");

        await Assert.That((bool?)held.Result!["ready"]).IsFalse();
        await Assert.That((bool?)held.Result!["callInFlight"]).IsTrue();

        await background.SendAsync("""{"jsonrpc":"2.0","method":"browserai/called-off"}""");

        await Assert.That((await background.NextAsync()).Text).IsEqualTo(first);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(second);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(Roots);

        await background.SendAsync("""{"jsonrpc":"2.0","id":1,"result":{"content":[]}}""");
        await background.SendAsync("""{"jsonrpc":"2.0","id":2,"result":{"content":[]}}""");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("1");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("2");
    }

    /// <summary>
    /// The commit answers every request the relay held with the update sentence, closes
    /// the pipe and ends the relay for an update.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCommitAnswersWhatTheRelayHeldWithTheUpdateSentenceAndEndsIt()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync(KnownClients.ClaudeCode);
        _ = await rig.ListAsync();

        await rig.StepAsync(RelayConstants.IdleCountdown);
        await Assert.That((bool?)(await AskAsync(background, "r1")).Result!["ready"]).IsTrue();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That((await background.NextAsync()).Method).IsEqualTo("browserai/withdraw");

        await background.SendAsync("""{"jsonrpc":"2.0","method":"browserai/end","params":{"now":false,"version":"1.2.0"}}""");

        var answered = await rig.NextAsync();

        await Assert.That(answered.IdText).IsEqualTo("1");
        await Assert.That(answered.IsToolError).IsTrue();
        Match(answered.ToolText, nameof(RelayErrors.UpdateInstalling), RelayErrors.UpdateInstalling("browser_navigate", "1.2.0", KnownClients.ClaudeCode));

        await Assert.That(await background.ClosedAsync()).IsTrue();

        var end = await rig.EndedAsync();

        await Assert.That(end.Reason).IsEqualTo(RelayEnding.ForAnUpdate);
        await Assert.That(end.UpdateVersion).IsEqualTo("1.2.0");
    }

    /// <summary>A commit with <c>now</c> set cuts off a call in flight and tells it so.</summary>
    /// <remarks>
    /// A person's Install now commits while a call runs; the call may have run in part,
    /// and its sentence says to check before repeating it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACommitWithNowCutsOffACallInFlight()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync(KnownClients.Codex);
        _ = await rig.ListAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");

        await background.SendAsync("""{"jsonrpc":"2.0","method":"browserai/end","params":{"now":true,"version":"1.2.0"}}""");

        var cut = await rig.NextAsync();

        await Assert.That(cut.IdText).IsEqualTo("1");
        Match(cut.ToolText, nameof(RelayErrors.UpdateInstallingDuringTheCall), RelayErrors.UpdateInstallingDuringTheCall("browser_navigate", "1.2.0", KnownClients.Codex));

        await Assert.That(await background.ClosedAsync()).IsTrue();
        await Assert.That((await rig.EndedAsync()).Reason).IsEqualTo(RelayEnding.ForAnUpdate);
    }

    /// <summary>
    /// A call the client sent after the commit, and before the relay stopped reading,
    /// gets the update sentence too.
    /// </summary>
    /// <remarks>
    /// The arm holds the relay inside its answer to a ping, so that the commit and a
    /// call queue up behind it in that order, and then lets it go.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallQueuedBehindTheCommitGetsTheUpdateSentence()
    {
        await using var rig = RelayRig.Start(gated: true);
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        await rig.StepAsync(RelayConstants.IdleCountdown);
        await Assert.That((bool?)(await AskAsync(background, "r1")).Result!["ready"]).IsTrue();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That((await background.NextAsync()).Method).IsEqualTo("browserai/withdraw");

        rig.Gate!.Hold();
        await rig.SendAsync("""{"jsonrpc":"2.0","id":"held-ping","method":"ping"}""");
        await rig.Gate.Waiting.WaitAsync(TestDefaults.InProcessHang);

        await background.SendAsync("""{"jsonrpc":"2.0","method":"browserai/end","params":{"now":false,"version":"1.2.0"}}""");
        await rig.QueuedAsync(1);

        await rig.SendAsync(RelayRig.CallFrame("2"));
        await rig.QueuedAsync(2);

        rig.Gate.Open();

        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("held-ping");
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("1");

        var late = await rig.NextAsync();

        await Assert.That(late.IdText).IsEqualTo("2");
        Match(late.ToolText, nameof(RelayErrors.UpdateInstalling), RelayErrors.UpdateInstalling("browser_navigate", "1.2.0", "BrowserAI.RelayTests"));

        await Assert.That((await rig.EndedAsync()).Reason).IsEqualTo(RelayEnding.ForAnUpdate);
    }

    /// <summary>The client's input ending ends the relay and closes the background's pipe.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientsInputEndingEndsTheRelayAndClosesThePipe()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();

        rig.EndTheClientsInput();

        await Assert.That((await rig.EndedAsync()).Reason).IsEqualTo(RelayEnding.ClientWentAway);
        await Assert.That(await background.ClosedAsync()).IsTrue();
    }

    /// <summary>The token the relay runs under ends it and closes the background's pipe.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheTokenEndsTheRelayAndClosesThePipe()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();

        await rig.CancelAsync();

        await Assert.That((await rig.EndedAsync()).Reason).IsEqualTo(RelayEnding.Cancelled);
        await Assert.That(await background.ClosedAsync()).IsTrue();
    }

    /// <summary>Asks the relay, as the background, whether it is ready to end, and reads the answer.</summary>
    /// <param name="background">The background.</param>
    /// <param name="id">The question's id.</param>
    /// <returns>The answer, which must be the background's next frame.</returns>
    private static async Task<WireFrame> AskAsync(FakeBackground background, string id)
    {
        await background.SendAsync(Frames.ReadyToEnd(id));
        var answer = await background.NextAsync();

        return answer.IdText == id && answer.Result is not null
            ? answer
            : throw new InvalidOperationException($"The background's next frame was {answer.Text}, where the answer to {id} was due.");
    }
}

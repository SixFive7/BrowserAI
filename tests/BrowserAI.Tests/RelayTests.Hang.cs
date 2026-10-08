// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>A background that stops answering, and what the relay tells the calls it was carrying.</summary>
internal sealed partial class RelayTests
{
    /// <summary>
    /// While a call is outstanding the relay asks the background every ten seconds
    /// whether it is alive; when nothing has been answered for the hang bound, the call
    /// is answered with the hang sentence, a new call is answered at once, the probes go
    /// on, a late answer is dropped, and the first answered probe ends it.
    /// </summary>
    /// <remarks>
    /// D10 in the maintainer's version and R, 2026-10-08: 150 s, and nothing is killed or
    /// restarted.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHungBackgroundIsReportedAfterTheHangBoundAndAnsweringAProbeClearsIt()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync(KnownClients.ClaudeCode);
        _ = await rig.ListAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");

        var probes = (int)(RelayConstants.HangBound / RelayConstants.ProbeInterval);

        for (var probe = 1; probe < probes; probe++)
        {
            await rig.StepAsync(RelayConstants.ProbeInterval);
            var asked = await background.NextAsync();

            await Assert.That(asked.Method).IsEqualTo("ping");
            await Assert.That(asked.IdText).IsEqualTo($"{RelayWire.IdPrefix}ping-{probe}");
        }

        // A probe interval short of the bound, the call is still the background's.
        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        await rig.StepAsync(RelayConstants.ProbeInterval);
        var hung = await rig.NextAsync();

        await Assert.That(hung.IdText).IsEqualTo("1");
        await Assert.That(hung.IsToolError).IsTrue();
        Match(hung.ToolText, nameof(RelayErrors.Hung), RelayErrors.Hung("browser_navigate", wasPassedOn: true, RelayRig.Facts.LogPath));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo($"{RelayWire.IdPrefix}ping-{probes}");

        // While hung, a new call is answered at once and is not passed on: the
        // background hears of it as activity, and that is all.
        await rig.SendAsync(RelayRig.CallFrame("2"));
        var refused = await rig.NextAsync();

        await Assert.That(refused.IdText).IsEqualTo("2");
        Match(refused.ToolText, nameof(RelayErrors.Hung), RelayErrors.Hung("browser_navigate", wasPassedOn: false, RelayRig.Facts.LogPath));
        await ReportedAsync(background, RelayConstants.HangBound + RelayConstants.IdleCountdown);

        // The probes go on, with nothing outstanding.
        await rig.StepAsync(RelayConstants.ProbeInterval);
        var next = await background.NextAsync();

        await Assert.That(next.Method).IsEqualTo("ping");
        await Assert.That(next.IdText).IsEqualTo($"{RelayWire.IdPrefix}ping-{probes + 1}");

        // The late answer to the first call is dropped, and an answered probe ends the
        // hang. The marker after them is the first thing the client sees.
        await background.SendAsync("""{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"late"}]}}""");
        await background.SendAsync(Frames.Empty(next.IdText!));

        const string Marker = """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progressToken":"marker","progress":1}}""";
        await background.SendAsync(Marker);
        await Assert.That((await rig.NextAsync()).Text).IsEqualTo(Marker);

        var third = RelayRig.CallFrame("3");
        await rig.SendAsync(third);
        await ReportedAsync(background, RelayConstants.HangBound + RelayConstants.ProbeInterval + RelayConstants.IdleCountdown);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(third);
    }

    /// <summary>A background that answers its probes is never reported hung, however long a call runs.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABackgroundThatAnswersItsProbesIsNeverReportedHung()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync();
        _ = await rig.ListAsync();

        await rig.SendAsync(RelayRig.CallFrame("1"));
        await Assert.That((await background.NextAsync()).IdText).IsEqualTo("1");

        // Three hang bounds' worth of probes, every one answered.
        await KeepAnsweringAsync(rig, background, RelayConstants.HangBound * 3);

        await Assert.That(await rig.BarrierAsync()).IsEmpty();

        const string Answer = """{"jsonrpc":"2.0","id":1,"result":{"content":[]}}""";
        await background.SendAsync(Answer);
        await Assert.That((await rig.NextAsync()).Text).IsEqualTo(Answer);
    }

    /// <summary>Moves the clock a probe interval at a time, answering each probe the background is sent.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="background">The background.</param>
    /// <param name="span">How far.</param>
    /// <returns>The task.</returns>
    private static async Task KeepAnsweringAsync(RelayRig rig, FakeBackground background, TimeSpan span)
    {
        for (var moved = TimeSpan.Zero; moved < span; moved += RelayConstants.ProbeInterval)
        {
            await rig.StepAsync(RelayConstants.ProbeInterval);
            var probe = await background.NextAsync();

            await Assert.That(probe.Method).IsEqualTo("ping");
            await background.SendAsync(Frames.Empty(probe.IdText!));
        }
    }
}

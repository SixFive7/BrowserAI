// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Sessions;

namespace BrowserAI.Tests;

/// <summary>
/// Every bound the session host added is derived from the setting it names, and holds
/// what the reason beside it says.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer, 2026-10-03, about the idle close's cap, verbatim: <i>"I need a
/// motivation. Also, I do not like magic numbers."</i></b> Each bound Q366 b's build
/// added is a named constant derived from something measured or from another named
/// setting, with the reason in its remarks. A remark cannot hold a derivation across
/// two binaries, and two of these reach across: the server's idle close and the app's
/// tab linger are out of the library's sight, so the arms here are what keep each
/// copy equal to what it was derived from.
/// </para>
/// <para>
/// <b>Nothing here starts anything.</b> Every arm compares constants.
/// </para>
/// </remarks>
internal sealed class SessionHostBoundsTests
{
    /// <summary>
    /// A front waits for a host half of what a client gives a server to start, and the
    /// in-process start it falls back to has the other half.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFrontWaitsForAHostHalfOfWhatAClientGivesAServerToStart()
    {
        await Assert.That(SessionHostAccess.ClientStartupAllowance).IsEqualTo(TimeSpan.FromSeconds(30))
            .Because("codex-cli 0.155.0-alpha.9.2 and 0.160.0 gave a stdio server 30 s to start, measured 2026-10-03, and Claude Code 2.1.288 reads 30000 ms: a client that changes it changes this");
        await Assert.That(SessionHostAccess.StartBound * 2).IsEqualTo(SessionHostAccess.ClientStartupAllowance);
    }

    /// <summary>
    /// The host's close before an update is the idle close's own, and the coordinator's
    /// wait for the host covers the host's whole shutdown.
    /// </summary>
    /// <remarks>
    /// <b>The second assertion is the reason the remark gives, made checkable</b>: the
    /// browsers' closes, all at once, then each session's child and the tool list's own
    /// child ended through their stdin, one after the other, each given its transport's
    /// shutdown timeout. A stop bound shorter than that would end a host that was still
    /// closing cleanly.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHostsCloseIsTheIdleClosesAndTheStopCoversTheHostsWholeShutdown()
    {
        await Assert.That(SessionHostProtocol.ShutdownCloseBudget).IsEqualTo(LiveSession.IdleCloseBudget);
        await Assert.That(SessionHostProtocol.StopBound).IsEqualTo(SessionHostProtocol.ShutdownCloseBudget * 2);

        var childShutdown = new ChildProcessOptions
        {
            Command = "node.exe",
            WorkingDirectory = Environment.CurrentDirectory,
            Environment = new Dictionary<string, string>(),
        }.ShutdownTimeout;

        await Assert.That(SessionHostProtocol.StopBound).IsGreaterThanOrEqualTo(SessionHostProtocol.ShutdownCloseBudget + (childShutdown * 2))
            .Because("a stop that gave up before the browsers' closes and both children's ends would cut a clean shutdown short");
    }

    /// <summary>
    /// The host lingers as long as the coordinator does after its last tab, and looks
    /// at itself and at a detached headed session's window on one cadence.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHostLingersAsLongAsTheCoordinatorAfterItsLastTabAndLooksOnOneCadence()
    {
        await Assert.That(SessionHostServer.Linger).IsEqualTo(PageTabs.ProductLinger);
        await Assert.That(SessionHostServer.LingerLook * 4).IsEqualTo(SessionHostServer.Linger);
        await Assert.That(LiveSession.DetachedWindowLook).IsEqualTo(SessionHostServer.LingerLook);
    }

    /// <summary>The host's pipe buffers what a server's pipe does.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHostsPipeBuffersWhatAServersPipeDoes() =>
        await Assert.That(NamedPipes.StreamBufferBytes).IsEqualTo(NamedPipes.OutBufferBytes);
}

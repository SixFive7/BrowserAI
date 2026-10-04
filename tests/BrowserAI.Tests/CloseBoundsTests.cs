// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;
using BrowserAI.Sessions;

namespace BrowserAI.Tests;

/// <summary>
/// Every bound a clean close is given: one named cap, derived from what was measured,
/// and every bound that depends on it derived from it in turn.
/// </summary>
/// <remarks>
/// <para>
/// <b>D4.1 and D4.2, the maintainer's words of 2026-10-04, verbatim:</b> <i>"Make it a
/// roomy 1 min. We want everything nicely saved to disk even on a slow system."</i> and
/// <i>"Same 1 min. under option d (lane c)"</i>. And his standing words about these
/// bounds: <i>"I do not like magic numbers"</i>.
/// </para>
/// <para>
/// <b>Nothing here starts anything.</b> Every arm compares constants or reads the tree
/// as text. That each close really waits for the cap and not a number of its own is
/// held through the product, on the session's clock and to the tick:
/// <see cref="BrowserIdleTimerTests"/> for the idle close,
/// <see cref="SessionCloseTests"/> for the shutdown and the caller's own close, and
/// <see cref="CloseOrderingTests"/> for everything that waits on a close in flight.
/// The coordinator's wait for the session host, twice the cap, is held with the host's
/// other bounds in <see cref="SessionHostBoundsTests"/>.
/// </para>
/// </remarks>
internal sealed class CloseBoundsTests
{
    /// <summary>A wait on a child's exit with a duration written where it is used, split so this file cannot match itself.</summary>
    private const string InlineExitWait = "WaitForExitAsync(TimeSpan" + ".From";

    /// <summary>
    /// The cap is twice Chromium's cookie commit interval, the longest commit window
    /// measured, and it is one minute.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, through the three
    /// names the cap replaced, each held equal to it: the idle close's thirty seconds,
    /// the shutdown's one second and the session host's thirty seconds. All three went
    /// with the change, so what is left to hold is the cap and what it is derived from.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryCleanCloseTakesOneCapOfTwiceTheLongestCommitWindowMeasured()
    {
        await Assert.That(SessionTimes.ChromiumCookieCommitInterval).IsEqualTo(TimeSpan.FromSeconds(30))
            .Because("a Chromium cookie was lost in every run killed less than 30 s after the write and kept from 30 s at chromium-1246 and from 31.1 s at chromium-1247, measured 2026-10-03, and the interval is a constant in sqlite_persistent_cookie_store.cc: a Chromium that changes it changes this");
        await Assert.That(SessionTimes.BrowserCloseCap).IsEqualTo(SessionTimes.ChromiumCookieCommitInterval * 2);
        await Assert.That(SessionTimes.BrowserCloseCap).IsEqualTo(TimeSpan.FromMinutes(1))
            .Because("D4.1, the maintainer's words: \"Make it a roomy 1 min.\"");
    }

    /// <summary>
    /// A teardown waits for an idle close in flight for the cap and then the child's own
    /// shutdown timeout, and a child is given five seconds unless its launch says
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// <b>The first assertion was planted red</b> with the twenty seconds the timer's
    /// teardown wait held until 2026-10-04, a number pinned by nothing and shorter than
    /// the close it waited on. The child's five seconds did not move; it is named now
    /// and held here because the two bounds above are derived from it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATeardownWaitsOutTheCapAndTheChildsOwnEnd()
    {
        await Assert.That(BrowserIdleTimer.CloseBudget).IsEqualTo(SessionTimes.BrowserCloseCap + ChildProcessOptions.DefaultShutdownTimeout);

        await Assert.That(ChildProcessOptions.DefaultShutdownTimeout).IsEqualTo(TimeSpan.FromSeconds(5))
            .Because("measured 2026-10-03 at @playwright/mcp 0.0.82, the slowest a child took to exit after its stdin closed was 1.5 s, parked on a debugger pause, and five seconds is the hang detector over it");

        var launch = new ChildProcessOptions
        {
            Command = "node.exe",
            WorkingDirectory = Environment.CurrentDirectory,
            Environment = new Dictionary<string, string>(),
        };

        await Assert.That(launch.ShutdownTimeout).IsEqualTo(ChildProcessOptions.DefaultShutdownTimeout);
    }

    /// <summary>
    /// A child given its stdin's end and then its job's close is waited on for the
    /// child's own shutdown timeout both times, and neither wait is a number of its own.
    /// </summary>
    /// <remarks>
    /// <b>Read as text</b>, because the wait after the job closes is inside a private
    /// method of the transport, and until 2026-10-04 the number it used was a literal
    /// nothing pinned and nothing derived. Planted red against that literal.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryWaitOnAChildsExitIsTheChildsOwnShutdownTimeout()
    {
        var file = RepositoryLayout.ProductSourceFiles.Single(candidate => candidate.Name is "ChildProcessSession.cs");
        var code = await RepositoryLayout.ReadCodeAsync(file);

        await Assert.That(code).DoesNotContain(InlineExitWait)
            .Because("every wait on the child's exit is the child's own shutdown timeout, which ChildProcessOptions names");
        await Assert.That(code).Contains("WaitForExitAsync(_shutdownTimeout)");

        // The positive control, the line as it stood until that day: the needle finds it.
        await Assert.That("        _ = await _process.WaitForExitAsync(TimeSpan" + ".FromSeconds(5)).ConfigureAwait(false);")
            .Contains(InlineExitWait);
    }
}

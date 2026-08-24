// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The run's own statement of whether it could have seen a browser take the
/// foreground — which on this machine it could not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here launches anything and nothing here touches the
/// foreground.</b> It is one <c>SystemParametersInfoW</c> read and the sentence
/// the coverage block prints about it. A test that provoked a real focus steal
/// would have to put a browser window over whoever is at the machine, which is
/// the thing the windowless rig exists to prevent, and it would still prove
/// nothing here for the reason this file exists.
/// </para>
/// <para>
/// ⚠️ <b>It reports and it does not repair.</b> <c>SPI_SETFOREGROUNDLOCKTIMEOUT</c>
/// appears nowhere: the timeout is a machine-wide user preference, and a suite
/// that wrote to it to make itself informative would be editing the developer's
/// desktop and invalidating every <c>[MACHINE]</c> measurement already recorded
/// against this machine.
/// </para>
/// <para>
/// <b>Why the pure arm carries most of the weight.</b> A machine sits in exactly
/// one band and this one sits in the band that proves nothing, so a check written
/// only against the live reading would leave three quarters of the classification
/// as code no run ever takes — the same dead-mechanism defect as a release branch
/// that first runs on release day.
/// </para>
/// </remarks>
internal sealed class ForegroundLockTests
{
    /// <summary>
    /// The verdict is a function of the timeout and the budget, and every band
    /// is reachable from any machine.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheVerdictComesFromTheTimeoutAndTheBudgetRatherThanFromThisMachine()
    {
        var budget = ForegroundLock.Budget;

        // Windows would not answer. Distinct from every other state, because
        // "the call failed" and "the lock is off" are opposite conclusions and a
        // zero default would quietly turn one into the other.
        await Assert.That(ForegroundLock.Classify(null, budget)).IsEqualTo(ForegroundLockVerdict.Unreadable);

        // The lock is off: Windows never refuses a foreground change on this
        // ground, so a steal is visible the moment it happens.
        await Assert.That(ForegroundLock.Classify(TimeSpan.Zero, budget)).IsEqualTo(ForegroundLockVerdict.Unlocked);

        // Anything that expires inside the budget an experiment here may take.
        await Assert.That(ForegroundLock.Classify(TimeSpan.FromTicks(1), budget)).IsEqualTo(ForegroundLockVerdict.Waitable);
        await Assert.That(ForegroundLock.Classify(budget, budget)).IsEqualTo(ForegroundLockVerdict.Waitable);

        // ⚠️ THE BOUNDARY, one tick either side of it. A band whose edge is not
        // pinned is a band that can be moved by a refactor and nothing says so.
        await Assert.That(ForegroundLock.Classify(budget + TimeSpan.FromTicks(1), budget)).IsEqualTo(ForegroundLockVerdict.Blind);

        // And this machine's own value, as a constant rather than as a reading,
        // so the band it lands in is asserted on every machine that runs this.
        await Assert.That(ForegroundLock.Classify(TimeSpan.FromMilliseconds(int.MaxValue), budget)).IsEqualTo(ForegroundLockVerdict.Blind);

        // The budget derives rather than being written here: a number chosen at
        // this line is exactly what the house rule on durations forbids, and it
        // would put the band edge somewhere nobody could find it from the code.
        await Assert.That(ForegroundLock.Budget).IsEqualTo(TestDefaults.BrowserHang);
    }

    /// <summary>
    /// Every run says which question it answered about focus, and says it with
    /// the number it read.
    /// </summary>
    /// <remarks>
    /// <b>This is the arm that makes a green run honest.</b> The suite has never
    /// been able to observe a focus steal on this machine and every run of it
    /// reported exactly what a run on a machine that could would report. The row
    /// is the difference.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRunSaysWhetherItCouldHaveSeenAFocusSteal()
    {
        var reading = ForegroundLock.Reading;
        var verdict = ForegroundLock.Classify(reading.Timeout, ForegroundLock.Budget);
        var summary = SuiteEnvironment.Summary();

        // The live call is exercised rather than assumed: either Windows
        // answered, or it did not and the error is carried instead of being
        // flattened into a zero that reads as "the lock is off".
        if (reading.Timeout is { } timeout)
        {
            await Assert.That(verdict).IsNotEqualTo(ForegroundLockVerdict.Unreadable);
            await Assert.That(summary).Contains(timeout.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms");
        }
        else
        {
            await Assert.That(verdict).IsEqualTo(ForegroundLockVerdict.Unreadable);
            await Assert.That(reading.Error).IsNotEqualTo(0);
            await Assert.That(summary).Contains(reading.Error.ToString(CultureInfo.InvariantCulture));
        }

        // The row is in the block every run prints, it names the setting it
        // read, and it says which of the four things this run may claim.
        await Assert.That(summary).Contains(ForegroundLock.Title);
        await Assert.That(summary).Contains("SPI_GETFOREGROUNDLOCKTIMEOUT");
        await Assert.That(summary).Contains(ForegroundLock.StateWord(verdict).Trim());
        await Assert.That(summary).Contains(ForegroundLock.RowFor(reading));
    }

    /// <summary>
    /// A run that could not have seen a steal says so, rather than printing a
    /// number and leaving the reader to draw the wrong conclusion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted over a synthetic reading, because the property has to hold on
    /// a machine that is not this one.</b> This machine happens to be blind, so
    /// the live row would satisfy this today and stop asserting anything the
    /// moment somebody ran the suite anywhere else — a check that is green when
    /// it is watching nothing.
    /// </para>
    /// <para>
    /// <b>And the other direction, which is the one that rots quietly:</b> a
    /// machine that <i>can</i> see a steal must not be told it did not answer.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheBlindRowSaysWhatTheRunDidNotAnswerAndWhyANullTrialLooksLikeAPass()
    {
        // This machine's own value, which no experiment can wait out.
        var blind = ForegroundLock.RowFor(new ForegroundLockReading(TimeSpan.FromMilliseconds(int.MaxValue), 0));

        await Assert.That(blind).Contains(ForegroundLock.BlindState);
        await Assert.That(blind).Contains("2147483647 ms");

        // ⚠️ 24.9 and not 24.8, and the difference is a rounding rather than a
        // disagreement: 2,147,483,647 ms is 24.855 days, which every record in
        // this repository truncates to "about 24.8 days" and this row rounds to
        // one decimal. Asserted so that whoever notices the two spellings finds
        // the reason here instead of chasing a drift that is not one.
        await Assert.That(blind).Contains("24.9 days");
        await Assert.That(blind).Contains("DID NOT ANSWER");

        // The exception that fires here, named — without it a reader has a
        // number and no way to tell a null trial from a clean one.
        await Assert.That(blind).Contains("ancestor of the launching process");

        // And where the rest of it is written down, because the row is one line
        // and the hazard is a page.
        await Assert.That(blind).Contains("HAZARDS.md");

        // ⚠️ The other direction. A machine whose lock is off is answered by
        // the same run, and it must not carry the warning: a block that said
        // "did not answer" whatever it read would be the same false assurance
        // pointing the other way.
        var seeing = ForegroundLock.RowFor(new ForegroundLockReading(TimeSpan.Zero, 0));

        await Assert.That(seeing).DoesNotContain("DID NOT ANSWER");
        await Assert.That(seeing).DoesNotContain(ForegroundLock.BlindState);
        await Assert.That(seeing).Contains("0 ms");

        // A reading Windows refused, which is neither of the two above and must
        // not be reported as either.
        var unread = ForegroundLock.RowFor(new ForegroundLockReading(null, 5));

        await Assert.That(unread).Contains("failed with Win32 error 5");
        await Assert.That(unread).DoesNotContain(ForegroundLock.BlindState);
    }
}

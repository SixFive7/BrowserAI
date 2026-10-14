// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The suite's hand-moved clock in the orders a step can meet code that arms its timers on a
/// thread of its own: a timer disarmed after the clock chose it, a timer armed from a reading
/// the step had already left behind, and a timer re-armed after the clock chose it.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-14 with the maintainer's 170 a</b>, in his words verbatim, <i>"170 a"</i>.
/// The relay's rig met the straddle of two readings in the gate at <c>e092de31</c>, and a
/// reading of <see cref="ManualClock"/> beside that fix found the clock itself answering each
/// of these orders wrongly. Each arm forces its order on one thread through
/// <see cref="ManualClock.AtTheNextChoice"/>, so nothing here depends on timing.
/// </remarks>
internal sealed class ManualClockTests
{
    private static readonly long Second = TimeSpan.FromSeconds(1).Ticks;

    /// <summary>
    /// A timer disarmed after the clock chose it, and before the clock moved to its due, ends
    /// the step without an error: it is fired once, as a real timer's tick already handed to
    /// the thread pool is, and not at all when it was disposed there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order</b>: a timer's <c>Change</c> and <c>Dispose</c> take the timer's own lock
    /// and not the clock's, so a thread of the code under test can disarm a timer between the
    /// clock's choice of it and the clock's move to its due. Here the chosen timer is disarmed
    /// at that moment, and then the next one chosen is disposed there.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-14</b> at the commit that added the arm, against the clock as it
    /// was, which read the chosen timer's due a second time to move the clock: the first step
    /// ended with <c>InvalidOperationException</c>, <i>"Nullable object must have a value."</i>,
    /// out of <see cref="ManualClock.FireTheNextDueBy"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATimerDisarmedAfterTheClockChoseItEndsTheStepWithoutAnError()
    {
        var clock = new ManualClock();
        var fired = new List<string>();

        using var disarmed = clock.CreateTimer(_ => fired.Add("disarmed"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var disposed = clock.CreateTimer(_ => fired.Add("disposed"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        clock.AtTheNextChoice(static chosen => _ = chosen.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));
        clock.Advance(TimeSpan.FromMilliseconds(1500));

        await Assert.That(clock.GetTimestamp()).IsEqualTo(Second * 3 / 2);
        await Assert.That(string.Join(", ", fired)).IsEqualTo("disarmed");

        clock.AtTheNextChoice(static chosen => chosen.Dispose());
        clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(clock.GetTimestamp()).IsEqualTo(Second * 5 / 2);
        await Assert.That(string.Join(", ", fired)).IsEqualTo("disarmed").Because("a disposed timer of this clock never fires");
    }

    /// <summary>
    /// A timer armed during a step from a reading the step had already left behind fires at the
    /// clock's own reading, late, and the clock never reads earlier than it already has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order</b>: a timer's <c>Change</c> reads the clock before it takes its own lock
    /// and writes the due after it, so a thread of the code under test that arms a timer while
    /// a step goes on can write a due the step has already passed, as the relay can (its row in
    /// the hazard index). Here a five-second step chooses a timer due at one second, and at that
    /// moment a second timer is armed for half a second from the reading the step started at.
    /// A real clock never reads earlier than it has, and a real timer whose moment has passed
    /// fires at once.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-14</b> at the commit that added the arm, against the clock as it
    /// was, which moved itself to the due of the timer it fired: the timer armed during the step
    /// read the clock at tick 5000000, after the first had read it at tick 10000000.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATimerArmedFromAReadingAStepHadLeftFiresLateAndTheClockNeverMovesBack()
    {
        var clock = new ManualClock();
        var readings = new List<string>();

        using var late = clock.CreateTimer(_ => readings.Add("late at " + Ticks(clock.GetTimestamp())), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        using var first = clock.CreateTimer(_ => readings.Add("first at " + Ticks(clock.GetTimestamp())), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.AtTheNextChoice(chosen => _ = late.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan));
        clock.Advance(TimeSpan.FromSeconds(5));

        await Assert.That(string.Join(", ", readings)).IsEqualTo($"first at {Ticks(Second)}, late at {Ticks(Second)}");
        await Assert.That(clock.GetTimestamp()).IsEqualTo(Second * 5);
    }

    /// <summary>
    /// A timer re-armed after the clock chose it runs the tick that was chosen and fires again
    /// at the due the re-arm set, as a real timer runs a tick already handed to the thread pool
    /// and keeps the due a later <c>Change</c> gave it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order</b>: a thread of the code under test re-arms a timer between the clock's
    /// choice of it and its fire. Here a five-second step chooses a timer due at one second, and
    /// at that moment the timer is armed again for two seconds, from the reading the step started
    /// at.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-14</b> at the commit that added the arm, against the clock as it
    /// was: the timer fired once, at tick 20000000, because the clock read the chosen timer's due
    /// again after the re-arm and moved to it, and the tick it had chosen at one second never ran.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATimerReArmedAfterTheClockChoseItRunsTheChosenTickAndFiresAgainAtItsNewDue()
    {
        var clock = new ManualClock();
        var fired = new List<string>();

        using var timer = clock.CreateTimer(_ => fired.Add(Ticks(clock.GetTimestamp())), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.AtTheNextChoice(static chosen => _ = chosen.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan));
        clock.Advance(TimeSpan.FromSeconds(5));

        await Assert.That(string.Join(", ", fired)).IsEqualTo($"{Ticks(Second)}, {Ticks(Second * 2)}");
        await Assert.That(clock.GetTimestamp()).IsEqualTo(Second * 5);
    }

    private static string Ticks(long ticks) => ticks.ToString(CultureInfo.InvariantCulture);
}

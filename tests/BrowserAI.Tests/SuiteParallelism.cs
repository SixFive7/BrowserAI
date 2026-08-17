// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<BrowserAI.Tests.SuiteParallelism>]

namespace BrowserAI.Tests;

/// <summary>
/// How many tests this suite runs at once, and <b>what is blocking it from
/// running all of them</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-08-17 (previously <c>4</c>, "chosen because it was
/// measured … the resource being contended is not CPU, it is the machine's
/// capacity to start browsers").</b> That cap was added against a 1-in-7 mass
/// failure whose cause was <i>never established</i> — its own note said so,
/// twice. It was a workaround, and it was hiding four defects. This one is not
/// the same kind of number: it is a value the suite is <b>demonstrated</b> to
/// pass at, sitting beside a named list of what has to be fixed before it can
/// be removed.
/// </para>
/// <para>
/// ⚠️⚠️ <b>THE SUITE IS INTERMITTENTLY RED AT THIS SETTING, AND THAT IS THE
/// SETTING. DO NOT "FIX" IT BY CAPPING IT.</b> The maintainer's ruling,
/// 2026-08-17, verbatim: <i>"Keep unbounded regardless — It is also a test for
/// race conditions and interferences. It is not only there for speed."</i>
/// Running every test at once is a <b>race detector</b>, and the fixture that
/// only ever runs four-wide finds nothing. The intermittency is a symptom of
/// timing assumptions that are being removed next — it is not evidence that this
/// number is wrong, and the last person who read a red run as evidence about
/// the number set <c>Limit => 4</c> and hid four defects behind it for a day.
/// </para>
/// <para>
/// <b>Unbounded was tried, measured, and is not yet quiet. That is the point.</b>
/// Twenty runs with the limiter above the test count went red eleven times, and
/// every single failure was a defect four-way parallelism had been hiding rather
/// than a limit being exceeded. All four are fixed:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>A sixty-minute product hang.</b> <c>browserai_reinstall_browser</c>
/// deletes the browser tree, then provisions; provisioning that could not take
/// the machine-wide mutex concluded somebody else was downloading and watched
/// for a marker nobody was going to write. Fixed in <c>BrowserProvisioner</c>.
/// </description></item>
/// <item><description>
/// <b>Two wall-clock assertions over an async pipeline</b> — one asserting a
/// <i>rate</i>, one a stopwatch against a budget already raised once. The first
/// is now driven by a <c>ManualClock</c> through a
/// <see cref="TimeProvider"/> seam on the product; the second is deleted,
/// because what it claimed to defend is asserted directly three ways.
/// </description></item>
/// <item><description>
/// <b>A whole-conversation deadline wearing a per-exchange name</b> in
/// <c>RawPipeClient</c>.
/// </description></item>
/// <item><description>
/// <b>Two publish-by-rename commits refused by a scanner's handle</b>
/// ([kb](../../kb/windows/processes.md#saturation-the-100-process-design-point)).
/// </description></item>
/// </list>
/// <para>
/// <b>THE BLOCKER, and it is the reason this is not <see cref="Unbounded"/>
/// today.</b> With those four fixed, what remains is a class rather than a bug:
/// <b>fixed patience bounds that are promptness assertions in disguise</b>. They
/// do not fail because anything is wrong; they fail because the machine is busy,
/// and every one of them reports something other than "this machine is busy".
/// Measured at unbounded after the four fixes, with the saturation test already
/// exclusive:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>BrowserContainmentTests</c>' 180 s <c>ReportPatience</c>: a real Firefox
/// launcher did not finish inside it, at 3m01s, with <b>zero bytes</b> on the
/// child's stdout and stderr — so nothing had gone wrong, it had not got that
/// far. One run in four.
/// </description></item>
/// <item><description>
/// <c>FirefoxTests</c>' launch budget, which was <i>equal</i> to Playwright's own
/// launch timeout and therefore always won the race, replacing upstream's
/// diagnosis with <i>"the budget expired"</i>. Corrected there, but the shape is
/// the same one.
/// </description></item>
/// <item><description>
/// <c>TestDefaults.Patience</c>, thirty seconds, applied to in-process
/// frames and to rig teardown. Under a saturated machine a thirty-second silence
/// between two objects <i>in the same process</i> stops meaning "deadlock".
/// </description></item>
/// </list>
/// <para>
/// <b>The maintainer's instruction is the fix, and it is not this change's
/// work:</b> <i>remove any timings other than timeouts that catch really hung
/// processes, even on slow systems</i>. That is the next task. Every bound
/// listed above is a <i>promptness</i> assertion wearing a timeout's clothes,
/// and each one should either become a genuine hang detector or go.
/// </para>
/// <para>
/// <b>THE MEASURED FALLBACK, recorded so nobody has to rediscover it.</b> If a
/// green gate is needed before the timing work lands, <see cref="Demonstrated"/>
/// is the number, and it is a measurement rather than a guess:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Limit 16: 13 consecutive green runs</b>, 419 tests, 0 failed, 0 skipped,
/// 76.5–104 s. The streak was stopped at thirteen because the machine was needed,
/// not because it broke.
/// </description></item>
/// <item><description>
/// <b>Unbounded: 3 green then 1 red</b> in the same session, on the same tree —
/// <c>BrowserContainmentTests.AFirefoxTreeIsContainedAndItsProfileDeletesCleanly</c>,
/// whose launcher did not finish inside its fixed 180 s bound and had written
/// <b>zero bytes</b> to either stream, so nothing had gone wrong; it had not got
/// that far.
/// </description></item>
/// </list>
/// <para>
/// Flipping <c>Limit</c> to <see cref="Demonstrated"/> is a one-line, informed
/// choice. Flipping it because a run went red is the mistake this whole comment
/// exists to prevent.
/// </para>
/// <para>
/// <b>Wall clock, and what is actually in it.</b> Before this work:
/// <b>33.7 s</b> at four-wide, with no saturation test. Now: <b>76.5–107 s</b>.
/// The two are not comparable as a parallelism measurement, because
/// <c>SaturationTests</c> is new, is <c>[NotInParallel]</c>, and takes the
/// machine to itself for ~80–96 s of that. The parallelism change on its own,
/// measured before the saturation test existed, was <b>33.7 s → ~20 s</b>.
/// </para>
/// </remarks>
internal sealed class SuiteParallelism : IParallelLimit
{
    /// <summary>
    /// Above the total test count, so the scheduler's semaphore never blocks:
    /// every test at once, which is what makes this a race detector.
    /// </summary>
    /// <remarks>
    /// A number rather than <see cref="int.MaxValue"/> because the limit becomes
    /// a <see cref="SemaphoreSlim"/>'s initial count, and a suite that grows past
    /// this should meet a number somebody chose rather than an overflow.
    /// </remarks>
    public const int Unbounded = 1024;

    /// <summary>
    /// Sixteen: the measured fallback, <b>not the default</b>.
    /// </summary>
    /// <remarks>
    /// Thirteen consecutive green runs, against three-green-then-red at
    /// <see cref="Unbounded"/> on the same tree. Kept as a named constant so that
    /// anyone who needs a green gate before the timing work lands can make an
    /// informed one-line change instead of rediscovering the number — and so
    /// that <see cref="Limit"/> below is visibly a <i>choice</i> rather than the
    /// only value anyone measured.
    /// </remarks>
    public const int Demonstrated = 16;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="Unbounded"/>, deliberately, including the days it is red. See
    /// the type's remarks before changing this line.
    /// </remarks>
    public int Limit => Unbounded;
}

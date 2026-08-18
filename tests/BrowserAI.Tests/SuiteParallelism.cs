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
/// ⚠️⚠️ <b>DO NOT "FIX" A RED RUN BY CAPPING THIS.</b> The maintainer's ruling,
/// 2026-08-17, verbatim: <i>"Keep unbounded regardless — It is also a test for
/// race conditions and interferences. It is not only there for speed."</i>
/// Running every test at once is a <b>race detector</b>, and the fixture that
/// only ever runs four-wide finds nothing. The last person who read a red run as
/// evidence about this number set <c>Limit => 4</c> and hid four defects behind
/// it for a day.
/// </para>
/// <para>
/// ✅ <b>Unbounded is quiet as of 2026-08-18: 20 consecutive green runs</b>, 419
/// tests, 0 failed, 0 skipped, <b>72–142 s</b> each — and that streak ran while
/// three other agents were building and testing on the same machine, which is
/// why the wall clock moves by a factor of two across it and why it is the
/// number worth quoting. It was red <b>11 runs in 20</b> the day before.
/// Nothing about the limit changed; what changed is that every <i>promptness
/// assertion</i> in the suite was deleted or made event-driven, and every
/// surviving duration was given headroom a starved machine cannot reach — the
/// maintainer's instruction, verbatim: <i>"Remove any timings other than
/// timeouts that catch really hung processes. Even on slow systems."</i> The
/// vocabulary those bounds now come from is <c>TestDefaults</c>, and its remarks
/// are where a reader should start.
/// </para>
/// <para>
/// ⚠️ <b>It took eight streaks and about 120 runs, and the failures along the
/// way are a better argument for this line than the green streak at the end.</b>
/// <b>Not one of them was a duration.</b> Each was a real defect that four-way
/// parallelism had never surfaced: a rename refused <c>ERROR_ACCESS_DENIED</c> on
/// a destination the test had just released; the <i>read</i> side of the same
/// delete-pending window, throwing out of <c>SessionLock.ReadRecord</c> past
/// every handler on the path, at the entry point that opens a session; the
/// discovery that <c>MoveFileEx</c> leaves the destination name transiently
/// unbound, so an owned session can read as unowned; and a two-second retry
/// budget in shipped code that a starved process exhausted in three attempts
/// ([kb](../../kb/windows/processes.md#files-durable-writes-and-deletes)).
/// </para>
/// <para>
/// <b>Unbounded was tried and measured before that work, and it was not quiet.</b>
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
/// <b>WHAT THE BLOCKER WAS, and what closed it on 2026-08-18.</b> With those
/// four fixed, what remained was a class rather than a bug: <b>fixed patience
/// bounds that were promptness assertions in disguise</b>. They did not fail
/// because anything was wrong; they failed because the machine was busy, and
/// every one of them reported something other than "this machine is busy". The
/// three that were named here — <c>BrowserContainmentTests</c>' 180 s
/// <c>ReportPatience</c>, <c>FirefoxTests</c>' launch budget, and
/// <c>TestDefaults.Patience</c> at thirty seconds — are all gone, and so are the
/// twenty-odd that were not named. The whole failure population at unbounded was
/// three messages: <c>Initialization timed out</c> (46), <c>No frame arrived on
/// this pipe within 30 s</c> (71) and a bare <c>A task was canceled</c> (48).
/// </para>
/// <para>
/// The fix had four parts, each recorded where the code is: a single named
/// vocabulary of hang detectors in <c>TestDefaults</c>, sized so a
/// 25×-overcommitted machine cannot reach them; the SDK's <b>60 s</b>
/// <c>InitializationTimeout</c>, which nobody had chosen and which the product
/// inherited silently, set explicitly on both sides; the last
/// whole-conversation budget (<see cref="Harness.RawStdioClient"/>) split per
/// exchange; and seven wall-clock assertions deleted in favour of the event each
/// was standing in for.
/// </para>
/// <para>
/// <b>THE MEASURED FALLBACK, kept because it is a measurement.</b>
/// <see cref="Demonstrated"/> is not needed today and is not the default; it is
/// the number to reach for if this suite ever has to go green on a machine
/// smaller than the one these runs were made on, and it exists so that choice is
/// a one-line informed change rather than a rediscovery:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Limit 16: 13 consecutive green runs</b>, 419 tests, 0 failed, 0 skipped,
/// 76.5–104 s. The streak was stopped at thirteen because the machine was needed,
/// not because it broke.
/// </description></item>
/// <item><description>
/// <b>Unbounded, before the timing work: 11 red in 20.</b>
/// <b>Unbounded, after it: 20 green in 20.</b>
/// </description></item>
/// </list>
/// <para>
/// Flipping <c>Limit</c> to <see cref="Demonstrated"/> is a one-line, informed
/// choice. Flipping it because a run went red is the mistake this whole comment
/// exists to prevent — and it is now also the wrong diagnosis, because the class
/// of failure that made unbounded red has been removed rather than accommodated.
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
    /// <para>
    /// Thirteen consecutive green runs, against eleven-red-in-twenty at
    /// <see cref="Unbounded"/> on the same tree. Kept as a named constant so that
    /// <see cref="Limit"/> below is visibly a <i>choice</i> rather than the only
    /// value anyone measured.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is no longer the fallback it was written as, and it is kept
    /// anyway — deliberately.</b> The reason it existed was <i>"if a green gate
    /// is needed before the timing work lands"</i>, and that work landed on
    /// 2026-08-18. Deleting it then was tempting and would have been wrong twice
    /// over. It is a <b>measurement</b>, and this repository does not delete
    /// measurements because the situation they were taken in has passed; and it
    /// is the only recorded answer to a question that will be asked again the
    /// first time this suite meets a machine smaller than the 32-core one every
    /// number here was taken on. What has changed is its <i>meaning</i>: it is no
    /// longer a way to avoid a defect, because the defect is fixed. It is a
    /// capacity figure. Somebody choosing it now is saying "this machine cannot
    /// carry 419 at once", which is a true thing to say about some machines and
    /// was never what this constant meant before.
    /// </para>
    /// </remarks>
    public const int Demonstrated = 16;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="Unbounded"/>, deliberately, including the days it is red. See
    /// the type's remarks before changing this line.
    /// </remarks>
    public int Limit => Unbounded;
}

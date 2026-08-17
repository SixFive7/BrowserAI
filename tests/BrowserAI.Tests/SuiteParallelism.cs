// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<BrowserAI.Tests.SuiteParallelism>]

namespace BrowserAI.Tests;

/// <summary>
/// How many tests this suite runs at once: <b>every one of them</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-08-17 (previously <c>4</c>, "chosen because it was
/// measured … the resource being contended is not CPU, it is the machine's
/// capacity to start browsers").</b> That cap was added on 2026-08-16 against a
/// <b>1-in-7 mass failure</b> whose cause was never established — the note it
/// carried said as much, twice, and recorded that a previous explanation
/// (Defender) had been assumed rather than verified. It was a workaround, and it
/// held for one day.
/// </para>
/// <para>
/// <b>Unbounded means above the test count, so the scheduler's semaphore never
/// blocks.</b> Removing the attribute entirely would not do it: TUnit's default
/// limiter is <see cref="Environment.ProcessorCount"/>, which is 32 here and is
/// still a cap.
/// </para>
/// <para>
/// <b>What twenty runs at unbounded actually found, and it was not
/// parallelism.</b> Eleven of twenty went red. Every failure was a defect that
/// four-way parallelism had been hiding, and all of them are now fixed at the
/// mechanism:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>A sixty-minute product hang.</b> <c>browserai_reinstall_browser</c>
/// deletes the browser tree, then provisions; provisioning that could not take
/// the machine-wide mutex concluded that somebody else was downloading and
/// watched for a marker nobody was going to write. The holder was past its
/// download, in a prune that walks every process on the machine. Found here,
/// reachable in production, fixed in <c>BrowserProvisioner</c>.
/// </description></item>
/// <item><description>
/// <b>Two wall-clock assertions over an async pipeline.</b> The idle timer's
/// driving test asserted a <i>rate</i>; a rig test asserted a stopwatch against
/// a ten-second budget that had already been raised once from two. Neither can
/// tell a hang from a descheduled continuation. The first is now driven by a
/// <c>ManualClock</c> through a <see cref="TimeProvider"/> seam on the product;
/// the second is deleted, because what it claimed to defend is asserted
/// directly three ways.
/// </description></item>
/// <item><description>
/// <b>A whole-conversation deadline wearing a per-exchange name.</b>
/// <c>RawPipeClient</c> armed one thirty-second token in its constructor and
/// used it for every frame for the life of the client, so a long conversation of
/// prompt exchanges died on its fortieth — as a bare <i>"The operation was
/// canceled."</i> with no method and no elapsed time.
/// </description></item>
/// <item><description>
/// <b>Two publish-by-rename commits refused by a scanner's handle.</b> Windows
/// will not rename a directory while anything below it is open, and a real-time
/// scanner opens every file just after it is written
/// ([kb](../../kb/windows/processes.md#saturation-the-100-process-design-point)).
/// </description></item>
/// </list>
/// <para>
/// <b>The one hypothesis that was tested and rejected is worth keeping.</b> The
/// multi-second latencies on <i>in-process</i> round trips looked like the
/// thread pool's hill-climbing injection, which adds roughly one thread per
/// 500 ms above <see cref="Environment.ProcessorCount"/>. It is not:
/// <c>ThreadPool.SetMinThreads(1024, 1024)</c> made the same measurement
/// <b>worse</b> — 2.27 s where it had been 1.51 s — so the floor was measured,
/// rejected and not kept. The cause is plain oversubscription, and a round trip
/// through the in-process rig is four thread handoffs.
/// </para>
/// <para>
/// <b>The cost, both ways, because the number moved twice.</b> At the cap:
/// <b>33.7 s</b>. Unbounded, with the defects fixed: <b>~20 s</b> over ten runs.
/// Unbounded with <c>SaturationTests</c> added — a hundred concurrent BrowserAI
/// processes, which is the charter's design point and which nothing exercised
/// before: <b>~105 s</b>, of which 96 s is that one test.
/// </para>
/// <para>
/// <b>Why there is no ceiling here at all, rather than a large one.</b> Every
/// failure at unbounded was a defect in something being asserted, not a limit
/// being exceeded; the only measured ceiling this work found is on
/// <c>SaturationTests</c>' own browser count, and it is recorded on that
/// constant with the evidence. A number here would be a place for the next
/// unexplained failure to hide.
/// </para>
/// </remarks>
internal sealed class SuiteParallelism : IParallelLimit
{
    /// <summary>
    /// Above the total test count, so the scheduler's semaphore never blocks.
    /// </summary>
    /// <remarks>
    /// A number rather than <see cref="int.MaxValue"/> because the limit becomes
    /// a <see cref="SemaphoreSlim"/>'s initial count, and a suite that grows past
    /// this should meet a number somebody chose rather than an overflow.
    /// </remarks>
    public const int Unbounded = 1024;

    /// <inheritdoc />
    public int Limit => Unbounded;
}

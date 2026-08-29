// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace BrowserAI.Tests.Harness;

/// <summary>What a commit-charge reading amounts to for a run's credibility.</summary>
internal enum CommitChargeVerdict
{
    /// <summary>Windows would not answer at either end of the run.</summary>
    Unreadable,

    /// <summary>Room to spare: a stall on this machine is not a commit stall.</summary>
    Healthy,

    /// <summary>Close enough to the limit that a large allocation could fail.</summary>
    Tight,

    /// <summary>At or past the band where allocations are refused outright.</summary>
    Critical,
}

/// <summary>One reading of the machine's commit charge.</summary>
/// <param name="Committed">Bytes committed system-wide, or <see langword="null"/> when unreadable.</param>
/// <param name="Limit">The commit limit in bytes, or <see langword="null"/> when unreadable.</param>
internal readonly record struct CommitChargeReading(ulong? Committed, ulong? Limit)
{
    /// <summary>A reading nobody has taken.</summary>
    public static CommitChargeReading NotTaken => new(null, null);

    /// <summary>Whether Windows answered.</summary>
    public bool Answered => Committed is not null && Limit is { } limit && limit is not 0;

    /// <summary>How full the commit is, as a fraction, or <see langword="null"/>.</summary>
    public double? Fraction => Answered ? (double)Committed!.Value / Limit!.Value : null;

    /// <summary>Takes a reading now.</summary>
    /// <returns>What Windows said, or an unreadable reading.</returns>
    public static CommitChargeReading Take() =>
        MachineLoad.ReadCommitCharge() is { } pair ? new(pair.Committed, pair.Limit) : NotTaken;
}

/// <summary>
/// The machine's commit charge at the start and the end of the run, in the
/// coverage block.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This row exists because a closed hazard row asks for it by name, and no
/// run recorded it.</b> The 2026-08-24 closure of the six-run-gate row in
/// [HAZARDS.md](../../../HAZARDS.md#hazard-index) attributed three timed-out
/// hang detectors to a kernel-level memory leak outside this repository —
/// <b>137.4 GB committed of a 157.7 GB limit</b> at the worst of it — and it
/// closed by naming the one reading that would tell that cause from a live one:
/// <i>"the commit charge beside the run"</i>. It then named that reading in a
/// document and nothing took it, so every gate run since has been unable to
/// answer the question its own hazard row asks. On 2026-08-29 the same shape
/// recurred and the reading did not exist for it either.
/// </para>
/// <para>
/// <b>Two readings rather than one, and the pair is the point.</b> A single
/// number taken at the end says what the machine looked like once the run had
/// released everything; a single number at the start says nothing about what the
/// run itself did. The difference between them is what separates <i>the machine
/// was already loaded</i> from <i>this suite loaded it</i>, and those are
/// different findings with different owners.
/// </para>
/// <para>
/// <b>No assertion may read the numbers, for
/// <see cref="MachineLoad"/>'s reason exactly</b> — they are properties of
/// whatever else the machine is running, so a bound on one would be a test that
/// passes or fails depending on the developer's other windows. What
/// <see cref="SuiteCoverageTests"/> asserts is that the row is produced, that its
/// bands are classified correctly, and that an unreadable reading says so rather
/// than printing a zero.
/// </para>
/// <para>
/// <b>It is a row and not a <see cref="SuiteCapability"/></b>, for the reason
/// <see cref="ForegroundLock"/> states about itself: every capability names a
/// command that would make it PRESENT, and nothing anybody types makes a
/// machine's commit charge healthy.
/// </para>
/// </remarks>
internal static class CommitCharge
{
    /// <summary>The block's row title, matching the width the others use.</summary>
    public const string Title = "commit charge";

    /// <summary>
    /// Where <see cref="CommitChargeVerdict.Tight"/> begins: three quarters of
    /// the limit.
    /// </summary>
    /// <remarks>
    /// <b>A band boundary and never an assertion, which is why a number written
    /// here is not the thing the house rule forbids.</b> That rule governs an
    /// upper bound on a <i>measured duration</i>; this classifies a reading into
    /// words for a reader. Nothing fails at either boundary.
    /// </remarks>
    public const double TightFraction = 0.75;

    /// <summary>Where <see cref="CommitChargeVerdict.Critical"/> begins: nine tenths of the limit.</summary>
    public const double CriticalFraction = 0.90;

    /// <summary>The reading taken before the first test ran.</summary>
    /// <remarks>
    /// <see cref="CommitChargeReading.NotTaken"/> until
    /// <see cref="SuiteCoverage.TakeTheCommitChargeReading"/> has run, which is
    /// the honest value: a run whose session hooks never fired has no start
    /// reading, and that must not read as zero bytes committed.
    /// </remarks>
    public static CommitChargeReading AtStart { get; private set; } = CommitChargeReading.NotTaken;

    /// <summary>Takes the start-of-session reading.</summary>
    public static void TakeTheStartReading() => AtStart = CommitChargeReading.Take();

    /// <summary>The row the coverage block prints, with the end reading taken now.</summary>
    public static string CoverageRow => RowFor(AtStart, CommitChargeReading.Take());

    /// <summary>
    /// What a pair of readings amounts to, as a pure function of them.
    /// </summary>
    /// <remarks>
    /// <b>Pure so that every band is exercised on every machine</b>, which is
    /// <see cref="ForegroundLock.Classify"/>'s reason and the same one: a healthy
    /// machine sits in one band for ever, so a classification written only
    /// against the live reading would be three quarters dead code and the band
    /// that matters would be the one nobody had ever run.
    /// </remarks>
    /// <param name="start">The reading taken before the run.</param>
    /// <param name="end">The reading taken after it.</param>
    /// <returns>The verdict, taken on the worse of the two.</returns>
    public static CommitChargeVerdict Classify(CommitChargeReading start, CommitChargeReading end)
    {
        // The WORSE of the two, and never the last one. A run that started at
        // 95% and ended at 30% because the leak was reaped mid-run is a run
        // whose failures are suspect, and an end-only verdict would call it
        // healthy.
        var worst = Math.Max(start.Fraction ?? -1, end.Fraction ?? -1);

        return worst switch
        {
            < 0 => CommitChargeVerdict.Unreadable,
            >= CriticalFraction => CommitChargeVerdict.Critical,
            >= TightFraction => CommitChargeVerdict.Tight,
            _ => CommitChargeVerdict.Healthy,
        };
    }

    /// <summary>The state word the block prints, padded to the width the other rows use.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The padded word.</returns>
    public static string StateWord(CommitChargeVerdict verdict) => verdict switch
    {
        CommitChargeVerdict.Healthy => "HEALTHY  ",
        CommitChargeVerdict.Tight => "TIGHT    ",
        CommitChargeVerdict.Critical => "CRITICAL ",
        _ => "UNREADABLE",
    };

    /// <summary>
    /// The row for a given pair of readings.
    /// </summary>
    /// <param name="start">The reading taken before the run.</param>
    /// <param name="end">The reading taken after it.</param>
    /// <returns>The row, with a second line in the two bands that need one.</returns>
    public static string RowFor(CommitChargeReading start, CommitChargeReading end)
    {
        var verdict = Classify(start, end);
        var row = "  " + Title.PadRight(20) + StateWord(verdict) + "  " + Witness(start, end);

        // ⚠️ The extra lines exist for the same reason ForegroundLock's does: a
        // number printed without its meaning is an assurance the run has not
        // earned. In these two bands the run's own timings are suspect, and the
        // reader has to be told so in the run's output rather than working it
        // out from a percentage.
        return verdict switch
        {
            CommitChargeVerdict.Critical => row + "\n"
                + "      ⚠️  THIS MACHINE WAS AT OR PAST THE COMMIT LIMIT during this run. A hang\n"
                + "      detector reached here is evidence about the machine and not about the code:\n"
                + "      allocations fail outright in this band, and 'The paging file is too small' is\n"
                + "      what the suite reported the last time it happened. See HAZARDS.md.",
            CommitChargeVerdict.Tight => row + "\n"
                + "      ⚠️  Commit was tight during this run. A timing failure here is not safely\n"
                + "      attributable to the code under test; re-run it on a quieter machine before\n"
                + "      recording it as a defect.",
            CommitChargeVerdict.Unreadable => row + "\n"
                + "      ⚠️  THIS RUN DID NOT ANSWER what the machine's commit charge was, so the one\n"
                + "      reading that separates a load-caused stall from a real one is missing from it.",
            _ => row,
        };
    }

    private static string Witness(CommitChargeReading start, CommitChargeReading end)
    {
        var limit = end.Limit ?? start.Limit;

        return $"start {Describe(start)} · end {Describe(end)} · limit {Mib(limit)} MiB"
            + $" · \\Memory\\Committed Bytes, {(start.Answered ? "both ends" : "end only")} read through GetPerformanceInfo";
    }

    private static string Describe(CommitChargeReading reading) =>
        reading.Answered
            ? $"{Mib(reading.Committed)} MiB ({(reading.Fraction!.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%)"
            : "<not read>";

    private static string Mib(ulong? bytes) =>
        bytes is { } value
            ? (value / (1024d * 1024d)).ToString("N0", CultureInfo.InvariantCulture)
            : "<unreadable>";
}

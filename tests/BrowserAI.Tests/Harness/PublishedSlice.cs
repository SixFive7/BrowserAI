// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What a run may say about whether the binary it drove belongs to the tree it
/// was reading.
/// </summary>
internal enum PublishFreshnessVerdict
{
    /// <summary>
    /// Nothing was compared, because there was no published binary to compare
    /// anything against.
    /// </summary>
    NotEstablished,

    /// <summary>The binary is at least as new as every input that goes into it.</summary>
    Fresh,

    /// <summary>At least one input is newer than the binary.</summary>
    Stale,
}

/// <summary>
/// The comparison <see cref="PublishedSlice.EnsureFresh"/> performs, as data.
/// </summary>
/// <remarks>
/// <b>A record so that the guard's refusal and the run's coverage row are two
/// renderings of one reading rather than two comparisons.</b> The failure this
/// closes is not that the check was wrong — it was right every time — but that
/// it said nothing when it passed, so the only sentence available to a reader
/// with a staleness suspicion was one nobody had measured.
/// </remarks>
/// <param name="Published">The published binary's modification time, or <see langword="null"/> when there is none.</param>
/// <param name="Newest">The newest input's modification time, or <see langword="null"/>.</param>
/// <param name="NewestInput">That input's repository-relative path, or <see langword="null"/>.</param>
/// <param name="Inputs">How many inputs were compared.</param>
/// <param name="Newer">Every input newer than the binary, repository-relative and ordered.</param>
/// <param name="Absence">Why nothing could be compared, or <see langword="null"/> when something was.</param>
internal sealed record PublishFreshnessReading(
    DateTime? Published,
    DateTime? Newest,
    string? NewestInput,
    int Inputs,
    IReadOnlyList<string> Newer,
    string? Absence)
{
    /// <summary>A reading that established nothing, and why.</summary>
    /// <param name="why">What was missing.</param>
    /// <returns>The reading.</returns>
    public static PublishFreshnessReading Establishing(string why) => new(null, null, null, 0, [], why);

    /// <summary>
    /// How much newer the binary is than the newest input, negative when it is
    /// older, or <see langword="null"/> when nothing was compared.
    /// </summary>
    public TimeSpan? Margin =>
        Published is { } published && Newest is { } newest ? published - newest : null;
}

/// <summary>
/// The published NativeAOT binary, and the payload beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The slice is driven from the published binary rather than from
/// <c>dotnet run</c>, and that is the point of the step it belongs to.</b> The
/// decisions under test — <c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c> under
/// <c>[LibraryImport]</c>, the SDK's serialization under ILC, a
/// <c>JsonSerializerContext</c>-free JSON path — are all things that behave
/// identically under the JIT and can only fail after native compilation.
/// </para>
/// <para>
/// <b>A stale publish is refused rather than tested.</b> Nothing rebuilds it
/// automatically: ILC costs about a minute and a test that silently ran last
/// week's binary would report a green suite for code that was never compiled.
/// <see cref="EnsureFresh"/> compares its timestamp against every source file
/// that goes into it and fails with the command to run.
/// </para>
/// </remarks>
internal static class PublishedSlice
{
    /// <summary>Where <c>dotnet publish -r win-x64 --self-contained</c> puts it.</summary>
    public static string Directory { get; } = Path.Combine(
        RepositoryLayout.Root.FullName,
        "src", "BrowserAI", "bin", "Release", "net10.0-windows", "win-x64", "publish");

    /// <summary>The published binary.</summary>
    public static string Executable { get; } = Path.Combine(Directory, "BrowserAI.exe");

    /// <summary>The payload that must sit beside it for a child to start.</summary>
    public static string PayloadMarker { get; } = Path.Combine(Directory, "payload", "payload.json");

    /// <summary>The command that produces both.</summary>
    public const string PublishCommand =
        "dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64 --self-contained";

    /// <summary>Whether a published binary with a payload beside it exists.</summary>
    public static bool IsPresent => File.Exists(Executable) && File.Exists(PayloadMarker);

    /// <summary>
    /// Whether the publish directory is absent <b>as a whole</b>, which is what
    /// a clean clone looks like.
    /// </summary>
    /// <remarks>
    /// Asserted rather than <see cref="IsPresent"/>'s negation, so that "nobody
    /// has published" is distinguishable from "the publish ran and the binary or
    /// the payload is missing from it". The second is a real defect and would
    /// otherwise read as a clean clone.
    /// </remarks>
    public static bool IsAbsentAsAWhole => !System.IO.Directory.Exists(Directory);

    /// <summary>
    /// The committed provenance stamp for the payload that publishes beside the
    /// binary: <c>build/payload/package-lock.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named by path, because it is the one input here that cannot be
    /// enumerated.</b> <c>RepositoryLayout</c> prunes any directory called
    /// <c>payload</c> during the walk — there are two of them and one carries an
    /// unpacked <c>node_modules</c> — so every corpus that class produces is
    /// blind to this file by construction. Naming it is not a shortcut around
    /// the walk; it is the only way to watch a tree the walk is right to prune.
    /// </para>
    /// <para>
    /// <b>The lock rather than the payload, and that is the honest input.</b>
    /// The thing that actually goes into the publish is the resolved
    /// <c>node_modules</c> tree, which is gitignored, is tens of thousands of
    /// files, and whose timestamps say when <c>npm ci</c> last ran rather than
    /// what it resolved. The lock is the committed record of exactly that
    /// resolution: it moves when and only when the payload's resolved set moves,
    /// and it is what the upstream review reads. Watching it means a re-resolve
    /// that changed something makes the publish stale; it does not mean an
    /// unpacked tree that was deleted and restored does, which is correct — that
    /// is the same payload.
    /// </para>
    /// </remarks>
    public static FileInfo PayloadProvenanceStamp { get; } = new(
        Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "package-lock.json"));

    /// <summary>
    /// Every file that goes into the published binary, and whose being newer
    /// than it means the binary is stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Four kinds, and the last two each arrived as a measured gap.</b> The
    /// product's C#, the build files that decide how it is compiled, the source
    /// this repository vendors from elsewhere and compiles in — which until
    /// 2026-08-26 was watched by nothing, so a swapped SQLite amalgamation left
    /// the binary reading as fresh and every arm driving it asserting about a
    /// library nobody had built — and, since 2026-08-30, the payload's
    /// provenance stamp.
    /// </para>
    /// <para>
    /// <b>The fourth is the same failure one directory across.</b> A publish
    /// copies the resolved payload beside the executable, so a payload
    /// re-resolve that moved <c>@playwright/mcp</c>, <c>playwright-core</c> or
    /// <c>node</c> leaves the published tree carrying the old one — and until
    /// this row existed, reading as fresh. It was benign on the day it was
    /// found, 2026-08-29, and only because the re-resolve had come back byte for
    /// byte; nothing about the check made it benign. See
    /// <see cref="PayloadProvenanceStamp"/> for why the stamp is watched and the
    /// tree is not.
    /// </para>
    /// <para>
    /// <b>A property rather than a local, so that a test can assert what is in
    /// it.</b> A staleness check is exactly the kind of thing that silently
    /// stops covering something: it fails loudly when it fires and says
    /// nothing at all about what it never looked at.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileInfo> FreshnessInputs { get; } =
    [
        .. RepositoryLayout.ProductSourceFiles,
        .. RepositoryLayout.BuildFiles,
        .. RepositoryLayout.VendoredSourceFiles,
        PayloadProvenanceStamp,
    ];

    /// <summary>
    /// Fails if the published binary is older than anything that goes into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Both sides of this comparison are file modification times, and a
    /// commit's date is neither of them.</b> It reads as pedantry until somebody
    /// makes the substitution, and on 2026-08-30 somebody did: a gate runner put
    /// the published binary's <c>LastWriteTime</c> of <b>01:14:16.500</b> beside
    /// commit <c>56383c9</c>'s date of <b>01:20:40</b>, saw the commit touching
    /// <c>src/BrowserAI/Sessions/SessionLock.cs</c> six minutes after the
    /// publish, and reported that four subsequent gate sets — twelve full runs —
    /// had driven a stale binary while passing this check. <b>Every part of that
    /// reading is true and the conclusion is false.</b> <c>SessionLock.cs</c>'s
    /// own timestamp was <b>01:12:22.665</b>, one minute 53.8 seconds
    /// <i>before</i> the publish; <c>git commit</c> records when it ran and never
    /// touches a working-tree file. The batch edited, published, gated and then
    /// committed, in that order, which is the order its own commit message says
    /// it took. Re-measured the same day over the whole of
    /// <see cref="FreshnessInputs"/>: <b>0 of 95 inputs newer than the binary</b>.
    /// </para>
    /// <para>
    /// <b>What made the misreading available is that this check used to say
    /// nothing when it passed.</b> It threw or it was silent, and the run's
    /// coverage block carried a <c>published slice</c> row reporting
    /// <c>PRESENT</c> — a claim about existence and not about freshness. So
    /// twelve green logs offered no sentence to check a staleness suspicion
    /// against, and the nearest thing to hand was a commit date.
    /// ***Corrected 2026-08-30 (previously "Making the run state its own
    /// freshness margin is a change to the coverage block rather than to this
    /// method, and it is not made here.")*** — it is made now, and it is made
    /// here rather than beside the block: <see cref="Measure"/> is the one
    /// comparison, <see cref="RefusalFor"/> renders it as this method's refusal
    /// and <see cref="RowFor"/> renders it as the run's
    /// <c>publish freshness</c> row, so the sentence in the log and the sentence
    /// in the exception cannot come to disagree. See
    /// [Testing](../../../TESTING.md#the-run-states-the-publish-freshness-it-established).
    /// </para>
    /// <para>
    /// <b>Timestamps rather than content, and that is forced rather than
    /// chosen.</b> The obvious stronger check — hash the inputs, hash the
    /// binary, refuse a binary that does not belong to them — has no binary
    /// half to compare against here. Measured 2026-08-30: two publishes of an
    /// <i>identical</i> input set, nothing in <see cref="FreshnessInputs"/>
    /// touched between them, produced binaries of the same length
    /// (<c>19,186,688</c> bytes) and <b>different SHA-256</b>. So a content hash
    /// cannot answer <i>is this binary from this source</i> for this toolchain,
    /// and the modification times are what is left. Re-establish it by running
    /// <see cref="PublishCommand"/> twice with no edit between and comparing
    /// <c>Get-FileHash</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The publish is stale, or there is nothing to compare.</exception>
    public static void EnsureFresh()
    {
        var reading = Measure();

        if (Judge(reading) is not PublishFreshnessVerdict.Fresh)
        {
            throw new InvalidOperationException(RefusalFor(reading));
        }
    }

    /// <summary>The label the <c>publish freshness</c> row carries in the coverage block.</summary>
    public const string FreshnessTitle = "publish freshness";

    /// <summary>The word a run prints when the binary is newer than everything in it.</summary>
    public const string FreshState = "FRESH";

    /// <summary>The word a run prints when something that goes into the binary is newer than it.</summary>
    public const string StaleState = "STALE";

    /// <summary>The word a run prints when there was nothing to compare.</summary>
    public const string NotEstablishedState = "NOT ESTABLISHED";

    /// <summary>
    /// The comparison, taken now, in one pass over
    /// <see cref="FreshnessInputs"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One pass producing both answers, because they are one question asked
    /// twice.</b> <i>Is anything newer than the binary?</i> is what
    /// <see cref="EnsureFresh"/> refuses on; <i>what is the newest thing, and by
    /// how much did the binary beat it?</i> is what the coverage row states. A
    /// second enumeration for the row would be a second implementation free to
    /// ask a subtly different question — which is exactly how the corpus scan
    /// came to disagree with <c>git ls-files</c> by 520 files while its own
    /// remark said the two matched.
    /// </para>
    /// <para>
    /// <b>The absence is gated on the executable and not on
    /// <see cref="IsPresent"/>, and the difference is deliberate.</b> The
    /// comparison needs the binary's timestamp and nothing else, so a publish
    /// whose payload is missing still has an answerable freshness question and
    /// gets a real answer here. That its tier is broken is a different sentence
    /// and the block already carries it: <c>published slice</c> reads
    /// <c>PARTIAL</c>, and
    /// <see cref="SuiteCoverageTests.NothingThisRunLacksIsHalfInstalled"/> fails
    /// the run. Each row answers its own question rather than borrowing another's
    /// verdict.
    /// </para>
    /// <para>
    /// ⚠️ <b>The input side is the snapshot the run started with, not a fresh
    /// stat, and that is a property of <see cref="FreshnessInputs"/> rather than
    /// a choice made here.</b> Those <see cref="FileInfo"/> instances are created
    /// once when <c>RepositoryLayout</c> initialises and cache their timestamps,
    /// so an edit made <i>while</i> the suite is running is invisible to this
    /// comparison — as it always has been. It is stated rather than fixed
    /// because the row must report what the guard compared: a row that re-stat'd
    /// while the guard did not would be the two-implementations defect wearing
    /// the clothes of an improvement.
    /// </para>
    /// </remarks>
    /// <returns>What this run can say about the binary it would drive.</returns>
    public static PublishFreshnessReading Measure()
    {
        if (!File.Exists(Executable))
        {
            return PublishFreshnessReading.Establishing(
                IsAbsentAsAWhole
                    ? $"nothing has been published: there is no directory at '{Directory}'"
                    : $"the publish directory exists and the binary is not in it: '{Executable}' is missing");
        }

        var published = File.GetLastWriteTimeUtc(Executable);

        FileInfo? newest = null;
        var newer = new List<string>();

        foreach (var file in FreshnessInputs)
        {
            var stamp = file.LastWriteTimeUtc;

            if (newest is null || stamp > newest.LastWriteTimeUtc)
            {
                newest = file;
            }

            if (stamp > published)
            {
                newer.Add(Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName));
            }
        }

        newer.Sort(StringComparer.Ordinal);

        return new PublishFreshnessReading(
            published,
            newest?.LastWriteTimeUtc,
            newest is null ? null : Path.GetRelativePath(RepositoryLayout.Root.FullName, newest.FullName),
            FreshnessInputs.Count,
            newer,
            Absence: null);
    }

    /// <summary>What this run's own reading amounts to.</summary>
    public static PublishFreshnessVerdict Verdict => Judge(Measure());

    /// <summary>The coverage block's row for this run.</summary>
    public static string CoverageRow => RowFor(Measure());

    /// <summary>
    /// What a reading amounts to, as a pure function of it.
    /// </summary>
    /// <remarks>
    /// <b>Pure for <see cref="SuiteEnvironment.Decide"/>'s reason exactly.</b>
    /// A healthy tree publishes and then runs, so <c>STALE</c> is a state this
    /// machine reaches perhaps once a fortnight — and a rendering first exercised
    /// on the day it matters is the same dead-mechanism defect the coverage block
    /// exists to remove, one layer in.
    /// </remarks>
    /// <param name="reading">The reading.</param>
    /// <returns>Fresh, stale, or nothing established.</returns>
    public static PublishFreshnessVerdict Judge(PublishFreshnessReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        // Driven by the same list EnsureFresh refuses on rather than by the sign
        // of the margin: a file stamped to the millisecond OF the publish is not
        // newer than it, and the two would part company at exactly that tick.
        return reading.Absence is not null || reading.Published is null || reading.Newest is null
            ? PublishFreshnessVerdict.NotEstablished
            : reading.Newer.Count is 0 ? PublishFreshnessVerdict.Fresh : PublishFreshnessVerdict.Stale;
    }

    /// <summary>
    /// The sentence <see cref="EnsureFresh"/> refuses with, for a reading.
    /// </summary>
    /// <remarks>
    /// <b>A function of the reading so that a refusal can be driven without
    /// arranging a stale publish.</b> The alternative is a test that edits a
    /// source file to provoke one, which would leave the tree needing a
    /// re-publish to go green again — and the guard's message is the thing a
    /// developer reads at the worst moment, so it is worth exercising on every
    /// ordinary run.
    /// </remarks>
    /// <param name="reading">The reading.</param>
    /// <returns>The refusal, naming the command that resolves it.</returns>
    public static string RefusalFor(PublishFreshnessReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (Judge(reading) is PublishFreshnessVerdict.NotEstablished)
        {
            return $"There is no published binary to test against — {reading.Absence} — so this test would prove nothing about the code in the tree. Run: {PublishCommand}";
        }

        return $"The published binary at '{Executable}' is older than {reading.Newer.Count.ToString(CultureInfo.InvariantCulture)} source file(s), so this test would prove nothing about the code in the tree. Run: {PublishCommand}"
            + Environment.NewLine + string.Join(Environment.NewLine, reading.Newer);
    }

    /// <summary>
    /// The coverage block's row for a reading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This row exists because a check that is silent on success leaves a
    /// suspicion nothing to be checked against.</b> On 2026-08-30 a gate runner
    /// noticed the published binary was stamped 01:14:16 and that commit
    /// <c>56383c9</c> was dated 01:20:40 touching a product source file, and
    /// reported that four gate sets — twelve full runs — had driven a stale
    /// binary. The suspicion was reasonable, the arithmetic was right, and the
    /// conclusion was false: a commit's date is when <c>git commit</c> ran and
    /// never a working-tree file's timestamp, and the file in question was
    /// stamped 01:12:22.665, comfortably before the publish. Dissolving it took
    /// an investigation. <b>One line in twelve green logs would have killed it
    /// instantly</b>, and there was no such line to read.
    /// </para>
    /// <para>
    /// <b>Milliseconds, deliberately.</b> The whole of that misreading turned on
    /// a gap of one minute 53.8 seconds; a row printed to the second would have
    /// answered it, and a row printed to the minute would have made it worse. The
    /// timestamps are UTC and say so, because the two figures put side by side
    /// that day were a local-time file stamp and a commit date, and nothing in
    /// either sentence named a zone.
    /// </para>
    /// <para>
    /// <b>The margin is stated rather than left to be subtracted.</b> A reader
    /// with a suspicion has two timestamps and a hypothesis; what settles it is
    /// the difference and its sign, so the row carries the word — <i>newer</i> or
    /// <i>OLDER</i> — as well as the number.
    /// </para>
    /// <para>
    /// <b>It is a row and not a <see cref="SuiteCapability"/>, and the reason is
    /// the opposite of <see cref="CommitCharge"/>'s.</b> That one is not a
    /// capability because nothing anybody types would make it green; this one is
    /// not a capability because <see cref="SuiteCapability.PublishedSlice"/>
    /// already <i>is</i> one and reports the artefact's existence. Freshness is a
    /// second question about the same artefact, and the two were conflated by a
    /// reader who had only the first — which is how this row came to be written.
    /// </para>
    /// </remarks>
    /// <param name="reading">The reading.</param>
    /// <returns>The row, and a warning beneath it in the band that needs one.</returns>
    public static string RowFor(PublishFreshnessReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);
        var row = "  " + FreshnessTitle.PadRight(20) + StateWord(verdict) + "  " + Witness(reading, verdict);

        // ⚠️ The second block appears only in the band where the run's own
        // results are worthless, for the reason ForegroundLock's does: a state
        // word without its consequence is an assurance a reader has to assemble,
        // and a stale publish means every slice arm in the run refused rather
        // than ran.
        return verdict is not PublishFreshnessVerdict.Stale
            ? row
            : row + "\n"
                + "      ⚠️  THE PUBLISHED BINARY IS OLDER THAN THE SOURCE THAT GOES INTO IT, so every\n"
                + "      slice arm in this run refused rather than proving anything about the code in\n"
                + $"      the tree. Run: {PublishCommand}";
    }

    /// <summary>The state word the block prints, padded to the width the other rows use.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The padded word.</returns>
    public static string StateWord(PublishFreshnessVerdict verdict) => verdict switch
    {
        PublishFreshnessVerdict.Fresh => FreshState + "    ",
        PublishFreshnessVerdict.Stale => StaleState + "    ",
        _ => NotEstablishedState,
    };

    /// <summary>The sentence beside the state, which is where the numbers live.</summary>
    /// <param name="reading">The reading.</param>
    /// <param name="verdict">Its verdict.</param>
    /// <returns>The witness.</returns>
    public static string Witness(PublishFreshnessReading reading, PublishFreshnessVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (verdict is PublishFreshnessVerdict.NotEstablished)
        {
            return $"{reading.Absence} — nothing was compared, so no line in this log says whether a binary matches the tree";
        }

        var inputs = reading.Inputs.ToString(CultureInfo.InvariantCulture);
        var margin = Humanise(reading.Margin!.Value);
        var direction = verdict is PublishFreshnessVerdict.Stale ? "OLDER than" : "newer than";

        var sentence =
            $"exe {Stamp(reading.Published)} is {margin} {direction} the newest of {inputs} inputs"
            + $" ({reading.NewestInput}, {Stamp(reading.Newest)})";

        return verdict is PublishFreshnessVerdict.Stale
            ? sentence + $" · {reading.Newer.Count.ToString(CultureInfo.InvariantCulture)} of {inputs} are newer · the comparison PublishedSlice.EnsureFresh refused on"
            : sentence + " · the comparison PublishedSlice.EnsureFresh passed silently until 2026-08-30";
    }

    /// <summary>A modification time, in UTC, to the millisecond that settled the misreading.</summary>
    /// <param name="stamp">The timestamp.</param>
    /// <returns>The text.</returns>
    public static string Stamp(DateTime? stamp) =>
        stamp is { } value
            ? value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
            : "<none>";

    /// <summary>A margin in the largest unit that leaves a number a reader can hold.</summary>
    /// <param name="span">The margin, whose sign is carried by the row's own words.</param>
    /// <returns>The text.</returns>
    public static string Humanise(TimeSpan span)
    {
        var absolute = span.Duration();

        return absolute switch
        {
            { TotalDays: >= 1 } =>
                $"{((int)absolute.TotalDays).ToString(CultureInfo.InvariantCulture)}d{absolute.Hours.ToString("D2", CultureInfo.InvariantCulture)}h{absolute.Minutes.ToString("D2", CultureInfo.InvariantCulture)}m",
            { TotalHours: >= 1 } =>
                $"{((int)absolute.TotalHours).ToString(CultureInfo.InvariantCulture)}h{absolute.Minutes.ToString("D2", CultureInfo.InvariantCulture)}m{absolute.Seconds.ToString("D2", CultureInfo.InvariantCulture)}s",
            { TotalMinutes: >= 1 } =>
                $"{((int)absolute.TotalMinutes).ToString(CultureInfo.InvariantCulture)}m{absolute.Seconds.ToString("D2", CultureInfo.InvariantCulture)}s",
            _ => $"{absolute.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s",
        };
    }

    /// <summary>
    /// The environment BrowserAI itself is started with: this process's own,
    /// which is what an MCP client would hand it.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> <c>ChildEnvironment.Build()</c>. That is the
    /// allowlist BrowserAI applies to its own child, and using it here would
    /// mean the test proved the allowlist by supplying it — the child's
    /// environment has to be whatever BrowserAI decides when handed an ordinary
    /// one.
    /// </remarks>
    /// <returns>The inherited environment block.</returns>
    public static Dictionary<string, string> InheritedEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        return environment;
    }
}

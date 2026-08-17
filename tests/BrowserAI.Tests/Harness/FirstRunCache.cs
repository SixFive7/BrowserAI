// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>Where a first-run test's 203.8 MB actually came from.</summary>
internal enum FirstRunSource
{
    /// <summary>
    /// Upstream's own installer, over the network, into an empty root. The only
    /// mode that can say anything about the CDN, the mirror, or whether the
    /// revision the payload pins still resolves.
    /// </summary>
    Cdn,

    /// <summary>
    /// A tree a <see cref="Cdn"/> run downloaded within the last hour, copied in
    /// by the test while BrowserAI watched for the marker.
    /// </summary>
    Cache,
}

/// <summary>One cached provisioned tree, and the stamp that describes it.</summary>
/// <param name="Path">The entry directory.</param>
/// <param name="BrowsersDirectory">The browsers root inside it.</param>
/// <param name="DownloadedUtc">
/// When the CDN run that produced it <b>downloaded</b>, never when it was last
/// read. A cache whose clock is reset by use never expires.
/// </param>
/// <param name="ChromiumRevision">The revision it holds, which must match this build's.</param>
/// <param name="Files">How many files the stamp recorded.</param>
/// <param name="Bytes">How many bytes the stamp recorded.</param>
internal sealed record FirstRunCacheEntry(
    string Path,
    string BrowsersDirectory,
    DateTimeOffset DownloadedUtc,
    string ChromiumRevision,
    int Files,
    long Bytes);

/// <summary>What the first-run test is about to do, and why.</summary>
/// <param name="Source">Where the tree will come from.</param>
/// <param name="Entry">The entry to seed from, or <see langword="null"/> for a CDN run.</param>
/// <param name="Reason">
/// One sentence naming why, for the run's own output. Never empty: a run that
/// did not download has to say what it did instead.
/// </param>
internal sealed record FirstRunPlan(FirstRunSource Source, FirstRunCacheEntry? Entry, string Reason);

/// <summary>
/// A once-an-hour ceiling on how often the suite asks Playwright's CDN for
/// 203.8 MB.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem it solves is bandwidth, not time.</b>
/// <see cref="FirstRunProvisioningTests"/> provisions a browser from an empty
/// root on every run, which is 203.8 MB down
/// ([kb](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)).
/// That was one run a day; it is about to be dozens, and pointing that at a
/// public CDN is not something to do because nobody stopped us.
/// </para>
/// <para>
/// <b>What is cached is the provisioned tree, and the TTL is one hour measured
/// from the download.</b> Inside the hour the test copies that tree in instead
/// of downloading; outside it, the test downloads for real and refreshes the
/// entry. So the genuinely cold path still runs — at most hourly, and at least
/// hourly — and <b>no cached run can extend the TTL</b>, because the stamp
/// records when the bytes were fetched rather than when they were last used.
/// </para>
/// <para>
/// ⚠️ <b>A cached run proves less than a CDN run, and the difference is stated
/// on the test rather than here.</b> Four mechanisms keep that from becoming
/// silent, and the first is the one that matters:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>A release run never uses the cache.</b> <c>BROWSERAI_RELEASE_RUN=1</c>
/// forces <see cref="FirstRunSource.Cdn"/>, so no release can be cut on
/// evidence that came out of <c>.work\</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Every run says which path it took</b>, in the coverage block
/// <see cref="SuiteEnvironment.Summary"/> prints unconditionally — the same
/// block, and for the same reason, that makes a degraded run distinguishable
/// from a real one.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>The ceiling is a ceiling.</b> One hour, and an entry stamped in the
/// future is refused rather than trusted forever, which is what a clock change
/// or a hand-edited stamp would otherwise buy.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b><c>BROWSERAI_FIRST_RUN_CACHE=off</c></b> forces a cold run on demand,
/// without editing anything.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Publishing is committed by a rename and never by a write, which is this
/// repository's standing answer to a concurrent writer.</b> The tree is copied
/// into <c>.staging-&lt;guid&gt;\</c> — a name readers do not enumerate — and
/// then moved to its final <c>entry-&lt;stamp&gt;-&lt;guid&gt;\</c> name in one
/// <c>MoveFileEx</c>. The destination name carries a GUID, so two publishers
/// cannot collide and neither has to wait for a lock; a reader either sees a
/// complete entry or does not see it at all, exactly as the session index
/// answers the same question with an atomic replace rather than a mutex.
/// </para>
/// <para>
/// <b>Completeness is not assumed from the rename.</b>
/// <see cref="Plan"/> re-establishes it before every use: the stamp must name
/// this build's revision, <c>chromium-&lt;rev&gt;\INSTALLATION_COMPLETE</c> and
/// an <c>ffmpeg-*</c> marker must both be present, <c>chrome.exe</c> must be
/// where the payload says, no <c>chromium_headless_shell-*</c> may exist — a
/// cache carrying one would make the test's negative assertion pass for the
/// wrong reason — and the census must match the stamp file for file and byte for
/// byte. A tree that fails any of those is refused with the reason, and the run
/// goes to the CDN.
/// </para>
/// </remarks>
internal static class FirstRunCache
{
    /// <summary>The variable that forces a cold run without editing anything.</summary>
    /// <remarks>Any value other than <c>off</c> or <c>0</c> leaves the cache on.</remarks>
    public const string DisableVariable = "BROWSERAI_FIRST_RUN_CACHE";

    /// <summary>The stamp file inside an entry.</summary>
    public const string StampFile = "entry.json";

    private const string EntryPrefix = "entry-";
    private const string StagingPrefix = ".staging-";

    /// <summary>
    /// How long <see cref="Commit"/> waits out a scanner's handle before giving
    /// up and reporting the refusal as itself.
    /// </summary>
    /// <remarks>
    /// Two seconds, the same figure <see cref="InstallationMarker"/> uses
    /// against the same class of transient. Every observed occurrence cleared
    /// inside one retry.
    /// </remarks>
    private static readonly TimeSpan CommitBudget = TestDefaults.ProcessHang;

    /// <summary>
    /// The coverage row, written once by the first-run test and read once by
    /// the session hook that prints the block.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Volatile"/> rather than a lock, because there is exactly
    /// one writer and one reader and they never overlap.</b> The read happens in
    /// an <c>[After(TestSession)]</c> hook, after every test has finished; what
    /// is needed is that the write be <i>visible</i> there, not that it be
    /// serialised against anything. A lock would say the opposite about the
    /// contention this has.
    /// </remarks>
    private static string _row = Row("NOT RUN", "no first-run test has run in this session yet");

    /// <summary>
    /// How old a cached tree may be before the CDN is asked again.
    /// </summary>
    /// <remarks>
    /// One hour, which is the maintainer's instruction of 2026-08-17 verbatim:
    /// <i>"only download once per hour. I don't want to hammer the servers."</i>
    /// </remarks>
    public static TimeSpan Ttl { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Where entries live: <c>&lt;repo&gt;\.work\first-run-cache</c>.
    /// </summary>
    /// <remarks>
    /// <b>Under the repository's own gitignored scratch root, and nowhere near
    /// <c>%LocalAppData%\BrowserAI</c>.</b> That directory holds the developer's
    /// real provisioned browsers; a cache that wrote there — or worse, pruned
    /// there — would be a destructive operation against the one tree this
    /// repository's rules put out of bounds.
    /// </remarks>
    public static string Root { get; } = Path.Combine(RepositoryLayout.Root.FullName, ".work", "first-run-cache");

    /// <summary>Whether this run was told to bypass the cache.</summary>
    public static bool IsDisabled { get; } =
        Environment.GetEnvironmentVariable(DisableVariable) is { } value
        && (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("0", StringComparison.Ordinal));

    /// <summary>The coverage block's row for this run.</summary>
    public static string CoverageRow => Volatile.Read(ref _row);

    /// <summary>
    /// The decision, as a pure function of its three inputs, so the release
    /// branch is exercised rather than only written.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="SuiteEnvironment.Decide"/> and for the same
    /// reason: a policy that only runs on release day is a mechanism nobody
    /// exercises until it matters.
    /// </remarks>
    /// <param name="age">
    /// How long ago the newest usable entry was downloaded, or
    /// <see langword="null"/> when there is no usable entry.
    /// </param>
    /// <param name="isReleaseRun">Whether <c>BROWSERAI_RELEASE_RUN</c> is set.</param>
    /// <param name="cacheDisabled">Whether <c>BROWSERAI_FIRST_RUN_CACHE=off</c> is set.</param>
    /// <returns>Where the tree must come from.</returns>
    public static FirstRunSource Decide(TimeSpan? age, bool isReleaseRun, bool cacheDisabled) =>
        !isReleaseRun
        && !cacheDisabled
        && age is { } elapsed

        // Negative means the stamp is in the future, which is a clock change or
        // an edited file rather than a fresh download. Refused, because the
        // alternative is an entry that never expires.
        && elapsed >= TimeSpan.Zero
        && elapsed <= Ttl
            ? FirstRunSource.Cache
            : FirstRunSource.Cdn;

    /// <summary>
    /// Chooses this run's source, and says why in a sentence the run prints.
    /// </summary>
    /// <returns>The plan.</returns>
    public static FirstRunPlan Plan()
    {
        var (entry, refusal) = Newest(Root);
        var age = entry is null ? default(TimeSpan?) : DateTimeOffset.UtcNow - entry.DownloadedUtc;
        var source = Decide(age, SuiteEnvironment.IsReleaseRun, IsDisabled);

        if (source is FirstRunSource.Cache)
        {
            return new FirstRunPlan(
                source,
                entry,
                $"seeded from a tree the CDN served {Minutes(age!.Value)} ago; nothing was downloaded in this run");
        }

        var why = SuiteEnvironment.IsReleaseRun
            ? $"{SuiteEnvironment.ReleaseRunVariable} is set, so the cache is bypassed and the CDN is asked"
            : IsDisabled
                ? $"{DisableVariable} is set, so the cache is bypassed and the CDN is asked"
                : age is { } elapsed
                    ? $"the newest cached tree was downloaded {Minutes(elapsed)} ago, past the {Minutes(Ttl)} ceiling"
                    : refusal;

        return new FirstRunPlan(source, null, $"downloaded 203.8 MB from the CDN because {why}");
    }

    /// <summary>Records what the first-run test did, for the coverage block.</summary>
    /// <param name="plan">The plan it took.</param>
    /// <param name="elapsed">How long the whole first-run sequence took.</param>
    /// <param name="note">What publishing the tree afterwards did, or nothing.</param>
    public static void Record(FirstRunPlan plan, TimeSpan elapsed, string note)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Volatile.Write(
            ref _row,
            Row(
                plan.Source is FirstRunSource.Cdn ? "CDN    " : "CACHED ",
                $"{plan.Reason} ({elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s). {note}"));
    }

    /// <summary>
    /// Copies a cached tree into a browsers root, <b>writing the completion
    /// markers last</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The ordering is the whole contract, and it is upstream's own.</b>
    /// BrowserAI decides a browser is present by looking for
    /// <c>INSTALLATION_COMPLETE</c> and nothing else, and a BrowserAI process is
    /// watching this directory while the copy runs. A marker that arrived before
    /// the bytes underneath it would hand that process a half-copied Chromium
    /// and produce <c>spawn EFTYPE</c> — which is precisely the tree upstream
    /// never checks for and this product does.
    /// </para>
    /// <para>
    /// Every marker is written through <see cref="InstallationMarker"/>, which
    /// shares the write, and each is empty — verified 2026-08-17 against a real
    /// provisioned root, where all four markers are 0 bytes.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root to fill. Created if absent.</param>
    /// <param name="entry">The entry to copy from.</param>
    public static void SeedInto(string browsersDirectory, FirstRunCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(entry);

        var markers = new List<string>();

        CopyTree(entry.BrowsersDirectory, browsersDirectory, markers);

        foreach (var marker in markers)
        {
            InstallationMarker.Write(Path.GetDirectoryName(marker)!);
        }
    }

    /// <summary>
    /// Publishes a freshly downloaded tree as the newest entry, and prunes the
    /// rest.
    /// </summary>
    /// <remarks>
    /// <b>Never fatal.</b> A publish that fails costs the next run a download,
    /// which is the safe direction; failing the test that just proved
    /// provisioning works would be the unsafe one. What it must never do is
    /// leave a half-written tree under a name a reader trusts, and the rename is
    /// what makes that impossible rather than unlikely.
    /// </remarks>
    /// <param name="browsersDirectory">The root the CDN run filled.</param>
    /// <param name="downloadedUtc">When that download happened.</param>
    /// <param name="root">
    /// The cache root. <see cref="Root"/> in a real run; a scratch directory in
    /// the tests that drive this machinery with a three-file tree, which must
    /// never prune the real cache out from under a run in flight.
    /// </param>
    /// <returns>One sentence saying where it went, or why it did not go anywhere.</returns>
    public static string Publish(string browsersDirectory, DateTimeOffset downloadedUtc, string root)
    {
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(root);

        var staging = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(root);

            var browsers = Path.Combine(staging, "browsers");

            CopyTree(browsersDirectory, browsers, markers: null);

            var (files, bytes) = Census(browsers);

            File.WriteAllText(
                Path.Combine(staging, StampFile),
                new JsonObject
                {
                    ["_what_this_is"] = "A provisioned browsers tree the suite downloaded for real, kept so that the next hour of runs need not download it again. Deleting this directory costs one download.",
                    ["downloadedUtc"] = downloadedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    ["chromiumRevision"] = BrowserAiPaths.ChromiumRevision,
                    ["files"] = files,
                    ["bytes"] = bytes,
                }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // The commit, and the only instant at which this entry becomes
            // visible. The destination name carries a GUID, so no two publishers
            // can name the same directory and neither needs a lock.
            var published = Path.Combine(
                root,
                $"{EntryPrefix}{downloadedUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");

            Commit(staging, published);
            Prune(root, published);

            return $"Cached {files} files / {bytes} B for the next {Minutes(Ttl)}.";
        }
#pragma warning disable CA1031 // A cache that will not publish costs a download and must never fail the test that just proved provisioning works.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            _ = ScratchDirectory.RemoveTree(staging);

            return $"NOT cached ({failure.GetType().Name}: {failure.Message}), so the next run downloads again.";
        }
    }

    /// <summary>The newest structurally complete entry, or why there is none.</summary>
    /// <remarks>
    /// The age is <b>not</b> applied here. An entry that is merely old is
    /// refused by <see cref="Decide"/> with a different sentence from one that
    /// is broken, and telling those two apart is the difference between "the
    /// cache is working" and "the cache has been silently dead for a week".
    /// </remarks>
    /// <param name="root">The cache root to look in.</param>
    /// <returns>The entry and an empty reason, or no entry and the reason.</returns>
    public static (FirstRunCacheEntry? Entry, string Reason) Newest(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Directory.Exists(root))
        {
            return (null, $"no cached tree exists yet under '{root}'");
        }

        var refusals = new List<string>();

        foreach (var candidate in Directory.EnumerateDirectories(root, EntryPrefix + "*")
            .OrderByDescending(directory => directory, StringComparer.Ordinal))
        {
            var (entry, refusal) = Inspect(candidate);

            if (entry is not null)
            {
                return (entry, string.Empty);
            }

            refusals.Add(refusal);
        }

        return (
            null,
            refusals.Count is 0
                ? $"no cached tree exists yet under '{root}'"
                : $"every cached tree was refused: {string.Join("; ", refusals)}");
    }

    /// <summary>
    /// Establishes that one entry is complete, rather than inferring it from the
    /// rename that published it.
    /// </summary>
    /// <remarks>
    /// <b>Public because it is the half worth testing directly.</b> The rename
    /// makes a torn entry unreachable; this is what catches an entry that was
    /// complete once and is not now — a file deleted out of the cache, a disk
    /// that lost one, a revision that moved under it — and every refusal it
    /// returns names what it found rather than saying no.
    /// </remarks>
    /// <param name="candidate">The entry directory.</param>
    /// <returns>The entry and an empty reason, or no entry and the reason.</returns>
    public static (FirstRunCacheEntry? Entry, string Reason) Inspect(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var name = Path.GetFileName(candidate);
        var stamp = Path.Combine(candidate, StampFile);
        var browsers = Path.Combine(candidate, "browsers");

        DateTimeOffset downloaded;
        string revision;
        int files;
        long bytes;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(stamp));

            downloaded = DateTimeOffset.Parse(
                document.RootElement.GetProperty("downloadedUtc").GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            revision = document.RootElement.GetProperty("chromiumRevision").GetString()!;
            files = document.RootElement.GetProperty("files").GetInt32();
            bytes = document.RootElement.GetProperty("bytes").GetInt64();
        }
#pragma warning disable CA1031 // A stamp that cannot be read for ANY reason is a stamp that cannot be trusted, and the answer is the same for all of them: download instead.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return (null, $"{name} has no readable {StampFile} ({failure.GetType().Name})");
        }

        if (!string.Equals(revision, BrowserAiPaths.ChromiumRevision, StringComparison.Ordinal))
        {
            return (null, $"{name} holds chromium-{revision} and this build wants chromium-{BrowserAiPaths.ChromiumRevision}");
        }

        var chromium = Path.Combine(browsers, $"chromium-{BrowserAiPaths.ChromiumRevision}");

        if (!File.Exists(Path.Combine(chromium, BrowsersManifest.InstallationCompleteMarker)))
        {
            return (null, $"{name} carries no {BrowsersManifest.InstallationCompleteMarker} for chromium-{BrowserAiPaths.ChromiumRevision}");
        }

        if (!File.Exists(Path.Combine(chromium, "chrome-win64", "chrome.exe")))
        {
            return (null, $"{name} carries no chrome-win64\\chrome.exe");
        }

        if (!Directory.EnumerateDirectories(browsers, "ffmpeg-*")
            .Any(directory => File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker))))
        {
            return (null, $"{name} carries no completed ffmpeg-*");
        }

        // A cache carrying a headless shell would make the test's negative
        // assertion pass for the wrong reason -- and worse, would keep passing
        // after `--no-shell` stopped working.
        if (Directory.EnumerateDirectories(browsers, "chromium_headless_shell-*").Any())
        {
            return (null, $"{name} carries a chromium_headless_shell-*, which no BrowserAI install ever produces");
        }

        var census = Census(browsers);

        return census == (files, bytes)
            ? (new FirstRunCacheEntry(candidate, browsers, downloaded, revision, files, bytes), string.Empty)
            : (null, $"{name} holds {census.Files} files / {census.Bytes} B where its stamp records {files} / {bytes}");
    }

    /// <summary>
    /// Renames a staging directory to its published name, against a scanner that
    /// may be holding a file inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A directory rename is refused while <i>anything</i> inside the tree
    /// is open, and the thing holding it is not another test.</b> Windows fails
    /// <c>MoveFileEx</c> on a directory with <c>ERROR_ACCESS_DENIED</c> when a
    /// handle is open below it; a real-time scanner opens every file the copy
    /// above has just written, milliseconds after it is written. The publish is
    /// therefore racing a filter driver rather than a peer, and no amount of
    /// locking between publishers can close it.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-17 with the suite running every test at once:</b>
    /// <c>FirstRunCacheTests</c> failed in <b>five of twenty-one</b> full-suite
    /// runs, always inside milliseconds of the copy, and always with
    /// <i>"Access to the path '…\.staging-&lt;guid&gt;' is denied"</i>. It never
    /// failed once in the same twenty-one runs at four-way parallelism, because
    /// the scanner was never that far behind.
    /// </para>
    /// <para>
    /// <b>This is the same shape, and the same justification, as
    /// <see cref="InstallationMarker"/>'s retry</b> — a bounded wait against a
    /// transient sharing state that resolves on its own, not a sleep inserted to
    /// let a peer finish. The budget is small and the last failure is
    /// <b>rethrown</b>, so a rename that is genuinely blocked still surfaces as
    /// itself rather than as an empty directory. The product meets the identical
    /// condition in <c>InstanceDirectory.Claim</c> and answers it differently and
    /// correctly: there the refusal <i>means</i> "somebody holds it", the
    /// directory is skipped, and the next startup reclaims it — a retry there
    /// would weaken a liveness test, where here it protects a commit.
    /// </para>
    /// </remarks>
    /// <param name="staging">The staging directory.</param>
    /// <param name="published">The name that makes it visible.</param>
    private static void Commit(string staging, string published)
    {
        var clock = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Directory.Move(staging, published);
                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (clock.Elapsed >= CommitBudget)
                {
                    throw;
                }

                Thread.Sleep(5);
            }
        }
    }

    /// <summary>Keeps the entry just published and removes every other one.</summary>
    /// <remarks>
    /// <b>One entry, because each is ~430 MiB and a second buys nothing.</b> The
    /// window in which this could delete a tree another process is reading needs
    /// one run to be seeding from an entry while a second finishes a cold
    /// download — that is, for the same entry to be inside the TTL for one
    /// process and outside it for another, within the seconds a copy takes. A
    /// staging directory older than the TTL is swept too: it belongs to a run
    /// that died mid-publish, and nothing will ever come back for it.
    /// </remarks>
    /// <param name="root">The cache root to sweep.</param>
    /// <param name="keep">The entry to keep.</param>
    private static void Prune(string root, string keep)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);

            var stale = name.StartsWith(EntryPrefix, StringComparison.Ordinal)
                ? !string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase)
                : name.StartsWith(StagingPrefix, StringComparison.Ordinal)
                    && DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) > Ttl;

            if (stale)
            {
                _ = ScratchDirectory.RemoveTree(directory);
            }
        }
    }

    /// <summary>Copies a tree, optionally holding the completion markers back.</summary>
    /// <param name="source">The directory to copy.</param>
    /// <param name="destination">Where it goes. Created if absent.</param>
    /// <param name="markers">
    /// When given, every <c>INSTALLATION_COMPLETE</c> is skipped and its
    /// destination path appended here instead, for the caller to write last.
    /// When <see langword="null"/>, markers are copied like anything else.
    /// </param>
    private static void CopyTree(string source, string destination, List<string>? markers)
    {
        _ = Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));

            if (markers is not null && Path.GetFileName(file) is BrowsersManifest.InstallationCompleteMarker)
            {
                markers.Add(target);
                continue;
            }

            File.Copy(file, target, overwrite: true);
        }

        foreach (var child in Directory.EnumerateDirectories(source))
        {
            CopyTree(child, Path.Combine(destination, Path.GetFileName(child)), markers);
        }
    }

    /// <summary>Counts what is actually on disk, which is what the stamp is checked against.</summary>
    /// <param name="directory">The tree to measure.</param>
    /// <returns>The file count and the total size.</returns>
    private static (int Files, long Bytes) Census(string directory)
    {
        var files = 0;
        var bytes = 0L;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files++;
            bytes += new FileInfo(file).Length;
        }

        return (files, bytes);
    }

    private static string Row(string state, string witness) =>
        "  " + "first-run bytes".PadRight(20) + state + "  " + witness;

    private static string Minutes(TimeSpan span) =>
        span.TotalMinutes < 1
            ? $"{span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s"
            : $"{span.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} min";
}

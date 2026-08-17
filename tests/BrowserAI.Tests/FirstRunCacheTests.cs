// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The once-an-hour ceiling on <see cref="FirstRunProvisioningTests"/>'s
/// download, and the refusals that keep a bad cache from being used.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test here is about the mechanism, not about the machine.</b> None of
/// them downloads anything and none of them touches
/// <see cref="FirstRunCache.Root"/> — the round trip below publishes a
/// three-file tree into a scratch directory, because a test that published into
/// the real cache would prune the real entry out from under a first-run test
/// running beside it. That is not hypothetical: this suite runs four tests at
/// once.
/// </para>
/// <para>
/// <b>What this file exists to stop is the cache quietly becoming permanent.</b>
/// A TTL that never expires, a stamp in the future that is trusted forever, a
/// release run that reads <c>.work\</c> instead of the CDN, or a half-copied
/// tree that satisfies the marker check — each of those turns
/// <see cref="FirstRunProvisioningTests"/> into a test that no longer exercises
/// what its name claims, which is the failure class this repository exists to
/// eliminate.
/// </para>
/// </remarks>
internal sealed class FirstRunCacheTests
{
    /// <summary>
    /// The ffmpeg directory the planted trees carry.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not a real revision.</b> The cache matches
    /// <c>ffmpeg-*</c>, so a revision number here would be a second place for
    /// upstream's to be recorded — and a stale one reads as a claim about what
    /// this build installs rather than as the placeholder it is.
    /// </remarks>
    private const string PlantedFfmpeg = "ffmpeg-0";

    [Test]
    public async Task TheCeilingIsAnHourAndAReleaseRunAlwaysAsksTheCdn()
    {
        // The ordinary case, and the only one that avoids a download.
        await Assert.That(FirstRunCache.Decide(TimeSpan.FromMinutes(59), isReleaseRun: false, cacheDisabled: false))
            .IsEqualTo(FirstRunSource.Cache);

        // A ceiling rather than a nudge. One minute past and the CDN is asked,
        // which is what makes "at most once an hour" also mean "at least once an
        // hour of runs".
        await Assert.That(FirstRunCache.Decide(TimeSpan.FromMinutes(61), isReleaseRun: false, cacheDisabled: false))
            .IsEqualTo(FirstRunSource.Cdn);

        await Assert.That(FirstRunCache.Ttl).IsEqualTo(TimeSpan.FromHours(1));

        // ⚠️ The release branch, exercised in every ordinary run for the same
        // reason SuiteEnvironment.Decide is: a policy that only runs on release
        // day is a mechanism nobody exercises until it matters. No release may
        // be cut on evidence that came out of a scratch directory.
        await Assert.That(FirstRunCache.Decide(TimeSpan.FromMinutes(1), isReleaseRun: true, cacheDisabled: false))
            .IsEqualTo(FirstRunSource.Cdn);

        // And the manual override, so forcing a cold run needs no edit.
        await Assert.That(FirstRunCache.Decide(TimeSpan.FromMinutes(1), isReleaseRun: false, cacheDisabled: true))
            .IsEqualTo(FirstRunSource.Cdn);

        // No usable entry at all.
        await Assert.That(FirstRunCache.Decide(null, isReleaseRun: false, cacheDisabled: false))
            .IsEqualTo(FirstRunSource.Cdn);
    }

    [Test]
    public async Task AStampInTheFutureIsRefusedRatherThanTrustedForever()
    {
        // A clock change, a machine whose time is wrong, or a hand-edited stamp.
        // Treated as "not fresh" rather than "always fresh": the second reading
        // is the one that would silently retire the cold path.
        await Assert.That(FirstRunCache.Decide(TimeSpan.FromMinutes(-1), isReleaseRun: false, cacheDisabled: false))
            .IsEqualTo(FirstRunSource.Cdn);

        await Assert.That(FirstRunCache.Decide(TimeSpan.FromDays(-400), isReleaseRun: false, cacheDisabled: false))
            .IsEqualTo(FirstRunSource.Cdn);
    }

    [Test]
    public async Task AnEntryIsPublishedByARenameAndIsFoundBackWholeOrNotAtAll()
    {
        using var scratch = ScratchDirectory.Create("first-run-cache-round-trip");

        var root = Path.Combine(scratch.Path, "cache");
        var downloaded = DateTimeOffset.UtcNow;

        // Asserted rather than discarded, for the reason given in RefusalFor: a
        // publish that failed is reported by this sentence and by nothing else.
        await Assert.That(FirstRunCache.Publish(Plant(Path.Combine(scratch.Path, "browsers")), downloaded, root))
            .StartsWith("Cached ");

        var (entry, refusal) = FirstRunCache.Newest(root);

        await Assert.That(refusal).IsEmpty();
        await Assert.That(entry).IsNotNull();
        await Assert.That(entry!.ChromiumRevision).IsEqualTo(BrowserAiPaths.ChromiumRevision);
        await Assert.That(File.Exists(Path.Combine(entry.BrowsersDirectory, $"chromium-{BrowserAiPaths.ChromiumRevision}", "chrome-win64", "chrome.exe"))).IsTrue();

        // Nothing is left half-published. A staging directory is named so that
        // readers do not enumerate it, and the rename is what makes an entry
        // visible -- so the only directory under the root is the entry itself.
        await Assert.That(Directory.EnumerateDirectories(root).Count()).IsEqualTo(1);

        // A second publish supersedes the first rather than accumulating beside
        // it. Each entry is ~430 MiB in a real run, so a cache that kept them
        // all would cost more disk than the browsers root it exists to spare.
        await Assert.That(FirstRunCache.Publish(Plant(Path.Combine(scratch.Path, "browsers-again")), downloaded.AddSeconds(1), root))
            .StartsWith("Cached ");

        await Assert.That(Directory.EnumerateDirectories(root).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task EveryWayATreeCanBeIncompleteIsRefusedWithTheReason()
    {
        using var scratch = ScratchDirectory.Create("first-run-cache-refusals");

        // The census, which is the check the rename cannot make: an entry that
        // was whole when it was published and has since lost a file reads as
        // complete by every marker and is not. The file removed here is one
        // nothing else looks at, precisely so that the census is what fires.
        await Assert.That(RefusalFor(scratch, "truncated", entry =>
            File.Delete(Path.Combine(entry, "browsers", $"chromium-{BrowserAiPaths.ChromiumRevision}", "chrome-win64", "resources.pak"))))
            .Contains("where its stamp records");

        // The marker BrowserAI actually reads. Without it the product would have
        // re-downloaded anyway, so seeding from such a tree would silently turn
        // a cached run back into a CDN run and report the opposite.
        await Assert.That(RefusalFor(scratch, "unmarked", entry =>
            File.Delete(Path.Combine(entry, "browsers", $"chromium-{BrowserAiPaths.ChromiumRevision}", BrowsersManifest.InstallationCompleteMarker))))
            .Contains(BrowsersManifest.InstallationCompleteMarker);

        // ffmpeg is the LAST component a real install fetches, which is what
        // makes the no-headless-shell assertion sound. A cache missing it would
        // hang the test on a marker nothing was ever going to write.
        await Assert.That(RefusalFor(scratch, "no-ffmpeg", entry =>
            _ = ScratchDirectory.RemoveTree(Path.Combine(entry, "browsers", PlantedFfmpeg))))
            .Contains("ffmpeg");

        // ⚠️ A headless shell in the cache would make the test's negative
        // assertion pass because the cache never had one, rather than because
        // --no-shell works -- and it would keep passing after --no-shell broke.
        await Assert.That(RefusalFor(scratch, "with-shell", entry =>
            InstallationMarker.Write(Path.Combine(entry, "browsers", $"chromium_headless_shell-{BrowserAiPaths.ChromiumRevision}"))))
            .Contains("chromium_headless_shell");

        // A revision bump. The tree is intact and belongs to a build that no
        // longer exists, which is a cache to replace rather than to use.
        await Assert.That(RefusalFor(scratch, "old-revision", entry =>
            File.WriteAllText(
                Path.Combine(entry, FirstRunCache.StampFile),
                File.ReadAllText(Path.Combine(entry, FirstRunCache.StampFile))
                    .Replace($"\"{BrowserAiPaths.ChromiumRevision}\"", "\"1\"", StringComparison.Ordinal))))
            .Contains("this build wants");

        // And a stamp that cannot be read at all, which covers a torn write, a
        // truncation, and a file somebody edited by hand.
        await Assert.That(RefusalFor(scratch, "no-stamp", entry =>
            File.WriteAllText(Path.Combine(entry, FirstRunCache.StampFile), "{ not json")))
            .Contains(FirstRunCache.StampFile);
    }

    [Test]
    public async Task TheCoverageBlockAlwaysSaysWhereTheBytesCameFrom()
    {
        // The block is printed by every run whatever happened, so this line is
        // the thing that makes "the suite quietly stopped downloading" visible.
        // Its VALUE depends on whether the first-run test has run yet, so what
        // is asserted is that the row is there and says one of the three things
        // it may say.
        var summary = SuiteEnvironment.Summary();

        await Assert.That(summary).Contains("first-run bytes");
        await Assert.That(summary.Contains("CDN", StringComparison.Ordinal)
            || summary.Contains("CACHED", StringComparison.Ordinal)
            || summary.Contains("NOT RUN", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// Publishes a planted tree, breaks it, and answers with the refusal.
    /// </summary>
    /// <param name="scratch">The scratch directory to work in.</param>
    /// <param name="label">What is being broken, which names both directories.</param>
    /// <param name="damage">How to break the published entry.</param>
    /// <returns>The reason the entry was refused.</returns>
    private static string RefusalFor(ScratchDirectory scratch, string label, Action<string> damage)
    {
        var root = Path.Combine(scratch.Path, label);

        // ⚠️ The sentence is READ, never discarded. Publish is deliberately
        // never fatal — a cache that will not publish costs a download — so a
        // publish that failed used to reach the line below as an empty
        // directory and fail with "Sequence contains no elements", which names
        // neither the operation nor the reason. Measured 2026-08-17 under
        // unbounded parallelism: three of sixteen full-suite runs failed here,
        // and the swallowed exception was the whole diagnosis.
        var published = FirstRunCache.Publish(Plant(Path.Combine(scratch.Path, $"{label}-browsers")), DateTimeOffset.UtcNow, root);

        var entry = Directory.EnumerateDirectories(root).SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Nothing was published under '{root}'. Publish said: {published}");

        damage(entry);

        return FirstRunCache.Newest(root).Reason;
    }

    /// <summary>
    /// Plants the smallest tree the cache will accept: the two markers, the
    /// executable, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>Three files rather than 318.</b> Everything the cache checks is a
    /// name, a marker or a census, and none of it needs a real Chromium — so
    /// these tests cost milliseconds and the real layout is asserted by the run
    /// that actually downloads it.
    /// </remarks>
    /// <param name="browsers">Where to plant it.</param>
    /// <returns>The same path, for chaining.</returns>
    private static string Plant(string browsers)
    {
        var chromium = Path.Combine(browsers, $"chromium-{BrowserAiPaths.ChromiumRevision}");

        _ = Directory.CreateDirectory(Path.Combine(chromium, "chrome-win64"));

        File.WriteAllText(Path.Combine(chromium, "chrome-win64", "chrome.exe"), "not a browser");

        // A file nothing inspects by name, so that the census has something of
        // its own to be wrong about.
        File.WriteAllText(Path.Combine(chromium, "chrome-win64", "resources.pak"), "not resources");

        InstallationMarker.Write(chromium);
        InstallationMarker.Write(Path.Combine(browsers, PlantedFfmpeg));

        return browsers;
    }
}

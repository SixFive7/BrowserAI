// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;

namespace BrowserAI.Tests;

/// <summary>
/// The marker test: the version the build resolved equals the version a human
/// reviewed, for every upstream in <c>upstream-review.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A red test here is never fixed by editing that file.</b> It means the
/// build resolved a version nobody has reviewed, and the fix is
/// <c>UPSTREAM-REVIEW.md</c>: diff upstream's tests, diff <c>config.d.ts</c>,
/// check <c>browsers.json</c> and the CLI surface, then record what changed,
/// what was adopted, and what was declined and why. Bumping the number to make
/// this green defeats the only mechanism that catches a behaviour change
/// behind an identical schema.
/// </para>
/// <para>
/// This is the half the four snapshots cannot do. They catch a surface that
/// moved; this catches a version that moved whether or not its surface did.
/// </para>
/// </remarks>
internal sealed class UpstreamReviewTests
{
    /// <summary>
    /// Upstreams that are reviewed but that nothing in the build references
    /// yet, so there is no resolved version to compare against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named, not skipped. A missing comparison that reports nothing is
    /// the failure this project exists to eliminate, so the day an upstream
    /// enters the build this list stops matching and the suite says so.
    /// </para>
    /// <para>
    /// <b>It worked.</b> <c>ModelContextProtocol</c> left this list on
    /// 2026-08-16 at build-order step 5, one step earlier than the note here
    /// predicted, because the two custom transports need the SDK's
    /// <c>TransportBase</c> and <c>IClientTransport</c>. Adding the package
    /// turned this test red on its own, with no other change; it resolved
    /// <b>2.2.0</b>, which is the version <c>upstream-review.json</c> records
    /// as reviewed, so no review was owed and the marker test was already
    /// green.
    /// </para>
    /// <para>
    /// <b>It worked a second time.</b> <c>Velopack</c> left the list on
    /// 2026-08-16, when the update path was built --
    /// exactly where this note predicted -- and again the only change was adding
    /// the package: this test went red on its own. It resolved <b>1.2.0</b>,
    /// which is what <c>upstream-review.json</c> records as reviewed, so no
    /// review was owed. <b>The list is now empty, and an empty list is the
    /// state this test is most useful in</b> -- every reviewed upstream is in
    /// the build, so any unresolved one is a defect and not a plan.
    /// </para>
    /// </remarks>
    private static readonly string[] NotReferencedByAnyProjectYet = [];

    [Test]
    public async Task EveryReviewedVersionEqualsTheVersionTheBuildResolved()
    {
        var mismatches = Upstreams()
            .Select(upstream => (upstream.Name, upstream.Reviewed, Resolved: Resolve(upstream.Name)))
            .Where(upstream => upstream.Resolved is not null && upstream.Resolved != upstream.Reviewed)
            .Select(upstream => $"{upstream.Name}: reviewed {upstream.Reviewed}, resolved {upstream.Resolved}")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, mismatches)).IsEmpty();
    }

    [Test]
    public async Task TheUpstreamsWithNothingResolvedAreExactlyTheOnesNotInTheBuildYet()
    {
        var unresolved = Upstreams()
            .Where(upstream => Resolve(upstream.Name) is null)
            .Select(upstream => upstream.Name)
            .ToList();

        await Assert.That(string.Join(", ", unresolved.Except(NotReferencedByAnyProjectYet, StringComparer.Ordinal))).IsEmpty();
        await Assert.That(string.Join(", ", NotReferencedByAnyProjectYet.Except(unresolved, StringComparer.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task EveryEntryCarriesNotesAndAnIsoDate()
    {
        // "An empty note is a review that did not happen, and it is visible as
        // such in the diff" (UPSTREAM-REVIEW.md). Visible is not enough on its
        // own, so it is also a failing test.
        var defective = new List<string>();

        foreach (var (name, _, date, notes) in Upstreams())
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                defective.Add($"{name}: empty notes");
            }

            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                defective.Add($"{name}: date '{date}' is not ISO 8601");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, defective)).IsEmpty();
    }

    [Test]
    public async Task TheFiveReviewedUpstreamsAreTheOnesTheDriftCheckResolves()
    {
        // Two files list the same upstreams for different reasons:
        // upstream-review.json records what was reviewed, drift-check.json
        // records what a lookup last saw. A name in one and not the other is
        // an upstream that either drifts unwatched or is watched and never
        // reviewed.
        var watched = DriftCheckedUpstreams();
        var reviewed = Upstreams().Select(upstream => upstream.Name).ToList();

        await Assert.That(string.Join(", ", watched.Except(reviewed, StringComparer.Ordinal))).IsEmpty();
        await Assert.That(string.Join(", ", reviewed.Except(watched, StringComparer.Ordinal))).IsEmpty();
    }


    /// <summary>
    /// Every review entry adjudicates every golden snapshot by name, and the set
    /// of names is exactly what <c>upstream-snapshots/</c> holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the <c>snapshots</c> half of what
    /// <see href="TESTING.md">the marker gate</see> specifies, and it is the half
    /// that could be built.</b> The other half, <c>reverification</c> -- an
    /// outcome for every manual row, by name -- is deliberately not built, and the
    /// reason is in <c>TODO.md</c>: there are around forty manual rows, and a test
    /// demanding an outcome for each of them today could only be satisfied by
    /// typing forty outcomes nobody measured, which is the act
    /// <c>UPSTREAM-REVIEW.md</c> exists to forbid.
    /// </para>
    /// <para>
    /// <b>The file list is asserted in BOTH directions, and that is the arm's real
    /// content.</b> A fifth golden snapshot would force every entry to answer for
    /// it, and a deleted one cannot linger as a line nobody re-reads. An entry that
    /// answers three of four is a review that looked at three, and the shape that
    /// hides it is a block that looks complete because every line in it is right.
    /// </para>
    /// <para>
    /// ⚠️ <b>WHAT THIS CANNOT DO, said here so nobody reads it as more than it
    /// is.</b> It cannot tell whether an adjudication is TRUE. <c>unchanged</c> on
    /// a snapshot that moved is a false sentence in a JSON string, and no scan
    /// reaches it; that is what the procedure, the notes and a human reading the
    /// regenerate-and-diff output are for. What this holds is that every snapshot
    /// was ANSWERED, which is the failure that actually happens -- a review that
    /// adjudicates what it noticed and is silent about the rest.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-23</b> by deleting one snapshot line from one entry,
    /// and watched naming the entry and the missing file.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryEntryAdjudicatesEveryGoldenSnapshotByName()
    {
        var golden = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots"))
            .EnumerateFiles()
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        // ⚠️ THE CORPUS CONTROL. An empty directory would make every entry below
        // vacuously complete, which is what a clean run looks like.
        await Assert.That(golden.Count).IsEqualTo(4);

        var defective = new List<string>();

        foreach (var (name, adjudications) in SnapshotBlocks())
        {
            foreach (var missing in golden.Except(adjudications.Keys, StringComparer.Ordinal))
            {
                defective.Add($"{name}: says nothing about '{missing}' -- four snapshots, four lines, every entry, every time");
            }

            foreach (var stranger in adjudications.Keys.Except(golden, StringComparer.Ordinal))
            {
                defective.Add($"{name}: adjudicates '{stranger}', which is not a file under upstream-snapshots/");
            }

            foreach (var (file, verdict) in adjudications)
            {
                if (!verdict.StartsWith("unchanged", StringComparison.Ordinal)
                    && !verdict.StartsWith("changed", StringComparison.Ordinal))
                {
                    defective.Add($"{name}/{file}: a verdict opens with 'unchanged' or 'changed' and this one opens '{verdict.Split(' ')[0]}'");
                    continue;
                }

                if (verdict.StartsWith("changed", StringComparison.Ordinal) && verdict.Length < 80)
                {
                    defective.Add($"{name}/{file}: 'changed' with no adjudication behind it -- {verdict.Length} characters");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, defective)).IsEmpty();
    }

    /// <summary>Each entry's per-snapshot adjudications.</summary>
    /// <returns>One pair per upstream, naming the snapshot and its verdict.</returns>
    private static List<(string Name, Dictionary<string, string> Adjudications)> SnapshotBlocks()
    {
        using var review = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-review.json")));

        return
        [
            .. review.RootElement.GetProperty("upstreams").EnumerateObject()
                .Select(entry => (
                    entry.Name,
                    entry.Value.TryGetProperty("snapshots", out var block)
                        ? block.EnumerateObject().ToDictionary(
                            adjudication => adjudication.Name,
                            adjudication => adjudication.Value.GetString() ?? string.Empty,
                            StringComparer.Ordinal)
                        : [])),
        ];
    }

    private static string? Resolve(string upstream) => upstream switch
    {
        "@playwright/mcp" or "playwright-core" => ResolvedVersions.FromPayloadLock(upstream),
        "node" => ResolvedVersions.FromSnapshotProvenance("node"),
        _ => ResolvedVersions.FromNuGetLocks(upstream),
    };

    private static List<string> DriftCheckedUpstreams()
    {
        using var drift = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "drift-check.json")));

        return [.. drift.RootElement.GetProperty("resolved").EnumerateObject().Select(property => property.Name)];
    }

    private static List<(string Name, string? Reviewed, string? Date, string? Notes)> Upstreams()
    {
        using var review = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-review.json")));

        return
        [
            .. review.RootElement.GetProperty("upstreams").EnumerateObject()
                .Select(entry => (
                    entry.Name,
                    entry.Value.GetProperty("reviewed").GetString(),
                    entry.Value.GetProperty("date").GetString(),
                    entry.Value.GetProperty("notes").GetString())),
        ];
    }
}

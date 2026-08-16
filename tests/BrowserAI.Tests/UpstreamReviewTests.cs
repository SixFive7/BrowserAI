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
    /// Named rather than skipped. A missing comparison that reports nothing is
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
    /// 2026-08-16 at [step 19](../../plan/build-order.md#19-velopack-package-update-roll-back),
    /// exactly where this note predicted, and again the only change was adding
    /// the package: this test went red on its own. It resolved <b>1.2.0</b>,
    /// which is what <c>upstream-review.json</c> records as reviewed, so no
    /// review was owed. <b>The list is now empty, and an empty list is the
    /// state this test is most useful in</b> — every reviewed upstream is in
    /// the build, so any unresolved one is a defect rather than a plan.
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

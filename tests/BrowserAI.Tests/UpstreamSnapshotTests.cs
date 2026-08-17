// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts that the four committed upstream snapshots are present, complete
/// and internally coherent.
/// </summary>
/// <remarks>
/// <para>
/// These do not regenerate anything. Regeneration and comparison belong to
/// <c>build/UpstreamSnapshots.targets</c>, which runs
/// <c>build/Update-UpstreamSnapshots.ps1</c> on every build and fails it with
/// the diff itself; a test that also regenerated would be a second opinion
/// from the same source. What is left to assert here is everything a hand-edit
/// could break that a diff against an absent payload could not catch: that all
/// four files exist, that nothing else is sitting in the directory pretending
/// to be diffed, and that the counts in <c>tools-list.json</c> agree with the
/// arrays they count.
/// </para>
/// <para>
/// The numbers themselves are deliberately not asserted. 24, 69 and 78 are
/// upstream's to change, and the snapshot is what makes a change visible; a
/// literal here would turn an upstream change into two red things to fix and
/// would be the second place a count lives.
/// </para>
/// </remarks>
internal sealed class UpstreamSnapshotTests
{
    private static readonly string[] TheFour =
    [
        "tools-list.json",
        "cli-help.txt",
        "config-schema.d.ts",
        "browsers.json",
    ];

    private static DirectoryInfo SnapshotDirectory { get; } =
        new(Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots"));

    [Test]
    public async Task AllFourSnapshotsAreCommittedAndNonEmpty()
    {
        var missing = TheFour
            .Select(name => new FileInfo(Path.Combine(SnapshotDirectory.FullName, name)))
            .Where(file => !file.Exists || file.Length == 0)
            .Select(file => file.Name)
            .ToList();

        await Assert.That(string.Join(", ", missing)).IsEmpty();
    }

    [Test]
    public async Task TheSnapshotDirectoryHoldsNothingElse()
    {
        // A fifth file here is a snapshot nobody regenerates and nobody diffs,
        // which reads as covered and is not. The build script refuses one too;
        // this is the half that runs without a payload.
        var strays = SnapshotDirectory.EnumerateFiles()
            .Select(file => file.Name)
            .Where(name => !TheFour.Contains(name, StringComparer.Ordinal))
            .ToList();

        await Assert.That(string.Join(", ", strays)).IsEmpty();
    }

    [Test]
    public async Task TheCountsAgreeWithTheArraysTheyCount()
    {
        using var snapshot = ReadToolsList();
        var root = snapshot.RootElement;
        var counts = root.GetProperty("counts");

        var registry = root.GetProperty("toolsByCapability").EnumerateObject()
            .SelectMany(capability => Names(capability.Value))
            .ToList();

        await Assert.That(counts.GetProperty("internalRegistry").GetInt32()).IsEqualTo(registry.Count);
        await Assert.That(counts.GetProperty("exposedMaximum").GetInt32()).IsEqualTo(root.GetProperty("tools").GetArrayLength());
        await Assert.That(counts.GetProperty("defaultSurface").GetInt32()).IsEqualTo(Names(root.GetProperty("defaultSurface")).Count);
        await Assert.That(counts.GetProperty("skillOnly").GetInt32()).IsEqualTo(Names(root.GetProperty("skillOnly")).Count);

        // The one arithmetic relation upstream cannot break without meaning
        // something: everything in the registry is exposed unless it is
        // skill-only.
        await Assert.That(counts.GetProperty("internalRegistry").GetInt32() - counts.GetProperty("skillOnly").GetInt32())
            .IsEqualTo(counts.GetProperty("exposedMaximum").GetInt32());
    }

    [Test]
    public async Task TheDefaultSurfaceIsTheUnconditionalCapabilitiesMinusTheSkillOnlyTools()
    {
        // filteredTools() ors `capability.startsWith("core")` with the
        // configured capabilities, so the core family is on whatever the
        // caller asks for. That is why setting `capabilities: ["config"]`
        // yields 25 tools rather than 1, and it is the mechanism a session
        // type cannot opt out of.
        using var snapshot = ReadToolsList();
        var root = snapshot.RootElement;

        var byCapability = root.GetProperty("toolsByCapability");
        var unconditional = Names(root.GetProperty("unconditionalCapabilities"));
        var skillOnly = Names(root.GetProperty("skillOnly")).ToHashSet(StringComparer.Ordinal);

        var expected = unconditional
            .SelectMany(capability => Names(byCapability.GetProperty(capability)))
            .Where(name => !skillOnly.Contains(name))
            .ToHashSet(StringComparer.Ordinal);

        var actual = Names(root.GetProperty("defaultSurface")).ToHashSet(StringComparer.Ordinal);

        await Assert.That(string.Join(", ", expected.Except(actual))).IsEmpty();
        await Assert.That(string.Join(", ", actual.Except(expected))).IsEmpty();
        await Assert.That(unconditional.Where(capability => !capability.StartsWith("core", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task NoSkillOnlyToolIsExposedAndEveryDefaultToolIs()
    {
        using var snapshot = ReadToolsList();
        var root = snapshot.RootElement;

        var exposed = root.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToList();

        await Assert.That(exposed.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(exposed.Count);
        await Assert.That(string.Join(", ", Names(root.GetProperty("skillOnly")).Intersect(exposed, StringComparer.Ordinal))).IsEmpty();
        await Assert.That(string.Join(", ", Names(root.GetProperty("defaultSurface")).Except(exposed, StringComparer.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task EveryCapabilityInTheSnapshotIsOneUpstreamDeclares()
    {
        using var snapshot = ReadToolsList();
        var root = snapshot.RootElement;

        var declared = Names(root.GetProperty("declaredCapabilities")).ToHashSet(StringComparer.Ordinal);
        var carrying = root.GetProperty("toolsByCapability").EnumerateObject().Select(property => property.Name).ToList();
        var carryingNone = Names(root.GetProperty("capabilitiesCarryingNoTool"));

        await Assert.That(declared).IsNotEmpty();
        await Assert.That(string.Join(", ", carrying.Where(capability => !declared.Contains(capability)))).IsEmpty();
        await Assert.That(string.Join(", ", carryingNone.Where(capability => !declared.Contains(capability)))).IsEmpty();
        // A capability that carries no tool does nothing when set. It is
        // recorded so the day upstream gives it one is a diff, not a surprise.
        await Assert.That(string.Join(", ", carryingNone.Intersect(carrying, StringComparer.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task EveryExposedToolCarriesANameADescriptionAndASchema()
    {
        using var snapshot = ReadToolsList();

        var defective = snapshot.RootElement.GetProperty("tools").EnumerateArray()
            .Where(tool =>
                string.IsNullOrWhiteSpace(tool.GetProperty("name").GetString())
                || string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString())
                || tool.GetProperty("inputSchema").ValueKind is not JsonValueKind.Object)
            .Select(tool => tool.GetProperty("name").GetString() ?? "(unnamed)")
            .ToList();

        await Assert.That(string.Join(", ", defective)).IsEmpty();
    }

    /// <summary>The snapshot's provenance is the marker test's idea of "resolved".</summary>
    [Test]
    public async Task TheSnapshotProvenanceMatchesTheCommittedPayloadLock()
    {
        // Two records of the same resolve, written by different steps: the
        // payload build copies the lock back, and the snapshot generator reads
        // the assembled tree. They can only disagree if a payload was built
        // from a lock that was never committed, and then every version this
        // repository reports is a version nobody resolved.
        using var snapshot = ReadToolsList();
        var resolvedFrom = snapshot.RootElement.GetProperty("resolvedFrom");

        foreach (var package in new[] { "@playwright/mcp", "playwright-core" })
        {
            await Assert.That(resolvedFrom.GetProperty(package).GetString())
                .IsEqualTo(ResolvedVersions.FromPayloadLock(package));
        }

        await Assert.That(resolvedFrom.GetProperty("node").GetString()).StartsWith("v");
    }

    /// <summary>
    /// The suite's own capability filter reproduces the snapshot's recorded
    /// default surface, and BrowserAI's two capability sets are the 42 and 59
    /// [kb](../../kb/playwright/tools-and-artifacts.md#the-per-capability-breakdown-counted)
    /// records — upstream's numbers, before this product's own filtering.
    /// </summary>
    /// <remarks>
    /// Without the first half, <c>UpstreamSurface</c> is a second implementation
    /// of upstream's filter that nothing checks, and every surface assertion
    /// built on it would be measuring the helper rather than the product. The
    /// second half is what turns two numbers in a design document into something
    /// a build can be wrong about.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCapabilityFilterReproducesTheRecordedSurfaces()
    {
        await Assert.That(string.Join(", ", UpstreamSurface.For([])))
            .IsEqualTo(string.Join(", ", UpstreamSurface.DefaultSurface()));

        await Assert.That(UpstreamSurface.For(BrowserConfiguration.BaseCapabilities).Count).IsEqualTo(42);
        await Assert.That(UpstreamSurface.For(BrowserConfiguration.UnionCapabilities).Count).IsEqualTo(59);
    }

    private static JsonDocument ReadToolsList() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(SnapshotDirectory.FullName, "tools-list.json")));

    private static List<string> Names(JsonElement array) =>
        [.. array.EnumerateArray().Select(element => element.GetString()!)];
}

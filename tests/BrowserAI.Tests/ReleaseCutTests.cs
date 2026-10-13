// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.App.Page;
using BrowserAI.Hosting;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A release run gates the commit a release is cut from, stamped, sealed and tagged, and the
/// program built from it: the version every binary it drives and ships carries, and the
/// changelog each carries for its What is new page.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 103 e, in his words of 2026-10-12 verbatim: <i>"103 your
/// recommendation"</i></b>, which was b for 2.0.0 and e afterwards: the release checklist's
/// [running order](../../RELEASING.md#the-order-the-last-six-steps-are-executed-in----and-it-is-not-the-numbering)
/// changed so that the six release runs follow the stamp, and the gate tests the program that
/// ships, the What is new page with the stamped heading included. Since 2026-10-08 the
/// changelog is compiled into the binary, so in the order before 2026-10-13 the gated binary
/// carried <c>[Unreleased]</c> and a pre-release version, and the shipped one the stamped
/// section, the release's own version and links at its tag, which no run saw.
/// </para>
/// <para>
/// <b>This arm is what holds the order.</b> The order is a sequence of commands, and what a
/// run can read of it is the state they leave. In a release run the tag exactly at HEAD must
/// name the newest dated section of <c>CHANGELOG.md</c>; <c>[Unreleased]</c> must hold no
/// entry; that section must be dated the day <c>drift-check.json</c> was last taken; the test
/// host, the published slice and the release publish must each have been built as that
/// version; the two published binaries must each carry this tree's <c>CHANGELOG.md</c> byte for
/// byte; and the full package that ships must carry the release publish. A release run in the
/// order before 2026-10-13, at the commit before the stamp, fails on the first of those. The
/// maintainer chose the tag before the runs and this arm to hold it, in his words of 2026-10-13
/// verbatim: <i>"143 a"</i> and <i>"146 your recommendation"</i>, the second with a row of the
/// coverage block naming the cut to follow, which <c>TODO.md</c> carries.
/// </para>
/// <para>
/// <b>An ordinary run gates no cut</b>, so it holds the judgement over readings it builds, one
/// fault at a time, and the byte search over the test host's own <c>BrowserAI.App</c>
/// assembly, which carries the changelog as a resource, with a copy one byte different as the
/// control that the search can come back empty. That the search finds the changelog in a
/// published binary was measured on 2026-10-13
/// ([kb](../../kb/toolchain.md#a-resource-the-build-compiles-in-is-one-run-of-its-bytes-in-the-published-binary----measured-2026-10-13)).
/// </para>
/// <para>
/// <b>What it cannot see</b>: whether the tag is pushed, which comes after the go; whether
/// the six runs were six, since each run reads only itself; and whether the lookups behind
/// the drift check's stamp were taken, which is the honesty rule <c>drift-check.json</c>
/// carries.
/// </para>
/// </remarks>
internal sealed partial class ReleaseCutTests
{
    /// <summary>The one executable, as an entry inside a full package.</summary>
    private const string PackedExecutable = "lib/app/BrowserAI.exe";

    /// <summary>The changelog at the repository's root.</summary>
    private static string ChangelogPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "CHANGELOG.md");

    /// <summary>The daily drift check, whose stamp is the day the dependencies were read.</summary>
    private static string DriftCheckPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "drift-check.json");

    /// <summary>The release publish's executable, which the full package is packed from.</summary>
    private static string ReleaseExecutable { get; } = Path.Combine(ReleaseLayout.ReleasePublish, "BrowserAI.exe");

    /// <summary>
    /// A release run gates the stamped and tagged commit and the program built from it; an
    /// ordinary run holds the judgement over readings it builds and the search over its own
    /// assembly.
    /// </summary>
    /// <remarks>
    /// <b>The controls first, and unconditionally.</b> No ordinary run is a cut, so a judgement
    /// read only in a release run would run for the first time on the day it matters. Each
    /// fault is planted alone, so a judgement that only refused them together would fail here.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReleaseRunGatesTheStampedTaggedCommitAndTheProgramThatShips()
    {
        const string Development = "1.1.1-alpha.0.430";

        var cut = new CutReading(
            TagAtHead: "v2.0.0",
            NewestVersion: "2.0.0",
            NewestDate: "2026-10-13",
            DriftCheckedOn: "2026-10-13",
            UnreleasedEntries: 0,
            TestHostVersion: "2.0.0",
            Slice: new BinaryReading("slice", "BrowserAI.exe", "2.0.0", CarriesTheChangelog: true),
            ReleasePublish: new BinaryReading("release publish", "artifacts/publish-release/BrowserAI.exe", "2.0.0", CarriesTheChangelog: true),
            ShippingPackageMismatch: null);

        await Assert.That(string.Join(Environment.NewLine, Problems(cut))).IsEmpty().Because("the cut itself: stamped, tagged, then published and packed");

        // The order before 2026-10-13: the six runs at the commit before the stamp, with every
        // binary a pre-release built from it.
        var beforeTheStamp = cut with
        {
            TagAtHead = null,
            NewestVersion = "1.1.0",
            NewestDate = "2026-09-23",
            UnreleasedEntries = 48,
            TestHostVersion = Development,
            Slice = cut.Slice with { Version = Development },
            ReleasePublish = cut.ReleasePublish with { Version = Development },
        };

        var refused = Problems(beforeTheStamp);

        await Assert.That(refused.Count).IsEqualTo(6).Because(string.Join(Environment.NewLine, refused));
        await Assert.That(refused[0]).Contains("No tag is exactly at HEAD");

        (string What, CutReading Reading, string Says)[] faults =
        [
            ("stamped and not tagged", cut with { TagAtHead = null }, "No tag is exactly at HEAD"),
            ("tagged at another release", cut with { TagAtHead = "v1.1.0" }, "name two releases"),
            ("an entry left after the stamp", cut with { UnreleasedEntries = 1 }, "as not yet released"),
            ("stamped on another day than the drift check", cut with { NewestDate = "2026-10-12" }, "the day of the cut"),
            ("a test host built before the tag", cut with { TestHostVersion = Development }, "This test host was built as"),
            ("a slice published before the tag", cut with { Slice = cut.Slice with { Version = Development } }, "The slice at"),
            ("a slice published before the stamp", cut with { Slice = cut.Slice with { CarriesTheChangelog = false } }, "What is new page"),
            ("no release publish", cut with { ReleasePublish = cut.ReleasePublish with { Version = null } }, "There is no release publish"),
            ("a release publish made before the stamp", cut with { ReleasePublish = cut.ReleasePublish with { CarriesTheChangelog = false } }, "What is new page"),
            ("a full package of another publish", cut with { ShippingPackageMismatch = "its lib/app/BrowserAI.exe is not the release publish" }, "The full package that ships"),
        ];

        foreach (var (what, reading, says) in faults)
        {
            var problems = Problems(reading);

            await Assert.That(problems.Count).IsEqualTo(1).Because($"{what}: {string.Join(" | ", problems)}");
            await Assert.That(problems[0]).Contains(says).Because(what);
        }

        // Nothing stamped at all: said once, and nothing else is judged against no version.
        await Assert.That(Problems(cut with { NewestVersion = null, NewestDate = null }).Count).IsEqualTo(1);

        // The search, on every run, over the test host's own assembly, which carries this
        // tree's changelog as a resource; and a copy one byte different is not found in it.
        var changelog = await File.ReadAllBytesAsync(ChangelogPath);
        var assembly = typeof(ShippedChangelog).Assembly.Location;

        await Assert.That(assembly).IsNotEmpty();

        var own = await File.ReadAllBytesAsync(assembly);
        byte[] altered = [.. changelog];

        altered[^2] ^= 0x01;

        await Assert.That(Carries(own, changelog)).IsTrue().Because($"the positive control: '{assembly}' carries this tree's CHANGELOG.md as a resource");
        await Assert.That(Carries(own, altered)).IsFalse().Because("the negative control: a copy one byte different is in no binary");

        // And the reading of the newest release agrees with the page's own.
        var text = Encoding.UTF8.GetString(changelog);

        await Assert.That(NewestDated(text).Version).IsNotNull().Because("the positive control: this tree's changelog holds a dated section");
        await Assert.That(NewestDated(text).Version).IsEqualTo(ChangelogPageContent.Releases(text).First(static release => release.Version is not null).Version);

        if (!SuiteEnvironment.IsReleaseRun)
        {
            return;
        }

        var live = await ReadAsync(changelog, text);

        await Assert.That(string.Join(Environment.NewLine, Problems(live)))
            .IsEmpty()
            .Because("a release run gates the cut it ships, which RELEASING.md's running order stamps, seals and tags before the publishes and the six runs");
    }

    /// <summary>What keeps a reading from being the cut a release run gates, one sentence each.</summary>
    /// <param name="reading">What the run read.</param>
    /// <returns>The problems, in the order the running order would meet them; none for the cut.</returns>
    internal static List<string> Problems(CutReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.NewestVersion is not { } version)
        {
            return ["CHANGELOG.md holds no dated section, so nothing has been stamped for a release run to gate."];
        }

        var problems = new List<string>();
        var tag = "v" + version;

        if (reading.TagAtHead is null)
        {
            problems.Add(
                $"No tag is exactly at HEAD, so this release run gates a commit no release is cut from. RELEASING.md's running order stamps the changelog with build/Get-ReleaseNotes.ps1 -StampVersion, seals the section in the same commit and tags that commit before the publishes and the six runs. The newest dated section here is [{version}], whose tag would be {tag}.");
        }
        else if (!string.Equals(reading.TagAtHead, tag, StringComparison.Ordinal))
        {
            problems.Add($"The tag exactly at HEAD is {reading.TagAtHead} and the newest dated section of CHANGELOG.md is [{version}], so the stamp and the tag name two releases.");
        }

        if (reading.UnreleasedEntries > 0)
        {
            problems.Add(
                $"'## [Unreleased]' holds {reading.UnreleasedEntries} {(reading.UnreleasedEntries is 1 ? "entry" : "entries")} at the cut, so {version} would ship them as not yet released. They landed after the stamp: make the cut again, so that they move under [{version}].");
        }

        if (!string.Equals(reading.NewestDate, reading.DriftCheckedOn, StringComparison.Ordinal))
        {
            problems.Add(
                $"[{version}] is dated {reading.NewestDate ?? "no day"} and drift-check.json was last taken on {reading.DriftCheckedOn ?? "no day it states"}, and both are the day of the cut. Take the drift check and then the stamp on one day, by the local clock.");
        }

        if (!IsVersion(reading.TestHostVersion, version))
        {
            problems.Add($"This test host was built as {reading.TestHostVersion} and the cut is {version}, so it was built before the tag. Build again at the tagged commit.");
        }

        AddBinary(problems, reading.Slice, version, "dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64 --self-contained");
        AddBinary(problems, reading.ReleasePublish, version, "pwsh -File build/New-Release.ps1");

        if (reading.ShippingPackageMismatch is { } mismatch)
        {
            problems.Add($"The full package that ships does not carry the release publish: {mismatch}. Delete the four files of the shipping feed and run pwsh -File build/New-Release.ps1 at the tagged commit.");
        }

        return problems;
    }

    /// <summary>Whether a binary carries a changelog as one run of its bytes.</summary>
    /// <param name="binary">The binary's bytes.</param>
    /// <param name="changelog">The changelog's bytes.</param>
    /// <returns>Whether it does.</returns>
    internal static bool Carries(byte[] binary, byte[] changelog) =>
        changelog.Length > 0 && binary.AsSpan().IndexOf(changelog) >= 0;

    /// <summary>One published binary's problems, said once each.</summary>
    /// <param name="problems">Where to add them.</param>
    /// <param name="binary">What was read of it.</param>
    /// <param name="version">The cut's version.</param>
    /// <param name="command">What makes it again.</param>
    private static void AddBinary(List<string> problems, BinaryReading binary, string version, string command)
    {
        if (binary.Version is not { } built)
        {
            problems.Add($"There is no {binary.What} at '{binary.Path}', which a release run drives or ships. Run {command} at the tagged commit.");
        }
        else if (!IsVersion(built, version))
        {
            problems.Add($"The {binary.What} at '{binary.Path}' was built as {built} and the cut is {version}, so it was built before the tag. Run {command} at the tagged commit.");
        }
        else if (!binary.CarriesTheChangelog)
        {
            problems.Add($"The {binary.What} at '{binary.Path}' does not carry this tree's CHANGELOG.md byte for byte, so its What is new page is not the stamped one. Run {command} after the stamp.");
        }
    }

    /// <summary>What this run can read of the cut it gates.</summary>
    /// <param name="changelog">This tree's changelog, as bytes.</param>
    /// <param name="text">The same, as text.</param>
    /// <returns>The reading.</returns>
    private static async Task<CutReading> ReadAsync(byte[] changelog, string text)
    {
        var (version, date) = NewestDated(text);
        var drift = JsonNode.Parse(await File.ReadAllTextAsync(DriftCheckPath))?["lastChecked"] is JsonValue stamp && stamp.TryGetValue<string>(out var day) ? day : null;
        var releasePublish = await BinaryAsync("release publish", ReleaseExecutable, changelog);
        var package = Path.Combine(ReleaseLayout.Directory, $"{ReleaseLayout.PackId}-{version}-full.nupkg");
        var mismatch = !File.Exists(package)
            ? $"there is no {Path.GetFileName(package)} in {ReleaseLayout.Directory}"
            : await ReleaseLayout.MismatchBetweenAsync(package, [(PackedExecutable, ReleaseExecutable)]);

        return new CutReading(
            await GitOracle.TagExactlyAtHeadAsync(),
            version,
            date,
            drift,
            UnreleasedEntries(text),
            BuildVersion.Current,
            await BinaryAsync("slice", PublishedSlice.Executable, changelog),
            releasePublish,
            mismatch);
    }

    /// <summary>What one published binary is: the version it was built as, and whether it carries the changelog.</summary>
    /// <param name="what">What the run calls it.</param>
    /// <param name="path">Where it is.</param>
    /// <param name="changelog">This tree's changelog, as bytes.</param>
    /// <returns>The reading; a binary that is not there carries no version.</returns>
    private static async Task<BinaryReading> BinaryAsync(string what, string path, byte[] changelog)
    {
        if (!File.Exists(path))
        {
            return new BinaryReading(what, path, null, CarriesTheChangelog: false);
        }

        var built = FileVersionInfo.GetVersionInfo(path).ProductVersion is { Length: > 0 } product ? product : "<none>";

        return new BinaryReading(what, path, built, Carries(await File.ReadAllBytesAsync(path), changelog));
    }

    /// <summary>Whether a binary's version is the cut's, build metadata after <c>+</c> left out.</summary>
    /// <param name="built">The version it reports.</param>
    /// <param name="version">The cut's version.</param>
    /// <returns>Whether they are one version.</returns>
    private static bool IsVersion(string built, string version) =>
        string.Equals(built.Split('+')[0], version, StringComparison.Ordinal);

    /// <summary>The newest dated section's version and date, the first such heading in the file.</summary>
    /// <param name="changelog">The whole changelog.</param>
    /// <returns>Both, or two nulls when the file holds no dated section.</returns>
    private static (string? Version, string? Date) NewestDated(string changelog) =>
        DatedHeading().Match(changelog) is { Success: true } heading
            ? (heading.Groups["version"].Value, heading.Groups["date"].Value)
            : (null, null);

    /// <summary>How many entries <c>## [Unreleased]</c> holds, counted the way the stamp script counts them.</summary>
    /// <param name="changelog">The whole changelog.</param>
    /// <returns>The count; none when the heading is missing.</returns>
    private static int UnreleasedEntries(string changelog) =>
        UnreleasedSection().Match(changelog) is { Success: true } section ? EntryLine().Count(section.Groups["body"].Value) : 0;

    /// <summary>A dated section heading, <c>## [2.0.0] - 2026-10-13</c>, as <c>ChangelogTests</c> reads one.</summary>
    [GeneratedRegex(@"(?m)^\#\#[ \t]+\[(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.\-]*)?)\][ \t]+-[ \t]+(?<date>\d{4}-\d{2}-\d{2})[ \t]*\r?$")]
    private static partial Regex DatedHeading();

    /// <summary>The unreleased section, from its heading to the next section's, as the stamp script matches it.</summary>
    [GeneratedRegex(@"(?ms)^\#\#[ \t]+\[Unreleased\][ \t]*\r?$(?<body>.*?)(?=^\#\#[ \t]|\z)")]
    private static partial Regex UnreleasedSection();

    /// <summary>A changelog list item at the start of a line, as the stamp script counts one.</summary>
    [GeneratedRegex(@"(?m)^[ \t]*[-*][ \t]+\S")]
    private static partial Regex EntryLine();
}

/// <summary>What a release run reads of the cut it gates.</summary>
/// <param name="TagAtHead">The tag exactly at HEAD, or <see langword="null"/>.</param>
/// <param name="NewestVersion">The newest dated section's version, or <see langword="null"/> when there is none.</param>
/// <param name="NewestDate">The date that section's heading gives.</param>
/// <param name="DriftCheckedOn">The day <c>drift-check.json</c> was last taken, its <c>lastChecked</c>.</param>
/// <param name="UnreleasedEntries">How many entries <c>## [Unreleased]</c> holds.</param>
/// <param name="TestHostVersion">The version this test host was built as.</param>
/// <param name="Slice">The published slice the slice arms drive.</param>
/// <param name="ReleasePublish">The release publish the full package and the suite's installer are packed from.</param>
/// <param name="ShippingPackageMismatch">Why the full package that ships does not carry the release publish, or <see langword="null"/> when it does.</param>
internal sealed record CutReading(
    string? TagAtHead,
    string? NewestVersion,
    string? NewestDate,
    string? DriftCheckedOn,
    int UnreleasedEntries,
    string TestHostVersion,
    BinaryReading Slice,
    BinaryReading ReleasePublish,
    string? ShippingPackageMismatch);

/// <summary>One published binary a release run drives or ships.</summary>
/// <param name="What">What the run calls it.</param>
/// <param name="Path">Where it is.</param>
/// <param name="Version">The version it was built as, or <see langword="null"/> when it is not there.</param>
/// <param name="CarriesTheChangelog">Whether it carries this tree's <c>CHANGELOG.md</c> byte for byte.</param>
internal sealed record BinaryReading(string What, string Path, string? Version, bool CarriesTheChangelog);

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.IO.Compression;
using System.Text.Json;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The redistribution obligations, asserted against what actually ships.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these survived to a release gate because nothing looked.</b>
/// [Item 13](../../plan/pre-release.md) names four obligations that attach at
/// first installer handoff, independently of BrowserAI's own licence, and the
/// first run of that checklist — 2026-08-16, reading the packed
/// <c>.nupkg</c> rather than the source tree — found <b>two of the four absent
/// from an otherwise releasable package</b>: Velopack's MIT notice, because a
/// NuGet dependency's licence stays in the package cache and is never copied to
/// a publish output, and the trademark disclaimer, because no upstream file
/// carries one.
/// </para>
/// <para>
/// <b>The set is data, so a fifth obligation is a red build rather than a
/// discovery at the next release.</b> Add a row to <see cref="Obligations"/> and
/// the suite fails until the file ships; that is the whole point of writing it
/// this way rather than as five assertions.
/// </para>
/// <para>
/// <b>Corrected 2026-08-16 at the plan's final audit: the list is six, not four
/// (previously "four obligations that attach at first installer handoff").</b>
/// Two more packages are compiled into <c>BrowserAI.exe</c> on exactly
/// Velopack's terms and were carrying no notice — the Apache-2.0 MCP SDK, whose
/// §4(a) requires a copy of the licence to reach every recipient, and the
/// seventeen MIT <c>Microsoft.Extensions.*</c> assemblies. A NuGet package's
/// licence stays in the machine's package cache and never reaches a publish
/// output, so "it is linked in" and "its notice ships" are independent facts,
/// and the second was false for both.
/// </para>
/// <para>
/// <b>Three subjects, not one, because each can be right while the next is
/// wrong.</b> The repository's own <c>THIRD-PARTY-NOTICES.txt</c> is what a
/// person edits; the publish output is what <c>vpk pack</c> is handed; the
/// packed <c>.nupkg</c> is what a user's machine unpacks. A file present in the
/// first and missing from the third is exactly the state item 13 found.
/// </para>
/// </remarks>
internal sealed class ThirdPartyNoticeTests
{
    /// <summary>
    /// Every notice that must be inside the installed artifact, by the path it
    /// must be at, relative to the directory holding <c>BrowserAI.exe</c>.
    /// </summary>
    /// <remarks>
    /// <b>Paths, not a count.</b> A count would go green on a file landing in
    /// the wrong place, and where these sit is what a licence obligation is
    /// about — a notice nobody can find beside the binary is not shipped.
    /// </remarks>
    private static readonly (string Obligation, string Path)[] Obligations =
    [
        ("Node.js, whose LICENSE aggregates OpenSSL, ICU, V8, zlib and c-ares", @"payload\node\LICENSE"),
        ("@playwright/mcp, Apache-2.0", @"payload\mcp\node_modules\@playwright\mcp\LICENSE"),
        ("playwright-core, Apache-2.0", @"payload\mcp\node_modules\playwright-core\LICENSE"),
        ("playwright-core, the NOTICE section 4(d) propagates", @"payload\mcp\node_modules\playwright-core\NOTICE"),
        ("playwright-core, its own third-party notices", @"payload\mcp\node_modules\playwright-core\ThirdPartyNotices.txt"),
        ("Velopack, ModelContextProtocol and Microsoft.Extensions.*, plus the trademark disclaimer", "THIRD-PARTY-NOTICES.txt"),
    ];

    /// <summary>
    /// The packages compiled into <c>BrowserAI.exe</c> whose licence text is
    /// reproduced in the notices file, and the version stamp that must name the
    /// version the build resolved.
    /// </summary>
    /// <remarks>
    /// <b>A licence text is a measurement, and everything here floats.</b> Any
    /// of these can be bumped by a restore; a bump can change the licence, the
    /// copyright years or the holder, and text copied against an older version
    /// would then be a confident wrong answer rather than a gap. Stamping the
    /// resolved version inside the notices and asserting it against
    /// <c>packages.lock.json</c> makes the bump red until the text has been
    /// re-fetched from the new package's own commit.
    /// </remarks>
    private static readonly (string Package, string[] Needles)[] StampedPackages =
    [
        ("Velopack", ["Velopack {0} - MIT", "Retrieved 2026-08-16 against Velopack {0}."]),
        ("ModelContextProtocol", ["ModelContextProtocol {0} and", "Retrieved 2026-08-16 against ModelContextProtocol {0}."]),
        ("ModelContextProtocol.Core", ["ModelContextProtocol.Core {0} - Apache-2.0"]),
    ];

    /// <summary>The file the build ships, as it sits in the repository.</summary>
    private static string NoticesFile { get; } =
        Path.Combine(RepositoryLayout.Root.FullName, "THIRD-PARTY-NOTICES.txt");

    /// <summary>
    /// The repository's notices file carries the two obligations that have no
    /// upstream file of their own.
    /// </summary>
    /// <remarks>
    /// Asserted on content rather than on the file existing: an empty file at
    /// the right path satisfies a presence check and discharges nothing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNoticesFileCarriesVelopacksLicenceAndTheTrademarkDisclaimer()
    {
        var notices = await File.ReadAllTextAsync(NoticesFile);

        // MIT's operative sentence, which is the half that must travel with the
        // binary, and the copyright line it requires alongside it.
        await Assert.That(notices).Contains("Permission is hereby granted");
        await Assert.That(notices).Contains("The above copyright notice and this permission notice shall be included in");
        await Assert.That(notices).Contains("Caelan Sayler");
        await Assert.That(notices).Contains("Velopack Ltd.");

        // The disclaimer, in the terms README -> Third-party components states.
        // Apache-2.0 section 6 grants no trademark rights and the inherited
        // browser_* names put upstream branding in BrowserAI's own API.
        // Fragments that cannot straddle the file's own line wrapping: the
        // disclaimer is prose in a fixed-width file, so a longer needle would
        // fail on a rewrap rather than on the claim going missing.
        await Assert.That(notices).Contains("Playwright is a trademark of Microsoft Corporation");
        await Assert.That(notices).Contains("Chrome and Chromium are");
        await Assert.That(notices).Contains("trademarks of Google LLC");
        await Assert.That(notices).Contains("not affiliated");
        await Assert.That(notices).Contains("endorsed by, or sponsored by");
        await Assert.That(notices).Contains("Apache-2.0 section 6 grants no trademark rights");
    }

    /// <summary>
    /// Every reproduced licence is stamped with the version the build resolved.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryReproducedLicenceIsStampedWithTheVersionTheBuildResolved()
    {
        var notices = await File.ReadAllTextAsync(NoticesFile);

        foreach (var (package, needles) in StampedPackages)
        {
            var resolved = ResolvedVersions.FromNuGetLocks(package);
            await Assert.That(resolved).IsNotNull();

            foreach (var needle in needles)
            {
                await Assert.That(notices).Contains(string.Format(null, needle, resolved));
            }
        }
    }

    /// <summary>
    /// The notices file carries the MCP SDK's Apache-2.0 licence, whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>§4(a) is the obligation and it is unconditional</b> — a redistributor
    /// must give every recipient a copy of the licence, and no copy travels: the
    /// package is compiled into <c>BrowserAI.exe</c> and its licence stays in
    /// the NuGet cache. This is Velopack's situation exactly, so it gets
    /// Velopack's remedy.
    /// </para>
    /// <para>
    /// <b>Upstream's file grants three licences, not one, and all three are
    /// asserted.</b> The MCP project is mid-transition from MIT: contributions
    /// whose authors have not consented to relicensing are still MIT, and
    /// documentation is CC-BY-4.0. Reproducing the Apache-2.0 half alone would
    /// drop the terms that cover part of the code, so a needle from each of the
    /// three is checked and a future re-fetch that quietly loses one is red.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNoticesFileCarriesTheMcpSdksApacheLicenceWhole()
    {
        var notices = await File.ReadAllTextAsync(NoticesFile);

        // Apache-2.0: the title, the grant §4(a) is about, and the closing line
        // of §9, which is where upstream's own copy ends.
        //
        // "Whole" means upstream's file whole, and upstream's file is NOT the
        // canonical Apache text: it carries §1-9 and stops at END OF TERMS AND
        // CONDITIONS, omitting the appendix its own §4 points at ("an example is
        // provided in the Appendix below"). Measured 2026-08-16 against the
        // commit the nuspec records. Asserting the appendix here would demand we
        // add text upstream did not ship, which is the opposite of verbatim.
        await Assert.That(notices).Contains("Apache License");
        await Assert.That(notices).Contains("Version 2.0, January 2004");
        await Assert.That(notices).Contains("You must give any other recipients of the Work or");
        await Assert.That(notices).Contains("END OF TERMS AND CONDITIONS");

        // The MIT half, for contributions never relicensed.
        await Assert.That(notices).Contains("who have not yet granted explicit permission to relicense remain licensed under the MIT License");

        // The CC-BY-4.0 half, for documentation.
        await Assert.That(notices).Contains("Creative Commons Attribution 4.0 International (CC-BY-4.0)");

        // Attribution, and the provenance of the copy.
        await Assert.That(notices).Contains("Model Context Protocol a Series of LF Projects, LLC.");
        await Assert.That(notices).Contains("6fa3825973949a9c4f0cd8af344e15a8db09dc35");
    }

    /// <summary>
    /// Every <c>Microsoft.Extensions.*</c> package linked into the product is
    /// named in the notices, and both MIT copyright lines are reproduced.
    /// </summary>
    /// <remarks>
    /// <b>Read from the lock file rather than maintained by hand.</b> These
    /// arrive almost entirely transitively — two are referenced directly and the
    /// rest come through those and through the MCP SDK — so the set changes
    /// whenever anything above them is bumped, silently and without anyone
    /// choosing it. Deriving the list from
    /// <c>src/BrowserAI/packages.lock.json</c> makes a new arrival a red build
    /// here rather than a licence nobody noticed had appeared, which is the same
    /// property <see cref="Obligations"/> has and the reason both are data.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryMicrosoftExtensionsPackageLinkedIntoTheProductIsNamedInTheNotices()
    {
        var notices = await File.ReadAllTextAsync(NoticesFile);
        var packages = ProductPackagesStartingWith("Microsoft.Extensions.");

        // A guard on the derivation itself: an empty set would make the loop
        // below vacuously green, which is the failure mode a data-driven
        // assertion has and a hand-written one does not.
        await Assert.That(packages.Count).IsGreaterThan(10);

        var unnamed = packages.Where(package => !notices.Contains(package, StringComparison.Ordinal)).ToList();
        await Assert.That(string.Join(Environment.NewLine, unnamed)).IsEmpty();

        // The two counts the prose states, which a reader takes at face value
        // and which nothing else would contradict when a bump adds a package.
        await Assert.That(notices).Contains($"{packages.Count} Microsoft.Extensions packages");
        await Assert.That(notices).Contains($"{packages.Count - 1} of the {packages.Count} are built from");

        // Both copyright lines. The two source repositories carry the same MIT
        // text under different holders, and asserting only one would go green
        // on a file that had dropped the other.
        await Assert.That(notices).Contains("Copyright (c) .NET Foundation and Contributors");
        await Assert.That(notices).Contains("Copyright (c) .NET Foundation. All rights reserved.");
        await Assert.That(notices).Contains("Copyright (c) Microsoft Corporation. All rights reserved.");
    }

    /// <summary>
    /// Every package in the product's own resolved closure whose id starts with
    /// the given prefix.
    /// </summary>
    /// <remarks>
    /// The product's lock file specifically, not every project's: the obligation
    /// is about what ships inside <c>BrowserAI.exe</c>, and the suite's own
    /// dependencies are redistributed to nobody.
    /// </remarks>
    /// <param name="prefix">The package-id prefix to match.</param>
    /// <returns>The matching package ids, in order.</returns>
    private static IReadOnlyList<string> ProductPackagesStartingWith(string prefix)
    {
        var path = Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "packages.lock.json");

        using var lockFile = JsonDocument.Parse(File.ReadAllText(path));

        return
        [
            .. lockFile.RootElement.GetProperty("dependencies").EnumerateObject()
                .SelectMany(framework => framework.Value.EnumerateObject())
                .Select(dependency => dependency.Name)
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Every obligation is in the publish output, which is what <c>vpk pack</c>
    /// is handed.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryNoticeIsInThePublishOutput()
    {
        SuiteEnvironment.RequirePublishedSlice();
        RefuseAnArtifactOlderThanTheNotices(PublishedSlice.Executable, PublishedSlice.PublishCommand);

        var missing = Obligations
            .Where(obligation => !File.Exists(Path.Combine(PublishedSlice.Directory, obligation.Path)))
            .Select(obligation => $"{obligation.Path} ({obligation.Obligation})")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // The shipped copy is the repository's copy, byte for byte. Without
        // this the two could drift and the artifact would carry an older
        // disclaimer while the repository read correctly.
        var shipped = await File.ReadAllBytesAsync(Path.Combine(PublishedSlice.Directory, "THIRD-PARTY-NOTICES.txt"));
        var source = await File.ReadAllBytesAsync(NoticesFile);

        await Assert.That(shipped.SequenceEqual(source)).IsTrue();
    }

    /// <summary>
    /// Every obligation is inside the packed <c>.nupkg</c>, which is what a
    /// user's machine unpacks.
    /// </summary>
    /// <remarks>
    /// <b>Read from the package, because that is where item 13 found the
    /// absence.</b> Everything upstream of this was green: the source tree had
    /// the payload, the publish had the payload, and the package was still
    /// missing two of four. <c>vpk</c> lays the publish directory down under
    /// <c>lib/app/</c>, so that prefix is asserted rather than assumed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryNoticeIsInsideThePackedRelease()
    {
        var package = SuiteEnvironment.RequirePackagedRelease();
        RefuseAnArtifactOlderThanTheNotices(package, "pwsh -File build/New-Release.ps1");

        using var archive = await ZipFile.OpenReadAsync(package);

        var entries = archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The prefix itself, so that a vpk layout change is a named failure
        // rather than six mysterious ones.
        await Assert.That(entries).Contains("lib/app/BrowserAI.exe");

        var missing = Obligations
            .Where(obligation => !entries.Contains("lib/app/" + obligation.Path.Replace('\\', '/')))
            .Select(obligation => $"lib/app/{obligation.Path.Replace('\\', '/')} ({obligation.Obligation})")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // And the notices file inside the package says what the repository's
        // says, read out of the package rather than off disk.
        var notices = archive.GetEntry("lib/app/THIRD-PARTY-NOTICES.txt");
        await Assert.That(notices).IsNotNull();

        using var reader = new StreamReader(await notices!.OpenAsync());
        var text = await reader.ReadToEndAsync();

        await Assert.That(text).Contains("Permission is hereby granted");
        await Assert.That(text).Contains("Playwright is a trademark of Microsoft Corporation");

        // One needle from each of the two sections added at the plan's final
        // audit, so that a package shipping without them is caught here — where
        // the first run found the previous two absences — and not in the tree.
        await Assert.That(text).Contains("Version 2.0, January 2004");
        await Assert.That(text).Contains("Microsoft.Extensions packages");
    }

    /// <summary>
    /// Refuses an artifact older than the notices it is being asserted about.
    /// </summary>
    /// <remarks>
    /// <b>The same rule as <see cref="PublishedSlice.EnsureFresh"/>, for the
    /// same reason.</b> Editing the notices without rebuilding would fail these
    /// tests with <i>the notice is missing from the package</i>, which is true
    /// of that package and says nothing about the tree — a mystery failure
    /// pointing at the wrong thing. This names what to run instead.
    /// </remarks>
    /// <param name="artifact">The built thing under assertion.</param>
    /// <param name="remedy">The command that rebuilds it.</param>
    private static void RefuseAnArtifactOlderThanTheNotices(string artifact, string remedy)
    {
        var built = File.GetLastWriteTimeUtc(artifact);
        var notices = File.GetLastWriteTimeUtc(NoticesFile);

        if (notices > built)
        {
            throw new InvalidOperationException(
                $"'{artifact}' is older than '{NoticesFile}', so what it carries is a previous version of the notices and this test would report a defect in the tree that is really a stale build. Run: {remedy}");
        }
    }
}

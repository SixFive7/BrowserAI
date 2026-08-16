// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.IO.Compression;
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
        ("Velopack's MIT notice and the trademark disclaimer", "THIRD-PARTY-NOTICES.txt"),
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
    /// The Velopack version the notices file was copied against is the version
    /// the build resolved.
    /// </summary>
    /// <remarks>
    /// <b>A licence text is a measurement, and everything here floats.</b>
    /// Velopack resolves to latest at build time; a bump can change the licence,
    /// the copyright years or the holder, and a notice copied against an older
    /// version would then be a confident wrong answer rather than a gap. This
    /// makes the bump red until the text has been re-fetched from the new
    /// package's own commit.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheVelopackNoticeIsStampedWithTheVersionTheBuildResolved()
    {
        var resolved = ResolvedVersions.FromNuGetLocks("Velopack");
        await Assert.That(resolved).IsNotNull();

        var notices = await File.ReadAllTextAsync(NoticesFile);

        await Assert.That(notices).Contains($"Velopack {resolved} - MIT");
        await Assert.That(notices).Contains($"Retrieved 2026-08-16 against Velopack {resolved}.");
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The redistribution obligations, asserted against what actually ships.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these survived to a release gate because nothing looked.</b>
/// [Item 13](../../RELEASING.md) names the obligations that attach at
/// first installer handoff, independently of BrowserAI's own licence, and the
/// first run of that checklist — 2026-08-16, reading the packed
/// <c>.nupkg</c> rather than the source tree — found <b>two of the four it then
/// listed absent from an otherwise releasable package</b>: Velopack's MIT
/// notice, because a NuGet dependency's licence stays in the package cache and
/// is never copied to a publish output, and the trademark disclaimer, because no
/// upstream file carries one.
/// <i>Corrected 2026-08-17 (previously "names four obligations")</i> — the same
/// day's audit found the item itself short by two, the Apache-2.0 MCP SDK and
/// the MIT <c>Microsoft.Extensions.*</c> family, so it names six and this
/// sentence must not fix a count that is allowed to grow.
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
    /// <para>
    /// <b>Paths, not a count.</b> A count would go green on a file landing in
    /// the wrong place, and where these sit is what a licence obligation is
    /// about — a notice nobody can find beside the binary is not shipped.
    /// </para>
    /// <para>
    /// ⚠️ <b>A typed list can only be wrong in one direction, and 2026-09-17
    /// found it wrong in the other.</b> It can notice a path that is not in the
    /// artifact; it cannot notice a package that ships and is in nobody's list.
    /// <c>playwright</c> had been in the payload since the first build of one
    /// and is in neither this list nor the notices file. The three rows below
    /// were added 2026-09-18, and the half that keeps it from happening again is
    /// <see cref="TheNoticesNameEveryPackageThatShipsAndEveryFamilyThatIsProvisioned"/>,
    /// which reads the lock instead of a list.
    /// </para>
    /// </remarks>
    private static readonly (string Obligation, string Path)[] Obligations =
    [
        ("Node.js, whose LICENSE aggregates OpenSSL, ICU, V8, zlib and c-ares", @"payload\node\LICENSE"),
        ("@playwright/mcp, Apache-2.0", @"payload\mcp\node_modules\@playwright\mcp\LICENSE"),
        ("playwright-core, Apache-2.0", @"payload\mcp\node_modules\playwright-core\LICENSE"),
        ("playwright-core, the NOTICE section 4(d) propagates", @"payload\mcp\node_modules\playwright-core\NOTICE"),
        ("playwright-core, its own third-party notices", @"payload\mcp\node_modules\playwright-core\ThirdPartyNotices.txt"),
        ("playwright, Apache-2.0", @"payload\mcp\node_modules\playwright\LICENSE"),
        ("playwright, the NOTICE section 4(d) propagates", @"payload\mcp\node_modules\playwright\NOTICE"),
        ("playwright, its own third-party notices", @"payload\mcp\node_modules\playwright\ThirdPartyNotices.txt"),
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
    /// The notices name every package the payload ships and every browser family
    /// the product provisions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Both halves were wrong on 2026-09-17 and neither was findable by
    /// any test that existed.</b> <see cref="Obligations"/> is a list of paths
    /// somebody typed, so it can only be wrong in the direction of naming a path
    /// that is not there — it cannot notice a package that ships and is not
    /// named at all. The
    /// [licensing re-read](../../kb/packaging/dependencies.md#third-party-payload-as-shipped)
    /// found two of those: <c>playwright</c> has been in
    /// <c>build/payload/package-lock.json</c> since the first payload build
    /// while the notices said <i>"the two Playwright packages"</i>, and Firefox
    /// has been a provisioned family since 2026-08-19 while the notices named it
    /// only in a list of things no copy of which ships.
    /// </para>
    /// <para>
    /// <b>Enumerated from two sources that move on their own, which is the point
    /// of writing it this way.</b> The payload half reads the committed lock, so
    /// it runs on a clean clone and a package arriving on a later roll is a red
    /// build here rather than a name nobody noticed was missing. The browser
    /// half reads <c>ProvisionedBrowsers.Families</c>, which is the product's own
    /// list and the thing a third family would be added to.
    /// </para>
    /// <para>
    /// <b>The path half needs an assembled payload and the naming half does
    /// not</b>, so the payload's absence is recorded rather than skipped: an arm
    /// that threw away the half not needing a payload would cover less on a
    /// clean clone than a hand-written list did.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNoticesNameEveryPackageThatShipsAndEveryFamilyThatIsProvisioned()
    {
        var notices = await File.ReadAllTextAsync(NoticesFile);
        var packages = PayloadLockPackages();
        var offences = new List<string>();

        // ⚠️ Read against a whitespace-flattened copy where a needle spans two
        // words, because this file is hard-wrapped prose: `playwright-core
        // 1.64.0-alpha-2026-09-17` is one string to a reader and two lines to a
        // text editor, and a needle that a rewrap can break fails on the layout
        // rather than on the claim. Found the same afternoon it was written.
        var flattened = Regex.Replace(notices, @"\s+", " ");

        // Not vacuous: a lock that parsed to nothing would make every loop below
        // pass, which is the failure mode a derived list has.
        await Assert.That(packages.Count).IsGreaterThan(2);

        // ⚠️ A WHOLE-WORD MATCH, and a plain Contains would have passed on the
        // very omission this arm was written for: `playwright` is a substring of
        // both `@playwright/mcp` and `playwright-core`, so a file naming neither
        // of the other two would still "name" it.
        offences.AddRange(packages
            .Where(package => !NamedInItsOwnRight(notices, package))
            .Select(package => $"the payload ships '{package}' and THIRD-PARTY-NOTICES.txt does not name it"));

        // And the count the prose states, so "the two Playwright packages"
        // cannot survive a third arriving.
        if (!flattened.Contains($"{packages.Count} Playwright packages", StringComparison.Ordinal))
        {
            offences.Add($"the payload ships {packages.Count} Playwright packages and the notices do not say so");
        }

        // ⚠️ EVERY VERSION THIS FILE STATES ABOUT A PAYLOAD PACKAGE IS READ BACK
        // OUT OF THE LOCK. It states two, and it states them because they
        // DISAGREE: an npm override pins `playwright-core` one build ahead of
        // what `@playwright/mcp` declares, so a reader who sees two Playwright
        // versions in one payload would otherwise have no way to tell a pin from
        // a mistake. A number in this file that nothing checks is a number that
        // goes stale silently, which is the argument StampedPackages already
        // won for the NuGet half.
        foreach (var package in StampedPayloadPackages)
        {
            var resolved = ResolvedVersions.FromPayloadLock(package);

            await Assert.That(resolved).IsNotNull();

            if (!flattened.Contains($"{package} {resolved}", StringComparison.Ordinal))
            {
                offences.Add($"the payload resolved {package} {resolved} and the notices do not say so");
            }
        }

        // Every family the product will provision on demand gets an entry of its
        // own, saying what is downloaded and where that thing's terms are. The
        // bare NAME is not enough and was never the gap: Firefox has been in a
        // list of things no copy of which ships since before it was a family,
        // and a reader could still not find its licence from that sentence.
        var provisioned = ProvisionedBrowsers.Families
            .Where(family => !EntryFor(notices, family))
            .Select(family => $"'{family}' is a provisioned family and THIRD-PARTY-NOTICES.txt has no entry for it under '{ProvisionedHeading}'");

        offences.AddRange(provisioned);

        if (!SuiteEnvironment.HasRepositoryPayload())
        {
            await Assert.That(string.Join(Environment.NewLine, offences)).IsEmpty();
            return;
        }

        // The path half. Every package that carries one of these files in the
        // assembled payload must have that path in the notices, at the spelling
        // a reader would use to find it beside the binary.
        foreach (var package in packages)
        {
            var directory = Path.Combine(
                RepositoryPayload.Layout.Root, "mcp", "node_modules", package.Replace('/', Path.DirectorySeparatorChar));

            foreach (var file in (string[])["LICENSE", "NOTICE", "ThirdPartyNotices.txt"])
            {
                if (!File.Exists(Path.Combine(directory, file)))
                {
                    continue;
                }

                var shipped = $@"payload\mcp\node_modules\{package.Replace('/', '\\')}\{file}";

                if (!notices.Contains(shipped, StringComparison.Ordinal))
                {
                    offences.Add($"'{shipped}' is in the payload and the notices do not point at it");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offences)).IsEmpty();
    }

    /// <summary>
    /// <c>README.md</c>'s third-party tables name every package the payload
    /// ships and every family the product provisions, and every browser revision
    /// they state is the one the committed <c>browsers.json</c> names today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The same two omissions, one document across.</b>
    /// <see cref="TheNoticesNameEveryPackageThatShipsAndEveryFamilyThatIsProvisioned"/>
    /// closed them in <c>THIRD-PARTY-NOTICES.txt</c> on 2026-09-18 and nothing
    /// looked at the table in <c>README.md</c> that answers the same question for
    /// a reader rather than for a recipient. That table still read <i>full
    /// <c>chromium</c> 1237</i> against a payload that resolves 1245, still
    /// carried a <c>chromium-headless-shell</c> row for a tree nothing has
    /// provisioned since <c>--no-shell</c> on 2026-08-16, and still had no
    /// Firefox row although Firefox has been a provisioned family since
    /// 2026-08-19. <b>The file that ships was mechanised and the file that
    /// explains it was not</b>, which is how one correction leaves half of itself
    /// behind.
    /// </para>
    /// <para>
    /// <b>Three sources, none of them typed here, and all three committed.</b>
    /// The package half reads <c>build/payload/package-lock.json</c>, the family
    /// half reads <see cref="ProvisionedBrowsers.Families"/>, and the revision
    /// half reads <c>upstream-snapshots/browsers.json</c> through
    /// <see cref="BrowserAiPaths.RevisionOf"/> — which
    /// <c>build/UpstreamSnapshots.targets</c> regenerates from the resolved
    /// payload and diffs on every build, so the snapshot and the payload cannot
    /// drift apart without failing the build first. Being committed is what lets
    /// this arm run whole on a clean clone, where the notices arm's path half
    /// cannot.
    /// </para>
    /// <para>
    /// ⚠️ <b>A <c>previously "…"</c> span is cut out before the revisions are
    /// read, and that exemption is the point rather than a concession.</b> A
    /// correction stamp records what a cell used to say, and that clause is the
    /// load-bearing half: it is what tells a reader who learned 1237 that the
    /// number was reviewed and replaced rather than lost. Holding it to today's
    /// manifest would demand the record be rewritten at every roll, which is the
    /// opposite of what a stamp is for.
    /// </para>
    /// <para>
    /// <b>A scan that matches nothing is indistinguishable from a table with
    /// nothing wrong in it, so the positive control is inside the arm</b>: each
    /// provisioned family must state a revision of its own in the downloads
    /// table, which goes red if the pattern stops matching or if the
    /// <c>previously</c> cut swallows a live cell. The shared components are held
    /// to today's revision only if they state one, because what those rows are
    /// about is where a licence file sits rather than which revision is current.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReadmeTablesNameEveryPackageThatShipsAndEveryFamilyThatIsProvisioned()
    {
        var section = ReadmeThirdPartySection();
        var rows = DownloadsTableRows(section);
        var packages = PayloadLockPackages();
        var offences = new List<string>();

        // Neither derivation may come back empty: a lock that parsed to nothing
        // and a table that matched nothing each make every loop below vacuous,
        // which is the failure mode a derived list has and a typed one does not.
        await Assert.That(packages.Count).IsGreaterThan(2);
        await Assert.That(rows.Count).IsGreaterThan(2);

        // A whole-word match for the reason the notices arm gives: `playwright`
        // is a substring of the two package ids that were never missing.
        offences.AddRange(packages
            .Where(package => !NamedInItsOwnRight(section, package))
            .Select(package => $"the payload ships '{package}' and README.md's third-party components section does not name it"));

        // The row, not the name. Every family is already named in the prose
        // above the table and in the trademark paragraph below it; what a reader
        // cannot find without a row of its own is where that family's terms are.
        offences.AddRange(ProvisionedBrowsers.Families
            .Where(family => !rows.Any(row => NamedInItsOwnRight(FirstCell(row), family)))
            .Select(family => $"'{family}' is a provisioned family and README.md's '{DownloadsHeading}' table has no row of its own naming it"));

        foreach (var component in (string[])[.. ProvisionedBrowsers.Families, .. ProvisionedBrowsers.SharedComponents])
        {
            var expected = BrowserAiPaths.RevisionOf(component);
            var stated = RevisionsStatedFor(rows, component);

            offences.AddRange(stated
                .Where(revision => !string.Equals(revision, expected, StringComparison.Ordinal))
                .Select(revision => $"README.md's '{DownloadsHeading}' table says '{component} {revision}' and the committed browsers.json snapshot says {expected}"));

            if (stated.Count is 0 && ProvisionedBrowsers.Families.Contains(component))
            {
                offences.Add(
                    $"README.md's '{DownloadsHeading}' table states no revision beside the provisioned family '{component}', so nothing here can tell whether that row moved with the payload");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offences)).IsEmpty();
    }

    /// <summary>The heading the provisioned-browser entries live under.</summary>
    private const string ProvisionedHeading = "Browsers provisioned on first run";

    /// <summary>The heading <c>README.md</c>'s third-party tables live under.</summary>
    private const string ThirdPartyHeading = "### Third-party components";

    /// <summary>The sentence the downloads table sits under.</summary>
    /// <remarks>
    /// <b>The stable head of it, not the whole line.</b> The sentence ends in a
    /// clause about obligations that is free to be reworded; the subject is what
    /// identifies the table.
    /// </remarks>
    private const string DownloadsHeading = "What the user's machine downloads";

    /// <summary>
    /// A correction stamp's record of what a cell used to say, which is exempt
    /// from the revision check for the reason
    /// <see cref="TheReadmeTablesNameEveryPackageThatShipsAndEveryFamilyThatIsProvisioned"/>
    /// states.
    /// </summary>
    private static readonly Regex PreviouslyClause = new(@"previously[^""\n]{0,8}""[^""]*""", RegexOptions.IgnoreCase);

    /// <summary><c>README.md</c>'s third-party components section, as text.</summary>
    /// <remarks>
    /// <b>Anchored on the heading and refused when it is gone</b>, rather than
    /// scanning the whole file: every number outside this section is either a
    /// measurement of something else or a <c>previously</c> clause recording an
    /// old one, and a scan that read those would demand the history be rewritten.
    /// </remarks>
    /// <returns>The section body, from the heading to the next one.</returns>
    /// <exception cref="InvalidOperationException">The heading is gone.</exception>
    private static string ReadmeThirdPartySection()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, "README.md"));
        var at = readme.IndexOf(ThirdPartyHeading, StringComparison.Ordinal);

        if (at < 0)
        {
            throw new InvalidOperationException(
                $"README.md carries no '{ThirdPartyHeading}' heading, which is this check's anchor: the section was renamed or moved and nothing here can say what its tables claim. Re-anchor this test on the new heading rather than deleting it.");
        }

        var body = readme[(at + ThirdPartyHeading.Length)..];
        var next = Regex.Match(body, "(?m)^#{1,3} ");

        return next.Success ? body[..next.Index] : body;
    }

    /// <summary>
    /// The rows of the <i>what the user's machine downloads</i> table.
    /// </summary>
    /// <remarks>
    /// <b>The contiguous run of table lines under the sentence</b>, the way
    /// <c>HazardIndex.TableLines</c> walks its own: a table is where it is
    /// because of what it sits under, and taking every line in the section that
    /// starts with a pipe would fold in the redistribution table above it, whose
    /// rows make the opposite claim.
    /// </remarks>
    /// <param name="section">The third-party components section.</param>
    /// <returns>The rows, the alignment rule excluded.</returns>
    /// <exception cref="InvalidOperationException">The sentence is gone.</exception>
    private static IReadOnlyList<string> DownloadsTableRows(string section)
    {
        var at = section.IndexOf(DownloadsHeading, StringComparison.Ordinal);

        if (at < 0)
        {
            throw new InvalidOperationException(
                $"README.md's third-party section carries no '{DownloadsHeading}' sentence, which is the anchor its table is found by. Re-anchor this test on the new wording rather than deleting it.");
        }

        return
        [
            .. section[at..]
                .Split('\n')
                .SkipWhile(line => !line.StartsWith('|'))
                .TakeWhile(line => line.StartsWith('|'))
                .Where(line => !Regex.IsMatch(line, @"^\|[\s|:-]*$")),
        ];
    }

    /// <summary>A row's first cell, which is the component it is about.</summary>
    /// <param name="row">The table row.</param>
    /// <returns>The first cell, or the row when it has none.</returns>
    private static string FirstCell(string row) =>
        row.Split('|', StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : row;

    /// <summary>
    /// Every revision the downloads table states beside a component's name.
    /// </summary>
    /// <remarks>
    /// <b>Adjacency is the whole definition.</b> A number elsewhere in a cell is
    /// a file size, a line count or a year, and the one thing that reads as
    /// <i>this is the revision that ships</i> is a number written next to the
    /// component it belongs to.
    /// </remarks>
    /// <param name="rows">The table rows.</param>
    /// <param name="component">The component, as upstream names it.</param>
    /// <returns>The revisions stated, in the order they appear.</returns>
    private static IReadOnlyList<string> RevisionsStatedFor(IEnumerable<string> rows, string component) =>
    [
        .. rows
            .Select(row => PreviouslyClause.Replace(row, " "))
            .SelectMany(row => Regex.Matches(
                row,
                $@"(?<![\w@./\\-]){Regex.Escape(component)}(?![\w./\\-])[^\w\n]{{0,6}}(\d{{3,5}})"))
            .Select(match => match.Groups[1].Value),
    ];

    /// <summary>
    /// The payload packages whose resolved version the notices state, and which
    /// must therefore state the version the payload actually resolved.
    /// </summary>
    /// <remarks>
    /// <b>Two, because two disagree.</b> The notices do not stamp a version on
    /// every package they name — the obligation is about the path a licence sits
    /// at, not about a number — but they do stamp the pair an npm override
    /// separated, because an unexplained pair of Playwright versions in one
    /// payload reads as a mistake.
    /// </remarks>
    private static readonly string[] StampedPayloadPackages = ["playwright", "playwright-core"];

    /// <summary>
    /// Whether the notices name a package as itself rather than as part of a
    /// longer id.
    /// </summary>
    /// <param name="notices">The notices text.</param>
    /// <param name="package">The package id.</param>
    /// <returns>Whether it is named in its own right.</returns>
    private static bool NamedInItsOwnRight(string notices, string package) =>
        Regex.IsMatch(
            notices,
            $@"(?<![\w@./\\-]){Regex.Escape(package)}(?![\w./\\-])");

    /// <summary>
    /// Whether the provisioned-browsers block carries an entry for a family.
    /// </summary>
    /// <remarks>
    /// <b>Scoped to the block rather than to the file</b>, because every family
    /// is already named elsewhere — in the trademark disclaimer, and in the
    /// sentence saying no copy of any browser ships. What has to exist is the
    /// entry that says where that family's terms are, and only the block can
    /// answer that.
    /// </remarks>
    /// <param name="notices">The notices text.</param>
    /// <param name="family">The family, as upstream names it.</param>
    /// <returns>Whether the block has an entry for it.</returns>
    private static bool EntryFor(string notices, string family)
    {
        var at = notices.IndexOf(ProvisionedHeading, StringComparison.Ordinal);

        if (at < 0)
        {
            return false;
        }

        var block = notices[at..];
        var end = block.IndexOf("\nTrademarks", StringComparison.Ordinal);

        return Regex.IsMatch(
            end < 0 ? block : block[..end],
            $@"(?m)^\s\s{Regex.Escape(family)}\s");
    }

    /// <summary>
    /// Every npm package the payload ships, read from the committed lock.
    /// </summary>
    /// <remarks>
    /// <b>The lock rather than the assembled tree</b>, for the reason
    /// <see cref="ResolvedVersions"/> gives: the lock is committed, so this list
    /// is the same on a clean clone as on a machine that has built a payload,
    /// and a test that read the tree would go quiet exactly when nobody had
    /// assembled one.
    /// </remarks>
    /// <returns>The package ids, in order.</returns>
    private static IReadOnlyList<string> PayloadLockPackages()
    {
        var path = Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "package-lock.json");

        using var lockFile = JsonDocument.Parse(File.ReadAllText(path));

        return
        [
            .. lockFile.RootElement.GetProperty("packages").EnumerateObject()
                .Select(entry => entry.Name)
                .Where(name => name.StartsWith("node_modules/", StringComparison.Ordinal))
                .Select(name => name["node_modules/".Length..])
                .Order(StringComparer.Ordinal),
        ];
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

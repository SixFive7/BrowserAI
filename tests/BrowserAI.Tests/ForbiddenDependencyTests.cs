// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests;

/// <summary>
/// The packages this repository may not take, asserted where a package version
/// is allowed to appear at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a banned symbol.</b> <c>BannedSymbols.txt</c> is the right
/// mechanism for an API and the wrong one for a dependency. With no
/// <c>PackageReference</c> the forbidden type does not resolve, so the analyzer
/// matches nothing and the entry sits there reading as coverage while providing
/// none -- it could only ever fire after somebody had already added the package
/// <b>and</b> written code against it. The reference is the earlier signal and
/// the one that costs least to reverse, so the reference is what is asserted.
/// </para>
/// <para>
/// <b>One file is enough because one file is all there is.</b> Central package
/// management plus transitive pinning makes <c>Directory.Packages.props</c> the
/// only place in the repository where a package version may appear, and
/// <c>BuildConfigurationTests</c> already fails the build on a <c>Version=</c>
/// attribute anywhere else. The project files are read here as well anyway: a
/// <c>PackageReference</c> with no version is still a reference, and it is
/// exactly what someone adding a package the quick way would write.
/// </para>
/// </remarks>
internal sealed class ForbiddenDependencyTests
{
    [Test]
    public async Task NoProjectDrivesPlaywrightDirectly()
    {
        // The scope boundary, from CLAUDE.md: "Never drive Playwright directly
        // -- no Microsoft.Playwright, no reimplementation of the snapshot/ref
        // system, response formatting or error shaping." BrowserAI is a proxy;
        // it spawns @playwright/mcp and forwards JSON-RPC. Taking the .NET
        // binding would make a second, silent way to reach the browser, and the
        // first tool composed out of it is a charter change nobody voted on.
        //
        // Until 2026-08-17 nothing enforced this at all.
        await Assert.That(string.Join(Environment.NewLine, Mentioning("Microsoft.Playwright"))).IsEmpty();
    }

    [Test]
    public async Task NeitherFluentAssertionsNorTheTestSdkIsReferenced()
    {
        // Both are named in CLAUDE.md and in Directory.Packages.props's own
        // comment, and both were held by nothing but that comment until
        // 2026-08-17.
        //
        // FluentAssertions relicensed at 8.0.0 to a commercial tier, and the
        // float in this repository resolves to latest by construction -- so
        // "we would take an old one" is not available as an answer here.
        //
        // Microsoft.NET.Test.Sdk conflicts with TUnit, which is MTP-only. It is
        // also the single most likely package for someone to add on reflex,
        // because every other .NET test project in the world has it.
        await Assert.That(string.Join(Environment.NewLine, Mentioning("FluentAssertions"))).IsEmpty();
        await Assert.That(string.Join(Environment.NewLine, Mentioning("Microsoft.NET.Test.Sdk"))).IsEmpty();

        // And the scan is looking at the file that matters, so neither
        // assertion can pass by reading nothing.
        await Assert.That(RepositoryLayout.BuildFiles.Any(file =>
            string.Equals(file.Name, "Directory.Packages.props", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(Mentioning("TUnit").Any()).IsTrue();
    }

    /// <summary>
    /// The <c>Microsoft.Data.Sqlite</c> meta package is never referenced,
    /// anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is the convenient one, and taking it silently downgrades the
    /// native SQLite this repository pins in its own tree.</b> The meta package
    /// depends on <c>SQLitePCLRaw.bundle_e_sqlite3</c>, which carries its own
    /// pinned native build -- 3.53.0 through bundle 2.1.12 when this was written
    /// -- so a reference added "for convenience" replaces the version
    /// <c>third-party/sqlite</c> holds with an older one, and everything keeps
    /// working. Nothing in a lock file reads as wrong; the amalgamation is
    /// still vendored, the drift row is still accurate, and the binary is
    /// simply not running the SQLite anybody chose.
    /// </para>
    /// <para>
    /// <b>The <c>.Core</c> package is a different question and is not banned
    /// here.</b> It carries no native library at all, so it cannot do this --
    /// what it would cost is three managed packages and their notices in front
    /// of a publish that fails on one ILC warning, which is a trade somebody
    /// may legitimately want to make later. This bans the one that fails
    /// silently, not the one that costs.
    /// </para>
    /// <para>
    /// <b><c>SourceGear.sqlite3</c> is the positive control and is deliberately
    /// present</b>, in the test project only: it is what puts an
    /// <c>e_sqlite3.dll</c> beside a CoreCLR test host, and its version number
    /// is the SQLite version, so the float writes today's SQLite into
    /// <c>packages.lock.json</c> and the gap against the pin is visible for
    /// free.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSqliteMetaPackageIsNeverReferenced()
    {
        await Assert.That(string.Join(Environment.NewLine, Mentioning("Microsoft.Data.Sqlite"))).IsEmpty();

        // The control: the scan can see a SQLite package reference, so the
        // assertion above is an absence and not a matcher that stopped
        // matching. This is also the reference that must never move into the
        // product, because the publish output is a single file by construction
        // of there being no native package in its graph.
        var native = Mentioning("SourceGear.sqlite3").ToList();

        // ⚠️ THREE SINCE 2026-08-26 (previously two). The test PROBE opens
        // sessions too, and since the cutover a session is a database -- so a
        // CoreCLR probe with no `e_sqlite3.dll` beside it dies on
        // `DllNotFoundException` and reports as a race that nobody won, which is
        // exactly what sixteen contenders did. The count is asserted and not
        // bounded because the thing that must never happen is a FOURTH one under
        // `src\`, and a `>=` would not see it.
        await Assert.That(native.Count).IsEqualTo(3);
        await Assert.That(native.Any(line => line.StartsWith("Directory.Packages.props", StringComparison.Ordinal))).IsTrue();
        await Assert.That(native.Any(line => line.Contains("BrowserAI.Tests.csproj", StringComparison.Ordinal))).IsTrue();
        await Assert.That(native.Any(line => line.Contains("BrowserAI.TestProbe.csproj", StringComparison.Ordinal))).IsTrue();
        await Assert.That(native.Any(line => line.Contains($"src{Path.DirectorySeparatorChar}BrowserAI", StringComparison.Ordinal))).IsFalse();
    }


    /// <summary>
    /// A project under <c>src/</c> that references the code generator ships with
    /// the notice of every metadata package the generator reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Inverted 2026-09-24, when the maintainer withdrew the rule it
    /// enforced.</b> <i>Previously
    /// <c>NoProjectUnderSrcReferencesTheCodeGenerator</c>, which refused any
    /// <c>Include="Microsoft.Windows.CsWin32"</c> under <c>src/</c> because "no
    /// generated code ships: DECISIONS.md, decided 2026-08-20, and a reference
    /// under src/ is what reverses it".</i> Q274, his words verbatim: <i>"q274 c be
    /// liberal with the license interpretation. I really believe it is ok."</i>
    /// Generated code may ship now, CsWin32 output from Microsoft's Windows
    /// metadata and C#/WinRT projections included, and the licence contradiction
    /// <c>QUESTIONS.md</c> section 12 documents is resolved by his reading. The
    /// decision of record is the <c>Generated code</c> row of <c>DECISIONS.md</c>.
    /// </para>
    /// <para>
    /// <b>What the decision kept is what this asserts: the first commit that
    /// ships generated output from third-party metadata carries that metadata's
    /// notice.</b> <c>THIRD-PARTY-NOTICES.txt</c> ships beside the binary, and
    /// generated declarations compile into whatever references the generator, so
    /// a shipping reference with no notice is a redistribution with nothing
    /// travelling with it. The packages named are the generator's own
    /// dependencies as the test project's lock file resolves them, read and not
    /// typed, so a fourth metadata package arriving with a CsWin32 bump joins the
    /// list by itself.
    /// </para>
    /// <para>
    /// <b>Why the reference is still the signal.</b> Generated code has no mark in
    /// a compiled binary and no file in the tree, so the reference by a shipping
    /// project remains the one earlier signal there is. <b>C#/WinRT projections
    /// are not covered here</b>: they arrive through a target framework carrying a
    /// Windows SDK version or a <c>Microsoft.Windows.CsWinRT</c> reference, and the
    /// notice that goes with them is a reader's job, which the decision row says.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by adding the generator's reference to
    /// <c>src/BrowserAI/BrowserAI.csproj</c> as text, with no notice beside it,
    /// and watching this arm name the project line and each of the three metadata
    /// packages; the doctored project was reverted before anything built it. The
    /// layout oracle's two references are still required, so losing the only
    /// independent check of the seven hand-written interop structs is a red build
    /// as it was before.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectUnderSrcThatReferencesTheCodeGeneratorShipsTheMetadatasNotice()
    {
        var references = Mentioning(CodeGenerator).ToList();
        var metadata = MetadataTheGeneratorReads();
        var notices = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, NoticesFile));

        // THE RULE, stated before its controls so that a violation fails on the
        // sentence naming the project and the package.
        await Assert.That(string.Join(Environment.NewLine, MissingNotices(references, metadata, notices)))
            .IsEmpty()
            .Because("generated output from third-party metadata ships with that metadata's notice: DECISIONS.md, Generated code, decided 2026-09-24");

        // ⚠️ THE LIST IS READ, SO IT HAS TO BE THERE. An empty read would make
        // every shipping reference pass, which is the vacuity this arm exists to
        // refuse.
        await Assert.That(metadata).Contains("Microsoft.Windows.SDK.Win32Metadata");

        // The oracle stays, and the scan still sees it: the central version and
        // the test project's layout-oracle reference.
        await Assert.That(references.Any(line => line.StartsWith("Directory.Packages.props", StringComparison.Ordinal))).IsTrue();
        await Assert.That(references.Any(line => line.Contains("BrowserAI.Tests.csproj", StringComparison.Ordinal))).IsTrue();

        // ⚠️ THE CONTROL, in both directions over lines this arm composes: a
        // shipping reference with no notice is named once per package, the same
        // reference beside every notice is not, and a test project's reference
        // is never a shipping one.
        string[] shipping = [$"src{Path.DirectorySeparatorChar}BrowserAI{Path.DirectorySeparatorChar}BrowserAI.csproj:9: <PackageReference Include=\"{CodeGenerator}\" />"];
        string[] oracle = [$"tests{Path.DirectorySeparatorChar}BrowserAI.Tests{Path.DirectorySeparatorChar}BrowserAI.Tests.csproj:9: <PackageReference Include=\"{CodeGenerator}\" />"];

        await Assert.That(MissingNotices(shipping, metadata, "no notice here").Count).IsEqualTo(metadata.Count);
        await Assert.That(MissingNotices(shipping, metadata, string.Join(Environment.NewLine, metadata))).IsEmpty();
        await Assert.That(MissingNotices(oracle, metadata, "no notice here")).IsEmpty();
    }

    /// <summary>The generator whose output the rule above is about.</summary>
    private const string CodeGenerator = "Microsoft.Windows.CsWin32";

    /// <summary>The file that travels beside the binary with every notice in it.</summary>
    private const string NoticesFile = "THIRD-PARTY-NOTICES.txt";

    /// <summary>
    /// The packages the generator depends on, as the test project's lock file
    /// resolves them.
    /// </summary>
    /// <returns>Their ids, in order.</returns>
    private static List<string> MetadataTheGeneratorReads()
    {
        var lockFile = Path.Combine(RepositoryLayout.Root.FullName, "tests", "BrowserAI.Tests", "packages.lock.json");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockFile));

        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            if (framework.Value.TryGetProperty(CodeGenerator, out var generator)
                && generator.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    _ = found.Add(dependency.Name);
                }
            }
        }

        return [.. found];
    }

    /// <summary>One complaint per shipping reference per metadata notice it lacks.</summary>
    /// <param name="references">What <see cref="Mentioning"/> found for the generator.</param>
    /// <param name="metadata">The packages the generator reads.</param>
    /// <param name="notices">The notices file's text.</param>
    /// <returns>The complaints.</returns>
    private static List<string> MissingNotices(IEnumerable<string> references, IReadOnlyList<string> metadata, string notices) =>
    [
        .. from reference in references
           where reference.StartsWith($"src{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           from package in metadata
           where !notices.Contains(package, StringComparison.Ordinal)
           select $"{reference} ships generated output and {NoticesFile} does not name {package}, whose metadata that output is generated from",
    ];

    /// <summary>Every build file that declares a package, and where it declares it.</summary>
    /// <remarks>
    /// <para>
    /// <b>It matches the <c>Include=</c> attribute and not the bare name,
    /// and that distinction is load-bearing, not tidy.</b>
    /// <c>Directory.Packages.props</c> names FluentAssertions and
    /// <c>Microsoft.NET.Test.Sdk</c> in a comment, precisely in order to forbid
    /// them -- so a substring scan reports the prohibition itself as a violation,
    /// which it did on the first run. Writing down why a rule exists must not
    /// violate the rule.
    /// </para>
    /// <para>
    /// A commented-out <c>Include=</c> still matches, and that is deliberate: it
    /// is a reference waiting for somebody to delete four characters.
    /// </para>
    /// </remarks>
    /// <param name="package">The package identifier.</param>
    /// <returns>One line per offending file, naming the line it was found on.</returns>
    private static IEnumerable<string> Mentioning(string package) =>
        from file in RepositoryLayout.BuildFiles
        from line in File.ReadAllLines(file.FullName).Index()
        where line.Item.Contains($"Include=\"{package}\"", StringComparison.OrdinalIgnoreCase)
        select $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}:{line.Index + 1}: {line.Item.Trim()}";
}

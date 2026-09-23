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
    /// The code generator is referenced by no project under <c>src/</c>, which is
    /// the mechanism behind a standing decision that no generated code ever ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The decision, 2026-08-20.</b> The maintainer ruled that no generated
    /// code will ever ship -- not as a <i>for now</i> but as a standing rule -- and
    /// that is what makes the CsWin32 metadata licence question unreachable rather
    /// than open. The terms are quoted in full in <c>QUESTIONS.md</c> section 12
    /// and the contradiction they carry is real and unresolved; the only act that
    /// would engage it has been ruled out. The decision of record is in
    /// <c>DECISIONS.md</c>.
    /// </para>
    /// <para>
    /// <b>Why the reference is the assertion and not the output.</b> Generated code
    /// has no distinguishing mark in a compiled binary and no file in the tree, so
    /// there is nothing downstream to scan. What can be asserted is the one thing
    /// that has to happen first: the generator has to be referenced by a project
    /// that ships. <c>PrivateAssets="all"</c> keeps it out of the product's
    /// closure today, and that attribute is load-bearing, not tidy -- but it is
    /// only load-bearing where the reference is, so the rule is about WHERE.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-23</b> by adding the reference to
    /// <c>src/BrowserAI/BrowserAI.csproj</c> and watching the arm name the file
    /// and the line; the doctored project was then reverted. The two references
    /// that must stay are asserted by count, so deleting the layout oracle is a
    /// red build as well -- it is the only independent check that the seven
    /// hand-written interop structs match what Windows expects, and the absence of
    /// a shipped generator is not a reason to lose it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoProjectUnderSrcReferencesTheCodeGenerator()
    {
        var references = Mentioning("Microsoft.Windows.CsWin32").ToList();

        // THE RULE, and it is stated before its control so that a violation
        // fails on the sentence that names the offending file instead of on a
        // count that names a number.
        await Assert.That(string.Join(
            Environment.NewLine,
            references.Where(line => line.StartsWith($"src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))))
            .IsEmpty()
            .Because("no generated code ships: DECISIONS.md, decided 2026-08-20, and a reference under src/ is what reverses it");

        // ⚠️ THE CONTROL, because the assertion above is an absence and a scan
        // that stopped matching would report the tree clean. The generator IS
        // referenced, in the two places it belongs: the central version, and the
        // test project that uses it as a layout oracle.
        await Assert.That(references.Count).IsEqualTo(2);
        await Assert.That(references.Any(line => line.StartsWith("Directory.Packages.props", StringComparison.Ordinal))).IsTrue();
        await Assert.That(references.Any(line => line.Contains("BrowserAI.Tests.csproj", StringComparison.Ordinal))).IsTrue();
    }

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

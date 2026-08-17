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
/// none — it could only ever fire after somebody had already added the package
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

    /// <summary>Every build file that names a package, and where it names it.</summary>
    /// <param name="package">The package identifier.</param>
    /// <returns>One line per offending file, naming the line it was found on.</returns>
    private static IEnumerable<string> Mentioning(string package) =>
        from file in RepositoryLayout.BuildFiles
        from line in File.ReadAllLines(file.FullName).Index()
        where line.Item.Contains(package, StringComparison.OrdinalIgnoreCase)
        select $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}:{line.Index + 1}: {line.Item.Trim()}";
}

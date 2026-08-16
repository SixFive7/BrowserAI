// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the properties of the build configuration that the versioning
/// policy depends on.
/// </summary>
/// <remarks>
/// <para>
/// These are build-order step 1's done-tests, expressed as tests rather than
/// as a checklist, because a rule that can be a failing test must be one. The
/// failure they prevent is specific: a hard-coded version in a project file
/// wins for that project while <c>packages.lock.json</c> honestly records it,
/// so the artifact contains a version nobody chose and nothing says it is old.
/// </para>
/// <para>
/// A red test here is never fixed by editing the test. It is fixed by removing
/// the version from the project file.
/// </para>
/// </remarks>
internal sealed partial class BuildConfigurationTests
{
    [Test]
    public async Task NoProjectFileDeclaresAPackageVersion()
    {
        var offenders = RepositoryLayout.ProjectFiles
            .SelectMany(file => XDocument.Load(file.FullName).Descendants()
                .Where(element =>
                    (element.Name.LocalName is "PackageReference" && element.Attribute("Version") is not null)
                    || element.Name.LocalName is "PackageVersion")
                .Select(element => $"{Relative(file)}: <{element.Name.LocalName} Include=\"{element.Attribute("Include")?.Value}\" Version=\"{element.Attribute("Version")?.Value}\" />"))
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task NoProjectFileContainsAVersionLiteral()
    {
        // Comments are stripped first: the prose in these files explains the
        // policy, and explaining it must not be indistinguishable from breaking
        // it. Everything that survives is build input.
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProjectFiles)
        {
            var document = XDocument.Load(file.FullName);
            document.DescendantNodes().OfType<XComment>().Remove();

            offenders.AddRange(VersionLiteral()
                .Matches(document.ToString())
                .Select(match => $"{Relative(file)}: {match.Value}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task EveryCentrallyManagedPackageVersionFloats()
    {
        var packages = CentralPackageManagement().Descendants()
            .Where(element => element.Name.LocalName is "PackageVersion")
            .ToList();

        // An empty file would pass the "all float" check vacuously, which would
        // make this test green on the day someone deletes the item group.
        await Assert.That(packages).IsNotEmpty();

        var pinned = packages
            .Where(element => element.Attribute("Version")?.Value is not "*")
            .Select(element => $"{element.Attribute("Include")?.Value} = {element.Attribute("Version")?.Value}")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, pinned)).IsEmpty();
    }

    [Test]
    public async Task CentralPackageManagementIsOnWithTransitivePinning()
    {
        var properties = CentralPackageManagement().Descendants()
            .Where(element => element.Name.LocalName is "ManagePackageVersionsCentrally" or "CentralPackageTransitivePinningEnabled")
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);

        await Assert.That(properties.GetValueOrDefault("ManagePackageVersionsCentrally")).IsEqualTo("true");
        await Assert.That(properties.GetValueOrDefault("CentralPackageTransitivePinningEnabled")).IsEqualTo("true");
    }

    [Test]
    public async Task NoBuildFileSuppressesWarnings()
    {
        // This is the mechanism that actually protects CS0162, and every other
        // warning-as-error, from being demoted in bulk.
        //
        // Measured 2026-08-16, SDK 10.0.302: NoWarn beats BOTH
        // WarningsAsErrors and an .editorconfig `dotnet_diagnostic` severity
        // for compiler diagnostics. With NoWarn set to CS0162, a full rebuild
        // of a method holding a statement after a `return` reported 0 warnings
        // and 0 errors under each of them. So naming a warning in
        // WarningsAsErrors cannot defend it, and only something outside the
        // compiler's own precedence order can. That is this test.
        //
        // Per-site suppression is untouched and remains the sanctioned route:
        // a `#pragma warning disable` or an [SuppressMessage] at the line that
        // needs it, with the reason beside it, is visible in a diff. A shared
        // property is not.
        var offenders = RepositoryLayout.BuildFiles
            .SelectMany(file => XDocument.Load(file.FullName).Descendants()
                .Where(element => element.Name.LocalName is "NoWarn" or "WarningsNotAsErrors")
                .Select(element => $"{Relative(file)}: <{element.Name.LocalName}>{element.Value}</{element.Name.LocalName}>"))
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    private static XDocument CentralPackageManagement() =>
        XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "Directory.Packages.props"));

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName);

    // Three or more dot-separated numbers. Two parts are excluded on purpose:
    // `net10.0-windows` is a target framework, not a dependency version.
    [GeneratedRegex(@"\d+\.\d+\.\d+[\w.\-+]*")]
    private static partial Regex VersionLiteral();
}

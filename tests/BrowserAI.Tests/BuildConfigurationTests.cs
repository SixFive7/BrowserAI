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
    /// <summary>
    /// Every <c>.cs</c> file under <c>src/</c> and <c>tests/</c> is visible to
    /// git.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the founding failure class applied to a repository.</b>
    /// [Build order](../../plan/build-order.md#every-done-test-ends-with-a-clean-working-tree)
    /// opens with a repository in this estate holding a working app, a test
    /// project and a good CI workflow that were <b>never committed</b>: 29
    /// tracked files, zero <c>.cs</c>, every surface signal healthy. The check
    /// it prescribed — <c>git status --porcelain</c> is empty — cannot catch
    /// that on its own, because an <b>ignored</b> file is not untracked, it is
    /// invisible, and a clean tree is exactly what it reports.
    /// </para>
    /// <para>
    /// <b>It is not hypothetical.</b> Caught 2026-08-16 at build-order step 14:
    /// the .NET template's unanchored <c>artifacts/</c> rule matched
    /// <c>src/BrowserAI/Artifacts/</c> on case-insensitive Windows, and five
    /// product source files were ignored while the build, the suite and
    /// <c>git status</c> all read green. The rule is now root-anchored, and this
    /// is the mechanism that stops the next one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoSourceFileIsInvisibleToGit()
    {
        var tracked = await TrackedFilesAsync();

        // Skipped rather than failed on a machine with no git, and said out
        // loud: a check that silently becomes a no-op is the thing this file
        // exists to prevent. The build cannot run without git in this
        // repository anyway -- the snapshot gate shells out to it.
        await Assert.That(tracked.Count).IsGreaterThan(0);

        var onDisk = RepositoryLayout.SourceFilesUnder(["src", "tests"], ["*.cs"])
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/'));

        var invisible = onDisk
            .Where(path => !tracked.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, invisible)).IsEmpty();
    }

    /// <summary>Every path git is tracking, as forward-slashed repository-relative names.</summary>
    private static async Task<HashSet<string>> TrackedFilesAsync()
    {
        // `git ls-files` reports what is in the index, which is the question:
        // a file that is ignored is absent from it, and a file that is merely
        // untracked-and-new is also absent -- both are failures here, and both
        // are invisible to `git status --porcelain` for opposite reasons.
        using var git = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryLayout.Root.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in new[] { "ls-files", "--cached", "--others", "--exclude-standard", "--", "src", "tests" })
        {
            git.StartInfo.ArgumentList.Add(argument);
        }

        _ = git.Start();

        var output = await git.StandardOutput.ReadToEndAsync();
        await git.WaitForExitAsync();

        return git.ExitCode is 0
            ? [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())]
            : [];
    }

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
        //
        // The three MSBuild diagnostic tasks are stripped for the same reason,
        // one level less obvious: a task that REPORTS a version is not a task
        // that sets one, and both its Text and its Condition are about a value
        // rather than being one. Narrowed 2026-08-16 at build-order step 18,
        // which added a target refusing a version derived from no git tag --
        // `$(MinVerVersion.StartsWith('0.0.0'))`, with `0.0.0` in the message so
        // a reader knows what was refused. This test read both as pins.
        //
        // It costs nothing that matters: a pin is a VALUE, and every shape one
        // can take is an attribute or an element value somewhere else in the
        // file. Nothing inside an <Error> can declare a package version, and
        // NoProjectFileDeclaresAPackageVersion above covers the attribute form
        // independently.
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProjectFiles)
        {
            var document = XDocument.Load(file.FullName);
            document.DescendantNodes().OfType<XComment>().Remove();
            document.Descendants()
                .Where(element => element.Name.LocalName is "Error" or "Warning" or "Message")
                .Remove();

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

    /// <summary>
    /// <c>UseSystemResourceKeys</c> is off, and it is off <b>explicitly</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[Testing](../../plan/testing.md#what-the-build-itself-must-fail-on)
    /// requires this in those words</b> — <i>"assert the property is unset, so
    /// it cannot arrive later as somebody's size optimisation"</i> — and until
    /// 2026-08-16 nothing did: <c>grep -rn "ResourceKeys" tests/</c> returned
    /// nothing, found while assembling the evidence
    /// [item 7](../../plan/pre-release.md) asks for. The property was correct
    /// and unguarded, which is the state a size optimisation walks into.
    /// </para>
    /// <para>
    /// <b>What it costs if it ever arrives.</b> It strips the framework's
    /// exception message strings and leaves bare resource keys, so
    /// <c>Arg_DirectoryNotFound</c> replaces a sentence naming the path. This
    /// product's error text is read by a <i>model</i> deciding what to do next
    /// ([the error catalogue](../../plan/H-model-surface.md#h4-the-error-catalogue)),
    /// so the saving is kilobytes against a ~117 MB payload and the loss is the
    /// catalogue, silently emptied.
    /// </para>
    /// <para>
    /// <b>Both halves are asserted.</b> Absent is not good enough: the default
    /// is already off, so a file that never mentions it passes a "not true"
    /// check and says nothing to the next reader. The literal <c>false</c> is
    /// what makes a later <c>true</c> a diff rather than an addition.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task UseSystemResourceKeysIsExplicitlyFalseEverywhereItAppears()
    {
        var declarations = RepositoryLayout.BuildFiles
            .SelectMany(file => XDocument.Load(file.FullName).Descendants()
                .Where(element => element.Name.LocalName is "UseSystemResourceKeys")
                .Select(element => (File: Relative(file), element.Value)))
            .ToList();

        // Anything other than `false` is the size optimisation arriving, from
        // whichever file it arrives in.
        var enabled = declarations
            .Where(declaration => !string.Equals(declaration.Value, "false", StringComparison.OrdinalIgnoreCase))
            .Select(declaration => $"{declaration.File}: <UseSystemResourceKeys>{declaration.Value}</UseSystemResourceKeys>")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, enabled)).IsEmpty();

        // And it is stated rather than defaulted, in the one file that applies
        // to every project. A deletion here reads as a tidy-up and is not one.
        await Assert.That(declarations.Select(declaration => declaration.File))
            .Contains("Directory.Build.props");
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

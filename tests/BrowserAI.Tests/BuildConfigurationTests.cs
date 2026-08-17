// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
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
    /// The AOT, trim and single-file knobs that may only ever be set on the
    /// assembly they are about.
    /// </summary>
    private static readonly string[] PerAssemblyOnly =
    [
        "PublishAot",
        "EnableAotAnalyzer",
        "EnableTrimAnalyzer",
        "EnableSingleFileAnalyzer",
        "SuppressTrimAnalysisWarnings",
        "SuppressAotAnalysisWarnings",
        "TrimmerSingleWarn",
        "ILLinkTreatWarningsAsErrors",
        "IsTrimmable",
        "PublishTrimmed",
    ];

    /// <summary>The one value the shipped assembly's trim suppression may hold.</summary>
    private static readonly string[] TrimSuppressionOff = ["false"];

    /// <summary>The manifest the product project must attach.</summary>
    private static readonly string[] TheManifest = ["app.manifest"];

    /// <summary>The one long-path setting the manifest may carry.</summary>
    private static readonly string[] LongPathAware = ["true"];

    /// <summary>The one privilege level BrowserAI may ever request.</summary>
    private static readonly string[] AsInvoker = ["asInvoker"];

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/</c> and <c>tests/</c> is visible to
    /// git.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the founding failure class applied to a repository.</b> A
    /// repository in this estate held a working app, a test
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
    /// <b>[TESTING.md](../../TESTING.md#what-the-build-itself-must-fail-on)
    /// requires this in those words</b> — <i>"assert the property is unset, so
    /// it cannot arrive later as somebody's size optimisation"</i> — and until
    /// 2026-08-16 nothing did: <c>grep -rn "ResourceKeys" tests/</c> returned
    /// nothing, found while assembling the evidence
    /// [item 7](../../RELEASING.md) asks for. The property was correct
    /// and unguarded, which is the state a size optimisation walks into.
    /// </para>
    /// <para>
    /// <b>What it costs if it ever arrives.</b> It strips the framework's
    /// exception message strings and leaves bare resource keys, so
    /// <c>Arg_DirectoryNotFound</c> replaces a sentence naming the path. This
    /// product's error text is read by a <i>model</i> deciding what to do next
    /// (<see cref="Sessions.SessionErrors"/>),
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

    /// <summary>
    /// Warnings are errors, said in the one file every project reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[TESTING.md](../../TESTING.md#what-the-build-itself-must-fail-on)
    /// opens with this and nothing asserted it.</b>
    /// <see cref="NoBuildFileSuppressesWarnings"/> forbids <c>NoWarn</c> and
    /// <c>WarningsNotAsErrors</c> — the two ways to demote a warning — and never
    /// looked for the property that promotes them in the first place, so
    /// deleting the line left the suite green and turned every analyzer in this
    /// repository into advice. Found by the plan's final audit, in the same
    /// paragraph list that produced <see cref="UseSystemResourceKeysIsExplicitlyFalseEverywhereItAppears"/>.
    /// </para>
    /// <para>
    /// <b>Both halves, for the same reason that one has both.</b> The property
    /// must be <c>true</c> wherever it appears, and it must <i>appear</i> in the
    /// file that reaches every project — a per-project declaration would leave
    /// the next project uncovered and pass a "nothing sets it to false" check.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WarningsAreErrorsForEveryProject()
    {
        var declarations = Declarations("TreatWarningsAsErrors");

        var demoted = declarations
            .Where(declaration => !string.Equals(declaration.Value, "true", StringComparison.OrdinalIgnoreCase))
            .Select(declaration => $"{declaration.File}: <TreatWarningsAsErrors>{declaration.Value}</TreatWarningsAsErrors>");

        await Assert.That(string.Join(Environment.NewLine, demoted)).IsEmpty();
        await Assert.That(declarations.Select(declaration => declaration.File)).Contains("Directory.Build.props");
    }

    /// <summary>
    /// <c>CS0162</c> is promoted by name, so it survives
    /// <c>TreatWarningsAsErrors</c> being turned off.
    /// </summary>
    /// <remarks>
    /// <b>Unreachable code is not a tidiness complaint.</b> It means the compiler
    /// proved a branch cannot execute, and in this codebase that branch is
    /// usually a guard, a <c>catch</c> or a cleanup path — the recovery path that
    /// was never going to run is this project's founding failure class. The
    /// promotion is what keeps it an error if the blanket property above ever
    /// goes; <see cref="NoBuildFileSuppressesWarnings"/> is what keeps a
    /// <c>NoWarn</c> from defeating both, measured on SDK 10.0.302. Three
    /// mechanisms, and until 2026-08-16 only the third was asserted.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task UnreachableCodeIsPromotedToAnErrorByName()
    {
        var promotions = Declarations("WarningsAsErrors");

        await Assert.That(promotions.Select(declaration => declaration.File)).Contains("Directory.Build.props");

        await Assert.That(promotions
            .Where(declaration => string.Equals(declaration.File, "Directory.Build.props", StringComparison.Ordinal))
            .Any(declaration => declaration.Value.Contains("CS0162", StringComparison.Ordinal)))
            .IsTrue();
    }

    /// <summary>
    /// AOT and trim analysis is configured on the assembly that ships, never on
    /// every assembly at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[TESTING.md](../../TESTING.md#what-the-build-itself-must-fail-on) in
    /// those words:</b> <i>"AOT and trim warning suppression is scoped
    /// per-assembly, never repo-wide. A repo-wide suppression is permanent and
    /// invisible: it silences the warning for every assembly added afterwards,
    /// including the one that will actually be broken by it."</i> A
    /// <c>&lt;SuppressTrimAnalysisWarnings&gt;true&lt;/&gt;</c> in
    /// <c>Directory.Build.props</c> was invisible to every scan the suite made.
    /// </para>
    /// <para>
    /// <b>The positive half is the one that catches a deletion.</b> Forbidding
    /// the property repo-wide says nothing if the product project stops
    /// declaring it at all — the ILC gate would then rest on <c>PublishAot</c>'s
    /// defaults, silently. So the shipped assembly must still say
    /// <c>false</c> in its own file, which is where a reviewer can see it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AotAndTrimAnalysisIsScopedPerAssemblyAndNeverRepoWide()
    {
        var repoWide = RepositoryLayout.RepositoryWideBuildFiles
            .SelectMany(file => XDocument.Load(file.FullName).Descendants()
                .Where(element => PerAssemblyOnly.Contains(element.Name.LocalName, StringComparer.Ordinal))
                .Select(element => $"{Relative(file)}: <{element.Name.LocalName}>{element.Value}</{element.Name.LocalName}> reaches every project, including the ones that do not exist yet"));

        await Assert.That(string.Join(Environment.NewLine, repoWide)).IsEmpty();

        // And the assembly that actually ships still says it, in its own file.
        var product = XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "BrowserAI.csproj"))
            .Descendants()
            .Where(element => element.Name.LocalName is "SuppressTrimAnalysisWarnings")
            .Select(element => element.Value)
            .ToList();

        await Assert.That(product).IsEquivalentTo(TrimSuppressionOff);
    }

    /// <summary>
    /// <c>global.json</c> declares a floor that rolls forward, and the runner
    /// mode TUnit cannot work without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neither entry is a pin, and the file is outside every other scan.</b>
    /// [STACK.md](../../STACK.md#the-build-configuration)
    /// requires *"an SDK floor that rolls forward"* — a floor forbids being
    /// stale, where a ceiling would forbid being current — and *"the MTP runner
    /// setting TUnit requires"*, without which <c>dotnet test</c> takes the
    /// VSTest path and errors out against an MTP v2 project.
    /// </para>
    /// <para>
    /// <b><c>rollForward</c> is the half that fails quietly.</b> Delete it and
    /// the build still works on this machine, because the floor happens to be
    /// installed; it fails on the next machine, and it fails as a version
    /// mismatch nobody attributes to a deleted line.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSdkFloorRollsForwardAndTheRunnerIsTheOneTUnitNeeds()
    {
        using var global = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(RepositoryLayout.Root.FullName, "global.json")));

        // Read through TryGetProperty rather than GetProperty: a DELETED entry
        // is the failure under test, and a KeyNotFoundException out of the JSON
        // reader names the dictionary rather than the setting. A guard whose
        // failure message does not say what to put back is half a guard.
        await Assert.That(Entry(global, "sdk", "rollForward")).IsEqualTo("latestMajor");
        await Assert.That(Entry(global, "sdk", "version")).IsNotEqualTo("<absent>");
        await Assert.That(Entry(global, "test", "runner")).IsEqualTo("Microsoft.Testing.Platform");
    }

    /// <summary>
    /// The application manifest is long-path aware and asks for no elevation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both settings fail silently, in opposite directions.</b>
    /// [STACK.md](../../STACK.md#the-build-configuration)
    /// requires <c>longPathAware</c> because session directories are the
    /// caller's choice and unbounded: without it a profile tree crossing
    /// <c>MAX_PATH</c> fails somewhere inside Chromium with an error nobody can
    /// attribute. The update path requires <c>asInvoker</c>
    /// because BrowserAI installs per-user precisely so that nothing ever waits
    /// on a UAC prompt a background MCP server cannot answer — an elevation
    /// request here would hang the client at startup.
    /// </para>
    /// <para>
    /// <b>And the manifest has to be attached.</b> A file nothing references is
    /// a file with no effect, which is indistinguishable from a correct one by
    /// reading it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheApplicationManifestIsLongPathAwareAndNeverAsksForElevation()
    {
        var project = XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "BrowserAI.csproj"));

        var attached = project.Descendants()
            .Where(element => element.Name.LocalName is "ApplicationManifest")
            .Select(element => element.Value)
            .ToList();

        await Assert.That(attached).IsEquivalentTo(TheManifest);

        var manifest = XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "app.manifest"));

        await Assert.That(manifest.Descendants()
            .Where(element => element.Name.LocalName is "longPathAware")
            .Select(element => element.Value))
            .IsEquivalentTo(LongPathAware);

        await Assert.That(manifest.Descendants()
            .Where(element => element.Name.LocalName is "requestedExecutionLevel")
            .Select(element => element.Attribute("level")?.Value ?? "<no level attribute>"))
            .IsEquivalentTo(AsInvoker);
    }

    /// <summary>One <c>global.json</c> entry, or a legible stand-in for an absent one.</summary>
    /// <param name="document">The parsed file.</param>
    /// <param name="section">The top-level object.</param>
    /// <param name="name">The entry inside it.</param>
    /// <returns>The value, or <c>&lt;absent&gt;</c>.</returns>
    private static string Entry(JsonDocument document, string section, string name) =>
        document.RootElement.TryGetProperty(section, out var parent) && parent.TryGetProperty(name, out var value)
            ? value.GetString() ?? "<null>"
            : "<absent>";

    /// <summary>Every declaration of a property across every build file.</summary>
    private static List<(string File, string Value)> Declarations(string property) =>
    [
        .. RepositoryLayout.BuildFiles
            .SelectMany(file => XDocument.Load(file.FullName).Descendants()
                .Where(element => string.Equals(element.Name.LocalName, property, StringComparison.Ordinal))
                .Select(element => (File: Relative(file), element.Value))),
    ];

    private static XDocument CentralPackageManagement() =>
        XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "Directory.Packages.props"));

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName);

    // Three or more dot-separated numbers. Two parts are excluded on purpose:
    // `net10.0-windows` is a target framework, not a dependency version.
    [GeneratedRegex(@"\d+\.\d+\.\d+[\w.\-+]*")]
    private static partial Regex VersionLiteral();
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests;

/// <summary>
/// The half of <i>never by image name</i> that an analyzer cannot see, and the
/// wiring that makes the analyzer cover every project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structural, not a review item.</b> A review already passed on code that
/// violated this rule: the Chromium probes this project grew out of counted and
/// killed by image name, which was harmless for Chromium on that machine and
/// would have killed roughly forty personal <c>firefox.exe</c> processes if
/// adapted naively.
/// </para>
/// <para>
/// <b>Two mechanisms, because neither covers the other.</b>
/// <c>Microsoft.CodeAnalysis.BannedApiAnalyzers</c> catches the
/// enumerate-by-name call at the call site, in every project, at error
/// severity. It is blind to a string: a kill-by-image-name command line
/// in a PowerShell script, a WMI query filtered on <c>Name</c>, a toolhelp walk
/// that compares <c>szExeFile</c>. That is what the scan below reads the tree
/// for.
/// </para>
/// <para>
/// <b>What is deliberately not banned:</b> reading a process's name. The rule
/// forbids matching, counting and terminating by name, not observing one —
/// <c>SdkStdioClientTransportTests</c> reads a parent's image name to prove
/// that the SDK's own transport interposes a shell, which is a defect being
/// exposed rather than a process being chosen.
/// </para>
/// </remarks>
internal sealed class NeverByImageNameTests
{
    /// <summary>
    /// The forbidden spellings, assembled at run time <b>so that this file does
    /// not match its own scan.</b> The alternative — an exclusion list naming
    /// this file — would create the one place in the repository where the rule
    /// does not apply, which is a worse trade than a slightly odd literal.
    /// </summary>
    private static readonly (string Needle, string Why)[] Forbidden =
    [
        ("task" + "kill", "kills by image name with /IM, and cannot tell our browser from the user's"),
        ("GetProcessesBy" + "Name", "enumerates by image name; the analyzer catches the C# call, this catches it in a script"),
        ("Win32_" + "Process", "a WMI query filtered on Name is the same rule wearing a different API"),
        ("Get-" + "Process", "PowerShell's name-filtered process enumerator"),

        // ⚠️ Added 2026-08-16 by the plan's final audit, which found it CLAIMED
        // and absent. `build/BannedSymbols.txt` said in as many words that "a
        // toolhelp walk that matches szExeFile ... is covered by
        // NeverByImageNameTests"; it was not, and a false claim of coverage is
        // worse than no coverage because it stops anyone looking.
        // [§D](../../plan/D-locking.md) forbids "any WMI OR TOOLHELP query
        // filtered by executable name" and asks for zero occurrences asserted.
        //
        // The needle is the FIELD rather than the walk. `CreateToolhelp32Snapshot`
        // and `Process32NextW` are how a process's pid and parent are read
        // without touching a name at all, and `JobProbe` does exactly that --
        // its PROCESSENTRY32 declares the last member as `ImageNameWeDoNotRead`
        // precisely so that the name cannot be compared even by accident.
        // Banning the walk would have made this repository's one legitimate,
        // deliberately name-blind toolhelp use into the exclusion this file
        // refuses to create; banning the field bans the only way a walk can
        // learn a name.
        ("szExe" + "File", "the PROCESSENTRY32 member that carries an image name -- reading it is the only way a toolhelp walk can match on one"),
    ];

    [Test]
    public async Task NoSourceOrScriptFileNamesAProcessToActOnIt()
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles)
        {
            var text = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(
                from forbidden in Forbidden
                where text.Contains(forbidden.Needle, StringComparison.OrdinalIgnoreCase)
                select $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}: '{forbidden.Needle}' -- {forbidden.Why}");
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task TheScanReadsMoreThanTheProductsOwnFiles()
    {
        // The scan is worth nothing if it is looking at four files. This fails
        // if the enumeration in RepositoryLayout ever narrows -- an excluded
        // directory, a pattern that stops matching -- which is a change that
        // otherwise leaves every assertion above passing.
        var scanned = RepositoryLayout.SourceAndScriptFiles;

        await Assert.That(scanned.Count).IsGreaterThan(15);
        await Assert.That(scanned.Any(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(scanned.Any(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(scanned.Any(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(scanned.Any(file => file.Extension is ".ps1")).IsTrue();
    }

    [Test]
    public async Task EveryProjectIsCoveredByTheBannedApiAnalyzer()
    {
        // The analyzer and the shared list are declared in Directory.Build.props
        // rather than per project, which is what makes a project added later
        // covered by construction. This asserts that arrangement, because
        // moving either line back into one .csproj would leave the suite green
        // and the other projects unguarded.
        var shared = Path.Combine(RepositoryLayout.Root.FullName, "build", "BannedSymbols.txt");

        await Assert.That(File.Exists(shared)).IsTrue();
        // Composed, for the same reason the needles above are: this file is
        // scanned by the test above it.
        await Assert.That(await File.ReadAllTextAsync(shared)).Contains("Process.GetProcessesBy" + "Name(System.String)");

        var props = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "Directory.Build.props"));

        await Assert.That(props).Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers");
        await Assert.That(props).Contains(@"build\BannedSymbols.txt");

        // No project may drop out of it. `Remove` on an AdditionalFiles item is
        // the quiet way to do exactly that.
        var opted = RepositoryLayout.ProjectFiles
            .Where(file => File.ReadAllText(file.FullName).Contains("AdditionalFiles Remove", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Name);

        await Assert.That(string.Join(", ", opted)).IsEmpty();
    }
}

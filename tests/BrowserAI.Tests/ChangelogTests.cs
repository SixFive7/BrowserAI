// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The changelog exists, has an unreleased section with real entries in it, and
/// a release attempt with that section empty is refused.
/// </summary>
/// <remarks>
/// <para>
/// [Release checklist item 10](../../RELEASING.md) refuses a release whose
/// unreleased section is empty, and until build-order step 18 there was no file
/// for it to be empty <i>of</i>. The refusal lives in
/// <c>build/Get-ReleaseNotes.ps1</c> rather than in the product, because it is
/// release machinery and BrowserAI is a proxy — but it is driven from here, so
/// it is exercised on every run rather than on the day someone cuts a release.
/// </para>
/// <para>
/// <b>Empty means no list items, not no characters.</b> A section holding
/// nothing but its <c>### Added</c> subheads is precisely what a changelog
/// nobody wrote looks like, and it would satisfy a non-empty-text check.
/// </para>
/// </remarks>
internal sealed partial class ChangelogTests
{
    private static string Changelog { get; } = Path.Combine(RepositoryLayout.Root.FullName, "CHANGELOG.md");

    private static string Script { get; } = Path.Combine(RepositoryLayout.Root.FullName, "build", "Get-ReleaseNotes.ps1");

    [Test]
    public async Task TheChangelogHasAnUnreleasedSectionWithEntriesInIt()
    {
        await Assert.That(File.Exists(Changelog)).IsTrue();

        var run = await RunAsync(Changelog);

        await Assert.That(run.ExitCode).IsEqualTo(0);

        // The notes it hands a release are the unreleased section, and there is
        // something in them. TODO.md is explicit that reconstructing entries at
        // release time is the failure this check exists to catch, so the file
        // being present is not the property -- the file being written is.
        await Assert.That(EntryLine().Count(run.StandardOutput)).IsGreaterThan(0);
    }

    [Test]
    public async Task EveryVersionHeadingIsTheBareVersionRatherThanTheTag()
    {
        // The house form, shared with four sibling repositories:
        // `## [0.1.0] - 2026-08-16`. The tag carries the `v` and the heading
        // does not, and the two must not drift into each other -- the stamping
        // path composes this line from a version string with no prefix on it.
        var headings = VersionHeading().Matches(await File.ReadAllTextAsync(Changelog))
            .Select(match => match.Groups["version"].Value)
            .ToList();

        await Assert.That(headings).IsNotEmpty();

        var malformed = headings
            .Where(heading => heading is not "Unreleased")
            .Where(heading => !BareVersion().IsMatch(heading))
            .ToList();

        await Assert.That(string.Join(", ", malformed)).IsEmpty();
    }

    [Test]
    public async Task AReleaseIsRefusedWhenTheUnreleasedSectionIsEmpty()
    {
        using var scratch = ScratchDirectory.Create("changelog-empty");
        var file = await WriteAsync(scratch, "empty.md", """
            # Changelog

            ## [Unreleased]

            ### Added

            ## [0.1.0] - 2026-08-16

            - Everything that has ever happened.
            """);

        var run = await RunAsync(file);

        await Assert.That(run.ExitCode).IsEqualTo(1);

        // Subheads and prose are not entries, and the message says what to do
        // rather than merely reporting that it stopped.
        await Assert.That(run.StandardError).Contains("no entries under");
        await Assert.That(run.StandardError).Contains("reconstruction at release time");
        await Assert.That(run.StandardError).Contains(file);
    }

    [Test]
    public async Task TheNotesAreTheUnreleasedSectionAndNothingBelowIt()
    {
        using var scratch = ScratchDirectory.Create("changelog-extract");
        var file = await WriteAsync(scratch, "full.md", """
            # Changelog

            ## [Unreleased]

            ### Fixed

            - The thing that was broken.

            ## [0.1.0] - 2026-08-16

            - The first one, which must not appear in the notes.
            """);

        var run = await RunAsync(file);

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.StandardOutput.ReplaceLineEndings("\n").Trim()).IsEqualTo("### Fixed\n\n- The thing that was broken.");
    }

    [Test]
    public async Task StampingMovesTheEntriesUnderTheVersionAndLeavesAnEmptyUnreleased()
    {
        using var scratch = ScratchDirectory.Create("changelog-stamp");
        var file = await WriteAsync(scratch, "stamp.md", """
            # Changelog

            ## [Unreleased]

            - The one entry.

            ## [0.1.0] - 2026-08-16

            - The first one.
            """);

        var stamped = await RunAsync(file, "-StampVersion", "0.2.0", "-Date", "2026-08-16");

        await Assert.That(stamped.ExitCode).IsEqualTo(0);

        // The heading is inserted below the unreleased one and nothing is
        // moved, so the entries become that version's by position. An edit that
        // relocated them could drop one; this cannot.
        var after = (await File.ReadAllTextAsync(file)).ReplaceLineEndings("\n");

        await Assert.That(after).Contains("## [Unreleased]\n\n## [0.2.0] - 2026-08-16\n\n- The one entry.");
        await Assert.That(after).Contains("## [0.1.0] - 2026-08-16");

        // And the next release starts from an empty section, which is the whole
        // mechanism: it refuses until somebody writes down what changed.
        var next = await RunAsync(file);

        await Assert.That(next.ExitCode).IsEqualTo(1);
        await Assert.That(next.StandardError).Contains("no entries under");
    }

    [Test]
    public async Task AVersionThisProjectCannotCutIsRefusedBeforeTheFileIsTouched()
    {
        using var scratch = ScratchDirectory.Create("changelog-version");
        var file = await WriteAsync(scratch, "version.md", """
            # Changelog

            ## [Unreleased]

            - Something worth releasing.
            """);

        var before = await HashAsync(file);

        // Four parts: `vpk` rejects them outright, which is the constraint the
        // whole derivation is shaped by.
        var fourPart = await RunAsync(file, "-StampVersion", "1.2.3.4");

        await Assert.That(fourPart.ExitCode).IsEqualTo(1);
        await Assert.That(fourPart.StandardError).Contains("four-part");

        // And the version that means the derivation found no tag, refused here
        // as well as by the build -- a release script that stamped it would be
        // writing a changelog section for a binary that does not know what it
        // is.
        var noTag = await RunAsync(file, "-StampVersion", "0.0.0-alpha.0.5");

        await Assert.That(noTag.ExitCode).IsEqualTo(1);
        await Assert.That(noTag.StandardError).Contains("derived from no git tag");

        // A refusal changes nothing. Asserted on the bytes, because the point
        // of extracting before stamping is that a rejected release leaves a
        // file somebody can still fix.
        await Assert.That(await HashAsync(file)).IsEqualTo(before);
    }

    private static async Task<string> WriteAsync(ScratchDirectory scratch, string name, string content)
    {
        var file = Path.Combine(scratch.Path, name);
        await File.WriteAllTextAsync(file, content);

        return file;
    }

    private static async Task<string> HashAsync(string file) =>
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)));

    /// <summary>What one run of the release-notes script produced.</summary>
    /// <param name="ExitCode">Its exit code. Non-zero is a refusal.</param>
    /// <param name="StandardOutput">The notes, when it produced any.</param>
    /// <param name="StandardError">The refusal, when it refused.</param>
    private sealed record ScriptRun(int ExitCode, string StandardOutput, string StandardError);

    private static async Task<ScriptRun> RunAsync(string changelog, params string[] arguments)
    {
        using var pwsh = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                // PowerShell 7, by name on PATH. Every build script in this
                // repository is pwsh and the snapshot gate already fails the
                // build when it is missing.
                FileName = "pwsh",
                WorkingDirectory = RepositoryLayout.Root.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Script, "-Path", changelog }.Concat(arguments))
        {
            pwsh.StartInfo.ArgumentList.Add(argument);
        }

        _ = pwsh.Start();

        var standardOutput = pwsh.StandardOutput.ReadToEndAsync();
        var standardError = pwsh.StandardError.ReadToEndAsync();

        await pwsh.WaitForExitAsync();

        return new ScriptRun(pwsh.ExitCode, await standardOutput, await standardError);
    }

    /// <summary>A changelog list item at the start of a line.</summary>
    [GeneratedRegex(@"(?m)^[ \t]*[-*][ \t]+\S")]
    private static partial Regex EntryLine();

    /// <summary>A section heading, capturing whatever is inside its brackets.</summary>
    [GeneratedRegex(@"(?m)^\#\#[ \t]+\[(?<version>[^\]]+)\]")]
    private static partial Regex VersionHeading();

    /// <summary>Three parts and an optional pre-release suffix, with no leading `v`.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$")]
    private static partial Regex BareVersion();
}

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
/// <para>
/// ⚠️ <b>There is exactly one state in which an empty unreleased section is
/// correct, and since 2026-09-15 this class knows it</b> —
/// <see cref="TheChangelogHasAnUnreleasedSectionWithEntriesInIt"/>. Stamping a
/// release empties the section by construction, so the check that guards against
/// a changelog nobody wrote was firing on the output of the step that writes one.
/// </para>
/// </remarks>
internal sealed partial class ChangelogTests
{
    private static string Changelog { get; } = Path.Combine(RepositoryLayout.Root.FullName, "CHANGELOG.md");

    private static string Script { get; } = Path.Combine(RepositoryLayout.Root.FullName, "build", "Get-ReleaseNotes.ps1");

    /// <summary>
    /// The unreleased section has entries in it — unless this commit <i>is</i>
    /// the release that emptied it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The exemption is one state wide, and it was added 2026-09-15 at the
    /// maintainer's decision (Q183a).</b> Stamping a release moves every entry
    /// under the new version's heading and leaves <c>## [Unreleased]</c> empty
    /// <i>by construction</i> — so on the release commit itself this arm was red,
    /// and stayed red on <c>master</c> until somebody landed the next change.
    /// That is the check firing on the one arrangement where an empty section is
    /// not a defect but the correct output of the step before it.
    /// </para>
    /// <para>
    /// <b>The condition is narrow on purpose and both halves are load-bearing:
    /// the newest dated section's version, and the tag EXACTLY at HEAD.</b>
    /// <c>git describe --tags --exact-match</c> semantics — distance zero — so a
    /// single commit past the release loses the exemption and the section must be
    /// written again. The tag must be that version and no other: the shapes are
    /// <c>## [1.0.0] - 2026-09-15</c> and <c>v1.0.0</c>, the heading bare and the
    /// tag carrying the <c>v</c>, which is
    /// <see cref="EveryVersionHeadingIsTheBareVersionRatherThanTheTag"/>'s rule
    /// read from the other end. A tag at HEAD naming some other version is not an
    /// exemption, it is a disagreement, and it stays red.
    /// </para>
    /// <para>
    /// <b>Without git there is no exemption, and that is the safe direction.</b>
    /// <see cref="GitOracle.TagExactlyAtHeadAsync"/> answers
    /// <see langword="null"/> when git cannot be reached at all, so an export
    /// makes exactly the demand this arm made before 2026-09-15. This one does
    /// not skip: the primary claim needs no git, and only the relaxation does.
    /// </para>
    /// <para>
    /// <b>Planted red in both directions, 2026-09-15</b>, against synthetic
    /// changelogs rather than against the tree — tagged-and-empty passes,
    /// untagged-and-empty fails, and tagged-at-the-wrong-version fails — because
    /// the tree can only ever be in one of those states at a time and a control
    /// that can only be read one way is not one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheChangelogHasAnUnreleasedSectionWithEntriesInIt()
    {
        await Assert.That(File.Exists(Changelog)).IsTrue();

        // ⚠️ THE CONTROLS FIRST, and unconditionally. The tree is in exactly one
        // of these states at a time, so controls placed inside the branch they
        // describe would run on the one day a year the exemption applies and
        // never otherwise -- which is a control that cannot fail.
        const string Emptied = """
            # Changelog

            ## [Unreleased]

            ## [1.0.0] - 2026-09-15

            - Everything that went into the release.
            """;

        // The arrangement being permitted: stamped, and tagged at that version.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, "v1.0.0")).IsTrue();

        // Untagged: the same file, on any commit after the release. This is the
        // state the exemption must NOT reach, and it is the state of this tree
        // on all but one commit in its life -- today's included.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, null)).IsFalse();

        // Tagged at a version that is not the newest section's. A disagreement
        // rather than an exemption -- the release stamped one version and the
        // tag names another.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, "v0.9.0")).IsFalse();

        // And the leading `v` is required rather than tolerated, because the
        // heading is bare by this file's own other rule.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, "1.0.0")).IsFalse();

        // A file with no dated section at all -- the state before the first
        // release -- is never exempt, whatever tag is standing.
        await Assert.That(IsTheCommitThatEmptiedIt("# Changelog\n\n## [Unreleased]\n", "v1.0.0")).IsFalse();

        // ---- And now the tree ---------------------------------------------------
        var text = await File.ReadAllTextAsync(Changelog);
        var tag = await GitOracle.TagExactlyAtHeadAsync();
        var run = await RunAsync(Changelog);

        if (run.ExitCode is 0)
        {
            // The notes it hands a release are the unreleased section, and there
            // is something in them. TODO.md is explicit that reconstructing
            // entries at release time is the failure this check exists to catch,
            // so the file being present is not the property -- the file being
            // written is.
            await Assert.That(EntryLine().Count(run.StandardOutput)).IsGreaterThan(0);

            return;
        }

        // ⚠️ THE ONE ACCEPTED REFUSAL. Everything else about it still has to
        // hold: it has to be the empty-section refusal rather than some other
        // failure of the script, and the entries have to have MOVED rather than
        // vanished -- so the section they went to is required to hold some.
        await Assert.That(IsTheCommitThatEmptiedIt(text, tag))
            .IsTrue()
            .Because(
                $"'## [Unreleased]' holds no entries and HEAD is not the release that emptied it (the tag exactly at HEAD is '{tag ?? "none"}',"
                + $" and the newest dated section is '{NewestDatedVersion(text) ?? "none"}'), so this is a change nobody wrote down: {run.StandardError.Trim()}");

        await Assert.That(run.StandardError).Contains("no entries under");
        await Assert.That(EntryLine().Count(NewestDatedSection(text))).IsGreaterThan(0);
    }

    /// <summary>
    /// Whether an empty unreleased section is what the release at this very
    /// commit left behind.
    /// </summary>
    /// <param name="changelog">The changelog's text.</param>
    /// <param name="tagAtHead">The tag exactly at HEAD, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the emptiness is the release's doing.</returns>
    private static bool IsTheCommitThatEmptiedIt(string changelog, string? tagAtHead) =>
        tagAtHead is not null
        && NewestDatedVersion(changelog) is { } version
        && string.Equals(tagAtHead, "v" + version, StringComparison.Ordinal);

    /// <summary>The version of the newest dated section, which is the one a release just wrote.</summary>
    /// <param name="changelog">The changelog's text.</param>
    /// <returns>The bare version, or <see langword="null"/> when there is no dated section.</returns>
    private static string? NewestDatedVersion(string changelog) =>
        DatedHeading().Match(changelog) is { Success: true } heading ? heading.Groups["version"].Value : null;

    /// <summary>The newest dated section's body, for the entries the stamp moved into it.</summary>
    /// <param name="changelog">The changelog's text.</param>
    /// <returns>Everything from that heading to the next one, or the empty string.</returns>
    private static string NewestDatedSection(string changelog)
    {
        if (DatedHeading().Match(changelog) is not { Success: true } heading)
        {
            return string.Empty;
        }

        var body = changelog[(heading.Index + heading.Length)..];
        var next = VersionHeading().Match(body);

        return next.Success ? body[..next.Index] : body;
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

    /// <summary>
    /// A <b>dated</b> section heading: <c>## [1.0.0] - 2026-09-15</c>.
    /// </summary>
    /// <remarks>
    /// The date is what tells a released section from <c>## [Unreleased]</c>,
    /// which carries none — so the first match in the file is the newest release
    /// rather than the section a stamp is about to fill. A version pattern alone
    /// would work today and would stop working the moment somebody wrote
    /// <c>## [Unreleased]</c> as a version.
    /// </remarks>
    [GeneratedRegex(@"(?m)^\#\#[ \t]+\[(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.\-]*)?)\][ \t]+-[ \t]+\d{4}-\d{2}-\d{2}[ \t]*$")]
    private static partial Regex DatedHeading();

    /// <summary>Three parts and an optional pre-release suffix, with no leading `v`.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$")]
    private static partial Regex BareVersion();
}

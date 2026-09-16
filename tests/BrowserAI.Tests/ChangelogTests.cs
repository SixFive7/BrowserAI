// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>The generator that turns one section into a release body.</summary>
    private static string NotesScript { get; } = Path.Combine(RepositoryLayout.Root.FullName, "build", "New-ReleaseNotes.ps1");

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
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, "v1.0.0", codeLandedSince: true)).IsTrue();

        // Untagged, with product or test code landed since the changelog was
        // last written. This is the state the exemption must NOT reach, and it
        // is the state of this tree on all but a handful of commits in its life.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, null, codeLandedSince: true)).IsFalse();

        // ⚠️ AND THE SECOND WAY IN, added 2026-09-15: untagged, and nothing
        // under src/ or tests/ has landed since the changelog was last written.
        // That is a release mid-cut -- stamped, gate running, tag not placed --
        // and it is the state the tag-at-HEAD clause could never describe.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, null, codeLandedSince: false)).IsTrue();

        // Git absent, or unable to answer: no exemption, which is the safe
        // direction and the same answer this arm gave before 2026-09-15.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, null, codeLandedSince: null)).IsFalse();

        // Tagged at a version that is not the newest section's. A disagreement
        // rather than an exemption -- the release stamped one version and the
        // tag names another.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, "v0.9.0", codeLandedSince: true)).IsFalse();

        // And the leading `v` is required rather than tolerated, because the
        // heading is bare by this file's own other rule.
        await Assert.That(IsTheCommitThatEmptiedIt(Emptied, "1.0.0", codeLandedSince: true)).IsFalse();

        // A file with no dated section at all -- the state before the first
        // release -- is never exempt, whatever tag is standing and whatever git
        // says has landed.
        await Assert.That(IsTheCommitThatEmptiedIt("# Changelog\n\n## [Unreleased]\n", "v1.0.0", codeLandedSince: false)).IsFalse();

        // ---- And now the tree ---------------------------------------------------
        var text = await File.ReadAllTextAsync(Changelog);
        var tag = await GitOracle.TagExactlyAtHeadAsync();
        var landed = await GitOracle.AnythingChangedSinceTheLastCommitTouchingAsync("CHANGELOG.md", "src", "tests");
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
        await Assert.That(IsTheCommitThatEmptiedIt(text, tag, landed))
            .IsTrue()
            .Because(
                $"'## [Unreleased]' holds no entries and this is not the release that emptied it (the tag exactly at HEAD is '{tag ?? "none"}',"
                + $" the newest dated section is '{NewestDatedVersion(text) ?? "none"}', and src/ or tests/ moved since the changelog was last written:"
                + $" {landed?.ToString() ?? "git could not say"}), so this is a change nobody wrote down: {run.StandardError.Trim()}");

        await Assert.That(run.StandardError).Contains("no entries under");
        await Assert.That(EntryLine().Count(NewestDatedSection(text))).IsGreaterThan(0);
    }

    /// <summary>
    /// Whether an empty unreleased section is what the release this commit is
    /// part of left behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Two ways in, and the second was added 2026-09-15 because the first
    /// one covers the wrong instant.</b> A release is not a commit: it is a
    /// stamp, then a gate, then a tag, and the section is empty for all of it.
    /// The tag-at-HEAD clause is true only at the end of that, so the arm was
    /// red through every step before it — which is exactly where a release
    /// stands when somebody is running the six-run gate, and exactly when a red
    /// suite costs the most.
    /// </para>
    /// <para>
    /// <b>The second clause is the rule the first was standing in for:</b>
    /// <i>nothing has landed that the changelog has not been written for</i>.
    /// Read from git as <c>src/</c> and <c>tests/</c> against the last commit
    /// that touched <c>CHANGELOG.md</c> — so a docs-only or release-machinery
    /// commit during a cut leaves the exemption standing, and the first product
    /// or test change after it takes the exemption away and demands an entry
    /// again. That is the demand the whole check exists to make.
    /// </para>
    /// <para>
    /// <b>Without git there is no exemption</b>, which is the safe direction:
    /// <see langword="null"/> from either oracle leaves the stricter claim
    /// standing, and no arrangement in which git cannot be reached turns a red
    /// into a green.
    /// </para>
    /// </remarks>
    /// <param name="changelog">The changelog's text.</param>
    /// <param name="tagAtHead">The tag exactly at HEAD, or <see langword="null"/>.</param>
    /// <param name="codeLandedSince">
    /// Whether anything under <c>src/</c> or <c>tests/</c> has changed since the
    /// last commit that touched the changelog. <see langword="null"/> when git
    /// could not be asked.
    /// </param>
    /// <returns><see langword="true"/> when the emptiness is the release's doing.</returns>
    private static bool IsTheCommitThatEmptiedIt(string changelog, string? tagAtHead, bool? codeLandedSince)
    {
        if (NewestDatedVersion(changelog) is not { } version)
        {
            return false;
        }

        return (tagAtHead is not null && string.Equals(tagAtHead, "v" + version, StringComparison.Ordinal))
            || codeLandedSince is false;
    }

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

    /// <summary>
    /// The palette a changelog entry may open with, in the order the legend
    /// lists it — the maintainer's own words, approved 2026-09-15 (Q192b).
    /// </summary>
    /// <remarks>
    /// <b>This array is the DECISION and the legend in the file is the
    /// PUBLICATION</b>, which is why one arm holds them identical and the other
    /// reads entries against the legend rather than against this. A palette that
    /// grew an icon nobody approved would otherwise pass by being written twice.
    /// </remarks>
    private static readonly (string Icon, string Means)[] Palette =
    [
        ("✨", "new capability"),
        ("🐛", "fix"),
        ("🔧", "behaviour or configuration change"),
        ("🔒", "security or permissions"),
        ("🗑️", "removal or deprecation"),
        ("💥", "breaking, or the reader must act"),
        ("📝", "documentation"),
        ("✅", "tests and the gate"),
        ("📦", "packaging, installer, release pipeline"),
        ("⚡", "performance"),
        ("♻️", "refactor with no behaviour change"),
        ("⬆️", "dependency move"),
    ];

    /// <summary>
    /// The Keep a Changelog group set, in the order that format fixes them in.
    /// </summary>
    private static readonly string[] Groups =
    [
        "Added", "Changed", "Deprecated", "Removed", "Fixed", "Security",
    ];

    /// <summary>
    /// How long a headline may be, including its full stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chosen, not measured, and that is said out loud.</b> One hundred
    /// characters is a readability budget: it is about the width of the bold
    /// line a reader scans on a release page, and it is short enough that a
    /// headline cannot become the entry. <b>Nothing here measured where GitHub
    /// wraps</b> — that is a question about a browser's layout at a font size
    /// and a column width, and the markdown API returns HTML rather than a line
    /// box, so the honest statement is that the number is a budget and not a
    /// wrap point.
    /// </para>
    /// <para>
    /// <b>It is an upper bound on a written sentence, not on a measured
    /// quantity</b>, so it is not the kind of number
    /// <c>HouseRuleTests.NoAssertionBoundsAMeasuredDurationWithANumberItInvented</c>
    /// exists to catch — nothing about a clock or a size the machine produced is
    /// being bounded here.
    /// </para>
    /// </remarks>
    private const int HeadlineBudget = 100;

    /// <summary>
    /// Every entry opens with exactly one palette icon and a bold one-sentence
    /// headline that fits the budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The shape is what the release-notes generator reads</b>, so a
    /// malformed entry is not an untidy line — it is a release body that cannot
    /// be produced, and `build/New-ReleaseNotes.ps1` refuses rather than
    /// guessing. Asserted here as well, because the changelog is edited every
    /// working day and a release is cut rarely.
    /// </para>
    /// <para>
    /// <b>The controls are synthetic and there are five of them</b>, because
    /// the tree can only ever be in the passing state: an entry with no icon, an
    /// entry with an icon that is not in the palette, a headline with no bold, a
    /// headline that runs to two sentences, and one that is a character over
    /// budget. Each is the exact mistake a person writing an entry by hand
    /// makes.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryEntryOpensWithOnePaletteIconAndABoldOneSentenceHeadline()
    {
        var icons = LegendIcons(await File.ReadAllTextAsync(Changelog));

        await Assert.That(icons).IsNotEmpty();

        var wrong = Malformed(await File.ReadAllTextAsync(Changelog), icons);

        await Assert.That(string.Join(Environment.NewLine, wrong)).IsEmpty();

        // ---- The controls, over text this file will never contain -----------
        var control = string.Join("\n", Palette.Select(entry => $"{entry.Icon} {entry.Means}")) + "\n";

        await Assert.That(Malformed(
            $"# C\n\n{control}\n## [9.9.9] - 2026-01-01\n\n### Added\n\n- An entry nobody gave an icon.\n", icons))
            .IsNotEmpty();

        await Assert.That(Malformed(
            $"# C\n\n{control}\n## [9.9.9] - 2026-01-01\n\n### Added\n\n- 🎉 **An icon that is not in the palette.**\n", icons))
            .IsNotEmpty();

        await Assert.That(Malformed(
            $"# C\n\n{control}\n## [9.9.9] - 2026-01-01\n\n### Added\n\n- ✨ A headline nobody made bold.\n", icons))
            .IsNotEmpty();

        await Assert.That(Malformed(
            $"# C\n\n{control}\n## [9.9.9] - 2026-01-01\n\n### Added\n\n- ✨ **Two sentences. That is one too many.**\n", icons))
            .IsNotEmpty();

        var overlong = new string('x', HeadlineBudget) + ".";

        await Assert.That(Malformed(
            $"# C\n\n{control}\n## [9.9.9] - 2026-01-01\n\n### Added\n\n- ✨ **{overlong}**\n", icons))
            .IsNotEmpty();

        // And the shape they are each one mutation away from, so the five above
        // fail for their own reason rather than because nothing passes.
        await Assert.That(Malformed(
            $"# C\n\n{control}\n## [9.9.9] - 2026-01-01\n\n### Added\n\n- ✨ **A headline of exactly the right shape.** The detail.\n", icons))
            .IsEmpty();
    }

    /// <summary>
    /// The legend lists the approved palette, all of it, in order, and nothing
    /// else.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLegendAtTheTopListsExactlyTheApprovedPalette()
    {
        var text = await File.ReadAllTextAsync(Changelog);

        // The legend is the last paragraph before the first version heading, and
        // it is read as one line however it is wrapped.
        var head = text[..text.IndexOf("\n## ", StringComparison.Ordinal)];
        var legend = head.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(paragraph => paragraph.Contains('·', StringComparison.Ordinal));

        await Assert.That(legend).IsNotNull();

        var flattened = string.Join(" ", legend!.Split('\n').Select(line => line.Trim()));
        var listed = flattened.Split('·').Select(part => part.Trim()).ToList();

        await Assert.That(string.Join(" | ", listed))
            .IsEqualTo(string.Join(" | ", Palette.Select(entry => $"{entry.Icon} {entry.Means}")));
    }

    /// <summary>
    /// Every section's groups are the Keep a Changelog set, each at most once
    /// and in that format's fixed order.
    /// </summary>
    /// <remarks>
    /// <b>Repeats are the failure this actually caught.</b> Before 2026-09-15
    /// the 1.0.0 section carried <c>Changed</c> four times, <c>Added</c> three,
    /// <c>Fixed</c> three and <c>Removed</c> three — one run per batch of work
    /// that landed — so the section read as a diary rather than as a release,
    /// and a reader looking for what was removed had to find three lists of it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EverySectionsGroupsAreTheKeepAChangelogSetInItsFixedOrder()
    {
        await Assert.That(string.Join(Environment.NewLine, OutOfOrder(await File.ReadAllTextAsync(Changelog)))).IsEmpty();

        // Repeated, out of order, and a group the format does not have.
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Added\n\n### Fixed\n\n### Added\n")).IsNotEmpty();
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Fixed\n\n### Added\n")).IsNotEmpty();
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Improved\n")).IsNotEmpty();
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Added\n\n### Fixed\n")).IsEmpty();
    }

    /// <summary>
    /// The newest released section opens with a paragraph, before its groups.
    /// </summary>
    /// <remarks>
    /// <b>A release page opens with the entry list unless somebody writes
    /// something first</b>, and what the maintainer met on the published v1.0.0
    /// was four warning icons and the middle of an argument about Velopack. The
    /// preamble is the one part of the body written for somebody who has never
    /// seen the project.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNewestReleasedSectionOpensWithAPreamble()
    {
        var text = await File.ReadAllTextAsync(Changelog);

        await Assert.That(Preamble(NewestDatedSection(text))).IsNotEmpty();

        // The control: a section that goes straight into its groups.
        await Assert.That(Preamble("\n\n### Added\n\n- ✨ **A thing.**\n")).IsEmpty();
    }

    /// <summary>The icons the legend publishes, in order.</summary>
    /// <param name="changelog">The changelog's text.</param>
    /// <returns>The icons.</returns>
    private static List<string> LegendIcons(string changelog)
    {
        var head = changelog.Replace("\r\n", "\n", StringComparison.Ordinal);
        var firstHeading = head.IndexOf("\n## ", StringComparison.Ordinal);

        if (firstHeading > 0)
        {
            head = head[..firstHeading];
        }

        var legend = head.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(paragraph => paragraph.Contains('·', StringComparison.Ordinal));

        return legend is null
            ? []
            : [.. string.Join(" ", legend.Split('\n').Select(line => line.Trim()))
                .Split('·')
                .Select(part => part.Trim().Split(' ')[0])
                .Where(icon => icon.Length > 0)];
    }

    /// <summary>
    /// Every entry of a changelog that is not in the shape a release body is
    /// generated from, with what is wrong with it.
    /// </summary>
    /// <param name="changelog">The changelog's text.</param>
    /// <param name="icons">The icons the legend permits.</param>
    /// <returns>One line per offending entry.</returns>
    private static List<string> Malformed(string changelog, List<string> icons)
    {
        var offences = new List<string>();
        var lines = changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            // ⚠️ THE ENTRY, NOT THE LINE. The file is hard-wrapped at 80
            // columns, so a headline routinely runs over two of them and a
            // line-at-a-time scan reports every entry as shapeless. The
            // generator joins an entry's lines before it reads the shape, and so
            // does this — which is the only arrangement in which the two are
            // asking one question.
            var number = index + 1;
            var block = new StringBuilder(lines[index]);

            for (var next = index + 1; next < lines.Length; next++)
            {
                if (lines[next].Length > 0 && !char.IsWhiteSpace(lines[next][0]))
                {
                    break;
                }

                _ = block.Append(' ').Append(lines[next]);
            }

            var line = Whitespace().Replace(block.ToString(), " ").Trim();
            var shaped = EntryShape().Match(line);

            if (!shaped.Success)
            {
                offences.Add($"CHANGELOG.md:{number}: not '- <icon> **Headline.** …' — {Excerpt(line)}");
                continue;
            }

            var icon = shaped.Groups["icon"].Value;
            var headline = shaped.Groups["headline"].Value;

            if (!icons.Contains(icon, StringComparer.Ordinal))
            {
                offences.Add($"CHANGELOG.md:{number}: '{icon}' is not in the legend's palette — {Excerpt(line)}");
            }

            if (!headline.EndsWith('.'))
            {
                offences.Add($"CHANGELOG.md:{number}: the headline does not end in a full stop — {headline}");
            }
            else if (SentenceBreak().IsMatch(headline[..^1]))
            {
                offences.Add($"CHANGELOG.md:{number}: the headline is more than one sentence — {headline}");
            }

            if (headline.Length > HeadlineBudget)
            {
                offences.Add(
                    $"CHANGELOG.md:{number}: the headline is {headline.Length.ToString(CultureInfo.InvariantCulture)} characters, "
                    + $"over the {HeadlineBudget.ToString(CultureInfo.InvariantCulture)} a headline may be — {headline}");
            }
        }

        return offences;
    }

    /// <summary>The first eighty characters of a line, for a failure message.</summary>
    /// <param name="line">The line.</param>
    /// <returns>An excerpt, since an unwrapped entry runs to thousands.</returns>
    private static string Excerpt(string line) =>
        line.Length <= 80 ? line : line[..80] + "…";

    /// <summary>Group headings that repeat, run out of order, or are not groups at all.</summary>
    /// <param name="changelog">The changelog's text.</param>
    /// <returns>One line per offence.</returns>
    private static List<string> OutOfOrder(string changelog)
    {
        var offences = new List<string>();
        var seen = new List<string>();

        foreach (var line in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                seen.Clear();
                continue;
            }

            if (!line.StartsWith("### ", StringComparison.Ordinal))
            {
                continue;
            }

            var group = line[4..].Trim();

            if (!Groups.Contains(group, StringComparer.Ordinal))
            {
                offences.Add($"'{group}' is not a Keep a Changelog group: {string.Join(", ", Groups)}");
                continue;
            }

            if (seen.Contains(group, StringComparer.Ordinal))
            {
                offences.Add($"'{group}' appears twice under one version — the entries belong in one list");
                continue;
            }

            if (seen.Count > 0 && Array.IndexOf(Groups, group) < Array.IndexOf(Groups, seen[^1]))
            {
                offences.Add($"'{group}' comes after '{seen[^1]}', which is not the order Keep a Changelog fixes");
            }

            seen.Add(group);
        }

        return offences;
    }

    /// <summary>The prose a section opens with, before its first group.</summary>
    /// <param name="section">A version section's body.</param>
    /// <returns>The preamble, trimmed, or the empty string.</returns>
    private static string Preamble(string section)
    {
        var body = section.Replace("\r\n", "\n", StringComparison.Ordinal);
        var firstGroup = body.IndexOf("\n### ", StringComparison.Ordinal);

        return (firstGroup < 0 ? body : body[..firstGroup]).Trim();
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

    /// <summary>
    /// A section becomes a body: the preamble, the groups, a headline a line,
    /// and every detail behind a fold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted as the whole body rather than as things it contains</b>,
    /// because what this generator is for is the document a person meets on a
    /// release page — and every failure it exists to prevent is a shape failure
    /// rather than a missing word. The fixture is small enough to write out in
    /// full, which is the only way an exact assertion is readable.
    /// </para>
    /// <para>
    /// <b>The indentation and the blank lines are load-bearing</b>: two spaces
    /// put the fold inside the list ITEM rather than ending the list, and a blank
    /// line on each side of the <c>&lt;summary&gt;</c> is what makes GitHub parse
    /// the inside as Markdown instead of as the inside of an HTML block. Verified
    /// against GitHub's own renderer rather than assumed — see
    /// [the pre-publish check](../../RELEASING.md#the-release-body-is-generated-and-its-rendering-is-checked-before-it-is-published).
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AVersionSectionBecomesHeadlinesWithTheDetailFolded()
    {
        using var scratch = ScratchDirectory.Create("release-notes");
        var changelog = await WriteAsync(scratch, "CHANGELOG.md", Fixture);
        var body = Path.Combine(scratch.Path, "body.md");

        var run = await RunScriptAsync(NotesScript, "-Path", changelog, "-Version", "9.9.9", "-Destination", body);

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.StandardOutput).Contains("folded");

        var expected = """
            A preamble sentence wrapped over two lines.

            ### Added

            - ✨ **A new thing.**

              <details><summary>read more</summary>

              It does a thing, over two lines.

              And a second paragraph.

              </details>

            ### Fixed

            - 🐛 **An old thing was wrong.**

            ---

            ✨ new capability · 🐛 fix

            Every entry in full, with its evidence: [CHANGELOG.md](https://github.com/SixFive7/BrowserAI/blob/v9.9.9/CHANGELOG.md#999---2026-01-01)

            """;

        await Assert.That(Lf(await File.ReadAllTextAsync(body))).IsEqualTo(Lf(expected));
    }

    /// <summary>
    /// A body that does not fit falls back to headlines alone, and the script
    /// says which shape it produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>GitHub's release body limit is 125,000 characters</b>
    /// ([FLOATS](../../kb/re-verification.md): it is their field and they can
    /// move it), and the 1.0.0 section folded comes to **280,063** — so this is
    /// not a defensive branch, it is the branch this release takes.
    /// </para>
    /// <para>
    /// <b>The fallback is driven by a small limit rather than a huge fixture</b>,
    /// which is the same document through the same code and costs nothing to
    /// read. The control is the arm above, which produces the folded shape from
    /// the same fixture.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABodyThatDoesNotFitFallsBackToHeadlinesAndSaysSo()
    {
        using var scratch = ScratchDirectory.Create("release-notes-limit");
        var changelog = await WriteAsync(scratch, "CHANGELOG.md", Fixture);
        var body = Path.Combine(scratch.Path, "body.md");

        var run = await RunScriptAsync(NotesScript, "-Path", changelog, "-Version", "9.9.9", "-Destination", body, "-Limit", "400");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.StandardOutput).Contains("HEADLINES ONLY");

        var expected = """
            A preamble sentence wrapped over two lines.

            ### Added

            - ✨ **A new thing.**

            ### Fixed

            - 🐛 **An old thing was wrong.**

            ---

            ✨ new capability · 🐛 fix

            Every entry in full, with its evidence: [CHANGELOG.md](https://github.com/SixFive7/BrowserAI/blob/v9.9.9/CHANGELOG.md#999---2026-01-01)

            """;

        await Assert.That(Lf(await File.ReadAllTextAsync(body))).IsEqualTo(Lf(expected));

        // The detail is not in the body at all, which is the whole cost of this
        // shape and is why the script says so out loud rather than quietly
        // producing a different document.
        await Assert.That(await File.ReadAllTextAsync(body)).DoesNotContain("second paragraph");
    }

    /// <summary>
    /// The anchor the body links to is the one the link checker computes.
    /// </summary>
    /// <remarks>
    /// <b>Two implementations, in two languages, and this is what stops them
    /// drifting.</b> The generator is PowerShell and
    /// <see cref="MarkdownAnchor.Slug"/> is C#, so they cannot be one
    /// implementation; what is mechanised is that they agree on the shapes that
    /// tell slug rules apart — a version heading's dots, an em dash between two
    /// spaces, a code span, and an underscore, which GitHub keeps and a
    /// hand-written rule usually strips.
    /// </remarks>
    /// <param name="heading">The version heading, as the file carries it.</param>
    /// <param name="version">The version inside it.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("## [9.9.9] - 2026-01-01", "9.9.9")]
    [Arguments("## [9.9.9] - 2026-01-01 — the `one_off` release", "9.9.9")]
    [Arguments("## [9.9.9-alpha.1] - 2026-01-01", "9.9.9-alpha.1")]
    public async Task TheReleaseNotesAnchorIsTheOneTheLinkCheckerComputes(string heading, string version)
    {
        using var scratch = ScratchDirectory.Create("release-notes-anchor");

        var changelog = await WriteAsync(
            scratch,
            "CHANGELOG.md",
            Fixture.Replace("## [9.9.9] - 2026-01-01", heading, StringComparison.Ordinal));

        var body = Path.Combine(scratch.Path, "body.md");
        var run = await RunScriptAsync(NotesScript, "-Path", changelog, "-Version", version, "-Destination", body);

        await Assert.That(run.ExitCode).IsEqualTo(0);

        var expected = MarkdownAnchor.Slug(heading["## ".Length..]);

        await Assert.That(await File.ReadAllTextAsync(body))
            .Contains($"/blob/v{version}/CHANGELOG.md#{expected})");
    }

    /// <summary>
    /// An entry that is not in the shape is refused, by name, rather than
    /// silently left out of the body.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnEntryNotInTheHeadlineShapeRefusesTheBody()
    {
        using var scratch = ScratchDirectory.Create("release-notes-shape");

        var changelog = await WriteAsync(
            scratch,
            "CHANGELOG.md",
            Fixture.Replace("- 🐛 **An old thing was wrong.**", "- An entry nobody gave a headline.", StringComparison.Ordinal));

        var body = Path.Combine(scratch.Path, "body.md");
        var run = await RunScriptAsync(NotesScript, "-Path", changelog, "-Version", "9.9.9", "-Destination", body);

        await Assert.That(run.ExitCode).IsNotEqualTo(0);
        await Assert.That(run.StandardError).Contains("An entry nobody gave a headline.");

        // And the positive control on the same call: the unmodified fixture is
        // accepted, so the refusal is the shape and not the rig.
        var good = await WriteAsync(scratch, "GOOD.md", Fixture);
        var accepted = await RunScriptAsync(NotesScript, "-Path", good, "-Version", "9.9.9", "-Destination", body);

        await Assert.That(accepted.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// The two shapes the generator used to lose or crash on are refused, in
    /// the script's own words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>An entry above the first <c>### </c> heading crashed with a message
    /// about a null-valued expression</b> — <c>$group</c> is
    /// <see langword="null"/> there and <c>Set-StrictMode</c> turns
    /// <c>$null.Entries.Add(…)</c> into a stack trace naming a variable nobody
    /// reading a changelog has heard of.
    /// </para>
    /// <para>
    /// ⚠️ <b>A paragraph UNDER a group heading was dropped, silently.</b> It is
    /// not a preamble, it is not an entry, and there is nowhere in a folded body
    /// for it — so it simply did not appear in the release notes. <b>Refused
    /// rather than carried, deliberately</b>: inventing a rendering for a shape
    /// nothing else reads would widen the changelog's format past what
    /// <see cref="EveryEntryOpensWithOnePaletteIconAndABoldOneSentenceHeadline"/>
    /// holds it to, and prose already has a place — the section preamble, above
    /// the first heading, which the body does render. <i>Both added 2026-09-16.</i>
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnEntryAboveTheFirstGroupAndAParagraphInsideOneAreBothRefused()
    {
        using var scratch = ScratchDirectory.Create("release-notes-shapes");
        var body = Path.Combine(scratch.Path, "body.md");

        // An entry above the first '### ' heading.
        var loose = await WriteAsync(
            scratch,
            "LOOSE.md",
            Fixture.Replace(
                "### Added",
                "- ✨ **A loose entry nobody grouped.**\n\n### Added",
                StringComparison.Ordinal));

        var looseRun = await RunScriptAsync(NotesScript, "-Path", loose, "-Version", "9.9.9", "-Destination", body);

        await Assert.That(looseRun.ExitCode).IsNotEqualTo(0);
        await Assert.That(looseRun.StandardError).Contains("A loose entry nobody grouped.");
        await Assert.That(looseRun.StandardError).Contains("group heading");

        // Never PowerShell's own words about a null-valued expression, which is
        // what this used to say.
        await Assert.That(looseRun.StandardError.Contains("null-valued", StringComparison.OrdinalIgnoreCase)).IsFalse();

        // A paragraph under a group heading, which used to vanish.
        var prose = await WriteAsync(
            scratch,
            "PROSE.md",
            Fixture.Replace(
                "### Fixed",
                "### Fixed\n\nA paragraph that belongs in the preamble.",
                StringComparison.Ordinal));

        var proseRun = await RunScriptAsync(NotesScript, "-Path", prose, "-Version", "9.9.9", "-Destination", body);

        await Assert.That(proseRun.ExitCode).IsNotEqualTo(0);
        await Assert.That(proseRun.StandardError).Contains("A paragraph that belongs in the preamble.");
        await Assert.That(proseRun.StandardError).Contains("preamble");

        // The positive control on the same call: the unmodified fixture, which
        // carries a preamble AND a second paragraph inside an entry, is still
        // accepted — so neither refusal is about prose or about blank lines.
        var good = await WriteAsync(scratch, "GOOD.md", Fixture);
        var accepted = await RunScriptAsync(NotesScript, "-Path", good, "-Version", "9.9.9", "-Destination", body);

        await Assert.That(accepted.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// A version with no section refuses rather than producing an empty body.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AVersionWithNoSectionRefusesRatherThanWritingAnEmptyBody()
    {
        using var scratch = ScratchDirectory.Create("release-notes-missing");
        var changelog = await WriteAsync(scratch, "CHANGELOG.md", Fixture);
        var body = Path.Combine(scratch.Path, "body.md");

        var run = await RunScriptAsync(NotesScript, "-Path", changelog, "-Version", "8.8.8", "-Destination", body);

        await Assert.That(run.ExitCode).IsNotEqualTo(0);
        await Assert.That(run.StandardError).Contains("8.8.8");
        await Assert.That(File.Exists(body)).IsFalse();
    }

    /// <summary>
    /// A changelog written in the shape the generator reads, small enough to
    /// assert the whole output of.
    /// </summary>
    /// <remarks>
    /// It carries the two things a real section has that a naive fixture would
    /// not: a hard-wrapped detail, because the file is wrapped at 80 columns and
    /// a release body is not, and an entry with <b>no</b> detail at all, which
    /// must produce a headline and no empty fold.
    /// </remarks>
    private const string Fixture = """
        # Changelog

        Everything notable, in the house format.

        ✨ new capability · 🐛 fix

        ## [Unreleased]

        ## [9.9.9] - 2026-01-01

        A preamble sentence
        wrapped over two lines.

        ### Added

        - ✨ **A new thing.** It does a thing,
          over two lines.

          And a second paragraph.

        ### Fixed

        - 🐛 **An old thing was wrong.**

        ## [0.0.1] - 2025-12-31

        ### Added

        - ✨ **The first thing.**
        """;

    /// <summary>The same text with LF line endings, whatever the checkout did.</summary>
    /// <remarks>
    /// The generator writes LF because that is what
    /// <c>gh release create --notes-file</c> sends; a raw string literal in this
    /// file carries whatever git checked the source out with, which on this
    /// machine is CRLF. Comparing them without this asserts on
    /// <c>core.autocrlf</c>.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns>It, with LF line endings.</returns>
    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

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

    private static async Task<ScriptRun> RunAsync(string changelog, params string[] arguments) =>
        await RunScriptAsync(Script, [.. new[] { "-Path", changelog }, .. arguments]);

    /// <summary>Runs one of the release-notes scripts and returns what it said.</summary>
    /// <param name="script">The script.</param>
    /// <param name="arguments">Its arguments, in full.</param>
    /// <returns>Its exit code, its stdout and its stderr.</returns>
    private static async Task<ScriptRun> RunScriptAsync(string script, params string[] arguments)
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

        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments))
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

    /// <summary>
    /// The shape every entry is written in: one icon, a bold headline, and then
    /// whatever the entry has to say.
    /// </summary>
    /// <remarks>
    /// The icon is matched as *whatever is between the dash and the bold run*
    /// rather than as an emoji class, so an entry that put a word there fails on
    /// the palette check with the word quoted, which is a better message than a
    /// pattern that simply did not match.
    /// </remarks>
    [GeneratedRegex(@"^-\s+(?<icon>\S+)\s+\*\*(?<headline>.+?)\*\*")]
    private static partial Regex EntryShape();

    /// <summary>
    /// A sentence ending inside a headline: terminal punctuation followed by a
    /// space.
    /// </summary>
    /// <remarks>
    /// <b>A period inside a version number or a filename is not one</b>, because
    /// neither is followed by whitespace — <c>1.0.0</c>, <c>lock.json</c> and
    /// <c>README.md#status</c> all pass, and they are what a changelog headline
    /// is mostly made of.
    /// </remarks>
    [GeneratedRegex(@"[.!?]\s")]
    private static partial Regex SentenceBreak();

    /// <summary>A run of whitespace, for unwrapping an entry that the file wrapped.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Three parts and an optional pre-release suffix, with no leading `v`.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$")]
    private static partial Regex BareVersion();
}

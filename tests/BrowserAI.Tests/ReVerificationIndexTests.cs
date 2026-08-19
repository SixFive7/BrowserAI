// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// Guards the <c>Automated by</c> column of the re-verification index in
/// <c>kb/re-verification.md</c>, and the marker count that index reports about
/// itself.
/// </summary>
/// <remarks>
/// <para>
/// The index lists the measured facts a version bump can silently invalidate.
/// A row naming a test is answered by the suite; a row marked <i>manual</i>
/// must be answered by name in the <c>upstream-review.json</c> entry. The rule
/// this class enforces is the one the index states about itself: <b>naming a
/// test that does not exist is worse than leaving a row manual, because it
/// reads as covered.</b>
/// </para>
/// <para>
/// It is a real failure and not a hypothetical one. Wiring the <c>Automated by</c>
/// column found eight rows naming test types that no build had ever produced —
/// written from spike work that never reached the suite — and a planning
/// document asserting those tests already existed.
/// </para>
/// <para>
/// <b>The index moved out of <c>kb/README.md</c> on 2026-08-17</b>, because it
/// was 74% of a file whose job is to be an article index. The anchor sentence
/// it is read by is byte-identical across that move except for the marker
/// count, which is the point: rewording the anchor is what would unhook this
/// check, so it was not reworded.
/// </para>
/// <para>
/// <b>The count is scoped to the articles, and that is a fix rather than a
/// convenience.</b> It used to sweep every tracked <c>.md</c> in the
/// repository, which meant five sentences of prose <i>about</i> the convention —
/// in <c>CLAUDE.md</c>, <c>TODO.md</c> and the plan — were counted as if they
/// stamped facts, and the recorded number was 195 for 190 stamped facts. The
/// counter now reads <c>kb/</c> and nothing else, minus the two pages whose job
/// is to discuss the convention. A real marker added anywhere in an article is
/// still red.
/// </para>
/// </remarks>
internal sealed partial class ReVerificationIndexTests
{
    private static readonly string[] IgnoredDirectories = [".git", ".work", "payload", "bin", "obj", "node_modules"];

    /// <summary>
    /// The two pages under <c>kb/</c> that are not articles: they discuss the
    /// convention rather than stamping facts with it, so their occurrences of
    /// the token are mentions and must not be counted.
    /// </summary>
    private static readonly string[] NotArticles = ["README.md", "re-verification.md"];

    [Test]
    public async Task EveryRowIsEitherManualOrNamesSomethingThatExists()
    {
        var offenders = new List<string>();

        foreach (var (number, automatedBy, _) in Rows())
        {
            if (automatedBy.Contains("manual", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var named = Backticked().Matches(automatedBy).Select(match => match.Groups[1].Value).ToList();
            if (named.Count == 0)
            {
                offenders.Add($"row {number}: '{automatedBy}' names neither a test nor manual");
                continue;
            }

            offenders.AddRange(named
                .Where(name => !Exists(name))
                .Select(name => $"row {number}: '{name}' does not exist"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task TheIndexReportsItsOwnSizeCorrectly()
    {
        var recorded = RecordedCounts();

        // The sentence this reads is the anchor. If it has been reworded, the
        // counts it carries are no longer checked by anything, which is the
        // exact drift the note in that file exists to complain about.
        await Assert.That(recorded.Success).IsTrue();

        var rows = Rows();
        await Assert.That(int.Parse(recorded.Groups["lines"].Value, CultureInfo.InvariantCulture)).IsEqualTo(rows.Count);
        await Assert.That(int.Parse(recorded.Groups["rows"].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(rows.Select(row => Digits().Match(row.Number).Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Test]
    public async Task TheRecordedFloatsMarkerCountIsWhatTheTreeHolds()
    {
        // "Across the articles", counted literally: the Markdown under kb/,
        // minus the two pages that are about the convention rather than stamped
        // with it. Recorded here rather than left as a habit because the note in
        // that file has already been wrong twice, both times by arithmetic
        // rather than by counting.
        var acrossTheArticles = MarkersAcrossTheArticles();
        var recorded = RecordedCounts();

        await Assert.That(recorded.Success).IsTrue();
        await Assert.That(acrossTheArticles).IsEqualTo(int.Parse(recorded.Groups["markers"].Value, CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task TheTwoPagesExcludedFromTheCountAreTheOnlyOnesThatDiscussTheConvention()
    {
        // The exclusion list is justified by a fact about the tree, so the fact
        // is asserted rather than remembered: both files must exist, and both
        // must really carry the token, or the list is hiding an article.
        var excluded = NotArticles
            .Select(name => Path.Combine(RepositoryLayout.Root.FullName, "kb", name))
            .ToList();

        foreach (var path in excluded)
        {
            await Assert.That(File.Exists(path)).IsTrue();
            await Assert.That(Floats().Count(await File.ReadAllTextAsync(path))).IsGreaterThan(0);
        }

        // And the scan must find the articles at all -- a narrowing that left
        // the corpus empty would satisfy every assertion above.
        await Assert.That(ArticleFiles().Count()).IsGreaterThan(10);
    }

    private static string IndexPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "kb", "re-verification.md");

    private static Match RecordedCounts() => RecordedCountsPattern().Match(File.ReadAllText(IndexPath));

    /// <summary>
    /// Every <c>[FLOATS]</c> marker in the corpus, which is what the anchor
    /// sentence's first number is checked against.
    /// </summary>
    /// <remarks>
    /// <b>Internal since 2026-08-19, so the per-article column in that file's
    /// "Where the holes are" table can be required to sum to it.</b> A
    /// breakdown that agrees with nothing is how that table drifted 19 of its 28
    /// numbers while the total beside it stayed asserted and correct.
    /// </remarks>
    /// <returns>The count across every article.</returns>
    internal static int MarkersAcrossTheArticles() =>
        ArticleFiles().Sum(MarkersIn);

    /// <summary>One article's <c>[FLOATS]</c> markers.</summary>
    /// <remarks>
    /// <b>The corpus-wide count is the sum of this, rather than a second scan.</b>
    /// Two implementations of "what is a marker" are two answers waiting to
    /// disagree, and this file already carries the note about a count that swept
    /// the wrong corpus and was wrong by five.
    /// </remarks>
    /// <param name="file">The article, absolute.</param>
    /// <returns>How many markers it carries.</returns>
    internal static int MarkersIn(string file) => Floats().Count(File.ReadAllText(file));

    /// <summary>Every knowledge-base article: the Markdown under <c>kb/</c> that stamps facts.</summary>
    /// <remarks>
    /// <b>Internal rather than private so the marker corpus has one definition.</b>
    /// <c>RecordedCountTests</c> checks a second claim about the same corpus — that
    /// no article carries a <c>[STALE]</c> marker — and a scan of its own would be
    /// free to disagree with this one about which files are articles.
    /// </remarks>
    internal static IEnumerable<string> ArticleFiles() =>
        MarkdownFiles(Path.Combine(RepositoryLayout.Root.FullName, "kb"))
            .Where(file => !NotArticles.Contains(
                Path.GetRelativePath(Path.Combine(RepositoryLayout.Root.FullName, "kb"), file),
                StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> MarkdownFiles(string root) =>
        Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(RepositoryLayout.Root.FullName, file)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => IgnoredDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase)));

    /// <summary>
    /// Every numbered row of the index: its number, its <c>Automated by</c>
    /// cell, and everything it says.
    /// </summary>
    /// <remarks>
    /// <b>Internal since 2026-08-19, so "what counts as a row" has one
    /// definition.</b> The "Where the holes are" table publishes a per-article
    /// row count, and a second parser for the same table would be free to
    /// disagree with this one — which is exactly how that table came to be
    /// wrong. <c>Content</c> is the four content cells joined, so a citation is
    /// counted wherever in the row it was written rather than only in
    /// <c>Fact</c>.
    /// </remarks>
    /// <returns>The rows, in file order.</returns>
    internal static List<(string Number, string AutomatedBy, string Content)> Rows()
    {
        var rows = new List<(string, string, string)>();

        foreach (var line in File.ReadAllLines(IndexPath))
        {
            if (!line.StartsWith('|'))
            {
                continue;
            }

            // Five cells, with an empty before the first pipe and after the
            // last. A row that does not split into exactly that is not a row
            // of this table.
            var cells = line.Split('|', StringSplitOptions.None);
            if (cells.Length != 7)
            {
                continue;
            }

            var number = cells[1].Trim();
            if (RowNumber().IsMatch(number))
            {
                rows.Add((number, cells[5].Trim(), string.Join(" ", cells[2..6])));
            }
        }

        return rows;
    }

    private static bool Exists(string name)
    {
        // A path is a build script; anything else is a type in this assembly,
        // optionally with the method that carries the assertion.
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            return File.Exists(Path.Combine(RepositoryLayout.Root.FullName, name));
        }

        var parts = name.Split('.', 2);
        var type = typeof(ReVerificationIndexTests).Assembly.GetTypes()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, parts[0], StringComparison.Ordinal));

        return type is not null && (parts.Length == 1 || type.GetMethod(parts[1]) is not null);
    }

    [GeneratedRegex(@"^\d+[a-z]?$")]
    private static partial Regex RowNumber();

    [GeneratedRegex(@"^\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Backticked();

    [GeneratedRegex(@"\[FLOATS\]")]
    private static partial Regex Floats();

    [GeneratedRegex(
        @"\*\*(?<markers>\d+)\*\*\s+`\[FLOATS\]`\s+markers stand across the articles against the\s+" +
        @"\*\*(?<rows>\d+)\*\*\s+numbered rows\s+below\s+\((?<lines>\d+)\s+lines")]
    private static partial Regex RecordedCountsPattern();
}

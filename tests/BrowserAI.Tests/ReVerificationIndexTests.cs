// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// Guards the <c>Automated by</c> column of the re-verification index in
/// <c>kb/README.md</c>, and the marker count that index reports about itself.
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
/// written from spike work that lived in <c>.work/</c>, never in the suite — and
/// a planning document asserting those tests already existed.
/// </para>
/// </remarks>
internal sealed partial class ReVerificationIndexTests
{
    private static readonly string[] IgnoredDirectories = [".git", ".work", "payload", "bin", "obj", "node_modules"];

    [Test]
    public async Task EveryRowIsEitherManualOrNamesSomethingThatExists()
    {
        var offenders = new List<string>();

        foreach (var (number, automatedBy) in Rows())
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
        // The index's own command, in code: every tracked Markdown file except
        // this index's self-references, and never the gitignored scratch tree.
        // Recorded here rather than left as a habit because the note in that
        // file has already been wrong twice, both times by arithmetic rather
        // than by counting.
        var acrossTheArticles = MarkersAcrossTheArticles();
        var recorded = RecordedCounts();

        await Assert.That(recorded.Success).IsTrue();
        await Assert.That(acrossTheArticles).IsEqualTo(int.Parse(recorded.Groups["markers"].Value, CultureInfo.InvariantCulture));
    }

    private static string IndexPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "kb", "README.md");

    private static Match RecordedCounts() => RecordedCountsPattern().Match(File.ReadAllText(IndexPath));

    private static int MarkersAcrossTheArticles() =>
        MarkdownFiles().Sum(file => Floats().Count(File.ReadAllText(file)))
        - Floats().Count(File.ReadAllText(IndexPath));

    private static IEnumerable<string> MarkdownFiles() =>
        Directory.EnumerateFiles(RepositoryLayout.Root.FullName, "*.md", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(RepositoryLayout.Root.FullName, file)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => IgnoredDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase)));

    /// <summary>Every numbered row of the index, with its <c>Automated by</c> cell.</summary>
    private static List<(string Number, string AutomatedBy)> Rows()
    {
        var rows = new List<(string, string)>();

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
                rows.Add((number, cells[5].Trim()));
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

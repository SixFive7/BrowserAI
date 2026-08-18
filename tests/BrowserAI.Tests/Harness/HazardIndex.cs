// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>One row of the hazard index in <c>HAZARDS.md</c>.</summary>
/// <param name="Line">The line it is written on, so an offender can be opened.</param>
/// <param name="Area">The <c>Area</c> cell, which is also the category a tally counts by.</param>
/// <param name="Hazard">The <c>Hazard</c> cell.</param>
/// <param name="Status">The <c>Status</c> cell, verbatim, emphasis and dates included.</param>
/// <param name="Evidence">The <c>How we proved it</c> cell.</param>
internal sealed record HazardRow(int Line, string Area, string Hazard, string Status, string Evidence)
{
    /// <summary>
    /// The state this row declares: exactly <c>open</c>, exactly <c>closed</c>,
    /// or <see langword="null"/> when it declares neither.
    /// </summary>
    public string? State => HazardIndex.StateOf(Status);

    /// <summary>
    /// Whether this row is <c>open</c> and has nothing in its evidence cell —
    /// the predicate the tally in <c>TODO.md</c> counts.
    /// </summary>
    /// <remarks>
    /// <b>Named rather than inlined, because getting this predicate wrong is the
    /// specific accident this whole mechanism exists for.</b> A re-count once
    /// measured <i>rows that are <c>open</c> at all</i> — a different question
    /// over the same table — and "corrected" a figure that had been right. Both
    /// the sentence in <c>TODO.md</c> and the test that checks it now read this
    /// one property, so they cannot be asking different questions.
    /// </remarks>
    public bool IsOpenAndUnadjudicated =>
        State is HazardIndex.Open && Evidence is "" or "—" or "-";
}

/// <summary>
/// The hazard index, parsed once and read by everything that counts it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One parser, because two would eventually disagree.</b>
/// <c>HazardIndexTests</c> checks the symbols a row names and the two-state
/// invariant; <c>RecordedCountTests</c> checks the tallies documents publish
/// about this table. Those were separate readings of the same file until
/// 2026-08-18, and the counting-discipline rule this repository states —
/// <i>the count and its check derive from one implementation</i> — is not
/// satisfiable while they are.
/// </para>
/// <para>
/// <b>The row shape is asserted rather than assumed</b>, by
/// <c>HazardIndexTests.TheTableIsStillTheShapeThisReads</c>: a renamed column or
/// a row rewritten to seven cells makes every check here pass over nothing,
/// silently, which is the failure mode of every test that reads a document.
/// </para>
/// </remarks>
internal static partial class HazardIndex
{
    /// <summary>The one spelling of the open state.</summary>
    public const string Open = "open";

    /// <summary>The one spelling of the closed state.</summary>
    public const string Closed = "closed";

    /// <summary>Where the index lives.</summary>
    public static string Path { get; } =
        System.IO.Path.Combine(RepositoryLayout.Root.FullName, "HAZARDS.md");

    /// <summary>Every row of the hazard table, in file order.</summary>
    /// <returns>The rows.</returns>
    public static List<HazardRow> Rows()
    {
        var rows = new List<HazardRow>();
        var number = 0;

        foreach (var line in File.ReadAllLines(Path))
        {
            number++;

            if (!line.StartsWith('|'))
            {
                continue;
            }

            // Six cells, with an empty before the first pipe and after the last.
            // A line that does not split into exactly that is not a row of this
            // table -- which includes the header and the separator.
            var cells = line.Split('|', StringSplitOptions.None);

            if (cells.Length != 8 || cells[1].Trim() is "Area" || cells[1].Trim().Trim('-', ':', ' ').Length is 0)
            {
                continue;
            }

            rows.Add(new HazardRow(number, cells[1].Trim(), cells[2].Trim(), cells[5].Trim(), cells[6].Trim()));
        }

        return rows;
    }

    /// <summary>
    /// The state a <c>Status</c> cell declares: exactly <c>open</c>, exactly
    /// <c>closed</c>, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The leading word, matched exactly — never containment.</b> Until
    /// 2026-08-18 the check asked whether the cell <i>contained</i> "open" or
    /// "closed", and a row reading <c>**half closed**</c> passed it for eight
    /// days: the invariant the test's name promised was not enforced, and the row
    /// was in neither tally. Containment is also ambiguous in the other
    /// direction — <c>**open** — bounded, not closed</c> contains both words, and
    /// under the old rule counted as closed.
    /// </para>
    /// <para>
    /// <b>What is deliberately still allowed after the word.</b> A closure date
    /// (<c>closed 2026-08-16</c>), Markdown emphasis, and a qualifying clause
    /// (<c>closed, with a stated limit</c>). Those carry information the file
    /// needs and none of them changes the state; requiring the cell to be nothing
    /// but the bare word would delete every closure date in the table to satisfy
    /// a parser.
    /// </para>
    /// </remarks>
    /// <param name="status">The <c>Status</c> cell.</param>
    /// <returns><see cref="Open"/>, <see cref="Closed"/>, or <see langword="null"/>.</returns>
    public static string? StateOf(string status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var plain = Emphasis().Replace(status, string.Empty).Trim();

        if (plain.Length is 0)
        {
            return null;
        }

        // Compared case-insensitively and then returned as the CANONICAL
        // spelling rather than lower-cased and returned: callers switch on
        // HazardIndex.Open and HazardIndex.Closed, so the answer has to be one of
        // those two strings and not merely equal to one ignoring case.
        var word = plain.Split(' ', '\t')[0].Trim(',', '.', ':', ';');

        if (string.Equals(word, Open, StringComparison.OrdinalIgnoreCase))
        {
            return Open;
        }

        return string.Equals(word, Closed, StringComparison.OrdinalIgnoreCase) ? Closed : null;
    }

    [GeneratedRegex(@"\*")]
    private static partial Regex Emphasis();
}

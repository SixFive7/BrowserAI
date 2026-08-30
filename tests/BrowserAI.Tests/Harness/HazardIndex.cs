// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
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

    /// <summary>
    /// What a row of the hazard index splits into: six cells, with an empty
    /// field before the first pipe and another after the last.
    /// </summary>
    /// <remarks>
    /// <b>Named rather than written at each reader, because a line that splits
    /// into any other number is dropped in silence.</b> That is the one failure
    /// this table's counting mechanism cannot describe: a skipped line is absent
    /// from the `open` tally and the `closed` tally at once, so
    /// <c>RecordedCountTests</c> sees only that a number moved and never which
    /// row went missing. <c>HazardIndexTests.EveryLineOfTheTableSplitsIntoTheFieldsTheParserReads</c>
    /// is what turns that into a named line number.
    /// </remarks>
    public const int Fields = 8;

    /// <summary>The <c>Area</c> cell of the header row, which is how the table is found.</summary>
    public const string HeaderCell = "Area";

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

            // A line that does not split into exactly Fields is not a row of this
            // table -- which includes the header, the separator, and the
            // three-column table above the index explaining what each column is
            // for. It ALSO includes a row somebody wrote a bare pipe into, and
            // that one is a defect rather than a non-row; the guard named on
            // Fields is what tells the two apart out loud.
            var cells = SplitRow(line);

            if (cells.Length != Fields || cells[1].Trim() is HeaderCell || cells[1].Trim().Trim('-', ':', ' ').Length is 0)
            {
                continue;
            }

            rows.Add(new HazardRow(number, cells[1].Trim(), cells[2].Trim(), cells[5].Trim(), cells[6].Trim()));
        }

        return rows;
    }

    /// <summary>
    /// The lines of the hazard index table itself, header and separator
    /// included, each with the file line it is written on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A second, deliberately independent enumeration of the same table.</b>
    /// <see cref="Rows"/> scans the whole file and keeps whatever looks like a
    /// row; this walks the contiguous run of pipe-leading lines that starts at
    /// the header. The two share the <i>predicate</i> — <see cref="SplitRow"/>
    /// and <see cref="Fields"/> — and nothing else, which is what lets one check
    /// the other: a region that stopped early and a line the parser dropped both
    /// show up as the two counts disagreeing.
    /// </para>
    /// <para>
    /// <b>It begins at the header rather than at the first pipe in the file</b>,
    /// because <c>HAZARDS.md</c> opens with a three-column table describing the
    /// index's own columns. Those lines split into five fields perfectly
    /// legitimately, and a guard that read them would have to be taught an
    /// exception — at which point the exception, not the rule, is what a future
    /// malformed row would land in.
    /// </para>
    /// </remarks>
    /// <returns>The header line, the separator line, and every row line after them.</returns>
    public static List<(int Line, string Text)> TableLines()
    {
        var region = new List<(int Line, string Text)>();
        var lines = File.ReadAllLines(Path);
        var started = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (!started)
            {
                var cells = SplitRow(line);

                if (line.StartsWith('|') && cells.Length == Fields && cells[1].Trim() is HeaderCell)
                {
                    started = true;
                    region.Add((index + 1, line));
                }

                continue;
            }

            if (!line.StartsWith('|'))
            {
                break;
            }

            region.Add((index + 1, line));
        }

        return region;
    }

    /// <summary>
    /// Splits one Markdown table line into its fields, honouring <c>\|</c> as a
    /// literal pipe rather than a separator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The escape is GitHub-Flavoured Markdown's own rule, not an invention
    /// here</b>: inside a table row a backslash-escaped pipe is content and the
    /// renderer prints one pipe. Reading it the same way means the parser and
    /// the rendered file agree about where a cell ends, and it is what makes the
    /// guard's advice — <i>write it <c>\|</c></i> — true rather than a
    /// suggestion that moves the row from one silent skip to another.
    /// </para>
    /// <para>
    /// A backslash before anything else is ordinary text and is left exactly as
    /// written, so nothing already in the file changes meaning. On 2026-08-30
    /// the file contained no <c>\|</c> at all and every one of its 199
    /// pipe-leading lines split identically before and after this was added.
    /// </para>
    /// </remarks>
    /// <param name="line">A line of a Markdown table.</param>
    /// <returns>The fields, with each <c>\|</c> restored to a single pipe.</returns>
    public static string[] SplitRow(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var cells = new List<string>();
        var cell = new StringBuilder();

        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] is '\\' && index + 1 < line.Length && line[index + 1] is '|')
            {
                cell.Append('|');
                index++;
                continue;
            }

            if (line[index] is '|')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            cell.Append(line[index]);
        }

        cells.Add(cell.ToString());

        return [.. cells];
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

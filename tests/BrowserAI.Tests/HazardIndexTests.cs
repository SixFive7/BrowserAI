// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;
using System.Text.RegularExpressions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Guards the <c>How we proved it</c> column of the hazard index in
/// <c>HAZARDS.md</c>: a row that names a symbol names one that exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in the suite read this file until 2026-08-17</b>, and it is the
/// longest-lived document in the repository — 137 rows on that day, 76 of them
/// <c>closed</c>, each closure resting on evidence nobody checked. (Those two
/// figures are a measurement of 2026-08-17 and are deliberately left at it; the
/// live tally is checked against the sentence <c>HAZARDS.md</c> publishes about
/// itself by <c>RecordedCountTests</c> — <i>corrected 2026-08-19, previously
/// "the sentence in <c>TODO.md</c>"</i>, which is where it lived while it was a
/// backlog of unadjudicated rows rather than an assertion that there are none.)
/// The rule it
/// states about itself is the rule enforced here, borrowed from the
/// re-verification index: <b>naming a test that does not exist is worse than
/// leaving a row open, because it reads as covered.</b>
/// </para>
/// <para>
/// <b>It found three stale claims on the first run.</b> Two rows credited
/// <c>DirectStdioClientTransport.BuildStartInfo</c>, a method that has never
/// existed in the shipped tree — the child is started through
/// <c>JobLauncher</c> and <c>CreateProcessW</c>, which has no
/// <c>ProcessStartInfo</c> to build. The claim had also been copied into an XML
/// doc comment, which is how a wrong sentence becomes two.
/// </para>
/// <para>
/// <b>Why the check is not limited to names ending in <c>Tests</c>.</b> A row
/// closed on a product symbol is making exactly the same kind of claim as one
/// closed on a test, and it decays the same way — the two stale rows above were
/// product symbols, not test names. Any backticked <c>Type.Member</c> whose type
/// resolves in either of this repository's two assemblies is therefore checked.
/// Framework symbols such as <c>Directory.Move</c> resolve in neither and are
/// skipped: they are not this repository's to guarantee, and a resolver wide
/// enough to find them would also start matching prose.
/// </para>
/// <para>
/// <b>What this deliberately does not check:</b> whether the evidence a row
/// names is evidence <i>for that row</i>. It cannot — that is a reading, not a
/// lookup. The audit that produced this file found one row closed on a test that
/// proves something else entirely, and a second row, open, that the same test
/// closes. Both were fixed by hand. This gate would not have caught either, and
/// saying so is the point: it makes the names real, and a human still has to
/// make them relevant.
/// </para>
/// </remarks>
internal sealed partial class HazardIndexTests
{
    /// <summary>
    /// The two assemblies this repository owns: the suite and the product.
    /// </summary>
    /// <remarks>
    /// <b>Internal since 2026-08-26, so "a symbol this repository can answer
    /// for" has one definition.</b> <c>ReVerificationIndexTests</c> asks the same
    /// question of its own index and searched the test assembly alone, which is
    /// one of the three axes on which the two gates had silently diverged — and
    /// <c>CLAUDE.md</c> presents them as one class of mechanism.
    /// </remarks>
    internal static Assembly[] OurAssemblies { get; } =
        [typeof(HazardIndexTests).Assembly, typeof(BrowserAI.Protocol.StdioChannel).Assembly];

    [Test]
    public async Task EveryRowThatNamesASymbolNamesOneThatExists()
    {
        var offenders = new List<string>();

        foreach (var row in HazardIndex.Rows())
        {
            offenders.AddRange(Named(row.Evidence)
                .Where(Missing)
                .Select(name => $"HAZARDS.md:{row.Line}: '{name}' does not exist — {Excerpt(row.Hazard)}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task NoRowIsClosedOnAnEmptyEvidenceCell()
    {
        // The file's own rule, in its column table: "A row marked `closed` with
        // `—` here is not closed. That is the whole point of splitting the two
        // columns: `closed` is a claim, and this column is what makes it
        // checkable." It was true of every row on 2026-08-17 and had never been
        // asserted, so the next row closed in a hurry was free to break it.
        var offenders = HazardIndex.Rows()
            .Where(row => row.State is HazardIndex.Closed)
            .Where(row => row.Evidence.Length == 0 || row.Evidence is "—" or "-")
            .Select(row => $"HAZARDS.md:{row.Line}: closed with no evidence — {Excerpt(row.Hazard)}");

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task EveryRowIsOpenOrClosedAndNothingElse()
    {
        // The Status column has two values by the file's own definition. A third
        // spelling is not a new state, it is a row that neither the open count
        // nor the closed count will ever include again.
        //
        // ⚠️ Corrected 2026-08-18 (previously: the cell had to CONTAIN "open"
        // or "closed", case-insensitively). That is weaker than this test's name
        // promises and it passed a row reading `**half closed**` for eight days,
        // which is exactly the state the two-state invariant forbids: not open,
        // not closed, and in neither tally. Containment was ambiguous the other
        // way too -- `**open** — bounded, not closed` carries both words, and the
        // old rule counted it as closed because it looked for "closed" first.
        // HazardIndex.StateOf now matches the LEADING word exactly; what may
        // follow it is a date, emphasis or a qualifying clause, none of which
        // changes the state.
        var offenders = HazardIndex.Rows()
            .Where(row => row.State is null)
            .Select(row => $"HAZARDS.md:{row.Line}: status '{row.Status}' declares neither '{HazardIndex.Open}' nor '{HazardIndex.Closed}' as its leading word, so it is in neither tally and no count will ever include it again");

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task TheTableIsStillTheShapeThisReads()
    {
        // Everything above passes vacuously against a parser that has stopped
        // matching -- a renamed column, a row rewritten to seven cells, a table
        // moved to another file. That is the failure mode of every test that
        // reads a document, and it is silent, so the corpus is asserted rather
        // than assumed.
        var rows = HazardIndex.Rows();

        await Assert.That(rows.Count).IsGreaterThan(130);
        await Assert.That(rows.Count(row => row.State is HazardIndex.Closed)).IsGreaterThan(50);
        await Assert.That(rows.Count(row => row.State is HazardIndex.Open)).IsGreaterThan(30);

        // Every row lands in exactly one of the two, which is what makes the two
        // floors above a partition rather than two overlapping counts. Under the
        // containment rule they were not: one row satisfied both.
        await Assert.That(rows.Count(row => row.State is HazardIndex.Open) + rows.Count(row => row.State is HazardIndex.Closed))
            .IsEqualTo(rows.Count);

        // And the names really are being extracted and really are resolving --
        // a matcher that found nothing would leave the gate above green while
        // guarding nothing at all.
        var named = rows.SelectMany(row => Named(row.Evidence)).ToList();

        await Assert.That(named.Count).IsGreaterThan(40);
        await Assert.That(named.Count(name => name.Contains("Tests.", StringComparison.Ordinal))).IsGreaterThan(30);
    }

    [Test]
    public async Task EveryLineOfTheTableSplitsIntoTheFieldsTheParserReads()
    {
        // The one failure this table's counting mechanism cannot describe.
        //
        // `HazardIndex.Rows` keeps a pipe-leading line only when it splits into
        // exactly `Fields`, and drops every other one WITHOUT SAYING SO -- which
        // is right for the header, the separator and the three-column table
        // above the index, and catastrophic for a row somebody wrote a bare `|`
        // into. That row leaves the `open` tally and the `closed` tally in the
        // same instant, so it never shows up as a state that needs adjudicating;
        // the only trace is `RecordedCountTests` reporting that a number moved,
        // and a number moving is what a legitimate edit looks like too. It cost
        // a real row on 2026-08-30: `FileShare.ReadWrite | FileShare.Delete`,
        // written into an evidence cell to say what a fix had opened the file
        // with, split the line into nine fields and deleted the row that the
        // same commit had just re-opened.
        //
        // So this arm is not about pipes. It is the assertion that the parser's
        // silence is only ever spent on lines that are not rows.
        var table = HazardIndex.TableLines();

        var offenders = table
            .Select(line => (line.Line, Cells: HazardIndex.SplitRow(line.Text)))
            .Where(line => line.Cells.Length != HazardIndex.Fields)
            .Select(line => $"HAZARDS.md:{line.Line}: splits into {line.Cells.Length} fields, not {HazardIndex.Fields}, so HazardIndex.Rows() drops it in silence and the row is in NEITHER tally — first cell: {Excerpt(line.Cells.ElementAtOrDefault(1)?.Trim() ?? string.Empty)} — a literal '|' written into a cell is the usual cause: escape it as '\\|', which this parser and Markdown both read as one pipe, or quote the value differently, or reword the cell so it does not need one");

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Everything above passes vacuously against a region that was never
        // found -- a renamed header cell returns nothing and reports nothing
        // wrong with it.
        await Assert.That(table.Count).IsGreaterThan(130)
            .Because($"the hazard table is found by its header's first cell reading '{HazardIndex.HeaderCell}', and only {table.Count} lines were walked from it");

        // And the two enumerations agree. They are deliberately different walks
        // -- Rows() scans the whole file, TableLines() walks the contiguous run
        // below the header -- sharing only the split, so this equality is the
        // half that catches a skip the field count cannot see: a row whose Area
        // cell is empty or all dashes splits into eight and is dropped anyway,
        // and a row written with a leading space ends the region early and takes
        // every row below it out of the guard.
        await Assert.That(table.Count - 2).IsEqualTo(HazardIndex.Rows().Count)
            .Because($"the table region is {table.Count} lines, which is {table.Count - 2} after its header and separator, and HazardIndex.Rows() returns {HazardIndex.Rows().Count} — the difference is lines the parser dropped without saying which");

        // The positive control, because every real line in this file splits into
        // eight and a check that can only ever come back empty is
        // indistinguishable from one that cannot look. The content is the
        // 2026-08-30 mistake verbatim.
        const string BareRow = "| Sessions and locking | **A dump could not read a live writer's file** | — | — | closed 2026-08-30 | `LauncherWait.Evidence` opens FileShare.ReadWrite | FileShare.Delete |";

        await Assert.That(HazardIndex.SplitRow(BareRow).Length).IsNotEqualTo(HazardIndex.Fields)
            .Because("the line the guard exists for has to be a line the guard can see");

        // And the control on the advice. Telling an author to write `\|` is only
        // honest if the parser then reads the row -- otherwise the fix moves a
        // silently-skipped row to a differently-silently-skipped row.
        var escaped = HazardIndex.SplitRow(BareRow.Replace(
            "FileShare.ReadWrite | FileShare.Delete",
            @"FileShare.ReadWrite \| FileShare.Delete",
            StringComparison.Ordinal));

        await Assert.That(escaped.Length).IsEqualTo(HazardIndex.Fields);
        await Assert.That(escaped[6].Trim()).IsEqualTo("`LauncherWait.Evidence` opens FileShare.ReadWrite | FileShare.Delete");
    }

    /// <summary>Every symbol a row's evidence cell names, brace groups expanded.</summary>
    /// <param name="evidenceIncludingHistory">The <c>How we proved it</c> cell, corrections and all.</param>
    /// <returns>Each <c>Type.Member</c> the cell claims exists.</returns>
    private static IEnumerable<string> Named(string evidenceIncludingHistory)
    {
        // A `previously "..."` clause is a record of what a row USED to claim,
        // not a claim. It has to be removed before anything else looks at the
        // cell, because the repository's own correction convention REQUIRES the
        // superseded text to be quoted verbatim -- so the first correction this
        // gate provoked put the stale name straight back into the cell that had
        // just been fixed, and the gate stayed red against a row that was now
        // right. Two conventions that cannot both hold is a defect in one of
        // them; the convention wins, because a correction nobody can read
        // against the value it replaced is the thing it exists to prevent.
        //
        // Shared with ReVerificationIndexTests since 2026-08-26, which read the
        // identical clause and did not strip it. See Harness/CorrectionClause.
        var evidence = CorrectionClause.Strip(evidenceIncludingHistory);

        // `ArtifactRoutingTests.{First, Second, Third}` is one backticked token
        // naming three methods, and it is how the densest rows are written.
        foreach (var group in BraceGroup().Matches(evidence).Cast<Match>())
        {
            foreach (var member in group.Groups["members"].Value.Split(','))
            {
                if (member.Trim() is { Length: > 0 } name)
                {
                    yield return $"{group.Groups["type"].Value}.{name}";
                }
            }
        }

        foreach (var single in DottedName().Matches(BraceGroup().Replace(evidence, string.Empty)).Cast<Match>())
        {
            yield return single.Groups["name"].Value;
        }
    }

    /// <summary>Whether a named symbol is absent from both of this repository's assemblies.</summary>
    /// <param name="name">A <c>Type.Member</c> pair.</param>
    /// <returns><c>true</c> when the type is ours and the member is not on it.</returns>
    private static bool Missing(string name)
    {
        var parts = name.Split('.', 2);

        var type = OurAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .FirstOrDefault(candidate => string.Equals(candidate.Name, parts[0], StringComparison.Ordinal));

        // A type that is not ours is a framework symbol or a word in prose that
        // happens to carry a dot. Neither is a claim this repository can check,
        // and reporting one would be a false failure -- the one outcome worse
        // than the gap.
        return type is not null
            && type.GetMember(
                parts[1],
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy).Length is 0;
    }

    /// <summary>The first words of a hazard, so an offender can be found by eye.</summary>
    /// <param name="hazard">The <c>Hazard</c> cell.</param>
    /// <returns>The cell, unemphasised, clipped to one readable phrase.</returns>
    private static string Excerpt(string hazard)
    {
        var plain = hazard.Replace("*", string.Empty, StringComparison.Ordinal);

        return plain.Length > 70 ? string.Concat(plain.AsSpan(0, 70), "…") : plain;
    }

    /// <summary>
    /// A backticked <c>Type.Member</c>.
    /// </summary>
    /// <remarks>
    /// The member half must begin with a capital, which is what keeps file names
    /// out: <c>MachineMutex.cs</c> and <c>Program.cs</c> are backticked in this
    /// column too, and <c>MachineMutex</c> is a real type, so a rule keyed on the
    /// type alone would report <c>cs</c> as a missing member.
    /// </remarks>
    [GeneratedRegex(@"`(?<name>[A-Z][A-Za-z0-9_]*\.[A-Z][A-Za-z0-9_]*)`")]
    private static partial Regex DottedName();

    /// <summary>A backticked type with several members named at once.</summary>
    [GeneratedRegex(@"`(?<type>[A-Z][A-Za-z0-9_]*)\.\{(?<members>[^}]*)\}`")]
    private static partial Regex BraceGroup();
}

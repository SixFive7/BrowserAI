// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// Guards the <c>How we proved it</c> column of the hazard index in
/// <c>HAZARDS.md</c>: a row that names a symbol names one that exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in the suite read this file until 2026-08-17</b>, and it is the
/// longest-lived document in the repository — 137 rows, 76 of them
/// <c>closed</c>, each closure resting on evidence nobody checked. The rule it
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
    private static string IndexPath { get; } =
        Path.Combine(RepositoryLayout.Root.FullName, "HAZARDS.md");

    private static Assembly[] OurAssemblies { get; } =
        [typeof(HazardIndexTests).Assembly, typeof(BrowserAI.Protocol.StdioChannel).Assembly];

    [Test]
    public async Task EveryRowThatNamesASymbolNamesOneThatExists()
    {
        var offenders = new List<string>();

        foreach (var (line, hazard, _, evidence) in Rows())
        {
            offenders.AddRange(Named(evidence)
                .Where(Missing)
                .Select(name => $"HAZARDS.md:{line}: '{name}' does not exist — {Excerpt(hazard)}"));
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
        var offenders = Rows()
            .Where(row => row.Status.Contains("closed", StringComparison.OrdinalIgnoreCase))
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
        var offenders = Rows()
            .Where(row => !row.Status.Contains("closed", StringComparison.OrdinalIgnoreCase)
                && !row.Status.Contains("open", StringComparison.OrdinalIgnoreCase))
            .Select(row => $"HAZARDS.md:{row.Line}: status '{row.Status}' is neither open nor closed");

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
        var rows = Rows();

        await Assert.That(rows.Count).IsGreaterThan(130);
        await Assert.That(rows.Count(row => row.Status.Contains("closed", StringComparison.OrdinalIgnoreCase))).IsGreaterThan(50);
        await Assert.That(rows.Count(row => row.Status.Contains("open", StringComparison.OrdinalIgnoreCase))).IsGreaterThan(30);

        // And the names really are being extracted and really are resolving --
        // a matcher that found nothing would leave the gate above green while
        // guarding nothing at all.
        var named = rows.SelectMany(row => Named(row.Evidence)).ToList();

        await Assert.That(named.Count).IsGreaterThan(40);
        await Assert.That(named.Count(name => name.Contains("Tests.", StringComparison.Ordinal))).IsGreaterThan(30);
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
        var evidence = PreviousValue().Replace(evidenceIncludingHistory, string.Empty);

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

    /// <summary>Every row of the hazard table, with the line it is written on.</summary>
    private static List<(int Line, string Hazard, string Status, string Evidence)> Rows()
    {
        var rows = new List<(int, string, string, string)>();
        var number = 0;

        foreach (var line in File.ReadAllLines(IndexPath))
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

            rows.Add((number, cells[2].Trim(), cells[5].Trim(), cells[6].Trim()));
        }

        return rows;
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

    /// <summary>
    /// The <c>(previously "…")</c> half of this repository's correction
    /// convention, which quotes the superseded text verbatim.
    /// </summary>
    /// <remarks>
    /// <c>CLAUDE.md</c> calls that clause <b>the load-bearing half</b> — it is
    /// what makes a change legible to someone who learned the file before it
    /// moved. So the clause is authoritative and this gate reads around it,
    /// rather than the reverse.
    /// </remarks>
    [GeneratedRegex(@"previously\s*""[^""]*""")]
    private static partial Regex PreviousValue();
}

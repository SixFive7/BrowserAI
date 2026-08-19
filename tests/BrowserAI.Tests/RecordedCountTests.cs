// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Every count a surviving document publishes about this repository, checked
/// against a live scan.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pattern is <c>ReVerificationIndexTests</c>', generalised.</b> That
/// class reads one sentence in <c>kb/re-verification.md</c> and asserts the
/// three numbers in it against a re-count. It caught nothing on 2026-08-18
/// because it was the only place the pattern had been applied — and four counts
/// in prose were wrong at the same moment, one of them because a "correction"
/// had replaced a right number with a wrong one by measuring a different
/// predicate over the same table.
/// </para>
/// <para>
/// <b>Two rules, and the second is what makes the first worth having.</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>The published number and its check derive from ONE implementation.</b>
/// The hazard tally reads <see cref="HazardIndex"/>, which is also what
/// <c>HazardIndexTests</c> reads; the fragment count calls
/// <c>DocumentationLinkTests.FragmentCountAsync</c>, which is the scan that
/// produces the figure in the first place; the marker corpus is
/// <c>ReVerificationIndexTests.ArticleFiles</c>. Nothing here re-implements a
/// scan, because a second implementation of "what counts as a row" is a second
/// answer waiting to happen.
/// </item>
/// <item>
/// <b>Where a count is per-category, the CATEGORY BREAKDOWN is asserted and not
/// just the total.</b> A wrong total is only visible against the sum of its
/// parts, which is precisely how the earlier error survived: the total moved,
/// the categories did not, and neither was checked against the table.
/// </item>
/// </list>
/// <para>
/// <b>The sentence is the anchor, and rewording it fails the build.</b> That is
/// deliberate and it is the same trade the re-verification index takes: a check
/// keyed on prose can be unhooked by editing the prose, so the unhooking is made
/// loud rather than silent. Every regex below is asserted to have matched before
/// its numbers are read.
/// </para>
/// <para>
/// <b>What is deliberately NOT here, and why</b> — a count nobody can re-derive
/// inside the suite must not be given a test that pretends otherwise:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>README.md</c>'s <i>"N tests, 0 failed, 0 skipped"</i> counts <b>executed
/// test cases</b>, which includes every <c>[Arguments]</c> and
/// <c>[MethodDataSource]</c> expansion. Reflecting over <c>[Test]</c>-attributed
/// <i>methods</i> is a different predicate and would be the exact mistake this
/// file exists to prevent. Re-measure it from a run.
/// </item>
/// <item>
/// <c>CLAUDE.md</c>'s <i>"kept by 222 files out of 224"</i> is a measurement of a
/// past moment — the day before the rule became a test — and is now false by
/// construction, because <c>HouseRuleTests</c> holds it at 100%.
/// </item>
/// <item>
/// <c>TESTING.md</c>'s <i>"tools/list → 24 tools"</i> is a reproduction of the
/// founding bug against upstream's default surface, not a claim about this
/// build's surface.
/// </item>
/// <item>
/// <c>RELEASING.md</c>'s publish-directory table and <c>DECISIONS.md</c>'s
/// installer size need a <c>vpk pack</c> artifact, and <c>Releases/</c> is
/// gitignored — a test would be red on every clean clone and on CI.
/// </item>
/// </list>
/// </remarks>
internal sealed partial class RecordedCountTests
{
    /// <summary>
    /// The tally the hazard index publishes about itself is what the index
    /// holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously
    /// <c>TheHazardTallyInTodoIsWhatTheIndexHolds</c>, reading the same sentence
    /// out of <c>TODO.md</c>).</b> It was written there because the number was a
    /// <i>backlog</i> — 55 rows that were <c>open</c> and carried <c>—</c>,
    /// nobody having adjudicated them either way — and a backlog is work not yet
    /// done, which is what that file is for. The backlog was cleared on
    /// 2026-08-19 and the item deleted, and the sentence had to go somewhere or
    /// the check went with it.
    /// </para>
    /// <para>
    /// <b>At zero it is a stronger mechanism than it was as a backlog</b>, which
    /// is why it moved rather than being deleted. Counting down, it said
    /// <i>somebody should decide these</i>. At zero it says <b>a row that
    /// arrives <c>open</c> with <c>—</c> fails the build</b>, so a hazard has to
    /// be adjudicated when it is written down instead of accumulating for a
    /// later pass.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHazardTallyIsWhatTheIndexHolds()
    {
        var index = await File.ReadAllTextAsync(HazardIndex.Path);

        // Whitespace is normalised because the sentence wraps, and a reflow must
        // not be able to unhook the check that a reword deliberately does.
        var recorded = HazardTally().Match(Whitespace().Replace(index, " "));

        await Assert.That(recorded.Success).IsTrue();

        var rows = HazardIndex.Rows();
        var unadjudicated = rows.Where(row => row.IsOpenAndUnadjudicated).ToList();
        var disagreements = new List<string>();

        Check(
            disagreements,
            "rows that are open AND carry — for evidence",
            recorded,
            "unadjudicated",
            unadjudicated.Count);

        Check(
            disagreements,
            "rows that are open while carrying evidence",
            recorded,
            "withEvidence",
            rows.Count(row => row.State is HazardIndex.Open) - unadjudicated.Count);

        Check(disagreements, "rows that are open", recorded, "open", rows.Count(row => row.State is HazardIndex.Open));
        Check(disagreements, "rows that are closed", recorded, "closed", rows.Count(row => row.State is HazardIndex.Closed));

        // ⚠️ THE CATEGORY BREAKDOWN, not just the total. The total is the only
        // number an eye checks, and it is the one a wrong total hides behind: the
        // categories still summed to something plausible while the total had
        // drifted. Read from the index's OWN `Area` cells, so the sentence must
        // spell them the way the table does and a renamed area is a red build
        // rather than a category that silently stops being counted.
        var byArea = unadjudicated
            .GroupBy(row => row.Area, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var published = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in CategoryTally().Matches(recorded.Groups["categories"].Value).Cast<Match>())
        {
            published[entry.Groups["area"].Value.Trim()] = int.Parse(entry.Groups["count"].Value, CultureInfo.InvariantCulture);
        }

        foreach (var (area, count) in byArea.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!published.TryGetValue(area, out var stated))
            {
                disagreements.Add($"HAZARDS.md's breakdown does not mention '{area}', which the index holds {count} of");
            }
            else if (stated != count)
            {
                disagreements.Add($"HAZARDS.md says '{area} {stated}'; the index holds {count}");
            }
        }

        foreach (var (area, stated) in published.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!byArea.ContainsKey(area))
            {
                disagreements.Add($"HAZARDS.md says '{area} {stated}', and the index has no unadjudicated row in an area of that name");
            }
        }

        // The sum, asserted separately from the parts. A breakdown that agrees
        // cell for cell with a total nobody added up is the shape that produced
        // the original defect.
        if (published.Values.Sum() != unadjudicated.Count)
        {
            disagreements.Add(
                $"HAZARDS.md's categories sum to {published.Values.Sum()} and its total says {unadjudicated.Count}; the index holds {unadjudicated.Count}");
        }

        await Assert.That(string.Join(Environment.NewLine, disagreements)).IsEmpty();

        // Not vacuous: a parser that stopped matching rows would make every
        // count zero and every comparison trivially satisfiable once the
        // sentence was edited to say zero.
        //
        // ⚠️ Corrected 2026-08-19 (previously `published.Count > 4` and
        // `unadjudicated.Count > 20`). Both were floors placed under the very
        // thing they were watching be counted DOWN: adjudicating the
        // unadjudicated backlog empties one category at a time and drives that
        // count towards zero, so the original pair would have gone red on the
        // third category -- a test failing because the work it watches got done,
        // which is worse than no guard because the fix looks like weakening it.
        // What they were really protecting against is a parser that has stopped
        // finding rows, and that is what is asserted now: the corpus is still a
        // corpus, every row still lands in exactly one of the two states, and
        // both states are populated. None of the three can be satisfied by an
        // empty read, and none of them moves when a row is adjudicated.
        await Assert.That(rows.Count).IsGreaterThan(130);
        await Assert.That(rows.Count(row => row.State is HazardIndex.Open) + rows.Count(row => row.State is HazardIndex.Closed)).IsEqualTo(rows.Count);
        await Assert.That(rows.Count(row => row.State is HazardIndex.Closed)).IsGreaterThan(50);
    }

    [Test]
    public async Task TheFragmentCountInClaudeMdIsWhatTheScanFinds()
    {
        var claude = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "CLAUDE.md"));
        var recorded = FragmentCount().Match(Whitespace().Replace(claude, " "));

        await Assert.That(recorded.Success).IsTrue();

        // The same scan that produces the number, called rather than copied. The
        // figure in CLAUDE.md is a stamp on THIS count and on nothing else.
        var live = await DocumentationLinkTests.FragmentCountAsync();
        var stated = int.Parse(recorded.Groups["fragments"].Value, CultureInfo.InvariantCulture);

        await Assert.That(stated)
            .IsEqualTo(live)
            .Because($"CLAUDE.md publishes {stated} `#fragment` links and the scan finds {live}. Re-measure and stamp it -- never adjust it by counting the links in a diff.");
    }

    [Test]
    public async Task NoKnowledgeBaseArticleCarriesAStaleMarker()
    {
        // kb/README.md's own claim, in the conventions table: "That no article
        // carries one today is the healthy state, not evidence the marker is
        // dead." It is a claim about the tree and it was held by nobody.
        var carriers = ReVerificationIndexTests.ArticleFiles()
            .Where(file => Stale().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, carriers))
            .IsEmpty()
            .Because("kb/README.md states that no article carries [STALE]. If one now does, that sentence is what has to change -- the marker is sanctioned and the article is not the defect.");

        // ⚠️ THE POSITIVE CONTROL, because a search that returns zero is
        // indistinguishable from a search that cannot match. CLAUDE.md states the
        // rule in as many words: prove the search can find something before
        // believing that it found nothing. kb/README.md is outside the article
        // corpus and defines the marker, so it must match.
        var definition = Path.Combine(RepositoryLayout.Root.FullName, "kb", "README.md");

        await Assert.That(Stale().Count(await File.ReadAllTextAsync(definition))).IsGreaterThan(0);
        await Assert.That(ReVerificationIndexTests.ArticleFiles().Count()).IsGreaterThan(10);
    }

    /// <summary>
    /// The per-article breakdown in the re-verification index's "Where the holes
    /// are" table is what the articles and the rows hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-19, and it was already wrong.</b> The table was stamped
    /// <i>Counted 2026-08-17</i> and published 28 numbers; <b>19 of them had
    /// drifted</b> — 9 of the 14 marker counts and 10 of the 14 row counts —
    /// while the three totals in the anchor sentence a few lines above it stayed
    /// asserted and correct the whole time. It drifted in <b>both</b> directions
    /// (<c>tools-and-artifacts.md</c> said 30 against 39,
    /// <c>windows/processes.md</c> said 17 against 7), which is why "it will
    /// only ever undercount" was not available as a defence.
    /// </para>
    /// <para>
    /// <b>Three assertions, and the second and third are what make the first
    /// worth having.</b> Cell by cell, both columns. Then the marker column must
    /// <b>sum to</b> the corpus-wide total the anchor sentence publishes — the
    /// check the earlier defect walked straight past, because a breakdown
    /// answerable to nothing can be individually plausible and collectively
    /// impossible. Then the table must name <b>every</b> article in the corpus,
    /// because a map of the holes that silently omits a file is not a map: one
    /// article was in fact missing from it.
    /// </para>
    /// <para>
    /// <b>Both predicates come from <c>ReVerificationIndexTests</c> and are not
    /// re-implemented here</b>, per this class's first rule.
    /// <c>MarkersIn</c> is the same token count, over the same corpus, that
    /// produces the total; <c>Rows</c> is the same row parser the
    /// <c>Automated by</c> gate reads. A row is counted for an article when any
    /// of its cells links to it, so a row citing two articles counts once for
    /// each and the column deliberately does not sum to the row count.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHolesTableIsWhatTheArticlesAndTheRowsHold()
    {
        var index = Path.Combine(RepositoryLayout.Root.FullName, "kb", "re-verification.md");
        var articles = Path.Combine(RepositoryLayout.Root.FullName, "kb");
        var published = new List<(string Article, int Markers, int Rows)>();

        foreach (var line in await File.ReadAllLinesAsync(index))
        {
            // Six cells, against the numbered table's seven: an empty before the
            // first pipe and after the last, with Article, Markers, Rows and the
            // prose between them. The article cell is a link, which is what
            // separates this table from any other four-column one.
            var cells = line.Split('|', StringSplitOptions.None);

            if (cells.Length is not 6 || HolesRow().Match(cells[1].Trim()) is not { Success: true } article)
            {
                continue;
            }

            published.Add((
                article.Groups["article"].Value,
                int.Parse(cells[2].Trim(), CultureInfo.InvariantCulture),
                int.Parse(cells[3].Trim(), CultureInfo.InvariantCulture)));
        }

        var rows = ReVerificationIndexTests.Rows();
        var disagreements = new List<string>();

        foreach (var (article, markers, cited) in published)
        {
            var file = Path.Combine(articles, article.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(file))
            {
                disagreements.Add($"the holes table names '{article}', which is not a file under kb/");
                continue;
            }

            var live = ReVerificationIndexTests.MarkersIn(file);

            if (markers != live)
            {
                disagreements.Add($"the holes table says {markers} [FLOATS] markers in '{article}'; the article carries {live}");
            }

            var citing = rows.Count(row => Cites(row.Content, article));

            if (cited != citing)
            {
                disagreements.Add($"the holes table says {cited} numbered rows cite '{article}'; {citing} of them link to it");
            }
        }

        // The corpus, against the map. Reading the article set off the same
        // helper the counts come from, so an article added under kb/ is a red
        // build rather than a row nobody wrote.
        var corpus = ReVerificationIndexTests.ArticleFiles()
            .Select(file => Path.GetRelativePath(articles, file).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        disagreements.AddRange(corpus
            .Where(article => !published.Any(entry => string.Equals(entry.Article, article, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(article => $"'{article}' is an article and the holes table does not list it"));

        disagreements.AddRange(published
            .Where(entry => !corpus.Contains(entry.Article))
            .Select(entry => $"the holes table lists '{entry.Article}', which is not in the article corpus"));

        // The sum, asserted separately from the parts, against the number the
        // anchor sentence already answers for. A breakdown nobody adds up is the
        // shape that produced this defect.
        if (published.Sum(entry => entry.Markers) != ReVerificationIndexTests.MarkersAcrossTheArticles())
        {
            disagreements.Add(
                $"the holes table's marker column sums to {published.Sum(entry => entry.Markers)} and the corpus holds "
                + $"{ReVerificationIndexTests.MarkersAcrossTheArticles().ToString(CultureInfo.InvariantCulture)}");
        }

        await Assert.That(string.Join(Environment.NewLine, disagreements)).IsEmpty();

        // Not vacuous. A parser that stopped matching the table would publish
        // nothing, agree with everything, and sum to a zero that the corpus check
        // above would then have to catch on its own.
        await Assert.That(published.Count).IsGreaterThan(10);
        await Assert.That(published.Count(entry => entry.Rows > 0)).IsGreaterThan(10);
    }

    [Test]
    public async Task TheToolSurfaceNumbersInDecisionsAreWhatTheSnapshotHolds()
    {
        var decisions = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "DECISIONS.md"));
        var normalised = Whitespace().Replace(decisions, " ");

        var whole = SnapshotToolCount().Match(normalised);
        var storage = StorageToolCount().Match(normalised);

        await Assert.That(whole.Success).IsTrue();
        await Assert.That(storage.Success).IsTrue();

        // Every figure below comes from the golden snapshot, which the build
        // regenerates from the resolved payload and diffs on every run -- so an
        // upstream change reaches these sentences as a snapshot diff first and a
        // red count second, which is the right order.
        var union = UpstreamSurface.For(BrowserConfiguration.UnionCapabilities).Count;
        var withoutStorage = UpstreamSurface.For(BrowserConfiguration.BaseCapabilities).Count;
        var everything = UpstreamSurface.SnapshotToolCount();

        await Assert.That(int.Parse(whole.Groups["tools"].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(everything)
            .Because("DECISIONS.md states how many tools the tools-list.json snapshot carries.");

        await Assert.That(int.Parse(storage.Groups["tools"].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(union - withoutStorage)
            .Because("DECISIONS.md states how many tools vanish from a child launched without the storage capability, which is the difference between the two surfaces.");

        // Not vacuous, and the relationship is asserted rather than the numbers:
        // a snapshot that had lost its capability map would make both surfaces
        // equal and the difference zero.
        await Assert.That(everything).IsGreaterThanOrEqualTo(union);
        await Assert.That(union).IsGreaterThan(withoutStorage);
    }

    /// <summary>
    /// Whether one numbered row of the index links to one article.
    /// </summary>
    /// <remarks>
    /// The links are relative to <c>kb/</c>, so the article's own relative path
    /// is what appears inside the brackets, optionally followed by a
    /// <c>#anchor</c>. Matched as <c>(path)</c> or <c>(path#…)</c> rather than by
    /// containment, because <c>mcp/sdk.md</c> is a substring of nothing here
    /// today and would be the day somebody adds <c>mcp/sdk.md.old</c>.
    /// </remarks>
    /// <param name="row">Everything the row says.</param>
    /// <param name="article">The article's path relative to <c>kb/</c>.</param>
    /// <returns>Whether the row cites it.</returns>
    private static bool Cites(string row, string article) =>
        row.Contains($"({article})", StringComparison.Ordinal)
        || row.Contains($"({article}#", StringComparison.Ordinal);

    /// <summary>Compares one named group against a live count.</summary>
    /// <param name="disagreements">Where a mismatch is recorded.</param>
    /// <param name="predicate">The predicate, quoted before the number as CLAUDE.md requires.</param>
    /// <param name="recorded">The matched sentence.</param>
    /// <param name="group">The capture group holding the published figure.</param>
    /// <param name="live">What the scan found.</param>
    private static void Check(List<string> disagreements, string predicate, Match recorded, string group, int live)
    {
        var stated = int.Parse(recorded.Groups[group].Value, CultureInfo.InvariantCulture);

        if (stated != live)
        {
            disagreements.Add($"HAZARDS.md says {stated} {predicate}; the index holds {live}");
        }
    }

    /// <summary>
    /// The tally sentence the hazard index publishes about itself, which is the
    /// anchor for four totals and a per-category breakdown.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-19 (previously <c>rows of the</c> followed by a
    /// Markdown link to the hazard index, matched against <c>TODO.md</c>).</b>
    /// The sentence moved into the file it describes when the backlog it
    /// counted reached zero, so that link became a self-link and the wording
    /// went with it. Nothing else about the shape changed: four named totals and
    /// a category clause with no full stop in it. The superseded text is
    /// paraphrased rather than quoted here for one reason —
    /// <c>DocumentationLinkTests</c> reads every relative link in every
    /// <c>.cs</c> file, and a quoted one resolves against this directory rather
    /// than against the repository root, so quoting it verbatim fails that gate.
    /// </remarks>
    [GeneratedRegex(
        @"\*\*(?<unadjudicated>\d+) rows of this index are `open` and carry `—` for evidence\.\*\*" +
        @".*?By category, using the index's own `Area` cells verbatim: (?<categories>[^.]+)\." +
        @" (?<withEvidence>\d+) more are `open` while carrying evidence, so (?<open>\d+) are `open` in total, against (?<closed>\d+) `closed`\.")]
    private static partial Regex HazardTally();

    /// <summary>One <c>Area name N</c> pair inside the breakdown.</summary>
    /// <remarks>
    /// The area half is greedy up to the count, so a name carrying spaces or
    /// brackets — every one of them does — is captured whole.
    /// </remarks>
    [GeneratedRegex(@"(?<area>[^,]+?)\s+(?<count>\d+)(?:,|$)")]
    private static partial Regex CategoryTally();

    /// <summary>The fragment count published in <c>CLAUDE.md</c>'s mechanism table.</summary>
    [GeneratedRegex(@"`DocumentationLinkTests` — (?<fragments>\d+) fragments as of \d{4}-\d{2}-\d{2}")]
    private static partial Regex FragmentCount();

    /// <summary>The snapshot's tool count, as <c>DECISIONS.md</c> states it.</summary>
    [GeneratedRegex(@"`tools-list\.json` carrying all (?<tools>\d+) tools")]
    private static partial Regex SnapshotToolCount();

    /// <summary>The storage-only tool count, as <c>DECISIONS.md</c> states it.</summary>
    [GeneratedRegex(@"those (?<tools>\d+) tools do not exist in that process")]
    private static partial Regex StorageToolCount();

    /// <summary>
    /// The <c>Article</c> cell of a "Where the holes are" row: a backticked path
    /// inside a Markdown link to the same path.
    /// </summary>
    [GeneratedRegex(@"^\[`(?<article>[^`]+)`\]\(\k<article>\)$")]
    private static partial Regex HolesRow();

    [GeneratedRegex(@"\[STALE\]")]
    private static partial Regex Stale();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

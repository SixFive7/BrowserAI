// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>tool-verdicts.json</c> against the golden snapshot, in both directions,
/// and every shape of that file this build refuses to serve on.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the drift anchor, and it is the half the golden snapshot cannot
/// do.</b> <c>upstream-snapshots/tools-list.json</c> is regenerated from the
/// resolved payload and byte-diffed on every build, so it already says <i>a tool
/// appeared</i>. What it cannot say is <i>and nobody has decided whether we
/// forward it</i> — a snapshot is a record of upstream and a verdict is a
/// statement about this product. The comparison below is what makes the second
/// question a red build rather than a thing somebody remembers to ask.
/// </para>
/// <para>
/// <b>The comparison runs on every build; the ADJUDICATION is release-gated.</b>
/// Those are different things and putting the comparison behind the release gate
/// would be a downgrade — <c>RELEASING.md</c> item 4 is where a human writes down
/// what verdict a new tool got and why, and this is where the build refuses to be
/// green until one exists.
/// </para>
/// <para>
/// <b>Both directions, and the second one is not symmetry for its own sake.</b> A
/// row naming a tool the snapshot does not carry is a tool upstream <i>removed</i>
/// — the row is then a judgement about nothing, and the build should say so while
/// somebody still remembers what it was for.
/// </para>
/// </remarks>
internal sealed class ToolVerdictTests
{
    /// <summary>
    /// A tool name no payload will ever carry, for the arms that need one.
    /// </summary>
    /// <remarks>
    /// Spelled to be obviously synthetic in a failure message: an arm that
    /// accidentally asserted against it would read as a defect rather than as a
    /// plausible upstream name.
    /// </remarks>
    private const string NeverATool = "browser_a_tool_no_payload_carries";

    /// <summary>
    /// The two upstreams a verdict is judged against, and the two the snapshot
    /// records its own provenance from.
    /// </summary>
    private static readonly string[] TheTwoPackages = ["@playwright/mcp", "playwright-core"];

    [Test]
    public async Task EveryToolInTheSnapshotHasAVerdictAndEveryVerdictNamesAToolInTheSnapshot()
    {
        var disagreements = Coverage(RepositoryVerdicts.Committed, Snapshot());

        await Assert.That(string.Join(Environment.NewLine, disagreements)).IsEmpty();

        // Not vacuous. A snapshot read that came back empty would agree with
        // everything, and a verdicts file that failed to parse would never have
        // reached this line -- so the denominator is stated rather than trusted.
        await Assert.That(Snapshot().Count).IsEqualTo(RepositoryVerdicts.Committed.Upstream.Count);
        await Assert.That(Snapshot().Count).IsGreaterThan(40);
    }

    /// <summary>
    /// The same comparison, against a file doctored each way, so the arm above
    /// cannot pass by asking nothing.
    /// </summary>
    /// <remarks>
    /// <b>A search that returns zero needs a positive control</b>, and a coverage
    /// check that returns zero disagreements is exactly that shape: it reads
    /// identically whether the file is complete or the comparison is broken. The
    /// two controls below are the same method, over the same snapshot, with one
    /// row removed and one row invented — and they assert on the <i>text</i> of
    /// the disagreement, because a message that does not name the tool is one
    /// nobody can act on.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCoverageComparisonFailsInBothDirectionsAgainstADoctoredFile()
    {
        var snapshot = Snapshot();
        var judged = snapshot[0];

        // Direction one: an upstream tool nobody judged. This is the Playwright
        // bump that adds a tool, in miniature.
        var unjudged = Coverage(RepositoryVerdicts.Without(judged), snapshot);

        await Assert.That(unjudged.Count).IsEqualTo(1);
        await Assert.That(unjudged[0]).Contains(judged);
        await Assert.That(unjudged[0]).Contains("UPSTREAM-REVIEW.md");

        // Direction two: a verdict about a tool that is not there. This is the
        // Playwright bump that REMOVES one, and it is the direction a
        // one-directional check would miss for ever.
        var document = RepositoryVerdicts.Document();
        document["upstream"]![NeverATool] = new JsonObject { ["verdict"] = "allow" };

        var stale = Coverage(RepositoryVerdicts.Parse(document), snapshot);

        await Assert.That(stale.Count).IsEqualTo(1);
        await Assert.That(stale[0]).Contains(NeverATool);
        await Assert.That(stale[0]).Contains("UPSTREAM-REVIEW.md");
    }

    [Test]
    public async Task TheAuthoredRowsAreExactlyTheToolsBrowserAiAnswersItself()
    {
        var authored = RepositoryVerdicts.Committed.Authored.Select(row => row.Name).ToList();

        // Both directions rather than a count: a file that named seven of the
        // wrong seven would satisfy any arithmetic.
        await Assert.That(string.Join(", ", authored.Except(SessionToolSurface.Names, StringComparer.Ordinal))).IsEmpty();
        await Assert.That(string.Join(", ", SessionToolSurface.Names.Except(authored, StringComparer.Ordinal))).IsEmpty();

        // And every one of them is an `answer`, which is the whole reason they
        // share the file: the record's tool field is drawn from one set whichever
        // branch wrote it.
        await Assert.That(RepositoryVerdicts.Committed.Authored.All(row => row.Kind is ToolVerdictKind.Answer)).IsTrue();
    }

    [Test]
    public async Task EveryDenialCarriesTheRefusalItsCallerReadsAndTheDateItWasJudged()
    {
        var denied = RepositoryVerdicts.Committed.Upstream
            .Where(row => row.Kind is ToolVerdictKind.Deny)
            .ToList();

        var faults = new List<string>();

        foreach (var row in denied)
        {
            if (row.Why is not { Length: > 0 })
            {
                faults.Add($"{row.Name}: denied with no 'why', so its refusal would carry nothing to act on");
            }

            if (!DateOnly.TryParseExact(row.Since ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                faults.Add($"{row.Name}: 'since' is '{row.Since}', which is not an ISO yyyy-MM-dd date");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, faults)).IsEmpty();

        // Not vacuous, and this is also the count DECISIONS.md publishes about
        // the surface: exactly one tool is withheld, and that is asserted here
        // against the FILE rather than against a C# constant, because the file
        // is now what decides.
        await Assert.That(denied.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheJudgementsNameTheUpstreamThisBuildResolved()
    {
        var judged = RepositoryVerdicts.Committed.JudgedAgainst;
        var faults = new List<string>();

        foreach (var package in judged)
        {
            var resolved = ResolvedVersions.FromPayloadLock(package.Key);

            if (!string.Equals(resolved, package.Value, StringComparison.Ordinal))
            {
                faults.Add(
                    $"tool-verdicts.json says the verdicts were judged against {package.Key} {package.Value}, and the committed payload lock resolves {resolved ?? "nothing"}. "
                    + "The verdicts describe a tool set that is no longer the one shipping. UPSTREAM-REVIEW.md governs what has to be re-read before the stamp is moved.");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, faults)).IsEmpty();

        // The same relation UpstreamSnapshotTests already asserts of the
        // snapshot's provenance, over the same two packages, so a bump moves
        // both stamps or neither.
        await Assert.That(judged.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(TheTwoPackages);
    }

    /// <summary>
    /// Every shape of verdicts file this build refuses to start on, and the
    /// positive control that says the loader accepts a good one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A missing or malformed file must be LOUD, and the reason is
    /// deny-by-default.</b> Any other configuration file could reasonably fall
    /// back to an empty default; this one cannot, because empty means <i>refuse
    /// every browser call</i>. A silent fallback would present as a server that
    /// starts, advertises a full surface, and then refuses everything with a
    /// sentence about verdicts that names no file — which is a morning lost to
    /// the wrong question.
    /// </para>
    /// <para>
    /// <b>Each shape asserts on the message, not only on the throw.</b> Every one
    /// of these names the file and what was wrong with it, because the reader is
    /// a person holding a broken install.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AVerdictsFileThisBuildCannotReadRefusesAndNamesTheFile()
    {
        var faults = new List<string>();

        foreach (var (what, doctor) in Malformed())
        {
            var document = RepositoryVerdicts.Document();
            doctor(document);

            try
            {
                _ = ToolVerdicts.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()), "the-doctored-file.json");
                faults.Add($"{what}: loaded, and it should not have");
            }
            catch (InvalidOperationException refused)
            {
                if (!refused.Message.Contains("the-doctored-file.json", StringComparison.Ordinal))
                {
                    faults.Add($"{what}: refused without naming the file -- {refused.Message}");
                }
            }
        }

        // Not JSON at all, which is its own path: the parser's own message is
        // carried rather than replaced, because "unexpected token at line 4" is
        // the thing a person fixes.
        var broken = Assert.Throws<InvalidOperationException>(
            () => ToolVerdicts.Parse("{ this is not json"u8, "the-broken-file.json"));

        await Assert.That(broken!.Message).Contains("the-broken-file.json");
        await Assert.That(broken.InnerException).IsNotNull();

        // A file that is not there at all, which is what a half-copied payload
        // looks like. FileNotFoundException rather than the above, so an
        // incomplete install and a corrupt one read differently.
        var absent = Path.Combine(ScratchRoot.Path, $"absent-{Guid.NewGuid():N}", ToolVerdicts.FileName);
        var missing = Assert.Throws<FileNotFoundException>(() => ToolVerdicts.Read(absent));

        await Assert.That(missing!.Message).Contains(absent);
        await Assert.That(missing.Message).Contains("Build-Payload.ps1");

        await Assert.That(string.Join(Environment.NewLine, faults)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL. The undoctored file loads, so a loader that
        // refused everything would pass every assertion above.
        await Assert.That(RepositoryVerdicts.Parse(RepositoryVerdicts.Document()).Upstream.Count).IsGreaterThan(40);
    }

    [Test]
    public async Task ThePayloadCarriesTheCommittedFileByteForByte()
    {
        // The copy step is a build target, and a build target that silently
        // stopped running would leave the product reading a file the suite never
        // tests -- so what ships and what is judged are compared rather than
        // assumed. Gated, because a clean clone has no payload and a scan of a
        // tree that is not there passes trivially.
        SuiteEnvironment.RequireRepositoryPayload();

        var shipped = RepositoryPayload.Layout.ToolVerdicts;

        await Assert.That(File.Exists(shipped)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(shipped))
            .IsEqualTo(await File.ReadAllTextAsync(RepositoryVerdicts.Path));
    }

    /// <summary>
    /// Which tools the snapshot and the verdicts file disagree about, in both
    /// directions.
    /// </summary>
    /// <remarks>
    /// <b>One implementation, read by the real comparison and by its controls,
    /// so the two cannot ask different questions.</b> A control that re-derived
    /// the comparison would eventually prove something about itself.
    /// </remarks>
    /// <param name="verdicts">The file under comparison.</param>
    /// <param name="snapshot">The tool names the golden snapshot carries.</param>
    /// <returns>One sentence per disagreement, in name order.</returns>
    private static List<string> Coverage(ToolVerdicts verdicts, IReadOnlyList<string> snapshot)
    {
        var judged = verdicts.Upstream.Select(row => row.Name).ToList();
        var disagreements = new List<string>();

        disagreements.AddRange(snapshot
            .Except(judged, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(tool =>
                $"'{tool}' is in upstream-snapshots/tools-list.json and has no row in {ToolVerdicts.FileName}. "
                + "A tool arrives unadjudicated when a Playwright bump adds one: nobody has decided whether BrowserAI forwards it, so it is denied at the door until somebody does. "
                + "Follow UPSTREAM-REVIEW.md, then add a row -- 'allow', or 'deny' with the reason a caller will read and the date. RELEASING.md item 4 is where the decision is recorded."));

        disagreements.AddRange(judged
            .Except(snapshot, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(tool =>
                $"'{tool}' has a row in {ToolVerdicts.FileName} and is in no upstream-snapshots/tools-list.json. "
                + "Upstream removed it, or it was never spelled the way upstream spells it: either way the row is a judgement about nothing and the capability it describes is gone. "
                + "Follow UPSTREAM-REVIEW.md and delete the row, recording what went and why, or correct the spelling."));

        return disagreements;
    }

    /// <summary>Every way a verdicts file can be wrong, and how to make it so.</summary>
    /// <returns>What each does, paired with the doctoring.</returns>
    private static IReadOnlyList<(string What, Action<JsonObject> Doctor)> Malformed() =>
    [
        ("a schema version this build does not know", file => file["schemaVersion"] = ToolVerdicts.SchemaVersion + 1),
        ("no schema version at all", file => file.Remove("schemaVersion")),
        ("no upstream object", file => file.Remove("upstream")),
        ("no authored object", file => file.Remove("authored")),
        ("no judgedAgainst object", file => file.Remove("judgedAgainst")),
        ("an empty upstream object", file => file["upstream"] = new JsonObject()),
        ("a row that is not an object", file => file["upstream"]![NeverATool] = "allow"),
        ("a row with no verdict", file => file["upstream"]![NeverATool] = new JsonObject()),
        ("a verdict word nobody defined", file => file["upstream"]![NeverATool] = new JsonObject { ["verdict"] = "maybe" }),
        (
            "a denial with no reason",
            file => file["upstream"]![NeverATool] = new JsonObject { ["verdict"] = "deny", ["since"] = "2026-08-26" }),
        (
            "a denial with no date",
            file => file["upstream"]![NeverATool] = new JsonObject { ["verdict"] = "deny", ["why"] = "because" }),
        (
            "an upstream tool marked as one BrowserAI answers",
            file => file["upstream"]![NeverATool] = new JsonObject { ["verdict"] = "answer" }),
        (
            "one of BrowserAI's own tools marked as forwarded",
            file => file["authored"]![SessionToolSurface.Init] = new JsonObject { ["verdict"] = "allow" }),
        (
            "a tool in the authored half that is not one of ours",
            file => file["authored"]![NeverATool] = new JsonObject { ["verdict"] = "answer" }),
        (
            "one name carrying two verdicts",
            file => file["upstream"]![SessionToolSurface.Init] = new JsonObject { ["verdict"] = "allow" }),
    ];

    private static IReadOnlyList<string> Snapshot() =>
        [.. UpstreamSurface.SnapshotDescriptions().Select(entry => entry.Name)];
}

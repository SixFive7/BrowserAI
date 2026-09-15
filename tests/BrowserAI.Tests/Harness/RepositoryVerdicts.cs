// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The committed <c>tool-verdicts.json</c>, read through the product's own
/// parser, and the machinery for handing a rig a doctored copy of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The TRACKED file at the repository root, not the payload's copy.</b> The
/// two are the same bytes — a build target copies one to the other, and
/// <c>ToolVerdictTests</c> asserts the copy landed — but the tracked one is
/// there on a clean clone with no payload assembled, so every arm that only
/// needs to know what this build judges runs without the capability gate.
/// </para>
/// <para>
/// <b>Read through <see cref="ToolVerdicts.Read"/> rather than parsed here.</b> A
/// second reader in the suite would eventually disagree with the product's, and
/// the disagreement would be reported as a product defect.
/// </para>
/// </remarks>
internal static class RepositoryVerdicts
{
    /// <summary>The committed file's path.</summary>
    public static string Path { get; } =
        System.IO.Path.Combine(RepositoryLayout.Root.FullName, ToolVerdicts.FileName);

    /// <summary>The committed file, parsed once.</summary>
    public static ToolVerdicts Committed { get; } = ToolVerdicts.Read(Path);

    /// <summary>
    /// Every tool this build ships a <c>deny</c> for, found rather than named,
    /// in the file's own order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Found, because the C# constant this replaced is exactly what the
    /// verdicts file exists to delete.</b> Until 2026-08-26 the suite spelled
    /// <c>SessionToolPolicy.AnnotateTool</c> in eight files, and every one of
    /// them was reading the product's own decision back out of the product — so
    /// a suite that agreed with a wrong constant could not say so. This reads
    /// the shipped file, which is what the product reads.
    /// </para>
    /// <para>
    /// ⚠️ <b>A LIST since 2026-09-15 (previously <c>TheOneDenial</c>, a
    /// <c>Single</c> that threw a type-initialiser failure on a second
    /// row).</b> There is a second row now — <c>browser_webmcp_call</c>, denied
    /// on the same liveness grounds as <c>browser_annotate</c> — so the shape
    /// that was protecting the suite from a silent re-point has to become the
    /// shape that scales with the file. <b>The protection is not dropped, it
    /// moves</b>: the arms that assert the <i>mechanism</i> — refused at the
    /// door, absent from <c>tools/list</c>, absent from the real binary's real
    /// answer — now run over <i>every</i> row rather than over one, so a third
    /// denial arriving is covered rather than ignored, and the counts those
    /// arms state are <see cref="Count"/> rather than <c>1</c>.
    /// </para>
    /// <para>
    /// <b>Empty is refused here rather than at the call site.</b> A build that
    /// withheld nothing would make every "the withheld tool is absent" arm
    /// below vacuously true, and a vacuous pass is the failure this whole file
    /// exists to prevent.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ToolVerdict> TheDenials { get; } =
        Committed.Upstream.Where(row => row.Kind is ToolVerdictKind.Deny).ToList() switch
        {
            [] => throw new InvalidOperationException(
                $"{ToolVerdicts.FileName} carries no 'deny' row at all, and every arm that asserts a withheld tool is "
                + "absent from the surface would pass by measuring nothing. If a denial really was withdrawn, the arms "
                + "that name one have to be withdrawn in the same change."),
            var rows => rows,
        };

    /// <summary>
    /// How many tools this build withholds — the addend every surface count in
    /// the suite is written against.
    /// </summary>
    public static int Count => TheDenials.Count;

    /// <summary>
    /// One denial, for the arms that have to drive a rig at a single tool rather
    /// than assert over the set.
    /// </summary>
    /// <remarks>
    /// <b>The oldest judgement, tie-broken by name, rather than the first row in
    /// the file.</b> Row order follows upstream's <c>tools/list</c> order, so
    /// "the first one" moves the day upstream reorders its own array — which is
    /// exactly the silent re-point the <c>Single</c> this replaced was guarding
    /// against. A date and a name are ours and do not move.
    /// </remarks>
    public static ToolVerdict ADenial { get; } =
        TheDenials.OrderBy(row => row.Since, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .First();

    /// <summary>The committed file's raw text, for an arm that doctors it.</summary>
    /// <returns>The bytes on disk, as text.</returns>
    public static string Text() => File.ReadAllText(Path);

    /// <summary>The committed file as a mutable node, for an arm that doctors it.</summary>
    /// <returns>A fresh parse; the caller owns it.</returns>
    public static JsonObject Document() => JsonNode.Parse(Text())!.AsObject();

    /// <summary>Parses a doctored copy the way the product parses the real one.</summary>
    /// <param name="document">The doctored file.</param>
    /// <param name="origin">What a failure message should call it.</param>
    /// <returns>The verdicts.</returns>
    public static ToolVerdicts Parse(JsonObject document, string origin = "a rig copy of tool-verdicts.json")
    {
        ArgumentNullException.ThrowIfNull(document);

        return ToolVerdicts.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()), origin);
    }

    /// <summary>
    /// The committed file with one more <c>deny</c> row, for the arms that need a
    /// denial the product does not ship.
    /// </summary>
    /// <remarks>
    /// <b>The suite cannot use a shipped denial for this and must not add a
    /// real one.</b> The advertised-tool counts this repository publishes are
    /// asserted against <see cref="Count"/>, so a shipped denial added to test a
    /// mechanism would move four documented numbers with it. A rig copy tests
    /// the mechanism and leaves the product's judgement alone. *(Was "cannot use
    /// <c>browser_annotate</c> … <c>withheld == 1</c>" until 2026-09-15, when
    /// the second real denial landed and the count stopped being a literal.)*
    /// </remarks>
    /// <param name="tool">The tool to deny.</param>
    /// <param name="why">The refusal a caller would read.</param>
    /// <param name="since">The ISO date to record.</param>
    /// <returns>The verdicts, with that one row changed.</returns>
    public static ToolVerdicts Denying(string tool, string why, string since = "2026-08-26")
    {
        var document = Document();

        document["upstream"]![tool] = new JsonObject
        {
            ["verdict"] = "deny",
            ["why"] = why,
            ["since"] = since,
        };

        return Parse(document, $"a rig copy of tool-verdicts.json denying '{tool}'");
    }

    /// <summary>
    /// The committed file with one row removed, for the arms about a tool
    /// nobody has judged.
    /// </summary>
    /// <param name="tool">The tool whose row goes.</param>
    /// <returns>The verdicts, with that row absent.</returns>
    public static ToolVerdicts Without(string tool)
    {
        var document = Document();

        _ = document["upstream"]!.AsObject().Remove(tool);

        return Parse(document, $"a rig copy of tool-verdicts.json with no row for '{tool}'");
    }
}

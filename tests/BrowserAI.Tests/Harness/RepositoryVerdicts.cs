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
    /// The one tool this build ships a <c>deny</c> for, found rather than named.
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
    /// <b><c>Single</c> rather than <c>First</c>, deliberately.</b> Four
    /// documents publish counts that rest on there being exactly one, and
    /// <c>ToolVerdictTests</c> asserts it — but that arm can only fail once,
    /// whereas a second denial arriving would silently re-point every arm below
    /// at whichever row happened to come first. A type-initialiser failure
    /// naming the problem is the better failure.
    /// </para>
    /// </remarks>
    public static ToolVerdict TheOneDenial { get; } =
        Committed.Upstream.Where(row => row.Kind is ToolVerdictKind.Deny).ToList() switch
        {
            [var only] => only,
            var many => throw new InvalidOperationException(
                $"{ToolVerdicts.FileName} carries {many.Count} 'deny' rows and the suite is written against exactly one "
                + $"({string.Join(", ", many.Select(row => row.Name))}). Every arm that says 'the withheld tool' has to say which one first."),
        };

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
    /// <b>The suite cannot use <c>browser_annotate</c> for this and must not
    /// add a second real one.</b> The advertised-tool counts this repository
    /// publishes are asserted against <c>withheld == 1</c>, so a second shipped
    /// denial would move four documented numbers to test one mechanism. A rig
    /// copy tests the mechanism and leaves the product's judgement alone.
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

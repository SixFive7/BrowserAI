// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What a containment arm says about the <c>job-launcher</c> probe's report when it fails: the
/// rows the launcher counted as escapees, and where the report is kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>95 d, decided 2026-10-12 by the maintainer, in his words verbatim: <i>"95 your
/// recommendation"</i>.</b> That day the Chromium arm read <c>escapees</c> 2 and said
/// <i>"Expected to be 0 but found 2"</i> and nothing more, and the report that named the two
/// processes was in the scratch directory the run removes, so which two they were is not
/// known. The count is asserted with its rows since, each with its pid, creation time, parent
/// and image path, and a failing arm copies the report out of its scratch first.
/// </para>
/// <para>
/// <b>The copy goes under the repository's own scratch, beside the summary a passing run
/// writes</b>: <c>.work\containment-reports\</c>, one file per failure, named for the arm and
/// the moment. The suite's reclaim pass takes what is under <c>.work\test-scratch</c> and
/// nothing here.
/// </para>
/// </remarks>
internal static class ContainmentReport
{
    /// <summary>Where a failing arm keeps the launcher's report.</summary>
    /// <param name="label">The arm's scratch label, which names the file.</param>
    /// <returns>A path no other arm or run writes.</returns>
    public static string KeptPathFor(string label) =>
        Path.Combine(
            RepositoryLayout.Root.FullName,
            ".work",
            "containment-reports",
            $"{label}-{DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.json");

    /// <summary>
    /// The rows the launcher counted as escapees, one line each, for the escapee count's
    /// failure message.
    /// </summary>
    /// <param name="walk">The report's <c>walk</c>.</param>
    /// <param name="kept">Where the report is kept if the arm fails.</param>
    /// <returns>The text: every counted row with its identity and image path, and the kept path.</returns>
    public static string EscapeesIn(JsonArray walk, string kept)
    {
        ArgumentNullException.ThrowIfNull(walk);

        var counted = walk
            .Where(row => (string?)row!["verdict"] is "Queried" && (bool?)row["inOurJob"] is false)
            .ToList();

        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"the launcher counted {counted.Count} walked process(es) outside its job:");

        foreach (var row in counted)
        {
            _ = text.AppendLine().Append(
                CultureInfo.InvariantCulture,
                $"  pid {(int)row!["pid"]!}, created {(long)row["createdFileTime"]!}, parent {(int)row["parentPid"]!}, depth {(int)row["depth"]!}, in any job {(bool?)row["inAnyJob"]}: {(string?)row["imagePath"] ?? "<no image path>"}");

            _ = text.AppendLine().Append("    ").Append(row.ToJsonString());
        }

        return text.AppendLine().Append(Kept(kept)).ToString();
    }

    /// <summary>The sentence a failure message ends with.</summary>
    /// <param name="kept">Where the report is kept if the arm fails.</param>
    /// <returns>The sentence.</returns>
    public static string Kept(string kept) =>
        $"The launcher's report is copied to '{kept}' when this arm fails.";

    /// <summary>Copies the launcher's report to where the failure message says it is kept.</summary>
    /// <remarks>
    /// Called from a failing arm's way out, so it never throws: a copy that fails leaves the
    /// arm's own failure to be reported, which is the one that matters.
    /// </remarks>
    /// <param name="report">The report in the arm's scratch directory.</param>
    /// <param name="kept">Where it goes.</param>
    /// <returns>A task that completes when the copy is written or could not be.</returns>
    public static async Task KeepAsync(string report, string kept)
    {
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            await File.WriteAllBytesAsync(kept, await File.ReadAllBytesAsync(report));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the arm's own failure is already on its way out.
        }
    }
}

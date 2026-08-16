// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Reading what a probe process wrote, without racing the write.
/// </summary>
/// <remarks>
/// <para>
/// The probe renames its report into place, so the file appears complete or not
/// at all. This waits for it and reports a timeout as a timeout — a test that
/// silently read an absent file would fail on the assertion instead, naming the
/// product for the harness's impatience.
/// </para>
/// <para>
/// ⚠️ <b>An atomic rename does not make the file openable, and that is measured
/// rather than anticipated.</b> Observed once on 2026-08-16 during build-order
/// step 12: <c>File.ReadAllTextAsync</c> on a report that had already been
/// renamed into place failed with <i>"the process cannot access the file …
/// because it is being used by another process"</i>, one run in a dozen. A
/// freshly-created file is briefly held by something outside this repository —
/// the same live condition <c>SessionLock</c>'s two-second move budget exists
/// for. The read is therefore <b>retried inside the patience budget</b>, and
/// opened <c>FileShare.ReadWrite | FileShare.Delete</c> so that neither a writer
/// nor a rename in flight can be refused by <i>this</i> process either. A
/// harness that fails intermittently is a red build wearing a disguise, and the
/// wrong name is on it.
/// </para>
/// </remarks>
internal static class ProbeReport
{
    /// <summary>Waits for a probe's report file and parses it.</summary>
    /// <param name="path">Where the probe was told to write.</param>
    /// <param name="patience">How long to wait.</param>
    /// <returns>The parsed report.</returns>
    /// <exception cref="TimeoutException">It never appeared, or never became readable.</exception>
    public static async Task<JsonNode> ReadAsync(string path, TimeSpan patience)
    {
        var clock = Stopwatch.StartNew();
        var lastFailure = "it never appeared";

        while (clock.Elapsed < patience)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                if (JsonNode.Parse(await reader.ReadToEndAsync()) is { } parsed)
                {
                    return parsed;
                }

                lastFailure = "it parsed as JSON null";
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // Absent, half-arrived, or briefly held by something outside
                // this repository. All three are conditions that pass.
                lastFailure = failure.Message;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"'{path}' was not readable within {patience}: {lastFailure}");
    }

    /// <summary>Waits for every report in a set.</summary>
    /// <param name="paths">Where each probe was told to write.</param>
    /// <param name="patience">How long to wait for all of them.</param>
    /// <returns>The parsed reports, in the order given.</returns>
    public static async Task<IReadOnlyList<JsonNode>> ReadAllAsync(IEnumerable<string> paths, TimeSpan patience)
    {
        var reports = new List<JsonNode>();

        foreach (var path in paths)
        {
            reports.Add(await ReadAsync(path, patience));
        }

        return reports;
    }
}

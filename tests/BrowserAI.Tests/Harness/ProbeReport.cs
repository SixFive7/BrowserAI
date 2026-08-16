// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Reading what a probe process wrote, without racing the write.
/// </summary>
/// <remarks>
/// The probe renames its report into place, so the file appears complete or not
/// at all. This waits for it and reports a timeout as a timeout — a test that
/// silently read an absent file would fail on the assertion instead, naming the
/// product for the harness's impatience.
/// </remarks>
internal static class ProbeReport
{
    /// <summary>Waits for a probe's report file and parses it.</summary>
    /// <param name="path">Where the probe was told to write.</param>
    /// <param name="patience">How long to wait.</param>
    /// <returns>The parsed report.</returns>
    /// <exception cref="TimeoutException">It never appeared.</exception>
    public static async Task<JsonNode> ReadAsync(string path, TimeSpan patience)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < patience)
        {
            if (File.Exists(path))
            {
                var text = await File.ReadAllTextAsync(path);

                if (JsonNode.Parse(text) is { } parsed)
                {
                    return parsed;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"'{path}' was not written within {patience}.");
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

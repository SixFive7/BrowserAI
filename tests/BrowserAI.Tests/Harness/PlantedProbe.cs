// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A copy of <c>BrowserAI.TestProbe.exe</c> at an image path the test chooses,
/// so that the sweep's <b>own</b> detection rule can be pointed at it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sweep matches on full image path and nothing else</b>, so a test that
/// wants a candidate has to produce a process whose image path is a string the
/// test declares. <see cref="PlantedProcess"/> already plants <c>cmd.exe</c> for
/// that, and <c>cmd.exe</c> cannot publish a <c>Chrome_MessageWindow</c> — which
/// is the other half of every question here. This plants the probe instead,
/// which can do both.
/// </para>
/// <para>
/// <b>The whole output directory is copied, and that is forced.</b> The probe is
/// a framework-dependent apphost: a lone <c>.exe</c> in a strange directory
/// cannot find its <c>.dll</c>, its <c>runtimeconfig.json</c> or its dependency
/// closure and dies immediately, silently, before anything looks — the exact
/// failure <see cref="PlantedProcess"/> records having met twice. Four megabytes
/// and about thirty files, into the run's own scratch tree.
/// </para>
/// <para>
/// <b>One plant is shared by every test that needs one, and those tests are
/// serialised.</b> A per-test copy would give perfect isolation and cost a copy
/// per test; serialising them costs nothing, because a sweep test is the only
/// thing that ever runs this image and two of them never run at once.
/// </para>
/// </remarks>
internal static class PlantedProbe
{
    private static readonly Lazy<string> Planted = new(Plant, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The planted executable, copied on first use and reused afterwards.
    /// </summary>
    /// <remarks>
    /// <b>Nothing else on the machine runs this path</b>, which is what makes it
    /// safe to declare it as "a browser BrowserAI provisioned" in a test that
    /// then terminates what it finds.
    /// </remarks>
    public static string ExecutablePath => Planted.Value;

    /// <summary>
    /// Starts a planted probe that publishes one message-only window and stays
    /// alive.
    /// </summary>
    /// <param name="scope">The job that owns it, so a failed assertion cannot leak it.</param>
    /// <param name="workingDirectory">Where the probe runs and writes its report.</param>
    /// <param name="className">The window class to register.</param>
    /// <param name="title">The window title, which is what attribution reads.</param>
    /// <param name="suppress">Whether its WndProc should lie about <c>WM_GETTEXT</c>.</param>
    /// <returns>The probe's own report: its pid, its window handle and the same-process reads.</returns>
    public static Task<JsonNode> PublishWindowAsync(
        JobObjectScope scope,
        string workingDirectory,
        string className,
        string title,
        bool suppress = false) =>
        PublishWindowAsync(scope, ExecutablePath, workingDirectory, className, title, suppress);

    /// <summary>
    /// Starts a probe from an explicit image path that publishes one message-only
    /// window and stays alive.
    /// </summary>
    /// <param name="scope">The job that owns it.</param>
    /// <param name="executablePath">Which probe copy to run.</param>
    /// <param name="workingDirectory">Where the probe runs and writes its report.</param>
    /// <param name="className">The window class to register.</param>
    /// <param name="title">The window title.</param>
    /// <param name="suppress">Whether its WndProc should lie about <c>WM_GETTEXT</c>.</param>
    /// <returns>The probe's own report.</returns>
    public static async Task<JsonNode> PublishWindowAsync(
        JobObjectScope scope,
        string executablePath,
        string workingDirectory,
        string className,
        string title,
        bool suppress = false)
    {
        ArgumentNullException.ThrowIfNull(scope);

        _ = Directory.CreateDirectory(workingDirectory);
        var report = Path.Combine(workingDirectory, $"window-{Guid.NewGuid():N}.json");

        _ = scope.Launch(
            executablePath,
            workingDirectory,
            "window",
            className,
            title,
            report,
            suppress ? "suppress" : "answer");

        var published = await ProbeReport.ReadAsync(report, TestDefaults.ProcessHang).ConfigureAwait(false);

        return (string?)published["error"] is { } failure
            ? throw new InvalidOperationException($"The window probe could not publish '{title}': {failure}")
            : published;
    }

    /// <summary>
    /// Waits until the product's own detection can see a pid running one of the
    /// declared images.
    /// </summary>
    /// <remarks>
    /// <b>Not optional.</b> <c>CreateProcessW</c> returns as soon as the process
    /// object exists, and a scan taken in that instant can legitimately miss it —
    /// which would have a test asserting that a sweep left something alone while
    /// the sweep never saw it at all.
    /// </remarks>
    /// <param name="images">The image paths the scan is asked about.</param>
    /// <param name="processId">The pid that must appear.</param>
    /// <returns>A task that completes when it does.</returns>
    /// <exception cref="InvalidOperationException">It never appeared.</exception>
    public static async Task WaitUntilDetectableAsync(IReadOnlyCollection<string> images, int processId)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (DateTime.UtcNow < deadline)
        {
            using (var scan = BrowserProcesses.ScanFor(images))
            {
                if (scan.Candidates.Any(candidate => candidate.ProcessId == processId))
                {
                    return;
                }
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Process {processId} never became visible to the product's own detection under {string.Join(", ", images)}. "
            + "Without it, a test about what the sweep does would be asserting against an empty answer.");
    }

    private static string Plant()
    {
        var source = ProbeOutputDirectory();
        var target = Path.Combine(ScratchRoot.Path, "planted-probe");

        _ = Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        var planted = Path.Combine(target, "BrowserAI.TestProbe.exe");

        return File.Exists(planted)
            ? planted
            : throw new InvalidOperationException($"'{source}' was copied to '{target}' and produced no probe executable.");
    }

    /// <summary>
    /// The probe project's own build output, composed from the test host's.
    /// </summary>
    /// <remarks>
    /// The test host's own directory carries the probe <i>and</i> the whole test
    /// framework, so copying that would be tens of megabytes of things the probe
    /// never loads. The two projects build into the same configuration and target
    /// framework, so the probe's directory is the host's with one name changed.
    /// </remarks>
    private static string ProbeOutputDirectory()
    {
        var host = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = host.Name;
        var configuration = host.Parent?.Name
            ?? throw new InvalidOperationException($"'{host.FullName}' has no configuration directory above it.");

        var probe = Path.Combine(
            RepositoryLayout.Root.FullName,
            "tests",
            "BrowserAI.TestProbe",
            "bin",
            configuration,
            framework);

        return Directory.Exists(probe)
            ? probe
            : throw new InvalidOperationException($"'{probe}' does not exist, so the probe cannot be planted at an image path of the test's choosing.");
    }
}

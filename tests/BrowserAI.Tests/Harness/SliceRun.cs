// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One live process seen from outside: its pid, the image it is running, and the
/// command line it was started with.
/// </summary>
/// <param name="ProcessId">The pid.</param>
/// <param name="CreatedFileTime">Its creation time, which together with the pid is its identity.</param>
/// <param name="ImagePath">Its full image path, or <see langword="null"/> if it could not be read.</param>
/// <param name="CommandLine">Its command line, or <see langword="null"/> if it could not be read.</param>
internal sealed record ObservedProcess(int ProcessId, long CreatedFileTime, string? ImagePath, string? CommandLine);

/// <summary>
/// Everything one run of the vertical slice produced, captured once and asserted
/// on by several tests.
/// </summary>
/// <remarks>
/// <b>One browser launch, not five.</b> Every fact below comes from the same
/// process tree, which is both cheaper and stronger than five independent runs:
/// the negotiated version, the tool list, the navigation result, the resolved
/// browser binary and the sandbox flag are then known to be true <i>of the same
/// launch</i>, rather than of five launches that might have differed.
/// </remarks>
/// <param name="InitializeResult">The <c>initialize</c> result the raw client received.</param>
/// <param name="ToolNames">The names <c>tools/list</c> returned, in order.</param>
/// <param name="NavigateEnvelope">The whole <c>tools/call</c> response envelope.</param>
/// <param name="NavigateText">Every text content block of that result, joined.</param>
/// <param name="Processes">Every member of the job at the moment the browser was up.</param>
/// <param name="BrowserAiProcessId">The published binary's own pid.</param>
/// <param name="Survivors">Processes still alive after the published binary was terminated from outside.</param>
/// <param name="StandardError">Everything BrowserAI wrote to stderr.</param>
internal sealed record SliceRun(
    JsonObject InitializeResult,
    IReadOnlyList<string> ToolNames,
    JsonObject NavigateEnvelope,
    string NavigateText,
    IReadOnlyList<ObservedProcess> Processes,
    int BrowserAiProcessId,
    IReadOnlyList<ObservedProcess> Survivors,
    string StandardError)
{
    private static readonly Lazy<Task<SliceRun>> Shared = new(CaptureAsync);

    /// <summary>The URL every browser assertion in this suite navigates to.</summary>
    /// <remarks>
    /// A <c>data:</c> URL, never <c>about:blank</c>: <c>about:blank</c> succeeds
    /// too trivially and its snapshot is empty, so a proxy that returned an
    /// empty result would pass.
    /// </remarks>
    public const string TargetUrl = "data:text/html,<h1>ok</h1>";

    /// <summary>The revision the raw client offers on the normal path.</summary>
    public const string OfferedProtocolVersion = "2025-11-25";

    /// <summary>The one shared capture, run at most once per test process.</summary>
    /// <returns>The captured run.</returns>
    public static Task<SliceRun> SharedAsync() => Shared.Value;

    /// <summary>The processes in the run whose image is the Chromium BrowserAI provisioned.</summary>
    /// <param name="browsersDirectory">The browsers root that Chromium must have come from.</param>
    /// <returns>Every Chromium process, browser and children alike.</returns>
    public IReadOnlyList<ObservedProcess> ChromiumProcesses(string browsersDirectory) =>
    [
        .. Processes.Where(process => process.ImagePath is { } path
            && path.StartsWith(browsersDirectory, StringComparison.OrdinalIgnoreCase)),
    ];

    /// <summary>
    /// The browser process itself, as opposed to its renderers and utilities.
    /// </summary>
    /// <remarks>
    /// Identified by <c>--remote-debugging-pipe</c> without a <c>--type=</c>
    /// switch, which is what Playwright starts and what every child of it
    /// inherits a <c>--type</c> from. Never by image name: every one of these
    /// processes runs the same image.
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root that Chromium must have come from.</param>
    /// <returns>The browser process.</returns>
    public ObservedProcess BrowserProcess(string browsersDirectory) =>
        ChromiumProcesses(browsersDirectory).SingleOrDefault(process =>
            process.CommandLine is { } line
            && line.Contains("--remote-debugging-pipe", StringComparison.Ordinal)
            && !line.Contains("--type=", StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            "No Chromium process in the job looks like the browser process. Members: "
            + string.Join("; ", Processes.Select(process => $"{process.ProcessId} {process.ImagePath}")));

    private static async Task<SliceRun> CaptureAsync()
    {
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("slice");

        // The job this client owns is also the containment net: an assertion
        // that throws anywhere below closes it, and KILL_ON_JOB_CLOSE takes the
        // browser with it.
        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        var browserAi = client.ProcessId;
        var browserAiCreated = ProcessIdentity.CreationTimeOf(browserAi);

        var initialize = await client.InitializeAsync(OfferedProtocolVersion).ConfigureAwait(false);

        var tools = await client.RoundTripAsync("tools/list", new JsonObject()).ConfigureAwait(false);
        var names = tools["tools"]!.AsArray().Select(tool => (string)tool!["name"]!).ToList();

        var navigate = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = TargetUrl },
        }).ConfigureAwait(false);

        var text = string.Join(
            "\n",
            (navigate["result"]?["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

        // Read while the browser is up. Everything after this line is teardown.
        var processes = Observe(client.JobProcessIds());

        // The event the containment contract is about: BrowserAI is killed from
        // outside and runs no code afterwards, so only the kernel closing its
        // last job handle can clean up.
        ProcessIdentity.Terminate(browserAi, browserAiCreated);

        var survivors = await WaitForNoneAliveAsync(
            [.. processes.Where(process => process.ProcessId != browserAi)],
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        return new SliceRun(
            initialize,
            names,
            navigate,
            text,
            processes,
            browserAi,
            survivors,
            client.StandardErrorSoFar());
    }

    private static List<ObservedProcess> Observe(IEnumerable<int> processIds)
    {
        var observed = new List<ObservedProcess>();

        foreach (var processId in processIds)
        {
            long created;

            try
            {
                created = ProcessIdentity.CreationTimeOf(processId);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Exited between the job reporting it and this call. Its pid is
                // then meaningless and recording it would make the survivor
                // check act on a number that may be reused.
                continue;
            }

            observed.Add(new ObservedProcess(
                processId,
                created,
                ProcessCommandLine.ImagePathOf(processId),
                ProcessCommandLine.Of(processId)));
        }

        return observed;
    }

    private static async Task<IReadOnlyList<ObservedProcess>> WaitForNoneAliveAsync(
        IReadOnlyList<ObservedProcess> recorded,
        TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (true)
        {
            var alive = recorded
                .Where(process => ProcessIdentity.IsAlive(process.ProcessId, process.CreatedFileTime))
                .ToList();

            if (alive.Count is 0 || deadline.Elapsed > patience)
            {
                return alive;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The repository's assembled payload, for the arms that drive a child directly
/// rather than through the proxy.
/// </summary>
internal static class RepositoryPayload
{
    /// <summary>The assembled payload tree at the repository root.</summary>
    public static PayloadLayout Layout { get; } =
        new(Path.Combine(RepositoryLayout.Root.FullName, "payload"));

    /// <summary>Whether <c>build/Build-Payload.ps1</c> has been run.</summary>
    public static bool IsPresent =>
        File.Exists(Path.Combine(Layout.Root, "payload.json"))
        && File.Exists(Layout.NodeExecutable)
        && File.Exists(Layout.PlaywrightMcpCli);

    /// <summary>Whether the payload directory is absent as a whole, as on a clean clone.</summary>
    public static bool IsAbsentAsAWhole => !Directory.Exists(Layout.Root);
}

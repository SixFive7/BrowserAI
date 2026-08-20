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
/// <param name="ToolList">The whole <c>tools</c> array, for the assertions about injected schema members.</param>
/// <param name="NavigateEnvelope">The whole <c>tools/call</c> response envelope.</param>
/// <param name="NavigateText">Every text content block of that result, joined.</param>
/// <param name="ScreenshotEnvelope">
/// The whole <c>tools/call</c> envelope for a <c>browser_take_screenshot</c>
/// that named no file — which is upstream's own condition for answering with an
/// inline image, and the case BrowserAI's routing used to swallow.
/// </param>
/// <param name="ScreenshotFile">
/// The absolute path BrowserAI routed that screenshot to, read out of its own
/// note rather than reconstructed.
/// </param>
/// <param name="ScreenshotBytes">
/// What is on disk at that path, captured <b>before</b> the scratch tree is
/// removed, so a test can compare the answer against the file rather than
/// against itself.
/// </param>
/// <param name="Processes">Every member of the job at the moment the browser was up.</param>
/// <param name="BrowserAiProcessId">The published binary's own pid.</param>
/// <param name="Survivors">Processes still alive after the published binary was terminated from outside.</param>
/// <param name="StandardError">
/// Everything BrowserAI wrote to stderr — <b>as much of it as survived the kill</b>.
/// ⚠️ Do not assert that a record was written on this: stderr goes through
/// <c>AddConsole</c>, whose background processor loses whatever it still holds
/// when the process is terminated, and this run terminates it on purpose. Use
/// <paramref name="ProcessLog"/>, which is durable per record by construction.
/// </param>
/// <param name="ProcessLog">
/// Every record <b>this run's BrowserAI</b> wrote to the shared process log,
/// which <c>RollingFileWriter</c> writes unbuffered — so a record that was
/// logged is on disk whatever happens to the process afterwards.
/// </param>
/// <param name="SessionDirectory">The session this run's browser belongs to.</param>
internal sealed record SliceRun(
    JsonObject InitializeResult,
    IReadOnlyList<string> ToolNames,
    JsonArray ToolList,
    JsonObject NavigateEnvelope,
    string NavigateText,
    JsonObject ScreenshotEnvelope,
    string ScreenshotFile,
    byte[] ScreenshotBytes,
    IReadOnlyList<ObservedProcess> Processes,
    int BrowserAiProcessId,
    IReadOnlyList<ObservedProcess> Survivors,
    string StandardError,
    string ProcessLog,
    string SessionDirectory)
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
        var toolList = tools["tools"]!.AsArray();
        var names = toolList.Select(tool => (string)tool!["name"]!).ToList();

        // ⚠️ A session, because step 13 made `session` mandatory. Before it, a
        // call naming none went to the run's own child -- which is a session
        // nobody chose the mode of, and therefore a way round every enforcement
        // decision. The browser this slice contains is now this session's.
        var session = Path.Combine(scratch.Path, "slice-session");

        _ = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the vertical slice's own session",
            },
        }).ConfigureAwait(false);

        var navigate = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = TargetUrl, ["session"] = session },
        }).ConfigureAwait(false);

        var text = string.Join(
            "\n",
            (navigate["result"]?["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

        // ⚠️ NO `filename` argument, deliberately. That is upstream's own
        // condition for putting the image in the answer as well as on disk --
        // `if (!params.filename) await response.registerImageResult(...)` -- and
        // it is the case BrowserAI's routing swallowed for the life of the
        // feature, because the routing always supplied one. The answer is
        // captured whole so the test can assert on the block rather than on a
        // summary of it.
        var screenshot = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_take_screenshot",
            ["arguments"] = new JsonObject { ["session"] = session },
        }).ConfigureAwait(false);

        var screenshotFile = ArtifactPathIn(screenshot);

        // Read here rather than in the test: `scratch` is removed when this
        // method returns, so a test that opened the path afterwards would be
        // asserting against a file that had been deleted.
        var screenshotBytes = screenshotFile.Length is not 0 && File.Exists(screenshotFile)
            ? await File.ReadAllBytesAsync(screenshotFile).ConfigureAwait(false)
            : [];

        // Read while the browser is up. Everything after this line is teardown.
        var processes = Observe(client.JobProcessIds());

        // The event the containment contract is about: BrowserAI is killed from
        // outside and runs no code afterwards, so only the kernel closing its
        // last job handle can clean up.
        ProcessIdentity.Terminate(browserAi, browserAiCreated);

        var survivors = await WaitForNoneAliveAsync(
            [.. processes.Where(process => process.ProcessId != browserAi)],
            TestDefaults.ProcessHang).ConfigureAwait(false);

        // ⚠️ DRAINED, never `StandardErrorSoFar`, and this is read AFTER the
        // waits above rather than before them. Everything that could hold the
        // write end of that pipe — BrowserAI, and the node and browser processes
        // that inherited it — is gone by this line, so end-of-file is guaranteed
        // and waiting for it is an event rather than a duration.
        //
        // Taking the snapshot instead is what put CI red on 2026-08-18: the
        // pump is a pool work item, the runner had four cores and 431 tests on
        // them, and the capture stopped mid-run. The assertion that failed named
        // the product for the harness's own starvation.
        var standardError = await client.DrainedStandardErrorAsync().ConfigureAwait(false);

        return new SliceRun(
            initialize,
            names,
            toolList,
            navigate,
            text,
            screenshot,
            screenshotFile,
            screenshotBytes,
            processes,
            browserAi,
            survivors,
            standardError,
            ProcessLogRecords.ForPid(browserAi),
            session);
    }

    /// <summary>
    /// The absolute path BrowserAI's own note names, out of a <c>tools/call</c>
    /// envelope.
    /// </summary>
    /// <remarks>
    /// <b>Read from the answer rather than rebuilt from the layout.</b> The
    /// generated name depends on the last URL the session navigated to and on a
    /// per-stem counter, so a test that composed the path would be asserting its
    /// own arithmetic; and the claim under test is that the path in the note is
    /// the path the file is at, which cannot be checked by producing both ends.
    /// </remarks>
    /// <param name="envelope">The whole response envelope.</param>
    /// <returns>The path, or an empty string when the note names none.</returns>
    private static string ArtifactPathIn(JsonObject envelope)
    {
        const string Marker = "  file: ";

        foreach (var block in envelope["result"]?["content"]?.AsArray() ?? [])
        {
            if ((string?)block?["type"] is not "text" || (string?)block["text"] is not { } content)
            {
                continue;
            }

            foreach (var line in content.Split('\n'))
            {
                if (line.StartsWith(Marker, StringComparison.Ordinal))
                {
                    return line[Marker.Length..].Trim();
                }
            }
        }

        return string.Empty;
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

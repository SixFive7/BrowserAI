// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A Chromium session's browser starts without Chromium's own stack sampling profiler,
/// whose stack walk crashed the browser.
/// </summary>
/// <remarks>
/// <para>
/// <b>129 a, decided 2026-10-13 by the maintainer, in his words verbatim:</b> <i>"129 a -
/// but also d and add monitoring d to the long term todo. If it gets fixed we reverse
/// a."</i> The release run's red of the real Chromium arm of <c>ScreenshotLimitTests</c>
/// at <c>44368995</c> was the browser process ending with 0xC0000005 inside Windows'
/// unwinder, called by that profiler on a worker thread
/// (<see href="../../kb/playwright/configuration.md#chrome-for-testing-samples-its-own-stacks-and-the-sampler-can-crash-the-browser----measured-2026-10-13">kb</see>),
/// and <see cref="BrowserConfiguration.StackProfilerSwitch"/> keeps the profiler from
/// starting.
/// </para>
/// <para>
/// <b>The effect and not the string</b>: the arm reads the names of the threads of the
/// browser's main process, the one <c>BrowserExitWatch</c> holds, found the same way, as
/// the earliest process of the session child's job that runs the provisioned
/// <c>chrome.exe</c>. Chromium starts the profiler's thread, <c>StackSamplingProfiler</c>,
/// as soon as its thread pool exists, so it is up before the first page loads. A Chromium
/// roll that dropped or renamed the switch would start the thread again with no error,
/// and this arm is what says so (re-verification row 220). <c>CrBrowserMain</c>, the
/// browser's main thread, is the positive control: a reading that found no thread at all
/// would otherwise pass.
/// </para>
/// <para>
/// <b>Planted red against the tree before the switch</b>, on 2026-10-13: the thread was
/// there.
/// </para>
/// </remarks>
internal sealed class StackProfilerTests
{
    /// <summary>The name Chromium gives the stack sampling profiler's thread.</summary>
    private const string ProfilerThread = "StackSamplingProfiler";

    /// <summary>The name Chromium gives the browser process's main thread.</summary>
    private const string MainThread = "CrBrowserMain";

    /// <summary>
    /// Against a <b>real</b> Chromium: the session's browser runs its main thread and no
    /// stack sampling profiler.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AgainstARealChromiumASessionsBrowserStartsNoStackSamplingProfiler()
    {
        await SuiteEnvironment.RequireProvisionedChromiumAsync();

        await using var sessions = await RigSessionEnvironment.CreateAsync(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "real-profiler");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose browser's threads the suite reads",
            ["browser"] = ProvisionedBrowsers.Chromium,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        var navigated = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite starting the session's browser",
            ["url"] = "data:text/html,<p>threads</p>",
        });

        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true).Because(TextOf(navigated));

        var child = sessions.RealSessionChildren.Single();
        var executable = Path.Combine(BrowserAiPaths.BrowsersDirectory, $"chromium-{BrowserAiPaths.ChromiumRevision}", ProvisionedBrowsers.ExecutableWithin(ProvisionedBrowsers.Chromium)!);

        using var browser = BrowserProcesses.HoldTheEarliest(child.JobProcessIds(), executable)
            ?? throw new InvalidOperationException($"No process in the session's job runs '{executable}'.");

        var threads = ThreadDescriptions.Of(browser.ProcessId);
        var named = string.Join(", ", threads.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        await Assert.That(threads).Contains(MainThread).Because($"the browser's threads read as: {named}");
        await Assert.That(threads).DoesNotContain(ProfilerThread).Because($"the browser's threads read as: {named}");
    }

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}

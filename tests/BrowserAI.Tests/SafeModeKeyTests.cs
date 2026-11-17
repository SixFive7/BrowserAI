// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A Firefox session's browser starts with its safe-mode key switched off, whoever switches it
/// off.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 197 b, 2026-11-17, in his words verbatim: <i>"197 b"</i></b>, which
/// took <c>MOZ_DISABLE_SAFE_MODE_KEY</c> out of <c>ChildEnvironment.Forced</c> and asked for
/// the effect to be read the way <c>StackProfilerTests</c> reads the profiler's. On Windows a
/// Firefox browser process that starts while Shift is held enters safe mode unless the
/// variable is in its environment, and a safe-mode Firefox never answers Playwright's launch
/// (<see href="../../kb/playwright/provisioning-and-timings.md#a-headless-firefox-that-starts-while-shift-is-held-never-finishes-launching----measured-2026-09-25">kb</see>).
/// <c>playwright-core</c> 1.66.0-alpha-2026-11-09 sets it itself in both Firefox launchers'
/// <c>amendEnvironment</c> (microsoft/playwright#43178), and 1.64 did not, so BrowserAI's copy,
/// forced from Q312 b of 2026-10-03, went.
/// </para>
/// <para>
/// <b>The effect and not the string</b>: the arm reads the environment block of the
/// session's own Firefox browser process, the earliest process of the session child's job
/// that runs the provisioned <c>firefox.exe</c>, through <see cref="ProcessEnvironment"/>.
/// <c>PLAYWRIGHT_BROWSERS_PATH</c>, which BrowserAI hands every child, is the positive
/// control: a reading that came back with nothing would otherwise pass the one assertion and
/// fail on the other for the wrong reason.
/// </para>
/// <para>
/// <b>Planted red the way 197 b asked</b>: with BrowserAI's copy gone, the payload of
/// <c>@playwright/mcp</c> 0.0.83, whose Playwright does not set the variable, must turn this
/// arm red, and the adopted payload green. <c>SafeModeWatch</c> stays beside it, because the
/// variable covers the key and nothing else: a Firefox the suite starts in safe mode by any
/// other route still fails the run.
/// </para>
/// </remarks>
internal sealed class SafeModeKeyTests
{
    /// <summary>The entry the arm looks for.</summary>
    private const string KeySwitchedOff = "MOZ_DISABLE_SAFE_MODE_KEY=1";

    /// <summary>The entry BrowserAI hands every child, which says the reading worked.</summary>
    private const string BrowsersPathPrefix = "PLAYWRIGHT_BROWSERS_PATH=";

    [Test]
    public async Task AgainstARealFirefoxASessionsBrowserStartsWithItsSafeModeKeySwitchedOff()
    {
        await SuiteEnvironment.RequireProvisionedFirefoxAsync();

        await using var sessions = await RigSessionEnvironment.CreateAsync(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "real-safe-mode-key");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose Firefox's environment the suite reads",
            ["browser"] = ProvisionedBrowsers.Firefox,
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
            ["url"] = "data:text/html,<p>environment</p>",
        });

        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true).Because(TextOf(navigated));

        var child = sessions.RealSessionChildren.Single();

        using var browser = BrowserProcesses.HoldTheEarliest(child.JobProcessIds(), BrowserAiPaths.FirefoxExecutable)
            ?? throw new InvalidOperationException($"No process in the session's job runs '{BrowserAiPaths.FirefoxExecutable}'.");

        var environment = ProcessEnvironment.Of(browser.ProcessId);
        var read = string.Join(", ", environment.Select(entry => entry.Split('=', 2)[0]).Order(StringComparer.OrdinalIgnoreCase));

        await Assert.That(environment.Any(entry => entry.StartsWith(BrowsersPathPrefix, StringComparison.OrdinalIgnoreCase))).IsTrue().Because($"the browser's environment read as: {read}");
        await Assert.That(environment).Contains(KeySwitchedOff).Because($"the browser's environment read as: {read}");
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

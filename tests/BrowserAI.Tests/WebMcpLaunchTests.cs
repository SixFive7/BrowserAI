// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A Chromium session's browser runs WebMCP, and every feature Playwright's own
/// <c>--enable-features</c> names is still on beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> <c>playwright-core</c> 1.66.0-alpha-2026-11-09, which
/// <c>@playwright/mcp</c> 0.0.85 pins, lists and calls a page's tools only for a browser whose
/// launch arguments turn WebMCP on, and answers every other one with an empty list, so
/// <c>PageToolTests</c>' real-page arms went red against the adopted payload until
/// <see cref="BrowserConfiguration.WebMcpFeatureSwitch"/> joined
/// <see cref="BrowserConfiguration.ChromiumArguments"/>. Playwright passes an
/// <c>--enable-features</c> of its own, and Chromium 158 keeps the last one on a command line
/// and drops the rest, measured 2026-11-17 through <c>chrome://version</c>, so BrowserAI's
/// switch carries Playwright's features beside WebMCP. That is a copy of upstream's list,
/// and these two arms are what notice when it goes stale.
/// </para>
/// <para>
/// <b>The payload arm reads the list and the browser arm reads the effect.</b> The first
/// reads <c>enabledFeatures</c> out of the assembled payload's <c>coreBundle.js</c> and fails
/// on any feature it names that BrowserAI's switch does not, with a doctored bundle as its
/// control in both directions. The second asks a real session's browser, through
/// <c>browser_run_code_unsafe</c>, for its command line (<c>Browser.getBrowserCommandLine</c>,
/// which Chromium answers because the launch carries <c>--enable-automation</c>) and for the
/// features it runs (<c>chrome://version/?show-variations-cmd</c>), and fails on a feature
/// named on the command line and not running, and on WebMCP missing.
/// <c>SystemInfo.getFeatureState</c> was tried first and answers <i>"Unknown feature"</i> for
/// all three.
/// </para>
/// </remarks>
internal sealed partial class WebMcpLaunchTests
{
    /// <summary>The feature the doctored bundle adds, which the payload arm must report.</summary>
    private static readonly string[] AddedLater = ["SomeFeaturePlaywrightAddedLater"];

    /// <summary>Features BrowserAI's switch carries, which the payload arm must not report.</summary>
    private static readonly string[] AlreadyCarried = ["WebMCP", "CDPScreenshotNewSurface"];

    /// <summary>What the browser arm hands the session's child to run.</summary>
    /// <remarks>
    /// The answer is one plain line with no quote in it, because the child renders a returned
    /// string inside its own quotes and the arm reads the line between its two markers.
    /// </remarks>
    private const string ReadFeatures =
        "async (page) => { const s = await page.context().browser().newBrowserCDPSession(); "
        + "const { arguments: args } = await s.send('Browser.getBrowserCommandLine'); "
        + "const named = [...new Set(args.filter((a) => a.startsWith('--enable-features=')).flatMap((a) => a.slice('--enable-features='.length).split(',')).filter(Boolean))]; "
        + "const version = await page.context().newPage(); await version.goto('chrome://version/?show-variations-cmd'); "
        + "const text = await version.evaluate(() => document.getElementById('variations-cmd').textContent); await version.close(); "
        + "const m = text.match(/--enable-features=\"?([^\" ]*)/); const on = m ? m[1].split(',').map((f) => f.split('<')[0]) : []; "
        + "const off = named.filter((f) => !on.includes(f)); "
        + "return 'FEATURES named=' + named.join(',') + ' running=' + on.join(',') + ' off=' + off.join(',') + ' END'; }";

    [Test]
    public async Task EveryFeaturePlaywrightsOwnSwitchEnablesIsInBrowserAisWebMcpSwitch()
    {
        await SuiteEnvironment.RequireRepositoryPayloadAsync();

        var bundle = await File.ReadAllTextAsync(Path.Combine(
            RepositoryPayload.Layout.Root,
            "mcp",
            "node_modules",
            "playwright-core",
            "lib",
            "coreBundle.js"));

        var playwrights = PlaywrightsEnabledFeatures(bundle);

        await Assert.That(playwrights.Count).IsGreaterThan(0).Because("the payload's chromiumSwitches named no --enable-features list, so this arm read nothing");
        await Assert.That(string.Join(", ", MissingFromBrowserAisSwitch(playwrights))).IsEmpty();

        // Both directions off a doctored bundle: a feature Playwright adds is caught, and a
        // list BrowserAI already carries is not.
        var doctored = bundle.Replace("enabledFeatures = () => [", "enabledFeatures = () => [\n      \"SomeFeaturePlaywrightAddedLater\",", StringComparison.Ordinal);

        await Assert.That(MissingFromBrowserAisSwitch(PlaywrightsEnabledFeatures(doctored))).IsEquivalentTo(AddedLater);
        await Assert.That(MissingFromBrowserAisSwitch(AlreadyCarried)).IsEmpty();
    }

    [Test]
    public async Task AgainstARealChromiumASessionsBrowserRunsWebMcpBesidePlaywrightsOwnFeatures()
    {
        await SuiteEnvironment.RequireProvisionedChromiumAsync();

        await using var sessions = await RigSessionEnvironment.CreateAsync(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "real-webmcp");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose browser's features the suite reads",
            ["browser"] = ProvisionedBrowsers.Chromium,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        var read = await CallAsync(rig, "browser_run_code_unsafe", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite reading which features the session's browser runs",
            ["code"] = ReadFeatures,
        });

        var text = TextOf(read);
        await Assert.That((bool?)read["isError"]).IsNotEqualTo(true).Because(text);

        var line = Answer().Match(text);
        await Assert.That(line.Success).IsTrue().Because(text);

        var named = line.Groups["named"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var running = line.Groups["running"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(running).Contains("WebMCP").Because(line.Value);
        await Assert.That(named.Length).IsGreaterThan(1).Because(line.Value);
        await Assert.That(line.Groups["off"].Value).IsEmpty().Because(line.Value);
    }

    /// <summary>The features the payload's own Chromium switch list enables.</summary>
    /// <param name="bundle">The text of <c>coreBundle.js</c>.</param>
    /// <returns>Every non-empty string literal in <c>enabledFeatures</c>.</returns>
    private static List<string> PlaywrightsEnabledFeatures(string bundle)
    {
        var block = EnabledFeaturesBlock().Match(bundle);

        return block.Success
            ? [.. StringLiteral().Matches(block.Groups["body"].Value).Select(literal => literal.Groups[1].Value).Where(name => name.Length > 0)]
            : [];
    }

    /// <summary>What a list of features has that BrowserAI's switch does not.</summary>
    /// <param name="features">The features to look for.</param>
    /// <returns>Those the switch leaves out, in order.</returns>
    private static List<string> MissingFromBrowserAisSwitch(IEnumerable<string> features)
    {
        var carried = BrowserConfiguration.WebMcpFeatureSwitch["--enable-features=".Length..].Split(',');

        return [.. features.Where(feature => !carried.Contains(feature, StringComparer.Ordinal))];
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

    [GeneratedRegex(@"FEATURES named=(?<named>\S*) running=(?<running>\S*) off=(?<off>\S*) END")]
    private static partial Regex Answer();

    [GeneratedRegex(@"enabledFeatures = \(\) => \[(?<body>.*?)\]\.filter\(Boolean\)", RegexOptions.Singleline)]
    private static partial Regex EnabledFeaturesBlock();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex StringLiteral();
}

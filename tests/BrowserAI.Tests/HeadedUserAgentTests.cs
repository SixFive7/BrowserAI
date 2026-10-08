// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// A hidden Chromium sends the user agent a headed one of the same build sends,
/// read off the browser itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>6 b, decided 2026-10-04 by the maintainer, in his words verbatim: <i>"6
/// b"</i></b>. Measured the same day at Chrome for Testing 155.0.8059.12: hidden it
/// sent <c>HeadlessChrome/155.0.0.0</c>, headed <c>Chrome/155.0.0.0</c>, and
/// <c>Sec-CH-UA</c> was the same in both.
/// </para>
/// <para>
/// <b>The real-browser arm is the one planted red</b>: against the tree before
/// <see cref="HeadedUserAgent"/>, the server read <c>HeadlessChrome</c>. The others
/// hold the derivation, the kept answer and the generated switch without a browser.
/// </para>
/// </remarks>
internal sealed class HeadedUserAgentTests
{
    /// <summary>What Chrome for Testing 155 sent hidden, measured 2026-10-04: an input, never an expectation of the arm below.</summary>
    private const string Hidden = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/155.0.0.0 Safari/537.36";

    /// <summary>
    /// The headed user agent is the hidden one with its product token replaced,
    /// and anything that is not a hidden Chromium's user agent gives none.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHeadedUserAgentIsTheHiddenOneWithItsOneProductTokenReplaced()
    {
        await Assert.That(HeadedUserAgent.Headed(Hidden))
            .IsEqualTo("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36");

        // Already headed, a Firefox string, two tokens, a quote, and nothing.
        await Assert.That(HeadedUserAgent.Headed(HeadedUserAgent.Headed(Hidden))).IsNull();
        await Assert.That(HeadedUserAgent.Headed("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:156.0) Gecko/20100101 Firefox/156.0")).IsNull();
        await Assert.That(HeadedUserAgent.Headed(Hidden + " HeadlessChrome/1.0")).IsNull();
        await Assert.That(HeadedUserAgent.Headed(Hidden + " \"")).IsNull();
        await Assert.That(HeadedUserAgent.Headed(null)).IsNull();
        await Assert.That(HeadedUserAgent.Headed(string.Empty)).IsNull();
    }

    /// <summary>The body of the page the browser dumped is what it wrote there.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAnswerIsTheBodyOfThePageTheBrowserDumped()
    {
        var dumped = $"<html><head><title>ua</title><script>document.write(navigator.userAgent)</script></head><body>{Hidden}</body></html>\n";

        await Assert.That(HeadedUserAgent.BodyOf(dumped)).IsEqualTo(Hidden);
        await Assert.That(HeadedUserAgent.BodyOf("<html><head></head></html>")).IsNull();
        await Assert.That(HeadedUserAgent.BodyOf(string.Empty)).IsNull();
    }

    /// <summary>
    /// A hidden Chromium's config carries the switch, after BrowserAI's own five; a
    /// headed one, a Firefox one and a hidden one with nothing to send carry none.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OnlyAHiddenChromiumConfigCarriesTheSwitch()
    {
        var session = SessionPath.For(Path.Combine(ScratchRoot.Path, $"ua-config-{Guid.NewGuid():N}"));
        var headed = HeadedUserAgent.Headed(Hidden)!;

        // Joined, so the order is part of what is compared: the switch comes after
        // BrowserAI's own five.
        static string? argumentsOf(GeneratedConfig config) =>
            JsonNode.Parse(config.Json)!["browser"]!["launchOptions"]!["args"] is JsonArray arguments
                ? string.Join(" | ", arguments.Select(argument => (string?)argument))
                : null;

        var own = string.Join(" | ", BrowserConfiguration.ChromiumArguments);

        await Assert.That(argumentsOf(BrowserConfiguration.ForSession(session, headed: false, ProvisionedBrowsers.Chromium, transcript: false, RunOptions.Default, headed)))
            .IsEqualTo($"{own} | {HeadedUserAgent.Switch}{headed}");

        await Assert.That(argumentsOf(BrowserConfiguration.ForSession(session, headed: true, ProvisionedBrowsers.Chromium, transcript: false, RunOptions.Default, headed)))
            .IsEqualTo(own);

        await Assert.That(argumentsOf(BrowserConfiguration.ForSession(session, headed: false, ProvisionedBrowsers.Chromium, transcript: false, RunOptions.Default)))
            .IsEqualTo(own);

        await Assert.That(argumentsOf(BrowserConfiguration.ForSession(session, headed: false, ProvisionedBrowsers.Firefox, transcript: false, RunOptions.Default, headed)))
            .IsNull();
    }

    /// <summary>
    /// A kept answer is used for the build it was asked of and for no other: one
    /// whose executable has changed since is asked again.
    /// </summary>
    /// <remarks>
    /// <b>No browser starts here.</b> The executable is a file of the arm's own that
    /// is no program at all, so an ask that should not happen is visible as a failed
    /// start and a <see langword="null"/>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AKeptAnswerIsUsedForTheBuildItWasAskedOfAndNoOther()
    {
        var revision = Path.Combine(ScratchRoot.Path, $"chromium-ua-{Guid.NewGuid():N}");
        var executable = Path.Combine(revision, "chrome-win64", "chrome.exe");

        _ = Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "not a program");

        var headed = HeadedUserAgent.Headed(Hidden)!;
        var cache = HeadedUserAgent.CacheFileFor(executable);

        await Assert.That(cache).IsEqualTo(Path.Combine(revision, HeadedUserAgent.CacheFileName));

        await File.WriteAllTextAsync(cache, $"{HeadedUserAgent.StampOf(executable)}\n{headed}\n");

        await Assert.That(await HeadedUserAgent.ForAsync(executable, revision, NullLogger.Instance, CancellationToken.None)).IsEqualTo(headed);

        // A different build: the kept answer no longer matches, and the ask that
        // follows meets a file that is no program.
        await File.AppendAllTextAsync(executable, " any more");

        await Assert.That(await HeadedUserAgent.ForAsync(executable, revision, NullLogger.Instance, CancellationToken.None)).IsNull();
    }

    /// <summary>
    /// Against a <b>real</b> Chromium: a hidden session's requests and its pages
    /// carry the headed user agent of the provisioned build, and the default client
    /// hints still name that build.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree before <see cref="HeadedUserAgent"/></b>: the
    /// server read <c>HeadlessChrome/155.0.0.0</c>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AgainstARealChromiumAHiddenSessionSendsTheHeadedUserAgent()
    {
        SuiteEnvironment.RequireProvisionedChromium();

        using var site = LoopbackSite.Start();

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "hidden-user-agent");
        var major = BrowserAiPaths.BrowserVersionOf(ProvisionedBrowsers.Chromium).Split('.')[0];

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a hidden session whose user agent the suite reads",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        var navigated = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite opening a page that records what it was sent",
            ["url"] = site.Url("user-agent"),
        });

        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true).Because(TextOf(navigated));

        var (_, headers) = site.Requests.Last(entry => entry.Name == "user-agent");
        var sent = headers["User-Agent"];

        await Assert.That(sent).DoesNotContain(HeadedUserAgent.HeadlessProduct);
        await Assert.That(sent).Contains($" Chrome/{major}.");
        await Assert.That(headers["Sec-CH-UA"]).Contains($"v=\"{major}\"");

        var read = await CallAsync(rig, "browser_evaluate", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite reading what the page itself sees",
            ["function"] = "() => navigator.userAgent",
        });

        await Assert.That(TextOf(read)).Contains(sent);
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

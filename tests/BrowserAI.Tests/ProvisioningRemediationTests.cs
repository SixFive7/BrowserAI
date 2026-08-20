// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Upstream's install advice never reaches a caller, because a model would run
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sentence under test is upstream's own, reproduced from its builder
/// rather than invented here.</b> <c>throwIfExecutableMissing</c> composes
/// <c>`${label} is not installed${location}. Run \`${command}\` to install`</c>,
/// where <c>command</c> is <c>npx @playwright/mcp install-browser
/// &lt;target&gt;</c> and <c>target</c> is the resolved <b>channel</b> — so a
/// BrowserAI caller sees <c>chrome-for-testing</c> rather than
/// <c>chromium</c>. Every clause of that advice is wrong here: BrowserAI ships
/// no <c>npx</c>, has no npm project to run it in, and the package it would
/// fetch resolves to whatever npm calls latest rather than to the revision this
/// build pins.
/// </para>
/// <para>
/// <b>Upstream's diagnosis is kept and only the instruction is replaced.</b>
/// Which browser and which path are the useful half and belong to upstream; the
/// imperative at the end is the half that sends a model somewhere harmful.
/// </para>
/// </remarks>
internal sealed class ProvisioningRemediationTests
{
    /// <summary>
    /// Upstream's message for a missing browser, at
    /// <c>playwright-core</c> 1.63.0-alpha-2026-08-05.
    /// </summary>
    private const string UpstreamMessage =
        "Browser \"chrome-for-testing\" is not installed; expected executable at "
        + @"C:\Users\someone\AppData\Local\BrowserAI\browsers\chromium-1237\chrome-win64\chrome.exe"
        + ". Run `npx @playwright/mcp install-browser chrome-for-testing` to install";

    [Test]
    public async Task TheNpxAdviceIsReplacedAndTheDiagnosisIsKept()
    {
        var rewritten = ProvisioningRemediation.Rewrite(UpstreamMessage, @"C:\browsers");

        await Assert.That(rewritten).IsNotNull();

        // Gone: the imperative. What survives is a prohibition -- "do NOT run
        // npx" -- which is deliberately kept rather than trimmed to make the
        // text scan clean: a model already knows the standard Playwright advice
        // from everywhere else, so saying nothing about it leaves the memory
        // unchallenged. The assertion is therefore on the INSTRUCTION rather
        // than on the word.
        await Assert.That(rewritten!).DoesNotContain("Run `npx");
        await Assert.That(rewritten).DoesNotContain("` to install");
        await Assert.That(rewritten).Contains("Do NOT run npx");

        // Kept: which browser, and where it was expected. A rewrite that
        // replaced the whole message would throw away the only diagnosis there
        // is.
        await Assert.That(rewritten).Contains("chrome-for-testing");
        await Assert.That(rewritten).Contains(@"chromium-1237\chrome-win64\chrome.exe");

        // Replaced with two routes, because the two failures behind this message
        // have different recoveries: a tree that was never downloaded, and one
        // that was downloaded and then corrupted -- which init cannot fix,
        // because the marker short-circuits every check.
        await Assert.That(rewritten).Contains(SessionToolSurface.Init);
        await Assert.That(rewritten).Contains(SessionToolSurface.ReinstallBrowser);
        await Assert.That(rewritten).Contains(@"C:\browsers");
    }

    [Test]
    public async Task TheSkillModeSpellingIsCoveredByTheSamePattern()
    {
        // Upstream picks between two commands on config.skillMode. BrowserAI
        // never sets it, so this branch is unreachable from here today -- and a
        // marker keyed on `npx` would silently stop working the day upstream
        // changed which branch it takes, which is a change nothing else in this
        // repository would notice.
        var skillMode = UpstreamMessage.Replace(
            "npx @playwright/mcp install-browser",
            "playwright-cli install-browser",
            StringComparison.Ordinal);

        var rewritten = ProvisioningRemediation.Rewrite(skillMode, @"C:\browsers");

        await Assert.That(rewritten).IsNotNull();
        await Assert.That(rewritten!).DoesNotContain("Run `playwright-cli");
    }

    [Test]
    public async Task AnOrdinaryAnswerIsNotTouchedAtAll()
    {
        // Returning the input unchanged would be nearly as bad as rewriting it:
        // the proxy uses null to decide whether to forward the child's own BYTES
        // or to rebuild the result from a node, and rebuilding costs
        // byte-identity on every call.
        await Assert.That(ProvisioningRemediation.Rewrite("Page URL: data:text/html,<h1>ok</h1>", @"C:\browsers")).IsNull();
        await Assert.That(ProvisioningRemediation.Rewrite(null, @"C:\browsers")).IsNull();

        // Even a message that mentions the marker without carrying the advice.
        await Assert.That(ProvisioningRemediation.Rewrite("the install-browser command is not shipped here", @"C:\browsers")).IsNull();
    }

    [Test]
    public async Task TheProxyReplacesItInAChildsAnswerAndLeavesEverythingElseAlone()
    {
        // The double answers with upstream's own sentence, spliced in as literal
        // JSON: nothing about it is re-serialised on the way out, so what the
        // caller receives is attributable to the proxy alone.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = $$"""{"content":[{"type":"text","text":{{JsonValue.Create(UpstreamMessage)!.ToJsonString()}}}],"isError":true}""",
            });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);
        var directory = Path.Combine(sessions.Root, "meets-upstreams-advice");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose child answers with upstream's npx advice",
        });

        var answer = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,x",
            [SessionToolSurface.SessionParameter] = directory,
        });

        var text = TextOf(answer);

        // The whole point: a model reading this answer must not be TOLD to run
        // npx, and it is the proxy rather than the child that decides so.
        await Assert.That(text).DoesNotContain("Run `npx");
        await Assert.That(text).Contains("Do NOT run npx");
        await Assert.That(text).Contains(SessionToolSurface.ReinstallBrowser);
        await Assert.That(text).Contains("chrome-for-testing");
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Protocol;
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
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
        });

        var text = TextOf(answer);

        // The whole point: a model reading this answer must not be TOLD to run
        // npx, and it is the proxy rather than the child that decides so.
        await Assert.That(text).DoesNotContain("Run `npx");
        await Assert.That(text).Contains("Do NOT run npx");
        await Assert.That(text).Contains(SessionToolSurface.ReinstallBrowser);
        await Assert.That(text).Contains("chrome-for-testing");
    }

    /// <summary>
    /// A page that renders upstream's advice in its own title, in an answer the
    /// child did not mark as an error, is forwarded exactly as the child wrote
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is <c>isError</c>, and it is the whole provenance check there
    /// is.</b> Upstream sets <c>isError</c> on every answer carrying an
    /// <c>Error</c> section and on no other, so gating the scan on it loses
    /// nothing on the path the rewrite exists for and takes every ordinary
    /// answer out of reach of page-controlled text.
    /// </para>
    /// <para>
    /// ⚠️ <b>This does not close the whole bypass and is not claimed to.</b> An
    /// <c>isError</c> answer against a live tab carries the page's own title in
    /// the same result, so page content can still trip the rewrite on a call
    /// that genuinely failed. ⚠️ <i>Corrected 2026-08-26 (previously "what stops
    /// that mattering is the other half — <c>ArtifactPointerTests.APointerSurvivesAnAnswerThatAlsoTrippedTheProvisioningRewrite</c>").</i>
    /// There is no other half and there is nothing left for it to matter to:
    /// what made a spurious rewrite <i>harmful</i> was that the rewrite branch
    /// skipped the artifact bookkeeping, so a page could switch the pointer
    /// protection off. Nothing does bookkeeping now. A page that quotes
    /// upstream's advice inside a genuinely failed call costs that one answer
    /// its byte-identity, and the proxy logs that it did.
    /// </para>
    /// </remarks>
    [Test]
    public async Task APageQuotingUpstreamsAdviceInASuccessfulAnswerIsForwardedUntouched()
    {
        // The sentence sits exactly where upstream puts a page title, and the
        // answer carries no `isError`: this is an ordinary, successful call
        // against a page that happens to render the words.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = $$"""{"content":[{"type":"text","text":{{JsonValue.Create("### Page\n- Page Title: " + UpstreamMessage)!.ToJsonString()}}}]}""",
            });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);
        var directory = Path.Combine(sessions.Root, "meets-a-page-that-quotes-it");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose child answers with a page quoting upstream's npx advice",
        });

        var answer = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,x",
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
        });

        var text = TextOf(answer);

        // The child's own bytes, unedited: BrowserAI's instruction text does not
        // get spliced into whatever a page happened to say.
        await Assert.That(text).Contains("Run `npx");
        await Assert.That(text).DoesNotContain("Do NOT run npx");
        await Assert.That(text).DoesNotContain(SessionToolSurface.ReinstallBrowser);
    }

    /// <summary>
    /// The real bundled child, with a browsers root that has nothing in it,
    /// still says the sentence this rewrite is anchored on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE CANARY, and the thing it watches is upstream's WORDING rather
    /// than BrowserAI's code.</b> Every other test in this file drives
    /// <see cref="UpstreamMessage"/>, which is a constant somebody typed out of
    /// the bundle on 2026-08-16. If upstream rewords its advice, every one of
    /// them stays green while the rewrite silently stops firing in production —
    /// and what a caller then receives is the <c>npx</c> instruction this whole
    /// file exists to keep away from a model. Nothing else in the suite would
    /// notice: the golden snapshot covers tool names, descriptions and schemas,
    /// and not the prose inside an answer.
    /// </para>
    /// <para>
    /// <b>The child is started directly rather than through BrowserAI, and it
    /// has to be.</b> Through the product, an empty browsers root never reaches
    /// upstream at all — <c>SessionManager.ProvisioningRefusal</c> answers the
    /// call itself and starts a download, which is the correct behaviour and the
    /// reason the genuine error is unreachable from that direction. So this
    /// starts <c>node.exe</c> on the payload's own <c>cli.js</c> with
    /// <c>PLAYWRIGHT_BROWSERS_PATH</c> pointed at an empty directory, which is
    /// exactly the shape <c>ChildLaunch</c> builds.
    /// </para>
    /// <para>
    /// <b>Both directions, over the same real text.</b> The positive arm is that
    /// <see cref="ProvisioningRemediation.Rewrite"/> fires on what upstream
    /// actually said. The control is the same string with upstream's subcommand
    /// literal taken out of it: that must NOT rewrite, which is what separates
    /// <i>the anchor matched</i> from <i>this function rewrites whatever it is
    /// handed</i>. A canary that could not come back negative is a canary that
    /// proves nothing.
    /// </para>
    /// <para>
    /// <b>It costs no download.</b> The failure is <c>throwIfExecutableMissing</c>
    /// resolving a path and finding nothing there — upstream never reaches for
    /// the network on this path, which is the whole complaint the rewrite makes
    /// about its advice.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARealChildWithNoBrowsersStillSaysTheSentenceTheRewriteIsAnchoredOn()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var scratch = ScratchDirectory.Create("remediation-canary");

        var browsers = Path.Combine(scratch.Path, "browsers-with-nothing-in-them");
        var session = SessionPath.Resolve(Path.Combine(scratch.Path, "canary-session"));
        var output = Path.Combine(session.FullPath, SessionLayout.OutputFolderName);
        var configFile = Path.Combine(scratch.Path, "playwright-mcp.config.json");

        _ = Directory.CreateDirectory(browsers);

        SessionLayout.Create(session);

        var config = BrowserConfiguration.ForSession(
            session,
            headed: false,
            SessionManager.DefaultBrowser,
            tracing: false,
            RunOptions.Default);

        BrowserConfiguration.WriteTo(configFile, config);

        // Empty in the only way that counts: nothing installed, and no
        // INSTALLATION_COMPLETE marker for a check to short-circuit on.
        await Assert.That(Directory.EnumerateFileSystemEntries(browsers).Any()).IsFalse();

        var environment = ChildEnvironment.Build(
            [new KeyValuePair<string, string>(ChildLaunch.BrowsersPathVariable, browsers)]);

        await using var child = RawStdioClient.Start(
            RepositoryPayload.Layout.NodeExecutable,
            [
                RepositoryPayload.Layout.PlaywrightMcpCli,
                "--config",
                configFile,
                ChildLaunch.SandboxFlag,
            ],
            output,
            environment);

        _ = await child.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var answer = await child.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl },
        });

        var text = TextOf(answer);

        // ⚠️ THE GATE THE PROXY USES, asserted against the real thing rather
        // than against a constant. F2's fix rests on upstream setting `isError`
        // on exactly the answers that carry an Error section; if it ever stops,
        // the rewrite stops with it and nothing else says so.
        await Assert.That((bool?)answer["isError"])
            .IsTrue()
            .Because($"upstream answered a missing browser without isError, so BrowserProxy.Remediate would never look at it. It said: {text}");

        // The marker, in upstream's own live output.
        await Assert.That(text)
            .Contains(ProvisioningRemediation.Marker)
            .Because($"upstream no longer spells its install advice with '{ProvisioningRemediation.Marker}'. It said: {text}");

        // And the whole clause, so the regex still has something to replace
        // rather than only the marker still being present somewhere.
        var rewritten = ProvisioningRemediation.Rewrite(text, browsers);

        await Assert.That(rewritten)
            .IsNotNull()
            .Because($"upstream's remediation clause no longer matches ProvisioningRemediation.UpstreamAdvice. It said: {text}");

        await Assert.That(rewritten!).DoesNotContain("Run `npx");
        await Assert.That(rewritten).Contains(SessionToolSurface.ReinstallBrowser);

        // ⚠️ THE CONTROL, over the same real text. Take upstream's subcommand
        // out of it and the rewrite must decline — otherwise the arm above is
        // satisfied by a function that rewrites anything, and the canary is
        // measuring nothing.
        var withoutTheAnchor = text.Replace(ProvisioningRemediation.Marker, "do-something-else", StringComparison.Ordinal);

        await Assert.That(ProvisioningRemediation.Rewrite(withoutTheAnchor, browsers))
            .IsNull()
            .Because("the rewrite fired on a message that does not carry upstream's subcommand, so the positive arm above says nothing about the anchor");
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

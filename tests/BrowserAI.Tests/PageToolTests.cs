// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai_page_tool</c>, driven against real pages that really register
/// WebMCP tools.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real pages and not a double, because the thing under test is a rule
/// about somebody else's code.</b> The wire name is built by
/// <c>@playwright/mcp</c> out of the page's own tool name, the tool list covers
/// the current tab only, and a page's handler is what decides whether a call ever
/// answers. A fake child would let this suite assert that BrowserAI agrees with
/// BrowserAI about all three. Every page below is a <c>data:</c> URL carrying a
/// <c>document.modelContext</c>, which is upstream's own page-side contract --
/// measured 2026-09-21 to register exactly as a served page does.
/// </para>
/// <para>
/// ⚠️ <b>Served from loopback and registered through the browser's own WebMCP since
/// 2026-11-17</b>, with the adoption of <c>@playwright/mcp</c> 0.0.85. Its
/// <c>playwright-core</c>, 1.66.0-alpha-2026-11-09, reads a page's tools only through the
/// browser's own interface and from an isolated world, so the arms went red against it, eight
/// of them, on every page this class had: the snapshot carried no tool block and every call
/// was refused as a tool the page does not offer. The paragraph above is the record of the
/// pages before; see <c>Page</c> for the ones now.
/// </para>
/// <para>
/// <b>One conversation, not one per assertion.</b> Every answer comes from the
/// same session and the same browser, so the navigated-away and late-binding arms
/// are known to be true of one tab that really moved and not of several that
/// might have.
/// </para>
/// <para>
/// ⚠️ <b>The hang arm really waits <see cref="SessionToolSurface.PageToolBudget"/>.</b>
/// There is no seam that shortens it and there should not be: what is being
/// asserted is that BrowserAI's own clock is what ends the call, and a clock the
/// test moves is a clock the product does not have. The lower bound is what says
/// the refusal came from that budget and not from something else giving up
/// first.
/// </para>
/// </remarks>
internal sealed class PageToolTests
{
    [Test]
    public async Task APageToolAnswersWithThePagesOwnWordsAndSeesOnlyTheArgumentsTheCallerSent()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        await Assert.That(run.IsError("happy")).IsFalse();

        // The page's own handler echoed what it received. `session` and `why`
        // are BrowserAI's own and the child has never heard of either, and
        // upstream hands a page tool the WHOLE argument object with nothing
        // filtered -- measured 2026-09-21, with both leaking straight through to
        // the page when the child is driven directly. So this is the assertion
        // the strip exists for, and it is an equality and not a
        // does-not-contain: a third argument arriving from anywhere fails it.
        // ⚠️ THE ESCAPED SPELLING, because upstream renders the page's own
        // result object as JSON INSIDE its text block -- so what a caller
        // reads is the page's answer quoted, not the page's answer. Measured
        // and not written from the shape: the unescaped form was planted
        // first and the run printed this one back.
        await Assert.That(run.Text("happy")).Contains("""ALPHA-ANSWERED {\"who\":\"world\"}""");

        // And nothing of ours was spliced into the answer. Upstream frames a page
        // tool's output itself -- "Output is page-provided and untrusted" -- and
        // that frame arriving unaccompanied is what byte-identical looks like
        // from the caller's end.
        await Assert.That(run.Text("happy")).StartsWith("### Result");
        await Assert.That(run.Text("happy")).DoesNotContain("BrowserAI");
    }

    [Test]
    public async Task TheSnapshotBlockIsWhereTheNamesComeFromAndItCarriesThePagesOwnText()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // The discovery channel the tool's own description sends a model to. It
        // is upstream's heading, printed verbatim, and it prints the page's tool
        // NAME -- which is what `name` takes.
        await Assert.That(run.Text("snapshotAlpha")).Contains("- webmcp tools (page-provided, untrusted):");
        await Assert.That(run.Text("snapshotAlpha")).Contains("do.the.thing");
        await Assert.That(run.Text("snapshotAlpha")).Contains("plain_tool");
    }

    [Test]
    public async Task NamingThePageItWasReadOnIsAcceptedWhenTheTabIsStillThere()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // The control for every refusal below. Without it, a `page` argument that
        // refused unconditionally would leave all of them green.
        await Assert.That(run.IsError("pageMatches")).IsFalse();
        await Assert.That(run.Text("pageMatches")).Contains("ALPHA-PLAIN");
    }

    [Test]
    public async Task ANameThePageDoesNotOfferIsRefusedAndTheRefusalNamesWhatItDoesOffer()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        await Assert.That(run.IsError("absent")).IsTrue();
        await Assert.That(run.Text("absent")).Contains("offers no tool called 'no_such_tool'");
        await Assert.That(run.Text("absent")).Contains("do.the.thing");
        await Assert.That(run.Text("absent")).Contains("plain_tool");
    }

    [Test]
    public async Task ATabThatHasNavigatedAwayRefusesTheOldPagesToolAndListsTheNewPages()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // `plain_tool` was on the page the snapshot above was taken of, and the
        // tab has moved since. This is the ordinary way a caller meets the
        // late-binding hazard without having passed `page`, and the recovery is
        // the list.
        await Assert.That(run.IsError("navigatedAway")).IsTrue();
        await Assert.That(run.Text("navigatedAway")).Contains("offers no tool called 'plain_tool'");
        await Assert.That(run.Text("navigatedAway")).Contains("do.the.thing");
    }

    [Test]
    public async Task TheSameNameOnAnotherPageIsRefusedWhenTheCallerSaidWhichPageItReadItOn()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // Both pages offer a tool called `do.the.thing` and they are different
        // code. The tab is on the second; the caller read the first. Refusing is
        // the whole of what `page` buys, and naming BOTH URLs is what lets the
        // caller see which end it was wrong about.
        await Assert.That(run.IsError("pageMoved")).IsTrue();
        await Assert.That(run.Text("pageMoved")).Contains(run.AlphaUrl);
        await Assert.That(run.Text("pageMoved")).Contains(run.BetaUrl);
        await Assert.That(run.Text("pageMoved")).Contains("bind late");

        // And it really was refused and not merely reported: the second
        // page's tool never ran.
        // ⚠️ A SENTINEL THE PAGE COMPOSES AT RUN TIME, never one written whole
        // into the page source. The refusal quotes both URLs, and these pages
        // ARE their URLs -- a literal in the source would be in the refusal
        // whether or not the tool ever ran, which is how this arm first passed
        // for the wrong reason and then failed for the right one.
        await Assert.That(run.Text("pageMoved")).DoesNotContain("BETA-RAN");
    }

    [Test]
    public async Task TwoToolsWithOneNameOnOnePageAreRefusedAsAmbiguousRatherThanGuessedAt()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        await Assert.That(run.IsError("ambiguous")).IsTrue();
        await Assert.That(run.Text("ambiguous")).Contains("2 tools called 'twin'");

        // Upstream's own collision spelling, which is the only thing that tells
        // them apart and is therefore what the refusal has to hand back.
        await Assert.That(run.Text("ambiguous")).Contains("webmcp_twin");
        await Assert.That(run.Text("ambiguous")).Contains("webmcp_twin_2");
        await Assert.That(run.Text("ambiguous")).DoesNotContain("TWIN-RAN");
    }

    [Test]
    public async Task ADisplayTitleIsNotTheNameAndTheRefusalSaysWhichOneToPass()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // Upstream builds the entry with `title: tool.title || tool.name`, so a
        // page that sets its own title puts THAT in `annotations.title` while the
        // snapshot block goes on printing the name -- measured 2026-09-21. The
        // name is what resolves; the title is the cross-check, and a title that
        // matches with a wire name that does not follow the rule has exactly two
        // readings, so the refusal carries both.
        //
        // ⚠️ NO TITLE REACHES THE CHILD SINCE 2026-11-17 (previously the refusal said
        // "whose title is 'Human Title'" and named webmcp_raw_name_here and
        // webmcp_Human_Title). playwright-core 1.66.0-alpha-2026-11-09 keeps no title
        // out of the browser's own WebMCP and sets `annotations.title` to the name,
        // read in its bundle that day, so a display title is a name the page does not
        // offer and is refused as one, with the name it does offer.
        await Assert.That(run.IsError("byTitle")).IsTrue();
        await Assert.That(run.Text("byTitle")).Contains("offers no tool called 'Human Title'");
        await Assert.That(run.Text("byTitle")).Contains("webmcp_raw_name_here");

        // And the same tool answers when it is named the way the snapshot printed
        // it, which is what makes the refusal above a correction and not a
        // wall.
        await Assert.That(run.IsError("byName")).IsFalse();
        await Assert.That(run.Text("byName")).Contains("TITLED-ANSWERED");
    }

    [Test]
    public async Task APageToolThatNeverAnswersIsEndedByUpstreamsOwnTimeoutBeforeBrowserAisAndTheSessionSurvivesIt()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // ⚠️ UPSTREAM'S OWN TIMEOUT ENDS THE CALL SINCE 2026-11-17 (previously
        // APageToolThatNeverAnswersIsAbandonedOnBrowserAiSOwnClockAndTheSessionSurvivesIt,
        // which held BrowserAI's refusal "did not answer within", its three sentences
        // and a lower bound of SessionToolSurface.PageToolBudget). playwright-core
        // 1.66.0-alpha-2026-11-09 bounds a page tool's call itself and answers
        // "browserBackend.callTool: Timeout 30000ms exceeded.", measured that day, so the
        // call is ended by upstream half way through BrowserAI's budget, which stays as the
        // bound behind it.
        await Assert.That(run.IsError("hang")).IsTrue();
        await Assert.That(run.Text("hang")).Contains("Timeout");
        await Assert.That(run.HangElapsed).IsLessThan(SessionToolSurface.PageToolBudget);

        // And the session is genuinely still there, asked AFTER the abandonment
        // and not before it.
        await Assert.That(run.IsError("afterHang")).IsFalse();
        await Assert.That(run.Text("afterHang")).Contains("- Page URL:");
    }

    [Test]
    public async Task APageSuppliedNameStillHasNoWayThroughTheDoorOfItsOwn()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var run = await PageToolRun.SharedAsync();

        // The rule the page-tool caller does NOT relax. A `webmcp_*` name
        // arriving straight from a client has no verdict row, is refused before
        // anything is forwarded, and stays that way -- the only route to a page
        // tool is through the judged tool.
        //
        // ⚠️ Answered since 2026-10-04 as a tool BrowserAI does not have
        // (previously asserted "no forwarding verdict", the refusal for a listed
        // tool with no row). The maintainer's decision of 2026-10-03: a name in
        // no list this BrowserAI serves gets the plain answer, and BrowserAI's
        // list never carries a page's tools.
        await Assert.That(run.IsError("straightToTheWireName")).IsTrue();
        await Assert.That(run.Text("straightToTheWireName")).StartsWith("BrowserAI has no tool 'webmcp_");
        await Assert.That(run.Text("straightToTheWireName")).DoesNotContain("ALPHA-ANSWERED");
    }
}

/// <summary>
/// One scripted conversation with the published binary against pages that
/// register WebMCP tools, captured once.
/// </summary>
internal sealed record PageToolRun
{
    private static readonly Lazy<Task<PageToolRun>> Shared = new(CaptureAsync);

    /// <summary>Answers from the run, keyed by a short label.</summary>
    public required IReadOnlyDictionary<string, JsonObject> Answers { get; init; }

    /// <summary>The first page's URL, as the browser reported it.</summary>
    public required string AlphaUrl { get; init; }

    /// <summary>The second page's URL, as the browser reported it.</summary>
    public required string BetaUrl { get; init; }

    /// <summary>How long the abandoned page-tool call actually took.</summary>
    public required TimeSpan HangElapsed { get; init; }

    /// <summary>The one capture, run at most once per test process.</summary>
    /// <returns>The captured run.</returns>
    public static Task<PageToolRun> SharedAsync() => Shared.Value;

    /// <summary>The text of one answer.</summary>
    /// <param name="label">The label the answer was recorded under.</param>
    /// <returns>The joined text content.</returns>
    public string Text(string label) =>
        string.Join(
            "\n",
            (Answers[label]["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

    /// <summary>Whether one answer was a refusal.</summary>
    /// <param name="label">The label the answer was recorded under.</param>
    /// <returns>Its <c>isError</c>.</returns>
    public bool IsError(string label) => (bool?)Answers[label]["isError"] is true;

    /// <summary>
    /// A page that registers WebMCP tools through the browser's own <c>document.modelContext</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The browser's own interface since 2026-11-17</b> <i>(previously a
    /// <c>data:</c> URL whose script set <c>document.modelContext</c> to an object of its
    /// own, with a <c>getTools()</c> and an <c>invokeTool()</c>, which
    /// <c>collectToolsInPage</c> read in the page's own world)</i>. <c>playwright-core</c>
    /// 1.66.0-alpha-2026-11-09 reads a page's tools from an isolated world, where an object
    /// a page script set is not there, so only the browser's own WebMCP reaches it, and a
    /// browser offers that to a secure origin: the page is served from loopback by
    /// <see cref="LoopbackSite"/>, and each tool is handed to <c>registerTool</c> with an
    /// <c>execute</c> that calls the page's one handler with the tool's name. A tool the
    /// browser refuses to register is left out, and the arm that needs it says what it saw.
    /// The answers are deliberately unmistakable strings: the question is what a PAGE's words
    /// do, and a string nobody could mistake for Playwright's own is what makes the answer
    /// readable.
    /// </para>
    /// </remarks>
    /// <param name="tools">The literal JavaScript array of the tools to register.</param>
    /// <param name="invoke">The literal JavaScript handler, called with a tool's name and its input.</param>
    /// <returns>The page's HTML.</returns>
    private static string Page(string tools, string invoke) =>
        "<!doctype html><title>page tools</title><h1>page tools</h1><script>const handler="
        + invoke
        + ";for(const t of "
        + tools
        + "){try{document.modelContext.registerTool({...t,execute:(i)=>handler(t.name,i)});}catch(e){document.title+=' refused '+t.name+': '+e.message;}}</script>";

    private static async Task<PageToolRun> CaptureAsync()
    {
        PublishedSlice.EnsureFresh();

        using var scratch = await ScratchDirectory.CreateAsync("page-tools");

        var alpha = Page(
            """[{name:"do.the.thing",description:"ALPHA-DESC-A",inputSchema:{type:"object",properties:{who:{type:"string"}}}},{name:"plain_tool",description:"ALPHA-DESC-B",inputSchema:{type:"object",properties:{}}}]""",
            """(n,i)=>({content:[{type:"text",text:n==="plain_tool"?"ALPHA-PLAIN":"ALPHA-ANSWERED "+JSON.stringify(i)}]})""");

        var beta = Page(
            """[{name:"do.the.thing",description:"BETA-DESC",inputSchema:{type:"object",properties:{}}}]""",
            """()=>({content:[{type:"text",text:"BETA"+"-RAN"}]})""");

        // ⚠️ One in the page and one in a frame since 2026-11-17 (previously both in the
        // page): Chromium's own registerTool keeps the first of two tools of one name in a
        // frame, measured that day, and a frame of the same origin registers its own.
        var twin = Page(
            """[{name:"twin",description:"TWIN-FIRST",inputSchema:{type:"object",properties:{}}}]""",
            """()=>({content:[{type:"text",text:"TWIN"+"-RAN"}]})""") + "<iframe src=\"/twin-frame\"></iframe>";

        var twinFrame = Page(
            """[{name:"twin",description:"TWIN-SECOND",inputSchema:{type:"object",properties:{}}}]""",
            """()=>({content:[{type:"text",text:"TWIN"+"-RAN"}]})""");

        var titled = Page(
            """[{name:"raw_name_here",title:"Human Title",description:"TITLED-DESC",inputSchema:{type:"object",properties:{}}}]""",
            """()=>({content:[{type:"text",text:"TITLED-ANSWERED"}]})""");

        // A handler that returns a promise nothing ever settles, which is the
        // shape the browser server puts no limit on at all.
        var hang = Page(
            """[{name:"hang_forever",description:"HANG-DESC",inputSchema:{type:"object",properties:{}}}]""",
            """()=>new Promise(()=>{})""");

        using var site = LoopbackSite.Start(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alpha"] = alpha,
            ["beta"] = beta,
            ["twin"] = twin,
            ["twin-frame"] = twinFrame,
            ["titled"] = titled,
            ["hang"] = hang,
        });

        var answers = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var session = Path.Combine(scratch.Path, "page-tool-session");

        await using var client = await RawStdioClient.StartAsync(
            PublishedSlice.Executable,
            PublishedSlice.Mcp,
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion).ConfigureAwait(false);

        _ = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the page-tool suite's own session",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        }).ConfigureAwait(false);

        async Task<JsonObject> navigateAsync(string url) =>
            await CallAsync(client, "browser_navigate", new JsonObject
            {
                ["url"] = url,
                ["session"] = session,
                ["why"] = "the suite putting a page with its own tools in front of the session",
            }).ConfigureAwait(false);

        async Task<JsonObject> snapshotAsync() =>
            await CallAsync(client, "browser_snapshot", new JsonObject
            {
                ["session"] = session,
                ["why"] = "the suite reading the page-provided tool block a model reads",
            }).ConfigureAwait(false);

        async Task<JsonObject> pageToolAsync(string name, JsonObject arguments, string? page = null)
        {
            var call = new JsonObject
            {
                ["session"] = session,
                [SessionToolSurface.NameParameter] = name,
                [SessionToolSurface.ArgumentsParameter] = arguments,
                ["why"] = "the suite exercising this call",
            };

            if (page is not null)
            {
                call[SessionToolSurface.PageParameter] = page;
            }

            return await CallAsync(client, SessionToolSurface.PageTool, call).ConfigureAwait(false);
        }

        _ = await navigateAsync(site.Url("alpha")).ConfigureAwait(false);

        answers["snapshotAlpha"] = await snapshotAsync().ConfigureAwait(false);

        // Read out of the browser's own answer and not composed from the
        // string that was navigated to: what `page` is compared against is what
        // the tab reports, and a test that built both ends would be asserting its
        // own escaping.
        var alphaUrl = PageUrlIn(TextOf(answers["snapshotAlpha"]));

        answers["happy"] = await pageToolAsync("do.the.thing", new JsonObject { ["who"] = "world" }).ConfigureAwait(false);
        answers["pageMatches"] = await pageToolAsync("plain_tool", [], alphaUrl).ConfigureAwait(false);
        answers["absent"] = await pageToolAsync("no_such_tool", []).ConfigureAwait(false);

        // The door, unchanged: the wire name straight from a client, with the
        // page that owns it in front of the session.
        answers["straightToTheWireName"] = await CallAsync(client, "webmcp_do_the_thing", new JsonObject
        {
            ["session"] = session,
            ["why"] = "the suite exercising this call",
        }).ConfigureAwait(false);

        var betaSnapshot = await NavigateAndSnapshotAsync(navigateAsync, snapshotAsync, site.Url("beta")).ConfigureAwait(false);
        var betaUrl = PageUrlIn(TextOf(betaSnapshot));

        answers["navigatedAway"] = await pageToolAsync("plain_tool", []).ConfigureAwait(false);
        answers["pageMoved"] = await pageToolAsync("do.the.thing", [], alphaUrl).ConfigureAwait(false);

        _ = await navigateAsync(site.Url("twin")).ConfigureAwait(false);
        answers["ambiguous"] = await pageToolAsync("twin", []).ConfigureAwait(false);

        _ = await navigateAsync(site.Url("titled")).ConfigureAwait(false);
        answers["byTitle"] = await pageToolAsync("Human Title", []).ConfigureAwait(false);
        answers["byName"] = await pageToolAsync("raw_name_here", []).ConfigureAwait(false);

        _ = await navigateAsync(site.Url("hang")).ConfigureAwait(false);

        var clock = Stopwatch.StartNew();

        answers["hang"] = await pageToolAsync("hang_forever", []).ConfigureAwait(false);

        var hangElapsed = clock.Elapsed;

        answers["afterHang"] = await snapshotAsync().ConfigureAwait(false);

        _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
        {
            ["directory"] = session,
            ["why"] = "the page-tool suite finished with its session",
        }).ConfigureAwait(false);

        return new PageToolRun
        {
            Answers = answers,
            AlphaUrl = alphaUrl,
            BetaUrl = betaUrl,
            HangElapsed = hangElapsed,
        };
    }

    private static async Task<JsonObject> NavigateAndSnapshotAsync(
        Func<string, Task<JsonObject>> navigate,
        Func<Task<JsonObject>> snapshot,
        string url)
    {
        _ = await navigate(url).ConfigureAwait(false);

        return await snapshot().ConfigureAwait(false);
    }

    private static string TextOf(JsonObject result) =>
        string.Join(
            "\n",
            (result["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

    /// <summary>The URL upstream printed in a snapshot's page header.</summary>
    /// <param name="text">Everything the answer said.</param>
    /// <returns>The URL.</returns>
    /// <exception cref="InvalidOperationException">No line carried one.</exception>
    private static string PageUrlIn(string text)
    {
        const string Marker = "- Page URL: ";

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (trimmed.StartsWith(Marker, StringComparison.Ordinal))
            {
                return trimmed[Marker.Length..];
            }
        }

        throw new InvalidOperationException($"No '{Marker}' line in the snapshot answer: {text}");
    }

    private static async Task<JsonObject> CallAsync(RawStdioClient client, string tool, JsonObject arguments)
    {
        var envelope = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        }).ConfigureAwait(false);

        return envelope["result"]?.AsObject()
            ?? throw new InvalidOperationException(
                $"'{tool}' answered with a JSON-RPC error rather than a result: {envelope.ToJsonString()}");
    }
}

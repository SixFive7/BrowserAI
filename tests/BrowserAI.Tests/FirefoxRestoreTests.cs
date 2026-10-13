// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A resumed Firefox session's browser, started by the resume: Playwright's blank page
/// first, Firefox's session restore beside it, the blank page closed once a reopened tab is
/// up, and the resume's answer saying what came back.
/// </summary>
/// <remarks>
/// <para>
/// <b>161, decided 2026-10-13 by the maintainer, in his words verbatim:</b> <i>"161 your
/// recommendation"</i>, after his question <i>"could we add the blank start page back but
/// inform the model resuming on what has happenend so there are no surprises?"</i> See
/// <see cref="FirefoxRestore"/>.
/// </para>
/// <para>
/// <b>The layer is the in-process rig</b> except where a real browser is the subject: the
/// double answers <c>browser_tabs</c> as upstream does (<see cref="FakeFirefoxTabs"/>), so
/// what the resume asked of the child and what it said are both readable. The real-browser
/// arm is the one a stalled launch would fail.
/// </para>
/// <para>
/// <b>Watched red on 2026-10-13, in filtered runs before the change went in</b>: the arms that
/// resume a session against <c>SessionManager.cs</c> as it stood at <c>f509229d</c>, alone and
/// with the other three files 161 changed; the four arms of the start's own reading against
/// five planted strings in <see cref="FirefoxRestore"/>; and the two arms of the waits, the
/// arm of a start that fails, the arm whose blank page is not first and the arm of the error
/// page's codes against the tree
/// with each wait taken out and the texts and codes of the reading of that day not yet in.
/// The runs and their counts are in <c>DECISIONS.md</c>, under 161.
/// </para>
/// </remarks>
internal sealed class FirefoxRestoreTests
{
    /// <summary>A page a restore reopens, as the double lists it.</summary>
    private static readonly (string Title, string Url) Reopened = ("Reopened page", "http://127.0.0.1:54321/reopened?tab=1");

    /// <summary>
    /// A page whose server had ended, as the double lists it once Firefox's error page is up:
    /// under its own address, with the title a Firefox in Dutch gives the page, which is not
    /// what tells it apart.
    /// </summary>
    private static readonly (string Title, string Url) Unreachable = ("Probleem bij het laden van de pagina", "http://127.0.0.1:54322/gone?tab=1");

    /// <summary>
    /// Upstream's tab line is read back field by field, whatever a page put in its title,
    /// and a line that is not a tab is passed over.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheTabListIsReadTheWayUpstreamWritesIt()
    {
        var tabs = UpstreamTab.ParseList(
            "### Result\n"
            + "- 0: (current) [](about:blank)\n"
            + "- 1: [A [bracketed] title](http://127.0.0.1:1/a?b=1)\n"
            + "- 2: [Gone](http://127.0.0.1:2/) [crashed]\n"
            + "- x: [not](a tab)\n"
            + "### Page\n");

        await Assert.That(tabs.Count).IsEqualTo(3);

        await Assert.That(tabs[0]).IsEqualTo(new UpstreamTab(0, IsCurrent: true, Title: string.Empty, Url: "about:blank", Crashed: false));
        await Assert.That(tabs[1]).IsEqualTo(new UpstreamTab(1, IsCurrent: false, Title: "A [bracketed] title", Url: "http://127.0.0.1:1/a?b=1", Crashed: false));
        await Assert.That(tabs[2]).IsEqualTo(new UpstreamTab(2, IsCurrent: false, Title: "Gone", Url: "http://127.0.0.1:2/", Crashed: true));

        // Written back the way upstream writes it, so a caller can match the two lists.
        await Assert.That(tabs[0].Line).IsEqualTo("- 0: (current) [](about:blank)");
        await Assert.That(tabs[2].Line).IsEqualTo("- 2: [Gone](http://127.0.0.1:2/) [crashed]");

        // A title that held a line break is one line in the answer.
        await Assert.That(new UpstreamTab(3, false, "two\nlines", "http://127.0.0.1:3/", false).Line).IsEqualTo("- 3: [two lines](http://127.0.0.1:3/)");
    }

    /// <summary>
    /// Playwright's blank page goes only when it is the first tab and a reopened tab is
    /// listed beside it, and the tab made current is the first reopened one that is not
    /// blank itself.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheBlankPageGoesOnlyWhenItIsFirstAndAReopenedTabIsListed()
    {
        static List<UpstreamTab> listing(params string[] urls) =>
            [.. urls.Select((url, index) => new UpstreamTab(index, index is 0, string.Empty, url, Crashed: false))];

        await Assert.That(FirefoxRestore.TheBlankPageCanGo(listing("about:blank"), out _)).IsFalse();
        await Assert.That(FirefoxRestore.TheBlankPageCanGo(listing("http://127.0.0.1:1/", "about:blank"), out _)).IsFalse();

        await Assert.That(FirefoxRestore.TheBlankPageCanGo(listing("about:blank", "http://127.0.0.1:1/"), out var one)).IsTrue();
        await Assert.That(one).IsEqualTo(1);

        // A blank tab the last run left open is reopened too, and is passed over for a page.
        await Assert.That(FirefoxRestore.TheBlankPageCanGo(listing("about:blank", "about:blank", "http://127.0.0.1:1/"), out var two)).IsTrue();
        await Assert.That(two).IsEqualTo(2);

        await Assert.That(FirefoxRestore.TheBlankPageCanGo(listing("about:blank", "about:blank"), out var blank)).IsTrue();
        await Assert.That(blank).IsEqualTo(1);
    }

    /// <summary>
    /// The two tools the start calls are upstream's own, in the committed tool list, and the
    /// committed verdicts allow both.
    /// </summary>
    /// <remarks>
    /// <b>Asked of the shipped files and not of constants</b>, as the idle close's own tool is:
    /// the start sends both to the session's child and asks no verdict, so a rename upstream,
    /// or a verdicts file that stopped allowing one, would leave a resume that starts nothing
    /// or reads nothing, and this is where that turns red.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheToolsTheStartCallsAreUpstreamsOwnAndAllowed()
    {
        var surface = await UpstreamSurface.DefaultSurfaceAsync();

        foreach (var tool in new[] { FirefoxRestore.TabsTool, FirefoxRestore.RunCodeTool })
        {
            await Assert.That(surface).Contains(tool);
            await Assert.That(RepositoryVerdicts.Committed.Decide(tool).IsAllowed).IsTrue().Because(tool);
        }
    }

    /// <summary>
    /// The read of every tab's document URI is matched to the tab list by position when the
    /// addresses agree and by address when they do not, and only a document URI of Firefox's
    /// network error page marks a tab.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDocumentReadIsMatchedToTheTabListAndOnlyTheErrorPageMarksATab()
    {
        static UpstreamTab tabAt(int index, string url) => new(index, index is 0, "a title in any language", url, Crashed: false);

        var pages = FirefoxRestore.ParsePages(
            "### Result\n"
            + "[{\"url\":\"http://127.0.0.1:1/\",\"documentURI\":\"about:neterror?e=connectionFailure\"},"
            + "{\"url\":\"http://127.0.0.1:2/\",\"documentURI\":\"http://127.0.0.1:2/\"},"
            + "{\"url\":\"http://127.0.0.1:3/\",\"documentURI\":null}]\n"
            + "### Page\n- Page URL: http://127.0.0.1:1/");

        await Assert.That(pages.Count).IsEqualTo(3);

        // In the same order: by position.
        await Assert.That(FirefoxRestore.Match([tabAt(0, "http://127.0.0.1:1/"), tabAt(1, "http://127.0.0.1:2/"), tabAt(2, "http://127.0.0.1:3/")], pages))
            .IsEquivalentTo([0]);

        // In another order, or with a tab the read did not see: by address.
        await Assert.That(FirefoxRestore.Match([tabAt(0, "http://127.0.0.1:2/"), tabAt(1, "http://127.0.0.1:1/")], pages))
            .IsEquivalentTo([1]);
        await Assert.That(FirefoxRestore.Match([tabAt(0, "http://127.0.0.1:9/")], pages).Count).IsEqualTo(0);

        // An answer that holds no list marks nothing.
        await Assert.That(FirefoxRestore.ParsePages("### Error\nTimeoutError: something").Count).IsEqualTo(0);
    }

    /// <summary>
    /// A resumed Firefox session whose profile holds a session: the resume starts the
    /// browser itself, waits for the reopened tabs, makes the first of them current, closes
    /// Playwright's blank page, and says which tabs came back and which shows Firefox's error
    /// page.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumedFirefoxSessionStartsItsBrowserClosesTheBlankPageAndSaysWhatCameBack()
    {
        var tabs = new FakeFirefoxTabs([Reopened, Unreachable], errorPages: [Unreachable.Url]);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(), tabs.DocumentRead());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-reopened", holdsASession: true);
        var resumed = await ResumeAsync(rig, directory);
        var text = TextOf(resumed);

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(text);

        // The child was asked to start the browser, to list until the reopened tabs were up,
        // to make the first reopened tab current and to close the blank page, in that order.
        var actions = tabs.Actions.ToList();

        await Assert.That(actions[0]).IsEqualTo("list");
        await Assert.That(actions).Contains("select 1");
        await Assert.That(actions.IndexOf("close 0")).IsEqualTo(actions.IndexOf("select 1") + 1);

        var (left, current) = tabs.Now;

        await Assert.That(left.Select(tab => tab.Url)).IsEquivalentTo([Reopened.Url, Unreachable.Url]);
        await Assert.That(left[current].Url).IsEqualTo(Reopened.Url);

        // And the answer says so, in the child's own lines.
        await Assert.That(text).Contains("this resume started the browser, and Firefox's session restore reopened 2 tabs. The blank page the browser starts on was closed");
        await Assert.That(text).Contains(new UpstreamTab(0, IsCurrent: true, Reopened.Title, Reopened.Url, Crashed: false).Line);
        await Assert.That(text).Contains($"{new UpstreamTab(1, IsCurrent: false, Unreachable.Title, Unreachable.Url, Crashed: false).Line} -- Firefox's error page: nothing answered at this address");
        await Assert.That(text).DoesNotContain($"({Reopened.Url}) -- Firefox's error page");
        await Assert.That(text).DoesNotContain(SessionManager.WhatTheFirstBrowserCallReopens);

        // Which tabs show the error page was read once, for every tab at once.
        await Assert.That(tabs.DocumentReads).IsEqualTo(1);

        // The resume made the session, so it is not told it is driving somebody else's.
        await Assert.That(text).DoesNotContain("this connection did not create");
    }

    /// <summary>
    /// A resumed Firefox session whose profile holds no session: the resume starts the
    /// browser, reads the tabs once, closes nothing and says there was nothing to reopen.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxResumeWithNothingToReopenReadsTheTabsOnceAndClosesNothing()
    {
        var tabs = new FakeFirefoxTabs([Reopened]);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(), tabs.DocumentRead());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-nothing", holdsASession: false);
        var text = TextOf(await ResumeAsync(rig, directory));

        await Assert.That(tabs.Actions).IsEquivalentTo(["list"]);
        await Assert.That(tabs.DocumentReads).IsEqualTo(0);
        await Assert.That(text).Contains("Firefox had no tabs of this session to reopen, so the browser shows one blank page.");
    }

    /// <summary>
    /// A resumed Firefox session whose browser does not start: the resume says what the
    /// browser server answered and what the next call does, and closes nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxResumeWhoseBrowserDoesNotStartSaysWhatTheBrowserServerAnswered()
    {
        var failing = new FakeToolBehaviour
        {
            RawResult = FakeFirefoxTabs.Error("TimeoutError: async initializeServer: Timeout 180000ms exceeded.\nCall log:\n  - <launching> firefox.exe"),
        };

        await using var sessions = await FirefoxRigAsync(failing);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-failed", holdsASession: true);
        var resumed = await ResumeAsync(rig, directory);
        var text = TextOf(resumed);

        // The session is open: the resume did what it is for, and its browser is the next call's to start.
        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(text);
        await Assert.That(text).Contains(
            "its browser did not start: TimeoutError: async initializeServer: Timeout 180000ms exceeded. The next browser call starts it again and acts on the blank page it starts on. "
            + "A tab Firefox reopens appears in browser_tabs once it is up, with the blank page beside it.");
        await Assert.That(sessions.SessionChildren[^1].ToolCallsReceived.Count(tool => tool == FirefoxRestore.TabsTool)).IsEqualTo(1);
    }

    /// <summary>
    /// A call that names the session while its resume is still starting the browser waits
    /// for the start, and reaches the child after the blank page has gone.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallMadeWhileTheResumeStartsTheBrowserWaitsForTheStart()
    {
        var tabs = new FakeFirefoxTabs([Reopened]);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(held.Task), tabs.DocumentRead());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-gate", holdsASession: true);
        var children = sessions.SessionChildren.Count;

        var resumeId = await rig.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, ResumeArguments(directory)));

        // The resume's first list is held inside the new child, so the start is running.
        await WaitUntilAsync(
            () => sessions.SessionChildren.Count > children && sessions.SessionChildren[^1].ToolCallsReceived.Contains(FirefoxRestore.TabsTool),
            "the resume never asked its new child for the tabs");

        var child = sessions.SessionChildren[^1];

        var navigateId = await rig.Client.BeginAsync("tools/call", Call("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite calling while the resume is still starting the browser",
            ["url"] = "data:text/html,<h1>after</h1>",
        }));

        await WaitUntilAsync(
            () => rig.Logs.Logged("waits for the session's resume to finish starting its browser") || child.ToolCallsReceived.Contains("browser_navigate"),
            "the call neither reached the child nor said it was waiting");

        await Assert.That(child.ToolCallsReceived).DoesNotContain("browser_navigate")
            .Because("the call reached the child while the resume was still starting the browser");

        held.SetResult();

        // Both answers at once: the call goes on as soon as the start ends, so its answer
        // can come back before the resume's, which is written after the start.
        var answers = await rig.Client.AwaitManyAsync([resumeId, navigateId]);
        var resumed = answers.Single(answer => answer.Id == resumeId).Envelope;
        var navigated = answers.Single(answer => answer.Id == navigateId).Envelope;

        await Assert.That((bool?)resumed["result"]?["isError"]).IsNotEqualTo(true).Because(resumed.ToJsonString());
        await Assert.That(navigated["error"]).IsNull().Because(navigated.ToJsonString());
        await Assert.That((bool?)navigated["result"]?["isError"]).IsNotEqualTo(true).Because(navigated.ToJsonString());

        var calls = child.ToolCallsReceived.ToList();

        await Assert.That(calls.IndexOf("browser_navigate")).IsGreaterThan(calls.LastIndexOf(FirefoxRestore.TabsTool));
        await Assert.That(tabs.Actions).Contains("close 0");
    }

    /// <summary>
    /// The agent's own close made while the resume is still starting the browser waits for
    /// the start, so the start's last list is not sent after the close, and the browser the
    /// close shuts is the one the start left.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAgentsCloseMadeWhileTheResumeStartsTheBrowserWaitsForTheStart()
    {
        var tabs = new FakeFirefoxTabs([Reopened]);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(held.Task), tabs.DocumentRead());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-close-gate", holdsASession: true);
        var children = sessions.SessionChildren.Count;

        var resumeId = await rig.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, ResumeArguments(directory)));

        await WaitUntilAsync(
            () => sessions.SessionChildren.Count > children && sessions.SessionChildren[^1].ToolCallsReceived.Contains(FirefoxRestore.TabsTool),
            "the resume never asked its new child for the tabs");

        var child = sessions.SessionChildren[^1];

        var closeId = await rig.Client.BeginAsync("tools/call", Call(SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing while the resume is still starting the browser",
        }));

        await WaitUntilAsync(
            () => rig.Logs.Records.Any(record => record.Message.StartsWith($"'{SessionToolSurface.Close}' ", StringComparison.Ordinal)
                    && record.Message.Contains("waits for the session's resume to finish starting its browser", StringComparison.Ordinal))
                || child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool),
            "the close neither reached the child nor said it was waiting");

        await Assert.That(child.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool)
            .Because("the close reached the child while the resume was still starting the browser");

        held.SetResult();

        var answers = await rig.Client.AwaitManyAsync([resumeId, closeId]);
        var resumed = answers.Single(answer => answer.Id == resumeId).Envelope;
        var closed = answers.Single(answer => answer.Id == closeId).Envelope;

        await Assert.That((bool?)resumed["result"]?["isError"]).IsNotEqualTo(true).Because(resumed.ToJsonString());
        await Assert.That((bool?)closed["result"]?["isError"]).IsNotEqualTo(true).Because(closed.ToJsonString());

        var calls = child.ToolCallsReceived.ToList();

        await Assert.That(calls.IndexOf(LiveSession.BrowserCloseTool)).IsGreaterThan(calls.LastIndexOf(FirefoxRestore.TabsTool));
        await Assert.That(tabs.Actions).Contains("close 0");
    }

    /// <summary>
    /// A resumed Firefox session whose blank page is not the first tab the child lists:
    /// nothing is closed, and the answer says so without counting the tabs Firefox reopened.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-13 with the texts of 161.</b> The blank page was the first tab in 30 of
    /// 30 resumes of the rig of that day, so this branch was never measured; it is also the
    /// answer when the <c>select</c> or the <c>close</c> fails.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxResumeWhoseBlankPageIsNotListedFirstClosesNothingAndSaysSo()
    {
        var tabs = new FakeFirefoxTabs([Reopened], blankPageFirst: false);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(), tabs.DocumentRead());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-blank-last", holdsASession: true);
        var text = TextOf(await ResumeAsync(rig, directory));

        await Assert.That(tabs.Actions).DoesNotContain("close 0").Because(text);
        await Assert.That(tabs.Now.Tabs.Count).IsEqualTo(2);
        await Assert.That(text).Contains("this resume started the browser, and Firefox's session restore reopened tabs of the session's last run. Nothing was closed, so the tabs are:\n");
        await Assert.That(text).Contains(new UpstreamTab(0, IsCurrent: false, Reopened.Title, Reopened.Url, Crashed: false).Line);
        await Assert.That(text).DoesNotContain("reopened 2 tabs");
    }

    /// <summary>
    /// Only Firefox's network error page for an address that gave no answer marks a tab: a
    /// connection refused, a timeout, a reset, an interruption, or a name that did not
    /// resolve. Firefox shows the same page for answers it could not use, and those tabs are
    /// listed without the mark.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-13 with the texts of 161</b>, on the reader's fact 2 of that day: the
    /// sentence the mark adds says nothing answered, which is false for a redirect loop, a
    /// server error or a failed TLS handshake, and the codes are read from the page's
    /// <c>e=</c>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OnlyTheErrorPageOfAnAddressThatGaveNoAnswerMarksATab()
    {
        static UpstreamTab tabAt(int index) => new(index, index is 0, "a title in any language", $"http://127.0.0.1:{index + 1}/", Crashed: false);

        string[] codes = ["connectionFailure", "netTimeout", "netReset", "netInterrupt", "dnsNotFound", "redirectLoop", "serverError", "nssFailure2", "netOffline"];

        var tabs = codes.Select((_, index) => tabAt(index)).ToList();
        List<(string Url, string? DocumentUri)> pages =
            [.. codes.Select((code, index) => ($"http://127.0.0.1:{index + 1}/", $"about:neterror?e={code}&u=http%3A//127.0.0.1%3A{index + 1}/&c=UTF-8"))];

        await Assert.That(FirefoxRestore.Match(tabs, pages)).IsEquivalentTo([0, 1, 2, 3, 4]);

        // An error page that names no code marks nothing, and nor does a certificate error.
        await Assert.That(FirefoxRestore.Match([tabAt(0)], [("http://127.0.0.1:1/", "about:neterror?u=http%3A//127.0.0.1%3A1/")])).IsEmpty();
        await Assert.That(FirefoxRestore.Match([tabAt(0)], [("http://127.0.0.1:1/", "about:certerror?e=nssBadCert&u=http%3A//127.0.0.1%3A1/")])).IsEmpty();
    }

    /// <summary>
    /// A resumed Firefox session whose profile holds a session and whose tabs never come up
    /// within the wait: the resume says so after the wait, and the blank page stays as the
    /// only tab.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxResumeWhoseTabsNeverComeUpSaysSoAfterTheWait()
    {
        var clock = new ManualClock();
        var tabs = new FakeFirefoxTabs([Reopened], listsBeforeTheRestore: int.MaxValue);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(), tabs.DocumentRead(), clock);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-never", holdsASession: true);
        var resumeId = await rig.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, ResumeArguments(directory)));
        var resumed = rig.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        // The wait runs on the session's clock, so the suite moves it a poll at a time.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(SessionTimes.FirefoxRestorePoll);
                return resumed.IsCompleted;
            },
            "the resume never answered, however far the clock was moved");

        var text = TextOf((await resumed).Result);

        await Assert.That(text).Contains($"no tab of the session's last run had come up after {SessionErrors.Duration(SessionTimes.FirefoxRestoreWait)}");
        await Assert.That(tabs.Actions).DoesNotContain("close 0");
        await Assert.That(tabs.Now.Tabs.Count).IsEqualTo(1);
        await Assert.That(tabs.DocumentReads).IsEqualTo(0);
    }

    /// <summary>
    /// Against a <b>real</b> Firefox: a session resumed after the server of its last page has
    /// ended, the page served with <c>Cache-Control: no-store</c>, starts, and its resume
    /// names the tab Firefox could not reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The hazard row of 2026-10-12 this closes</b>: before 161 the first browser call
    /// after such a resume waited out Playwright's 180 s and failed, 14 of 14 on 2026-10-13
    /// (kb, the timings article), because the restored tab was the launch's first page and
    /// Firefox's error page never reports <c>load</c>.
    /// </para>
    /// <para>
    /// <b>No duration is asserted</b>: a launch that waits out its timeout fails the call,
    /// and the call's own outcome is what is held.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumedFirefoxSessionWhosePageCannotBeReachedStartsAndSaysSo()
    {
        await SuiteEnvironment.RequireProvisionedFirefoxAsync();

        await using var site = NoStoreSite.Start();

        {
            await using var sessions = await RigSessionEnvironment.CreateAsync(opensDefaultSession: false, realSessionChildren: true);
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            var directory = Path.Combine(sessions.Root, "restore-unreachable");

            var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = "the Firefox session whose page's server ends before the resume",
                ["browser"] = ProvisionedBrowsers.Firefox,
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            });

            await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

            var navigated = await CallAsync(rig, "browser_navigate", new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite opening the page whose server ends",
                ["url"] = site.Url,
            });

            await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true).Because(TextOf(navigated));

            var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite closing the browser so the resume restores the page",
            });

            await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));

            // The server ends, so the page can neither load nor come back from the cache.
            await site.StopAsync();

            var resumed = await ResumeAsync(rig, directory);
            var text = TextOf(resumed);

            await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(text);
            await Assert.That(text).Contains("this resume started the browser, and Firefox's session restore reopened 1 tab");
            await Assert.That(text).Contains($"{site.Url}) -- Firefox's error page");

            // The first browser call is answered, on the reopened tab and not on a blank page.
            var snapshot = await CallAsync(rig, "browser_snapshot", new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite reading the tab the restore reopened",
            });

            await Assert.That((bool?)snapshot["isError"]).IsNotEqualTo(true).Because(TextOf(snapshot));

            var listed = TextOf(await CallAsync(rig, "browser_tabs", new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite reading which tabs are open",
                ["action"] = "list",
            }));

            await Assert.That(listed).Contains(site.Url);
            await Assert.That(listed).DoesNotContain("about:blank");
        }
    }

    /// <summary>
    /// A rig whose session children answer <c>browser_tabs</c> with one behaviour and the read
    /// of every tab's document URI with another, Firefox installed.
    /// </summary>
    /// <param name="tabs">The tab tool's behaviour.</param>
    /// <param name="documents">The run-code tool's behaviour, or <see langword="null"/> for none.</param>
    /// <param name="clock">The session's clock, or <see langword="null"/> for the system's.</param>
    /// <returns>The rig.</returns>
    private static async Task<RigSessionEnvironment> FirefoxRigAsync(FakeToolBehaviour tabs, FakeToolBehaviour? documents = null, ManualClock? clock = null)
    {
        var sessions = await RigSessionEnvironment.CreateAsync(
            child =>
            {
                child.Tools[FirefoxRestore.TabsTool] = tabs;
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();

                // A browser call an arm makes once the start has ended.
                child.Tools["browser_navigate"] = new FakeToolBehaviour();

                if (documents is not null)
                {
                    child.Tools[FirefoxRestore.RunCodeTool] = documents;
                }
            },
            opensDefaultSession: false,
            clock: clock);

        // Seeded complete, so the session's browser counts as installed and nothing starts a
        // download this rig has no installer for.
        await InstallationMarker.WriteAsync(Path.Combine(sessions.Environment.Paths.BrowsersDirectory, $"firefox-{BrowserAiPaths.FirefoxRevision}"));

        return sessions;
    }

    /// <summary>
    /// Creates a Firefox session, closes it with nothing up, and leaves a session file in its
    /// profile when asked, so the resume that follows expects a restore.
    /// </summary>
    /// <param name="sessions">The rig.</param>
    /// <param name="rig">The harness.</param>
    /// <param name="name">The session's folder name.</param>
    /// <param name="holdsASession">Whether the profile holds a session file.</param>
    /// <returns>The session directory.</returns>
    private static async Task<string> ClosedFirefoxSessionAsync(RigSessionEnvironment sessions, McpTestHarness rig, string name, bool holdsASession)
    {
        var directory = Path.Combine(sessions.Root, name);

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the Firefox session a resume starts the browser of",
            ["browser"] = ProvisionedBrowsers.Firefox,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the session so a resume opens it again",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));

        if (holdsASession)
        {
            // The double has no Firefox, so the file only has to be there: the resume asks
            // whether it exists and never reads it.
            await File.WriteAllBytesAsync(Path.Combine(directory, SessionLayout.ProfileFolderName, FirefoxRestore.SessionFiles[0]), "mozLz40\0"u8.ToArray());
        }

        return directory;
    }

    private static JsonObject ResumeArguments(string directory) => new()
    {
        ["directory"] = directory,
        ["why"] = "the suite resuming the Firefox session",
        ["headed"] = false,
        ["transcript"] = false,
        ["captureNetwork"] = false,
        ["idleMinutes"] = 10,
    };

    private static Task<JsonObject> ResumeAsync(McpTestHarness rig, string directory) =>
        CallAsync(rig, SessionToolSurface.Resume, ResumeArguments(directory));

    private static JsonObject Call(string tool, JsonObject arguments) => new()
    {
        ["name"] = tool,
        ["arguments"] = arguments,
    };

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", Call(tool, arguments));

    private static string TextOf(JsonObject? result) =>
        string.Concat((result?["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    /// <summary>Waits for an event, bounded by the suite's hang detector and by no number written here.</summary>
    /// <param name="condition">The event.</param>
    /// <param name="whatWentWrong">What it means when it never comes.</param>
    /// <returns>A task that completes once the event has happened.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}

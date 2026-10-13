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
/// </remarks>
internal sealed class FirefoxRestoreTests
{
    /// <summary>A page a restore reopens, as the double lists it.</summary>
    private static readonly (string Title, string Url) Reopened = ("Reopened page", "http://127.0.0.1:54321/reopened?tab=1");

    /// <summary>A page whose server had ended, as the double lists it once Firefox's error page is up.</summary>
    private static readonly (string Title, string Url) Unreachable = (FirefoxRestore.ErrorPageTitle, "http://127.0.0.1:54322/gone?tab=1");

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
        static List<UpstreamTab> Tabs(params string[] urls) =>
            [.. urls.Select((url, index) => new UpstreamTab(index, index is 0, string.Empty, url, Crashed: false))];

        await Assert.That(FirefoxRestore.TheBlankPageCanGo(Tabs("about:blank"), out _)).IsFalse();
        await Assert.That(FirefoxRestore.TheBlankPageCanGo(Tabs("http://127.0.0.1:1/", "about:blank"), out _)).IsFalse();

        await Assert.That(FirefoxRestore.TheBlankPageCanGo(Tabs("about:blank", "http://127.0.0.1:1/"), out var one)).IsTrue();
        await Assert.That(one).IsEqualTo(1);

        // A blank tab the last run left open is reopened too, and is passed over for a page.
        await Assert.That(FirefoxRestore.TheBlankPageCanGo(Tabs("about:blank", "about:blank", "http://127.0.0.1:1/"), out var two)).IsTrue();
        await Assert.That(two).IsEqualTo(2);

        await Assert.That(FirefoxRestore.TheBlankPageCanGo(Tabs("about:blank", "about:blank"), out var blank)).IsTrue();
        await Assert.That(blank).IsEqualTo(1);
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
        var tabs = new FakeFirefoxTabs([Reopened, Unreachable]);

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-reopened", holdsASession: true);
        var resumed = await ResumeAsync(rig, directory);
        var text = TextOf(resumed);

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(text);

        // The child was asked to start the browser, to list until the reopened tabs were up,
        // to make the first reopened tab current and to close the blank page, in that order.
        var actions = tabs.Actions;

        await Assert.That(actions[0]).IsEqualTo("list");
        await Assert.That(actions).Contains("select 1");
        await Assert.That(actions.IndexOf("close 0")).IsEqualTo(actions.IndexOf("select 1") + 1);

        var (left, current) = tabs.Now;

        await Assert.That(left.Select(tab => tab.Url)).IsEquivalentTo(new[] { Reopened.Url, Unreachable.Url });
        await Assert.That(left[current].Url).IsEqualTo(Reopened.Url);

        // And the answer says so, in the child's own lines.
        await Assert.That(text).Contains("this resume started the browser, and Firefox's session restore reopened 2 tabs. The blank page the browser starts on was closed");
        await Assert.That(text).Contains($"- 0: (current) [{Reopened.Title}]({Reopened.Url})");
        await Assert.That(text).Contains($"- 1: [{Unreachable.Title}]({Unreachable.Url}) -- Firefox's error page: nothing answered at this address");
        await Assert.That(text).DoesNotContain(SessionManager.WhatTheFirstBrowserCallReopens);

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

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await ClosedFirefoxSessionAsync(sessions, rig, "restore-nothing", holdsASession: false);
        var text = TextOf(await ResumeAsync(rig, directory));

        await Assert.That(tabs.Actions).IsEquivalentTo(new[] { "list" });
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
        await Assert.That(text).Contains("its browser did not start: TimeoutError: async initializeServer: Timeout 180000ms exceeded. The next browser call starts it again.");
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

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(held.Task));
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

        var resumed = await rig.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);
        var navigated = await rig.Client.AwaitAsync(navigateId, "browser_navigate");

        await Assert.That((bool?)resumed.Result?["isError"]).IsNotEqualTo(true).Because(TextOf(resumed.Result));
        await Assert.That((bool?)navigated.Result?["isError"]).IsNotEqualTo(true).Because(TextOf(navigated.Result));

        var calls = child.ToolCallsReceived.ToList();

        await Assert.That(calls.IndexOf("browser_navigate")).IsGreaterThan(calls.LastIndexOf(FirefoxRestore.TabsTool));
        await Assert.That(tabs.Actions).Contains("close 0");
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

        await using var sessions = await FirefoxRigAsync(tabs.Behaviour(), clock);
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

        var site = NoStoreSite.Start();
        var stopped = false;

        try
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
            await site.DisposeAsync();
            stopped = true;

            var resumed = await ResumeAsync(rig, directory);
            var text = TextOf(resumed);

            await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(text);
            await Assert.That(text).Contains("this resume started the browser, and Firefox's session restore reopened 1 tab");
            await Assert.That(text).Contains($"]({site.Url}) -- Firefox's error page");

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
        finally
        {
            if (!stopped)
            {
                await site.DisposeAsync();
            }
        }
    }

    /// <summary>A rig whose session children answer <c>browser_tabs</c> with one behaviour, Firefox installed.</summary>
    /// <param name="tabs">The behaviour.</param>
    /// <param name="clock">The session's clock, or <see langword="null"/> for the system's.</param>
    /// <returns>The rig.</returns>
    private static async Task<RigSessionEnvironment> FirefoxRigAsync(FakeToolBehaviour tabs, ManualClock? clock = null)
    {
        var sessions = await RigSessionEnvironment.CreateAsync(
            child =>
            {
                child.Tools[FirefoxRestore.TabsTool] = tabs;
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
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

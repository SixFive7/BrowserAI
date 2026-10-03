// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The page the browser tab shows, end to end through its own listener: what it
/// says, what its buttons do, which tab wins, and when the coordinator behind it
/// may stop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q315 a, the maintainer's words verbatim: <i>"Q315 a"</i></b>: a tab in the
/// person's own browser replaces the configuration window. Every arm here drives
/// the product's <see cref="PageService"/> over raw HTTP on <c>127.0.0.1</c>, the way
/// the page's own script does, with the update machinery, the running servers and
/// the desktop replaced by stand-ins, so nothing reaches the network, a server or
/// the screen.
/// </para>
/// <para>
/// <b>The clock is a <see cref="ManualClock"/></b> wherever a minute matters, so
/// Q336 a's linger is moved by the arm and never waited out.
/// </para>
/// </remarks>
internal sealed class PageServiceTests
{
    /// <summary>The instant every rig's clock starts at, so a time the arm writes and a time the page reads are on one clock.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// The status page says which BrowserAI this is and where everything is, links
    /// out only into a new tab with no referrer, and carries no inline script or
    /// style for the content security policy to have to stop.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusPageSaysWhatIsInstalledAndWhereThingsAreAndCarriesNothingInline()
    {
        using var rig = new PageRig();

        var page = await PageRig.GetAsync(rig.HandOut());

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Header("Content-Type")).IsEqualTo("text/html; charset=utf-8");
        await Assert.That(page.Body).Contains("<h1>BrowserAI 9.0.0</h1>");
        await Assert.That(page.Body).Contains(PageContent.Text(rig.Facts.InstallRoot));
        await Assert.That(page.Body).Contains(PageContent.Text(rig.Facts.DataRoot));
        await Assert.That(page.Body).Contains(PageContent.Text(rig.Facts.LogDirectory));
        await Assert.That(page.Body).Contains(PageContent.Text(rig.Facts.ServerCommand));
        await Assert.That(page.Body).Contains($"href=\"{PageContent.GuideUrl}\" target=\"_blank\" rel=\"noopener noreferrer\"");
        await Assert.That(page.Body).Contains("<script src=\"page.js\" defer></script>");
        await Assert.That(page.Body).Contains("data-tab=\"1\"");

        // Nothing for the policy to have to refuse: one script element and it has a
        // source, no style attribute, and no inline handler.
        await Assert.That(page.Body.Split("<script").Length).IsEqualTo(2);
        await Assert.That(page.Body).DoesNotContain(" style=");
        await Assert.That(page.Body).DoesNotContain(" onclick=");
        await Assert.That(page.Body).DoesNotContain(" onerror=");

        var script = await PageRig.GetAsync(rig.Root + "page.js");

        await Assert.That(script.Status).IsEqualTo(200);
        await Assert.That(script.Header("Content-Type")).IsEqualTo("text/javascript; charset=utf-8");
        await Assert.That(script.Body).Contains("new EventSource('events?tab='");

        var style = await PageRig.GetAsync(rig.Root + "page.css");

        await Assert.That(style.Status).IsEqualTo(200);
        await Assert.That(style.Header("Content-Type")).IsEqualTo("text/css; charset=utf-8");
    }

    /// <summary>
    /// A check whose answer is an older version says so (Q308 a), one whose answer
    /// is newer offers it, and one that fails is one plain sentence with the raw text
    /// under <i>Show details</i> (Q309 b).
    /// </summary>
    /// <remarks>
    /// <b>Q308 a and Q309 b, the maintainer's words verbatim: <i>"Q308 a"</i> and
    /// <i>"Q309 b"</i>.</b> The window showed an older version exactly like an
    /// upgrade and printed a failure's raw text as its whole sentence; the page does
    /// neither.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACheckSaysWhenTheOfferedVersionIsOlderAndAFailureIsOneSentenceWithTheRawTextUnderDetails()
    {
        using var rig = new PageRig();

        rig.Updates.Check = _ => Task.FromResult<UpdateCandidate?>(Candidate("8.5.0", older: true));

        using var stream = await rig.StreamAsync(rig.HandOut(), 1);

        await rig.ActAsync("""{"action":"check-updates"}""");

        var older = await StateContainingAsync(stream, "8.5.0 is on offer, and it is older than the installed 9.0.0");

        await Assert.That(older).IsNotNull();
        await Assert.That(older!).Contains("Install BrowserAI 8.5.0 now");

        rig.Updates.Check = _ => Task.FromResult<UpdateCandidate?>(Candidate("9.1.0"));

        await rig.ActAsync("""{"action":"check-updates"}""");

        var newer = await StateContainingAsync(stream, "BrowserAI 9.1.0 is available.");

        await Assert.That(newer).IsNotNull();
        await Assert.That(newer!).DoesNotContain("older than the installed");

        rig.Updates.Check = _ => Task.FromException<UpdateCandidate?>(new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable)."));

        await rig.ActAsync("""{"action":"check-updates"}""");

        var failed = await StateContainingAsync(stream, "The update check did not finish.");

        await Assert.That(failed).IsNotNull();
        await Assert.That(failed!).Contains("<details><summary>Show details</summary><pre>Response status code does not indicate success: 503 (Service Unavailable).</pre></details>");
    }

    /// <summary>
    /// A feed that is a folder with no release list in it is not read as up to date,
    /// and a check that does not come back can be given up, its late answer dropped.
    /// </summary>
    /// <remarks>
    /// <b>Two of the three states the 2026-09-24 rendering of the window found</b>:
    /// a feed folder with no manifest read as up to date, and a hung check showed
    /// <i>Checking</i> with no way out.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderFeedWithNoReleaseListIsNotUpToDateAndAHungCheckCanBeGivenUp()
    {
        using var logs = new CapturingLoggerProvider();
        using var rig = new PageRig(logs);

        rig.Updates.Missing = @"C:\feed\releases.win.json";

        using var stream = await rig.StreamAsync(rig.HandOut(), 1);

        await rig.ActAsync("""{"action":"check-updates"}""");

        var missing = await StateContainingAsync(stream, "no list of releases in it");

        await Assert.That(missing).IsNotNull();
        await Assert.That(missing!).Contains("No file at C:\\feed\\releases.win.json");
        await Assert.That(missing!).DoesNotContain("9.0.0 is up to date");

        // A check that never comes back on its own.
        var hung = new TaskCompletionSource<UpdateCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);

        rig.Updates.Missing = null;
        rig.Updates.Check = _ => hung.Task;

        await rig.ActAsync("""{"action":"check-updates"}""");

        var checking = await StateContainingAsync(stream, "Asking the release feed");

        await Assert.That(checking).IsNotNull();
        await Assert.That(checking!).Contains("data-action=\"stop-check\"");

        await rig.ActAsync("""{"action":"stop-check"}""");

        var stopped = await StateContainingAsync(stream, "has not asked the release feed");

        await Assert.That(stopped).IsNotNull();
        await Assert.That(stopped!).Contains("data-action=\"check-updates\"");

        // The answer arrives after all, and nobody is waiting for it.
        hung.SetResult(Candidate("9.9.9"));

        await Assert.That(await WaitForAsync(() => logs.Records.Any(record => record.EventId.Id is 7013))).IsTrue();

        var page = await PageRig.GetAsync(rig.Root + "?tab=1");

        await Assert.That(page.Body).DoesNotContain("9.9.9");
    }

    /// <summary>
    /// A package a server has staged is offered with a link that installs it; the
    /// install asks every running server to stop first, hands over, tells the tab,
    /// and asks the coordinator to exit; a version no longer on offer installs
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <b>Q310 a, the maintainer's words verbatim: <i>"Q310 a - the install link
    /// triggering the (download and) install. Not navigating to the release page I
    /// assume?"</i></b> The tab is told before the coordinator exits, and after the
    /// restart there is always a new tab (Q338 b), which is the restart's own start.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStagedUpdateIsInstalledFromItsLinkAfterEveryServerIsAskedToStop()
    {
        using var rig = new PageRig();

        rig.Updates.StagedCandidate = Candidate("9.2.0");
        rig.Sessions.Snapshot = new SessionsSnapshot(Now, [Server(101, "claude-code", Now.AddMinutes(-30)), Server(102, "codex-mcp-client", null)], []);

        var page = await PageRig.GetAsync(rig.HandOut());

        await Assert.That(page.Body).Contains("BrowserAI 9.2.0 is downloaded and ready to install.");
        await Assert.That(page.Body).Contains("data-action=\"install-update\" data-version=\"9.2.0\"");
        await Assert.That(page.Body).Contains(PageContent.Text(PageContent.InstallWarning));

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1);

        // A version that is not on offer installs nothing and says so.
        await rig.ActAsync("""{"action":"install-update","version":"9.3.0"}""");

        await Assert.That(await StateContainingAsync(stream, "That version is no longer on offer")).IsNotNull();
        await Assert.That(rig.Updates.Installed.IsEmpty).IsTrue();

        await rig.ActAsync("""{"action":"install-update","version":"9.2.0"}""");

        var closing = await stream.NextNamedAsync(PageEvents.Closing);

        await Assert.That(closing).IsNotNull();
        await Assert.That(closing!.Member("sentence")).Contains("BrowserAI is installing 9.2.0");
        await Assert.That(await stream.EndAsync()).IsTrue();

        await Assert.That(rig.Updates.Installed.Select(candidate => candidate.Version).ToArray()).IsEquivalentTo(["9.2.0"]);
        await Assert.That(rig.Sessions.Closed.ToArray()).IsEquivalentTo(["101-1", "102-1"]);
        await Assert.That(rig.Page.IsExitRequested()).IsTrue();
        await Assert.That(rig.Page.HandOut(PageKind.Status)).IsNull();
    }

    /// <summary>
    /// The sessions page lists every server with its client, its sessions and both
    /// warnings where they apply, encodes what a model wrote, and closes the servers
    /// that were selected and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>Q254 and Q317 c.</b> The two warnings are the <i>informs you of the
    /// dangers</i> half of the maintainer's instruction: a Codex-hosted server does
    /// not come back in the same thread, and a server that answered a call within the
    /// browser-idle period, or is answering one, may be in the middle of a task.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSessionsPageListsEveryServerWithItsWarningsAndClosesOnlyTheSelected()
    {
        using var rig = new PageRig();

        var claude = Server(201, "claude-code", Now.AddMinutes(-3), purpose: "<img src=x onerror=alert(1)> checks the shop");
        var codex = Server(202, "codex-mcp-client", Now.AddHours(-2));
        var unnamed = Server(203, null, null, inFlight: 1);

        rig.Sessions.Snapshot = new SessionsSnapshot(Now, [claude, codex, unnamed], ["9999-x.live: the pipe did not answer"]);

        var page = await PageRig.GetAsync(rig.HandOut(PageKind.Sessions));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("data-page=\"sessions\"");
        await Assert.That(page.Body).Contains("value=\"201-1\"");
        await Assert.That(page.Body).Contains("value=\"202-1\"");
        await Assert.That(page.Body).Contains("value=\"203-1\"");
        await Assert.That(page.Body).Contains("Claude Code 2.1.288, pid 201");
        await Assert.That(page.Body).Contains("A client that has not said what it is, pid 203");
        await Assert.That(page.Body).Contains("Last call 3 minutes ago.");
        await Assert.That(page.Body).Contains("One call is running now.");
        await Assert.That(page.Body).Contains("One more server is running and did not answer");

        // The Codex warning once, for the Codex server; the recent one for the two that are busy.
        await Assert.That(page.Body.Split(PageContent.Text(PageContent.CodexWarning)).Length - 1).IsEqualTo(1);
        await Assert.That(page.Body.Split(PageContent.Text(PageContent.RecentWarning)).Length - 1).IsEqualTo(2);

        // What a model wrote is text.
        await Assert.That(page.Body).Contains("&lt;img src=x onerror=alert(1)&gt; checks the shop");
        await Assert.That(page.Body).DoesNotContain("<img src=x");

        using var stream = await rig.StreamAsync(rig.Gate.Root + "sessions", 1, "sessions");

        await rig.ActAsync("""{"action":"close-servers","servers":[]}""");
        await Assert.That(await StateContainingAsync(stream, "No server was selected")).IsNotNull();

        await rig.ActAsync("""{"action":"close-servers","servers":["201-1","203-1","not-a-server"]}""");

        await Assert.That(await StateContainingAsync(stream, "2 servers were asked to close.")).IsNotNull();
        await Assert.That(rig.Sessions.Closed.ToArray()).IsEquivalentTo(["201-1", "203-1"]);
    }

    /// <summary>
    /// A button names a folder, a session or a server by a name the page was given,
    /// never by a path: the three folders open where the facts say, a session opens
    /// its own directory, and a path sent in their place is a bare 404.
    /// </summary>
    /// <remarks>
    /// <b>Control 7 of the first report, split by the second</b>: no endpoint takes a
    /// command, and here no endpoint takes a path either, so a request the page did
    /// not compose can at worst open one of the folders BrowserAI already shows.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnActionNamesWhatItOpensByANameThePageWasGivenAndNeverByAPath()
    {
        using var rig = new PageRig();

        var server = Server(301, "claude-code", null);

        rig.Sessions.Snapshot = new SessionsSnapshot(Now, [server], []);

        _ = await PageRig.GetAsync(rig.HandOut(PageKind.Sessions));

        await Assert.That((await rig.ActAsync("""{"action":"open-folder","folder":"install"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync("""{"action":"open-folder","folder":"data"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync("""{"action":"open-folder","folder":"logs"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync($$"""{"action":"open-session","session":"{{server.Sessions[0].Id}}"}""")).Status).IsEqualTo(204);

        await Assert.That(rig.Host.Opened.ToArray()).IsEquivalentTo(
            [rig.Facts.InstallRoot!, rig.Facts.DataRoot, rig.Facts.LogDirectory, server.Sessions[0].Directory]);

        string[] refused =
        [
            """{"action":"open-folder","folder":"C:\\Windows"}""",
            """{"action":"open-session","session":"C:\\Windows"}""",
            """{"action":"open-session","session":"0000000000000000"}""",
            """{"action":"run","command":"calc.exe"}""",
            """{"folder":"install"}""",
            """not json""",
        ];

        foreach (var body in refused)
        {
            var answer = await rig.ActAsync(body);

            await Assert.That(answer.Status).IsEqualTo(404).Because(body);
            await Assert.That(answer.Body).IsEmpty();
        }

        await Assert.That(rig.Host.Opened.Count).IsEqualTo(4);
    }

    /// <summary>
    /// The newest tab wins: once a newer tab's stream arrives, the older one is told
    /// it was replaced and its stream ends, and the older tab reloading is told so at
    /// once; a reload of the newest keeps its place.
    /// </summary>
    /// <remarks>
    /// <b>Q337 a, the maintainer's words verbatim: <i>"Q337 a"</i></b>: a second Start
    /// Menu click opens a new tab and the older tab closes itself, because a page
    /// cannot bring an existing tab forward. The page's script answers the event with
    /// <c>window.close()</c>; this arm holds the coordinator's half.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNewestTabWinsAndAnOlderTabIsToldItWasReplaced()
    {
        using var rig = new PageRig();

        _ = rig.HandOut();

        using var first = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await first.NextNamedAsync(PageEvents.State)).IsNotNull();

        // A second Start Menu click: a new address, the same listener.
        var second = rig.HandOut();

        await Assert.That(second).IsEqualTo(rig.Gate.Root + "?tab=2");

        using var newer = await rig.StreamAsync(rig.Gate.Root, 2);

        await Assert.That(await newer.NextNamedAsync(PageEvents.State)).IsNotNull();
        await Assert.That(await first.NextNamedAsync(PageEvents.Superseded)).IsNotNull();
        await Assert.That(await first.EndAsync()).IsTrue();

        // The old tab reloading is told at once.
        using var reloaded = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That((await reloaded.NextAsync())?.Name).IsEqualTo(PageEvents.Superseded);
        await Assert.That(await reloaded.EndAsync()).IsTrue();

        // The newest reloading keeps its place.
        newer.Dispose();

        using var again = await rig.StreamAsync(rig.Gate.Root, 2);

        await Assert.That((await again.NextAsync())?.Name).IsEqualTo(PageEvents.State);
        await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();
    }

    /// <summary>
    /// The coordinator keeps running while a tab is connected, keeps running for a
    /// minute after the last one leaves so a reload keeps working, and stops once the
    /// minute is up; after that it hands out nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q336 a, the maintainer's words verbatim: <i>"Q336 a - also make sure that
    /// an exist only happens after 1 min. of a tab closed so a reload keeps working
    /// (because that does not take 1 min.)"</i></b>
    /// </para>
    /// <para>
    /// <b>The product's own loop and the product's own minute, on a clock the arm
    /// moves.</b> Each <i>still running</i> is a pass the arm forced and saw end in a
    /// wait, through the loop's one seam for that question, so no reading here is a
    /// wait for something that should not happen.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCoordinatorStopsAMinuteAfterTheLastTabLeavesAndAReloadKeepsItRunning()
    {
        using var root = ScratchDirectory.Create("page-linger");
        using var inbox = new CoordinatorInbox();
        using var rig = new PageRig(wake: inbox.Wake);
        using var waits = new SemaphoreSlim(0);

        var loop = new CoordinatorLoop(root.Path, inbox, NothingStaged.Instance, () => new RootScan([], []), rig.Page, NullLogger.Instance)
        {
            Waiting = () => waits.Release(),
        };

        _ = rig.HandOut();

        var run = RunOnItsOwnThread(loop);

        async Task stillRunningAsync()
        {
            while (await waits.WaitAsync(TimeSpan.Zero))
            {
            }

            inbox.Wake();

            await Assert.That(await waits.WaitAsync(TestDefaults.InProcessHang)).IsTrue();
            await Assert.That(run.IsCompleted).IsFalse();
        }

        await stillRunningAsync();

        // A tab connected, and five minutes pass.
        var tab = await rig.StreamAsync(rig.Gate.Root, 1);

        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        await stillRunningAsync();

        // It leaves, and 59 seconds pass.
        tab.Dispose();

        await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 0)).IsTrue();

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        await stillRunningAsync();

        // It reloads inside the minute and leaves again: the minute starts over.
        using (var reload = await rig.StreamAsync(rig.Gate.Root, 1))
        {
            await Assert.That(await reload.NextNamedAsync(PageEvents.State)).IsNotNull();
        }

        await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 0)).IsTrue();

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        await stillRunningAsync();

        // The minute runs out.
        rig.Clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.NothingPending);
        await Assert.That(rig.Page.IsServing).IsFalse();
        await Assert.That(rig.Page.HandOut(PageKind.Status)).IsNull();
    }

    /// <summary>
    /// An open tab does not hold an update back: with a package staged and nothing
    /// else running from the install, the tab is told to open BrowserAI again from the
    /// Start Menu, and the package is handed over.
    /// </summary>
    /// <remarks>
    /// <b>Q336 a's second half</b>, in the decision's words: <i>when one is staged and
    /// no server runs from the install, the tab is told, the update is applied, and
    /// the old tab says to reopen BrowserAI from the Start Menu</i>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOpenTabDoesNotHoldAnUpdateBackAndIsToldToReopenFromTheStartMenu()
    {
        using var root = ScratchDirectory.Create("page-apply");
        using var inbox = new CoordinatorInbox();
        using var rig = new PageRig(wake: inbox.Wake);

        var staged = new ScriptedStaged { Candidate = Candidate("9.3.0") };

        _ = rig.HandOut();

        using var tab = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await tab.NextNamedAsync(PageEvents.State)).IsNotNull();

        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, staged, () => new RootScan([], []), rig.Page, NullLogger.Instance));

        var closing = await tab.NextNamedAsync(PageEvents.Closing);

        await Assert.That(closing).IsNotNull();
        await Assert.That(closing!.Member("sentence")).Contains("Open BrowserAI from the Start Menu again");
        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.Applied);
        await Assert.That(staged.Applies).IsEqualTo(1);
    }

    /// <summary>
    /// A person's start opens the address it was handed with the shell, or writes it
    /// to the file it was asked to and opens nothing, and its record names the port
    /// and never the token.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStartOpensItsAddressOrWritesItAndNeverLogsTheToken()
    {
        using var scratch = ScratchDirectory.Create("page-opener");
        using var logs = new CapturingLoggerProvider();

        var logger = logs.CreateLogger("opener");
        var token = PageGate.NewToken();
        var address = $"http://127.0.0.1:49152/{token}/?tab=1";
        var opened = new List<string>();

        bool open(string url)
        {
            opened.Add(url);
            return true;
        }

        await Assert.That(PageOpener.Deliver(address, null, open, logger)).IsTrue();
        await Assert.That(opened).IsEquivalentTo([address]);

        var file = Path.Combine(scratch.Path, "address.txt");

        await Assert.That(PageOpener.Deliver(address, file, open, logger)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(file)).IsEqualTo(address);
        await Assert.That(opened.Count).IsEqualTo(1);

        await Assert.That(PageOpener.Deliver(null, null, open, logger)).IsFalse();
        await Assert.That(opened.Count).IsEqualTo(1);

        await Assert.That(PageOpener.WriteAddressFrom(["--write-address", file])).IsEqualTo(file);
        await Assert.That(PageOpener.WriteAddressFrom(["--write-address"])).IsNull();

        await Assert.That(logs.Records.Count).IsEqualTo(3);
        await Assert.That(logs.Records.Any(record => record.Message.Contains(token, StringComparison.Ordinal))).IsFalse();
        await Assert.That(logs.Logged("port 49152")).IsTrue();
    }

    /// <summary>Reads states off a stream until one carries the text.</summary>
    private static async Task<string?> StateContainingAsync(RawEventStream stream, string text)
    {
        while (await stream.NextNamedAsync(PageEvents.State) is { } state)
        {
            var html = state.Member("html");

            if (html.Contains(PageContent.Text(text), StringComparison.Ordinal) || html.Contains(text, StringComparison.Ordinal))
            {
                return html;
            }
        }

        return null;
    }

    /// <summary>Polls a condition until it holds or the hang detector runs out.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestDefaults.InProcessHang;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return true;
    }

    private static Task<CoordinatorEnd> RunOnItsOwnThread(CoordinatorLoop loop)
    {
        var completion = new TaskCompletionSource<CoordinatorEnd>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(loop.Run());
            }
#pragma warning disable CA1031 // Whatever the loop threw belongs to the awaiting arm, not to this thread.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                completion.SetException(failure);
            }
        })
        {
            IsBackground = true,
            Name = "coordinator loop under test",
        };

        thread.Start();
        return completion.Task;
    }

    private static UpdateCandidate Candidate(string version, bool older = false) => new()
    {
        Version = version,
        IsDowngrade = older,
        DeltaCount = 0,
        FullPackageSize = 1,
    };

    private static ServerEntry Server(int pid, string? client, DateTimeOffset? lastCall, string? purpose = "reads the docs", int inFlight = 0)
    {
        var directory = $@"C:\sessions\s{pid.ToString(CultureInfo.InvariantCulture)}";
        var description = new ServerDescription(
            ServerPipeProtocol.Version,
            pid,
            1,
            "9.0.0",
            @"C:\install\current\BrowserAI.Server.exe",
            ServerDescription.States.Serving,
            client is null ? null : new ClientIdentity(client, client is "claude-code" ? "Claude Code" : "Codex", client is "claude-code" ? "2.1.288" : "0.155.0"),
            @"C:\work",
            Now.AddHours(-3),
            lastCall,
            inFlight,
            [new HeldSession(directory, purpose, BrowserOpen: true)]);

        return CensusPageSessions.Entry($@"C:\install\live\{pid}-0.live", description, Now);
    }

    /// <summary>A staged update the arm controls.</summary>
    private sealed class ScriptedStaged : IStagedUpdates
    {
        public UpdateCandidate? Candidate { get; set; }

        public int Applies { get; private set; }

        public UpdateCandidate? Pending() => Candidate;

        public void ApplyAfterThisProcessExits(UpdateCandidate candidate) => Applies++;
    }
}

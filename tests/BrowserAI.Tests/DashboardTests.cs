// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Proxy;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// BrowserAI's page as the maintainer chose it on 2026-10-11: a home of cards, a page
/// behind each, the conversations as a live map or a list, and the three facts he let
/// the background pass to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>40 to 46, the maintainer's answer verbatim: <i>"40-46 your recommendations"</i></b>.
/// 40 a, F: direction A's home of cards, each leading to a page with a way back, and C's
/// map as the conversations page. 41 a: the page deletes nothing. 42 a: the mockups'
/// words. 43 b: three new facts, each session folder's size and the folders not open now,
/// where updates come from and when BrowserAI last checked, and when the background
/// started and how. 44 a: Windows' own light or dark. 45 a: pids and command lines folded
/// under the details for troubleshooting. 46 a: the conversations and sessions views
/// pushed on every change.
/// </para>
/// <para>
/// <b>Every arm drives the product's <see cref="PageService"/> over raw HTTP and raw event
/// streams</b>, with the background, the desktop and the measurement replaced by stand-ins
/// and the clock a <see cref="ManualClock"/>, so nothing reaches a browser or the screen.
/// What only a browser can show, the map's lines and the focus kept across a new state, is
/// <see cref="PageBrowserTests"/>'.
/// </para>
/// </remarks>
internal sealed class DashboardTests
{
    /// <summary>The instant every rig's clock starts at.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>The actions a page may post, and nothing among them removes anything.</summary>
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
    {
        "open-folder",
        "open-session",
        "measure-sessions",
        "install-now",
        "read-registration",
        "register",
        "unregister",
        "register-in-project",
        "unregister-from-project",
        "unregister-from-a-project",

        // The after-update notice's dismiss, 57 a, which takes away a notice and no file.
        "dismiss-notice",
    };

    /// <summary>
    /// Every address a start, a toast or <c>--sessions</c> hands out still opens, on the page
    /// that now holds what it held: the old status page's address is the home, the sessions
    /// page's is the conversations page, and the update and changelog pages keep theirs,
    /// whatever tab number the address carries.
    /// </summary>
    /// <remarks>
    /// <b>40 a, and the brief's constraint</b>: every address handed out before must land on
    /// the page that now holds what it held, <c>?tab=N</c> included.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryAddressHandedOutBeforeLandsOnThePageThatNowHoldsWhatItHeld()
    {
        await using var rig = await PageRig.CreateAsync();

        var home = await rig.HandOutAsync(PageKind.Status);
        var sessions = await rig.HandOutAsync(PageKind.Sessions);
        var update = await rig.HandOutAsync(PageKind.Update);
        var changelog = await rig.HandOutAsync(PageKind.Changelog);

        await Assert.That(home).IsEqualTo(rig.Root + "?tab=1");
        await Assert.That(sessions).IsEqualTo(rig.Root + "sessions?tab=2");
        await Assert.That(update).IsEqualTo(rig.Root + "update?tab=3");
        await Assert.That(changelog).IsEqualTo(rig.Root + "changelog?tab=4");

        var overview = await PageRig.GetAsync(home);

        await Assert.That(overview.Status).IsEqualTo(200).Because(overview.Raw);
        await Assert.That(overview.Body).Contains("<h1>All quiet</h1>");
        await Assert.That(overview.Body).Contains("BrowserAI is running. No conversation is using it right now.");

        foreach (var card in new[] { "Right now", "Update", "Claude Code and Codex", "Storage", "This install" })
        {
            await Assert.That(overview.Body).Contains("<h2>" + card + "</h2>").Because(card);
        }

        await Assert.That(overview.Body).Contains("<span class=\"version\">9.0.0</span>");

        var conversations = await PageRig.GetAsync(sessions);

        await Assert.That(conversations.Body).Contains("<h1 class=\"page-title\">Conversations and browsers</h1>");
        await Assert.That(conversations.Body).Contains(Crumbs(2));

        var updatePage = await PageRig.GetAsync(update);

        await Assert.That(updatePage.Body).Contains("<h1 class=\"page-title\">No update is waiting</h1>");
        await Assert.That(updatePage.Body).Contains(Crumbs(3));

        var whatIsNew = await PageRig.GetAsync(changelog);

        await Assert.That(whatIsNew.Body).Contains("<h1 class=\"page-title\">What is new</h1>");
        await Assert.That(whatIsNew.Body).Contains(Crumbs(4));

        // An address an older tab kept, with another number: the same home, and every link
        // in it keeps that number.
        var older = await PageRig.GetAsync(rig.Root + "?tab=7");

        await Assert.That(older.Body).Contains("data-tab=\"7\"");
        await Assert.That(older.Body).Contains("<h1>All quiet</h1>");
        await Assert.That(older.Body).Contains("href=\"sessions?tab=7\"");
        await Assert.That(older.Body).DoesNotContain("?tab=1\"");

        // And every page's name reads back as the page.
        foreach (var kind in Enum.GetValues<PageKind>())
        {
            await Assert.That(PageNames.Parse(PageNames.Of(kind))).IsEqualTo(kind);
        }
    }

    /// <summary>
    /// The page behind each card has a route of its own and begins with the way back to the
    /// home, and a route that is no page is the gate's bare 404.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-12 (previously also "a session's page is reached by the session's
    /// opaque name, a name nothing holds says so")</i>: the session page is dropped, and its
    /// address opens the conversations page with the session chosen, which
    /// <see cref="ConversationsMapTests.ALinkToASessionOpensTheMapWithThatSessionChosen"/> holds.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachPageBehindACardIsServedAtItsRouteAndBeginsWithAWayBack()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Busy();
        _ = await rig.HandOutAsync();

        (string Route, string Heading)[] pages =
        [
            ("setup?tab=1", "Claude Code and Codex"),
            ("storage?tab=1", "Storage"),
            ("install?tab=1", "This install"),
        ];

        foreach (var (route, heading) in pages)
        {
            var page = await PageRig.GetAsync(rig.Root + route);

            await Assert.That(page.Status).IsEqualTo(200).Because(route);
            await Assert.That(page.Body).Contains($"<h1 class=\"page-title\">{heading}</h1>").Because(route);
            await Assert.That(page.Body).Contains(Crumbs(1)).Because(route);
        }

        var nothing = await PageRig.GetAsync(rig.Root + "nonsense?tab=1");

        await Assert.That(nothing.Status).IsEqualTo(404);
        await Assert.That(nothing.Body).IsEmpty();
    }

    /// <summary>
    /// The storage page says how old its measurement is once it is older than the fresh
    /// window, and only then.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-12 for the maintainer's 80 c</b>, in his words verbatim:
    /// <i>"78-82 your recommendation"</i>, and his condition, <i>"say how old a size is when
    /// it is not fresh"</i>. The measurement's time is set against the page's own clock, so
    /// the arm waits for nothing to grow old.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStoragePageSaysHowOldItsMeasurementIsOnceItIsNotFresh()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Busy();
        rig.Folders.Folders = [new SessionFolder(CensusPageSessions.IdOf(CheckoutRounding), CheckoutRounding, 1024 * 1024)];
        rig.Folders.At = rig.Clock.GetUtcNow() - (BrowserAI.Sessions.SessionTimes.FolderSizesFreshFor * 5);

        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Storage));

        using var storage = await rig.StreamAsync(rig.Gate.Root, 1, "storage");

        var stale = await StateContainingAsync(storage, "min ago");

        await Assert.That(stale).IsNotNull();
        await Assert.That(stale!).Contains($"Measured at {PageWords.ClockToTheSecond(rig.Folders.At)}, 5 min ago.");

        // Measured again at the page's own now: the time, and no age.
        rig.Folders.At = rig.Clock.GetUtcNow();

        await Assert.That((await rig.ActAsync("""{"action":"measure-sessions"}""")).Status).IsEqualTo(204);

        var fresh = await StateContainingAsync(storage, $"Measured at {PageWords.ClockToTheSecond(rig.Folders.At)}.");

        await Assert.That(fresh).IsNotNull();
        await Assert.That(fresh!).DoesNotContain("min ago");
    }

    /// <summary>
    /// The storage page lists every session folder BrowserAI knows of with its size, largest
    /// first, and says which are open now and which are not; the home's card says the total
    /// and the two parts; and a folder not open now opens in Explorer by its opaque name.
    /// </summary>
    /// <remarks>
    /// <b>43 b, the first of the three facts</b>: each session folder's size, and the session
    /// folders not open now, which the background passes to the page.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHomeAndTheStoragePageSayEachSessionFoldersSizeAndWhichAreNotOpen()
    {
        const long MiB = 1024 * 1024;

        await using var rig = await PageRig.CreateAsync();

        const string Invoice = @"C:\Code\billing\.work\invoice-export";
        const string Search = @"C:\Code\acme-shop\.work\search-slow-network";

        rig.Sessions.Snapshot = Busy();
        rig.Folders.Folders =
        [
            new SessionFolder(CensusPageSessions.IdOf(CheckoutRounding), CheckoutRounding, 96 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(Invoice), Invoice, 214 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(Search), Search, 377 * MiB),
        ];

        // The page, which asks for the measurement, and then its stream, as a browser opens it.
        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Storage));

        using var storage = await rig.StreamAsync(rig.Gate.Root, 1, "storage");

        var html = await StateContainingAsync(storage, "377 MB");

        await Assert.That(html).IsNotNull();
        await Assert.That(html!).Contains("687 MB").Because("the three folders together");
        await Assert.That(html).Contains("Open now");
        await Assert.That(html).Contains("Not open now");

        // Largest first.
        var search = html.IndexOf("search-slow-network", StringComparison.Ordinal);
        var invoice = html.IndexOf("invoice-export", StringComparison.Ordinal);
        var checkout = html.IndexOf("checkout-rounding", StringComparison.Ordinal);

        await Assert.That(search).IsGreaterThan(-1);
        await Assert.That(search).IsLessThan(invoice);
        await Assert.That(invoice).IsLessThan(checkout);
        await Assert.That(html).Contains("data-action=\"open-session\" data-session=\"" + CensusPageSessions.IdOf(Invoice) + "\"");

        // A folder not open now opens by the name the page was given.
        var opened = await rig.ActAsync($$"""{"action":"open-session","session":"{{CensusPageSessions.IdOf(Invoice)}}"}""");

        await Assert.That(opened.Status).IsEqualTo(204);
        await Assert.That(rig.Host.Opened.ToArray()).Contains(Invoice);

        // And the home's card says the whole and its two parts.
        _ = await rig.HandOutAsync();

        using var home = await rig.StreamAsync(rig.Gate.Root, 2);

        var card = await StateContainingAsync(home, "2 folders not open now");

        await Assert.That(card).IsNotNull();
        await Assert.That(card!).Contains("687 MB");
        await Assert.That(card).Contains("1 folder open now");
        await Assert.That(card).Contains("in 3 session folders on this PC");
    }

    /// <summary>
    /// Where updates come from and when BrowserAI last checked are said on the home's card
    /// and on the install page: GitHub's releases, a folder on this PC with the folder named,
    /// and a source the install named and this build refused, which checks nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>43 b, the second of the three facts.</b></para>
    /// <para>
    /// <b>And the sentence that a newer version installs by itself is said only where BrowserAI
    /// checks</b>, since fact 2 of the reader pass of 2026-10-11: it was drawn on every installed
    /// home with nothing waiting, under the sentence saying BrowserAI does not check for a refused
    /// source or a development build's web address. Planted red that day against the home as it
    /// stood.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhereUpdatesComeFromAndTheLastCheckAreSaidWhereTheUpdateIs()
    {
        const string InstallsByItself = "When a newer version is out, BrowserAI downloads it and installs it once nothing uses BrowserAI.";
        var at = Now.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        await using (var rig = await PageRig.CreateAsync())
        {
            var home = await PageRig.GetAsync(await rig.HandOutAsync());

            await Assert.That(home.Body).Contains("No update is waiting");
            await Assert.That(home.Body).Contains($"BrowserAI last checked GitHub releases at {at}.");
            await Assert.That(home.Body).Contains(InstallsByItself);

            var install = await PageRig.GetAsync(rig.Root + "install?tab=1");

            await Assert.That(install.Body).Contains("from GitHub releases");
            await Assert.That(install.Body).Contains($"<dt>Last check</dt><dd>{at}</dd>");
        }

        var folder = new NothingHolds { Watching = new UpdateWatch(UpdateChecks.Checking, UpdateSourceKind.Folder, @"C:\BrowserAI-feed", null, Now) };

        await using (var rig = await PageRig.CreateAsync(holds: folder))
        {
            var home = await PageRig.GetAsync(await rig.HandOutAsync());

            await Assert.That(home.Body).Contains($"BrowserAI last checked its update folder at {at}.");

            var install = await PageRig.GetAsync(rig.Root + "install?tab=1");

            await Assert.That(install.Body).Contains("from a folder on this PC");
            await Assert.That(install.Body).Contains(PageContent.Text(@"C:\BrowserAI-feed"));
        }

        var refused = new NothingHolds { Watching = new UpdateWatch(UpdateChecks.SourceRefused, UpdateSourceKind.None, null, "'feed' is neither an absolute http or https URL nor a fully qualified folder path.", null) };

        await using (var rig = await PageRig.CreateAsync(holds: refused))
        {
            var home = await PageRig.GetAsync(await rig.HandOutAsync());

            await Assert.That(home.Body).Contains("BrowserAI does not check for updates, because the update source its install named was refused.");
            await Assert.That(home.Body).DoesNotContain("last checked");
            await Assert.That(home.Body).DoesNotContain(InstallsByItself).Because("a home that says BrowserAI does not check said a newer version installs by itself");
        }

        var development = new NothingHolds { Watching = new UpdateWatch(UpdateChecks.PreReleaseUrl, UpdateSourceKind.Url, "https://example.invalid/feed", null, null) };

        await using (var rig = await PageRig.CreateAsync(holds: development))
        {
            var home = await PageRig.GetAsync(await rig.HandOutAsync());

            await Assert.That(home.Body).Contains("This development build does not check for updates, because its install named a web address and it checks only a folder.");
            await Assert.That(home.Body).DoesNotContain(InstallsByItself).Because("a development build that checks nothing said a newer version installs by itself");
        }

        await using (var rig = await PageRig.CreateAsync(installed: false))
        {
            var home = await PageRig.GetAsync(await rig.HandOutAsync());

            await Assert.That(home.Body).Contains(PageContent.Text(PageContent.NotInstalledSentence));
        }
    }

    /// <summary>
    /// The home's update card says how long a conversation holds the update from the relay's
    /// own countdown, which the background hands the page, and counts every message to
    /// BrowserAI, where it said each call.
    /// </summary>
    /// <remarks>
    /// <b>Fact 6 of the reader pass of 2026-10-11</b>: the card said <i>10 min after each
    /// call</i>, a number written in a string where the numbers index's scan cannot see it, and
    /// a call where the countdown counts every message but a ping. A length the relay does not
    /// have shows the derivation: the card follows <see cref="PageFacts.RelayHold"/>. Planted
    /// red that day against the card as it stood. ⚠️ <i>Corrected 2026-10-12 (previously
    /// <c>TheHomeSaysHowLongAConversationHoldsTheUpdateFromTheRelaysCountdown</c>, which read
    /// the length on the home)</i>: the home counts what holds the update in one line since the
    /// side-column layout (61, 67), and the update page's caption, which wrote <i>ten
    /// minutes</i> in its string, says the length from the same countdown.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageSaysHowLongAConversationHoldsTheUpdateFromTheRelaysCountdownAndTheHomeCountsWhatHoldsIt()
    {
        await using var rig = await PageRig.CreateAsync(holds: new ScriptedHolds { Snapshot = Held() }, facts: facts => facts with { RelayHold = TimeSpan.FromMinutes(7) });

        var home = await PageRig.GetAsync(await rig.HandOutAsync());

        await Assert.That(home.Body).Contains("Held by 1 window, 1 hidden browser and 1 active conversation.");
        await Assert.That(home.Body).DoesNotContain("after each call");

        var update = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Update));

        await Assert.That(update.Body).Contains("Each holds the update for 7 min after its app last sent BrowserAI anything but a ping.");
        await Assert.That(update.Body).DoesNotContain("ten minutes");
    }

    /// <summary>
    /// A read of where the updates come from that fails is logged as that, under an event of
    /// its own, and never as a read of what holds the update.
    /// </summary>
    /// <remarks>
    /// <b>Fact 7 of the reader pass of 2026-10-11</b>: the page logged the failure through
    /// <c>PageServiceLog.HoldsUnread</c>, event 7014, <i>"What holds the update could not be read
    /// for the page."</i>, so the log named the wrong read. Planted red that day against the
    /// page as it stood.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWatchThatCannotBeReadIsLoggedAsItselfAndNotAsWhatHoldsTheUpdate()
    {
        using var logs = new CapturingLoggerProvider();
        await using var rig = await PageRig.CreateAsync(logs: logs, holds: new NothingHolds { WatchFailure = new InvalidOperationException("the update core is gone") });

        var home = await PageRig.GetAsync(await rig.HandOutAsync());

        await Assert.That(home.Status).IsEqualTo(200).Because(home.Raw);
        await Assert.That(home.Body).DoesNotContain("last checked");
        await Assert.That(logs.Records.Any(record => record.EventId.Id == 7017)).IsTrue().Because("the failed read of where updates come from was not logged under its own event");
        await Assert.That(logs.Logged("Where the updates come from could not be read for the page")).IsTrue();
        await Assert.That(logs.Logged("What holds the update could not be read for the page.")).IsFalse().Because("the failed read of where updates come from was logged as a read of what holds the update");
    }

    /// <summary>
    /// When the background started and how are said: its time in the header, and on the
    /// install page the time and the way, at sign-in or when the person opened BrowserAI.
    /// </summary>
    /// <remarks><b>43 b, the third of the three facts.</b></remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheBackgroundsStartIsSaidAndHowItStarted()
    {
        var at = Now.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        await using (var rig = await PageRig.CreateAsync())
        {
            var home = await PageRig.GetAsync(await rig.HandOutAsync());

            await Assert.That(home.Body).Contains($"Running since {at}");

            var install = await PageRig.GetAsync(rig.Root + "install?tab=1");

            await Assert.That(install.Body).Contains($"since {at}, started at sign-in");
        }

        await using (var rig = await PageRig.CreateAsync(facts: facts => facts with { StartedHow = BackgroundStart.Person }))
        {
            var install = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Install));

            await Assert.That(install.Body).Contains($"since {at}, started when you opened BrowserAI");
        }
    }

    /// <summary>How the background was started is read from what its task handed it.</summary>
    /// <param name="startedBy">What <c>--started-by</c> carried.</param>
    /// <param name="expected">How the page says it started.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("$(Arg0)", BackgroundStart.SignIn)]
    [Arguments(PersonStart.StartedByPerson, BackgroundStart.Person)]
    [Arguments(PersonStart.StartedByTheInstaller, BackgroundStart.Installer)]
    [Arguments(PersonStart.StartedAfterAnUpdate, BackgroundStart.AfterUpdate)]
    [Arguments(null, BackgroundStart.Unknown)]
    [Arguments("something-else", BackgroundStart.Unknown)]
    public async Task HowTheBackgroundStartedIsReadFromWhatItsTaskHandedIt(string? startedBy, BackgroundStart expected) =>
        await Assert.That(Program.BackgroundStartOf(startedBy)).IsEqualTo(expected);

    /// <summary>
    /// The conversations page and the home are sent a new state when a conversation
    /// connects, when a session opens and when its browser closes, read by the watch once a
    /// second; a second that changes nothing sends no changed view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>46 a</b>: the conversations and sessions views are live, pushed on every change the
    /// way the update page already is. The barrier is a change of another kind, a downloaded
    /// version: the first state after the quiet second must be the barrier's.
    /// </para>
    /// <para>
    /// <b>One tab at a time</b>, because the newest tab replaces every older one (Q337 a): the
    /// home is a tab of its own after the conversations page's, and is sent a change of its
    /// own.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheConversationsAndSessionsViewsArePushedOnEveryChange()
    {
        var holds = new ScriptedHolds();

        await using var rig = await PageRig.CreateAsync(holds: holds);

        // The session's folder weighs a mebibyte, so the state its measurement sends can be
        // told from the one before it.
        rig.Folders.Folders = [new SessionFolder(CensusPageSessions.IdOf(CheckoutRounding), CheckoutRounding, 1024 * 1024)];

        _ = await rig.HandOutAsync(PageKind.Sessions);

        using (var conversations = await rig.StreamAsync(rig.Gate.Root, 1, "sessions"))
        {
            await Assert.That(await StateContainingAsync(conversations, "No conversation is using BrowserAI")).IsNotNull();

            // A conversation connects with its sessions; a session opened, so its folder is
            // measured, and the state with its weight is the last this change sends.
            rig.Sessions.Snapshot = Busy();
            rig.Clock.Advance(PageService.HoldsWatchPeriod);

            var connected = await StateContainingAsync(conversations, "1 MB");

            await Assert.That(connected).IsNotNull();
            await Assert.That(connected!).Contains(PageContent.Text("\"Fix the <checkout> total\""));
            await Assert.That(connected).Contains("checkout-rounding");

            // A session's browser closes.
            rig.Sessions.Snapshot = Busy(checkoutOpen: false);
            rig.Clock.Advance(PageService.HoldsWatchPeriod);

            var closed = await StateContainingAsync(conversations, "No browser open. A call that names the session opens it again.");

            await Assert.That(closed).IsNotNull();

            // A second that changes nothing sends no change: every state before the barrier's
            // is the closed view again, byte for byte but for its live figures. ⚠️ Since
            // 2026-10-13 (previously "sends nothing: the next state is the barrier's"): one
            // change can send the same view more than once, the sessions' refresh and then the
            // folder measurement that change started, and under load the second of them landed
            // after the close's state, in the gate over 03ed35ca.
            rig.Clock.Advance(PageService.HoldsWatchPeriod);

            holds.Snapshot = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "9.9.9", [], [], []);
            rig.Clock.Advance(PageService.HoldsWatchPeriod);

            await Assert.That(await BarrierAfterTheSameViewAsync(conversations, closed!)).Contains("9.9.9");
        }

        // The home, in a tab of its own: every conversation ends, and the home says so.
        _ = await rig.HandOutAsync(PageKind.Status);

        using var home = await rig.StreamAsync(rig.Gate.Root, 2);

        await Assert.That(await StateContainingAsync(home, "3 conversations are using BrowserAI")).IsNotNull();

        rig.Sessions.Snapshot = SessionsSnapshot.Empty;
        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        await Assert.That(await StateContainingAsync(home, "All quiet")).IsNotNull();
    }

    /// <summary>
    /// No page offers to delete anything, and an action that would is refused like any the
    /// page does not have: every button on every page posts one of the actions that open,
    /// measure, install or set up, and every folder is where it was after all of them.
    /// </summary>
    /// <remarks>
    /// <b>41 a</b>: the page deletes nothing; it shows sizes and an Open folder button, and
    /// only the agent that made a session deletes it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageOffersNoDeleteAndADeleteSentIsRefused()
    {
        using var scratch = await ScratchDirectory.CreateAsync("dashboard-no-delete");

        var folders = new List<SessionFolder>();

        foreach (var name in new[] { "one", "two", "three" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(scratch.Path, name)).FullName;

            await File.WriteAllTextAsync(Path.Combine(directory, "browserai.data"), name);
            folders.Add(new SessionFolder(CensusPageSessions.IdOf(directory), directory, name.Length));
        }

        // After an update, so that the home also carries the notice's dismiss (57 a).
        await using var rig = await PageRig.CreateAsync(registration: new FakeRegistration { State = Apps() }, occasion: Occasion.AfterUpdate);

        rig.Sessions.Snapshot = Busy();
        rig.Folders.Folders = folders;
        _ = await rig.HandOutAsync();

        foreach (var kind in Enum.GetValues<PageKind>())
        {
            var route = PageNames.RouteOf(kind) + "?tab=1";
            var page = await PageRig.GetAsync(rig.Root + route);

            foreach (Match action in Regex.Matches(page.Body, "data-action=\"(?<action>[^\"]*)\""))
            {
                await Assert.That(Actions).Contains(action.Groups["action"].Value).Because(route);
            }

            await Assert.That(Regex.IsMatch(page.Body, ">\\s*(Delete|Remove the folder|Clean up)", RegexOptions.IgnoreCase)).IsFalse().Because(route);
        }

        foreach (var body in new[]
        {
            $$"""{"action":"delete-session","session":"{{folders[0].Id}}"}""",
            $$"""{"action":"destroy-session","session":"{{folders[1].Id}}"}""",
            $$"""{"action":"delete-folder","folder":"{{folders[2].Id}}"}""",
            """{"action":"clean-up"}""",
        })
        {
            var answer = await rig.ActAsync(body);

            await Assert.That(answer.Status).IsEqualTo(404).Because(body);
        }

        await Assert.That((await rig.ActAsync("""{"action":"measure-sessions"}""")).Status).IsEqualTo(204);

        foreach (var folder in folders)
        {
            await Assert.That(File.Exists(Path.Combine(folder.Directory, "browserai.data"))).IsTrue().Because(folder.Directory);
        }
    }

    /// <summary>
    /// The conversations page draws its map whatever the number of conversations and
    /// sessions, past the eight rows and twelve sessions it once stopped at, every row's two
    /// cards in the rows' own grid and every session a card on it, and at any size it also
    /// carries the list a narrow window shows in the map's place.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer's request of 2026-10-13</b>, in his words verbatim: <i>"Feature
    /// request. Right now I see "There are too many conversations and sessions to draw as a
    /// map, so they are listed.". Can we make it so that this still draws on a map. The page
    /// can become longer (not wider) if need be."</i> The width at which the map becomes a
    /// list is still the stylesheet's, <see cref="PageAssets.MapMinimumWidth"/>. What only a
    /// browser can show, that the page grows in height and never in width, is
    /// <see cref="PageBrowserTests.InARealBrowserAMapOfManyRowsGrowsInHeightOnlyAndKeepsBrowserAIAndTheInspectorInView"/>.
    /// <i>Corrected 2026-10-13 (previously <c>TheMapIsDrawnUpToItsLimitAndAListBeyondIt</c>,
    /// which held the map to eight rows and twelve sessions and a list past them that said
    /// why).</i>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheMapIsDrawnWhateverTheNumberOfConversationsAndSessions()
    {
        await using var rig = await PageRig.CreateAsync();

        foreach (var rows in new[] { 3, 8, 9, 16, 40 })
        {
            var snapshot = ConversationsMapTests.Rows(rows);
            var board = PageBoard.Of(snapshot);

            rig.Sessions.Snapshot = snapshot;

            var page = (await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions))).Body;
            var map = ConversationsMapTests.MapOf(page);
            var drawn = $"{rows} rows, {board.All.Count} sessions";

            await Assert.That(page).Contains("<div class=\"map\" id=\"map\">").Because(drawn);
            await Assert.That(Regex.Count(map, "<div class=\"left\" data-row=\"")).IsEqualTo(rows).Because(drawn);
            await Assert.That(Regex.Count(map, "<div class=\"right\" data-row=\"")).IsEqualTo(rows).Because(drawn);
            await Assert.That(Regex.Count(map, "id=\"n-s-")).IsEqualTo(board.All.Count).Because(drawn);
            await Assert.That(map).Contains("<div class=\"hub-cell\" data-inspect=\"hub\">").Because(drawn);
            await Assert.That(page).Contains("<div class=\"list-view\">").Because(drawn);
            await Assert.That(page).DoesNotContain("too many").Because(drawn);
            await Assert.That(page).DoesNotContain("crowded").Because(drawn);
        }

        // The data reaches past both of the old limits.
        await Assert.That(PageBoard.Of(ConversationsMapTests.Rows(16)).All.Count).IsGreaterThan(12);
    }

    /// <summary>
    /// No page calls an app a client, says that a conversation drives a session, calls a
    /// setup a registration or shows a pid, outside the details for troubleshooting and the
    /// raw text under Show details.
    /// </summary>
    /// <remarks>
    /// <b>42 a</b>, the mockups' words, and <b>45 a</b>, pids and command lines folded under
    /// <i>Details for troubleshooting</i>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoPageCallsAnAppAClientOrShowsAPidOutsideItsDetailsForTroubleshooting()
    {
        string[] forbidden = ["client", "pid", "driven by", "driven through", "registered", "register", "relay", "connection, pid"];

        await using var rig = await PageRig.CreateAsync(registration: new FakeRegistration { State = Apps() }, holds: new ScriptedHolds { Snapshot = Held() });

        rig.Sessions.Snapshot = Busy();
        rig.Folders.Folders = [new SessionFolder(CensusPageSessions.IdOf(CheckoutRounding), CheckoutRounding, 1024)];
        _ = await rig.HandOutAsync();

        foreach (var kind in Enum.GetValues<PageKind>())
        {
            var route = PageNames.RouteOf(kind) + "?tab=1";

            // As a browser does: the page first, which starts what it is drawn from, then
            // its stream.
            _ = await PageRig.GetAsync(rig.Root + route);

            using var stream = await rig.StreamAsync(rig.Gate.Root, 1, PageNames.Of(kind));

            // The state the stream opens with, after the registration and the measurement are in.
            var html = await StateContainingAsync(stream, kind is PageKind.Setup ? "Read at" : string.Empty);

            await Assert.That(html).IsNotNull().Because(route);

            // ⚠️ THE RIG'S OWN FOLDER IS TAKEN OUT BEFORE THE SCAN, AND NOTHING ELSE -- 2026-10-14.
            // The BrowserAI card names the install root and the data root, which are folders
            // of the rig's scratch and so of this checkout, and a checkout's path holds whatever
            // words its folders were given: under .work\wt\inv-relay this arm was red on every
            // run, on "relay".
            var shown = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html!, "<details.*?</details>", " ", RegexOptions.Singleline), "<[^>]+>", " "))
                .Replace(rig.Folder, " ", StringComparison.OrdinalIgnoreCase);

            foreach (var word in forbidden)
            {
                await Assert.That(Regex.IsMatch(shown, "\\b" + Regex.Escape(word) + "\\b", RegexOptions.IgnoreCase)).IsFalse().Because($"{route} says '{word}' outside its details: {shown}");
            }
        }
    }

    /// <summary>Every page carries one script, from its own origin, and no inline style or handler for the policy to refuse.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryPageCarriesOneScriptFromItsOwnOriginAndNothingInline()
    {
        await using var rig = await PageRig.CreateAsync(registration: new FakeRegistration { State = Apps() });

        rig.Sessions.Snapshot = Busy();
        _ = await rig.HandOutAsync();

        foreach (var kind in Enum.GetValues<PageKind>())
        {
            var route = PageNames.RouteOf(kind) + "?tab=1";
            var page = await PageRig.GetAsync(rig.Root + route);

            await Assert.That(page.Status).IsEqualTo(200).Because(route);
            await Assert.That(page.Body.Split("<script").Length).IsEqualTo(2).Because(route);
            await Assert.That(page.Body).Contains("<script src=\"page.js\" defer></script>").Because(route);
            await Assert.That(Regex.IsMatch(page.Body, "\\sstyle\\s*=", RegexOptions.IgnoreCase)).IsFalse().Because(route);
            await Assert.That(Regex.IsMatch(page.Body, "\\son[a-z]+\\s*=", RegexOptions.IgnoreCase)).IsFalse().Because(route);
            await Assert.That(page.Body).DoesNotContain("<style").Because(route);
        }
    }

    // ---- The rig's data -----------------------------------------------------------

    /// <summary>The folder of the session the busy afternoon's first conversation uses.</summary>
    internal const string CheckoutRounding = @"C:\Code\acme-shop\.work\checkout-rounding";

    /// <summary>The way back every page but the home begins with, for one tab.</summary>
    internal static string Crumbs(int tab) =>
        "<nav class=\"crumbs\" aria-label=\"Where you are\"><a id=\"l-crumb-home\" href=\"./?tab=" + tab.ToString(CultureInfo.InvariantCulture) + "\">Overview</a>";

    /// <summary>
    /// A busy afternoon: three conversations, one in a VS Code window working now, one in a
    /// terminal with a window on the screen, and a Codex one with no browser yet; and one
    /// session kept after its conversation ended.
    /// </summary>
    /// <param name="checkoutOpen">Whether the first conversation's session has its browser open.</param>
    /// <returns>The snapshot.</returns>
    internal static SessionsSnapshot Busy(bool checkoutOpen = true)
    {
        var composed = CensusPageSessions.Compose(
            Now,
            [
                ("background", Description(900, ServerDescription.Roles.Host,
                [
                    new HeldSession(CheckoutRounding, "reproducing the rounding error in the checkout total", BrowserOpen: checkoutOpen, DrivenBy: "claude-code", DrivenThrough: 301, IdleCloseAt: checkoutOpen ? Now.AddMinutes(7) : null),
                    new HeldSession(@"C:\Code\market-notes\.work\pricing-compare", "comparing the three pricing pages side by side", BrowserOpen: true, Headed: true, DrivenBy: "claude-code", DrivenThrough: 302, IdleCloseAt: Now.AddMinutes(48)),
                    new HeldSession(@"C:\Code\acme-shop\.work\login-flow-check", "checking that the sign-in survives a reload", BrowserOpen: true, Kept: true, IdleCloseAt: Now.AddMinutes(17)),
                ])),
                ("relay:a", Description(301, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\acme-shop", inFlight: 1)),
                ("relay:b", Description(302, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\market-notes")),
                ("relay:c", Description(303, ServerDescription.Roles.Relay, [], "codex-mcp-client", @"C:\Code\docs-site")),
            ],
            []);

        var window = new ClientWindow("1200-134360600000000000", @"C:\Code\acme-shop");

        return composed with
        {
            Servers =
            [
                .. composed.Servers.Select(server => server.Description.ProcessId switch
                {
                    301 => server with { Conversation = new ConversationName("Fix the <checkout> total", IsTitle: true), Window = window, Reconnect = RelayReconnect.None },
                    302 => server with { Conversation = ClientFolder.Unnamed("Claude Code", @"C:\Code\market-notes"), Reconnect = RelayReconnect.McpReconnect },
                    303 => server with { Conversation = new ConversationName("Update the install guide screenshots", IsTitle: true), Reconnect = RelayReconnect.NewConversation },
                    _ => server,
                }),
            ],
        };
    }

    /// <summary>A server's description, as the background writes one for itself or a relay.</summary>
    internal static ServerDescription Description(int pid, string role, IReadOnlyList<HeldSession> sessions, string? client = null, string folder = @"C:\work", int inFlight = 0) =>
        new(
            pid,
            0,
            client is null ? null : new ClientIdentity(client, Title: null, client is "claude-code" ? "2.1.300" : "0.165.0"),
            folder,
            null,
            inFlight,
            sessions,
            role);

    /// <summary>Claude Code set up for all projects and Codex not set up, read as RegisterAI would answer.</summary>
    internal static AppState Apps() =>
        new()
        {
            Version = "9.0.0",
            InstallRoot = @"C:\install",
            DataRoot = @"C:\data",
            ServerCommand = @"C:\install\current\BrowserAI.exe",
            ServerRefusal = null,
            Clients =
            [
                new ClientState
                {
                    Client = RegistrationClient.ClaudeCode,
                    ClientPath = @"C:\Users\sam\.local\bin\claude.exe",
                    ServerComposed = true,
                    UserScope = new RegistrationView(RegistrationScope.User, @"C:\Users\sam\.claude.json", @"C:\install\current\BrowserAI.exe", RegistrationOwnership.OursAndPresent, null),
                    ProjectDirectory = null,
                    ProjectScope = null,
                },
                new ClientState
                {
                    Client = RegistrationClient.Codex,
                    ClientPath = @"C:\Users\sam\.codex\codex.exe",
                    ServerComposed = true,
                    UserScope = new RegistrationView(RegistrationScope.User, @"C:\Users\sam\.codex\config.toml", null, RegistrationOwnership.Absent, null),
                    ProjectDirectory = null,
                    ProjectScope = null,
                },
            ],
        };

    /// <summary>An update held by a hidden browser, a window and a conversation in a terminal.</summary>
    internal static UpdateHoldSnapshot Held() => new(
        Now,
        UpdateHoldState.Held,
        "9.1.0",
        [new HoldingSession(CheckoutRounding, "reproducing the rounding error in the checkout total", Now.AddMinutes(7))],
        [new HoldingSession(@"C:\Code\market-notes\.work\pricing-compare", "comparing the three pricing pages side by side", Now.AddMinutes(48))],
        [
            new HoldingRelay("claude-code 2.1.300", @"C:\Code\market-notes", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.McpReconnect, "s2", ClientFolder.Unnamed("Claude Code", @"C:\Code\market-notes"))
            {
                ClientName = KnownClients.ClaudeCode,
                ClientVersion = "2.1.300",
            },
        ]);

    /// <summary>
    /// Reads states until the one that carries the barrier's version, holding every state
    /// before it to the view the arm last saw, byte for byte but for its live figures: a view
    /// sent again passes, and a changed one fails.
    /// </summary>
    /// <param name="stream">The tab's stream.</param>
    /// <param name="view">The view the arm last saw.</param>
    /// <returns>The barrier's state, or an empty string when the stream ended first.</returns>
    private static async Task<string> BarrierAfterTheSameViewAsync(RawEventStream stream, string view)
    {
        var expected = WithoutLiveFigures(view);

        while (await stream.NextNamedAsync(PageEvents.State) is { } state)
        {
            var html = state.Member("html");

            if (html.Contains("9.9.9", StringComparison.Ordinal))
            {
                return html;
            }

            await Assert.That(WithoutLiveFigures(html)).IsEqualTo(expected).Because("a second that changed nothing sent a changed view of its own");
        }

        return string.Empty;
    }

    /// <summary>A state's html with the text of every live figure taken out, and nothing else.</summary>
    /// <remarks>
    /// <b>Those elements, and only those</b>: each that carries <c>data-usage</c> or
    /// <c>data-usage-chart</c>. The usage events own them, and the page's script writes each new
    /// reading's words and charts into them in place, so a state drawn after a reading has
    /// landed carries other figures than one drawn before it with nothing else changed. Every
    /// other byte is compared.
    /// </remarks>
    /// <param name="html">The state's html.</param>
    /// <returns>The html with each live figure's element left empty.</returns>
    private static string WithoutLiveFigures(string html) =>
        Regex.Replace(html, @"(?<open><(?<tag>[a-z]+)\b[^>]*\bdata-usage(?:-chart)?=""[^""]*""[^>]*>)(?s:.*?)(?<close></\k<tag>>)", "${open}${close}");

    /// <summary>Reads states off a stream until one carries the text.</summary>
    internal static async Task<string?> StateContainingAsync(RawEventStream stream, string text)
    {
        while (await stream.NextNamedAsync(PageEvents.State) is { } state)
        {
            var html = state.Member("html");

            if (html.Contains(text, StringComparison.Ordinal) || html.Contains(PageContent.Text(text), StringComparison.Ordinal))
            {
                return html;
            }
        }

        return null;
    }

    /// <summary>What holds the update, as the arm sets it.</summary>
    internal sealed class ScriptedHolds : IUpdateHolds
    {
        public UpdateHoldSnapshot Snapshot { get; set; } = UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch);

        public Task<UpdateHoldSnapshot> ReadAsync() => Task.FromResult(Snapshot);

        public UpdateHoldSnapshot ReadCountdown() => Snapshot;

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("No update is downloaded and waiting, so there is nothing to install.");

        public UpdateWatch Watch() => new(UpdateChecks.Checking, UpdateSourceKind.GitHubReleases, null, null, DateTimeOffset.UnixEpoch);
    }
}

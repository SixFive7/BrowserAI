// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The home as the maintainer chose it on 2026-10-11: counts and one button where the
/// conversations were listed, the live CPU and memory, and an after-update notice shown once.
/// </summary>
/// <remarks>
/// <para>
/// <b>61, layout 1, the side column; 59 b, 62 a, 65 a, 66 a and 67; and 57 a</b>, his words
/// verbatim: <i>"Can we remove the arbretary list of browsers and isntead show stats of all teh
/// windows and everything we have on the data with a clear button to open the list going to
/// the more converstations page?"</i>, <i>"Show this all with realtime updates. Show the
/// separate process memory and the shared base separately. The page has to show it all
/// clearly."</i> and <i>"57 a - also allow dismissing the banner in a nice way."</i>
/// </para>
/// <para>
/// <b>Every arm drives the product's <see cref="PageService"/> over raw HTTP and raw event
/// streams</b>, as <see cref="DashboardTests"/>' do, with the readings scripted through
/// <see cref="FakeUsage"/> and the clock a <see cref="ManualClock"/>.
/// </para>
/// </remarks>
internal sealed class HomeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// The home counts the conversations, those working now, the windows, the hidden browsers,
    /// the browsers not open and the sessions kept, lists no conversation, and leads to the
    /// conversations page by one button.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHomeCountsWhatRunsAndLeadsToTheConversationsByOneButtonWithNoRowListed()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = DashboardTests.Busy();

        var home = await PageRig.GetAsync(await rig.HandOutAsync());

        await Assert.That(home.Status).IsEqualTo(200).Because(home.Raw);
        await Assert.That(home.Body).Contains("<div class=\"stats five\">");
        await Assert.That(home.Body).Contains("id=\"s-conversations\"");
        await Assert.That(home.Body).Contains("<b>3</b><span>Conversations</span><small><span class=\"working\"><span class=\"pulse\" aria-hidden=\"true\"></span>1 working now</span></small>");
        await Assert.That(home.Body).Contains("<b>1</b><span>Window on your screen</span>");
        await Assert.That(home.Body).Contains("<b>2</b><span>Hidden browsers</span>");
        await Assert.That(home.Body).Contains("<b>0</b><span>Browsers not open</span>");
        await Assert.That(home.Body).Contains("<b>1</b><span>Kept for later</span>");
        await Assert.That(home.Body).Contains("<a class=\"btn primary go wide\" id=\"l-conversations\" href=\"sessions?tab=1\">See the conversations and browsers");

        // No conversation is listed, and the button is the card's one way in (58 a).
        await Assert.That(home.Body).DoesNotContain("class=\"row\"");
        await Assert.That(home.Body).DoesNotContain("id=\"l-see-all\"");
        await Assert.That(home.Body).DoesNotContain("more conversation");
        await Assert.That(home.Body).DoesNotContain(PageContent.Text("Fix the <checkout> total"));

        // The CPU and memory card keeps its second way in, the maintainer's 66 a.
        await Assert.That(home.Body).Contains("<a class=\"more\" id=\"l-per-session\" href=\"sessions?tab=1\">Per session");
    }

    /// <summary>
    /// The sentence under the headline says how many browser windows are on the screen, none
    /// included, and never that the conversations are listed below.
    /// </summary>
    /// <remarks>
    /// <b>61, 2026-10-11</b>: <i>"Each is listed below, with the browsers it opened."</i> became
    /// wrong once the rows were hidden, and is wrong on a home that lists none. The zero case
    /// is the maintainer's accepted words, 67.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLedeSaysNoneOfThemWhenNoWindowIsOnTheScreenAndNeverThatEachIsListed()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Hidden();

        var home = await PageRig.GetAsync(await rig.HandOutAsync());

        await Assert.That(home.Body).Contains("<h1>2 conversations are using BrowserAI</h1><p class=\"lede\">None of them has a browser window open on your screen.</p>");
        await Assert.That(home.Body).DoesNotContain("Each is listed below");

        rig.Sessions.Snapshot = DashboardTests.Busy();

        var busy = await PageRig.GetAsync(await rig.HandOutAsync());

        await Assert.That(busy.Body).Contains("<p class=\"lede\">A browser window is open on your screen.</p>");
    }

    /// <summary>
    /// After an update the notice shows in every tab and after a restart until a <i>What is
    /// new</i> link is pressed, stays seen after that, and comes back for a newer version.
    /// </summary>
    /// <remarks>
    /// <b>57 a</b>: <i>"Opening the changelog page by any of the three "What is new" links
    /// counts as seen"</i>, and <i>"BrowserAI writes the seen version into its data folder, and
    /// the banner returns only when a newer version is installed."</i> Until 2026-10-12 the
    /// notice was the first tab's alone, and nothing remembered that it had been seen.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAfterUpdateNoticeShowsUntilAWhatIsNewLinkIsPressedAndComesBackOnlyForANewerVersion()
    {
        await using var updated = await PageRig.CreateAsync(occasion: Occasion.AfterUpdate);

        var first = await PageRig.GetAsync(await updated.HandOutAsync());
        var second = await PageRig.GetAsync(await updated.HandOutAsync());

        await Assert.That(first.Body).Contains("Updated to BrowserAI 9.0.0");
        await Assert.That(second.Body).Contains("Updated to BrowserAI 9.0.0").Because("a second tab, after the first, still shows the notice nobody has seen");
        await Assert.That(second.Body).Contains("<button type=\"button\" id=\"b-dismiss-updated\" class=\"dismiss\" data-action=\"dismiss-notice\" aria-label=\"Dismiss this notice\" title=\"Dismiss\">");

        var record = Path.Combine(updated.Facts.DataRoot, UpdateNoticeFile.FileName);

        await Assert.That((await File.ReadAllTextAsync(record)).Trim()).IsEqualTo("9.0.0 waiting");

        // A restart before anybody looked keeps it waiting.
        await using (var restarted = await PageRig.CreateAsync(facts: facts => facts with { DataRoot = updated.Facts.DataRoot }))
        {
            await Assert.That((await PageRig.GetAsync(await restarted.HandOutAsync())).Body).Contains("Updated to BrowserAI 9.0.0");
        }

        // The notice's own What is new opens the changelog page, and that is seeing it.
        var changelog = await PageRig.GetAsync(updated.Root + "changelog?tab=3");

        await Assert.That(changelog.Status).IsEqualTo(200).Because(changelog.Raw);
        await Assert.That((await PageRig.GetAsync(await updated.HandOutAsync())).Body).DoesNotContain("Updated to BrowserAI");
        await Assert.That((await File.ReadAllTextAsync(record)).Trim()).IsEqualTo("9.0.0 seen");

        // Seen stays seen across a restart, the start after the same update included.
        await using (var again = await PageRig.CreateAsync(occasion: Occasion.AfterUpdate, facts: facts => facts with { DataRoot = updated.Facts.DataRoot }))
        {
            await Assert.That((await PageRig.GetAsync(await again.HandOutAsync())).Body).DoesNotContain("Updated to BrowserAI");
        }

        // A newer version installed is a notice again.
        await using var newer = await PageRig.CreateAsync(occasion: Occasion.AfterUpdate, facts: facts => facts with { DataRoot = updated.Facts.DataRoot, Version = "9.1.0" });

        await Assert.That((await PageRig.GetAsync(await newer.HandOutAsync())).Body).Contains("Updated to BrowserAI 9.1.0");
        await Assert.That((await File.ReadAllTextAsync(record)).Trim()).IsEqualTo("9.1.0 waiting");
    }

    /// <summary>The notice's dismiss marks it seen, and every tab is sent the home without it.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNoticesDismissMarksItSeenAndTheHomeIsSentWithoutIt()
    {
        await using var rig = await PageRig.CreateAsync(occasion: Occasion.AfterUpdate);

        _ = await rig.HandOutAsync();

        using var home = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await DashboardTests.StateContainingAsync(home, "Updated to BrowserAI 9.0.0")).IsNotNull();

        var dismissed = await rig.ActAsync("{\"action\":\"dismiss-notice\"}");

        await Assert.That(dismissed.Status).IsEqualTo(204).Because(dismissed.Raw);

        var state = await home.NextNamedAsync(PageEvents.State);

        await Assert.That(state).IsNotNull();
        await Assert.That(state!.Member("html")).DoesNotContain("Updated to BrowserAI");
        await Assert.That(await WaitForAsync(async () => File.Exists(Path.Combine(rig.Facts.DataRoot, UpdateNoticeFile.FileName))
            && (await File.ReadAllTextAsync(Path.Combine(rig.Facts.DataRoot, UpdateNoticeFile.FileName))).Trim() is "9.0.0 seen")).IsTrue();
    }

    /// <summary>
    /// The figures are read only while a tab shows them, a page that shows them is drawn with
    /// a reading, and each reading is sent to that tab as words and charts where they stand.
    /// </summary>
    /// <remarks>
    /// <b>59 a and b</b>: <i>"read every 2 seconds while a dashboard tab is open and never
    /// otherwise"</i>; the home and the conversations page show them, and no other page does.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFiguresAreReadOnlyWhileATabShowsThemAndEachReadingIsSentToIt()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Usage.Reading = new UsageReading(new ProcessCounters(0, 46 * 1024 * 1024, 30 * 1024 * 1024), [], [], 4);

        // A page that does not show them is drawn without a reading, and its tab starts none.
        _ = await rig.HandOutAsync(PageKind.Storage);

        using (var storage = await rig.StreamAsync(rig.Gate.Root, 1, "storage"))
        {
            await Assert.That(await storage.NextNamedAsync(PageEvents.State)).IsNotNull();
            rig.Clock.Advance(PageService.UsageInterval * 3);
            await Assert.That(rig.Usage.Reads).IsEqualTo(0);
        }

        // The home is drawn with one, whose memory it shows at once.
        var address = await rig.HandOutAsync();
        var home = await PageRig.GetAsync(address);

        await Assert.That(rig.Usage.Reads).IsEqualTo(1);
        await Assert.That(home.Body).Contains("<b class=\"m-value\" data-usage=\"own\">46 MB</b>");

        using (var stream = await rig.StreamAsync(rig.Gate.Root, 2))
        {
            await Assert.That(await stream.NextNamedAsync(PageEvents.State)).IsNotNull();

            // Two seconds of CPU on four processors is a quarter of the PC.
            rig.Usage.Reading = new UsageReading(new ProcessCounters(20_000_000, 47 * 1024 * 1024, 30 * 1024 * 1024), [], [], 4);
            rig.Clock.Advance(PageService.UsageInterval);

            var usage = await stream.NextNamedAsync(PageEvents.Usage);

            await Assert.That(usage).IsNotNull();

            using var data = JsonDocument.Parse(usage!.Data);

            await Assert.That(data.RootElement.GetProperty("text").GetProperty("cpu").GetString()).IsEqualTo("25.0%");
            await Assert.That(data.RootElement.GetProperty("text").GetProperty("own").GetString()).IsEqualTo("47 MB");
            await Assert.That(data.RootElement.GetProperty("charts").GetProperty("cpu").GetString()).Contains("<svg");
            await Assert.That(rig.Usage.Reads).IsEqualTo(2);
        }

        // The last such tab gone, no reading follows.
        await Assert.That(await WaitForAsync(() => Task.FromResult(rig.Page.Tabs?.Connected is 0))).IsTrue();

        var reads = rig.Usage.Reads;

        rig.Clock.Advance(PageService.UsageInterval * 3);

        await Assert.That(rig.Usage.Reads).IsEqualTo(reads);
    }

    /// <summary>A reading that fails is a line in the log of its own, and the figures stay where they were.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReadingThatFailsIsLoggedAndTheFiguresStay()
    {
        using var logs = new CapturingLoggerProvider();
        await using var rig = await PageRig.CreateAsync(logs: logs);

        rig.Usage.Failure = new InvalidOperationException("Windows would not say");

        var home = await PageRig.GetAsync(await rig.HandOutAsync());

        await Assert.That(home.Status).IsEqualTo(200).Because(home.Raw);
        await Assert.That(home.Body).Contains("<b class=\"m-value\" data-usage=\"cpu\">Measuring</b>");
        await Assert.That(logs.Records.Any(record => record.EventId.Id == 7018)).IsTrue();
        await Assert.That(logs.Logged("What BrowserAI's processes use could not be read for the page, so it shows the last reading.")).IsTrue();
    }

    /// <summary>Two conversations, one working, each with a hidden browser: no window on the screen.</summary>
    private static SessionsSnapshot Hidden() =>
        CensusPageSessions.Compose(
            Now,
            [
                ("background", DashboardTests.Description(900, ServerDescription.Roles.Host,
                [
                    new HeldSession(DashboardTests.CheckoutRounding, "reproducing the rounding error in the checkout total", BrowserOpen: true, DrivenBy: "claude-code", DrivenThrough: 301, IdleCloseAt: Now.AddMinutes(7)),
                    new HeldSession(@"C:\Code\market-notes\.work\pricing-compare", "comparing the three pricing pages side by side", BrowserOpen: true, DrivenBy: "claude-code", DrivenThrough: 302, IdleCloseAt: Now.AddMinutes(8)),
                ])),
                ("relay:a", DashboardTests.Description(301, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\acme-shop", inFlight: 1)),
                ("relay:b", DashboardTests.Description(302, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\market-notes")),
            ],
            []);

    /// <summary>Waits for a condition, as a hang detector, and never as a promptness claim.</summary>
    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TestDefaults.InProcessHang;

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return true;
    }
}

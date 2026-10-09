// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using BrowserAI.App.Page;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The dashboard's update page: what holds a downloaded update, split three ways
/// with a countdown each, what installing now does, and the button that does it.
/// </summary>
/// <remarks>
/// <para>
/// <b>T and H1, decided 2026-10-08 by the maintainer, his words verbatim:</b>
/// <i>"the install now button takes you to the browser interface gui of the
/// coordinator where it can better explain what the risks of forcing the update now
/// are together with an overview of who is still using it"</i>, and <i>"the
/// coordinator can show what is holding up the update clearly split by browsers,
/// relays, etc..."</i>
/// </para>
/// <para>
/// <b>The page is rendered from snapshots</b>, the contract the background fills,
/// and served through the page's own listener with a scripted background behind it;
/// nothing here reaches a browser, a session or the screen.
/// </para>
/// </remarks>
internal sealed class UpdatePageTests
{
    /// <summary>The instant every rig's clock starts at.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A held update is shown split into hidden browser sessions, visible windows and
    /// agents, each with its countdown as a deadline the page's script counts down;
    /// a visible window says to close it, each agent says the reconnect its client
    /// will need, and what installing now does is said beside the button.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageSplitsWhatHoldsTheUpdateAndGivesEachItsCountdown()
    {
        var html = Render(Held());

        await Assert.That(html).Contains("<h1>Update</h1>");
        await Assert.That(html).Contains("BrowserAI 1.2.0 is downloaded and ready to install. It installs by itself once BrowserAI has been idle.");
        await Assert.That(html).Contains($"It installs in {Countdown(Now.AddMinutes(50), "50:00")}, at about ");
        await Assert.That(html).Contains(", if nothing uses BrowserAI before then.");

        await Assert.That(Section(html, "hidden")).Contains("<strong>&lt;b&gt;Reads&lt;/b&gt; the release notes</strong><br><code>C:\\work\\hidden</code>");
        await Assert.That(Section(html, "hidden")).Contains($"Closes in {Countdown(Now.AddMinutes(5), "5:00")} if no call names it.");

        await Assert.That(Section(html, "windows")).Contains("<span class=\"warning\">Close this to let the update proceed.</span>");
        await Assert.That(Section(html, "windows")).Contains($"Closes by itself in {Countdown(Now.AddMinutes(50), "50:00")} if nobody uses it.");

        var agents = Section(html, "agents");

        await Assert.That(agents).Contains("<strong>Claude Code 2.1.290</strong> in <code>C:\\Source\\one</code>");
        await Assert.That(agents).Contains($"Holds the update for {Countdown(Now.AddMinutes(8), "8:00")} more if its client sends nothing.");
        await Assert.That(agents).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.McpReconnect)));
        await Assert.That(agents).Contains("Idle: it no longer holds the update.");
        await Assert.That(agents).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.NewConversation)));
        await Assert.That(agents).Contains("A call is running now, so it holds the update.");
        await Assert.That(agents).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.None)));

        var install = Section(html, "install-now");

        foreach (var effect in UpdatePageContent.InstallNowEffects)
        {
            await Assert.That(install).Contains(PageContent.Text(effect));
        }

        await Assert.That(install).Contains("<button type=\"button\" data-action=\"install-now\" data-version=\"1.2.0\">Install BrowserAI 1.2.0 now</button>");
    }

    /// <summary>
    /// A wait no countdown leads says what it waits for, and a session or a window set
    /// never to close says so where its countdown would be.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWaitNoCountdownLeadsSaysWhatItWaitsFor()
    {
        var empty = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "1.2.0", [], [], []);

        var window = Render(empty with { VisibleWindows = [new HoldingSession(@"C:\w", null, null)] });

        await Assert.That(window).Contains(PageContent.Text("A visible window set never to close holds it: it installs once you close that window and nothing else uses BrowserAI."));
        await Assert.That(Section(window, "windows")).Contains("Set never to close by itself.");
        await Assert.That(Section(window, "windows")).Contains("<strong>No purpose recorded</strong>");

        var session = Render(empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, null)] });

        await Assert.That(session).Contains(PageContent.Text("A hidden session set never to close holds it: it installs once an agent closes that session and nothing else uses BrowserAI."));
        await Assert.That(Section(session, "hidden")).Contains(PageContent.Text("Set never to close: an agent has to close it."));

        var call = Render(empty with { Relays = [new HoldingRelay("Codex", null, Now.AddMinutes(-1), CallInFlight: true, RelayReconnect.NewConversation)] });

        await Assert.That(call).Contains(PageContent.Text("Every countdown has run out, and a call is still running: it installs once that call has finished."));

        var closing = Render(empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, Now.AddSeconds(-1))] });

        await Assert.That(closing).Contains(PageContent.Text("Every countdown has run out: it installs once the last browser has closed."));
        await Assert.That(Section(closing, "hidden")).Contains("Closing now.");

        var nothing = Render(empty);

        await Assert.That(nothing).Contains(PageContent.Text("Nothing uses BrowserAI now: it installs in a moment."));
        await Assert.That(Section(nothing, "hidden")).Contains("No hidden browser session is open.");
        await Assert.That(Section(nothing, "windows")).Contains("No visible window is open.");
        await Assert.That(Section(nothing, "agents")).Contains("No agent is connected to BrowserAI.");
    }

    /// <summary>
    /// With nothing waiting the page says which version is installed; while it
    /// installs it says so and offers no button; and where nothing reports what holds
    /// an update it says that, and claims nothing else.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageSaysWhenNothingWaitsWhenItIsInstallingAndWhenNothingReports()
    {
        var none = Render(UpdateHoldSnapshot.Nothing(Now));

        // ⚠️ Corrected 2026-10-09 (previously "... BrowserAI 9.0.0 is installed. The
        // status page checks for a newer one."): since the one resident background,
        // the status page checks for nothing itself, so the sentence ends at what is
        // installed.
        await Assert.That(none).Contains("<p>No downloaded update is waiting. BrowserAI 9.0.0 is installed.</p>");
        await Assert.That(none).DoesNotContain("data-action=\"install-now\"");

        var installing = Render(new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, "1.2.0", [], [], []));

        await Assert.That(installing).Contains("BrowserAI 1.2.0 is installing now. This page stops when BrowserAI closes, and BrowserAI starts again by itself.");
        await Assert.That(installing).DoesNotContain("data-action=\"install-now\"");

        var unreported = Render(null);

        await Assert.That(unreported).Contains(PageContent.Text(UpdatePageContent.NoHoldsReported));
        await Assert.That(unreported).DoesNotContain("data-action=\"install-now\"");
    }

    /// <summary>
    /// The update page is served at its own route, the navigation marks it, and its
    /// button asks the background to install now: a refusal is a sentence on the page,
    /// and an install that started tells every tab and ends their streams.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageIsServedAndItsButtonAsksTheBackgroundToInstallNow()
    {
        var holds = new ScriptedHolds(Held()) { Answer = "An agent's call is still running, so nothing was installed." };

        using var rig = new PageRig(holds: holds);

        var address = rig.HandOut(PageKind.Update);

        await Assert.That(address).Contains("update?tab=1");

        var page = await PageRig.GetAsync(address);

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<title>BrowserAI update</title>");
        await Assert.That(page.Body).Contains("data-page=\"update\"");
        await Assert.That(page.Body).Contains("<a href=\"update?tab=1\" aria-current=\"page\">Update</a>");
        await Assert.That(page.Body).Contains("data-action=\"install-now\" data-version=\"1.2.0\"");

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "update");

        var refused = await rig.ActAsync("""{"action":"install-now","version":"1.2.0"}""");

        await Assert.That(refused.Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "An agent&#x27;s call is still running, so nothing was installed.")).IsNotNull();
        await Assert.That(string.Join(",", holds.Asked)).IsEqualTo("1.2.0");

        holds.Answer = null;
        _ = await rig.ActAsync("""{"action":"install-now","version":"1.2.0"}""");

        var closing = await stream.NextNamedAsync(PageEvents.Closing);

        await Assert.That(closing).IsNotNull();
        await Assert.That(closing!.Member("sentence")).Contains("BrowserAI is installing 1.2.0");
        await Assert.That(string.Join(",", holds.Asked)).IsEqualTo("1.2.0,1.2.0");
    }

    /// <summary>A page with nothing reporting what holds an update refuses the install button's request.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNothingReportingTheInstallRequestIsRefused()
    {
        using var rig = new PageRig();

        _ = rig.HandOut(PageKind.Update);

        var answer = await rig.ActAsync("""{"action":"install-now","version":"1.2.0"}""");

        await Assert.That(answer.Status).IsNotEqualTo(204);
    }

    /// <summary>
    /// Every tab is sent a new state once what holds the update has changed, read
    /// once a second while a listener is up; a second that changes nothing sends
    /// nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATabIsSentANewStateWhenWhatHoldsTheUpdateChanges()
    {
        var holds = new ScriptedHolds(Held());

        using var rig = new PageRig(holds: holds);

        _ = rig.HandOut(PageKind.Update);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "update");

        // The stream's own first state.
        await Assert.That(await StateContainingAsync(stream, "Claude Code 2.1.290")).IsNotNull();

        holds.Snapshot = Held() with
        {
            Relays = [.. Held().Relays, new HoldingRelay("Codex 0.162.0", @"C:\Source\four", Now.AddMinutes(9), CallInFlight: false, RelayReconnect.NewConversation)],
        };

        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        var changed = await StateContainingAsync(stream, "Codex 0.162.0");

        await Assert.That(changed).IsNotNull();
        await Assert.That(changed!).Contains("C:\\Source\\four");
    }

    /// <summary>
    /// Where the page checks for nothing itself and the background reports what holds
    /// an update, the status page's update section says what is waiting and leads to
    /// the update page, and never that no release feed is set, because the background
    /// has one; where nothing reports, the stage's own sentence stands.
    /// </summary>
    /// <remarks>
    /// The background builds its page with no feed of its own since 2026-10-08, so
    /// the page's own check and install are off and the stage reads
    /// <see cref="UpdateStage.NoFeed"/> on every installed BrowserAI. Its sentence was
    /// written for a build with no feed at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusPageSaysWhatTheBackgroundHoldsAndNeverThatNoFeedIsSet()
    {
        const string NoFeed = "No release feed is set for this build";

        var held = Status(Held());

        await Assert.That(held).Contains("<p>BrowserAI 1.2.0 is downloaded and ready to install. It installs by itself once BrowserAI has been idle.</p>");
        await Assert.That(held).Contains("<p><a href=\"update?tab=3\">See what holds it, or install it now</a></p>");
        await Assert.That(held).DoesNotContain(NoFeed);
        await Assert.That(held).DoesNotContain("data-action=\"check-updates\"");

        var none = Status(UpdateHoldSnapshot.Nothing(Now));

        await Assert.That(none).Contains("<p>No downloaded update is waiting.</p>");
        await Assert.That(none).DoesNotContain(NoFeed);

        var installing = Status(new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, "1.2.0", [], [], []));

        await Assert.That(installing).Contains("<p>BrowserAI 1.2.0 is installing now.</p>");
        await Assert.That(installing).DoesNotContain(NoFeed);

        await Assert.That(Status(null)).Contains(NoFeed);
    }

    /// <summary>
    /// A status tab is sent a new state when an update becomes held, by the same
    /// once-a-second watch that keeps the update page current.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStatusTabIsSentANewStateWhenAnUpdateBecomesHeld()
    {
        var holds = new ScriptedHolds(UpdateHoldSnapshot.Nothing(Now));

        using var rig = new PageRig(holds: holds, unavailable: UpdateStage.NoFeed);

        _ = rig.HandOut(PageKind.Status);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await StateContainingAsync(stream, "No downloaded update is waiting.")).IsNotNull();

        holds.Snapshot = Held();
        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        await Assert.That(await StateContainingAsync(stream, "BrowserAI 1.2.0 is downloaded and ready to install.")).IsNotNull();
    }

    /// <summary>The status page's main part, rendered for tab 3 of a page that checks for nothing itself.</summary>
    private static string Status(UpdateHoldSnapshot? holds) =>
        PageContent.Fragment(
            new PageView(
                new PageFacts
                {
                    Version = "9.0.0",
                    InstallRoot = @"C:\install",
                    DataRoot = @"C:\data",
                    LogDirectory = @"C:\data\logs",
                    ServerCommand = null,
                    ServerRefusal = null,
                },
                new UpdateView(UpdateStage.NoFeed),
                null,
                SessionsSnapshot.Empty,
                null,
                Holds: holds),
            PageKind.Status,
            3,
            Occasion.Ordinary,
            Now);

    private static string Render(UpdateHoldSnapshot? holds) =>
        UpdatePageContent.Render(
            new PageView(
                new PageFacts
                {
                    Version = "9.0.0",
                    InstallRoot = null,
                    DataRoot = @"C:\data",
                    LogDirectory = @"C:\data\logs",
                    ServerCommand = null,
                    ServerRefusal = null,
                },
                new UpdateView(UpdateStage.NotChecked),
                null,
                SessionsSnapshot.Empty,
                null,
                Holds: holds),
            Now);

    /// <summary>A countdown as the page writes it.</summary>
    private static string Countdown(DateTimeOffset ends, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"<span class=\"countdown\" data-ends-at=\"{ends.ToUnixTimeMilliseconds()}\">{text}</span>");

    /// <summary>One section of the page, by its id.</summary>
    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"<section id=\"{id}\">", StringComparison.Ordinal);

        return start < 0 ? string.Empty : html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }

    /// <summary>
    /// A held update: a hidden browser closing in five minutes, a visible window in
    /// fifty, a terminal relay in eight, a Codex relay already out, and a VS Code
    /// relay past its countdown with a call running.
    /// </summary>
    private static UpdateHoldSnapshot Held() => new(
        Now,
        UpdateHoldState.Held,
        "1.2.0",
        [new HoldingSession(@"C:\work\hidden", "<b>Reads</b> the release notes", Now.AddMinutes(5))],
        [new HoldingSession(@"C:\work\window", "A form the person fills in", Now.AddMinutes(50))],
        [
            new HoldingRelay("Claude Code 2.1.290", @"C:\Source\one", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.McpReconnect),
            new HoldingRelay("Codex 0.161.0", @"C:\Source\two", Now.AddMinutes(-1), CallInFlight: false, RelayReconnect.NewConversation),
            new HoldingRelay("Claude Code 2.1.290 (VS Code)", @"C:\Source\three", Now.AddMinutes(-2), CallInFlight: true, RelayReconnect.None),
        ]);

    private static async Task<string?> StateContainingAsync(RawEventStream stream, string text)
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

    /// <summary>What holds the update, and the background's answer to an install-now, as the arm sets them.</summary>
    private sealed class ScriptedHolds(UpdateHoldSnapshot snapshot) : IUpdateHolds
    {
        public UpdateHoldSnapshot Snapshot { get; set; } = snapshot;

        /// <summary>What <see cref="InstallNowAsync"/> answers: <see langword="null"/> once the install started.</summary>
        public string? Answer { get; set; }

        public ConcurrentQueue<string> Asked { get; } = new();

        public UpdateHoldSnapshot Read() => Snapshot;

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken)
        {
            Asked.Enqueue(version);
            return Task.FromResult(Answer);
        }
    }
}

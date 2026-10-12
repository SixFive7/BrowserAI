// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using BrowserAI.App.Page;
using BrowserAI.Relay;

namespace BrowserAI.Tests;

/// <summary>
/// The CPU and memory figures the page draws from the background's readings: what a reading
/// adds up, what two readings say about the CPU, and one session's lines.
/// </summary>
/// <remarks>
/// <b>The maintainer's 59 b, 62 a and 65 a, 2026-10-11</b>, and his words verbatim: <i>"Show
/// this all with realtime updates. Show the separate process memory and the shared base
/// separately. The page has to show it all clearly."</i> Every arm here is a pure function of
/// readings the arm writes, so a number is checked against arithmetic done by hand.
/// </remarks>
internal sealed partial class UsageTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    private const long Megabyte = 1024 * 1024;

    private static readonly string[] ChartNames = ["cpu", "own"];

    private static readonly string[] NotMeasuredOnly = ["Not measured"];

    [Test]
    public async Task TheCpuIsWhatEveryProcessAddedBetweenTwoReadingsAsAShareOfTheWholeProcessor()
    {
        var first = PageUsage.Next(
            null,
            Reading(
                background: 1_000_000,
                relays: [new RelayReading(7, 70, new ProcessCounters(500_000, 0, 0))],
                sessions: [new SessionReading("a", 2_000_000, 0, 0, 0)]),
            Start,
            Window);

        // A first reading has nothing to count against.
        await Assert.That(first.Cpu).IsNull();
        await Assert.That(first.SessionOf("a")).IsNotNull();
        await Assert.That(first.SessionOf("a")!.Cpu).IsNull();

        // Two seconds later on four processors: 8 s of processor. The background added
        // 0.2 s, the relay 0.1 s, session a 0.4 s, and session b, which the first reading
        // did not see, 0.08 s from its start: 0.78 s of 8 s.
        var second = PageUsage.Next(
            first,
            Reading(
                background: 3_000_000,
                relays: [new RelayReading(7, 70, new ProcessCounters(1_500_000, 0, 0))],
                sessions: [new SessionReading("a", 6_000_000, 0, 0, 0), new SessionReading("b", 800_000, 0, 0, 0)]),
            Start.AddSeconds(2),
            Window);

        await Assert.That(Rounded(second.Cpu)).IsEqualTo(0.0975);
        await Assert.That(Rounded(second.SessionOf("a")!.Cpu)).IsEqualTo(0.05);
        await Assert.That(Rounded(second.SessionOf("b")!.Cpu)).IsEqualTo(0.01);
        await Assert.That(Rounded(second.Relays[7].Cpu)).IsEqualTo(0.0125);

        // The same pid with another creation time is another relay, counted from its start,
        // and a session whose time went down is a new job with an old name: both count from
        // zero, never as a negative.
        var third = PageUsage.Next(
            second,
            Reading(
                background: 3_000_000,
                relays: [new RelayReading(7, 71, new ProcessCounters(400_000, 0, 0))],
                sessions: [new SessionReading("a", 1_200_000, 0, 0, 0)]),
            Start.AddSeconds(4),
            Window);

        await Assert.That(Rounded(third.Relays[7].Cpu)).IsEqualTo(0.005);
        await Assert.That(Rounded(third.SessionOf("a")!.Cpu)).IsEqualTo(0.015);
        await Assert.That(Rounded(third.Cpu)).IsEqualTo(0.02);

        // A reading further than the window after the one before is a first reading again.
        var late = PageUsage.Next(third, Reading(background: 9_000_000), Start.AddSeconds(4) + Window + TimeSpan.FromSeconds(1), Window);

        await Assert.That(late.Cpu).IsNull();
    }

    [Test]
    public async Task OwnMemoryIsAddedUpInItsFourPartsAndSharedIsTheLargestAnyOneProcessHolds()
    {
        var usage = PageUsage.Next(
            null,
            new UsageReading(
                new ProcessCounters(0, 46 * Megabyte, 30 * Megabyte),
                [new RelayReading(7, 70, new ProcessCounters(0, 12 * Megabyte, 25 * Megabyte)), new RelayReading(8, 80, new ProcessCounters(0, 13 * Megabyte, 26 * Megabyte))],
                [new SessionReading("a", 0, 400 * Megabyte, 60 * Megabyte, 268 * Megabyte), new SessionReading("b", 0, 512 * Megabyte, 58 * Megabyte, 200 * Megabyte)],
                Processors: 32),
            Start,
            Window);

        await Assert.That(usage.Own).IsEqualTo(new UsageMemory(912 * Megabyte, 118 * Megabyte, 46 * Megabyte, 25 * Megabyte));
        await Assert.That(usage.Own.Total).IsEqualTo(1101 * Megabyte);
        await Assert.That(usage.Shared).IsEqualTo(268 * Megabyte);
        await Assert.That(usage.SessionOf("a")!.OwnBytes).IsEqualTo(460 * Megabyte);
        await Assert.That(usage.Relays[8].OwnBytes).IsEqualTo(13 * Megabyte);
        await Assert.That(usage.SessionOf("c")).IsNull();
    }

    [Test]
    public async Task TheChartsKeepTheReadingsOfTheLastWindowAndNoOlder()
    {
        PageUsage? usage = null;

        for (var second = 0; second <= 180; second += 2)
        {
            usage = PageUsage.Next(usage, Reading(background: second * 10_000L, own: second * Megabyte), Start.AddSeconds(second), Window);
        }

        // At 180 s the window reaches back to 60 s, which is no longer in it: 62 s to 180 s.
        await Assert.That(usage!.History.Count).IsEqualTo(60);
        await Assert.That(usage.History[0].At).IsEqualTo(Start.AddSeconds(62));
        await Assert.That(usage.History[^1]).IsEqualTo(new UsagePoint(Start.AddSeconds(180), usage.Cpu, 180 * Megabyte));
    }

    [Test]
    public async Task ASessionsLinesSayItsCpuAndOwnMemoryAndPointItsSharedMemoryAtTheOneTotal()
    {
        var session = Session("0123456789abcdef");

        var before = UsageContent.SessionLines(View(null), session);

        await Assert.That(before).Contains("<dt>CPU</dt><dd data-usage=\"s:0123456789abcdef:cpu\">Measuring</dd>");
        await Assert.That(before).Contains("<dt>Own memory</dt><dd data-usage=\"s:0123456789abcdef:own\">Measuring</dd>");
        await Assert.That(before).Contains("<dt>Shared memory</dt><dd><a id=\"l-shared-0123456789abcdef\" href=\"./?tab=3#card-usage\" data-usage=\"shared-all\">Counted once for all of BrowserAI</a></dd>");

        var first = PageUsage.Next(null, Reading(sessions: [new SessionReading(session.Id, 0, 250 * Megabyte, 62 * Megabyte, 268 * Megabyte)], processors: 8), Start, Window);
        var second = PageUsage.Next(first, Reading(sessions: [new SessionReading(session.Id, 8_000_000, 250 * Megabyte, 62 * Megabyte, 268 * Megabyte)], processors: 8), Start.AddSeconds(2), Window);

        var lines = UsageContent.SessionLines(View(second), session);

        // 0.8 s of CPU over 2 s on 8 processors is 5 %.
        await Assert.That(lines).Contains("<dd data-usage=\"s:0123456789abcdef:cpu\">5.0%</dd>");
        await Assert.That(lines).Contains("<dd data-usage=\"s:0123456789abcdef:own\">312 MB</dd>");
        await Assert.That(lines).Contains("data-usage=\"shared-all\">About 268 MB for all of BrowserAI</a>");

        // In its first reading a session's memory is known and its CPU is not yet.
        var firstLines = UsageContent.SessionLines(View(first), session);

        await Assert.That(firstLines).Contains("<dd data-usage=\"s:0123456789abcdef:cpu\">Measuring</dd>");
        await Assert.That(firstLines).Contains("<dd data-usage=\"s:0123456789abcdef:own\">312 MB</dd>");

        // A session a reading does not include runs nothing of its own then, and says so:
        // Measuring is for before the first reading only (107 b). Its shared line is the
        // one total, as every session's is.
        var other = UsageContent.SessionLines(View(second), Session("fedcba9876543210"));

        await Assert.That(other).Contains("<dt>CPU</dt><dd data-usage=\"s:fedcba9876543210:cpu\">Not running</dd>");
        await Assert.That(other).Contains("<dt>Own memory</dt><dd data-usage=\"s:fedcba9876543210:own\">Not running</dd>");
        await Assert.That(other).Contains("<dt>Shared memory</dt><dd><a id=\"l-shared-fedcba9876543210\" href=\"./?tab=3#card-usage\" data-usage=\"shared-all\">About 268 MB for all of BrowserAI</a></dd>");
    }

    [Test]
    public async Task EveryFigureTheLinesCarryIsOneEachReadingSends()
    {
        var session = Session("0123456789abcdef");
        var usage = PageUsage.Next(null, Reading(sessions: [new SessionReading(session.Id, 0, 1, 1, 1)]), Start, Window);
        var keys = UsageKey().Matches(UsageContent.SessionLines(View(usage), session)).Select(static match => match.Groups[1].Value).ToList();
        var sent = UsageContent.Figures(usage);

        await Assert.That(keys.Count).IsEqualTo(3);

        foreach (var key in keys)
        {
            await Assert.That(sent.ContainsKey(key)).IsTrue().Because($"'{key}' is drawn and no reading sends it");
        }
    }

    [Test]
    public async Task FiguresAreWrittenAsTaskManagerCountsThem()
    {
        await Assert.That(UsageContent.Cpu(null)).IsEqualTo("Measuring");
        await Assert.That(UsageContent.Cpu(0)).IsEqualTo("0.0%");
        await Assert.That(UsageContent.Cpu(0.026)).IsEqualTo("2.6%");
        await Assert.That(UsageContent.Cpu(1)).IsEqualTo("100.0%");
        await Assert.That(UsageContent.Megabytes(0)).IsEqualTo("0 MB");
        await Assert.That(UsageContent.Megabytes(-5)).IsEqualTo("0 MB");
        await Assert.That(UsageContent.Megabytes(1276 * Megabyte)).IsEqualTo("1,276 MB");
        await Assert.That(UsageContent.Megabytes(Megabyte * 3 / 2)).IsEqualTo("2 MB");
    }

    [Test]
    public async Task TheCardSaysEveryFigureWhereEachReadingSendsItAndSaysAboutOfTheSharedMemory()
    {
        var first = PageUsage.Next(null, Card(0), Start, Window);
        var usage = PageUsage.Next(first, Card(8_000_000), Start.AddSeconds(2), Window);
        var card = UsageContent.Card(View(usage), Window);

        // 0.8 s over 2 s on four processors is 10 %; own memory is its four parts.
        await Assert.That(card).Contains("<b class=\"m-value\" data-usage=\"cpu\">10.0%</b>");
        await Assert.That(card).Contains("<b class=\"m-value\" data-usage=\"own\">1,101 MB</b>");
        await Assert.That(card).Contains("<dt>Browsers</dt><dd data-usage=\"own-browsers\">912 MB</dd>");
        await Assert.That(card).Contains("<dt>Browser servers</dt><dd data-usage=\"own-servers\">118 MB</dd>");
        await Assert.That(card).Contains("<dt>Background</dt><dd data-usage=\"own-background\">46 MB</dd>");
        await Assert.That(card).Contains("<dt>Connections, one per conversation</dt><dd data-usage=\"own-connections\">25 MB</dd>");
        await Assert.That(card).Contains("<b class=\"m-value\" data-usage=\"shared-about\">about 268 MB</b>");
        await Assert.That(card).Contains("<a class=\"more\" id=\"l-per-session\" href=\"sessions?tab=3\">Per session");
        await Assert.That(card).Contains("data-usage-chart=\"cpu\"");
        await Assert.That(card).Contains("data-usage-chart=\"own\"");

        // Every figure the card draws is one each reading sends, so none stands still.
        var sent = UsageContent.Figures(usage);

        foreach (var key in UsageKey().Matches(card).Select(static match => match.Groups[1].Value))
        {
            await Assert.That(sent.ContainsKey(key)).IsTrue().Because($"'{key}' is drawn and no reading sends it");
        }

        var charts = UsageContent.Charts(usage, Window);

        await Assert.That(charts.Keys.Order().ToList()).IsEquivalentTo(ChartNames);
        await Assert.That(charts["cpu"]).Contains("aria-label=\"CPU over the last 2 min, now 10.0%\"");
        await Assert.That(charts["own"]).Contains("<span class=\"c-top\">2 GB</span>");
        await Assert.That(card).Contains(charts["cpu"]);

        // The first reading knows the memory and not yet the CPU: the CPU chart's name, read
        // aloud, then says nothing of the figure, which beside it says Measuring.
        var firstCharts = UsageContent.Charts(first, Window);

        await Assert.That(firstCharts["cpu"]).Contains("aria-label=\"CPU over the last 2 min\"");
        await Assert.That(firstCharts["own"]).Contains("aria-label=\"Own memory over the last 2 min, now 1,101 MB\"");
    }

    [Test]
    public async Task AReadingWindowsWouldNotGiveSaysNotMeasuredAndNeverZero()
    {
        var usage = PageUsage.Next(null, new UsageReading(new ProcessCounters(0, 0, 0), [], [], 4) { Unreadable = true }, Start, Window);
        var figures = UsageContent.Figures(usage);

        await Assert.That(usage.Unreadable).IsTrue();
        await Assert.That(figures.Values.Distinct().ToList()).IsEquivalentTo(NotMeasuredOnly);
        await Assert.That(UsageContent.Card(View(usage), Window)).Contains("Windows on this PC does not say what each process uses, so BrowserAI cannot show it.");
        await Assert.That(UsageContent.SessionLines(View(usage), Session("0123456789abcdef"))).Contains("<dd data-usage=\"s:0123456789abcdef:cpu\">Not measured</dd>");
        await Assert.That(usage.History.Count).IsEqualTo(0);
    }

    [Test]
    public async Task EachReadingSendsEveryFigureAndBothCharts()
    {
        var usage = PageUsage.Next(null, Card(0), Start, Window);

        using var data = System.Text.Json.JsonDocument.Parse(UsageContent.EventData(usage, Window));

        var text = data.RootElement.GetProperty("text").EnumerateObject().Select(static member => member.Name).Order().ToList();
        var charts = data.RootElement.GetProperty("charts").EnumerateObject().Select(static member => member.Name).Order().ToList();

        await Assert.That(text).IsEquivalentTo(UsageContent.Figures(usage).Keys.Order().ToList());
        await Assert.That(charts).IsEquivalentTo(ChartNames);
        await Assert.That(data.RootElement.GetProperty("text").GetProperty("own-connections").GetString()).IsEqualTo("25 MB");
    }

    [Test]
    public async Task TheChartsTopsAreTheSmallestStepAboveEveryReading()
    {
        PageUsage? usage = null;

        // 4 % of four processors for two readings, then the window's own-memory top past 1 GB.
        usage = PageUsage.Next(usage, Reading(background: 0, own: 300 * Megabyte), Start, Window);
        usage = PageUsage.Next(usage, Reading(background: 3_200_000, own: 300 * Megabyte), Start.AddSeconds(2), Window);

        await Assert.That(UsageContent.Charts(usage, Window)["cpu"]).Contains("<span class=\"c-top\">5%</span>");
        await Assert.That(UsageContent.Charts(usage, Window)["own"]).Contains("<span class=\"c-top\">512 MB</span>");

        usage = PageUsage.Next(usage, Reading(background: 3_200_000, own: 1_300 * Megabyte), Start.AddSeconds(4), Window);

        await Assert.That(UsageContent.Charts(usage, Window)["own"]).Contains("<span class=\"c-top\">2 GB</span>");

        // Before any reading, both charts are drawn empty and the figures say they are being measured.
        await Assert.That(UsageContent.Charts(null, Window)["cpu"]).DoesNotContain("c-line");
        await Assert.That(UsageContent.Figures(null)["cpu"]).IsEqualTo("Measuring");
    }

    /// <summary>The card's reading: 46 MB of background, two relays and two sessions, as the memory arm's, with the background's CPU given.</summary>
    private static UsageReading Card(long background) =>
        new(
            new ProcessCounters(background, 46 * Megabyte, 30 * Megabyte),
            [new RelayReading(7, 70, new ProcessCounters(0, 12 * Megabyte, 25 * Megabyte)), new RelayReading(8, 80, new ProcessCounters(0, 13 * Megabyte, 26 * Megabyte))],
            [new SessionReading("a", 0, 400 * Megabyte, 60 * Megabyte, 268 * Megabyte), new SessionReading("b", 0, 512 * Megabyte, 58 * Megabyte, 200 * Megabyte)],
            4);

    /// <summary>A share to six places, which is as far as the arithmetic above is done by hand.</summary>
    private static double? Rounded(double? share) => share is { } known ? Math.Round(known, 6) : null;

    /// <summary>A reading with the background's CPU and memory, whatever relays and sessions an arm names, on four processors unless it says.</summary>
    private static UsageReading Reading(
        long background = 0,
        long own = 0,
        IReadOnlyList<RelayReading>? relays = null,
        IReadOnlyList<SessionReading>? sessions = null,
        int processors = 4) =>
        new(new ProcessCounters(background, own, 0), relays ?? [], sessions ?? [], processors);

    private static SessionEntry Session(string id) => new(id, @"C:\work\session", "Testing", BrowserOpen: true, []);

    private static PageView View(PageUsage? usage) =>
        new(
            new PageFacts
            {
                Version = "9.0.0",
                InstallRoot = @"C:\install",
                DataRoot = @"C:\data",
                LogDirectory = @"C:\data\logs",
                ServerCommand = null,
                ServerRefusal = null,
                RelayHold = RelayConstants.IdleCountdown,
            },
            SessionsSnapshot.Empty,
            null)
        {
            Tab = 3,
            Usage = usage,
        };

    [GeneratedRegex("data-usage=\"([^\"]+)\"")]
    private static partial Regex UsageKey();
}

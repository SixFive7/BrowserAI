// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BrowserAI.App.Page;

/// <summary>
/// The page's CPU and memory figures, as HTML: the overview's card, one session's lines for
/// the map's inspector, and the words every figure is written in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 59 b, 62 a and 65 a, 2026-10-11</b>, and his words verbatim: <i>"Show
/// this all with realtime updates. Show the separate process memory and the shared base
/// separately. The page has to show it all clearly."</i> A session's own memory is exact and
/// is said per session; the shared memory is said once, for all of BrowserAI, with
/// <i>about</i>, and a session's lines point at that one figure on the overview.
/// </para>
/// <para>
/// <b>Every live figure carries <c>data-usage</c></b>, the name of the figure, and every chart
/// <c>data-usage-chart</c>; the page's script writes each new reading's words and charts into
/// every element that carries the name, so a figure is updated where it stands without the
/// page around it being drawn again. The words and the charts come from <see cref="Figures"/>
/// and <see cref="Charts"/>, the same as the page's first draw.
/// </para>
/// </remarks>
internal static class UsageContent
{
    /// <summary>What a figure says before a reading has measured it.</summary>
    public const string Measuring = "Measuring";

    /// <summary>What a figure says where Windows would not report it.</summary>
    public const string NotMeasured = "Not measured";

    /// <summary>The id of the overview's card that says the totals, which a session's shared line links to.</summary>
    public const string CardId = "card-usage";

    /// <summary>What a session's shared line says before any reading.</summary>
    private const string SharedUnmeasured = "Counted once for all of BrowserAI";

    /// <summary>The chart's drawing box, which the browser stretches to the chart's size.</summary>
    private const double ChartWidth = 600;

    private const double ChartHeight = 100;

    private const long Megabyte = 1024 * 1024;

    /// <summary>The CPU charts' possible tops, as percentages: the smallest above every reading is used.</summary>
    private static readonly int[] CpuTops = [1, 2, 5, 10, 20, 25, 50, 100];

    /// <summary>
    /// One session's CPU and own memory, and where its shared memory is counted: three
    /// <c>dt</c> and <c>dd</c> pairs, for the inspector's own <c>dl</c>.
    /// </summary>
    /// <param name="view">What is true now; <see cref="PageView.Usage"/> is the newest reading, or <see langword="null"/> before the first.</param>
    /// <param name="session">The session.</param>
    /// <returns>The HTML.</returns>
    public static string SessionLines(PageView view, SessionEntry session)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(session);

        var id = PageContent.Text(session.Id);
        var figures = Figures(view.Usage);
        var unmeasured = view.Usage is { Unreadable: true } ? NotMeasured : Measuring;

        return new StringBuilder()
            .Append("<dt>CPU</dt><dd data-usage=\"").Append(SessionKey(session.Id, "cpu")).Append("\">").Append(PageContent.Text(figures.GetValueOrDefault(SessionKey(session.Id, "cpu"), unmeasured))).Append("</dd>")
            .Append("<dt>Own memory</dt><dd data-usage=\"").Append(SessionKey(session.Id, "own")).Append("\">").Append(PageContent.Text(figures.GetValueOrDefault(SessionKey(session.Id, "own"), unmeasured))).Append("</dd>")
            .Append("<dt>Shared memory</dt><dd><a id=\"l-shared-").Append(id).Append("\" href=\"").Append(PageContent.Home(view.Tab)).Append('#').Append(CardId).Append("\" data-usage=\"shared-all\">")
            .Append(PageContent.Text(figures["shared-all"])).Append("</a></dd>")
            .ToString();
    }

    /// <summary>
    /// The overview's card: CPU and own memory, each with its chart, own memory in its four
    /// parts, and the shared memory once, with <i>about</i>.
    /// </summary>
    /// <param name="view">What is true now.</param>
    /// <param name="window">How far back the charts reach.</param>
    /// <returns>The HTML.</returns>
    public static string Card(PageView view, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(view);

        var usage = view.Usage;
        var figures = Figures(usage);
        var charts = Charts(usage, window);

        var html = new StringBuilder("<article class=\"card\" id=\"").Append(CardId).Append("\">\n")
            .Append("<div class=\"card-head\">").Append(PageIcons.Of("cpu")).Append("<h2>CPU and memory</h2>")
            .Append("<span class=\"live\"><span class=\"pulse\" aria-hidden=\"true\"></span>Live</span>")
            .Append("<a class=\"more\" id=\"l-per-session\" href=\"").Append(PageContent.Route(PageKind.Sessions, view.Tab)).Append("\">Per session").Append(PageIcons.Of("chevron")).Append("</a></div>\n")
            .Append("<p class=\"caption\">")
            .Append(PageContent.Text(usage is { Unreadable: true }
                ? "Windows on this PC does not say what each process uses, so BrowserAI cannot show it."
                : "What BrowserAI's own processes use on this PC now."))
            .Append("</p>\n<div class=\"metrics tall\">");

        _ = html.Append("<div class=\"metric\" id=\"m-cpu\"><div class=\"m-top\">").Append(PageIcons.Of("cpu")).Append("<span class=\"m-label\">CPU</span></div>")
            .Append("<b class=\"m-value\" data-usage=\"cpu\">").Append(PageContent.Text(figures["cpu"])).Append("</b>")
            .Append("<div class=\"chart grow\" data-usage-chart=\"cpu\">").Append(charts["cpu"]).Append("</div>")
            .Append("<p class=\"m-why\">").Append(PageContent.Text("Share of this PC's processor that BrowserAI's processes use now, all cores together.")).Append("</p></div>");

        _ = html.Append("<div class=\"metric\" id=\"m-own\"><div class=\"m-top\">").Append(PageIcons.Of("memory")).Append("<span class=\"m-label\">Own memory</span></div>")
            .Append("<b class=\"m-value\" data-usage=\"own\">").Append(PageContent.Text(figures["own"])).Append("</b>")
            .Append("<div class=\"chart grow\" data-usage-chart=\"own\">").Append(charts["own"]).Append("</div>")
            .Append("<dl class=\"m-parts\">")
            .Append("<dt>Browsers</dt><dd data-usage=\"own-browsers\">").Append(PageContent.Text(figures["own-browsers"])).Append("</dd>")
            .Append("<dt>Browser servers</dt><dd data-usage=\"own-servers\">").Append(PageContent.Text(figures["own-servers"])).Append("</dd>")
            .Append("<dt>Background</dt><dd data-usage=\"own-background\">").Append(PageContent.Text(figures["own-background"])).Append("</dd>")
            .Append("<dt>Connections, one per conversation</dt><dd data-usage=\"own-connections\">").Append(PageContent.Text(figures["own-connections"])).Append("</dd>")
            .Append("</dl>")
            .Append("<p class=\"m-why\">").Append(PageContent.Text("Memory only BrowserAI holds: its background, each conversation's connection, and each open browser with the server that drives it.")).Append("</p></div>");

        _ = html.Append("<div class=\"metric\" id=\"m-shared\"><div class=\"m-top\">").Append(PageIcons.Of("shared")).Append("<span class=\"m-label\">Shared memory</span></div>")
            .Append("<b class=\"m-value\" data-usage=\"shared-about\">").Append(PageContent.Text(figures["shared-about"])).Append("</b>")
            .Append("<p class=\"m-why\">").Append(PageContent.Text("Memory its processes share with other programs, such as the browser's program files. Windows does not say with whom, so this is the most that any one of them shares.")).Append("</p></div>");

        return html.Append("</div>\n</article>\n").ToString();
    }

    /// <summary>
    /// Every live figure's words, by the name its element carries in <c>data-usage</c>: what
    /// the page draws first and what each new reading sends.
    /// </summary>
    /// <param name="usage">The newest reading, or <see langword="null"/> before the first.</param>
    /// <returns>Each figure's words, as plain text.</returns>
    public static Dictionary<string, string> Figures(PageUsage? usage)
    {
        var none = usage is { Unreadable: true } ? NotMeasured : Measuring;
        var measured = usage is { Unreadable: false } ? usage : null;

        var figures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cpu"] = measured is null ? none : Cpu(measured.Cpu),
            ["own"] = measured is null ? none : Megabytes(measured.Own.Total),
            ["own-browsers"] = measured is null ? none : Megabytes(measured.Own.Browsers),
            ["own-servers"] = measured is null ? none : Megabytes(measured.Own.BrowserServers),
            ["own-background"] = measured is null ? none : Megabytes(measured.Own.Background),
            ["own-connections"] = measured is null ? none : Megabytes(measured.Own.Connections),
            ["shared"] = measured is null ? none : Megabytes(measured.Shared),
            ["shared-about"] = measured is null ? none : "about " + Megabytes(measured.Shared),
            ["shared-all"] = measured is null
                ? usage is { Unreadable: true } ? NotMeasured : SharedUnmeasured
                : string.Create(CultureInfo.InvariantCulture, $"About {Megabytes(measured.Shared)} for all of BrowserAI"),
        };

        if (measured is null)
        {
            return figures;
        }

        foreach (var (id, session) in measured.Sessions)
        {
            figures[SessionKey(id, "cpu")] = Cpu(session.Cpu);
            figures[SessionKey(id, "own")] = Megabytes(session.OwnBytes);
        }

        foreach (var (pid, relay) in measured.Relays)
        {
            figures[string.Create(CultureInfo.InvariantCulture, $"r:{pid}:cpu")] = Cpu(relay.Cpu);
            figures[string.Create(CultureInfo.InvariantCulture, $"r:{pid}:own")] = Megabytes(relay.OwnBytes);
        }

        return figures;
    }

    /// <summary>The two charts, by the name their element carries in <c>data-usage-chart</c>: <c>cpu</c> and <c>own</c>.</summary>
    /// <param name="usage">The newest reading, or <see langword="null"/> before the first.</param>
    /// <param name="window">How far back the charts reach.</param>
    /// <returns>Each chart's inner HTML.</returns>
    public static Dictionary<string, string> Charts(PageUsage? usage, TimeSpan window)
    {
        var points = usage is { Unreadable: false } ? usage.History : [];
        var at = usage?.At ?? DateTimeOffset.UnixEpoch;
        var cpu = points.Where(static point => point.Cpu is not null).Select(point => (point.At, Value: point.Cpu!.Value * 100)).ToList();
        var own = points.Select(static point => (point.At, Value: point.OwnBytes / (double)Megabyte)).ToList();

        var cpuTop = CpuTops.FirstOrDefault(top => cpu.All(point => point.Value <= top), CpuTops[^1]);
        var ownTop = MemoryTop(own.Count is 0 ? 0 : own.Max(static point => point.Value));

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The CPU's figure is named in the chart's label only once it is known: a first
            // reading knows the memory and not yet the CPU (the reader pass of 2026-10-12,
            // previously "CPU over the last 2 min, now Measuring").
            ["cpu"] = Chart(cpu, at, window, cpuTop, string.Create(CultureInfo.InvariantCulture, $"{cpuTop}%"), "CPU", usage is { Unreadable: false, Cpu: { } share } ? Cpu(share) : null),
            ["own"] = Chart(own, at, window, ownTop, MemoryLabel(ownTop), "Own memory", usage is { Unreadable: false } ? Megabytes(usage.Own.Total) : null),
        };
    }

    /// <summary>What one new reading sends every tab that shows the figures: each figure's words and each chart.</summary>
    /// <param name="usage">The newest reading.</param>
    /// <param name="window">How far back the charts reach.</param>
    /// <returns>The event's data, one line of JSON.</returns>
    public static string EventData(PageUsage usage, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("text");

            foreach (var (name, words) in Figures(usage))
            {
                writer.WriteString(name, words);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("charts");

            foreach (var (name, markup) in Charts(usage, window))
            {
                writer.WriteString(name, markup);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The name one session's figure carries in <c>data-usage</c>.</summary>
    /// <param name="id">The session's opaque name.</param>
    /// <param name="figure"><c>cpu</c> or <c>own</c>.</param>
    /// <returns>The name.</returns>
    public static string SessionKey(string id, string figure) => "s:" + PageContent.Text(id) + ":" + figure;

    /// <summary>A share of this PC's processor, as a percentage to one place, or <see cref="Measuring"/>.</summary>
    /// <param name="share">The share, from 0 to 1, or <see langword="null"/> before it is known.</param>
    /// <returns>The words.</returns>
    public static string Cpu(double? share) =>
        share is { } known ? string.Create(CultureInfo.InvariantCulture, $"{known * 100:0.0}%") : Measuring;

    /// <summary>Memory in whole megabytes, as Task Manager counts them, with thousands grouped.</summary>
    /// <param name="bytes">The memory.</param>
    /// <returns>The words.</returns>
    public static string Megabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Math.Max(0, bytes) / (1024.0 * 1024.0), MidpointRounding.AwayFromZero):#,0} MB");

    /// <summary>
    /// One chart: the readings of the window placed by when they were taken, the newest
    /// segment and the newest reading drawn apart, a line at the top and one at the bottom.
    /// </summary>
    private static string Chart(List<(DateTimeOffset At, double Value)> points, DateTimeOffset at, TimeSpan window, double top, string topLabel, string what, string? now)
    {
        var since = at - window;
        var placed = points
            .Select(point => (X: Math.Clamp((point.At - since) / window, 0, 1) * ChartWidth, Y: (1 - Math.Clamp(point.Value / top, 0, 1)) * ChartHeight))
            .ToList();

        var html = new StringBuilder()
            .Append("<span class=\"c-top\">").Append(PageContent.Text(topLabel)).Append("</span>")
            .Append(string.Create(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {ChartWidth:0} {ChartHeight:0}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\""))
            .Append(PageContent.Text(now is null ? $"{what} over the last {PageWords.Minutes(window)}" : $"{what} over the last {PageWords.Minutes(window)}, now {now}"))
            .Append("\">")
            .Append(string.Create(CultureInfo.InvariantCulture, $"<path class=\"c-grid\" d=\"M0 0 H{ChartWidth:0}\"/><path class=\"c-base\" d=\"M0 {ChartHeight:0} H{ChartWidth:0}\"/>"));

        if (placed.Count > 0)
        {
            var (firstX, _) = placed[0];
            var (lastX, lastY) = placed[^1];

            _ = html.Append("<path class=\"c-area\" d=\"").Append(string.Create(CultureInfo.InvariantCulture, $"M{firstX:0.0} {ChartHeight:0}")).Append(Through(placed)).Append(string.Create(CultureInfo.InvariantCulture, $" L{lastX:0.0} {ChartHeight:0} Z\"/>"))
                .Append("<path class=\"c-line\" d=\"M").Append(Through(placed)[2..]).Append("\"/>");

            if (placed.Count > 1)
            {
                _ = html.Append("<path class=\"c-now\" d=\"M").Append(Through(placed.Skip(placed.Count - 2))[2..]).Append("\"/>");
            }

            _ = html.Append(string.Create(CultureInfo.InvariantCulture, $"<path class=\"c-ring\" d=\"M{lastX:0.0} {lastY:0.0} h0\"/><path class=\"c-dot\" d=\"M{lastX:0.0} {lastY:0.0} h0\"/>"));
        }

        return html.Append("</svg>")
            .Append("<span class=\"c-x\"><span>").Append(PageContent.Text(PageWords.Minutes(window) + " ago")).Append("</span><span>now</span></span>")
            .ToString();
    }

    /// <summary>The points as one line's segments, each <c> L x y</c>.</summary>
    private static string Through(IEnumerable<(double X, double Y)> points) =>
        string.Concat(points.Select(static point => string.Create(CultureInfo.InvariantCulture, $" L{point.X:0.0} {point.Y:0.0}")));

    /// <summary>The own-memory chart's top, in megabytes: the smallest power of two from 256 MB above every reading.</summary>
    private static double MemoryTop(double highest)
    {
        var top = 256.0;

        while (top < highest)
        {
            top *= 2;
        }

        return top;
    }

    /// <summary>A chart's top in the words its label says: megabytes below a gigabyte, whole gigabytes from one.</summary>
    private static string MemoryLabel(double megabytes) =>
        megabytes < 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{megabytes:0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{megabytes / 1024:0} GB");
}

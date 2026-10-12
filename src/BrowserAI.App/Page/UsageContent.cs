// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;

namespace BrowserAI.App.Page;

/// <summary>
/// The page's CPU and memory figures, as HTML: one session's lines for the map's inspector,
/// and the words every figure is written in.
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
/// <b>Every live figure carries <c>data-usage</c></b>, the name of the figure, and the page's
/// script writes each new reading's words into every element that carries the name, so a
/// figure is updated where it stands without the page around it being drawn again. The words
/// come from <see cref="Figures"/>, the same as the page's first draw.
/// </para>
/// </remarks>
internal static class UsageContent
{
    /// <summary>What a figure says before a reading has measured it.</summary>
    public const string Measuring = "Measuring";

    /// <summary>The id of the overview's card that says the totals, which a session's shared line links to.</summary>
    public const string CardId = "card-usage";

    /// <summary>What a session's shared line says before any reading.</summary>
    private const string SharedUnmeasured = "Counted once for all of BrowserAI";

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
        var usage = view.Usage?.SessionOf(session.Id);

        return new StringBuilder()
            .Append("<dt>CPU</dt><dd data-usage=\"").Append(SessionKey(session.Id, "cpu")).Append("\">").Append(PageContent.Text(Cpu(usage?.Cpu))).Append("</dd>")
            .Append("<dt>Own memory</dt><dd data-usage=\"").Append(SessionKey(session.Id, "own")).Append("\">").Append(PageContent.Text(usage is null ? Measuring : Megabytes(usage.OwnBytes))).Append("</dd>")
            .Append("<dt>Shared memory</dt><dd><a id=\"l-shared-").Append(id).Append("\" href=\"").Append(PageContent.Home(view.Tab)).Append('#').Append(CardId).Append("\" data-usage=\"shared-all\">")
            .Append(PageContent.Text(figures["shared-all"])).Append("</a></dd>")
            .ToString();
    }

    /// <summary>
    /// Every live figure's words, by the name its element carries in <c>data-usage</c>: what
    /// the page draws first and what each new reading sends.
    /// </summary>
    /// <param name="usage">The newest reading, or <see langword="null"/> before the first.</param>
    /// <returns>Each figure's words, as plain text.</returns>
    public static Dictionary<string, string> Figures(PageUsage? usage)
    {
        var figures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cpu"] = Cpu(usage?.Cpu),
            ["own"] = usage is null ? Measuring : Megabytes(usage.Own.Total),
            ["own-browsers"] = usage is null ? Measuring : Megabytes(usage.Own.Browsers),
            ["own-servers"] = usage is null ? Measuring : Megabytes(usage.Own.BrowserServers),
            ["own-background"] = usage is null ? Measuring : Megabytes(usage.Own.Background),
            ["own-connections"] = usage is null ? Measuring : Megabytes(usage.Own.Connections),
            ["shared"] = usage is null ? Measuring : Megabytes(usage.Shared),
            ["shared-all"] = usage is null ? SharedUnmeasured : string.Create(CultureInfo.InvariantCulture, $"About {Megabytes(usage.Shared)} for all of BrowserAI"),
        };

        if (usage is null)
        {
            return figures;
        }

        foreach (var (id, session) in usage.Sessions)
        {
            figures[SessionKey(id, "cpu")] = Cpu(session.Cpu);
            figures[SessionKey(id, "own")] = Megabytes(session.OwnBytes);
        }

        foreach (var (pid, relay) in usage.Relays)
        {
            figures[string.Create(CultureInfo.InvariantCulture, $"r:{pid}:cpu")] = Cpu(relay.Cpu);
            figures[string.Create(CultureInfo.InvariantCulture, $"r:{pid}:own")] = Megabytes(relay.OwnBytes);
        }

        return figures;
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
}

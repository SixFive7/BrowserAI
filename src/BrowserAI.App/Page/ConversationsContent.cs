// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// The conversations page: every conversation, BrowserAI and every session as C's live map,
/// and the same as a list for a narrow window or a crowded day.
/// </summary>
/// <remarks>
/// <para>
/// <b>40 a, F, the maintainer's choice of 2026-10-11</b>: C's map as the page the home's
/// <i>See all</i> leads to. Conversations on the left, BrowserAI in the middle, browser
/// sessions on the right, a line from each conversation through BrowserAI to the sessions it
/// uses, and an inspector beside the map for whatever is clicked. Each card says its state in
/// words and each line carries a pattern as well as a colour, so neither carries the meaning
/// alone.
/// </para>
/// <para>
/// <b>60, the maintainer's choice of 2026-10-11, in his words verbatim: <i>"60 I like C"</i></b>,
/// variant C, named lines, of the variants put to him after he wrote: <i>"the visual line
/// differences between: Window on your screen / Hidden browser / No browser open / Kept, no
/// conversation / Are very difficult to see. Use more different lines and colors here. But
/// keep it in style and pretty. Also, it is not clear you can select the browser session.
/// Communicate that better."</i> Each line on from BrowserAI says its state in a word as well
/// as by its colour and pattern, the legend draws a sample of each, every card carries a tab
/// that reads <i>Details</i>, and <i>Selected</i> on the card chosen, and the inspector with
/// nothing chosen says how to choose. <b>A conversation's half of its line carries every state
/// its sessions have</b>, one strand each, where it carried its first session's alone.
/// </para>
/// <para>
/// <b>The map becomes a list below <see cref="PageAssets.MapMinimumWidth"/> pixels of
/// window</b>, by the stylesheet, and <b>when it would be too crowded to read</b>: more than
/// <see cref="MostMapRows"/> rows, a row being a conversation or a session no conversation
/// uses, or more than <see cref="MostMapSessions"/> sessions. The write-up found the map
/// crowded past about eight conversations. Past either limit the map is not drawn at all.
/// </para>
/// <para>
/// <b>It keeps the sessions page's address</b>, <c>sessions?tab=N</c>, which
/// <c>BrowserAI.exe --sessions</c> opens, and it is live (46 a): the page's watch sends it a
/// new state on every change. <b>Since 2026-10-12 it is also every session's page</b>: an
/// address that names a session in <c>id</c> opens the page with that session chosen, and the
/// session page that address used to open is dropped, its lines in the inspector here.
/// </para>
/// </remarks>
internal static class ConversationsContent
{
    /// <summary>The most rows the map draws: a row is a conversation, or a session no conversation uses.</summary>
    public const int MostMapRows = 8;

    /// <summary>The most sessions the map draws.</summary>
    public const int MostMapSessions = 12;

    /// <summary>What a trace is, said beside a session's traces.</summary>
    /// <remarks>The session page's line, moved here with its page on 2026-10-12.</remarks>
    private const string TracesNote = "Recordings Playwright can replay, in the session's output folder.";

    /// <summary>Who deletes a session's folder, said beside its <i>Open folder</i> button (41 a).</summary>
    /// <remarks>
    /// The session page's notice, moved here with its page on 2026-10-12: its title, <i>This page
    /// deletes nothing</i>, is the first sentence's second half.
    /// </remarks>
    private const string KeepNote = "BrowserAI never deletes a session folder on its own, and this page deletes nothing. The agent that made this one can delete it, and so can you: open its folder and delete it in Explorer.";

    /// <summary>The order a conversation's strands are drawn in: a window, a hidden browser, no browser open.</summary>
    private static readonly string[] StrandOrder = ["w", "h", "c"];

    /// <summary>The conversations page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var board = PageBoard.Of(view.Sessions);
        var chosen = board.SessionById(view.Subject) is { } subject ? "s-" + subject.Id : null;
        var html = new StringBuilder()
            .Append(PageContent.Crumbs(view.Tab, "Conversations and browsers"))
            .Append(PageContent.Title(
                "Conversations and browsers",
                "Every chat in Claude Code or Codex that uses BrowserAI, and the browser sessions it opened. Nothing can be closed from this page: an agent closes its own browser, and a conversation ends when its app closes it."))
            .Append("<div class=\"explain\">")
            .Append(Explain("chat", "Conversation", "A chat in Claude Code or Codex. It is connected to BrowserAI while it is open."))
            .Append(Explain("folder", "Session", "A folder an agent made for its browser work: the browser profile with its logins, screenshots, downloads and a log."))
            .Append(Explain("globe", "Browser", "A session's browser while it runs: hidden, or a window on your screen. It closes by itself when nobody uses it, unless an agent set it never to close."))
            .Append("</div>\n");

        PageContent.AppendNote(html, view.Note);
        AppendNotHeld(html, view, board);

        if (board.Conversations.Count is 0 && board.All.Count is 0)
        {
            return html.Append(PageContent.Empty("chat", "No conversation is using BrowserAI", "Start a conversation in Claude Code or Codex and ask it to use a browser. It appears here as soon as it calls a BrowserAI tool."))
                .ToString();
        }

        var rows = board.Conversations.Count + board.Unlinked.Count;
        var crowded = rows > MostMapRows || board.All.Count > MostMapSessions;

        if (!crowded)
        {
            _ = html.Append(Legend()).Append("<div class=\"map-layout\">\n").Append(Map(view, board, chosen)).Append(Inspector(view, board, chosen)).Append("</div>\n");
        }

        _ = html.Append(crowded ? "<div class=\"list-view crowded\">\n" : "<div class=\"list-view\">\n");

        if (crowded)
        {
            _ = html.Append("<p class=\"small\">There are too many conversations and sessions to draw as a map, so they are listed.</p>\n");
        }

        return html.Append(List(view, board, chosen)).Append("</div>\n").ToString();
    }

    /// <summary>
    /// What a person would see change on the conversations page and the home, with every name
    /// and every countdown's running down left out: two snapshots with the same signature draw
    /// the same pages but for the time left.
    /// </summary>
    /// <remarks>
    /// <b>46 a</b>: the watch compares this once a second, over the snapshot it peeks with no
    /// conversation named, and sends every tab a new state when it moves.
    /// </remarks>
    /// <param name="snapshot">The sessions, as the watch peeked them.</param>
    /// <returns>The signature.</returns>
    public static string Signature(SessionsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var text = new StringBuilder();

        foreach (var server in snapshot.Servers)
        {
            var description = server.Description;

            _ = text.Append("|r:").Append(server.Id).Append(';').Append(description.Role).Append(';').Append(description.Client?.Name)
                .Append(';').Append(description.Client?.Version).Append(';').Append(description.WorkingDirectory).Append(';').Append(description.CallsInFlight)
                .Append(';').Append(server.Window?.Key).Append(';').Append(server.Reconnect).Append(';').Append(server.RecentlyActive);

            foreach (var session in server.Sessions)
            {
                _ = text.Append("|s:").Append(session.Id).Append(';').Append(session.Purpose).Append(';').Append(session.BrowserOpen).Append(';').Append(session.Headed)
                    .Append(';').Append(session.Kept).Append(';').Append(session.DrivenThrough).Append(';').Append(session.IdleCloseAt?.ToUnixTimeMilliseconds())
                    .Append(';').Append(session.Traces.Count);
            }
        }

        return text.ToString();
    }

    /// <summary>The folders of the sessions held now, which a measurement is asked for again when they change.</summary>
    /// <param name="snapshot">The sessions.</param>
    /// <returns>The folders, as one string.</returns>
    public static string HeldFolders(SessionsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return string.Join('|', snapshot.Servers.Where(server => server.IsHost).SelectMany(server => server.Sessions).Select(session => session.Id).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The states a conversation's half of its line is drawn in, one strand each: those its
    /// sessions have, in the order window, hidden, no browser open; <c>n</c> for a conversation
    /// with no session.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-12, for the defect of 2026-10-11's map</b>: the half was drawn in its
    /// first session's state, so a conversation whose first session had no browser open drew
    /// nothing of the window it had on the screen.
    /// </remarks>
    /// <param name="conversation">The conversation.</param>
    /// <returns>The states, separated by spaces.</returns>
    internal static string StrandsOf(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (conversation.Sessions.Count is 0)
        {
            return "n";
        }

        var states = conversation.Sessions.Select(PageParts.ClassOf).ToHashSet(StringComparer.Ordinal);

        return string.Join(' ', StrandOrder.Where(states.Contains));
    }

    /// <summary>What a session's folder weighs, as the last measurement found it.</summary>
    /// <remarks><i>Moved here 2026-10-12 from the session page, which is dropped.</i></remarks>
    /// <param name="view">What is true now.</param>
    /// <param name="id">The session's opaque name.</param>
    /// <returns>The words.</returns>
    internal static string SizeWords(PageView view, string id)
    {
        ArgumentNullException.ThrowIfNull(view);

        return view.Folders?.ById(id) switch
        {
            { Bytes: { } bytes } => PageWords.Size(bytes),
            { } => "Could not be measured",
            null when view.Measuring => "Measuring",
            _ => "Not measured yet",
        };
    }

    /// <summary>
    /// The sample of one line the script draws: the legend's, with its word, and a small one
    /// beside a session in the inspector.
    /// </summary>
    /// <param name="state">The line's state.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="mini">Whether it is the small one, drawn with no word.</param>
    /// <returns>The empty SVG the script draws into.</returns>
    internal static string Sample(string state, int width, int height, bool mini) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"<svg class=\"sample{(mini ? " mini" : string.Empty)}\" data-sample=\"{state}\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" aria-hidden=\"true\" focusable=\"false\"></svg>");

    /// <summary>
    /// What an address that names a session the background does not hold says above the map:
    /// that its folder is not open now, or that nothing knows the name.
    /// </summary>
    /// <remarks>
    /// The session page's two last cases, moved here on 2026-10-12. A folder the measurement
    /// knows is on the storage page, which says what it is; until a measurement has been
    /// taken, nothing is said, since nothing could be.
    /// </remarks>
    private static void AppendNotHeld(StringBuilder html, PageView view, PageBoard board)
    {
        if (view.Subject is not { } id || board.SessionById(id) is not null || view.Folders is not { } measured)
        {
            return;
        }

        _ = html.Append("<div class=\"note said\" role=\"status\">").Append(PageIcons.Of("info")).Append("<div class=\"grow\"><p>");

        _ = measured.ById(id) is { } folder
            ? html.Append(PageContent.Text($"{PageWords.NameOf(folder.Directory)} is not open now, so this page does not show it. The Storage page lists its folder and its size.")).Append("</p></div>")
                .Append(PageContent.LinkButton("l-not-open-storage", PageContent.Route(PageKind.Storage, view.Tab), "See the storage"))
            : html.Append("BrowserAI holds no session by that name now, and its last measurement of the session folders found none.</p></div>");

        _ = html.Append("</div>\n");
    }

    private static string Explain(string icon, string title, string text) =>
        "<div>" + PageIcons.Of(icon) + "<div><b>" + PageContent.Text(title) + "</b><p>" + PageContent.Text(text) + "</p></div></div>";

    /// <summary>The legend: a sample of each line with its words, and where a chosen card's details open.</summary>
    private static string Legend() =>
        "<div class=\"legend-row\"><div class=\"legend-items\">"
        + LegendItem("w", "Window on your screen")
        + LegendItem("h", "Hidden browser")
        + LegendItem("c", "No browser open")
        + LegendItem("k", "Kept, no conversation")
        + "</div><p class=\"hint\">" + PageIcons.Of("pointer") + "Click any card on the map. Its details open here.</p></div>\n";

    private static string LegendItem(string state, string words) =>
        "<span>" + Sample(state, 112, 24, mini: false) + words + "</span>";

    /// <summary>The tab every card carries: <i>Details</i>, and <i>Selected</i> on the card chosen.</summary>
    private static string Cue() =>
        "<span class=\"cue\" aria-hidden=\"true\"><span class=\"off\">Details</span><span class=\"on\">Selected</span>" + PageIcons.Of("chevron") + "</span>";

    private static string Pressed(string key, string? chosen) =>
        string.Equals(key, chosen, StringComparison.Ordinal) ? "true" : "false";

    // ---- The map ----------------------------------------------------------------

    /// <summary>The map: a column of conversations, BrowserAI, and a column of sessions, row by row.</summary>
    private static string Map(PageView view, PageBoard board, string? chosen)
    {
        var now = view.Now;
        var html = new StringBuilder("<div class=\"map\" id=\"map\">\n<svg class=\"wires\" id=\"wires\" aria-hidden=\"true\" focusable=\"false\"></svg>\n")
            .Append("<div class=\"col-head c1\">Conversations</div><div class=\"col-head c3\">BrowserAI</div><div class=\"col-head c5\">Browser sessions</div>\n");
        var rows = board.Conversations.Count + board.Unlinked.Count;
        var row = 0;

        _ = html.Append("<div class=\"hub-cell rows-").Append(Math.Max(1, rows).ToString(CultureInfo.InvariantCulture)).Append("\">").Append(Hub(view, board, chosen)).Append("</div>\n");

        string? lastWindow = null;

        foreach (var conversation in board.Conversations)
        {
            row++;

            var label = conversation.Relay.Window is { } window && !string.Equals(window.Key, lastWindow, StringComparison.Ordinal)
                ? "<div class=\"group-label\">" + PageIcons.Of("code") + PageContent.Text(window.Label()) + "</div>"
                : string.Empty;

            lastWindow = conversation.Relay.Window?.Key;

            _ = html.Append("<div class=\"left r").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" data-row=\"").Append(PageContent.Text(conversation.Key))
                .Append("\" data-line=\"").Append(StrandsOf(conversation)).Append('"').Append(conversation.Working ? " data-live=\"1\"" : string.Empty).Append('>')
                .Append(label).Append(ConversationNode(conversation, chosen)).Append("</div>\n")
                .Append("<div class=\"right r").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" data-row=\"").Append(PageContent.Text(conversation.Key)).Append("\">");

            if (conversation.Sessions.Count is 0)
            {
                _ = html.Append("<p class=\"none\">No browser yet</p>");
            }
            else
            {
                foreach (var session in conversation.Sessions)
                {
                    _ = html.Append(SessionNode(session, conversation.Working, now, chosen));
                }
            }

            _ = html.Append("</div>\n");
        }

        foreach (var session in board.Unlinked)
        {
            row++;

            _ = html.Append("<div class=\"left r").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" data-row=\"s-").Append(PageContent.Text(session.Id)).Append("\" data-line=\"k\">")
                .Append("<p class=\"ghost\">").Append(session.Kept ? "Its conversation ended. BrowserAI keeps the session for the next one." : "No conversation is connected to it now.").Append("</p></div>\n")
                .Append("<div class=\"right r").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" data-row=\"s-").Append(PageContent.Text(session.Id)).Append("\">")
                .Append(SessionNode(session, working: false, now, chosen)).Append("</div>\n");
        }

        return html.Append("</div>\n").ToString();
    }

    private static string ConversationNode(Conversation conversation, string? chosen) =>
        "<button type=\"button\" class=\"node\" id=\"n-" + PageContent.Text(conversation.Key) + "\" data-inspect=\"" + PageContent.Text(conversation.Key)
        + "\" aria-pressed=\"" + Pressed(conversation.Key, chosen) + "\" aria-controls=\"inspect-" + PageContent.Text(conversation.Key) + "\">"
        + PageContent.Badge(conversation.ClientName)
        + "<span class=\"txt\"><b>" + PageContent.Text(conversation.Name) + "</b><small>" + PageContent.Text(conversation.Place + ", " + PageWords.NameOf(conversation.Relay.Description.WorkingDirectory)) + "</small></span>"
        + (conversation.Working ? "<span class=\"tag live\">Working</span>" : string.Empty) + Cue() + "</button>";

    private static string SessionNode(SessionEntry session, bool working, DateTimeOffset now, string? chosen)
    {
        var key = "s-" + session.Id;
        var style = PageParts.ClassOf(session);

        return "<button type=\"button\" class=\"node " + style + "\" id=\"n-" + PageContent.Text(key) + "\" data-inspect=\"" + PageContent.Text(key)
            + "\" data-line=\"" + (session.Kept ? "k" : style) + "\" aria-pressed=\"" + Pressed(key, chosen) + "\" aria-controls=\"inspect-" + PageContent.Text(key) + "\">"
            + "<span class=\"br-ic " + style + "\">" + PageIcons.Of(PageParts.IconOf(session)) + "</span>"
            + "<span class=\"txt\"><b>" + PageContent.Text(PageWords.NameOf(session.Directory)) + "</b>"
            + (session.Kept ? "<small>Kept, no conversation</small>" : string.Empty)
            + "<span class=\"state\">" + PageParts.Short(session, working, now) + "</span></span>" + Cue() + "</button>";
    }

    private static string Hub(PageView view, PageBoard board, string? chosen)
    {
        var facts = view.Facts;
        var html = new StringBuilder("<button type=\"button\" class=\"hub node\" id=\"n-hub\" data-inspect=\"hub\" aria-pressed=\"").Append(Pressed("hub", chosen)).Append("\" aria-controls=\"inspect-hub\">")
            .Append(PageIcons.Brand("hub")).Append("<span class=\"hub-name\">").Append(PageContent.Text($"BrowserAI {facts.Version}")).Append("</span>");

        if (facts.StartedAt is { } started)
        {
            _ = html.Append("<span class=\"small\">").Append(PageContent.Text("Running since " + PageWords.When(started, view.Now))).Append("</span>");
        }

        _ = html.Append("<span class=\"hub-stat\">")
            .Append(HubStat("Conversations", board.Conversations.Count))
            .Append(HubStat("Open browsers", board.OpenBrowsers))
            .Append(HubStat("On your screen", board.OnScreen))
            .Append("</span>");

        _ = view.Holds switch
        {
            { State: UpdateHoldState.Held, Version: { } version } holds => html.Append("<span class=\"upd\"><b>").Append(PageContent.Text($"Update {version} ready")).Append("</b>")
                .Append(holds.WaitAt(view.Now) is { Wait: UpdateWait.Counting, Ends: { } ends }
                    ? "installs in " + PageContent.Countdown(ends, view.Now) + " if nothing uses BrowserAI"
                    : "waits for what holds it")
                .Append("</span>"),
            { State: UpdateHoldState.Installing, Version: { } installing } => html.Append("<span class=\"upd\"><b>").Append(PageContent.Text($"{installing} is installing")).Append("</b></span>"),
            _ => html.Append("<span class=\"upd ok\"><b>No update is waiting</b>")
                .Append(view.Updates.LastCheck is { } checkedAt ? PageContent.Text("checked at " + PageWords.When(checkedAt, view.Now)) : string.Empty).Append("</span>"),
        };

        return html.Append(Cue()).Append("</button>").ToString();
    }

    private static string HubStat(string label, int count) =>
        "<span class=\"hub-row\"><span>" + PageContent.Text(label) + "</span><b>" + count.ToString(CultureInfo.InvariantCulture) + "</b></span>";

    // ---- The inspector ------------------------------------------------------------

    /// <summary>What is chosen on the map: a panel per card, all drawn, one shown by the script.</summary>
    /// <remarks>
    /// <b>The panel of the card an address chose is drawn shown</b>, and the script takes that
    /// choice over when the page opens; with nothing chosen, the inspector says how to choose.
    /// </remarks>
    private static string Inspector(PageView view, PageBoard board, string? chosen)
    {
        var html = new StringBuilder("<aside class=\"inspector\" id=\"inspector\" aria-label=\"What is chosen on the map\" aria-live=\"polite\">\n")
            .Append("<div class=\"inspect-none\" id=\"inspect-none\"").Append(chosen is null ? string.Empty : " hidden").Append("><span class=\"point\">").Append(PageIcons.Of("arrowLeft")).Append("</span>")
            .Append("<p class=\"none-title\">Click any card on the map</p>")
            .Append("<p>Conversations on the left, BrowserAI in the middle and browser sessions on the right can all be clicked. Each one opens here.</p>")
            .Append("<p class=\"keys\"><kbd>Tab</kbd> and the arrow keys move between cards. <kbd>Enter</kbd> shows one, and <kbd>Esc</kbd> clears it.</p></div>\n")
            .Append(HubPanel(view, chosen));

        foreach (var conversation in board.Conversations)
        {
            _ = html.Append(ConversationPanel(conversation, chosen));
        }

        foreach (var session in board.All)
        {
            _ = html.Append(SessionPanel(view, board, session, chosen));
        }

        return html.Append("</aside>\n").ToString();
    }

    private static string Panel(string key, string icon, string kind, string title, string what, string? chosen) =>
        "<section class=\"inspect\" id=\"inspect-" + PageContent.Text(key) + "\"" + (string.Equals(key, chosen, StringComparison.Ordinal) ? string.Empty : " hidden") + ">"
        + "<p class=\"kind\">" + PageIcons.Of(icon) + PageContent.Text(kind) + "</p><h2>" + PageContent.Text(title) + "</h2>"
        + "<div class=\"what\">" + PageIcons.Of("question") + "<p>" + PageContent.Text(what) + "</p></div>";

    /// <summary>A card named in the inspector, which chooses that card on the map.</summary>
    private static string Pick(string id, string key, string text, string? sample = null, bool inline = false) =>
        "<button type=\"button\" class=\"pick" + (inline ? " inline" : string.Empty) + "\" id=\"" + PageContent.Text(id) + "\" data-inspect=\"" + PageContent.Text(key) + "\">"
        + (sample ?? string.Empty) + (inline ? PageContent.Text(text) : "<span>" + PageContent.Text(text) + "</span>") + PageIcons.Of("chevron") + "</button>";

    private static string HubPanel(PageView view, string? chosen)
    {
        var facts = view.Facts;
        var html = new StringBuilder(Panel(
                "hub",
                "info",
                "BrowserAI",
                $"BrowserAI {facts.Version}",
                "BrowserAI runs in the background on this PC and starts by itself when you sign in. Claude Code and Codex reach it for each conversation, and it gives the agent a real browser.",
                chosen))
            .Append("<dl>");

        if (InstallContent.RunningSince(facts, view.Now) is { } running)
        {
            _ = html.Append("<dt>Running</dt><dd>").Append(PageContent.Text(running)).Append("</dd>");
        }

        if (facts.InstallRoot is { Length: > 0 } install)
        {
            _ = html.Append("<dt>Installed in</dt><dd><code>").Append(PageContent.Text(install)).Append("</code></dd>");
        }

        _ = html.Append("<dt>Data</dt><dd><code>").Append(PageContent.Text(facts.DataRoot)).Append("</code></dd>")
            .Append("<dt>Updates</dt><dd>").Append(PageContent.Text(UpdatePageContent.SourceWords(view.Updates, facts))).Append("</dd></dl>")
            .Append("<div class=\"btn-row\">");

        if (facts.InstallRoot is { Length: > 0 })
        {
            _ = html.Append(PageContent.Button("ib-open-folder-install", "open-folder", "Install folder", "btn", "folder", ("folder", "install")));
        }

        return html.Append(PageContent.Button("ib-open-folder-data", "open-folder", "Data", "btn", "folder", ("folder", "data")))
            .Append(PageContent.Button("ib-open-folder-logs", "open-folder", "Logs", "btn", "file", ("folder", "logs")))
            .Append("</div></section>\n").ToString();
    }

    private static string ConversationPanel(Conversation conversation, string? chosen)
    {
        var app = PageWords.AppOf(conversation.ClientName);
        var description = conversation.Relay.Description;
        var html = new StringBuilder(Panel(
                conversation.Key,
                "chat",
                "Conversation",
                conversation.Name,
                $"A chat in {app}. It is connected to BrowserAI while it is open, and it ends when {app} closes it, never from here.",
                chosen))
            .Append("<dl>")
            .Append("<dt>App</dt><dd>").Append(PageContent.Text(PageWords.AppWithVersion(conversation.ClientName, description.Client?.Version) + Where(conversation))).Append("</dd>")
            .Append("<dt>Folder</dt><dd><code>").Append(PageContent.Text(description.WorkingDirectory)).Append("</code></dd>")
            .Append("<dt>Now</dt><dd>").Append(conversation.Working ? "A call is running." : "Idle.").Append("</dd>")
            .Append("<dt>After an update</dt><dd>").Append(PageContent.Text(PageWords.Reconnect(conversation.Relay.Reconnect))).Append("</dd></dl>");

        if (conversation.Sessions.Count is 0)
        {
            _ = html.Append("<p class=\"small\">No browser yet.</p>");
        }
        else
        {
            // Each one a card to choose, since 2026-10-12 (previously a link to its session's page).
            _ = html.Append("<p class=\"small\">Its browser sessions:</p><ul class=\"plain picks\">");

            foreach (var session in conversation.Sessions)
            {
                _ = html.Append("<li>")
                    .Append(Pick("ip-" + conversation.Key + "-" + session.Id, "s-" + session.Id, PageWords.NameOf(session.Directory), Sample(PageParts.ClassOf(session), 30, 12, mini: true)))
                    .Append("</li>");
            }

            _ = html.Append("</ul>");
        }

        return html.Append("</section>\n").ToString();
    }

    private static string Where(Conversation conversation) =>
        conversation.Relay.Window is not null ? ", in VS Code"
        : conversation.Relay.Reconnect is RelayReconnect.McpReconnect && !PageWords.IsCodex(conversation.ClientName) ? ", in a terminal"
        : string.Empty;

    /// <summary>One session's panel.</summary>
    /// <remarks>
    /// <b>It says what the session page said</b>, since that page was dropped on 2026-10-12: what
    /// a trace is, who deletes the folder, and the conversation that uses the session, which
    /// is a card to choose here.
    /// </remarks>
    private static string SessionPanel(PageView view, PageBoard board, SessionEntry session, string? chosen)
    {
        var key = "s-" + session.Id;
        var user = board.UsedBy(session);
        var html = new StringBuilder(Panel(
                key,
                "globe",
                "Browser session",
                PageWords.NameOf(session.Directory),
                "A folder an agent made for its browser work: the browser profile with its logins and cookies, screenshots, downloads and a log.",
                chosen))
            .Append("<dl class=\"lead\"><dt>Browser</dt><dd><span class=\"dd-sample\">").Append(Sample(session.Kept ? "k" : PageParts.ClassOf(session), 34, 14, mini: true)).Append("</span>")
            .Append(PageParts.Line(session, user?.Working ?? false, view.Now)).Append("</dd></dl>")
            // 59 b, 62 a and 65 a: what its own processes use, and its shared memory as the one
            // figure for all of BrowserAI, which the home's card says.
            .Append("<section class=\"use\" aria-label=\"What it uses now\"><p class=\"kind\">Uses now</p><dl>").Append(UsageContent.SessionLines(view, session)).Append("</dl></section>")
            .Append("<dl>")
            .Append("<dt>For</dt><dd>").Append(PageContent.Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded")).Append("</dd>")
            .Append("<dt>Used by</dt><dd>")
            .Append(user is { } conversation
                ? Pick("iu-" + key, conversation.Key, conversation.Name, inline: true)
                : PageContent.Text(session.Kept ? "No conversation: it was kept after its conversation ended." : "No conversation is connected to it now."))
            .Append("</dd>")
            .Append("<dt>Folder</dt><dd><code>").Append(PageContent.Text(session.Directory)).Append("</code></dd>")
            .Append("<dt>Size on disk</dt><dd>").Append(PageContent.Text(SizeWords(view, session.Id))).Append("</dd>");

        AppendTraces(html, session);

        _ = html.Append("</dl>");

        if (PageBoard.StateOf(session) is BrowserState.Window)
        {
            _ = html.Append("<div class=\"notice warn small-notice\">").Append(PageIcons.Of("window"))
                .Append("<p>This window is on your screen. While it is open, it holds back any update. Closing it ends the browser and keeps the session.</p></div>");
        }

        return html.Append("<p class=\"keep-note\">").Append(KeepNote).Append("</p>")
            .Append("<div class=\"btn-row\">")
            .Append(PageContent.Button("ib-open-session-" + session.Id, "open-session", "Open folder", "btn", "folder", ("session", session.Id)))
            .Append("</div></section>\n").ToString();
    }

    /// <summary>A session's traces as a row of a definition list, with what a trace is.</summary>
    private static void AppendTraces(StringBuilder html, SessionEntry session)
    {
        if (session.Traces.Count > 0)
        {
            _ = html.Append("<dt>Traces</dt><dd>").Append(PageContent.Text(string.Join(", ", session.Traces.Select(trace => trace.Name))))
                .Append("<span class=\"dd-note\">").Append(TracesNote).Append("</span></dd>");
        }
    }

    // ---- The list -------------------------------------------------------------------

    /// <summary>The same as a list: a card per conversation with its sessions, and the sessions no conversation uses.</summary>
    private static string List(PageView view, PageBoard board, string? chosen)
    {
        var html = new StringBuilder();
        var now = view.Now;

        foreach (var (window, members) in PageContent.ByWindow(board.Conversations, conversation => conversation.Relay.Window))
        {
            if (window is not null)
            {
                _ = html.Append("<section class=\"group\"><div class=\"group-head\">").Append(PageIcons.Of("code")).Append(PageContent.Text(window.Label())).Append("</div>\n");
            }

            foreach (var conversation in members)
            {
                _ = html.Append(ConversationCard(view, conversation, chosen));
            }

            if (window is not null)
            {
                _ = html.Append("</section>\n");
            }
        }

        if (board.Unlinked.Count > 0)
        {
            _ = html.Append("<section class=\"group\"><div class=\"group-head\">").Append(PageIcons.Of("folder")).Append("Kept after their conversation ended</div>\n")
                .Append("<article class=\"card convo\"><div class=\"convo-head\"><div><h3>")
                .Append(PageContent.Text(PageWords.Count(board.Unlinked.Count, "session", "sessions") + " with no conversation"))
                .Append("</h3><p class=\"row-sub\">BrowserAI keeps the browser as it was, so a conversation can pick the session up again. If none does, the browser closes by itself after its idle time.</p></div></div>\n");

            foreach (var session in board.Unlinked)
            {
                _ = html.Append(SessionRow(view, session, working: false, now, chosen));
            }

            _ = html.Append("</article></section>\n");
        }

        return html.ToString();
    }

    private static string ConversationCard(PageView view, Conversation conversation, string? chosen)
    {
        var description = conversation.Relay.Description;
        var status = conversation.Working
            ? "<span class=\"chip live\"><span class=\"pulse\" aria-hidden=\"true\"></span>Working now</span>"
            : conversation.Relay.RecentlyActive ? "<span class=\"chip\">Its browser is open</span>" : "<span class=\"chip\">Idle</span>";
        var html = new StringBuilder("<article class=\"card convo\" id=\"convo-").Append(PageContent.Text(conversation.Key)).Append("\"><div class=\"convo-head\">")
            .Append(PageContent.Badge(conversation.ClientName))
            .Append("<div><h3>").Append(PageContent.Text(conversation.Name)).Append("</h3><p class=\"row-sub\">")
            .Append(PageContent.Text(PageWords.AppWithVersion(conversation.ClientName, description.Client?.Version) + Where(conversation)))
            .Append("<span class=\"sep\" aria-hidden=\"true\"></span><code>").Append(PageContent.Text(description.WorkingDirectory)).Append("</code></p></div>")
            .Append("<div class=\"right\">").Append(status).Append("</div></div>\n");

        if (conversation.Sessions.Count is 0)
        {
            _ = html.Append("<p class=\"none-yet\">No browser yet. This conversation is connected and has not opened a session.</p>\n");
        }

        foreach (var session in conversation.Sessions)
        {
            _ = html.Append(SessionRow(view, session, conversation.Working, view.Now, chosen));
        }

        return html.Append("<p class=\"convo-foot\">").Append(PageIcons.Of("info")).Append(PageContent.Text(PageWords.Reconnect(conversation.Relay.Reconnect))).Append("</p></article>\n").ToString();
    }

    /// <summary>One session's row in the list.</summary>
    /// <remarks>
    /// <b>Its folder, size and traces fold away under the row</b> since 2026-10-12, with who
    /// deletes the folder: the row's <i>Details</i> button led to the session page for them,
    /// which is dropped.
    /// </remarks>
    private static string SessionRow(PageView view, SessionEntry session, bool working, DateTimeOffset now, string? chosen)
    {
        var fold = "d-sess-" + session.Id;
        var html = new StringBuilder("<div class=\"sess").Append(string.Equals("s-" + session.Id, chosen, StringComparison.Ordinal) ? " chosen" : string.Empty)
            .Append("\" id=\"sess-").Append(PageContent.Text(session.Id)).Append("\"><span class=\"sess-ic ").Append(PageParts.ClassOf(session)).Append("\">").Append(PageIcons.Of(PageParts.IconOf(session))).Append("</span>")
            .Append("<div><p class=\"sess-name\">").Append(PageContent.Text(PageWords.NameOf(session.Directory))).Append("</p>")
            .Append("<p class=\"sess-purpose\">").Append(PageContent.Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded")).Append("</p>")
            .Append("<p class=\"sess-line\">").Append(PageParts.Line(session, working, now)).Append("</p>")
            .Append("<details class=\"sess-more\" id=\"").Append(PageContent.Text(fold)).Append("\"><summary id=\"").Append(PageContent.Text(fold)).Append("-s\">")
            .Append(session.Traces.Count > 0 ? "Folder, size and traces" : "Folder and size").Append("</summary><dl>")
            .Append("<dt>Folder</dt><dd><code>").Append(PageContent.Text(session.Directory)).Append("</code></dd>")
            .Append("<dt>Size on disk</dt><dd>").Append(PageContent.Text(SizeWords(view, session.Id))).Append("</dd>");

        AppendTraces(html, session);

        return html.Append("</dl><p class=\"keep-note\">").Append(KeepNote).Append("</p></details></div>")
            .Append("<div class=\"sess-actions\">").Append(PageContent.Button("lb-open-session-" + session.Id, "open-session", "Open folder", "btn quiet", "folder", ("session", session.Id))).Append("</div></div>\n")
            .ToString();
    }
}

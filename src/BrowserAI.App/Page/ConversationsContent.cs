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
/// <b>The map becomes a list below <see cref="PageAssets.MapMinimumWidth"/> pixels of
/// window</b>, by the stylesheet, and <b>when it would be too crowded to read</b>: more than
/// <see cref="MostMapRows"/> rows, a row being a conversation or a session no conversation
/// uses, or more than <see cref="MostMapSessions"/> sessions. The write-up found the map
/// crowded past about eight conversations. Past either limit the map is not drawn at all.
/// </para>
/// <para>
/// <b>It keeps the sessions page's address</b>, <c>sessions?tab=N</c>, which
/// <c>BrowserAI.exe --sessions</c> opens, and it is live (46 a): the page's watch sends it a
/// new state on every change.
/// </para>
/// </remarks>
internal static class ConversationsContent
{
    /// <summary>The most rows the map draws: a row is a conversation, or a session no conversation uses.</summary>
    public const int MostMapRows = 8;

    /// <summary>The most sessions the map draws.</summary>
    public const int MostMapSessions = 12;

    /// <summary>The conversations page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var board = PageBoard.Of(view.Sessions);
        var html = new StringBuilder()
            .Append(PageContent.Crumbs(view.Tab, "Conversations and browsers"))
            .Append(PageContent.Title(
                "Conversations and browsers",
                "Every chat in Claude Code or Codex that uses BrowserAI, and the browser sessions it opened. Nothing here can be closed from this page: an agent closes its own browser, and a conversation ends when its app closes it."))
            .Append("<div class=\"explain\">")
            .Append(Explain("chat", "Conversation", "A chat in Claude Code or Codex. It is connected to BrowserAI while it is open."))
            .Append(Explain("folder", "Session", "A folder an agent made for its browser work: the browser profile with its logins, screenshots, downloads and a log."))
            .Append(Explain("globe", "Browser", "A session's browser while it runs: hidden, or a window on your screen. It closes by itself when nobody uses it."))
            .Append("</div>\n");

        PageContent.AppendNote(html, view.Note);

        if (board.Conversations.Count is 0 && board.All.Count is 0)
        {
            return html.Append(PageContent.Empty("chat", "No conversation is using BrowserAI", "Start a conversation in Claude Code or Codex and ask it to use a browser. It appears here as soon as it calls a BrowserAI tool."))
                .ToString();
        }

        var rows = board.Conversations.Count + board.Unlinked.Count;
        var crowded = rows > MostMapRows || board.All.Count > MostMapSessions;

        if (!crowded)
        {
            _ = html.Append(Legend()).Append("<div class=\"map-layout\">\n").Append(Map(view, board)).Append(Inspector(view, board)).Append("</div>\n");
        }

        _ = html.Append(crowded ? "<div class=\"list-view crowded\">\n" : "<div class=\"list-view\">\n");

        if (crowded)
        {
            _ = html.Append("<p class=\"small\">There are too many conversations and sessions to draw as a map, so they are listed.</p>\n");
        }

        return html.Append(List(view, board)).Append("</div>\n").ToString();
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

    private static string Explain(string icon, string title, string text) =>
        "<div>" + PageIcons.Of(icon) + "<div><b>" + PageContent.Text(title) + "</b><p>" + PageContent.Text(text) + "</p></div></div>";

    private static string Legend() =>
        "<div class=\"legend-row\" aria-hidden=\"true\">"
        + "<span><i class=\"w\"></i>Window on your screen</span>"
        + "<span><i class=\"h\"></i>Hidden browser</span>"
        + "<span><i class=\"c\"></i>No browser open</span>"
        + "<span><i class=\"k\"></i>Kept, no conversation</span></div>\n";

    // ---- The map ----------------------------------------------------------------

    /// <summary>The map: a column of conversations, BrowserAI, and a column of sessions, row by row.</summary>
    private static string Map(PageView view, PageBoard board)
    {
        var now = view.Now;
        var html = new StringBuilder("<div class=\"map\" id=\"map\">\n<svg class=\"wires\" id=\"wires\" aria-hidden=\"true\" focusable=\"false\"></svg>\n")
            .Append("<div class=\"col-head c1\">Conversations</div><div class=\"col-head c3\">BrowserAI</div><div class=\"col-head c5\">Browser sessions</div>\n");
        var rows = board.Conversations.Count + board.Unlinked.Count;
        var row = 0;

        _ = html.Append("<div class=\"hub-cell rows-").Append(Math.Max(1, rows).ToString(CultureInfo.InvariantCulture)).Append("\">").Append(Hub(view, board)).Append("</div>\n");

        string? lastWindow = null;

        foreach (var conversation in board.Conversations)
        {
            row++;

            var label = conversation.Relay.Window is { } window && !string.Equals(window.Key, lastWindow, StringComparison.Ordinal)
                ? "<div class=\"group-label\">" + PageIcons.Of("code") + PageContent.Text(window.Label()) + "</div>"
                : string.Empty;

            lastWindow = conversation.Relay.Window?.Key;

            var first = conversation.Sessions.Count > 0 ? PageParts.ClassOf(conversation.Sessions[0]) : "n";

            _ = html.Append("<div class=\"left r").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" data-row=\"").Append(PageContent.Text(conversation.Key))
                .Append("\" data-line=\"").Append(first).Append('"').Append(conversation.Working ? " data-live=\"1\"" : string.Empty).Append('>')
                .Append(label).Append(ConversationNode(conversation)).Append("</div>\n")
                .Append("<div class=\"right r").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" data-row=\"").Append(PageContent.Text(conversation.Key)).Append("\">");

            if (conversation.Sessions.Count is 0)
            {
                _ = html.Append("<p class=\"none\">No browser yet</p>");
            }
            else
            {
                foreach (var session in conversation.Sessions)
                {
                    _ = html.Append(SessionNode(session, conversation.Working, now));
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
                .Append(SessionNode(session, working: false, now)).Append("</div>\n");
        }

        return html.Append("</div>\n").ToString();
    }

    private static string ConversationNode(Conversation conversation) =>
        "<button type=\"button\" class=\"node\" id=\"n-" + PageContent.Text(conversation.Key) + "\" data-inspect=\"" + PageContent.Text(conversation.Key)
        + "\" aria-pressed=\"false\" aria-controls=\"inspect-" + PageContent.Text(conversation.Key) + "\">"
        + PageContent.Badge(conversation.ClientName)
        + "<span class=\"txt\"><b>" + PageContent.Text(conversation.Name) + "</b><small>" + PageContent.Text(conversation.Place + ", " + PageWords.NameOf(conversation.Relay.Description.WorkingDirectory)) + "</small></span>"
        + (conversation.Working ? "<span class=\"tag live\">Working</span>" : string.Empty) + "</button>";

    private static string SessionNode(SessionEntry session, bool working, DateTimeOffset now)
    {
        var key = "s-" + session.Id;
        var style = PageParts.ClassOf(session);

        return "<button type=\"button\" class=\"node " + style + "\" id=\"n-" + PageContent.Text(key) + "\" data-inspect=\"" + PageContent.Text(key)
            + "\" data-line=\"" + (session.Kept ? "k" : style) + "\" aria-pressed=\"false\" aria-controls=\"inspect-" + PageContent.Text(key) + "\">"
            + "<span class=\"br-ic " + style + "\">" + PageIcons.Of(PageParts.IconOf(session)) + "</span>"
            + "<span class=\"txt\"><b>" + PageContent.Text(PageWords.NameOf(session.Directory)) + "</b>"
            + (session.Kept ? "<small>Kept, no conversation</small>" : string.Empty)
            + "<span class=\"state\">" + PageParts.Short(session, working, now) + "</span></span></button>";
    }

    private static string Hub(PageView view, PageBoard board)
    {
        var facts = view.Facts;
        var html = new StringBuilder("<button type=\"button\" class=\"hub node\" id=\"n-hub\" data-inspect=\"hub\" aria-pressed=\"false\" aria-controls=\"inspect-hub\">")
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

        return html.Append("</button>").ToString();
    }

    private static string HubStat(string label, int count) =>
        "<span class=\"hub-row\"><span>" + PageContent.Text(label) + "</span><b>" + count.ToString(CultureInfo.InvariantCulture) + "</b></span>";

    // ---- The inspector ------------------------------------------------------------

    /// <summary>What is chosen on the map: a panel per card, all drawn, one shown by the script.</summary>
    private static string Inspector(PageView view, PageBoard board)
    {
        var html = new StringBuilder("<aside class=\"inspector\" aria-label=\"What is chosen on the map\" aria-live=\"polite\">\n")
            .Append("<p class=\"inspect-none\" id=\"inspect-none\">Choose a conversation, BrowserAI or a session on the map to see what it is.</p>\n")
            .Append(HubPanel(view));

        foreach (var conversation in board.Conversations)
        {
            _ = html.Append(ConversationPanel(view, conversation));
        }

        foreach (var session in board.All)
        {
            _ = html.Append(SessionPanel(view, board, session));
        }

        return html.Append("</aside>\n").ToString();
    }

    private static string Panel(string key, string icon, string kind, string title, string what) =>
        "<section class=\"inspect\" id=\"inspect-" + PageContent.Text(key) + "\" hidden>"
        + "<p class=\"kind\">" + PageIcons.Of(icon) + PageContent.Text(kind) + "</p><h2>" + PageContent.Text(title) + "</h2>"
        + "<div class=\"what\">" + PageIcons.Of("question") + "<p>" + PageContent.Text(what) + "</p></div>";

    private static string HubPanel(PageView view)
    {
        var facts = view.Facts;
        var html = new StringBuilder(Panel(
                "hub",
                "info",
                "BrowserAI",
                $"BrowserAI {facts.Version}",
                "BrowserAI runs in the background on this PC from sign-in to sign-out. Claude Code and Codex reach it for each conversation, and it gives the agent a real browser."))
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

    private static string ConversationPanel(PageView view, Conversation conversation)
    {
        var app = PageWords.AppOf(conversation.ClientName);
        var description = conversation.Relay.Description;
        var html = new StringBuilder(Panel(
                conversation.Key,
                "chat",
                "Conversation",
                conversation.Name,
                $"A chat in {app}. It is connected to BrowserAI while it is open, and it ends when {app} closes it, never from here."))
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
            _ = html.Append("<p class=\"small\">Its browser sessions:</p><ul class=\"plain\">");

            foreach (var session in conversation.Sessions)
            {
                _ = html.Append("<li><a id=\"il-").Append(PageContent.Text(conversation.Key)).Append('-').Append(PageContent.Text(session.Id)).Append("\" href=\"")
                    .Append(PageContent.SessionRoute(session.Id, view.Tab)).Append("\">").Append(PageContent.Text(PageWords.NameOf(session.Directory))).Append("</a></li>");
            }

            _ = html.Append("</ul>");
        }

        return html.Append("</section>\n").ToString();
    }

    private static string Where(Conversation conversation) =>
        conversation.Relay.Window is not null ? ", in VS Code"
        : conversation.Relay.Reconnect is RelayReconnect.McpReconnect && !PageWords.IsCodex(conversation.ClientName) ? ", in a terminal"
        : string.Empty;

    private static string SessionPanel(PageView view, PageBoard board, SessionEntry session)
    {
        var key = "s-" + session.Id;
        var user = board.UsedBy(session);
        var html = new StringBuilder(Panel(
                key,
                "globe",
                "Browser session",
                PageWords.NameOf(session.Directory),
                "A folder an agent made for its browser work: the browser profile with its logins and cookies, screenshots, downloads and a log. Only the agent that made it deletes it."))
            .Append("<dl>")
            .Append("<dt>For</dt><dd>").Append(PageContent.Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded")).Append("</dd>")
            .Append("<dt>Browser</dt><dd>").Append(PageParts.Line(session, user?.Working ?? false, view.Now)).Append("</dd>")
            .Append("<dt>Used by</dt><dd>").Append(PageContent.Text(user?.Name ?? (session.Kept ? "No conversation: it was kept after its conversation ended." : "No conversation is connected to it now."))).Append("</dd>")
            .Append("<dt>Folder</dt><dd><code>").Append(PageContent.Text(session.Directory)).Append("</code></dd>")
            .Append("<dt>Size</dt><dd>").Append(PageContent.Text(SessionPageContent.SizeWords(view, session.Id))).Append("</dd>");

        if (session.Traces.Count > 0)
        {
            _ = html.Append("<dt>Traces</dt><dd>").Append(PageContent.Text(string.Join(", ", session.Traces.Select(trace => trace.Name)))).Append("</dd>");
        }

        _ = html.Append("</dl>");

        if (PageBoard.StateOf(session) is BrowserState.Window)
        {
            _ = html.Append("<div class=\"notice warn small-notice\">").Append(PageIcons.Of("window"))
                .Append("<p>This window is on your screen. While it is open, it holds updates. Closing it ends the browser and keeps the session.</p></div>");
        }

        return html.Append("<div class=\"btn-row\">")
            .Append(PageContent.Button("ib-open-session-" + session.Id, "open-session", "Open folder", "btn", "folder", ("session", session.Id)))
            .Append(PageContent.LinkButton("il-session-" + session.Id, PageContent.SessionRoute(session.Id, view.Tab), "Details"))
            .Append("</div></section>\n").ToString();
    }

    // ---- The list -------------------------------------------------------------------

    /// <summary>The same as a list: a card per conversation with its sessions, and the sessions no conversation uses.</summary>
    private static string List(PageView view, PageBoard board)
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
                _ = html.Append(ConversationCard(view, conversation));
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
                .Append("</h3><p class=\"row-sub\">BrowserAI keeps the browser as it was, so a conversation can pick the session up again. Its idle close ends it otherwise.</p></div></div>\n");

            foreach (var session in board.Unlinked)
            {
                _ = html.Append(SessionRow(view, session, working: false, now));
            }

            _ = html.Append("</article></section>\n");
        }

        return html.ToString();
    }

    private static string ConversationCard(PageView view, Conversation conversation)
    {
        var description = conversation.Relay.Description;
        var status = conversation.Working
            ? "<span class=\"chip live\"><span class=\"pulse\" aria-hidden=\"true\"></span>Working now</span>"
            : conversation.Relay.RecentlyActive ? "<span class=\"chip\">Last call a few minutes ago</span>" : "<span class=\"chip\">Idle</span>";
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
            _ = html.Append(SessionRow(view, session, conversation.Working, view.Now));
        }

        return html.Append("<p class=\"convo-foot\">").Append(PageIcons.Of("info")).Append(PageContent.Text(PageWords.Reconnect(conversation.Relay.Reconnect))).Append("</p></article>\n").ToString();
    }

    private static string SessionRow(PageView view, SessionEntry session, bool working, DateTimeOffset now) =>
        "<div class=\"sess\"><span class=\"sess-ic " + PageParts.ClassOf(session) + "\">" + PageIcons.Of(PageParts.IconOf(session)) + "</span>"
        + "<div><p class=\"sess-name\">" + PageContent.Text(PageWords.NameOf(session.Directory)) + "</p>"
        + "<p class=\"sess-purpose\">" + PageContent.Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded") + "</p>"
        + "<p class=\"sess-line\">" + PageParts.Line(session, working, now) + "</p></div>"
        + "<div class=\"sess-actions\">" + PageContent.Button("lb-open-session-" + session.Id, "open-session", "Open folder", "btn quiet", "folder", ("session", session.Id))
        + PageContent.LinkButton("ll-session-" + session.Id, PageContent.SessionRoute(session.Id, view.Tab), "Details") + "</div></div>\n";
}

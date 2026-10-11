// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Proxy;
using BrowserAI.Registration;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// The home a Start Menu start opens: what needs the person, what runs now, the update,
/// Claude Code and Codex, the session folders and this install, each a card that leads to
/// a page of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>40 a, F, the maintainer's choice of 2026-10-11</b>: direction A's overview, which
/// answers in its first screen what the write-up ranked first. What needs the person, said
/// only when something does; what runs now and what is on the screen; whether it works and
/// which version; when the update installs; how much disk the sessions take; and where
/// things are.
/// </para>
/// <para>
/// <b>It keeps the status page's address</b>, <c>/&lt;token&gt;/?tab=N</c>, which the Start
/// Menu, the installer and the broken install's toast open.
/// </para>
/// </remarks>
internal static class OverviewContent
{
    /// <summary>How many conversations the home lists before it leaves the rest to the conversations page.</summary>
    public const int RowsShown = 4;

    /// <summary>The home's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <param name="occasion">Why this tab was opened.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view, Occasion occasion)
    {
        ArgumentNullException.ThrowIfNull(view);

        var board = PageBoard.Of(view.Sessions);
        var needs = NeedsYou(view);
        var html = new StringBuilder();
        var (headline, lede) = Headline(view, board, needs.Count, occasion);

        _ = html.Append("<section class=\"hello\"><h1>").Append(PageContent.Text(headline)).Append("</h1><p class=\"lede\">").Append(PageContent.Text(lede)).Append("</p></section>\n");

        PageContent.AppendNote(html, view.Note);

        foreach (var notice in needs)
        {
            _ = html.Append(notice);
        }

        if (occasion is Occasion.AfterUpdate)
        {
            _ = html.Append(PageContent.Notice(
                "good",
                $"Updated to BrowserAI {view.Facts.Version}",
                "BrowserAI updated itself and started again.",
                PageContent.LinkButton("l-notice-changelog", PageContent.Route(PageKind.Changelog, view.Tab), "What is new")));
        }

        if (occasion is Occasion.FirstRun)
        {
            _ = html.Append(PageContent.Notice(
                "good",
                "Claude Code and Codex",
                PageContent.FirstRunSentence,
                PageContent.LinkButton("l-notice-setup", PageContent.Route(PageKind.Setup, view.Tab), "See the setup")));
        }

        _ = html.Append("<div class=\"grid\">\n")
            .Append(NowCard(view, board))
            .Append(UpdateCard(view))
            .Append(AppsCard(view))
            .Append(StorageCard(view, board))
            .Append(InstallCard(view))
            .Append("</div>\n");

        return html.ToString();
    }

    /// <summary>Every notice of something the person has to act on, in the order the write-up ranked them.</summary>
    /// <remarks>
    /// <b>The broken install is not among them</b>: every page says it first, in full, above
    /// everything else (10 b). It counts towards the headline all the same.
    /// </remarks>
    /// <param name="view">What is true now.</param>
    /// <returns>Each notice's HTML.</returns>
    internal static List<string> NeedsYou(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var notices = new List<string>();

        if (view.InstallBroken is not null)
        {
            notices.Add(string.Empty);
        }

        if (view.Registration?.State is { } state)
        {
            foreach (var client in state.Clients.Where(PageParts.NeedsYou))
            {
                var app = client.Client.DisplayName;
                var (title, text) = client.Unanswered is not null || client.UserScope.Unreadable is not null
                    ? ($"BrowserAI cannot tell how {app} is set up", $"Until it can, BrowserAI cannot say whether {app} reaches it.")
                    : client.UserScope.Ownership is RegistrationOwnership.Foreign
                        ? ($"Another BrowserAI is set up in {app}", $"{app} starts a BrowserAI from another install, so this one is not used there.")
                        : ($"{app} needs repair", $"{app} is set up to start a file that is not this install's BrowserAI, so it cannot reach BrowserAI.");

                notices.Add(PageContent.Notice(
                    "warn",
                    title,
                    text,
                    PageContent.LinkButton("l-notice-" + client.Client.Key, PageContent.Route(PageKind.Setup, view.Tab), client.UserScope.Ownership is RegistrationOwnership.OursAndStale ? "Repair" : "See the setup"),
                    alert: true));
            }
        }

        if (view.Holds is { State: UpdateHoldState.Held, Version: { } version } holds)
        {
            var wait = holds.WaitAt(view.Now).Wait;

            if (wait is UpdateWait.WindowNeverCloses)
            {
                notices.Add(PageContent.Notice(
                    "warn",
                    "A window keeps the update waiting",
                    $"Update {version} installs once that window is closed, because it is set never to close by itself.",
                    PageContent.LinkButton("l-notice-update", PageContent.Route(PageKind.Update, view.Tab), "See the update"),
                    alert: true));
            }
            else if (wait is UpdateWait.SessionNeverCloses)
            {
                notices.Add(PageContent.Notice(
                    "warn",
                    "A session keeps the update waiting",
                    $"Update {version} installs once an agent closes that session, because it is set never to close by itself.",
                    PageContent.LinkButton("l-notice-update", PageContent.Route(PageKind.Update, view.Tab), "See the update"),
                    alert: true));
            }
        }

        return notices;
    }

    /// <summary>The headline and the sentence under it: the main question, answered.</summary>
    private static (string Headline, string Lede) Headline(PageView view, PageBoard board, int needs, Occasion occasion)
    {
        if (needs > 0)
        {
            return (needs is 1 ? "1 thing needs you" : string.Create(CultureInfo.InvariantCulture, $"{needs} things need you"),
                "BrowserAI is running.");
        }

        if (occasion is Occasion.FirstRun)
        {
            return ("BrowserAI is installed", "It runs in the background from now on and gives your agents a real browser.");
        }

        var conversations = board.Conversations.Count;

        if (conversations is 0)
        {
            return board.All.Count is 0
                ? ("All quiet", "BrowserAI is running. No conversation is using it right now.")
                : ("No conversation is using BrowserAI", board.Kept is 1
                    ? "BrowserAI keeps 1 session for the next conversation that names it."
                    : string.Create(CultureInfo.InvariantCulture, $"BrowserAI keeps {board.Kept} sessions for the next conversation that names one."));
        }

        var said = new List<string>();

        if (board.OnScreen is 1)
        {
            said.Add("A browser window is open on your screen.");
        }
        else if (board.OnScreen > 1)
        {
            said.Add(string.Create(CultureInfo.InvariantCulture, $"{board.OnScreen} browser windows are open on your screen."));
        }

        if (view.Holds is { State: UpdateHoldState.Held, Version: { } version })
        {
            said.Add($"Update {version} installs by itself once nothing uses BrowserAI.");
        }

        return (conversations is 1 ? "1 conversation is using BrowserAI" : string.Create(CultureInfo.InvariantCulture, $"{conversations} conversations are using BrowserAI"),
            said.Count > 0 ? string.Join(' ', said) : "Each is listed below, with the browsers it opened.");
    }

    /// <summary>The card that answers what runs now.</summary>
    private static string NowCard(PageView view, PageBoard board)
    {
        var html = new StringBuilder("<article class=\"card span-8\" id=\"card-now\">\n")
            .Append(PageContent.CardHead("pulse", "Right now", ("l-see-all", "See all", PageContent.Route(PageKind.Sessions, view.Tab))))
            .Append("<p class=\"caption\">Conversations in Claude Code and Codex that use BrowserAI, and the browsers they opened.</p>\n");

        if (board.Conversations.Count is 0 && board.All.Count is 0)
        {
            return html.Append(PageContent.Empty("chat", "No conversation is using BrowserAI", "When an agent in Claude Code or Codex calls a BrowserAI tool, its conversation appears here, with the browser it opened."))
                .Append("</article>\n").ToString();
        }

        _ = html.Append("<div class=\"stats\">")
            .Append(Stat("chat", board.Conversations.Count, "Conversation", "Conversations", warn: false))
            .Append(Stat("window", board.OnScreen, "On your screen", "On your screen", warn: board.OnScreen > 0))
            .Append(Stat("hidden", board.HiddenBrowsers, "Hidden browser", "Hidden browsers", warn: false))
            .Append(Stat("folder", board.Kept, "Kept for later", "Kept for later", warn: false))
            .Append("</div>\n<div class=\"rows\">\n");

        foreach (var conversation in board.Conversations.Take(RowsShown))
        {
            var chips = conversation.Sessions.Count is 0
                ? "<span class=\"chip\">No browser yet</span>"
                : string.Concat(conversation.Sessions.Select(session => PageParts.Chip(session, view.Now)));

            _ = html.Append("<a class=\"row\" id=\"l-row-").Append(PageContent.Text(conversation.Key)).Append("\" href=\"").Append(PageContent.Route(PageKind.Sessions, view.Tab)).Append("\">")
                .Append(PageContent.Badge(conversation.ClientName))
                .Append("<span class=\"row-main\"><span class=\"row-title\">").Append(PageContent.Text(conversation.Name)).Append("</span>")
                .Append("<span class=\"row-sub\">").Append(PageContent.Text(conversation.Place)).Append("<span class=\"sep\" aria-hidden=\"true\"></span>")
                .Append(PageContent.Text(PageWords.NameOf(conversation.Relay.Description.WorkingDirectory)))
                .Append(conversation.Working ? "<span class=\"sep\" aria-hidden=\"true\"></span><span class=\"working\"><span class=\"pulse\" aria-hidden=\"true\"></span>Working now</span>" : string.Empty)
                .Append("</span></span><span class=\"row-side\">").Append(chips).Append("</span>").Append(PageIcons.Of("chevron")).Append("</a>\n");
        }

        foreach (var session in board.Unlinked)
        {
            _ = html.Append("<a class=\"row\" id=\"l-row-s-").Append(PageContent.Text(session.Id)).Append("\" href=\"").Append(PageContent.SessionRoute(session.Id, view.Tab)).Append("\">")
                .Append("<span class=\"app kept\" aria-hidden=\"true\">").Append(PageIcons.Of("folder")).Append("</span>")
                .Append("<span class=\"row-main\"><span class=\"row-title\">").Append(PageContent.Text(PageWords.NameOf(session.Directory))).Append("</span>")
                .Append("<span class=\"row-sub\">").Append(session.Kept ? "Kept after its conversation ended" : "No conversation is connected to it now").Append("</span></span>")
                .Append("<span class=\"row-side\">").Append(PageParts.Chip(session, view.Now)).Append("</span>").Append(PageIcons.Of("chevron")).Append("</a>\n");
        }

        _ = html.Append("</div>\n");

        if (board.Conversations.Count > RowsShown)
        {
            var more = board.Conversations.Count - RowsShown;

            _ = html.Append("<p class=\"small more-line\"><a id=\"l-more\" href=\"").Append(PageContent.Route(PageKind.Sessions, view.Tab)).Append("\">")
                .Append(more is 1 ? "1 more conversation" : string.Create(CultureInfo.InvariantCulture, $"{more} more conversations")).Append("</a></p>\n");
        }

        return html.Append("</article>\n").ToString();
    }

    private static string Stat(string icon, int count, string one, string many, bool warn) =>
        "<div class=\"stat" + (warn ? " warn" : string.Empty) + "\">" + PageIcons.Of(icon) + "<div><b>"
        + count.ToString(CultureInfo.InvariantCulture) + "</b><span>" + PageContent.Text(count is 1 ? one : many) + "</span></div></div>";

    /// <summary>The card that says when the update installs, or that none waits and when BrowserAI last looked.</summary>
    private static string UpdateCard(PageView view)
    {
        var html = new StringBuilder("<article class=\"card span-4\" id=\"card-update\">\n").Append(PageContent.CardHead("update", "Update"));
        var facts = view.Facts;
        var now = view.Now;

        switch (view.Holds)
        {
            case null when facts.InstallRoot is not null:
                _ = html.Append("<p class=\"big\">Unknown</p>");
                UpdatePageContent.AppendUnread(html, facts);
                break;

            case { State: UpdateHoldState.Installing, Version: { } installing }:
                _ = html.Append("<p class=\"big\">").Append(PageContent.Text($"{installing} is installing")).Append("</p>\n<p>BrowserAI closes for a moment and starts again by itself.</p>\n");
                break;

            case { State: UpdateHoldState.Held, Version: { } version } holds:
                var wait = holds.WaitAt(now);

                _ = html.Append("<p class=\"big\">").Append(PageContent.Text(wait.Wait is UpdateWait.WindowNeverCloses or UpdateWait.SessionNeverCloses ? $"{version} is waiting" : $"{version} is ready")).Append("</p>\n");

                if (holds.Older)
                {
                    _ = html.Append("<p class=\"warning\">").Append(PageContent.Text(UpdatePageContent.OlderSentence(facts.Version))).Append("</p>\n");
                }

                _ = html.Append("<p>").Append(UpdatePageContent.WaitSentence(wait, now)).Append("</p>\n");
                AppendHolders(html, holds, now, facts.RelayHold);
                _ = html.Append("<div class=\"btn-row\">").Append(PageContent.LinkButton("l-update", PageContent.Route(PageKind.Update, view.Tab), "See what holds it", "btn primary")).Append("</div>\n");
                break;

            default:
                if (facts.InstallRoot is null)
                {
                    _ = html.Append("<p class=\"big\">Not installed</p>\n<p>").Append(PageContent.Text(PageContent.NotInstalledSentence)).Append("</p>\n");
                    break;
                }

                _ = html.Append("<p class=\"big\">No update is waiting</p>\n<p>").Append(PageContent.Text(UpdatePageContent.CheckedSentence(view.Updates, view.Now))).Append("</p>\n");

                // Only where BrowserAI checks, since the reader pass of 2026-10-11 (previously
                // on every installed home with nothing waiting, under a sentence saying it
                // does not check, for a refused source or a development build's web address).
                if (view.Updates.Checks is UpdateChecks.Checking)
                {
                    _ = html.Append("<p class=\"small\">When a newer version is out, BrowserAI downloads it and installs it once nothing uses BrowserAI.</p>\n");
                }

                break;
        }

        return html.Append("</article>\n").ToString();
    }

    /// <summary>What holds a waiting update, one short line each.</summary>
    /// <remarks>
    /// <b>The conversations' line says how long each holds it from <paramref name="relayHold"/></b>,
    /// since fact 6 of the reader pass of 2026-10-11 (previously <i>10 min after each call</i>,
    /// written in the string).
    /// </remarks>
    private static void AppendHolders(StringBuilder html, UpdateHoldSnapshot holds, DateTimeOffset now, TimeSpan relayHold)
    {
        var lines = new StringBuilder();

        foreach (var window in holds.VisibleWindows)
        {
            _ = lines.Append(Mini("window", "window", PageWords.NameOf(window.Directory), window.ClosesAt is { } at && at > now ? PageContent.Countdown(at, now) : "<span class=\"small\">never closes</span>"));
        }

        foreach (var session in holds.HiddenSessions)
        {
            _ = lines.Append(Mini("hidden", "hidden", PageWords.NameOf(session.Directory), session.ClosesAt is { } at && at > now ? PageContent.Countdown(at, now) : "<span class=\"small\">never closes</span>"));
        }

        var active = holds.HoldingRelaysAt(now).Count();

        if (active > 0)
        {
            _ = lines.Append(Mini("chat", string.Empty, active is 1 ? "1 active conversation" : string.Create(CultureInfo.InvariantCulture, $"{active} active conversations"), "<span class=\"small\">" + PageContent.Text(PageWords.Minutes(relayHold) + " after the last message to BrowserAI") + "</span>"));
        }


        if (lines.Length > 0)
        {
            _ = html.Append("<div class=\"minis\">").Append(lines).Append("</div>\n");
        }
    }

    private static string Mini(string icon, string style, string label, string side) =>
        "<div class=\"mini\"><span class=\"mini-ic " + style + "\">" + PageIcons.Of(icon) + "</span><span>" + PageContent.Text(label) + "</span>" + side + "</div>";

    /// <summary>The card that says how Claude Code and Codex are set up.</summary>
    private static string AppsCard(PageView view)
    {
        var html = new StringBuilder("<article class=\"card span-4\" id=\"card-apps\">\n")
            .Append(PageContent.CardHead("plug", "Claude Code and Codex", ("l-manage", "Manage", PageContent.Route(PageKind.Setup, view.Tab))))
            .Append("<p class=\"caption\">Your agents reach BrowserAI through these apps.</p>\n");

        switch (view.Registration)
        {
            case null:
                _ = html.Append("<p class=\"small\">Reading how Claude Code and Codex are set up.</p>\n");
                break;

            case { State: null }:
                _ = html.Append("<p>BrowserAI could not read how Claude Code and Codex are set up.</p>\n");
                break;

            case { State: { } state }:
                _ = html.Append("<div class=\"app-rows\">");

                foreach (var client in state.Clients)
                {
                    _ = html.Append("<div class=\"app-row\">").Append(PageContent.Badge(client.Client.Key is "codex" ? KnownClients.Codex : KnownClients.ClaudeCode))
                        .Append("<div><b>").Append(PageContent.Text(client.Client.DisplayName)).Append("</b>").Append(PageParts.SetupLine(client)).Append("</div></div>");
                }

                _ = html.Append("</div>\n");
                break;
        }

        return html.Append("</article>\n").ToString();
    }

    /// <summary>The card that says how much the session folders take.</summary>
    private static string StorageCard(PageView view, PageBoard board)
    {
        var html = new StringBuilder("<article class=\"card span-4\" id=\"card-storage\">\n")
            .Append(PageContent.CardHead("disk", "Storage", ("l-storage", "Details", PageContent.Route(PageKind.Storage, view.Tab))));

        if (view.Folders is not { } measured)
        {
            return html.Append("<p class=\"small\">Measuring the session folders.</p>\n</article>\n").ToString();
        }

        if (measured.Folders.Count is 0)
        {
            return html.Append(PageContent.Empty("folder", "No session folder yet", "Each time an agent starts a browser session, it names a folder for it."))
                .Append("</article>\n").ToString();
        }

        var open = board.All.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var openFolders = measured.Folders.Where(folder => open.Contains(folder.Id)).ToList();
        var closedFolders = measured.Folders.Where(folder => !open.Contains(folder.Id)).ToList();
        var total = measured.Folders.Sum(folder => folder.Bytes ?? 0);
        var openBytes = openFolders.Sum(folder => folder.Bytes ?? 0);
        var closedBytes = closedFolders.Sum(folder => folder.Bytes ?? 0);
        var share = total > 0 ? (double)openBytes / total * 100 : 0;

        _ = html.Append("<p class=\"big\">").Append(PageContent.Text(PageWords.Size(total))).Append("</p>\n")
            .Append("<p class=\"caption\">").Append(PageContent.Text("in " + PageWords.Count(measured.Folders.Count, "session folder", "session folders") + " on this PC")).Append("</p>\n")
            .Append(string.Create(CultureInfo.InvariantCulture, $"<svg class=\"bar\" viewBox=\"0 0 100 10\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"The folders open now and the folders not open now, as parts of the whole\"><rect class=\"seg-a\" x=\"0\" y=\"0\" width=\"{share:0.##}\" height=\"10\"/><rect class=\"seg-b\" x=\"{share:0.##}\" y=\"0\" width=\"{100 - share:0.##}\" height=\"10\"/></svg>\n"))
            .Append("<div class=\"legend\">")
            .Append("<div><span class=\"swatch a\"></span><span>").Append(PageContent.Text(PageWords.Count(openFolders.Count, "folder open now", "folders open now"))).Append("</span><b>").Append(PageContent.Text(PageWords.Size(openBytes))).Append("</b></div>")
            .Append("<div><span class=\"swatch b\"></span><span>").Append(PageContent.Text(PageWords.Count(closedFolders.Count, "folder not open now", "folders not open now"))).Append("</span><b>").Append(PageContent.Text(PageWords.Size(closedBytes))).Append("</b></div>")
            .Append("</div>\n");

        return html.Append("</article>\n").ToString();
    }

    /// <summary>The card that says which BrowserAI this is, since when it runs, and where its updates come from.</summary>
    private static string InstallCard(PageView view)
    {
        var facts = view.Facts;
        var html = new StringBuilder("<article class=\"card span-4\" id=\"card-install\">\n")
            .Append(PageContent.CardHead("info", "This install", ("l-install", "Paths", PageContent.Route(PageKind.Install, view.Tab))))
            .Append("<dl class=\"facts\">")
            .Append("<dt>Version</dt><dd>").Append(PageContent.Text(facts.Version)).Append(", <a id=\"l-whats-new\" href=\"").Append(PageContent.Route(PageKind.Changelog, view.Tab)).Append("\">what is new</a></dd>");

        if (InstallContent.RunningSince(facts, view.Now) is { } running)
        {
            _ = html.Append("<dt>Running</dt><dd>").Append(PageContent.Text(running)).Append("</dd>");
        }

        _ = html.Append("<dt>Updates</dt><dd>").Append(PageContent.Text(UpdatePageContent.SourceWords(view.Updates, facts))).Append("</dd></dl>\n")
            .Append("<div class=\"btn-row\">");

        if (facts.InstallRoot is { Length: > 0 })
        {
            _ = html.Append(PageContent.Button("b-open-folder-install", "open-folder", "Install folder", "btn", "folder", ("folder", "install")));
        }

        _ = html.Append(PageContent.Button("b-open-folder-data", "open-folder", "Data", "btn", "folder", ("folder", "data")))
            .Append(PageContent.Button("b-open-folder-logs", "open-folder", "Logs", "btn", "file", ("folder", "logs")))
            .Append("</div>\n");

        return html.Append("</article>\n").ToString();
    }
}

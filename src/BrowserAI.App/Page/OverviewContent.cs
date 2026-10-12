// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Proxy;
using BrowserAI.Registration;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// The home a Start Menu start opens: what needs the person, what runs now and what it
/// uses, the update, Claude Code and Codex, the session folders and this install, each a
/// card that leads to a page of its own.
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
/// <para>
/// <b>Layout 1, the side column, the maintainer's 61 of 2026-10-11, in his words verbatim
/// <i>"61 I like 1"</i></b>, made for his screen, Firefox maximised to the bottom half of a
/// rotated side screen, a viewport of about 1440 by 1170. The headline shares a band with the
/// after-update notice; the main column holds what runs now, as counts and one button, above
/// the live CPU and memory; the side column holds the update, the two apps, the storage and
/// this install, each at its own height. <b>No conversation is listed here</b>, his words
/// verbatim: <i>"Can we remove the arbretary list of browsers and isntead show stats of all
/// teh windows and everything we have on the data with a clear button to open the list going
/// to the more converstations page?"</i>
/// </para>
/// </remarks>
internal static class OverviewContent
{
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

        // The headline and the after-update notice share one band (61, layout 1).
        _ = html.Append(view.UpdatedTo is null ? "<div class=\"band\">" : "<div class=\"band two\">")
            .Append("<section class=\"hello\"><h1>").Append(PageContent.Text(headline)).Append("</h1><p class=\"lede\">").Append(PageContent.Text(lede)).Append("</p></section>\n");

        if (view.UpdatedTo is { } version)
        {
            _ = html.Append(UpdatedNotice(view, version));
        }

        _ = html.Append("</div>\n");

        PageContent.AppendNote(html, view.Note);

        foreach (var notice in needs)
        {
            _ = html.Append(notice);
        }

        if (occasion is Occasion.FirstRun)
        {
            _ = html.Append(PageContent.Notice(
                "good",
                "Claude Code and Codex",
                PageContent.FirstRunSentence,
                PageContent.LinkButton("l-notice-setup", PageContent.Route(PageKind.Setup, view.Tab), "See the setup")));
        }

        _ = html.Append("<div class=\"split\">\n<div class=\"col col-main\">\n")
            .Append(NowCard(view, board))
            .Append(UsageContent.Card(view, PageService.UsageWindow))
            .Append("</div>\n<div class=\"col col-side\">\n")
            .Append(UpdateCard(view))
            .Append(AppsCard(view))
            .Append(StorageCard(view, board))
            .Append(InstallCard(view))
            .Append("</div>\n</div>\n");

        return html.ToString();
    }

    /// <summary>
    /// The after-update notice, once: <i>What is new</i>, and a dismiss that folds it away
    /// (57 a, the maintainer's words verbatim: <i>"57 a - also allow dismissing the banner in
    /// a nice way."</i>).
    /// </summary>
    /// <remarks>
    /// <b>It shows until a <i>What is new</i> link or the dismiss is pressed</b>, in every tab
    /// and after a restart, and comes back only when a newer version is installed: the
    /// background keeps that in the data root (<see cref="UpdateNoticeFile"/>). The fold is the
    /// page's script's, which waits for it to finish before the next state replaces the page.
    /// </remarks>
    private static string UpdatedNotice(PageView view, string version) =>
        "<div class=\"fold\" id=\"fold-updated\"><div class=\"fold-in\">"
        + PageContent.Notice(
            "good",
            $"Updated to BrowserAI {version}",
            "BrowserAI updated itself and started again.",
            PageContent.LinkButton("l-notice-changelog", PageContent.Route(PageKind.Changelog, view.Tab), "What is new")
            + "<button type=\"button\" id=\"b-dismiss-updated\" class=\"dismiss\" data-action=\"dismiss-notice\" aria-label=\"Dismiss this notice\" title=\"Dismiss\">"
            + PageIcons.Of("x") + "</button>")
        + "</div></div>\n";

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

        // The window's sentence always, its zero case included, since the home lists no
        // conversation: "Each is listed below, with the browsers it opened." became wrong
        // once rows were hidden (61, 2026-10-11).
        var said = new List<string>
        {
            board.OnScreen switch
            {
                0 => "None of them has a browser window open on your screen.",
                1 => "A browser window is open on your screen.",
                _ => string.Create(CultureInfo.InvariantCulture, $"{board.OnScreen} browser windows are open on your screen."),
            },
        };

        if (view.Holds is { State: UpdateHoldState.Held, Version: { } version })
        {
            said.Add($"Update {version} installs by itself once nothing uses BrowserAI.");
        }

        return (conversations is 1 ? "1 conversation is using BrowserAI" : string.Create(CultureInfo.InvariantCulture, $"{conversations} conversations are using BrowserAI"),
            string.Join(' ', said));
    }

    /// <summary>The card that answers what runs now: five counts and the one way to the conversations and their browsers.</summary>
    /// <remarks>
    /// <b>The counts are the sessions' states and the conversations</b>: every conversation, with
    /// how many of them are working now; every browser that is a window on the screen; every
    /// hidden browser; every session whose browser is not open; and every session kept for a
    /// conversation to pick up again, which is also counted in one of the three before it. The
    /// button is the card's one link (58 a); the CPU and memory card's <i>Per session</i> is the
    /// other way to the same page (66 a).
    /// </remarks>
    private static string NowCard(PageView view, PageBoard board)
    {
        var working = board.Conversations.Count(static conversation => conversation.Working);
        var notOpen = board.All.Count(static session => PageBoard.StateOf(session) is BrowserState.Closed);
        var conversations = board.Conversations.Count;

        return new StringBuilder("<article class=\"card\" id=\"card-now\">\n")
            .Append(PageContent.CardHead("pulse", "Right now"))
            .Append("<p class=\"caption\">Conversations in Claude Code and Codex that use BrowserAI, and the browsers they opened.</p>\n")
            .Append("<div class=\"stats five\">")
            .Append(Stat("s-conversations", "chat", conversations, "Conversation", "Conversations", warn: false,
                working > 0 ? "<small><span class=\"working\"><span class=\"pulse\" aria-hidden=\"true\"></span>" + PageContent.Text(string.Create(CultureInfo.InvariantCulture, $"{working} working now")) + "</span></small>" : null))
            .Append(Stat("s-onscreen", "window", board.OnScreen, "Window on your screen", "Windows on your screen", warn: board.OnScreen > 0))
            .Append(Stat("s-hidden", "hidden", board.HiddenBrowsers, "Hidden browser", "Hidden browsers", warn: false))
            .Append(Stat("s-notopen", "closed", notOpen, "Browser not open", "Browsers not open", warn: false))
            .Append(Stat("s-kept", "folder", board.Kept, "Kept for later", "Kept for later", warn: false))
            .Append("</div>\n")
            .Append("<a class=\"btn primary go wide\" id=\"l-conversations\" href=\"").Append(PageContent.Route(PageKind.Sessions, view.Tab)).Append("\">See the conversations and browsers").Append(PageIcons.Of("chevron")).Append("</a>\n")
            .Append("</article>\n")
            .ToString();
    }

    private static string Stat(string id, string icon, int count, string one, string many, bool warn, string? below = null) =>
        "<div class=\"stat" + (warn ? " warn" : string.Empty) + "\" id=\"" + id + "\">" + PageIcons.Of(icon) + "<div><b>"
        + count.ToString(CultureInfo.InvariantCulture) + "</b><span>" + PageContent.Text(count is 1 ? one : many) + "</span>" + (below ?? string.Empty) + "</div></div>";

    /// <summary>The card that says when the update installs, or that none waits and when BrowserAI last looked.</summary>
    private static string UpdateCard(PageView view)
    {
        var html = new StringBuilder("<article class=\"card\" id=\"card-update\">\n").Append(PageContent.CardHead("update", "Update"));
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

                if (HeldBy(holds, now) is { } heldBy)
                {
                    _ = html.Append("<p class=\"small\">").Append(PageContent.Text(heldBy)).Append("</p>\n");
                }

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

    /// <summary>What holds a waiting update, in one sentence: <i>Held by 1 window, 3 hidden browsers and 3 active conversations.</i></summary>
    /// <remarks>
    /// <b>One line where the home listed each holder</b>, the side-column layout's, whose words
    /// the maintainer accepted, 67, in his words verbatim: <i>"67 ok"</i>. The update page lists
    /// each one with its countdown, and says how long a conversation holds the update.
    /// </remarks>
    /// <returns>The sentence, or <see langword="null"/> when nothing holds it.</returns>
    internal static string? HeldBy(UpdateHoldSnapshot holds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(holds);

        var parts = new List<string>();
        var windows = holds.VisibleWindows.Count;
        var hidden = holds.HiddenSessions.Count;
        var active = holds.HoldingRelaysAt(now).Count();

        if (windows > 0)
        {
            parts.Add(PageWords.Count(windows, "window", "windows"));
        }

        if (hidden > 0)
        {
            parts.Add(PageWords.Count(hidden, "hidden browser", "hidden browsers"));
        }

        if (active > 0)
        {
            parts.Add(PageWords.Count(active, "active conversation", "active conversations"));
        }

        return parts.Count is 0 ? null : "Held by " + PageParts.Listed(parts) + ".";
    }

    /// <summary>The card that says how Claude Code and Codex are set up.</summary>
    private static string AppsCard(PageView view)
    {
        var html = new StringBuilder("<article class=\"card\" id=\"card-apps\">\n")
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
        var html = new StringBuilder("<article class=\"card\" id=\"card-storage\">\n")
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
        var html = new StringBuilder("<article class=\"card\" id=\"card-install\">\n")
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

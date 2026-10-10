// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using BrowserAI.Registration;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// What the page says, as HTML, from one <see cref="PageView"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is a pure function of its arguments</b>, so the suite asserts
/// every sentence and every button without a browser; the time is a parameter for
/// the same reason. The page and its actions do not know what hosts them (Q315 a):
/// the buttons name an action and the script posts it, and nothing here knows it
/// is a browser tab.
/// </para>
/// <para>
/// ⚠️ <b>Every string that did not come from this file goes through
/// <see cref="Text"/></b> -- a path, a purpose a model wrote, a version a feed
/// offered, an exception's message. The content security policy stops a missed
/// one from running a script, and this is the half that stops it from changing
/// what the page says.
/// </para>
/// </remarks>
internal static class PageContent
{
    /// <summary>Where the footer link goes.</summary>
    public const string GuideUrl = "https://github.com/SixFive7/BrowserAI#readme";

    /// <summary>The whole document for one request.</summary>
    /// <param name="view">What is true now.</param>
    /// <param name="kind">Which page.</param>
    /// <param name="tab">The tab number the address carried.</param>
    /// <param name="occasion">Why this tab was opened.</param>
    /// <param name="now">The time the page is rendered at.</param>
    /// <returns>The HTML.</returns>
    public static string Document(PageView view, PageKind kind, int tab, Occasion occasion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(view);

        var page = PageNames.Of(kind);
        var tabText = tab.ToString(CultureInfo.InvariantCulture);
        var title = kind switch
        {
            PageKind.Sessions => "BrowserAI sessions",
            PageKind.Update => "BrowserAI update",
            PageKind.Changelog => "BrowserAI changelog",
            _ => "BrowserAI",
        };

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{title}</title>
            <link rel="stylesheet" href="page.css">
            <script src="page.js" defer></script>
            </head>
            <body data-page="{page}" data-tab="{tabText}">
            <nav>{Navigation(kind, tab)}</nav>
            <p id="banner" hidden></p>
            <main>
            {Fragment(view, kind, tab, occasion, now)}
            </main>
            <footer><a href="{GuideUrl}" target="_blank" rel="noopener noreferrer">How BrowserAI works</a></footer>
            </body>
            </html>

            """;
    }

    /// <summary>The part of the page a state event replaces.</summary>
    /// <param name="view">What is true now.</param>
    /// <param name="kind">Which page.</param>
    /// <param name="tab">The tab number.</param>
    /// <param name="occasion">Why this tab was opened.</param>
    /// <param name="now">The time the page is rendered at.</param>
    /// <returns>The HTML.</returns>
    public static string Fragment(PageView view, PageKind kind, int tab, Occasion occasion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(view);

        return kind switch
        {
            PageKind.Sessions => SessionsMain(view, now),
            PageKind.Update => UpdatePageContent.Render(view, now),
            PageKind.Changelog => ChangelogPageContent.Render(view),
            _ => StatusMain(view, occasion, tab),
        };
    }

    /// <summary>Encodes text for HTML, attribute values included.</summary>
    /// <param name="text">Anything.</param>
    /// <returns>The encoded text.</returns>
    public static string Text(string? text) => HtmlEncoder.Default.Encode(text ?? string.Empty);

    /// <summary>The heading: the version, and whether this tab came back after an update.</summary>
    /// <param name="facts">What does not change.</param>
    /// <param name="occasion">Why the tab was opened.</param>
    /// <returns>The heading's text.</returns>
    public static string Heading(PageFacts facts, Occasion occasion)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return occasion is Occasion.AfterUpdate ? $"Updated to BrowserAI {facts.Version}" : $"BrowserAI {facts.Version}";
    }

    /// <summary>The update section's sentence for one view, in plain text.</summary>
    /// <param name="facts">What does not change.</param>
    /// <param name="update">The section's state.</param>
    /// <returns>The sentence.</returns>
    public static string UpdateSentence(PageFacts facts, UpdateView update)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(update);

        return update.Stage switch
        {
            UpdateStage.NotChecked => "BrowserAI has not asked the release feed for a newer version since this page opened.",
            UpdateStage.Checking => "Asking the release feed for a newer BrowserAI.",
            UpdateStage.UpToDate => $"BrowserAI {facts.Version} is up to date.",
            UpdateStage.NoReleaseList =>
                "The release feed is a folder with no list of releases in it, so BrowserAI cannot tell whether it is up to date.",
            UpdateStage.Available when update.Older =>
                $"BrowserAI {update.Version} is on offer, and it is older than the installed {facts.Version}. Installing it goes back to the earlier version.",
            UpdateStage.Available => $"BrowserAI {update.Version} is available.",
            UpdateStage.Failed => "The update check did not finish.",
            UpdateStage.NoFeed => "No release feed is set for this build, so there is nothing to check.",
            UpdateStage.NotInstalled => "This BrowserAI is not installed, so there is nothing to update.",
            UpdateStage.Installing =>
                $"Installing BrowserAI {update.Version}. This tab stops working while it installs, and a new tab opens when it is done.",
            UpdateStage.InstallFailed => $"BrowserAI {update.Version} was not installed.",
            _ => string.Empty,
        };
    }

    /// <summary>What installing closes, said beside every install button.</summary>
    public const string InstallWarning =
        "Installing closes BrowserAI and every BrowserAI server running from this install, and their browsers with them. "
        + "Claude Code starts its server again on its next call. A Codex thread gets BrowserAI back only in a new thread.";

    private static string Navigation(PageKind kind, int tab)
    {
        var query = "?tab=" + tab.ToString(CultureInfo.InvariantCulture);
        var links = new StringBuilder();

        foreach (var (page, label) in new[] { (PageKind.Status, "Status"), (PageKind.Sessions, "Sessions"), (PageKind.Update, "Update"), (PageKind.Changelog, "Changelog") })
        {
            var href = page is PageKind.Status ? "./" : PageNames.RouteOf(page);

            _ = links.Append(links.Length > 0 ? " " : string.Empty)
                .Append("<a href=\"").Append(href).Append(query).Append('"')
                .Append(page == kind ? " aria-current=\"page\">" : ">").Append(label).Append("</a>");
        }

        return links.ToString();
    }

    private static string StatusMain(PageView view, Occasion occasion, int tab)
    {
        var facts = view.Facts;
        var html = new StringBuilder();

        _ = html.Append("<h1>").Append(Text(Heading(facts, occasion))).Append("</h1>\n");

        // The 2026-09-24 rendering found the window's first run claiming that every
        // client had been registered, whatever had happened. The sentence claims only
        // the install; what each client says is read live, in its own section below.
        if (occasion is Occasion.FirstRun)
        {
            _ = html.Append("<p>").Append(Text(FirstRunSentence)).Append("</p>\n");
        }

        AppendNote(html, view.Note);
        AppendUpdate(html, view, tab);
        AppendRegistration(html, view);
        AppendWhere(html, facts);

        return html.ToString();
    }

    /// <summary>What the tab the installer opened says first.</summary>
    public const string FirstRunSentence =
        "BrowserAI is installed. Below is how it is registered with each client now. Sessions and threads that were already open do not see BrowserAI until they are started again.";

    /// <summary>Q314 b: what the Codex section says about a Codex that predates the install.</summary>
    public const string CodexStartedBeforeTheInstall =
        "A Codex that was already running when BrowserAI was installed does not find BrowserAI in a project until it is restarted.";

    /// <summary>The last action's sentence, with its raw text under <i>Show details</i>.</summary>
    /// <param name="html">Where to write.</param>
    /// <param name="note">The note, or <see langword="null"/> for none.</param>
    internal static void AppendNote(StringBuilder html, PageNote? note)
    {
        if (note is null)
        {
            return;
        }

        _ = html.Append("<div class=\"note\" role=\"status\"><p>").Append(Text(note.Sentence)).Append("</p>");
        AppendDetails(html, note.Details);
        _ = html.Append("</div>\n");
    }

    private static void AppendDetails(StringBuilder html, string? details)
    {
        if (details is { Length: > 0 })
        {
            _ = html.Append("<details><summary>Show details</summary><pre>").Append(Text(details)).Append("</pre></details>");
        }
    }

    private static void AppendUpdate(StringBuilder html, PageView view, int tab)
    {
        var facts = view.Facts;
        var update = view.Update;

        _ = html.Append("<section id=\"update\"><h2>Updates</h2>\n");

        // The resident background builds this page with no feed of its own and
        // reports what holds an update, so where the page checks for nothing itself
        // the section says what the background holds and leads to the update page.
        if (update.Stage is UpdateStage.NoFeed && view.Holds is { } holds)
        {
            UpdatePageContent.AppendStatusSection(html, holds, tab);
            _ = html.Append("</section>\n");
            return;
        }

        // Q310 a: a package a server has already downloaded is offered first, with a
        // link that installs it, whatever the last check said.
        if (view.Staged is { Length: > 0 } staged && update.Stage is not UpdateStage.Installing)
        {
            _ = html.Append("<p>BrowserAI ").Append(Text(staged)).Append(" is downloaded and ready to install.</p>\n")
                .Append("<p><button type=\"button\" data-action=\"install-update\" data-version=\"").Append(Text(staged)).Append("\">Install BrowserAI ")
                .Append(Text(staged)).Append(" now</button></p>\n")
                .Append("<p class=\"warning\">").Append(Text(InstallWarning)).Append("</p>\n");
        }

        _ = html.Append("<p>").Append(Text(UpdateSentence(facts, update))).Append("</p>\n");

        if (update.Stage is UpdateStage.Failed or UpdateStage.InstallFailed or UpdateStage.NoReleaseList)
        {
            AppendDetails(html, update.Details);
        }

        switch (update.Stage)
        {
            case UpdateStage.NotChecked:
                _ = html.Append(Button("check-updates", "Check for updates"));
                break;

            case UpdateStage.Checking:
                // The 2026-09-24 rendering found a check that hung showed Checking with
                // no way out. The wait can always be given up.
                _ = html.Append(Button("stop-check", "Stop checking"));
                break;

            case UpdateStage.UpToDate or UpdateStage.NoReleaseList:
                _ = html.Append(Button("check-updates", "Check again"));
                break;

            case UpdateStage.Failed or UpdateStage.InstallFailed:
                _ = html.Append(Button("check-updates", "Try again"));
                break;

            case UpdateStage.Available when !string.Equals(update.Version, view.Staged, StringComparison.Ordinal):
                _ = html.Append("<p><button type=\"button\" data-action=\"install-update\" data-version=\"").Append(Text(update.Version))
                    .Append("\">Install BrowserAI ").Append(Text(update.Version)).Append(" now</button></p>\n")
                    .Append("<p class=\"warning\">").Append(Text(InstallWarning)).Append("</p>\n");
                break;

            default:
                break;
        }

        _ = html.Append("</section>\n");
    }

    private static void AppendWhere(StringBuilder html, PageFacts facts)
    {
        _ = html.Append("<section id=\"where\"><h2>Where things are</h2>\n<ul>\n");

        if (facts.InstallRoot is { Length: > 0 } install)
        {
            _ = html.Append("<li>Installed in <code>").Append(Text(install)).Append("</code> ")
                .Append(Button("open-folder", "Open", ("folder", "install"))).Append("</li>\n");
        }
        else
        {
            _ = html.Append("<li>This BrowserAI is not installed, so there is no install folder to show.</li>\n");
        }

        _ = html.Append("<li>Browsers, sessions and logs in <code>").Append(Text(facts.DataRoot)).Append("</code> ")
            .Append(Button("open-folder", "Open", ("folder", "data"))).Append("</li>\n")
            .Append("<li>Logs in <code>").Append(Text(facts.LogDirectory)).Append("</code> ")
            .Append(Button("open-folder", "Open", ("folder", "logs"))).Append("</li>\n")
            .Append("</ul>\n");

        if (facts.ServerCommand is { Length: > 0 } server)
        {
            _ = html.Append("<p>The server a client starts: <code>").Append(Text(server)).Append("</code></p>\n");
        }
        else if (facts.ServerRefusal is { Length: > 0 } refusal)
        {
            _ = html.Append("<p>").Append(Text(refusal)).Append("</p>\n");
        }

        _ = html.Append("</section>\n");
    }

    private static void AppendRegistration(StringBuilder html, PageView view)
    {
        _ = html.Append("<section id=\"registration\"><h2>Registration</h2>\n");

        if (view.Registering is { Length: > 0 } working)
        {
            _ = html.Append("<p role=\"status\">").Append(Text(working)).Append("</p>\n");
        }

        switch (view.Registration)
        {
            case null:
                _ = html.Append("<p class=\"muted\">Reading how BrowserAI is registered with Claude Code and Codex.</p>\n");
                break;

            case { State: null } failed:
                _ = html.Append("<p>BrowserAI could not read how it is registered.</p>\n");
                AppendDetails(html, failed.Failure);
                _ = html.Append(Button("read-registration", "Read it again"));
                break;

            case { State: { } state } read:
                _ = html.Append("<p class=\"muted\">")
                    .Append(Text($"Read at {read.ReadAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}."))
                    .Append(' ').Append(Button("read-registration", "Read again")).Append("</p>\n");

                foreach (var client in state.Clients)
                {
                    AppendClient(html, client, offerActions: view.Registering is null);
                }

                break;
        }

        _ = html.Append("</section>\n");
    }

    /// <summary>One client's registration and what can be done to it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every button names its client</b>, the maintainer's words of 2026-09-24
    /// verbatim: <i>"I easy I want separate control over system level registration
    /// between codex and claude."</i> No action here applies to both at once.
    /// </para>
    /// <para>
    /// <b>The register button's label follows the state</b>, as the window's link
    /// did: absent asks, stale repairs, and present offers to write it again, which
    /// is how a person undoes their own edits to the entry.
    /// </para>
    /// <para>
    /// <b>The words about scope are OutlookAI's</b>, <i>"for all my ... projects"</i>
    /// and <i>"in a project"</i>, because two products in one estate describing one
    /// mechanism differently is how a person learns it twice.
    /// </para>
    /// </remarks>
    /// <param name="html">Where to write.</param>
    /// <param name="client">The client.</param>
    /// <param name="offerActions">Whether buttons are offered: none while an action runs.</param>
    private static void AppendClient(StringBuilder html, ClientState client, bool offerActions)
    {
        var who = client.Client;
        var name = who.DisplayName;

        _ = html.Append("<div class=\"client\" id=\"client-").Append(Text(who.Key)).Append("\"><h3>").Append(Text(name)).Append("</h3>\n")
            .Append("<p>").Append(Text(client.StatusSentence())).Append("</p>\n");

        if (client.ProjectScope is { Command: { Length: > 0 } command } view && client.ProjectDirectory is { Length: > 0 } project)
        {
            _ = html.Append("<p>The project at <code>").Append(Text(project)).Append("</code> registers <code>").Append(Text(command))
                .Append("</code> in <code>").Append(Text(view.File)).Append("</code>.</p>\n");
        }

        if (offerActions)
        {
            if (client.MayRegister)
            {
                var (label, what) = client.UserScope.Ownership switch
                {
                    RegistrationOwnership.OursAndStale =>
                        ($"Repair the {name} registration", "Points that entry at this install, which an entry naming a file that is not the server needs."),
                    RegistrationOwnership.OursAndPresent =>
                        ($"Register again for all my {name} projects", "Rewrites the entry as BrowserAI writes it, which undoes your own edits to it."),
                    _ => ($"Register for all my {name} projects", $"Writes one entry in your own {name} configuration."),
                };

                AppendAction(html, "register", who.Key, label, what);
            }

            if (client.MayUnregister)
            {
                AppendAction(html, "unregister", who.Key, $"Unregister from {name}", "Removes that entry. BrowserAI stays installed, and your sessions and browsers are untouched.");
            }

            if (client.MayRegisterInProject)
            {
                AppendAction(html, "register-in-project", who.Key, $"Register in a project for {name}", $"Writes {who.ProjectFileName} in a folder you choose, to be committed with the project.");
            }

            if (client.MayUnregisterFromProject)
            {
                AppendAction(html, "unregister-from-project", who.Key, $"Remove BrowserAI from that project for {name}", $"Edits {who.ProjectFileIn(client.ProjectDirectory!)}.");
            }

            if (client.MayUnregisterFromAProject)
            {
                AppendAction(html, "unregister-from-a-project", who.Key, $"Remove from a project for {name}", $"Removes BrowserAI's entry from {who.ProjectFileName} in a folder you choose. An entry another install wrote is left alone.");
            }
        }

        if (string.Equals(who.Key, RegistrationClient.Codex.Key, StringComparison.Ordinal) && client.ClientFound)
        {
            _ = html.Append("<p class=\"muted\">").Append(Text(CodexStartedBeforeTheInstall)).Append("</p>\n");
        }

        _ = html.Append("</div>\n");
    }

    private static void AppendAction(StringBuilder html, string action, string client, string label, string what) =>
        _ = html.Append("<p>").Append(Button(action, label, ("client", client)).TrimEnd('\n'))
            .Append(" <span class=\"muted\">").Append(Text(what)).Append("</span></p>\n");

    private static string SessionsMain(PageView view, DateTimeOffset now)
    {
        var html = new StringBuilder();
        var sessions = view.Sessions;

        _ = html.Append("<h1>Sessions</h1>\n")
            .Append("<p>Every BrowserAI server running from this install, the client that started it, and its sessions. ")
            .Append("Closing a server that holds its own sessions ends their browsers. Where the session host holds them, ")
            .Append("closing a client's server ends only its connection, and the host keeps the sessions it drove.</p>\n");

        AppendNote(html, view.Note);

        _ = html.Append("<p class=\"muted\">")
            .Append(sessions.ReadAt == DateTimeOffset.MinValue
                ? "Not read yet."
                : $"Read at {Text(sessions.ReadAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))}.")
            .Append(' ').Append(Button("refresh-sessions", "Refresh")).Append("</p>\n");

        if (sessions.Servers.Count is 0)
        {
            _ = html.Append("<p>No BrowserAI server is running from this install.</p>\n");
        }
        else
        {
            _ = html.Append("<ul class=\"servers\">\n");

            // 1.5 a, 2026-10-10: a VS Code window's tabs are drawn together, under the
            // window, where the first of them would stand.
            foreach (var (window, servers) in ByWindow(sessions.Servers, server => server.Window))
            {
                if (window is null)
                {
                    AppendServer(html, servers[0], now);
                    continue;
                }

                _ = html.Append("<li class=\"window\"><p><strong>").Append(Text(window.Label())).Append("</strong>");

                if (window.Folder is { Length: > 0 } folder)
                {
                    _ = html.Append(" <code>").Append(Text(folder)).Append("</code>");
                }

                _ = html.Append("</p>\n<ul class=\"servers\">\n");

                foreach (var server in servers)
                {
                    AppendServer(html, server, now);
                }

                _ = html.Append("</ul></li>\n");
            }

            _ = html.Append("</ul>\n")
                .Append("<p>").Append(Button("close-servers", "Close the selected servers")).Append("</p>\n");
        }

        if (sessions.Unanswered.Count > 0)
        {
            _ = html.Append("<p>")
                .Append(Text(sessions.Unanswered.Count is 1
                    ? "One more server is running and did not answer, so it is not listed."
                    : $"{sessions.Unanswered.Count} more servers are running and did not answer, so they are not listed."))
                .Append("</p>\n");
            AppendDetails(html, string.Join("\n", sessions.Unanswered));
        }

        return html.ToString();
    }

    /// <summary>
    /// Items in their order, with every item of one VS Code window drawn together where
    /// the first of them stands, and every other item on its own.
    /// </summary>
    /// <typeparam name="T">The item.</typeparam>
    /// <param name="items">The items, in the order they are drawn.</param>
    /// <param name="windowOf">The window an item is a tab of, or <see langword="null"/>.</param>
    /// <returns>Each group: a window and its items, or no window and one item.</returns>
    internal static List<(ClientWindow? Window, List<T> Items)> ByWindow<T>(IEnumerable<T> items, Func<T, ClientWindow?> windowOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(windowOf);

        var groups = new List<(ClientWindow? Window, List<T> Items)>();
        var byKey = new Dictionary<string, List<T>>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (windowOf(item) is not { } window)
            {
                groups.Add((null, [item]));
                continue;
            }

            if (byKey.TryGetValue(window.Key, out var members))
            {
                members.Add(item);
                continue;
            }

            members = [item];
            byKey[window.Key] = members;
            groups.Add((window, members));
        }

        return groups;
    }

    /// <summary>What a client calls itself on the page.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "A client that has not said what it is", with
    /// the version after it)</i>: the update page called the same client <i>unnamed
    /// client</i> with no version, and the texts review found the two (item 85), so both
    /// pages now say it through <see cref="ClientNames"/>.
    /// </remarks>
    /// <param name="server">The server.</param>
    /// <returns>The client's name and version, its name alone, or <see cref="ClientNames.Unnamed"/>.</returns>
    public static string ClientOf(ServerEntry server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var client = server.Description.Client;
        var name = client?.Title is { Length: > 0 } title ? title
            : client?.Name is { Length: > 0 } named ? named
            : server.KnownAs is { Length: > 0 } known ? known
            : null;

        return ClientNames.Of(name, client?.Version);
    }

    /// <summary>How long ago, in words.</summary>
    /// <param name="then">When.</param>
    /// <param name="now">Now.</param>
    /// <returns>The words.</returns>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;

        return elapsed < WordingTimes.Minute ? "less than a minute ago"
            : elapsed < WordingTimes.TwoMinutes ? "a minute ago"
            : elapsed < WordingTimes.Hour ? $"{(int)elapsed.TotalMinutes} minutes ago"
            : elapsed < WordingTimes.TwoHours ? "an hour ago"
            : elapsed < WordingTimes.Day ? $"{(int)elapsed.TotalHours} hours ago"
            : $"{(int)elapsed.TotalDays} days ago";
    }

    /// <summary>The warning a Codex-hosted server carries.</summary>
    public const string CodexWarning =
        "Codex does not start this server again: its thread loses BrowserAI until a new thread is started.";

    /// <summary>The warning a server that answered recently carries.</summary>
    public const string RecentWarning =
        "This server answered a call in the last ten minutes and may be in the middle of a task. Closing it ends its browser.";

    /// <summary>The warning a server that relays to the session host carries when its client was busy.</summary>
    public const string RelayRecentWarning =
        "Its client made a call in the last ten minutes and may be in the middle of a task. Closing this server fails a call it is answering; the session host keeps the sessions.";

    /// <summary>What the page says of the session host.</summary>
    public const string HostSentence =
        "It holds the sessions of every client that reaches BrowserAI through it, and ends a minute after its last session and its last client have gone. The page offers no close for it, because closing it would end every session below.";

    /// <summary>What the page says of a server that relays to the session host.</summary>
    public const string RelaySentence =
        "The session host holds its sessions. Closing this server ends its client's connection, and the host keeps them.";

    /// <summary>The start of what the page says of a session the host keeps.</summary>
    public const string KeptSentence = "Kept: its client has gone";

    /// <summary>What a person can do about a kept session.</summary>
    public const string KeptTakeOver = "A client that names this session takes it over.";

    /// <summary>The words that say how a session stands, after its purpose.</summary>
    /// <param name="session">The session.</param>
    /// <param name="underHost">Whether it is listed under the session host, which names who drives it.</param>
    /// <returns>The sentence, or <see langword="null"/> when there is nothing to add.</returns>
    public static string? StateOf(SessionEntry session, bool underHost)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Kept)
        {
            return session.Headed
                ? $"{KeptSentence}, and its window is still open. {KeptTakeOver} Until then it ends when its window is closed."
                : session.IdleCloseAt is { } at
                    ? $"{KeptSentence}, and its browser was left as it was. {KeptTakeOver} Until then its idle close ends it at about {at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}."
                    : $"{KeptSentence}, and its browser was left as it was. {KeptTakeOver}";
        }

        return underHost && session.DrivenBy is { Length: > 0 } client
            ? session.DrivenThrough is { } through
                ? $"Driven by {client} through its server, pid {through.ToString(CultureInfo.InvariantCulture)}."
                : $"Driven by {client}."
            : null;
    }

    private static void AppendServer(StringBuilder html, ServerEntry server, DateTimeOffset now)
    {
        var description = server.Description;

        if (server.IsHost)
        {
            _ = html.Append("<li class=\"server host\"><p><strong>BrowserAI's session host</strong>, pid ")
                .Append(description.ProcessId.ToString(CultureInfo.InvariantCulture)).Append("</p>\n<p>").Append(Text(HostSentence)).Append("</p>\n");
            AppendSessions(html, server, underHost: true);
            _ = html.Append("</li>\n");
            return;
        }

        _ = html.Append("<li class=\"server\"><label><input type=\"checkbox\" name=\"server\" value=\"").Append(Text(server.Id)).Append("\"> ");

        // 1.2 a, 2026-10-10: the conversation first, as the person sees it called.
        if (server.Conversation is { } conversation)
        {
            _ = html.Append("<strong>").Append(Text(conversation.Shown())).Append("</strong>, ");
        }

        _ = html.Append(Text(ClientOf(server))).Append(", pid ").Append(description.ProcessId.ToString(CultureInfo.InvariantCulture))
            .Append("</label>\n<p>Started in <code>").Append(Text(description.WorkingDirectory)).Append("</code>. ");

        if (server.IsRelay)
        {
            _ = html.Append(Text(RelaySentence)).Append("</p>\n");

            if (server.Kind is ClientKind.Codex)
            {
                _ = html.Append("<p class=\"warning\">").Append(Text(CodexWarning)).Append("</p>\n");
            }

            if (server.RecentlyActive)
            {
                _ = html.Append("<p class=\"warning\">").Append(Text(RelayRecentWarning)).Append("</p>\n");
            }

            AppendSessions(html, server, underHost: false);
            _ = html.Append("</li>\n");
            return;
        }

        _ = html.Append(Text(description.LastToolCall is { } last ? $"Last call {Ago(last, now)}." : "No call yet."));

        if (description.CallsInFlight > 0)
        {
            _ = html.Append(' ').Append(Text(description.CallsInFlight is 1 ? "One call is running now." : $"{description.CallsInFlight} calls are running now."));
        }

        _ = html.Append("</p>\n");

        if (server.Kind is ClientKind.Codex)
        {
            _ = html.Append("<p class=\"warning\">").Append(Text(CodexWarning)).Append("</p>\n");
        }

        if (server.RecentlyActive)
        {
            _ = html.Append("<p class=\"warning\">").Append(Text(RecentWarning)).Append("</p>\n");
        }

        AppendSessions(html, server, underHost: false);
        _ = html.Append("</li>\n");
    }

    private static void AppendSessions(StringBuilder html, ServerEntry server, bool underHost)
    {
        if (server.Sessions.Count is 0)
        {
            _ = html.Append("<p class=\"muted\">")
                .Append(server.IsRelay ? "Its client drives no session the host holds." : "It holds no session.")
                .Append("</p>\n");
            return;
        }

        _ = html.Append("<ul class=\"sessions\">\n");

        foreach (var session in server.Sessions)
        {
            _ = html.Append(session.Kept ? "<li class=\"kept\"><strong>" : "<li><strong>")
                .Append(Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded"))
                .Append("</strong> <span class=\"muted\">").Append(session.BrowserOpen ? "browser open" : "no browser open")
                .Append("</span><br><code>").Append(Text(session.Directory)).Append("</code> ")
                .Append(Button("open-session", "Open its folder", ("session", session.Id)));

            if (StateOf(session, underHost) is { } state)
            {
                _ = html.Append("<br>").Append(Text(state));
            }

            if (session.Traces.Count > 0)
            {
                _ = html.Append("<br><span class=\"muted\">Traces in its output folder: ")
                    .Append(Text(string.Join(", ", session.Traces.Select(trace => trace.Name))))
                    .Append("</span>");
            }

            _ = html.Append("</li>\n");
        }

        _ = html.Append("</ul>\n");
    }

    private static string Button(string action, string label, params (string Name, string Value)[] data)
    {
        var html = new StringBuilder("<button type=\"button\" data-action=\"").Append(Text(action)).Append('"');

        foreach (var (name, value) in data)
        {
            _ = html.Append(" data-").Append(name).Append("=\"").Append(Text(value)).Append('"');
        }

        return html.Append('>').Append(Text(label)).Append("</button>\n").ToString();
    }
}

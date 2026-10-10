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

    /// <summary>Where the installer of the newest release is: the broken install's notice links it.</summary>
    public const string ReleasesUrl = "https://github.com/SixFive7/BrowserAI/releases/latest";

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
            <body data-page="{page}" data-tab="{tabText}" data-unanswered="{Text(Unanswered(view.Facts.InstallRoot is not null))}">
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

    /// <summary>What the page's banner says once BrowserAI stops answering it.</summary>
    /// <remarks>
    /// <i>Added 2026-10-10, the texts polish (previously the script said "BrowserAI is not
    /// answering this page. If it does not come back, open BrowserAI from the Start Menu
    /// again." on every build)</i>: for a build that is not installed, the Start Menu
    /// starts the installed BrowserAI, never this one, as the background's own stop says.
    /// </remarks>
    /// <param name="installed">Whether this BrowserAI is installed.</param>
    /// <returns>The sentence.</returns>
    public static string Unanswered(bool installed) => installed
        ? "BrowserAI is not answering this page. If it does not come back, open BrowserAI from the Start Menu again."
        : "BrowserAI is not answering this page.";

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

        var main = kind switch
        {
            PageKind.Sessions => SessionsMain(view),
            PageKind.Update => UpdatePageContent.Render(view, now),
            PageKind.Changelog => ChangelogPageContent.Render(view),
            _ => StatusMain(view, occasion, tab),
        };

        // 10 b, 2026-10-10: while the install is broken, every page says so first.
        return view.InstallBroken is { } difference ? InstallNotice(difference) + main : main;
    }

    /// <summary>The broken install's notice: what is wrong, what to run, and what is kept.</summary>
    /// <remarks>
    /// <b>The maintainer's 10 b, 2026-10-10.</b> The session that met a browser server
    /// listing different tools was refused, and every session will be until BrowserAI is
    /// reinstalled; the model was told so in the refusal, and the person is told here and
    /// by one toast, whose button opens the status page. ⚠️ <i>Corrected 2026-10-10
    /// (previously "To reinstall, run BrowserAI-win-Setup.exe again, the installer from the
    /// latest release."): the release ships the installer as <c>BrowserAI.exe</c>, and the
    /// notice names it as README's recovery note does.</i> ⚠️ <i>Corrected 2026-10-10 a
    /// second time, the texts polish, page #81 (previously "To reinstall, download
    /// BrowserAI.exe from the latest release and run it. It installs over this
    /// install, ..."): this is the page the toast's How to reinstall opens, and an install
    /// that takes its updates from a folder on this computer is reinstalled from that
    /// folder, with the feed variable it was installed with, which a reinstall does not
    /// keep.</i>
    /// </remarks>
    /// <param name="difference">The first difference, as the refusal words it.</param>
    /// <returns>The HTML.</returns>
    public static string InstallNotice(string difference) =>
        "<div class=\"note broken\" role=\"alert\">"
        + "<p><strong>BrowserAI needs reinstalling.</strong> Part of this install does not match the rest: the browser server it starts for every session lists different tools from the ones this BrowserAI was built with, and the first difference is "
        + Text(difference)
        + ". Every session is refused until BrowserAI is reinstalled.</p>"
        + "<p>Reinstall BrowserAI the way you installed it. For a release, download BrowserAI.exe from <a href=\""
        + ReleasesUrl
        + "\" target=\"_blank\" rel=\"noopener noreferrer\">the latest release</a> and run it. "
        + $"For an install that takes its updates from a folder on this computer, run BrowserAI.exe from that folder with the {UpdateConfiguration.FeedVariable} you installed it with. "
        + "Either way it installs over this install, and every session, with its profile and its files, is kept.</p>"
        + "</div>\n";

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

    /// <summary>What a BrowserAI that is not installed says about updates, on the status page and the update page alike.</summary>
    public const string NotInstalledSentence = "This BrowserAI is not installed, so there is nothing to update.";

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

    /// <summary>The status page's update section: what the background holds, or that this BrowserAI is not installed.</summary>
    /// <remarks>
    /// <para>
    /// <b>The page checks for nothing and installs nothing itself.</b> The background
    /// checks, on a timer, and the update page's <i>Install now</i> installs through its
    /// update core, so this section says whether a downloaded update waits and leads to
    /// the update page, or, where reading that failed, says so (#66 of the texts review).
    /// </para>
    /// <para>
    /// <i>Corrected 2026-10-10 (previously the page's own check, with its buttons, a
    /// version on offer said to be older (Q308 a), and a package a server had staged
    /// with a button that installed it (Q310 a))</i>: the background has built this page
    /// with no feed of its own since 2026-10-08, so none of that could run, and it is
    /// deleted under the maintainer's "9 a".
    /// </para>
    /// </remarks>
    /// <param name="html">Where to write.</param>
    /// <param name="view">What is true now.</param>
    /// <param name="tab">The tab the update page's link keeps.</param>
    private static void AppendUpdate(StringBuilder html, PageView view, int tab)
    {
        var facts = view.Facts;

        _ = html.Append("<section id=\"update\"><h2>Updates</h2>\n");

        if (facts.InstallRoot is null)
        {
            _ = html.Append("<p>").Append(Text(NotInstalledSentence)).Append("</p>\n");
        }
        else if (view.Holds is { } holds)
        {
            UpdatePageContent.AppendStatusSection(html, holds, facts.Version, tab);
        }
        else
        {
            UpdatePageContent.AppendUnread(html, facts);
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

        // The texts polish, 2026-10-10 (previously "Browsers, sessions and logs in"): a
        // session is the directory its agent named, and the data root holds the index.
        _ = html.Append("<li>Browsers, the session index and logs in <code>").Append(Text(facts.DataRoot)).Append("</code> ")
            .Append(Button("open-folder", "Open", ("folder", "data"))).Append("</li>\n")
            .Append("<li>Logs in <code>").Append(Text(facts.LogDirectory)).Append("</code> ")
            .Append(Button("open-folder", "Open", ("folder", "logs"))).Append("</li>\n")
            .Append("</ul>\n");

        // #67, 2026-10-10: the whole command, as every registration of this install
        // writes it. Started with no argument the same file is a person's start, which
        // opens this page.
        if (facts.ServerCommand is { Length: > 0 } server)
        {
            _ = html.Append("<p>The server a client starts: <code>").Append(Text(CommandLine(server, facts.ServerArguments))).Append("</code></p>\n");
        }
        else if (facts.ServerRefusal is { Length: > 0 } refusal)
        {
            _ = html.Append("<p>").Append(Text(refusal)).Append("</p>\n");
        }

        _ = html.Append("</section>\n");
    }

    /// <summary>A command and its arguments as one line: the command quoted, a flag as it is, and every value in quotes.</summary>
    /// <remarks>
    /// <b>The rule of the line a person runs by hand, <c>RegistrationClient.Typed</c>,
    /// since 2026-10-10</b>, round 2 of the texts review, #89 (previously a value was
    /// quoted only when it held a space), so the status page and the lines it shows in a
    /// registration's details quote a data root the same way.
    /// </remarks>
    /// <param name="command">The executable.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The line.</returns>
    internal static string CommandLine(string command, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);

        return arguments.Count is 0 ? $"\"{command}\"" : $"\"{command}\" {RegistrationClient.Typed(arguments)}";
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

    /// <summary>The sessions page's main part: the background, the sessions it holds, and each client connected to it.</summary>
    /// <remarks>
    /// <b>17 a, the maintainer's answer of 2026-10-10, verbatim: <i>"17 a"</i></b>: the
    /// page offers no close. The background refused every close it offered, because a
    /// relay ends with its client, so the boxes, the button and the close path went
    /// (#68 of the texts review).
    /// </remarks>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    private static string SessionsMain(PageView view)
    {
        var html = new StringBuilder();
        var sessions = view.Sessions;

        _ = html.Append("<h1>Sessions</h1>\n")
            .Append("<p>").Append(Text(SessionsIntroduction)).Append("</p>\n");

        AppendNote(html, view.Note);

        _ = html.Append("<p class=\"muted\">")
            .Append(sessions.ReadAt == DateTimeOffset.MinValue
                ? "Not read yet."
                : $"Read at {Text(sessions.ReadAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))}.")
            .Append(' ').Append(Button("refresh-sessions", "Refresh")).Append("</p>\n");

        // Nothing is listed only before the first read, which the line above says: once
        // read, the background lists itself.
        if (sessions.Servers.Count > 0)
        {
            _ = html.Append("<ul class=\"servers\">\n");

            // 1.5 a, 2026-10-10: a VS Code window's tabs are drawn together, under the
            // window, where the first of them would stand.
            foreach (var (window, servers) in ByWindow(sessions.Servers, server => server.Window))
            {
                if (window is null)
                {
                    AppendServer(html, servers[0]);
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
                    AppendServer(html, server);
                }

                _ = html.Append("</ul></li>\n");
            }

            _ = html.Append("</ul>\n");
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

    /// <summary>What the sessions page says first.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10, the texts polish, page #92 (previously the first sentence
    /// alone)</i>: how a client's connection ends is said once here, and no longer in
    /// every client's entry.
    /// </remarks>
    public const string SessionsIntroduction =
        "BrowserAI's background, every session it holds, and each client connected to it. A client's connection ends when the client closes it or exits, and when an update installs.";

    /// <summary>What the page says of the background.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "It holds the sessions of every client that
    /// reaches BrowserAI through it, and ends a minute after its last session and its last
    /// client have gone. The page offers no close for it, because closing it would end
    /// every session below.")</i>, #68 of the texts review: the one background ends at
    /// sign-out, at an uninstall or for an update, never because it is idle.
    /// </remarks>
    public const string HostSentence =
        "It holds every session, and ends at sign-out, at an uninstall or for an update, never because it is idle.";

    /// <summary>What the page says of each client connected to the background.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "The session host holds its sessions. Closing
    /// this server ends its client's connection, and the host keeps them.")</i>: the page
    /// closes nothing since 17 a, and a relay ends with its client.
    /// ⚠️ <i>Corrected 2026-10-10 a second time, round 2 of the texts review, #93
    /// (previously "It ends when its client closes or its conversation ends.")</i>: an
    /// update installing ends every connection too, as the update page says, and a
    /// conversation's end does not end one in Claude Code, where <c>/clear</c> starts a
    /// new conversation on the same connection (measured, kb/mcp/protocol.md).
    /// ⚠️ <i>Deleted 2026-10-10, the texts polish, page #95</i>: the sentence moved into
    /// <see cref="SessionsIntroduction"/>, said once for every client.
    /// </remarks>
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
                ? $"Driven by {client} through its connection, pid {through.ToString(CultureInfo.InvariantCulture)}."
                : $"Driven by {client}."
            : null;
    }

    /// <summary>One entry: the background with every session it holds, or one client connected to it.</summary>
    /// <remarks>
    /// <b>Since 17 a (2026-10-10) the page offers no close</b>, so no entry has a box, and
    /// the warnings about what a close would end went with it. Every session is the
    /// background's and is listed under it, naming the client that drives it, and a
    /// client's entry lists the sessions its client drives as well. The one background
    /// reports only itself and its relays, so no other kind of entry reaches the page.
    /// </remarks>
    /// <param name="html">Where to write.</param>
    /// <param name="server">The entry.</param>
    private static void AppendServer(StringBuilder html, ServerEntry server)
    {
        var description = server.Description;

        if (server.IsHost)
        {
            _ = html.Append("<li class=\"server host\"><p><strong>BrowserAI's background</strong>, pid ")
                .Append(description.ProcessId.ToString(CultureInfo.InvariantCulture)).Append("</p>\n<p>").Append(Text(HostSentence)).Append("</p>\n");
            AppendSessions(html, server, underHost: true);
            _ = html.Append("</li>\n");
            return;
        }

        _ = html.Append("<li class=\"server\"><p>");

        // 1.2 a, 2026-10-10: the conversation first, as the person sees it called.
        if (server.Conversation is { } conversation)
        {
            _ = html.Append("<strong>").Append(Text(conversation.Shown())).Append("</strong>, ");
        }

        _ = html.Append(Text(ClientOf(server))).Append(", pid ").Append(description.ProcessId.ToString(CultureInfo.InvariantCulture)).Append("</p>\n");

        // The texts polish, 2026-10-10, page #95 (previously "Started in <folder>." after
        // every name, and the relay sentence): BrowserAI's own words for a conversation it
        // has no title for end with the folder's name already, "Claude Code in BrowserAI",
        // so the folder is named again only beside a title, or when nothing names it. The
        // update page's rule, UpdatePageContent.NamesTheFolder.
        if (!UpdatePageContent.NamesTheFolder(server.Conversation, description.WorkingDirectory))
        {
            _ = html.Append("<p>Started in <code>").Append(Text(description.WorkingDirectory)).Append("</code>.</p>\n");
        }

        AppendSessions(html, server, underHost: false);
        _ = html.Append("</li>\n");
    }

    private static void AppendSessions(StringBuilder html, ServerEntry server, bool underHost)
    {
        if (server.Sessions.Count is 0)
        {
            _ = html.Append("<p class=\"muted\">")
                .Append(server.IsRelay ? "Its client drives no session." : "It holds no session.")
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

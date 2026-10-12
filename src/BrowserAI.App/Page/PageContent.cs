// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using BrowserAI.Registration;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// What the page says, as HTML, from one <see cref="PageView"/>: the frame every page
/// shares, and the parts every page draws the same way.
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
/// <b>40 a, F, the maintainer's choice of 2026-10-11</b>, in his words verbatim
/// <i>"40-46 your recommendations"</i>: direction A, a home of cards each leading to a
/// page of its own that begins with a way back, and direction C's live map as the page
/// the home's <i>See all</i> leads to. The directions and the mockups are in
/// <c>docs/design/dashboard-2026-10-11</c>.
/// </para>
/// <para>
/// ⚠️ <b>Every string that did not come from this file goes through
/// <see cref="Text"/></b> -- a path, a purpose a model wrote, a version a feed
/// offered, an exception's message. The content security policy stops a missed
/// one from running a script, and this is the half that stops it from changing
/// what the page says.
/// </para>
/// <para>
/// <b>Every control carries an id that one state and the next agree on</b>, so the
/// page's script can give the keyboard focus back to the control that had it when a
/// new state replaces the page's main part (46 a).
/// </para>
/// </remarks>
internal static class PageContent
{
    /// <summary>Where the help link goes.</summary>
    public const string GuideUrl = "https://github.com/SixFive7/BrowserAI#readme";

    /// <summary>Where the installer of the newest release is: the broken install's notice links it.</summary>
    public const string ReleasesUrl = "https://github.com/SixFive7/BrowserAI/releases/latest";

    /// <summary>What a BrowserAI that is not installed says about updates, on every page that speaks of them.</summary>
    public const string NotInstalledSentence = "This BrowserAI is not installed, so there is nothing to update.";

    /// <summary>What the tab the installer opened says first, under its notice.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously "BrowserAI is installed. Below is how it is
    /// registered with each client now. Sessions and threads that were already open do not
    /// see BrowserAI until they are started again.")</i>, the maintainer's 42 a: the apps
    /// are set up, and what was open is a conversation. <i>Corrected again 2026-10-11, fact 4 of
    /// that day's reader pass (previously "A conversation that was already open sees BrowserAI
    /// only after it is started again.")</i>: a Codex conversation never sees it again, and needs a
    /// new one, as each client's restart hint says.
    /// </remarks>
    public const string FirstRunSentence =
        "Below is how Claude Code and Codex are set up now. A Claude Code conversation that was already open sees BrowserAI only after it is started again, and Codex sees it only in a new conversation.";

    /// <summary>Q314 b: what the Codex card says about a Codex that predates the install.</summary>
    public const string CodexStartedBeforeTheInstall =
        "A Codex that was already running when BrowserAI was installed does not find BrowserAI in a project until it is restarted.";

    /// <summary>
    /// The maintainer's 30, 2026-10-10: what the Claude Code card says about a Claude Code
    /// that predates the install, now that its project entry names the bare name too.
    /// </summary>
    public const string ClaudeStartedBeforeTheInstall =
        "A Claude Code that was already running when BrowserAI was installed does not find BrowserAI in a project until it is restarted.";

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

        var facts = view.Facts;
        var page = PageNames.Of(kind);
        var tabText = tab.ToString(CultureInfo.InvariantCulture);
        var running = facts.StartedAt is { } started ? $"Running since {PageWords.When(started, now)}" : "Running";
        var subject = view.Subject is { Length: > 0 } named ? $" data-subject=\"{Text(named)}\"" : string.Empty;

        // 73 b, 2026-10-11: one line under the page's first folder link, which the script puts there.
        var linkHint = view.Links is not null && tab < 0 ? $" data-link-hint=\"{Text(LinkHint)}\"" : string.Empty;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <title>{Text(TitleOf(view, kind))}</title>
            <link rel="stylesheet" href="page.css">
            <script src="page.js" defer></script>
            </head>
            <body data-page="{page}" data-tab="{tabText}"{subject} data-unanswered="{Text(Unanswered(facts.InstallRoot is not null))}"{linkHint}>
            <header class="top"><div class="top-in">
            <a class="brand" id="l-brand" href="{Home(tab)}">{PageIcons.Brand("top")}<span>BrowserAI</span> <span class="version">{Text(facts.Version)}</span></a>
            <span class="top-gap"></span>
            <span class="running"><span class="dot" aria-hidden="true"></span>{Text(running)}</span>
            <a class="help" id="l-help" href="{GuideUrl}" target="_blank" rel="noopener noreferrer">{PageIcons.Of("question")}<span>How BrowserAI works</span></a>
            </div></header>
            <p id="banner" class="banner" role="status" hidden></p>
            <main id="main" tabindex="-1">
            {Fragment(view, kind, tab, occasion, now)}
            </main>
            <footer class="foot"><span>BrowserAI {Text(facts.Version)}</span><a id="l-foot-changelog" href="{Route(PageKind.Changelog, tab)}">What is new</a><a id="l-foot-install" href="{Route(PageKind.Install, tab)}">This install</a></footer>
            </body>
            </html>

            """;
    }

    /// <summary>What the page's banner says once BrowserAI stops answering it.</summary>
    /// <remarks>
    /// <para>
    /// <i>Added 2026-10-10, the texts polish (previously the script said "BrowserAI is not
    /// answering this page. If it does not come back, open BrowserAI from the Start Menu
    /// again." on every build)</i>: for a build that is not installed, the Start Menu
    /// starts the installed BrowserAI, never this one, as the background's own stop says.
    /// </para>
    /// <para>
    /// ⚠️ <b>71 a, the maintainer's words of 2026-10-11 verbatim: <i>"71 a - maybe
    /// communicate on the page that you need to restart from the start menu?"</i></b> A folder
    /// link clicked while no background answers opens nothing, so the banner says what starts
    /// BrowserAI again, and that the start opens the page in a tab of its own. <i>Corrected
    /// 2026-10-12 (previously "BrowserAI is not answering this page. If it does not come back,
    /// open BrowserAI from the Start Menu again.")</i>. The start hands out a newer tab, which
    /// replaces this one if it is reloaded (Q337 a), so the sentence does not send the person
    /// back to reload this one.
    /// </para>
    /// </remarks>
    /// <param name="installed">Whether this BrowserAI is installed.</param>
    /// <returns>The sentence.</returns>
    public static string Unanswered(bool installed) => installed
        ? "BrowserAI is not answering this page. If it does not come back, open BrowserAI from the Start Menu again."
        : "BrowserAI is not answering this page.";

    /// <summary>
    /// The line under a page's first folder link: the browser asks once whether the page may
    /// open BrowserAI.
    /// </summary>
    /// <remarks>
    /// <b>73 b, the maintainer's words of 2026-10-11 verbatim: <i>"73 b"</i></b>, of a line
    /// whose wording he left to the build. Firefox asks per address, with a box that always
    /// allows it, and the address stays the same while the background runs and across its
    /// restarts (69 d with b), so the question comes once.
    /// </remarks>
    public const string LinkHint =
        "The first time, your browser asks whether this page may open BrowserAI. Allow it always, and folders open at once from then on.";

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

        // 55 a and 70 a, 2026-10-11: the folder controls are links carrying the page's token
        // while the page is drawn with them (PageContent.Button).
        using var links = PageLinks.Use(view.Links);

        var drawn = view with { Tab = tab, Now = now };
        var main = kind switch
        {
            PageKind.Sessions => ConversationsContent.Render(drawn),
            PageKind.Update => UpdatePageContent.Render(drawn, now),
            PageKind.Changelog => ChangelogPageContent.Render(drawn),
            PageKind.Session => SessionPageContent.Render(drawn),
            PageKind.Setup => SetupContent.Render(drawn),
            PageKind.Storage => StorageContent.Render(drawn),
            PageKind.Install => InstallContent.Render(drawn),
            _ => OverviewContent.Render(drawn, occasion),
        };

        // 10 b, 2026-10-10: while the install is broken, every page says so first.
        return view.InstallBroken is { } difference ? InstallNotice(difference) + main : main;
    }

    /// <summary>The broken install's notice: what is wrong, what to run, and what is kept.</summary>
    /// <remarks>
    /// <b>The maintainer's 10 b, 2026-10-10.</b> The session that met a browser server
    /// listing different tools was refused, and every session will be until BrowserAI is
    /// reinstalled; the model was told so in the refusal, and the person is told here and
    /// by one toast, whose button opens the home. ⚠️ <i>Corrected 2026-10-10
    /// (previously "To reinstall, run BrowserAI-win-Setup.exe again, the installer from the
    /// latest release."): the release ships the installer as <c>BrowserAI.exe</c>, and the
    /// notice names it as README's recovery note does.</i> ⚠️ <i>Corrected 2026-10-10 a
    /// second time, the texts polish, page #81 (previously "To reinstall, download
    /// BrowserAI.exe from the latest release and run it. It installs over this
    /// install, ..."): this is the page the toast's How to reinstall opens, and an install
    /// that takes its updates from a folder on this computer is reinstalled from that
    /// folder, with the feed variable it was installed with, which a reinstall does not
    /// keep.</i> <i>Restyled 2026-10-11 as a banner with its icon (40 a); every word is
    /// as it was.</i>
    /// </remarks>
    /// <param name="difference">The first difference, as the refusal words it.</param>
    /// <returns>The HTML.</returns>
    public static string InstallNotice(string difference) =>
        "<div class=\"note broken\" role=\"alert\">" + PageIcons.Of("bad") + "<div class=\"grow\">"
        + "<p><strong>BrowserAI needs reinstalling.</strong> Part of this install does not match the rest: the browser server it starts for every session lists different tools from the ones this BrowserAI was built with, and the first difference is "
        + Text(difference)
        + ". Every session is refused until BrowserAI is reinstalled.</p>"
        + "<p>Reinstall BrowserAI the way you installed it. For a release, download BrowserAI.exe from <a id=\"l-releases\" href=\""
        + ReleasesUrl
        + "\" target=\"_blank\" rel=\"noopener noreferrer\">the latest release</a> and run it. "
        + $"For an install that takes its updates from a folder on this computer, run BrowserAI.exe from that folder with the {UpdateConfiguration.FeedVariable} you installed it with. "
        + "Either way it installs over this install, and every session, with its profile and its files, is kept.</p>"
        + "</div></div>\n";

    /// <summary>Encodes text for HTML, attribute values included.</summary>
    /// <param name="text">Anything.</param>
    /// <returns>The encoded text.</returns>
    public static string Text(string? text) => HtmlEncoder.Default.Encode(text ?? string.Empty);

    /// <summary>Encodes text for a <c>pre</c> element line by line, so its line ends stay line ends in the markup.</summary>
    /// <remarks>
    /// <see cref="Text"/> writes a line end as a character reference, which a browser shows
    /// the same, and which leaves the markup one line a person reading it cannot follow.
    /// </remarks>
    /// <param name="text">Anything.</param>
    /// <returns>The encoded lines, joined by line ends.</returns>
    public static string Lines(string? text) =>
        string.Join('\n', (text ?? string.Empty).Split('\n').Select(static line => Text(line.TrimEnd('\r'))));

    /// <summary>A command and its arguments as one line: the command quoted, a flag as it is, and every value in quotes.</summary>
    /// <remarks>
    /// <b>The rule of the line a person runs by hand, <c>RegistrationClient.Typed</c>,
    /// since 2026-10-10</b>, round 2 of the texts review, #89 (previously a value was
    /// quoted only when it held a space), so the install page and the lines it shows in a
    /// setup's details quote a data root the same way.
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

    /// <summary>The last action's sentence, with its raw text under <i>Show details</i>.</summary>
    /// <param name="html">Where to write.</param>
    /// <param name="note">The note, or <see langword="null"/> for none.</param>
    internal static void AppendNote(StringBuilder html, PageNote? note)
    {
        if (note is null)
        {
            return;
        }

        _ = html.Append("<div class=\"note said\" role=\"status\">").Append(PageIcons.Of("info")).Append("<div class=\"grow\"><p>").Append(Text(note.Sentence)).Append("</p>");
        AppendDetails(html, "d-note", note.Details);
        _ = html.Append("</div></div>\n");
    }

    /// <summary>The raw text behind a sentence, folded under <i>Show details</i>, Q309 b.</summary>
    /// <param name="html">Where to write.</param>
    /// <param name="id">The fold's id, which keeps it open across a new state.</param>
    /// <param name="details">The raw text, or <see langword="null"/> for none.</param>
    internal static void AppendDetails(StringBuilder html, string id, string? details)
    {
        if (details is { Length: > 0 })
        {
            _ = html.Append("<details class=\"more-info\" id=\"").Append(id).Append("\"><summary id=\"").Append(id).Append("-s\">Show details</summary><pre>").Append(Lines(details)).Append("</pre></details>");
        }
    }

    /// <summary>The address of a page, keeping the tab's number.</summary>
    /// <param name="kind">The page.</param>
    /// <param name="tab">The tab's number.</param>
    /// <param name="extra">More of the query, or <see langword="null"/>.</param>
    /// <returns>The relative address.</returns>
    internal static string Route(PageKind kind, int tab, string? extra = null) =>
        (kind is PageKind.Status ? "./" : PageNames.RouteOf(kind))
        + "?tab=" + tab.ToString(CultureInfo.InvariantCulture)
        + (extra is null ? string.Empty : "&amp;" + extra);

    /// <summary>The home's address, keeping the tab's number.</summary>
    /// <param name="tab">The tab's number.</param>
    /// <returns>The relative address.</returns>
    internal static string Home(int tab) => Route(PageKind.Status, tab);

    /// <summary>One session's page, by its opaque name.</summary>
    /// <param name="id">The session's opaque name.</param>
    /// <param name="tab">The tab's number.</param>
    /// <returns>The relative address.</returns>
    internal static string SessionRoute(string id, int tab) => Route(PageKind.Session, tab, "id=" + Text(id));

    /// <summary>The way back at the top of every page but the home: the home, then the pages between.</summary>
    /// <param name="tab">The tab's number.</param>
    /// <param name="here">What this page is called.</param>
    /// <param name="between">The pages between the home and this one, each with its address.</param>
    /// <returns>The HTML.</returns>
    internal static string Crumbs(int tab, string here, params (string Label, PageKind Kind)[] between)
    {
        var html = new StringBuilder("<nav class=\"crumbs\" aria-label=\"Where you are\"><a id=\"l-crumb-home\" href=\"")
            .Append(Home(tab)).Append("\">Overview</a>");

        foreach (var (label, kind) in between)
        {
            _ = html.Append(PageIcons.Of("chevron")).Append("<a id=\"l-crumb-").Append(PageNames.Of(kind)).Append("\" href=\"").Append(Route(kind, tab)).Append("\">")
                .Append(Text(label)).Append("</a>");
        }

        return html.Append(PageIcons.Of("chevron")).Append("<span aria-current=\"page\">").Append(Text(here)).Append("</span></nav>\n").ToString();
    }

    /// <summary>A page's heading and the sentence under it that says what the page is.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="lede">The sentence, or <see langword="null"/>.</param>
    /// <returns>The HTML.</returns>
    internal static string Title(string title, string? lede) =>
        "<h1 class=\"page-title\">" + Text(title) + "</h1>\n" + (lede is { Length: > 0 } ? "<p class=\"lede\">" + Text(lede) + "</p>\n" : string.Empty);

    /// <summary>A button that posts one action, or the folder link it is on a page that draws links.</summary>
    /// <remarks>
    /// <b>55 a, 70 a and 75 a, 2026-10-11</b>: a button that opens a folder, <c>open-folder</c>
    /// and <c>open-session</c>, and one that opens Windows' folder picker for a setup, is drawn
    /// as a <c>browserai:</c> link carrying the page's token while the page is drawn with links
    /// (<see cref="PageLinks.Use"/>), and keeps its id, its look and its label. Every page
    /// draws these controls through here, so no composer knows a control is a link.
    /// </remarks>
    /// <param name="id">Its id.</param>
    /// <param name="action">The action it names.</param>
    /// <param name="label">What it says.</param>
    /// <param name="style">Its look: <c>btn</c> and more, such as <c>btn primary</c>.</param>
    /// <param name="icon">An icon before the label, or <see langword="null"/>.</param>
    /// <param name="data">What else it posts, each as a <c>data-</c> attribute.</param>
    /// <returns>The HTML.</returns>
    internal static string Button(string id, string action, string label, string style = "btn", string? icon = null, params (string Name, string Value)[] data)
    {
        if (PageLinks.Current is { } links && action.Length < 0 && PageLinks.Href(links, action, data) is { } href)
        {
            return "<a id=\"" + Text(id) + "\" class=\"" + style + "\" href=\"" + Text(href) + "\" data-link=\"" + Text(action) + "\">"
                + (icon is null ? string.Empty : PageIcons.Of(icon)) + Text(label) + "</a>";
        }

        var html = new StringBuilder("<button type=\"button\" id=\"").Append(Text(id)).Append("\" class=\"").Append(style).Append("\" data-action=\"").Append(Text(action)).Append('"');

        foreach (var (name, value) in data)
        {
            _ = html.Append(" data-").Append(name).Append("=\"").Append(Text(value)).Append('"');
        }

        return html.Append('>').Append(icon is null ? string.Empty : PageIcons.Of(icon)).Append(Text(label)).Append("</button>").ToString();
    }

    /// <summary>A link that looks like a button.</summary>
    /// <param name="id">Its id.</param>
    /// <param name="href">Where it goes, already an attribute value.</param>
    /// <param name="label">What it says.</param>
    /// <param name="style">Its look.</param>
    /// <returns>The HTML.</returns>
    internal static string LinkButton(string id, string href, string label, string style = "btn") =>
        "<a id=\"" + Text(id) + "\" class=\"" + style + "\" href=\"" + href + "\">" + Text(label) + "</a>";

    /// <summary>A notice: an icon, a short title and one line, with what to do about it.</summary>
    /// <param name="level">Its colour: <c>bad</c>, <c>warn</c> or <c>good</c>.</param>
    /// <param name="title">The title.</param>
    /// <param name="text">The line.</param>
    /// <param name="action">A link or a button, already HTML, or <see langword="null"/>.</param>
    /// <param name="alert">Whether it is read out at once, as a problem is.</param>
    /// <returns>The HTML.</returns>
    internal static string Notice(string level, string title, string text, string? action = null, bool alert = false) =>
        "<div class=\"notice " + level + "\"" + (alert ? " role=\"alert\"" : string.Empty) + ">"
        + PageIcons.Of(level switch { "bad" => "bad", "warn" => "warn", _ => "checkCircle" })
        + "<div class=\"grow\"><h3>" + Text(title) + "</h3><p>" + Text(text) + "</p></div>"
        + (action ?? string.Empty) + "</div>\n";

    /// <summary>A card's head: its icon, its title, and a link to the page behind it.</summary>
    /// <param name="icon">The icon.</param>
    /// <param name="title">The title.</param>
    /// <param name="more">The link: its id, its label and its address; or <see langword="null"/>.</param>
    /// <returns>The HTML.</returns>
    internal static string CardHead(string icon, string title, (string Id, string Label, string Href)? more = null) =>
        "<div class=\"card-head\">" + PageIcons.Of(icon) + "<h2>" + Text(title) + "</h2>"
        + (more is { } link ? "<a class=\"more\" id=\"" + link.Id + "\" href=\"" + link.Href + "\">" + Text(link.Label) + PageIcons.Of("chevron") + "</a>" : string.Empty)
        + "</div>\n";

    /// <summary>An empty state: what would appear in this place, and when.</summary>
    /// <param name="icon">The icon.</param>
    /// <param name="title">What is not there.</param>
    /// <param name="text">What would appear, and why.</param>
    /// <returns>The HTML.</returns>
    internal static string Empty(string icon, string title, string text) =>
        "<div class=\"empty\">" + PageIcons.Of(icon) + "<p class=\"empty-title\">" + Text(title) + "</p><p>" + Text(text) + "</p></div>\n";

    /// <summary>A countdown the page's script keeps live: the deadline in the element, the time left as its text.</summary>
    /// <param name="ends">The deadline.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The HTML.</returns>
    internal static string Countdown(DateTimeOffset ends, DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"<span class=\"countdown\" data-ends-at=\"{ends.ToUnixTimeMilliseconds()}\">{UpdateToastContent.Remaining(ends - now)}</span>");

    /// <summary>The badge an app is shown with: its initials on its colour.</summary>
    /// <param name="clientName">The app's client name.</param>
    /// <returns>The HTML.</returns>
    internal static string Badge(string? clientName) =>
        PageWords.IsCodex(clientName) ? "<span class=\"app cx\" aria-hidden=\"true\">CX</span>"
        : string.Equals(PageWords.AppOf(clientName), "Claude Code", StringComparison.Ordinal) ? "<span class=\"app cc\" aria-hidden=\"true\">CC</span>"
        : "<span class=\"app other\" aria-hidden=\"true\">" + PageIcons.Of("chat") + "</span>";

    /// <summary>The words <c>&lt;title&gt;</c> carries for one page.</summary>
    private static string TitleOf(PageView view, PageKind kind) => kind switch
    {
        PageKind.Sessions => "Conversations and browsers - BrowserAI",
        PageKind.Update => "Update - BrowserAI",
        PageKind.Changelog => "What is new - BrowserAI",
        PageKind.Session => SessionPageContent.NameOf(view) + " - BrowserAI",
        PageKind.Setup => "Claude Code and Codex - BrowserAI",
        PageKind.Storage => "Storage - BrowserAI",
        PageKind.Install => "This install - BrowserAI",
        _ => "BrowserAI",
    };
}

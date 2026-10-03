// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

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

        var page = kind is PageKind.Sessions ? "sessions" : "status";
        var tabText = tab.ToString(CultureInfo.InvariantCulture);

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{(kind is PageKind.Sessions ? "BrowserAI sessions" : "BrowserAI")}</title>
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

        return kind is PageKind.Sessions ? SessionsMain(view, now) : StatusMain(view, occasion);
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

        return kind is PageKind.Sessions
            ? $"""<a href="./{query}">Status</a> <a href="sessions{query}" aria-current="page">Sessions</a>"""
            : $"""<a href="./{query}" aria-current="page">Status</a> <a href="sessions{query}">Sessions</a>""";
    }

    private static string StatusMain(PageView view, Occasion occasion)
    {
        var facts = view.Facts;
        var html = new StringBuilder();

        _ = html.Append("<h1>").Append(Text(Heading(facts, occasion))).Append("</h1>\n");

        if (occasion is Occasion.FirstRun)
        {
            _ = html.Append("<p>BrowserAI is installed.</p>\n");
        }

        AppendNote(html, view.Note);
        AppendUpdate(html, view);
        AppendWhere(html, facts);
        AppendRegistrationBridge(html);

        return html.ToString();
    }

    private static void AppendNote(StringBuilder html, PageNote? note)
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

    private static void AppendUpdate(StringBuilder html, PageView view)
    {
        var facts = view.Facts;
        var update = view.Update;

        _ = html.Append("<section id=\"update\"><h2>Updates</h2>\n");

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

    private static void AppendRegistrationBridge(StringBuilder html) =>
        _ = html.Append("<section id=\"registration\"><h2>Registration</h2>\n")
            .Append("<p>Registering BrowserAI with Claude Code and Codex is still done in the BrowserAI window.</p>\n")
            .Append(Button("registration-window", "Open the registration window"))
            .Append("</section>\n");

    private static string SessionsMain(PageView view, DateTimeOffset now)
    {
        var html = new StringBuilder();
        var sessions = view.Sessions;

        _ = html.Append("<h1>Sessions</h1>\n")
            .Append("<p>Every BrowserAI server running from this install, the client that started it, and the sessions it holds. ")
            .Append("Closing a server ends its browsers. BrowserAI closes nothing on its own.</p>\n");

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

            foreach (var server in sessions.Servers)
            {
                AppendServer(html, server, now);
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

    /// <summary>What a client calls itself on the page.</summary>
    /// <param name="server">The server.</param>
    /// <returns>The client's name and version, or a sentence fragment when it has not said.</returns>
    public static string ClientOf(ServerEntry server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var client = server.Description.Client;
        var name = client?.Title is { Length: > 0 } title ? title
            : client?.Name is { Length: > 0 } named ? named
            : "A client that has not said what it is";

        return client?.Version is { Length: > 0 } version ? $"{name} {version}" : name;
    }

    /// <summary>How long ago, in words.</summary>
    /// <param name="then">When.</param>
    /// <param name="now">Now.</param>
    /// <returns>The words.</returns>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;

        return elapsed < TimeSpan.FromMinutes(1) ? "less than a minute ago"
            : elapsed < TimeSpan.FromMinutes(2) ? "a minute ago"
            : elapsed < TimeSpan.FromHours(1) ? $"{(int)elapsed.TotalMinutes} minutes ago"
            : elapsed < TimeSpan.FromHours(2) ? "an hour ago"
            : elapsed < TimeSpan.FromDays(1) ? $"{(int)elapsed.TotalHours} hours ago"
            : $"{(int)elapsed.TotalDays} days ago";
    }

    /// <summary>The warning a Codex-hosted server carries.</summary>
    public const string CodexWarning =
        "Codex does not start this server again: its thread loses BrowserAI until a new thread is started.";

    /// <summary>The warning a server that answered recently carries.</summary>
    public const string RecentWarning =
        "This server answered a call in the last ten minutes and may be in the middle of a task. Closing it ends its browser.";

    private static void AppendServer(StringBuilder html, ServerEntry server, DateTimeOffset now)
    {
        var description = server.Description;

        _ = html.Append("<li class=\"server\"><label><input type=\"checkbox\" name=\"server\" value=\"").Append(Text(server.Id)).Append("\"> ")
            .Append(Text(ClientOf(server))).Append(", pid ").Append(description.ProcessId.ToString(CultureInfo.InvariantCulture))
            .Append("</label>\n<p>Started in <code>").Append(Text(description.WorkingDirectory)).Append("</code>. ")
            .Append(Text(description.LastToolCall is { } last ? $"Last call {Ago(last, now)}." : "No call yet."));

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

        if (server.Sessions.Count is 0)
        {
            _ = html.Append("<p class=\"muted\">It holds no session.</p>\n");
        }
        else
        {
            _ = html.Append("<ul class=\"sessions\">\n");

            foreach (var session in server.Sessions)
            {
                _ = html.Append("<li><strong>").Append(Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded"))
                    .Append("</strong> <span class=\"muted\">").Append(session.BrowserOpen ? "browser open" : "no browser open")
                    .Append("</span><br><code>").Append(Text(session.Directory)).Append("</code> ")
                    .Append(Button("open-session", "Open its folder", ("session", session.Id)));

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

        _ = html.Append("</li>\n");
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

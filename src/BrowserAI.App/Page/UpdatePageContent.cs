// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// The dashboard's update page, as HTML, from what holds the downloaded update.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the ready toast's <i>Install now</i> leads</b>, the maintainer's words
/// of 2026-10-08 verbatim: <i>"the install now button takes you to the browser
/// interface gui of the coordinator where it can better explain what the risks of
/// forcing the update now are together with an overview of who is still using
/// it."</i> And from H1 the same day: <i>"the coordinator can show what is holding up
/// the update clearly split by browsers, relays, etc..."</i>
/// </para>
/// <para>
/// <b>Three lists, each with its countdown</b>: windows on the person's screen, each marked
/// <i>close this window to let the update go ahead</i>, hidden browsers, and the
/// conversations, each with the reconnect its app will need afterwards (H1-T a). Then what
/// installing now does, and the button that does it. <i>Redrawn 2026-10-11 as A's update page
/// (40 a), in the mockups' words (42 a): a conversation, never an agent's connection.</i>
/// </para>
/// <para>
/// <b>Every countdown is a deadline in the page</b>, <c>data-ends-at</c> in
/// milliseconds since 1970, and the page's script counts it down each second. The
/// server sends a new state only when what holds the update has changed, which
/// <see cref="Signature"/> says.
/// </para>
/// <para>
/// A pure function of its arguments, like <see cref="PageContent"/>, and every
/// string that did not come from this file goes through <see cref="PageContent.Text"/>.
/// </para>
/// </remarks>
internal static class UpdatePageContent
{
    /// <summary>What installing now does, said beside the button, one sentence per line.</summary>
    /// <remarks>
    /// ⚠️ <b>The last line names the version that installs, since 2026-10-10</b>, round 2
    /// of the texts review, #118 (previously "BrowserAI starts again by itself once the new
    /// version is installed."): the version waiting can be older than the one installed,
    /// and the page says so above it (Q308 a). <i>Corrected 2026-10-11 (previously "Every
    /// browser session is closed cleanly, and each comes back where it was with
    /// browserai_resume.", "Every visible window closes, with what is open in it.", "Every
    /// agent's connection to BrowserAI ends. ..." and "BrowserAI starts again by itself once
    /// {version} is installed.")</i>, the mockups' words, 42 a.
    /// </remarks>
    /// <param name="version">The version the button installs.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> InstallNowEffects(string version) =>
    [
        "Every browser closes cleanly, windows included, with what is open in them. Each session comes back where it was when its agent resumes it.",
        "Every conversation loses BrowserAI. Claude Code in VS Code reconnects by itself; in a terminal, run /mcp there, choose BrowserAI, then Reconnect. A Codex conversation gets BrowserAI back only in a new conversation.",
        $"BrowserAI starts again by itself once {version} is installed, and a notification says so.",
    ];

    /// <summary>
    /// Says that reading what holds the update failed, and where the log that says why is.
    /// </summary>
    /// <remarks>
    /// <i>Added 2026-10-10 in place of "This BrowserAI does not report what holds an
    /// update", #66 and #73 of the texts review</i>: the page always has the update
    /// core, so a snapshot it does not have is a read that threw, which is logged.
    /// </remarks>
    /// <param name="html">Where to write.</param>
    /// <param name="facts">What does not change, the log's folder among it.</param>
    internal static void AppendUnread(StringBuilder html, PageFacts facts)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(facts);

        _ = html.Append("<p>BrowserAI could not read what holds an update. Its log, in <code>").Append(PageContent.Text(facts.LogDirectory)).Append("</code>, says why.</p>\n");
    }

    /// <summary>The update page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <param name="now">The time the page is rendered at.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(view);

        var html = new StringBuilder(PageContent.Crumbs(view.Tab, "Update"));

        switch (view.Holds)
        {
            case null:
                _ = html.Append(PageContent.Title("Update", null));
                PageContent.AppendNote(html, view.Note);
                AppendUnread(html, view.Facts);
                break;

            case { State: UpdateHoldState.Installing, Version: { } installing }:
                _ = html.Append(PageContent.Title($"Update {installing} is installing", "This page stops when BrowserAI closes, and BrowserAI starts again by itself."));
                PageContent.AppendNote(html, view.Note);
                break;

            case { State: UpdateHoldState.Held, Version: { } version } holds:
                _ = html.Append(PageContent.Title($"Update {version} is ready to install", "It installs by itself once nothing uses BrowserAI, so no agent is cut off in the middle of its work."));
                PageContent.AppendNote(html, view.Note);
                AppendHeld(html, view, holds, version, now);
                break;

            // #71, 2026-10-10: a background that is not installed holds nothing and says
            // so as the home does.
            case { } when view.Facts.InstallRoot is null:
                _ = html.Append(PageContent.Title("Update", PageContent.NotInstalledSentence));
                PageContent.AppendNote(html, view.Note);
                break;

            default:
                _ = html.Append(PageContent.Title("No update is waiting", $"BrowserAI {view.Facts.Version} is installed. {CheckedSentence(view.Updates, now)}".TrimEnd()));
                PageContent.AppendNote(html, view.Note);
                _ = html.Append(PageContent.Empty("checkCircle", "When an update waits", "This page shows what is still using BrowserAI, with a countdown for each, and a button to install it now."));
                break;
        }

        return html.ToString();
    }

    /// <summary>
    /// What a person would see change on the page, with the time left out: two
    /// snapshots with the same signature render the same apart from their
    /// countdowns, which the page's script counts down by itself.
    /// </summary>
    /// <param name="holds">What holds the update, or <see langword="null"/>.</param>
    /// <param name="now">The moment, which decides which relays still hold it.</param>
    /// <returns>The signature.</returns>
    public static string Signature(UpdateHoldSnapshot? holds, DateTimeOffset now)
    {
        if (holds is null)
        {
            return "unread";
        }

        var text = new StringBuilder()
            .Append(holds.State).Append('|').Append(holds.Version).Append('|').Append(holds.Older).Append('|').Append(holds.WaitAt(now).Wait).Append('|').Append(holds.WaitAt(now).Ends?.ToUnixTimeMilliseconds());

        foreach (var session in holds.HiddenSessions.Concat(holds.VisibleWindows))
        {
            _ = text.Append("|s:").Append(session.Directory).Append(';').Append(session.Purpose).Append(';').Append(session.ClosesAt?.ToUnixTimeMilliseconds());
        }

        foreach (var relay in holds.Relays)
        {
            _ = text.Append("|r:").Append(relay.Client).Append(';').Append(relay.ProjectFolder).Append(';').Append(relay.IdleAt.ToUnixTimeMilliseconds())
                .Append(';').Append(relay.CallInFlight).Append(';').Append(relay.Reconnect).Append(';').Append(relay.CallInFlight || relay.IdleAt > now)
                .Append(';').Append(relay.Label?.Shown()).Append(';').Append(relay.Window?.Key);
        }

        return text.ToString();
    }

    /// <summary>The reconnect an app will need, said to a person.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously "... BrowserAI cannot tell whether this client
    /// reconnects by itself.")</i>, 42 a: an app, never a client; the page's one wording is
    /// <see cref="PageWords.Reconnect"/>.
    /// </remarks>
    /// <param name="reconnect">What the app needs.</param>
    /// <returns>The sentence.</returns>
    public static string ReconnectSentence(RelayReconnect reconnect) => PageWords.Reconnect(reconnect);

    /// <summary>What the page says of a version older than the one installed, Q308 a.</summary>
    /// <param name="installed">The version installed now.</param>
    /// <returns>The sentences.</returns>
    public static string OlderSentence(string installed) =>
        $"It is older than the installed {installed}. Installing it goes back to the earlier version.";

    /// <summary>How the wait for a held update stands, in one sentence, as the home's card says it.</summary>
    /// <param name="wait">How the wait stands.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The HTML.</returns>
    public static string WaitSentence(UpdateWaitReading wait, DateTimeOffset now) => wait switch
    {
        { Wait: UpdateWait.Counting, Ends: { } ends } =>
            "It installs by itself in " + PageContent.Countdown(ends, now) + ", at about " + PageContent.Text(PageWords.Clock(ends)) + ", if nothing uses BrowserAI before then.",
        { Wait: UpdateWait.WindowNeverCloses } => PageContent.Text("A window set never to close by itself holds it. It installs once you close that window and nothing else uses BrowserAI."),
        { Wait: UpdateWait.SessionNeverCloses } => PageContent.Text("A hidden browser set never to close by itself holds it. It installs once an agent closes that session and nothing else uses BrowserAI."),
        { Wait: UpdateWait.CallRunning } => PageContent.Text("Every countdown has run out, and a call is still running: it installs once that call has finished."),
        { Wait: UpdateWait.Closing } => PageContent.Text("Every countdown has run out: it installs once the last browser has closed."),
        _ => PageContent.Text("Nothing uses BrowserAI now: it installs in a moment."),
    };

    /// <summary>When BrowserAI last checked for an update, or why it does not, as one sentence.</summary>
    /// <remarks><b>43 b, the second of the three facts</b>, which the update core reports.</remarks>
    /// <param name="watch">What the update core reports.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The sentence, or nothing where the core reported nothing.</returns>
    public static string CheckedSentence(UpdateWatch watch, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(watch);

        return watch.Checks switch
        {
            UpdateChecks.Checking => watch.LastCheck is { } at
                ? $"BrowserAI last checked {PageWords.CheckedWhere(watch)} at {PageWords.When(at, now)}."
                : $"BrowserAI has not checked {PageWords.CheckedWhere(watch)} yet.",
            UpdateChecks.NotInstalled => "This BrowserAI is not installed, so it does not check for updates.",
            UpdateChecks.SourceRefused => "BrowserAI does not check for updates, because the update source its install named was refused.",
            UpdateChecks.PreReleaseUrl => "This development build does not check for updates, because its install named a web address and it checks only a folder.",
            _ => string.Empty,
        };
    }

    /// <summary>Where the updates come from, as a value beside <i>Updates</i> says it.</summary>
    /// <param name="watch">What the update core reports.</param>
    /// <param name="facts">What does not change.</param>
    /// <returns>The words.</returns>
    public static string SourceWords(UpdateWatch watch, PageFacts facts)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(facts);

        return watch.Checks switch
        {
            UpdateChecks.Checking => PageWords.SourceOf(watch) is { } source ? "from " + source : "from its update source",
            UpdateChecks.NotInstalled => "none, because this BrowserAI is not installed",
            UpdateChecks.SourceRefused => "none, because the update source its install named was refused",
            UpdateChecks.PreReleaseUrl => "none, because a development build checks only a folder",
            _ => facts.InstallRoot is null ? "none, because this BrowserAI is not installed" : "not reported",
        };
    }

    /// <summary>A held update: the countdown, what holds it in three lists, and what installing now does.</summary>
    private static void AppendHeld(StringBuilder html, PageView view, UpdateHoldSnapshot holds, string version, DateTimeOffset now)
    {
        var wait = holds.WaitAt(now);

        if (holds.Older)
        {
            _ = html.Append("<p class=\"warning\">").Append(PageContent.Text(OlderSentence(view.Facts.Version))).Append("</p>\n");
        }

        var (clock, title, detail) = wait switch
        {
            { Wait: UpdateWait.Counting, Ends: { } ends } => (
                PageContent.Countdown(ends, now),
                "Installs by itself at about " + PageWords.Clock(ends),
                "if nothing below is used before then. Using anything below starts its countdown again."),
            { Wait: UpdateWait.WindowNeverCloses } => (PageIcons.Of("window"), "Waiting for a window", "A window set never to close by itself holds the update. It installs once you close that window and nothing else uses BrowserAI."),
            { Wait: UpdateWait.SessionNeverCloses } => (PageIcons.Of("hidden"), "Waiting for a session", "A hidden browser set never to close by itself holds the update. It installs once an agent closes that session and nothing else uses BrowserAI."),
            { Wait: UpdateWait.CallRunning } => (PageIcons.Of("clock"), "Waiting for a call", "Every countdown has run out, and a call is still running: it installs once that call has finished."),
            { Wait: UpdateWait.Closing } => (PageIcons.Of("clock"), "Closing the last browser", "Every countdown has run out: it installs once the last browser has closed."),
            _ => (PageIcons.Of("clock"), "Installing in a moment", "Nothing uses BrowserAI now: it installs in a moment."),
        };

        _ = html.Append("<div class=\"card hero\" id=\"hero\"><div class=\"clock\">").Append(clock).Append("</div>")
            .Append("<div class=\"hero-text\"><p><b>").Append(PageContent.Text(title)).Append("</b></p><p class=\"small\">").Append(PageContent.Text(detail)).Append("</p></div>")
            .Append("<div class=\"hero-act\">").Append(InstallButton("b-install-now-top", version))
            .Append("<span class=\"small\">Closes every browser and disconnects every conversation.</span></div></div>\n")
            .Append("<div class=\"grid\">\n");

        AppendSessions(html, "windows", "window", "Windows on your screen", "Each one closes by itself after its idle time, if nobody uses it, unless it is set never to close.", "No window is open.", holds.VisibleWindows, now, visible: true);
        AppendSessions(html, "hidden", "hidden", "Hidden browsers", "Each one closes by itself after its idle time, if no call uses it, unless it is set never to close.", "No hidden browser is open.", holds.HiddenSessions, now, visible: false);
        AppendConversations(html, holds.Relays, now);

        _ = html.Append("<section id=\"install-now\" class=\"card span-12\">").Append(PageContent.CardHead("bolt", "If you install it now"))
            .Append("<p>").Append(PageContent.Text("Installing now does not wait for any of the above:")).Append("</p>\n<ul class=\"effects\">\n");

        foreach (var effect in InstallNowEffects(version))
        {
            _ = html.Append("<li>").Append(PageContent.Text(effect)).Append("</li>\n");
        }

        _ = html.Append("</ul>\n<p>").Append(InstallButton("b-install-now-bottom", version)).Append("</p>\n</section>\n</div>\n");
    }

    private static string InstallButton(string id, string version) =>
        PageContent.Button(id, "install-now", $"Install BrowserAI {version} now", "btn danger", null, ("version", version));

    private static void AppendSessions(StringBuilder html, string id, string icon, string heading, string caption, string none, IReadOnlyList<HoldingSession> sessions, DateTimeOffset now, bool visible)
    {
        _ = html.Append("<section id=\"").Append(id).Append("\" class=\"card span-4\">").Append(PageContent.CardHead(icon, heading))
            .Append("<p class=\"caption\">").Append(PageContent.Text(caption)).Append("</p>\n");

        if (sessions.Count is 0)
        {
            _ = html.Append("<p class=\"small\">").Append(PageContent.Text(none)).Append("</p>\n</section>\n");
            return;
        }

        _ = html.Append("<div class=\"hold-list\">\n");

        foreach (var session in sessions)
        {
            var side = session.ClosesAt is { } closes
                ? closes > now ? "<span class=\"chip " + (visible ? "window" : "hidden") + "\">" + PageContent.Countdown(closes, now) + "</span>" : "<span class=\"chip\">Closing now</span>"
                : "<span class=\"chip " + (visible ? "window" : "hidden") + "\">Never closes</span>";

            _ = html.Append("<div class=\"hold-item\"><span class=\"sess-ic ").Append(visible ? "w" : "h").Append("\">").Append(PageIcons.Of(icon)).Append("</span><div>")
                .Append("<b>").Append(PageContent.Text(PageWords.NameOf(session.Directory))).Append("</b>")
                .Append("<p class=\"sub\">").Append(PageContent.Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded")).Append("</p>")
                .Append("<p class=\"sub\">").Append(PageContent.Text(visible
                    ? session.ClosesAt is null ? "Set never to close by itself. Close this window to let the update go ahead." : "Close this window to let the update go ahead, or it closes by itself if nobody types or clicks in it and no call names it."
                    : session.ClosesAt is null ? "Set never to close: an agent has to close it." : "Closes by itself if no call names it."))
                .Append("</p></div>").Append(side).Append("</div>\n");
        }

        _ = html.Append("</div>\n</section>\n");
    }

    private static void AppendConversations(StringBuilder html, IReadOnlyList<HoldingRelay> relays, DateTimeOffset now)
    {
        _ = html.Append("<section id=\"conversations\" class=\"card span-4\">").Append(PageContent.CardHead("chat", "Conversations"))
            .Append("<p class=\"caption\">").Append(PageContent.Text("Each holds the update for ten minutes after its app last sent BrowserAI anything but a ping. Every conversation loses BrowserAI when the update installs, whether it holds the update or not.")).Append("</p>\n");

        if (relays.Count is 0)
        {
            _ = html.Append("<p class=\"small\">No conversation is connected.</p>\n</section>\n");
            return;
        }

        _ = html.Append("<div class=\"hold-list\">\n");

        // 1.5 a, 2026-10-10: a VS Code window's tabs are listed together, under the
        // window, where the first of them would stand.
        foreach (var (window, members) in PageContent.ByWindow(relays, relay => relay.Window))
        {
            if (window is not null)
            {
                _ = html.Append("<p class=\"group-label\">").Append(PageIcons.Of("code")).Append(PageContent.Text(window.Label())).Append("</p>\n");
            }

            foreach (var relay in members)
            {
                AppendConversation(html, relay, now);
            }
        }

        _ = html.Append("</div>\n</section>\n");
    }

    private static void AppendConversation(StringBuilder html, HoldingRelay relay, DateTimeOffset now)
    {
        // The app as the page names it (42 a), from the client's own name, which the update
        // core passes beside the protocol's words it keeps for its log and the toast.
        var app = PageWords.AppWithVersion(relay.ClientName, relay.ClientVersion);
        var name = relay.Label?.Shown() ?? PageWords.ConversationOf(null, relay.ClientName, relay.ProjectFolder);
        var side = relay.CallInFlight
            ? "<span class=\"chip live\"><span class=\"pulse\" aria-hidden=\"true\"></span>Working</span>"
            : relay.IdleAt > now ? "<span class=\"chip\">" + PageContent.Countdown(relay.IdleAt, now) + "</span>" : "<span class=\"chip\">Idle</span>";
        var holding = relay.CallInFlight ? "A call is running now, so it holds the update."
            : relay.IdleAt > now ? "Holds the update until its countdown runs out, if its app sends nothing but pings."
            : "Idle: it no longer holds the update.";

        _ = html.Append("<div class=\"hold-item\"><span class=\"sess-ic\">").Append(PageIcons.Of(relay.Window is not null ? "code" : relay.Reconnect is RelayReconnect.McpReconnect ? "terminal" : "chat")).Append("</span><div>")
            .Append("<b>").Append(PageContent.Text(name)).Append("</b>")
            .Append("<p class=\"sub\">").Append(PageContent.Text(app)).Append("</p>")
            .Append("<p class=\"sub\">").Append(PageContent.Text(holding)).Append("</p>")
            .Append("<p class=\"sub").Append(relay.Reconnect is RelayReconnect.None ? string.Empty : " warning").Append("\">").Append(PageContent.Text(ReconnectSentence(relay.Reconnect))).Append("</p>")
            .Append("</div>").Append(side).Append("</div>\n");
    }
}

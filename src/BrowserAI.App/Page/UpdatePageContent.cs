// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
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
/// <b>Three lists, each with its countdown</b>: hidden browser sessions, visible
/// windows, each marked <i>close this to let the update proceed</i>, and the agents'
/// connections, each with the reconnect its client will need afterwards (H1-T a).
/// Then what installing now does, and the button that does it. <i>Added 2026-10-10:</i>
/// each connection is named by its conversation the way the person sees it called, and
/// a VS Code window's tabs are listed together under the window (1.2 a, 1.5 a).
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
    public static IReadOnlyList<string> InstallNowEffects { get; } =
    [
        "Every browser session is closed cleanly, and each comes back where it was with browserai_resume.",
        "Every visible window closes, with what is open in it.",
        "Every agent's connection to BrowserAI ends. Claude Code in VS Code reconnects by itself. Claude Code in a terminal needs /mcp, then BrowserAI, then Reconnect. A Codex conversation gets BrowserAI back only in a new conversation.",
        "BrowserAI starts again by itself once the new version is installed.",
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

        var html = new StringBuilder("<h1>Update</h1>\n");

        PageContent.AppendNote(html, view.Note);

        switch (view.Holds)
        {
            case null:
                AppendUnread(html, view.Facts);
                break;

            case { State: UpdateHoldState.Installing, Version: { } installing }:
                _ = html.Append("<p>")
                    .Append(PageContent.Text($"BrowserAI {installing} is installing now. This page stops when BrowserAI closes, and BrowserAI starts again by itself."))
                    .Append("</p>\n");
                break;

            case { State: UpdateHoldState.Held, Version: { } version } holds:
                AppendHeld(html, holds, version, view.Facts.Version, now);
                break;

            // #71, 2026-10-10: a background that is not installed holds nothing and says
            // so as the status page does. The installed sentence ends at what is
            // installed: since 2026-10-08 the status page checks for nothing itself, and
            // whether the background checks is not something a snapshot says.
            case { } when view.Facts.InstallRoot is null:
                _ = html.Append("<p>").Append(PageContent.Text(PageContent.NotInstalledSentence)).Append("</p>\n");
                break;

            default:
                _ = html.Append("<p>")
                    .Append(PageContent.Text($"No downloaded update is waiting. BrowserAI {view.Facts.Version} is installed."))
                    .Append("</p>\n");
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

    /// <summary>The reconnect a client will need, said to a person.</summary>
    /// <param name="reconnect">What the client needs.</param>
    /// <returns>The sentence.</returns>
    public static string ReconnectSentence(RelayReconnect reconnect) => reconnect switch
    {
        RelayReconnect.None => "After the update it reconnects by itself.",
        RelayReconnect.McpReconnect => "After the update, run /mcp in that terminal, choose BrowserAI, then Reconnect.",
        RelayReconnect.NewConversation => "After the update, BrowserAI is back only in a new conversation.",
        _ => "After the update it may need BrowserAI reconnected: BrowserAI cannot tell whether this client reconnects by itself.",
    };

    /// <summary>The first sentence about a downloaded update that waits, the same on the status page and on this one.</summary>
    /// <param name="version">The version waiting.</param>
    /// <returns>The sentence.</returns>
    public static string HeldSentence(string version) =>
        $"BrowserAI {version} is downloaded and ready to install. It installs by itself once BrowserAI has been idle.";

    /// <summary>
    /// The status page's update section where the page checks for nothing itself and
    /// the background reports what holds an update: what waits, and the way to this page.
    /// </summary>
    /// <remarks>
    /// The background builds its page with no feed of its own since 2026-10-08, and the
    /// page's own check is deleted (2026-10-10, the maintainer's "9 a"). The section says
    /// what the background holds; it claims nothing about checks, which a snapshot does
    /// not carry.
    /// </remarks>
    /// <param name="html">Where to write.</param>
    /// <param name="holds">What the background reports.</param>
    /// <param name="installed">The version installed now, which an older one is said to be older than.</param>
    /// <param name="tab">The tab the link keeps.</param>
    internal static void AppendStatusSection(StringBuilder html, UpdateHoldSnapshot holds, string installed, int tab)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentNullException.ThrowIfNull(installed);

        _ = holds switch
        {
            { State: UpdateHoldState.Held, Version: { } version } => AppendOlder(html.Append("<p>").Append(PageContent.Text(HeldSentence(version))).Append("</p>\n"), holds, installed)
                .Append("<p><a href=\"").Append(PageNames.RouteOf(PageKind.Update)).Append("?tab=").Append(tab.ToString(CultureInfo.InvariantCulture))
                .Append("\">See what holds it, or install it now</a></p>\n"),
            { State: UpdateHoldState.Installing, Version: { } installing } => html.Append("<p>").Append(PageContent.Text($"BrowserAI {installing} is installing now.")).Append("</p>\n"),
            _ => html.Append("<p>No downloaded update is waiting.</p>\n"),
        };
    }

    /// <summary>What the page says of a version older than the one installed, Q308 a.</summary>
    /// <param name="installed">The version installed now.</param>
    /// <returns>The sentences.</returns>
    public static string OlderSentence(string installed) =>
        $"It is older than the installed {installed}. Installing it goes back to the earlier version.";

    /// <summary>
    /// Says that the version waiting is older than the one installed, where it is: Q308 a,
    /// the maintainer's words of 2026-10-03 verbatim, <i>"Q308 a"</i>, built again
    /// 2026-10-10 from what the update core reads.
    /// </summary>
    /// <param name="html">Where to write.</param>
    /// <param name="holds">What the background reports.</param>
    /// <param name="installed">The version installed now.</param>
    /// <returns>The builder, for the next append.</returns>
    private static StringBuilder AppendOlder(StringBuilder html, UpdateHoldSnapshot holds, string installed) =>
        holds.Older ? html.Append("<p class=\"warning\">").Append(PageContent.Text(OlderSentence(installed))).Append("</p>\n") : html;

    private static void AppendHeld(StringBuilder html, UpdateHoldSnapshot holds, string version, string installed, DateTimeOffset now)
    {
        var wait = holds.WaitAt(now);

        _ = AppendOlder(html.Append("<p>").Append(PageContent.Text(HeldSentence(version))).Append("</p>\n"), holds, installed)
            .Append("<p class=\"wait\">");

        _ = wait switch
        {
            { Wait: UpdateWait.Counting, Ends: { } ends } => html.Append("It installs in ").Append(Countdown(ends, now))
                .Append(", at about ").Append(PageContent.Text(ends.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)))
                .Append(", if nothing uses BrowserAI before then."),
            { Wait: UpdateWait.WindowNeverCloses } => html.Append(PageContent.Text("A visible window set never to close holds it: it installs once you close that window and nothing else uses BrowserAI.")),
            { Wait: UpdateWait.SessionNeverCloses } => html.Append(PageContent.Text("A hidden session set never to close holds it: it installs once an agent closes that session and nothing else uses BrowserAI.")),
            { Wait: UpdateWait.CallRunning } => html.Append(PageContent.Text("Every countdown has run out, and a call is still running: it installs once that call has finished.")),
            { Wait: UpdateWait.Closing } => html.Append(PageContent.Text("Every countdown has run out: it installs once the last browser has closed.")),
            _ => html.Append(PageContent.Text("Nothing uses BrowserAI now: it installs in a moment.")),
        };

        _ = html.Append("</p>\n");

        AppendSessions(html, "hidden", "Hidden browser sessions", "No hidden browser session is open.", holds.HiddenSessions, now, visible: false);
        AppendSessions(html, "windows", "Visible windows", "No visible window is open.", holds.VisibleWindows, now, visible: true);
        AppendRelays(html, holds.Relays, now);

        _ = html.Append("<section id=\"install-now\"><h2>Install now</h2>\n<p>")
            .Append(PageContent.Text("Installing now does not wait for any of the above:")).Append("</p>\n<ul>\n");

        foreach (var effect in InstallNowEffects)
        {
            _ = html.Append("<li>").Append(PageContent.Text(effect)).Append("</li>\n");
        }

        _ = html.Append("</ul>\n<p><button type=\"button\" data-action=\"install-now\" data-version=\"").Append(PageContent.Text(version))
            .Append("\">").Append(PageContent.Text($"Install BrowserAI {version} now")).Append("</button></p>\n</section>\n");
    }

    private static void AppendSessions(StringBuilder html, string id, string heading, string none, IReadOnlyList<HoldingSession> sessions, DateTimeOffset now, bool visible)
    {
        _ = html.Append("<section id=\"").Append(id).Append("\"><h2>").Append(PageContent.Text(heading)).Append("</h2>\n");

        if (sessions.Count is 0)
        {
            _ = html.Append("<p class=\"muted\">").Append(PageContent.Text(none)).Append("</p>\n</section>\n");
            return;
        }

        _ = html.Append("<ul class=\"holders\">\n");

        foreach (var session in sessions)
        {
            _ = html.Append("<li><strong>").Append(PageContent.Text(session.Purpose is { Length: > 0 } purpose ? purpose : "No purpose recorded"))
                .Append("</strong><br><code>").Append(PageContent.Text(session.Directory)).Append("</code><br>");

            if (visible)
            {
                _ = html.Append("<span class=\"warning\">Close this to let the update proceed.</span> ");
            }

            _ = session.ClosesAt is { } closes
                ? closes > now
                    ? html.Append(visible ? "Closes by itself in " : "Closes in ").Append(Countdown(closes, now)).Append(visible ? " if no call names it and nobody types or clicks in it." : " if no call names it.")
                    : html.Append("Closing now.")
                : html.Append(PageContent.Text(visible ? "Set never to close by itself." : "Set never to close: an agent has to close it."));

            _ = html.Append("</li>\n");
        }

        _ = html.Append("</ul>\n</section>\n");
    }

    private static void AppendRelays(StringBuilder html, IReadOnlyList<HoldingRelay> relays, DateTimeOffset now)
    {
        _ = html.Append("<section id=\"agents\"><h2>Agents</h2>\n");

        if (relays.Count is 0)
        {
            _ = html.Append("<p class=\"muted\">No agent is connected to BrowserAI.</p>\n</section>\n");
            return;
        }

        _ = html.Append("<p>").Append(PageContent.Text("An agent holds the update for ten minutes after its client last sent BrowserAI anything but a ping. Every connection ends when the update installs, whether it holds the update or not."))
            .Append("</p>\n<ul class=\"holders\">\n");

        // 1.5 a, 2026-10-10: a VS Code window's tabs are listed together, under the
        // window, where the first of them would stand.
        foreach (var (window, members) in PageContent.ByWindow(relays, relay => relay.Window))
        {
            if (window is null)
            {
                AppendRelay(html, members[0], now);
                continue;
            }

            _ = html.Append("<li class=\"window\"><p><strong>").Append(PageContent.Text(window.Label())).Append("</strong></p>\n<ul class=\"holders\">\n");

            foreach (var relay in members)
            {
                AppendRelay(html, relay, now);
            }

            _ = html.Append("</ul></li>\n");
        }

        _ = html.Append("</ul>\n</section>\n");
    }

    private static void AppendRelay(StringBuilder html, HoldingRelay relay, DateTimeOffset now)
    {
        _ = html.Append("<li>");

        // 1.2 a, 2026-10-10: the conversation first, as the person sees it called, and
        // its client after it.
        if (relay.Label is { } label)
        {
            _ = html.Append("<strong>").Append(PageContent.Text(label.Shown())).Append("</strong>, ").Append(PageContent.Text(relay.Client));
        }
        else
        {
            _ = html.Append("<strong>").Append(PageContent.Text(relay.Client)).Append("</strong>");
        }

        if (relay.ProjectFolder is { Length: > 0 } folder)
        {
            _ = html.Append(" in <code>").Append(PageContent.Text(folder)).Append("</code>");
        }

        _ = html.Append("<br>");

        _ = relay.CallInFlight
            ? html.Append("A call is running now, so it holds the update.")
            : relay.IdleAt > now
                ? html.Append("Holds the update for ").Append(Countdown(relay.IdleAt, now)).Append(" more if its client sends nothing but pings.")
                : html.Append("Idle: it no longer holds the update.");

        _ = html.Append("<br>").Append(relay.Reconnect is RelayReconnect.None
            ? PageContent.Text(ReconnectSentence(relay.Reconnect))
            : "<span class=\"warning\">" + PageContent.Text(ReconnectSentence(relay.Reconnect)) + "</span>").Append("</li>\n");
    }

    /// <summary>A countdown the page's script keeps live: the deadline in the element, the time left as its text.</summary>
    private static string Countdown(DateTimeOffset ends, DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"<span class=\"countdown\" data-ends-at=\"{ends.ToUnixTimeMilliseconds()}\">{UpdateToastContent.Remaining(ends - now)}</span>");
}

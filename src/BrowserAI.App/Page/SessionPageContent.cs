// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;

namespace BrowserAI.App.Page;

/// <summary>
/// One session's page: its browser, the conversation that uses it, its folder and its size,
/// reached from a session's card by the session's opaque name.
/// </summary>
/// <remarks>
/// <para>
/// <b>40 a, F</b>: every page behind a card begins with the way back. <b>41 a</b>: the page
/// deletes nothing; it says the folder's size and opens the folder, and only the agent that
/// made a session deletes it (<i>Who deletes a session</i>, in <c>DECISIONS.md</c>).
/// </para>
/// <para>
/// <b>A session that is not open now is shown from the last measurement</b>, its folder and
/// its size, since a person reaches it from the storage page too. A name neither the
/// background nor the measurement knows says so.
/// </para>
/// </remarks>
internal static class SessionPageContent
{
    /// <summary>What a session's page is called: its folder's own name, or that there is no such session.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The words.</returns>
    public static string NameOf(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var board = PageBoard.Of(view.Sessions);

        return board.SessionById(view.Subject) is { } session ? PageWords.NameOf(session.Directory)
            : view.Subject is { } id && view.Folders?.ById(id) is { } folder ? PageWords.NameOf(folder.Directory)
            : "No such session";
    }

    /// <summary>What a session's folder weighs, as the last measurement found it.</summary>
    /// <param name="view">What is true now.</param>
    /// <param name="id">The session's opaque name.</param>
    /// <returns>The words.</returns>
    public static string SizeWords(PageView view, string id)
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

    /// <summary>One session's page.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var board = PageBoard.Of(view.Sessions);
        var name = NameOf(view);
        var html = new StringBuilder(PageContent.Crumbs(view.Tab, name, ("Conversations and browsers", PageKind.Sessions)));

        if (board.SessionById(view.Subject) is { } session)
        {
            var user = board.UsedBy(session);

            _ = html.Append(PageContent.Title(
                    name,
                    session.Purpose is { Length: > 0 } purpose ? $"A browser session. Its agent wrote that it is for {purpose}." : "A browser session. Its agent recorded no purpose."))
                .Append("<div class=\"two\"><div>\n")
                .Append("<div class=\"box\"><h2>Browser</h2><p>").Append(PageParts.Line(session, user?.Working ?? false, view.Now)).Append("</p></div>\n")
                .Append("<div class=\"box\"><h2>Used by</h2>");

            _ = user is { } conversation
                ? html.Append("<div class=\"app-row\">").Append(PageContent.Badge(conversation.ClientName)).Append("<div><b>").Append(PageContent.Text(conversation.Name)).Append("</b><span class=\"small\">")
                    .Append(PageContent.Text(conversation.Place + ", " + PageWords.NameOf(conversation.Relay.Description.WorkingDirectory))).Append("</span></div></div>")
                : html.Append("<p>").Append(session.Kept
                    ? "No conversation. The conversation that used it has ended, and BrowserAI keeps the session for the next one that names it."
                    : "No conversation is connected to it now.").Append("</p>");

            _ = html.Append("</div>\n");

            if (session.Traces.Count > 0)
            {
                _ = html.Append("<div class=\"box\"><h2>Traces</h2><p class=\"small\">Recordings Playwright can replay, in the session's output folder.</p><ul class=\"plain\">");

                foreach (var trace in session.Traces)
                {
                    _ = html.Append("<li><code>").Append(PageContent.Text(trace.Name)).Append("</code></li>");
                }

                _ = html.Append("</ul></div>\n");
            }

            _ = html.Append("</div><div>\n").Append(FolderBox(view, session.Id, session.Directory));
        }
        else if (view.Subject is { } id && view.Folders?.ById(id) is { } folder)
        {
            _ = html.Append(PageContent.Title(name, "A session folder that is not open now."))
                .Append("<div class=\"two\"><div>\n")
                .Append("<div class=\"box\"><h2>Browser</h2><p>Not open. A conversation that names this session opens it again.</p></div>\n")
                .Append("</div><div>\n").Append(FolderBox(view, folder.Id, folder.Directory));
        }
        else
        {
            return html.Append(PageContent.Title(name, "BrowserAI holds no session by that name now, and its last measurement of the session folders found none."))
                .Append("<p>").Append(PageContent.LinkButton("l-back-sessions", PageContent.Route(PageKind.Sessions, view.Tab), "See the conversations and browsers")).Append("</p>\n")
                .ToString();
        }

        return html.Append(PageContent.Notice("good", "Only its agent deletes it", "BrowserAI never deletes a session folder on its own. To remove this one yourself, open its folder."))
            .Append("</div></div>\n").ToString();
    }

    private static string FolderBox(PageView view, string id, string directory) =>
        "<div class=\"box\"><h2>Folder</h2><div class=\"path\"><code>" + PageContent.Text(directory) + "</code>"
        + PageContent.Button("b-open-session-" + id, "open-session", "Open", "btn", "folder", ("session", id)) + "</div>"
        + "<div class=\"kv\"><span>Size</span><b>" + PageContent.Text(SizeWords(view, id)) + "</b></div></div>\n";
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;

namespace BrowserAI.App.Page;

/// <summary>
/// The page behind the home's <i>Storage</i> card: every session folder BrowserAI knows of,
/// largest first, with its size and whether it is open now.
/// </summary>
/// <remarks>
/// <para>
/// <b>43 b, the first of the three facts the page may show</b>: each session folder's size
/// and the session folders not open now. The background finds them in its session index and
/// in what it holds, and weighs each; the page asks for a measurement when it is drawn and
/// when the sessions held change, and says when the one it shows was taken.
/// </para>
/// <para>
/// <b>41 a: the page deletes nothing.</b> Each folder has an <i>Open folder</i> button, and
/// the page says that only the agent that made a session deletes it.
/// </para>
/// </remarks>
internal static class StorageContent
{
    /// <summary>The storage page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var html = new StringBuilder()
            .Append(PageContent.Crumbs(view.Tab, "Storage"))
            .Append(PageContent.Title(
                "Storage",
                "BrowserAI keeps a folder for each session, wherever its agent put it. A folder stays until the agent that made it deletes it; BrowserAI never deletes one on its own. To remove one yourself, open its folder."));

        PageContent.AppendNote(html, view.Note);

        if (view.Folders is not { } measured)
        {
            return html.Append("<p class=\"small\">Measuring the session folders.</p>\n").ToString();
        }

        _ = html.Append("<p class=\"small read-at\">")
            .Append(PageContent.Text(view.Measuring ? "Measuring again." : $"Measured at {PageWords.ClockToTheSecond(measured.MeasuredAt)}."))
            .Append(' ').Append(PageContent.Button("b-measure-sessions", "measure-sessions", "Measure again", "btn quiet", "refresh")).Append("</p>\n");

        if (measured.Folders.Count is 0)
        {
            return html.Append(PageContent.Empty("folder", "No session folder yet", "Each time an agent starts a browser session, it names a folder for it. The folders are listed here with their size."))
                .ToString();
        }

        var open = PageBoard.Of(view.Sessions).All.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var openFolders = measured.Folders.Where(folder => open.Contains(folder.Id)).ToList();
        var closedFolders = measured.Folders.Where(folder => !open.Contains(folder.Id)).ToList();

        _ = html.Append("<div class=\"grid\">")
            .Append(Summary("folder", "Session folders", measured.Folders.Sum(folder => folder.Bytes ?? 0), PageWords.Count(measured.Folders.Count, "folder, each where its agent put it", "folders, each where its agent put it")))
            .Append(Summary("hidden", "Open now", openFolders.Sum(folder => folder.Bytes ?? 0), PageWords.Count(openFolders.Count, "session open now", "sessions open now")))
            .Append(Summary("closed", "Not open now", closedFolders.Sum(folder => folder.Bytes ?? 0), PageWords.Count(closedFolders.Count, "session not open now", "sessions not open now")))
            .Append("</div>\n");

        _ = html.Append("<article class=\"card spaced\">").Append(PageContent.CardHead("list", "Every session folder"))
            .Append("<p class=\"caption\">Largest first. A folder can hold a saved login or what was typed into a page; open it to see.</p>\n")
            .Append("<table class=\"table\"><thead><tr><th scope=\"col\">Session</th><th scope=\"col\">Browser</th><th scope=\"col\" class=\"num\">Size</th><th scope=\"col\"><span class=\"sr\">Folder</span></th></tr></thead><tbody>\n");

        foreach (var folder in measured.Folders)
        {
            var isOpen = open.Contains(folder.Id);

            _ = html.Append("<tr><td><a id=\"l-folder-").Append(PageContent.Text(folder.Id)).Append("\" href=\"").Append(PageContent.SessionRoute(folder.Id, view.Tab)).Append("\"><b>")
                .Append(PageContent.Text(PageWords.NameOf(folder.Directory))).Append("</b></a><br><code class=\"small\">").Append(PageContent.Text(folder.Directory)).Append("</code></td>")
                .Append("<td>").Append(isOpen ? "<span class=\"chip hidden\">Open now</span>" : "<span class=\"small\">Not open now</span>").Append("</td>")
                .Append("<td class=\"num\">").Append(PageContent.Text(folder.Bytes is { } bytes ? PageWords.Size(bytes) : "Could not be measured")).Append("</td>")
                .Append("<td>").Append(PageContent.Button("b-open-session-" + folder.Id, "open-session", "Open folder", "btn quiet", "folder", ("session", folder.Id))).Append("</td></tr>\n");
        }

        return html.Append("</tbody></table></article>\n").ToString();
    }

    private static string Summary(string icon, string title, long bytes, string caption) =>
        "<article class=\"card span-4\">" + PageContent.CardHead(icon, title)
        + "<p class=\"big\">" + PageContent.Text(PageWords.Size(bytes)) + "</p><p class=\"small\">" + PageContent.Text(caption) + "</p></article>";
}

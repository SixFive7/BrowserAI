// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// The page behind the home's <i>This install</i> card: where BrowserAI lives on this PC, which
/// version runs and since when, where its updates come from, and what a person troubleshooting
/// needs, folded.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status page's <i>Where things are</i>, moved behind its card</b> (40 a), with two of
/// the three facts 43 b lets the page show: when the background started and how, and where the
/// updates come from and when BrowserAI last checked.
/// </para>
/// <para>
/// <b>45 a</b>: the command Claude Code and Codex start, the background's process and each
/// connected app's are folded under <i>Details for troubleshooting</i>.
/// </para>
/// </remarks>
internal static class InstallContent
{
    /// <summary>Since when the background runs and how it started, as one line: <i>since 08:12, started at sign-in</i>.</summary>
    /// <param name="facts">What does not change.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The words, or <see langword="null"/> where nothing says when it started.</returns>
    public static string? RunningSince(PageFacts facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.StartedAt is not { } started)
        {
            return null;
        }

        var since = "since " + PageWords.When(started, now);

        return PageWords.StartedHow(facts.StartedHow) is { } how ? $"{since}, started {how}" : since;
    }

    /// <summary>The install page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var facts = view.Facts;
        var html = new StringBuilder()
            .Append(PageContent.Crumbs(view.Tab, "This install"))
            .Append(PageContent.Title("This install", "Where BrowserAI lives on this PC, and what Claude Code and Codex start to reach it."));

        PageContent.AppendNote(html, view.Note);

        _ = html.Append("<div class=\"two\"><div>\n");

        _ = facts.InstallRoot is { Length: > 0 } install
            ? html.Append(Path("Installed in", install, "install"))
            : html.Append("<div class=\"box\"><h2>Installed in</h2><p>This BrowserAI is not installed, so there is no install folder to show.</p></div>\n");

        _ = html.Append(Path("Data: browsers, the session index and logs", facts.DataRoot, "data"))
            .Append(Path("Logs", facts.LogDirectory, "logs"))
            .Append("</div><div>\n")
            .Append("<div class=\"box\"><h2>BrowserAI</h2><dl class=\"kvs\">")
            .Append("<dt>Version</dt><dd>").Append(PageContent.Text(facts.Version)).Append(", <a id=\"l-install-changelog\" href=\"").Append(PageContent.Route(PageKind.Changelog, view.Tab)).Append("\">what is new</a></dd>");

        if (RunningSince(facts, view.Now) is { } running)
        {
            _ = html.Append("<dt>Running</dt><dd>").Append(PageContent.Text(running)).Append("</dd>");
        }

        _ = html.Append("<dt>Updates</dt><dd>").Append(PageContent.Text(UpdatePageContent.SourceWords(view.Updates, facts))).Append("</dd>");

        if (view.Updates.Source is UpdateSourceKind.Folder or UpdateSourceKind.Url && view.Updates.Location is { Length: > 0 } location)
        {
            _ = html.Append("<dt>Update source</dt><dd><code>").Append(PageContent.Text(location)).Append("</code></dd>");
        }

        if (view.Updates.LastCheck is { } checkedAt)
        {
            _ = html.Append("<dt>Last check</dt><dd>").Append(PageContent.Text(PageWords.When(checkedAt, view.Now))).Append("</dd>");
        }

        _ = html.Append("</dl></div>\n");

        if (view.Updates.Refusal is { Length: > 0 } refusal)
        {
            _ = html.Append("<div class=\"box\"><h2>Why there are no updates</h2><p>").Append(PageContent.Text(UpdatePageContent.CheckedSentence(view.Updates, view.Now))).Append("</p>");
            PageContent.AppendDetails(html, "d-refusal", refusal);
            _ = html.Append("</div>\n");
        }

        return html.Append(Trouble(view)).Append("</div></div>\n").ToString();
    }

    private static string Path(string label, string directory, string folder) =>
        "<div class=\"box\"><h2>" + PageContent.Text(label) + "</h2><div class=\"path\"><code>" + PageContent.Text(directory) + "</code>"
        + PageContent.Button("b-open-folder-" + folder, "open-folder", "Open", "btn", "folder", ("folder", folder)) + "</div></div>\n";

    /// <summary>What Claude Code and Codex start, and the processes behind the page, folded (45 a).</summary>
    private static string Trouble(PageView view)
    {
        var facts = view.Facts;
        var lines = new List<string>();

        if (facts.ServerCommand is { Length: > 0 } server)
        {
            lines.Add("Claude Code and Codex start: " + PageContent.CommandLine(server, facts.ServerArguments));
            lines.Add("Started with no argument, the same file opens this page.");
        }
        else if (facts.ServerRefusal is { Length: > 0 } refusal)
        {
            lines.Add(refusal);
        }

        var board = PageBoard.Of(view.Sessions);

        if (board.Host is { } host)
        {
            lines.Add("BrowserAI's background: process " + host.Description.ProcessId.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var conversation in board.Conversations)
        {
            var description = conversation.Relay.Description;

            lines.Add($"{conversation.Name}: {PageWords.AppWithVersion(conversation.ClientName, description.Client?.Version)} through BrowserAI's process {description.ProcessId.ToString(CultureInfo.InvariantCulture)}, in {description.WorkingDirectory}");
        }

        return lines.Count is 0
            ? string.Empty
            : "<div class=\"box\"><details class=\"trouble\" id=\"d-trouble-install\"><summary id=\"d-trouble-install-s\">Details for troubleshooting</summary><pre>"
                + PageContent.Lines(string.Join('\n', lines)) + "</pre></details></div>\n";
    }
}

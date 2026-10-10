// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.RegularExpressions;

namespace BrowserAI.App.Page;

/// <summary>One version's section of the changelog shipped in the build.</summary>
/// <param name="Version">The version the section is for, or <see langword="null"/> for the changes not yet released.</param>
/// <param name="Date">The date its heading gives, or <see langword="null"/>.</param>
/// <param name="Markdown">The section's text, without its heading.</param>
internal sealed record ChangelogSection(string? Version, string? Date, string Markdown);

/// <summary>
/// The dashboard's changelog page: the section of <c>CHANGELOG.md</c> shipped inside
/// the build for the version that is installed, rendered readably.
/// </summary>
/// <remarks>
/// <para>
/// <b>The installed toast's <i>Changelog</i> leads here</b>, decided 2026-10-08 (T):
/// a page from the changelog shipped in the build, so it works for development
/// builds too, which have no release page.
/// </para>
/// <para>
/// <b>A release reads its own section; any other build reads the unreleased
/// one.</b> A release is a version a section heading names, <c>## [1.2.0] - date</c>;
/// a development build's version carries a pre-release suffix no heading has, and
/// what it has over the last release is exactly what <c>## [Unreleased]</c> lists.
/// </para>
/// <para>
/// <b>Each entry is folded under its headline</b>, the icon and the bold first
/// sentence every entry opens with (the changelog's own rule), the way the release
/// body folds them. Only the subset of Markdown the changelog uses is read:
/// paragraphs, the entry list, bold, italics, code and links; a link to a file in
/// the repository is shown as its text, because the page has no copy of the file.
/// </para>
/// </remarks>
internal static partial class ChangelogPageContent
{
    /// <summary>The heading of the changes not yet released.</summary>
    public const string Unreleased = "Unreleased";

    /// <summary>What a code span is held out as while the rest of a line is read: letters only, which no entry writes.</summary>
    private const string SpanToken = "QzxcodespanzxQ";

    /// <summary>The section a version reads: its own when a heading names it, and otherwise the unreleased one.</summary>
    /// <param name="changelog">The whole changelog.</param>
    /// <param name="version">The installed version; build metadata after <c>+</c> is ignored.</param>
    /// <returns>The section, or <see langword="null"/> when the changelog has neither.</returns>
    public static ChangelogSection? SectionFor(string changelog, string version)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        ArgumentNullException.ThrowIfNull(version);

        var plain = version.Split('+')[0];
        ChangelogSection? unreleased = null;

        foreach (var (heading, body) in Sections(changelog))
        {
            if (VersionHeading().Match(heading) is { Success: true } match)
            {
                var named = match.Groups["version"].Value;

                if (string.Equals(named, plain, StringComparison.Ordinal))
                {
                    return new ChangelogSection(named, match.Groups["date"].Success ? match.Groups["date"].Value : null, body);
                }

                if (string.Equals(named, Unreleased, StringComparison.OrdinalIgnoreCase))
                {
                    unreleased = new ChangelogSection(null, null, body);
                }
            }
        }

        return unreleased;
    }

    /// <summary>The changelog page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var html = new StringBuilder();
        var installed = view.Facts.Version;
        var section = view.Changelog;

        switch (section)
        {
            case null:
                _ = html.Append("<h1>Changelog</h1>\n<p>")
                    .Append(PageContent.Text($"This build of BrowserAI, {installed}, carries no changelog section for its version."))
                    .Append("</p>\n");
                return html.ToString();

            case { Version: { } version } released:
                _ = html.Append("<h1>").Append(PageContent.Text($"What changed in BrowserAI {version}")).Append("</h1>\n");

                if (released.Date is { Length: > 0 } date)
                {
                    _ = html.Append("<p class=\"muted\">").Append(PageContent.Text($"Released {date}.")).Append("</p>\n");
                }

                break;

            default:
                _ = html.Append("<h1>").Append(PageContent.Text("What changed since the last release")).Append("</h1>\n<p>")
                    // The texts polish, 2026-10-10, page #135 (previously "... What it has
                    // over the last release is what the changelog lists as not yet
                    // released.").
                    .Append(PageContent.Text($"This is a development build, {installed}. Below is what the changelog lists as not yet released."))
                    .Append("</p>\n");
                break;
        }

        AppendBody(html, section.Markdown);
        return html.ToString();
    }

    /// <summary>One section's Markdown, as HTML: its group headings, and each entry folded under its headline.</summary>
    /// <param name="html">Where to write.</param>
    /// <param name="markdown">The section.</param>
    internal static void AppendBody(StringBuilder html, string markdown)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(markdown);

        var paragraph = new List<string>();
        List<string>? entry = null;
        var inList = false;

        void flushParagraph()
        {
            if (paragraph.Count > 0)
            {
                _ = html.Append("<p>").Append(Inline(string.Join(' ', paragraph))).Append("</p>\n");
                paragraph.Clear();
            }
        }

        void flushEntry()
        {
            if (entry is null)
            {
                return;
            }

            AppendEntry(html, entry);
            entry = null;
        }

        void closeList()
        {
            flushEntry();

            if (inList)
            {
                _ = html.Append("</div>\n");
                inList = false;
            }
        }

        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                closeList();
                flushParagraph();
                _ = html.Append("<h2>").Append(Inline(line[4..].Trim())).Append("</h2>\n");
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                flushParagraph();
                flushEntry();

                if (!inList)
                {
                    _ = html.Append("<div class=\"entries\">\n");
                    inList = true;
                }

                entry = [line[2..]];
            }
            else if (entry is not null && (line.Length is 0 || line.StartsWith("  ", StringComparison.Ordinal)))
            {
                entry.Add(line.Length is 0 ? string.Empty : line[2..]);
            }
            else if (line.Length is 0)
            {
                flushParagraph();
            }
            else
            {
                closeList();
                paragraph.Add(line.Trim());
            }
        }

        closeList();
        flushParagraph();
    }

    /// <summary>Escapes text and then reads the inline Markdown the changelog uses.</summary>
    /// <param name="text">Raw Markdown text.</param>
    /// <returns>The HTML.</returns>
    internal static string Inline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Code spans first, so nothing inside one is read as markup. Each is held out
        // as a token of letters and digits, which the escaping leaves alone, and put
        // back last, escaped like everything else.
        var spans = new List<string>();
        var marked = CodeSpan().Replace(text, match =>
        {
            spans.Add(match.Groups["code"].Value);
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{SpanToken}{spans.Count - 1}{SpanToken}");
        });

        var html = PageContent.Text(marked);

        html = Link().Replace(html, match =>
        {
            var label = match.Groups["label"].Value;
            var target = System.Net.WebUtility.HtmlDecode(match.Groups["target"].Value);

            return target.StartsWith("https://", StringComparison.Ordinal)
                ? $"<a href=\"{PageContent.Text(target)}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>"
                : label;
        });
        html = Bold().Replace(html, "<strong>$1</strong>");
        html = Italic().Replace(html, "<em>$1</em>");

        return Placeholder().Replace(html, match => "<code>" + PageContent.Text(spans[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]) + "</code>");
    }

    /// <summary>One entry: its headline as the summary, and the rest folded under it.</summary>
    private static void AppendEntry(StringBuilder html, List<string> lines)
    {
        // The headline is the first paragraph: the icon and the bold sentence.
        var headline = new List<string>();
        var index = 0;

        while (index < lines.Count && lines[index].Length > 0)
        {
            headline.Add(lines[index].Trim());
            index++;
        }

        var first = string.Join(' ', headline);
        var detail = new StringBuilder();

        // A headline paragraph that runs on past its bold sentence keeps the rest as
        // the detail's first paragraph.
        var split = HeadlineAndRest().Match(first);
        var summary = split.Success ? split.Groups["headline"].Value : first;

        if (split.Success && split.Groups["rest"].Value.Trim() is { Length: > 0 } rest)
        {
            _ = detail.Append("<p>").Append(Inline(rest)).Append("</p>\n");
        }

        var paragraph = new List<string>();

        for (; index < lines.Count; index++)
        {
            if (lines[index].Length is 0)
            {
                if (paragraph.Count > 0)
                {
                    _ = detail.Append("<p>").Append(Inline(string.Join(' ', paragraph))).Append("</p>\n");
                    paragraph.Clear();
                }
            }
            else
            {
                paragraph.Add(lines[index].Trim());
            }
        }

        if (paragraph.Count > 0)
        {
            _ = detail.Append("<p>").Append(Inline(string.Join(' ', paragraph))).Append("</p>\n");
        }

        _ = detail.Length is 0
            ? html.Append("<p class=\"entry\">").Append(Inline(summary)).Append("</p>\n")
            : html.Append("<details class=\"entry\"><summary>").Append(Inline(summary)).Append("</summary>\n").Append(detail).Append("</details>\n");
    }

    /// <summary>Every <c>## </c> section of the changelog: its heading and its text.</summary>
    private static IEnumerable<(string Heading, string Body)> Sections(string changelog)
    {
        string? heading = null;
        var body = new StringBuilder();

        foreach (var raw in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    yield return (heading, body.ToString());
                }

                heading = raw[3..].Trim();
                _ = body.Clear();
            }
            else if (heading is not null)
            {
                _ = body.Append(raw).Append('\n');
            }
        }

        if (heading is not null)
        {
            yield return (heading, body.ToString());
        }
    }

    /// <summary>A section heading's version and date: <c>[1.2.0] - 2026-10-10</c>, or <c>[Unreleased]</c>.</summary>
    [GeneratedRegex(@"^\[(?<version>[^\]]+)\]\s*(?:-\s*(?<date>\S+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionHeading();

    /// <summary>An entry's headline: everything up to the end of its bold sentence.</summary>
    [GeneratedRegex(@"^(?<headline>.*?\*\*.+?\*\*)(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadlineAndRest();

    [GeneratedRegex(@"`(?<code>[^`]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(SpanToken + @"(\d+)" + SpanToken, RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\[(?<label>[^\]]+)\]\((?<target>[^)\s]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])", RegexOptions.CultureInvariant)]
    private static partial Regex Italic();
}

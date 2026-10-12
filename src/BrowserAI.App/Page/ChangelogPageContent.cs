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
/// <b>56 a, the maintainer's choice of 2026-10-11, in his words verbatim: <i>"56 a"</i></b>: a
/// sidebar beside the section lists every release the changelog holds, in its order, with the
/// installed one chosen and marked, and every other one at an address of its own,
/// <c>changelog?version=</c>, its version or <see cref="UnreleasedKey"/>.
/// </para>
/// <para>
/// <b>Each entry is folded under its headline</b>, the icon and the bold first
/// sentence every entry opens with (the changelog's own rule), the way the release
/// body folds them. Only the subset of Markdown the changelog uses is read:
/// paragraphs, the entry list, bold, italics, code and links.
/// </para>
/// <para>
/// <b>A link to a file in the repository opens that file on GitHub</b>, since 2026-10-12, the
/// maintainer's answer verbatim: <i>"88 a+c"</i>. <i>Previously such a link was shown as its
/// label alone, because the page has no copy of the file, and a pointer such as "The hazard
/// row." read as a fragment.</i> <b>Each release's section links at that release's own tag,
/// and only what is not yet released links at the build's ref</b>, his answer verbatim:
/// <i>"92 b"</i>, since a section's anchors are the ones its release was written against. The
/// build's ref is its release's tag for a release build; <b>a pre-release build has no tag and
/// carries no commit</b>, since the informational version is built with no source revision,
/// so its links go to <see cref="PreReleaseRef"/>, which is the build's choice, for his
/// review. Only a relative path that stays inside the repository becomes such a link; an
/// address keeps the treatment it had, and anything else stays its label.
/// </para>
/// </remarks>
internal static partial class ChangelogPageContent
{
    /// <summary>The heading of the changes not yet released.</summary>
    public const string Unreleased = "Unreleased";

    /// <summary>What the page's address names the changes not yet released by.</summary>
    public const string UnreleasedKey = "unreleased";

    /// <summary>What a code span is held out as while the rest of a line is read: letters only, which no entry writes.</summary>
    private const string SpanToken = "QzxcodespanzxQ";

    /// <summary>Where a file of this repository is shown on GitHub, before the ref and the path.</summary>
    public const string RepositoryFiles = "https://github.com/SixFive7/BrowserAI/blob/";

    /// <summary>
    /// The ref a pre-release build's links into the repository are drawn at: it has no tag of its
    /// own and carries no commit, so the branch it was built from.
    /// </summary>
    public const string PreReleaseRef = "master";

    /// <summary>The ref a build's links into the repository are drawn at: <c>v</c> and its version for a release, <see cref="PreReleaseRef"/> for anything else.</summary>
    /// <param name="version">The build's version; build metadata after <c>+</c> is ignored.</param>
    /// <returns>The ref.</returns>
    public static string RefFor(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var plain = version.Split('+')[0];

        return BrowserAI.Hosting.BuildVersion.HasPreReleaseSuffix(plain) ? PreReleaseRef : "v" + plain;
    }

    /// <summary>
    /// The address on GitHub of a link's target inside this repository, at a ref, or
    /// <see langword="null"/> for a target that is not a relative path inside it.
    /// </summary>
    /// <remarks>
    /// The changelog lives at the repository's root, so a path is read from there. A path that
    /// names a scheme, starts at a root, carries a query or climbs out of the repository is
    /// none of this repository's files, and a bare anchor is one of the changelog's own.
    /// </remarks>
    /// <param name="target">The link's target, as the changelog writes it.</param>
    /// <param name="at">The ref.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    internal static string? InRepository(string target, string at)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(at);

        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var path = hash < 0 ? target : target[..hash];
        var anchor = hash < 0 ? string.Empty : target[(hash + 1)..];

        if (path.Contains(':', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal)
            || path.StartsWith('/') || anchor.Contains('#', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = new List<string>();
        var parts = (path.Length is 0 ? "CHANGELOG.md" : path).Split('/');

        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];

            if (part is "." || (part.Length is 0 && index < parts.Length - 1))
            {
                continue;
            }

            if (part is "..")
            {
                if (segments.Count is 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(Uri.EscapeDataString(Uri.UnescapeDataString(part)));
        }

        if (segments.Count is 0 || segments.All(static segment => segment.Length is 0))
        {
            return null;
        }

        return RepositoryFiles + Uri.EscapeDataString(at) + "/" + string.Join('/', segments)
            + (anchor.Length is 0 ? string.Empty : "#" + Uri.EscapeDataString(Uri.UnescapeDataString(anchor)));
    }

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

    /// <summary>
    /// Every release the changelog holds, in its order, and what is not yet released first
    /// when it holds an entry.
    /// </summary>
    /// <param name="changelog">The whole changelog.</param>
    /// <returns>The sections.</returns>
    public static IReadOnlyList<ChangelogSection> Releases(string changelog)
    {
        ArgumentNullException.ThrowIfNull(changelog);

        var releases = new List<ChangelogSection>();

        foreach (var (heading, body) in Sections(changelog))
        {
            if (VersionHeading().Match(heading) is not { Success: true } match)
            {
                continue;
            }

            var named = match.Groups["version"].Value;

            if (!string.Equals(named, Unreleased, StringComparison.OrdinalIgnoreCase))
            {
                releases.Add(new ChangelogSection(named, match.Groups["date"].Success ? match.Groups["date"].Value : null, body));
            }
            else if (body.Split('\n').Any(static line => line.StartsWith("- ", StringComparison.Ordinal)))
            {
                releases.Add(new ChangelogSection(null, null, body));
            }
        }

        return releases;
    }

    /// <summary>What the page's address names a section by: its version, or <see cref="UnreleasedKey"/>.</summary>
    /// <param name="section">The section.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(ChangelogSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        return section.Version ?? UnreleasedKey;
    }

    /// <summary>The changelog page's main part.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously the headings "Changelog", "What changed in BrowserAI
    /// {version}" and "What changed since the last release", with no way back)</i>: the
    /// maintainer's 40 a, direction A's <i>What is new</i> page, reached from the home's footer
    /// and its install card, begun with a way back.
    /// </remarks>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var html = new StringBuilder();
        var installed = view.Facts.Version;
        var installedKey = view.Changelog is { } own ? KeyOf(own) : null;
        var asked = view.Subject is { Length: > 0 } named ? view.Releases.FirstOrDefault(release => string.Equals(KeyOf(release), named, StringComparison.Ordinal)) : null;
        var section = asked ?? view.Changelog;

        switch (section)
        {
            case null:
                return html.Append(PageContent.Crumbs(view.Tab, "What is new"))
                    .Append(PageContent.Title("What is new", $"This build of BrowserAI, {installed}, carries no changelog section for its version."))
                    .ToString();

            case { Version: { } version } released:
                _ = html.Append(PageContent.Crumbs(view.Tab, $"What is new in {version}"))
                    .Append(PageContent.Title(
                        $"What is new in BrowserAI {version}",
                        released.Date is { Length: > 0 } date ? $"Released {date}. Open a line to read more." : "Open a line to read more."));
                break;

            default:
                // The texts polish, 2026-10-10, page #135 (previously "... What it has over
                // the last release is what the changelog lists as not yet released."). A
                // release build shows it only when a person asks for it (56 a).
                _ = html.Append(PageContent.Crumbs(view.Tab, "What is new"))
                    .Append(PageContent.Title(
                        "What is new since the last release",
                        string.Equals(installedKey, UnreleasedKey, StringComparison.Ordinal)
                            ? $"This is a development build, {installed}. Below is what the changelog lists as not yet released."
                            : "Below is what the changelog lists as not yet released."));
                break;
        }

        if (view.Subject is { Length: > 0 } unknown && asked is null)
        {
            _ = html.Append("<p class=\"small\">This build's changelog holds no release ").Append(PageContent.Text(unknown)).Append(", so the page shows the installed one.</p>\n");
        }

        // 92 b, 2026-10-12: a release's own tag for its section, the build's ref for what is
        // not yet released.
        var at = section.Version is { } itsVersion ? RefFor(itsVersion) : RefFor(installed);

        if (view.Releases.Count < 2)
        {
            _ = html.Append("<div class=\"log-group\">\n");
            AppendBody(html, section.Markdown, at);
            return html.Append("</div>\n").ToString();
        }

        _ = html.Append("<div class=\"log-layout\">\n<nav class=\"releases\" aria-label=\"Every release\"><p class=\"rel-head\">Releases</p>");

        foreach (var release in view.Releases)
        {
            var key = KeyOf(release);
            var current = asked is null ? string.Equals(key, installedKey, StringComparison.Ordinal) : ReferenceEquals(release, asked);

            _ = html.Append("<a id=\"l-release-").Append(PageContent.Text(key)).Append("\" href=\"")
                .Append(PageContent.Route(PageKind.Changelog, view.Tab, "version=" + PageContent.Text(Uri.EscapeDataString(key)))).Append('"')
                .Append(current ? " aria-current=\"page\"" : string.Empty)
                .Append("><b>").Append(PageContent.Text(release.Version ?? "Not yet released")).Append("</b>")
                .Append(release.Date is { Length: > 0 } date ? "<span class=\"small\">" + PageContent.Text(date) + "</span>" : string.Empty)
                .Append(string.Equals(key, installedKey, StringComparison.Ordinal) ? "<span class=\"tag installed\">Installed</span>" : string.Empty)
                .Append("</a>");
        }

        _ = html.Append("</nav>\n<div class=\"log-group\">\n");
        AppendBody(html, section.Markdown, at);
        return html.Append("</div>\n</div>\n").ToString();
    }

    /// <summary>One section's Markdown, as HTML: its group headings, and each entry folded under its headline.</summary>
    /// <param name="html">Where to write.</param>
    /// <param name="markdown">The section.</param>
    /// <param name="at">The ref a link into the repository is drawn at, or <see langword="null"/> to show such a link as its label.</param>
    internal static void AppendBody(StringBuilder html, string markdown, string? at = null)
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
                _ = html.Append("<p>").Append(Inline(string.Join(' ', paragraph), at)).Append("</p>\n");
                paragraph.Clear();
            }
        }

        void flushEntry()
        {
            if (entry is null)
            {
                return;
            }

            AppendEntry(html, entry, at);
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
                _ = html.Append("<h2>").Append(Inline(line[4..].Trim(), at)).Append("</h2>\n");
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
    /// <param name="at">The ref a link into the repository is drawn at, or <see langword="null"/> to show such a link as its label.</param>
    /// <returns>The HTML.</returns>
    internal static string Inline(string text, string? at = null)
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
                : at is not null && InRepository(target, at) is { } file
                    ? $"<a href=\"{PageContent.Text(file)}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>"
                    : label;
        });
        html = Bold().Replace(html, "<strong>$1</strong>");
        html = Italic().Replace(html, "<em>$1</em>");

        return Placeholder().Replace(html, match => "<code>" + PageContent.Text(spans[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]) + "</code>");
    }

    /// <summary>One entry: its headline as the summary, and the rest folded under it.</summary>
    private static void AppendEntry(StringBuilder html, List<string> lines, string? at)
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
            _ = detail.Append("<p>").Append(Inline(rest, at)).Append("</p>\n");
        }

        var paragraph = new List<string>();

        for (; index < lines.Count; index++)
        {
            if (lines[index].Length is 0)
            {
                if (paragraph.Count > 0)
                {
                    _ = detail.Append("<p>").Append(Inline(string.Join(' ', paragraph), at)).Append("</p>\n");
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
            _ = detail.Append("<p>").Append(Inline(string.Join(' ', paragraph), at)).Append("</p>\n");
        }

        _ = detail.Length is 0
            ? html.Append("<p class=\"entry\">").Append(Inline(summary, at)).Append("</p>\n")
            : html.Append("<details class=\"entry\"><summary>").Append(Inline(summary, at)).Append("</summary>\n").Append(detail).Append("</details>\n");
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A Markdown heading as the <c>#anchor</c> GitHub gives it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from a working implementation rather than derived</b>
/// (<c>.work\check-anchors.py</c>, 2026-08-18), because the risk the anchor
/// check carries is a slug rule that is <i>nearly</i> right.
/// <c>DocumentationLinkTests.TheSlugRuleIsTheOneGitHubApplies</c> pins it to
/// worked examples.
/// </para>
/// <para>
/// The rule: a link becomes its own text, inline HTML disappears, code and
/// emphasis markers are dropped, then lower-case; keep letters, digits, hyphens
/// and underscores, turn each space into a hyphen, and drop everything else.
/// <b>Combining marks are kept</b> because GitHub keeps them — a branch this
/// corpus does not exercise, since the only non-ASCII characters in any heading
/// here are an em-dash and an arrow, both dropped.
/// </para>
/// <para>
/// ⚠️ <b>It lives here, rather than inside the test class that reads it, because
/// a second consumer arrived on 2026-09-15.</b>
/// <c>build/New-ReleaseNotes.ps1</c> computes the same anchor to point a release
/// body at the changelog section it was generated from, and a link in a
/// published release is the one link in this project nothing can re-check after
/// the fact. <c>ChangelogTests.TheReleaseNotesAnchorIsTheOneTheLinkCheckerComputes</c>
/// holds the two implementations against each other over the shapes that
/// distinguish them — the two are not one implementation and cannot be, since
/// one of them is PowerShell, so what is mechanised is that they agree.
/// </para>
/// </remarks>
internal static partial class MarkdownAnchor
{
    /// <summary>A heading's text as GitHub's anchor slug.</summary>
    /// <param name="heading">The heading text, without its leading hashes.</param>
    /// <returns>The slug.</returns>
    public static string Slug(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        // ⚠️ CODE SPANS FIRST, and this order is the whole reason the rule
        // agrees with GitHub. `Setup.exe -- <args>` in kb/packaging/velopack.md
        // is a heading whose code span holds an angle-bracketed word: to a
        // renderer that is four literal characters, to an HTML stripper it is a
        // tag. Strip the tags first and the two rules produce different slugs —
        // which HAZARDS.md recorded on 2026-08-17 by deliberately writing that
        // link WITHOUT its anchor, the one link in the repository that had to
        // avoid the check. Emptying the span of its angle brackets here, before
        // anything looks for a tag, is what closed that.
        var text = EmphasisAndCode().Replace(
            InlineHtml().Replace(
                LinkText().Replace(CodeSpan().Replace(heading, Literally), "$1"),
                string.Empty),
            string.Empty);

        var slug = new StringBuilder(text.Length);

        // Lower-cased one character at a time rather than by lower-casing the
        // whole string: CA1308 forbids the second at error severity, and the
        // classification below does not depend on case, so the two are the same
        // answer by a route the analyzer permits.
        foreach (var character in text.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                _ = slug.Append(char.ToLowerInvariant(character));
            }
            else if (character is ' ' or '\t')
            {
                _ = slug.Append('-');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(character)
                is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                _ = slug.Append(character);
            }
        }

        return slug.ToString();
    }

    /// <summary>
    /// A code span reduced to the text a reader sees, with the two characters
    /// that would otherwise be mistaken for a tag removed.
    /// </summary>
    /// <remarks>
    /// They are removed rather than replaced by a space because GitHub drops
    /// them as characters — a space would become a hyphen and the slug would be
    /// wrong in the other direction.
    /// </remarks>
    /// <param name="span">The matched code span, backticks included.</param>
    /// <returns>Its contents, without angle brackets.</returns>
    private static string Literally(Match span) =>
        span.Groups["text"].Value.Replace("<", string.Empty, StringComparison.Ordinal).Replace(">", string.Empty, StringComparison.Ordinal);

    /// <summary>A Markdown link inside a heading, reduced to the text a reader sees.</summary>
    /// <remarks>
    /// Written with every bracket escaped so that this pattern is not itself a
    /// link, which is the same trap the link scanner's own patterns carry.
    /// </remarks>
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkText();

    /// <summary>Inline HTML in a heading, which contributes nothing to the slug.</summary>
    /// <remarks>
    /// Applied only after <see cref="CodeSpan"/> has emptied the spans of their
    /// angle brackets, so that a tag-shaped word inside a code span is never
    /// read as a tag.
    /// </remarks>
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex InlineHtml();

    /// <summary>An inline code span, backticks included.</summary>
    [GeneratedRegex("`(?<text>[^`]*)`")]
    private static partial Regex CodeSpan();

    /// <summary>
    /// Code and emphasis markers, which are stripped before a character is
    /// looked at.
    /// </summary>
    /// <remarks>
    /// One character class rather than the ported alternation of backtick,
    /// double star and single star: removing every asterisk is exactly what
    /// removing both star forms does, and it cannot be read as ordered.
    /// <b>An underscore is not in it</b> — GitHub keeps underscores in a slug,
    /// so stripping them here would be a rule that is nearly right.
    /// </remarks>
    [GeneratedRegex("[`*]")]
    private static partial Regex EmphasisAndCode();
}

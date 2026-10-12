// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;

namespace BrowserAI.App.Page;

/// <summary>The page's line icons and BrowserAI's own mark, as inline SVG.</summary>
/// <remarks>
/// <para>
/// <b>Drawn for the mockups of 2026-10-11 and carried over as they were</b>: 24 by 24,
/// a stroke in the current colour, and none of them copies a vendor's mark. BrowserAI's
/// own is <c>assets/icon.svg</c>, the candidate the maintainer chose on 2026-09-16 (Q196).
/// </para>
/// <para>
/// <b>Inline SVG is markup, so the content security policy admits it</b>: no style
/// attribute, no script and nothing fetched. Every icon is hidden from assistive
/// technology, because the words beside it say the same thing.
/// </para>
/// </remarks>
internal static class PageIcons
{
    private static readonly FrozenDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["chat"] = "<path d=\"M4 5.5A1.5 1.5 0 0 1 5.5 4h13A1.5 1.5 0 0 1 20 5.5v9a1.5 1.5 0 0 1-1.5 1.5H9l-5 4z\"/>",
        ["window"] = "<rect x=\"3\" y=\"4.5\" width=\"18\" height=\"15\" rx=\"2\"/><path d=\"M3 9h18\"/><circle cx=\"6\" cy=\"6.8\" r=\".6\" fill=\"currentColor\"/><circle cx=\"8.2\" cy=\"6.8\" r=\".6\" fill=\"currentColor\"/>",
        ["hidden"] = "<path d=\"M3 12s3.2-6 9-6c1.6 0 3 .4 4.2 1M21 12s-3.2 6-9 6c-1.6 0-3-.4-4.2-1\"/><path d=\"M9.6 14.4a3 3 0 0 1 4.8-4.8\"/><path d=\"M4 20 20 4\"/>",
        ["closed"] = "<rect x=\"3\" y=\"4.5\" width=\"18\" height=\"15\" rx=\"2\" stroke-dasharray=\"3 2.4\"/><path d=\"M9.5 12h5\"/>",
        ["folder"] = "<path d=\"M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2.2h8.5A1.5 1.5 0 0 1 21 8.7V18a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18z\"/>",
        ["update"] = "<path d=\"M12 3.5v11m0 0-4.2-4.2M12 14.5l4.2-4.2\"/><path d=\"M4 16.5V19a1.5 1.5 0 0 0 1.5 1.5h13A1.5 1.5 0 0 0 20 19v-2.5\"/>",
        ["plug"] = "<path d=\"M9 3v5M15 3v5\"/><path d=\"M6.5 8h11v3.5a5.5 5.5 0 0 1-11 0z\"/><path d=\"M12 17v4\"/>",
        ["disk"] = "<ellipse cx=\"12\" cy=\"6\" rx=\"8\" ry=\"2.8\"/><path d=\"M4 6v12c0 1.6 3.6 2.8 8 2.8s8-1.2 8-2.8V6\"/><path d=\"M4 12c0 1.6 3.6 2.8 8 2.8s8-1.2 8-2.8\"/>",
        ["file"] = "<path d=\"M6.5 3h7.5l4.5 4.5V20a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z\"/><path d=\"M14 3v4.5h4.5M9 12.5h6M9 16h6\"/>",
        ["sparkle"] = "<path d=\"M12 3.5l1.9 5.2 5.1 1.8-5.1 1.9L12 17.5l-1.9-5.1L5 10.5l5.1-1.8z\"/><path d=\"M18.5 16.5l.7 1.8 1.8.7-1.8.7-.7 1.8-.7-1.8-1.8-.7 1.8-.7z\"/>",
        ["info"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 11v5.5\"/><circle cx=\"12\" cy=\"7.8\" r=\".7\" fill=\"currentColor\"/>",
        ["checkCircle"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M8.2 12.3l2.6 2.6 5-5.2\"/>",
        ["warn"] = "<path d=\"M12 4 2.8 19.5h18.4z\"/><path d=\"M12 10v4.5\"/><circle cx=\"12\" cy=\"17\" r=\".7\" fill=\"currentColor\"/>",
        ["bad"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M9 9l6 6M15 9l-6 6\"/>",
        ["minus"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M8.5 12h7\"/>",
        ["chevron"] = "<path d=\"M9.5 6l6 6-6 6\"/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 7.5V12l3 2\"/>",
        ["map"] = "<path d=\"M3.5 6.5 9 4l6 2.5L20.5 4v13.5L15 20l-6-2.5-5.5 2.5z\"/><path d=\"M9 4v13.5M15 6.5V20\"/>",
        ["terminal"] = "<rect x=\"3\" y=\"4.5\" width=\"18\" height=\"15\" rx=\"2\"/><path d=\"M7 10l3 2.2L7 14.5M12.5 15h4.5\"/>",
        ["code"] = "<rect x=\"3\" y=\"4.5\" width=\"18\" height=\"15\" rx=\"2\"/><path d=\"M9.5 9.5 7 12l2.5 2.5M14.5 9.5 17 12l-2.5 2.5\"/>",
        ["globe"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M3.5 12h17M12 3.5c2.4 2.4 3.4 5.2 3.4 8.5s-1 6.1-3.4 8.5c-2.4-2.4-3.4-5.2-3.4-8.5s1-6.1 3.4-8.5z\"/>",
        ["external"] = "<path d=\"M14 4h6v6M20 4l-8.5 8.5\"/><path d=\"M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5\"/>",
        ["refresh"] = "<path d=\"M19.5 12a7.5 7.5 0 1 1-2.2-5.3\"/><path d=\"M19.5 4.5v4h-4\"/>",
        ["pulse"] = "<path d=\"M3 12h4l2-5 4 10 2-5h6\"/>",
        ["question"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M9.6 9.4a2.5 2.5 0 1 1 3.4 2.4c-.7.3-1 .8-1 1.5v.6\"/><circle cx=\"12\" cy=\"16.8\" r=\".7\" fill=\"currentColor\"/>",
        ["list"] = "<path d=\"M9 7h11M9 12h11M9 17h11\"/><circle cx=\"5\" cy=\"7\" r=\".8\" fill=\"currentColor\"/><circle cx=\"5\" cy=\"12\" r=\".8\" fill=\"currentColor\"/><circle cx=\"5\" cy=\"17\" r=\".8\" fill=\"currentColor\"/>",
        ["bolt"] = "<path d=\"M13 3 5 13.5h6L10 21l8-10.5h-6z\"/>",

        // The overview's CPU and memory card and the notice's dismiss, 2026-10-12, as the
        // side-column layout drew them (59 b, 57 a).
        ["cpu"] = "<rect x=\"7\" y=\"7\" width=\"10\" height=\"10\" rx=\"1.6\"/><path d=\"M10 3.5v3.5M14 3.5v3.5M10 17v3.5M14 17v3.5M3.5 10H7M3.5 14H7M17 10h3.5M17 14h3.5\"/>",
        ["memory"] = "<rect x=\"3\" y=\"6.5\" width=\"18\" height=\"9\" rx=\"1.5\"/><path d=\"M7.5 9.5v3M11 9.5v3M14.5 9.5v3M6 15.5v2.5M10 15.5v2.5M14 15.5v2.5M18 15.5v2.5\"/>",
        ["shared"] = "<circle cx=\"9.2\" cy=\"12\" r=\"5.4\"/><circle cx=\"14.8\" cy=\"12\" r=\"5.4\"/>",
        ["x"] = "<path d=\"M7 7l10 10M17 7 7 17\"/>",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The names an icon can be asked for.</summary>
    public static IEnumerable<string> Names => Paths.Keys;

    /// <summary>One line icon.</summary>
    /// <param name="name">Its name; a name with no icon draws an empty one.</param>
    /// <param name="extra">A class to add, or <see langword="null"/>.</param>
    /// <returns>The SVG.</returns>
    public static string Of(string name, string? extra = null) =>
        "<svg class=\"icon" + (extra is null ? string.Empty : " " + extra)
        + "\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
        + Paths.GetValueOrDefault(name, string.Empty) + "</svg>";

    /// <summary>BrowserAI's own mark, as <c>assets/icon.svg</c> draws it.</summary>
    /// <param name="id">What its gradient and clip are called, unique in the page, since one page draws it twice.</param>
    /// <param name="extra">A class to add, or <see langword="null"/>.</param>
    /// <returns>The SVG.</returns>
    public static string Brand(string id, string? extra = null) =>
        "<svg class=\"brand-mark" + (extra is null ? string.Empty : " " + extra) + "\" viewBox=\"0 0 256 256\" aria-hidden=\"true\" focusable=\"false\">"
        + "<defs><linearGradient id=\"" + id + "-g\" x1=\"0.1\" y1=\"0\" x2=\"0.9\" y2=\"1\"><stop offset=\"0\" stop-color=\"#34D399\"/><stop offset=\"0.55\" stop-color=\"#10B981\"/><stop offset=\"1\" stop-color=\"#0E7490\"/></linearGradient>"
        + "<clipPath id=\"" + id + "-c\"><circle cx=\"128\" cy=\"128\" r=\"112\"/></clipPath></defs>"
        + "<circle cx=\"128\" cy=\"128\" r=\"112\" fill=\"url(#" + id + "-g)\"/>"
        + "<g clip-path=\"url(#" + id + "-c)\" fill=\"none\" stroke=\"#ECFEFF\" stroke-width=\"10\" opacity=\"0.45\" stroke-linecap=\"round\"><path d=\"M6 100 Q128 82 250 100\"/><path d=\"M6 158 Q128 176 250 158\"/><path d=\"M128 14 Q70 128 128 242\"/><path d=\"M128 14 Q186 128 128 242\"/></g>"
        + "<path d=\"M44 128 Q128 58 212 128 Q128 198 44 128 Z\" fill=\"#F0FDFA\"/><circle cx=\"128\" cy=\"128\" r=\"31\" fill=\"#083344\"/><circle cx=\"140\" cy=\"116\" r=\"9\" fill=\"#ECFEFF\"/></svg>";
}

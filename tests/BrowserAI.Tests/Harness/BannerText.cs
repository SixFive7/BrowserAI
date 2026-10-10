// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// How an update toast's text lies in its banner: how wide a line is, and how many
/// lines a text element takes once the banner wraps it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read 2026-10-10 off the screen crops of the run of 2026-10-08</b>, on the
/// maintainer's screen, Windows 11 Pro 10.0.26300.9550 at 3840x2160. The banner wraps
/// a text element at about 331 px of Segoe UI at 14 px: every line it showed whole
/// measures at most <see cref="LinePixels"/> through GDI's
/// <c>GetTextExtentPoint32W</c>, and every text it moved on to another line, or cut,
/// measures 337 px or more. <see cref="ShownWhole"/>, <see cref="DidNotFit"/> and
/// <see cref="Wrapped"/> are those lines, so a test that uses this measures them
/// first (<c>kb/windows/notifications.md</c>). <i>Added 2026-10-10 by addition</i>: the
/// crops of that day's on-screen check, seven toasts db50964e composed, agree, the widest
/// line shown whole 321 px and the narrowest text moved on 338 px, and
/// <see cref="WrappedExactly"/> holds five of their texts to the lines the banner gave
/// each, line by line (<c>docs/evidence/2026-10-10-toast-screen</c>).
/// </para>
/// <para>
/// <b>It breaks a line where the banner breaks one</b>: at a space, which goes, and after
/// a slash or a hyphen that a letter follows, which stays at the end of the line; a piece
/// wider than a line is cut where it stops fitting. The banner also breaks inside a path,
/// before a <c>%</c>, so it can take fewer lines than this counts: the failed toast's
/// third line of 2026-10-08 took three lines on screen and takes four here. <i>Corrected
/// 2026-10-10 (previously "It breaks a line at spaces only")</i>, off that day's crops,
/// where the banner ended a line with <i>need /</i> before <i>mcp</i> and with
/// <i>1.1.1-</i> before <i>alpha.0.325</i>.
/// </para>
/// <para>
/// <b>A title has two lines and the other two text elements four between them</b>, in
/// Microsoft's words <i>"The default (and maximum) is up to 2 lines of text for the
/// title, and up to 4 lines (combined) for the two additional description elements"</i>,
/// read 2026-10-10 in <i>App notification content</i> on Microsoft Learn. The failed
/// toast showed all four of its description lines that night.
/// </para>
/// </remarks>
internal static partial class BannerText
{
    /// <summary>The widest line the banner showed whole, in pixels of Segoe UI at 14 px.</summary>
    public const int LinePixels = 330;

    /// <summary>The lines a toast's title may take.</summary>
    public const int TitleLines = 2;

    /// <summary>The lines a toast's second and third text elements may take between them.</summary>
    public const int DescriptionLines = 4;

    private const int FontPixels = 14;
    private const int NormalWeight = 400;
    private const uint DefaultCharacterSet = 1;

    /// <summary>Lines the banner showed whole, each one line of its crop: those of 2026-10-08, then those of 2026-10-10.</summary>
    public static IReadOnlyList<string> ShownWhole { get; } =
    [
        "In use by 3 agents, 2 hidden browsers and 1 visible wi",
        "It installs by itself once BrowserAI has been idle.",
        "After the update: 1 Claude Code terminal needs /mcp,",
        "What happened is in Velopack's log, %LocalAppData",
        @"BrowserAI's log in %LocalAppData%\BrowserAI\logs.",
        "After the update: 99 Claude Code terminals need /",
        "mcp, BrowserAI, Reconnect; 99 Codex conversations",
        "After the update: 1 Codex conversation needs a new",
        "one; new conversation in RegisterAI, \"Summarise the",
        "BrowserAI 1.1.1-alpha.0.323, older than 1.1.1-",
        "99 agents, 99 hidden browsers, 99 windows",
        "from the latest release and run it; your sessions and",
    ];

    /// <summary>
    /// Texts the banner did not fit on one line: on 2026-10-08 it cut the first inside its
    /// last character and moved the last word of the next two on to the next line, and on
    /// 2026-10-10 it moved the last piece of each of the others on, a word or what follows
    /// a slash or a hyphen.
    /// </summary>
    public static IReadOnlyList<string> DidNotFit { get; } =
    [
        "In use by 3 agents, 2 hidden browsers and 1 visible win",
        "After the update: 1 Claude Code terminal needs /mcp, BrowserAI,",
        @"%\velopack\velopack_BrowserAI.log, and in BrowserAI's",
        "After the update: 99 Claude Code terminals need /mcp,",
        "mcp, BrowserAI, Reconnect; 99 Codex conversations need",
        "After the update: 1 Codex conversation needs a new one;",
        "one; new conversation in RegisterAI, \"Summarise the open",
        "BrowserAI 1.1.1-alpha.0.323, older than 1.1.1-alpha.0.325,",
    ];

    /// <summary>The two text elements of 2026-10-08 whose wrapping the crops show, with the lines each took on screen.</summary>
    public static IReadOnlyList<(string Text, int Lines)> Wrapped { get; } =
    [
        ("After the update: 1 Claude Code terminal needs /mcp, BrowserAI, Reconnect.", 2),
        (@"What happened is in Velopack's log, %LocalAppData%\velopack\velopack_BrowserAI.log, and in BrowserAI's log in %LocalAppData%\BrowserAI\logs.", 3),
    ];

    /// <summary>
    /// Five text elements of 2026-10-10, each with the lines the banner gave it, which the
    /// wrapping here must give too, line for line: the counted reconnect line, the
    /// reconnect line with names, the older version's title, the failed toast's third line
    /// and the broken install's second.
    /// </summary>
    public static IReadOnlyList<(string Text, string[] Lines)> WrappedExactly { get; } =
    [
        (
            "After the update: 99 Claude Code terminals need /mcp, BrowserAI, Reconnect; 99 Codex conversations need a new one; 99 clients may need a reconnect.",
            ["After the update: 99 Claude Code terminals need /", "mcp, BrowserAI, Reconnect; 99 Codex conversations", "need a new one; 99 clients may need a reconnect."]
        ),
        (
            "After the update: 1 Codex conversation needs a new one; new conversation in RegisterAI, \"Summarise the open issues\" and 97 more may need a reconnect.",
            ["After the update: 1 Codex conversation needs a new", "one; new conversation in RegisterAI, \"Summarise the", "open issues\" and 97 more may need a reconnect."]
        ),
        (
            "BrowserAI 1.1.1-alpha.0.323, older than 1.1.1-alpha.0.325, is ready to install",
            ["BrowserAI 1.1.1-alpha.0.323, older than 1.1.1-", "alpha.0.325, is ready to install"]
        ),
        (
            @"Velopack's log is in %LocalAppData%\velopack, and BrowserAI's in %LocalAppData%\BrowserAI\logs.",
            [@"Velopack's log is in %LocalAppData%\velopack, and", @"BrowserAI's in %LocalAppData%\BrowserAI\logs."]
        ),
        (
            "Part of this install does not match the rest, so no browser session can open. Download BrowserAI.exe from the latest release and run it; your sessions and their files are kept.",
            ["Part of this install does not match the rest, so no", "browser session can open. Download BrowserAI.exe", "from the latest release and run it; your sessions and", "their files are kept."]
        ),
    ];

    /// <summary>How wide a text is on one line.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Its width in pixels.</returns>
    public static int Width(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Measured(width => width(text));
    }

    /// <summary>The lines a text element wraps into at <see cref="LinePixels"/>.</summary>
    /// <param name="text">The text element's text.</param>
    /// <returns>Its lines.</returns>
    public static IReadOnlyList<string> Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. Measured(width => Wrap(text, width)).Select(line => text[line.Start..line.End])];
    }

    /// <summary>
    /// The parts of a text element that the banner would split across two lines: each
    /// piece given, at every place it occurs, that a line ends inside.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-10</b>, for the three breaks of that day's crops: <i>/mcp</i> after
    /// its slash, <i>1.1.1-alpha.0.325</i> after its hyphen and <i>"Summarise the open
    /// issues"</i> after <i>the</i>. A line ends inside a piece when the next line begins
    /// after the piece's first character and before its end.
    /// </remarks>
    /// <param name="text">The text element's text.</param>
    /// <param name="pieces">What must not be split: a command, a version, a quoted name.</param>
    /// <returns>Every piece split, once for each place it is split, in the order given.</returns>
    public static IReadOnlyList<string> SplitInside(string text, IEnumerable<string> pieces)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pieces);

        var starts = Measured(width => Wrap(text, width)).Skip(1).Select(line => line.Start).ToList();
        var split = new List<string>();

        foreach (var piece in pieces.Where(piece => piece.Length > 0))
        {
            for (var at = text.IndexOf(piece, StringComparison.Ordinal); at >= 0; at = text.IndexOf(piece, at + piece.Length, StringComparison.Ordinal))
            {
                if (starts.Exists(start => start > at && start < at + piece.Length))
                {
                    split.Add(piece);
                }
            }
        }

        return split;
    }

    /// <summary>
    /// Breaks a text into lines no wider than <see cref="LinePixels"/>, where the banner
    /// breaks: at a space, which goes, and after a slash or a hyphen that a letter
    /// follows, which stays.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">How wide a piece of it is.</param>
    /// <returns>Each line as where it starts in the text and where it ends.</returns>
    private static List<(int Start, int End)> Wrap(string text, Func<string, int> width)
    {
        var lines = new List<(int Start, int End)>();
        (int Start, int End)? line = null;

        foreach (var (start, end) in Pieces(text))
        {
            if (line is not { } current)
            {
                line = (start, end);
            }
            else if (width(text[current.Start..end]) <= LinePixels)
            {
                line = (current.Start, end);
            }
            else
            {
                lines.Add(current);
                line = (start, end);
            }

            // A piece wider than a line is cut where it stops fitting.
            while (line is { } wide && width(text[wide.Start..wide.End]) > LinePixels)
            {
                var fits = 1;

                while (wide.Start + fits < wide.End && width(text[wide.Start..(wide.Start + fits + 1)]) <= LinePixels)
                {
                    fits++;
                }

                lines.Add((wide.Start, wide.Start + fits));
                line = (wide.Start + fits, wide.End);
            }
        }

        if (line is { } last && last.End > last.Start)
        {
            lines.Add(last);
        }

        return lines;
    }

    /// <summary>
    /// The pieces a line is built of: what lies between two places the banner may break,
    /// a space dropped from the end of the piece before it, a slash or a hyphen kept.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Each piece as where it starts and where it ends.</returns>
    private static IEnumerable<(int Start, int End)> Pieces(string text)
    {
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is ' ')
            {
                yield return (start, index);
                start = index + 1;
            }
            else if (text[index] is '/' or '-' && index + 1 < text.Length && char.IsLetter(text[index + 1]))
            {
                yield return (start, index + 1);
                start = index + 1;
            }
        }

        yield return (start, text.Length);
    }

    /// <summary>One measuring at a time in this process: see <see cref="Measured{T}"/>.</summary>
    private static readonly Lock Gate = new();

    /// <summary>Measures with Segoe UI at 14 px selected into a memory device context, and lets both go after.</summary>
    /// <remarks>
    /// ⚠️ <b>One at a time, under <see cref="Gate"/>, since 2026-10-10.</b> Measured that day in
    /// the test host: one text wrapped 3,000 times on eight threads came back the same 3,000
    /// times alone, and 3 times of 3,000 with a line ended early while
    /// <c>UpdateToastContentTests</c> measured beside it, so a width read on one thread is
    /// sometimes wrong while another thread creates and selects a font of its own. With the
    /// gate the same pair gave 3,000 of 3,000. Why GDI does this was not found.
    /// </remarks>
    /// <typeparam name="T">What the measuring returns.</typeparam>
    /// <param name="measure">The measuring, handed how wide a text is.</param>
    /// <returns>What it returned.</returns>
    private static T Measured<T>(Func<Func<string, int>, T> measure)
    {
        lock (Gate)
        {
            return MeasuredAlone(measure);
        }
    }

    /// <summary>The measuring itself, which <see cref="Measured{T}"/> runs under its gate.</summary>
    /// <typeparam name="T">What the measuring returns.</typeparam>
    /// <param name="measure">The measuring, handed how wide a text is.</param>
    /// <returns>What it returned.</returns>
    private static T MeasuredAlone<T>(Func<Func<string, int>, T> measure)
    {
        var context = CreateCompatibleDC(nint.Zero);

        if (context == nint.Zero)
        {
            throw new Win32Exception("CreateCompatibleDC returned no device context.");
        }

        try
        {
            var font = CreateFontW(-FontPixels, 0, 0, 0, NormalWeight, 0, 0, 0, DefaultCharacterSet, 0, 0, 0, 0, "Segoe UI");

            if (font == nint.Zero)
            {
                throw new Win32Exception("CreateFontW returned no font for Segoe UI.");
            }

            var previous = SelectObject(context, font);

            try
            {
                return measure(text => WidthIn(context, text));
            }
            finally
            {
                _ = SelectObject(context, previous);
                _ = DeleteObject(font);
            }
        }
        finally
        {
            _ = DeleteDC(context);
        }
    }

    /// <summary>How wide a text is in the font a device context has selected.</summary>
    /// <param name="context">The device context.</param>
    /// <param name="text">The text.</param>
    /// <returns>Its width in pixels.</returns>
    private static int WidthIn(nint context, string text) =>
        text.Length is 0
            ? 0
            : GetTextExtentPoint32W(context, text, text.Length, out var size)
                ? size.Width
                : throw new Win32Exception($"GetTextExtentPoint32W could not measure '{text}'.");

    [StructLayout(LayoutKind.Sequential)]
    private struct TextSize
    {
        public int Width;
        public int Height;
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint context);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint context);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint context, nint graphicsObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint graphicsObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextExtentPoint32W(nint context, string text, int count, out TextSize size);
}

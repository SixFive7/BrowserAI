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
/// first (<c>kb/windows/notifications.md</c>).
/// </para>
/// <para>
/// <b>It breaks a line at spaces only</b>, and a word wider than a line is cut where it
/// stops fitting. The banner also breaks inside a path, before a <c>%</c>, so it can
/// take fewer lines than this counts: the failed toast's third line of that night took
/// three lines on screen and takes four here.
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

    /// <summary>Lines the banner showed whole on 2026-10-08, each one line of its crop.</summary>
    public static IReadOnlyList<string> ShownWhole { get; } =
    [
        "In use by 3 agents, 2 hidden browsers and 1 visible wi",
        "It installs by itself once BrowserAI has been idle.",
        "After the update: 1 Claude Code terminal needs /mcp,",
        "What happened is in Velopack's log, %LocalAppData",
        @"BrowserAI's log in %LocalAppData%\BrowserAI\logs.",
    ];

    /// <summary>
    /// Texts the banner did not fit on one line that night: it cut the first inside its
    /// last character, and moved the last word of each of the others on to the next line.
    /// </summary>
    public static IReadOnlyList<string> DidNotFit { get; } =
    [
        "In use by 3 agents, 2 hidden browsers and 1 visible win",
        "After the update: 1 Claude Code terminal needs /mcp, BrowserAI,",
        @"%\velopack\velopack_BrowserAI.log, and in BrowserAI's",
    ];

    /// <summary>The two text elements of that night whose wrapping the crops show, with the lines each took on screen.</summary>
    public static IReadOnlyList<(string Text, int Lines)> Wrapped { get; } =
    [
        ("After the update: 1 Claude Code terminal needs /mcp, BrowserAI, Reconnect.", 2),
        (@"What happened is in Velopack's log, %LocalAppData%\velopack\velopack_BrowserAI.log, and in BrowserAI's log in %LocalAppData%\BrowserAI\logs.", 3),
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

        return Measured(width => Wrap(text, width));
    }

    /// <summary>Breaks a text at spaces into lines no wider than <see cref="LinePixels"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="width">How wide a piece of it is.</param>
    /// <returns>The lines.</returns>
    private static List<string> Wrap(string text, Func<string, int> width)
    {
        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in text.Split(' '))
        {
            var longer = line.Length is 0 ? word : line + " " + word;

            if (width(longer) <= LinePixels)
            {
                line = longer;
                continue;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }

            // A word wider than a line is cut where it stops fitting.
            line = word;

            while (width(line) > LinePixels)
            {
                var fits = 1;

                while (fits < line.Length && width(line[..(fits + 1)]) <= LinePixels)
                {
                    fits++;
                }

                lines.Add(line[..fits]);
                line = line[fits..];
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Measures with Segoe UI at 14 px selected into a memory device context, and lets both go after.</summary>
    /// <typeparam name="T">What the measuring returns.</typeparam>
    /// <param name="measure">The measuring, handed how wide a text is.</param>
    /// <returns>What it returned.</returns>
    private static T Measured<T>(Func<Func<string, int>, T> measure)
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The comments a file carries, lexed out of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A lexer and not a pattern, and the difference was measured.</b> A URL in a
/// string literal begins with two slashes, so a regular expression looking for
/// <c>//</c> pulls <c>//example.invalid/browserai/</c> out of
/// <c>UpdateTests</c> and into what a prose scan then reads. Worse than the
/// noise, the stray quote characters that come with it desynchronise every
/// quotation after them: the first run of
/// <c>HouseRuleTests.NoMaintainedProseCarriesATell</c> reported <b>114</b>
/// offences that way, of which the real number was two.
/// </para>
/// <para>
/// <b>It skips what a comment cannot be inside.</b> For C# that is the four
/// literal forms -- ordinary, verbatim, raw and character -- each with its own
/// escape rule; for PowerShell and shell, single and double quoted strings; for
/// a project file, everything outside <c>&lt;!-- --&gt;</c>. Anything else is
/// prose and is returned whole, because a Markdown file has no code to skip.
/// </para>
/// </remarks>
internal static class Commentary
{
    /// <summary>The comments in one file, or the whole text when it is prose.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="suffix">Its extension, lower-case, with the dot.</param>
    /// <returns>What a person wrote as commentary.</returns>
    public static string Of(string text, string suffix)
    {
        ArgumentNullException.ThrowIfNull(text);

        return suffix switch
        {
            ".cs" or ".js" or ".mjs" => CStyle(text),
            ".ps1" or ".psm1" => Shell(text, powershell: true),
            ".sh" => Shell(text, powershell: false),
            ".csproj" or ".props" or ".targets" or ".slnx" or ".xml" or ".manifest" => Xml(text),
            _ => text,
        };
    }

    /// <summary>Every <c>//</c> and <c>/* */</c> comment, skipping every literal.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The comments, one per line.</returns>
    private static string CStyle(string text)
    {
        var found = new StringBuilder();

        for (var i = 0; i < text.Length;)
        {
            var c = text[i];

            if (c is '"' && text.AsSpan(i).StartsWith("\"\"\""))
            {
                var fence = 0;
                while (i + fence < text.Length && text[i + fence] is '"')
                {
                    fence++;
                }

                var close = text.IndexOf(new string('"', fence), i + fence, StringComparison.Ordinal);
                i = close < 0 ? text.Length : close + fence;
                continue;
            }

            if (c is '@' && i + 1 < text.Length && text[i + 1] is '"')
            {
                i += 2;
                while (i < text.Length)
                {
                    if (text[i] is not '"')
                    {
                        i++;
                        continue;
                    }

                    if (i + 1 < text.Length && text[i + 1] is '"')
                    {
                        i += 2;
                        continue;
                    }

                    i++;
                    break;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    i += text[i] is '\\' ? 2 : 1;
                }

                i++;
                continue;
            }

            if (c is '/' && i + 1 < text.Length && text[i + 1] is '/')
            {
                var end = text.IndexOf('\n', i);
                end = end < 0 ? text.Length : end;
                _ = found.Append(text[i..end]).Append('\n');
                i = end;
                continue;
            }

            if (c is '/' && i + 1 < text.Length && text[i + 1] is '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                _ = found.Append(text[i..end]).Append('\n');
                i = end;
                continue;
            }

            i++;
        }

        return found.ToString();
    }

    /// <summary>Every <c>#</c> comment, and PowerShell's block form.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="powershell">Whether <c>&lt;# #&gt;</c> and the backtick escape apply.</param>
    /// <returns>The comments, one per line.</returns>
    private static string Shell(string text, bool powershell)
    {
        var found = new StringBuilder();

        for (var i = 0; i < text.Length;)
        {
            var c = text[i];

            if (powershell && c is '<' && text.AsSpan(i).StartsWith("<#"))
            {
                var end = text.IndexOf("#>", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                _ = found.Append(text[i..end]).Append('\n');
                i = end;
                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    i += (powershell ? text[i] is '`' : text[i] is '\\') ? 2 : 1;
                }

                i++;
                continue;
            }

            if (c is '#')
            {
                var end = text.IndexOf('\n', i);
                end = end < 0 ? text.Length : end;
                _ = found.Append(text[i..end]).Append('\n');
                i = end;
                continue;
            }

            i++;
        }

        return found.ToString();
    }

    /// <summary>Every XML comment.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The comments, one per line.</returns>
    private static string Xml(string text)
    {
        var found = new StringBuilder();

        for (var i = 0; ;)
        {
            var start = text.IndexOf("<!--", i, StringComparison.Ordinal);

            if (start < 0)
            {
                return found.ToString();
            }

            var end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end + 3;
            _ = found.Append(text[start..end]).Append('\n');
            i = end;
        }
    }
}

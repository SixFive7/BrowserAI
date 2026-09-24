// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The comments a file carries, and the string literals it carries, lexed out of
/// it by the same walk read in either direction.
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
/// <para>
/// <b>Both halves matter, and they are complements.</b> <see cref="Of"/> returns
/// what a maintainer reads and <see cref="LiteralsOf"/> what a user, a model and
/// a compiler read, so a prose rule applied to only one of them covers half of
/// what the tree says. Neither is a superset of the other and nothing appears in
/// both.
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

        var found = new StringBuilder();

        foreach (var (start, end) in SpansOf(text, suffix))
        {
            _ = found.Append(text[start..end]).Append('\n');
        }

        return found.ToString();
    }

    /// <summary>Where the commentary is, as half-open ranges into the text.</summary>
    /// <remarks>
    /// <para>
    /// <b>The same walk as <see cref="Of"/>, which is built on this one.</b> A
    /// reader that needs to know whether a given CHARACTER is inside a comment
    /// cannot use the concatenated string -- the offsets are gone -- and a second
    /// walk written for the purpose would be a second answer to the same question.
    /// </para>
    /// <para>
    /// For prose the whole file is one span, because a Markdown file has no code
    /// to skip and every character in it is something a person wrote.
    /// </para>
    /// </remarks>
    /// <param name="text">The file's text.</param>
    /// <param name="suffix">Its extension, lower-case, with the dot.</param>
    /// <returns>One range per comment, in order.</returns>
    public static List<(int Start, int End)> SpansOf(string text, string suffix)
    {
        ArgumentNullException.ThrowIfNull(text);

        return suffix switch
        {
            ".cs" or ".js" or ".mjs" or ".cjs" => CStyle(text),
            ".ps1" or ".psm1" => Shell(text, powershell: true),
            ".sh" => Shell(text, powershell: false),
            ".csproj" or ".props" or ".targets" or ".slnx" or ".xml" or ".manifest" => Xml(text),
            _ => [(0, text.Length)],
        };
    }

    /// <summary>Whether one position in a file is inside its commentary.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="suffix">Its extension, lower-case, with the dot.</param>
    /// <param name="index">The position to ask about.</param>
    /// <returns>Whether a person reading the comments would read this character.</returns>
    public static bool IsCommentary(string text, string suffix, int index) =>
        SpansOf(text, suffix).Any(span => index >= span.Start && index < span.End);

    /// <summary>Every <c>//</c> and <c>/* */</c> comment, skipping every literal.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>One range per comment.</returns>
    private static List<(int Start, int End)> CStyle(string text)
    {
        var found = new List<(int Start, int End)>();

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
                found.Add((i, end));
                i = end;
                continue;
            }

            if (c is '/' && i + 1 < text.Length && text[i + 1] is '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                found.Add((i, end));
                i = end;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>Every <c>#</c> comment, and PowerShell's block form.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="powershell">Whether <c>&lt;# #&gt;</c> and the backtick escape apply.</param>
    /// <returns>One range per comment.</returns>
    private static List<(int Start, int End)> Shell(string text, bool powershell)
    {
        var found = new List<(int Start, int End)>();

        for (var i = 0; i < text.Length;)
        {
            var c = text[i];

            if (powershell && c is '<' && text.AsSpan(i).StartsWith("<#"))
            {
                var end = text.IndexOf("#>", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                found.Add((i, end));
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
                found.Add((i, end));
                i = end;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>Every XML comment.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>One range per comment.</returns>
    private static List<(int Start, int End)> Xml(string text)
    {
        var found = new List<(int Start, int End)>();

        for (var i = 0; ;)
        {
            var start = text.IndexOf("<!--", i, StringComparison.Ordinal);

            if (start < 0)
            {
                return found;
            }

            var end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end + 3;
            found.Add((start, end));
            i = end;
        }
    }

    /// <summary>The string literals in one file, or the whole text when it is prose.</summary>
    /// <remarks>
    /// <para>
    /// <b>The inverse of <see cref="Of"/>, over the same lexer.</b> What a person
    /// reads at run time is not in the comments: the server instructions, every
    /// tool and parameter description, every refusal and every log line are
    /// string literals, and a scan that reads only commentary cannot see one of
    /// them. This walks the same states and keeps what the other one skips.
    /// </para>
    /// <para>
    /// <b>A character literal is not returned.</b> It holds one character and can
    /// carry no sentence, and returning it would put every escape and delimiter
    /// this repository spells that way in front of a prose reader.
    /// </para>
    /// <para>
    /// For a project file it is the attribute values and the element bodies,
    /// which is where an <c>&lt;Error Text="..." /&gt;</c> lives. For anything
    /// else there is no code to skip, so the text is returned whole and is its
    /// own literal.
    /// </para>
    /// </remarks>
    /// <param name="text">The file's text.</param>
    /// <param name="suffix">Its extension, lower-case, with the dot.</param>
    /// <returns>What the file says at run time.</returns>
    public static IReadOnlyList<string> LiteralsOf(string text, string suffix)
    {
        ArgumentNullException.ThrowIfNull(text);

        return suffix switch
        {
            ".cs" => CStyleLiterals(text, script: false),
            ".js" or ".mjs" or ".cjs" => CStyleLiterals(text, script: true),
            ".ps1" or ".psm1" => ShellLiterals(text, powershell: true),
            ".sh" => ShellLiterals(text, powershell: false),
            ".csproj" or ".props" or ".targets" or ".slnx" or ".xml" or ".manifest" => XmlText(text),
            _ => [text],
        };
    }

    /// <summary>Every string literal, skipping every comment.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="script">
    /// Whether a single-quoted run and a backtick template are strings. In
    /// JavaScript both are, and the second is where a thrown message with a
    /// placeholder in it lives; in C# the first holds one character and the
    /// second is nothing, so a C# file's single quotes are stepped over and
    /// never returned. <b>Getting this wrong is silent</b>: the reader stays
    /// synchronised either way and simply returns less than the file says.
    /// </param>
    /// <returns>One entry per literal, without its delimiters.</returns>
    private static List<string> CStyleLiterals(string text, bool script)
    {
        var found = new List<string>();

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

                if (close < 0)
                {
                    break;
                }

                found.Add(text[(i + fence)..close]);
                i = close + fence;
                continue;
            }

            if (c is '@' && i + 1 < text.Length && text[i + 1] is '"')
            {
                var j = i + 2;
                while (j < text.Length)
                {
                    if (text[j] is not '"')
                    {
                        j++;
                        continue;
                    }

                    if (j + 1 < text.Length && text[j + 1] is '"')
                    {
                        j += 2;
                        continue;
                    }

                    break;
                }

                found.Add(text[(i + 2)..Math.Min(j, text.Length)]);
                i = j + 1;
                continue;
            }

            if (c is '"')
            {
                var j = i + 1;
                while (j < text.Length && text[j] is not '"')
                {
                    j += text[j] is '\\' ? 2 : 1;
                }

                found.Add(text[(i + 1)..Math.Min(j, text.Length)]);
                i = j + 1;
                continue;
            }

            if (c is '\'' or '`')
            {
                var quote = c;
                var j = i + 1;
                while (j < text.Length && text[j] != quote)
                {
                    j += text[j] is '\\' ? 2 : 1;
                }

                if (script)
                {
                    found.Add(text[(i + 1)..Math.Min(j, text.Length)]);
                }

                i = j + 1;
                continue;
            }

            if (c is '/' && i + 1 < text.Length && text[i + 1] is '/')
            {
                var end = text.IndexOf('\n', i);
                i = end < 0 ? text.Length : end;
                continue;
            }

            if (c is '/' && i + 1 < text.Length && text[i + 1] is '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>Every quoted string and here-string, skipping every comment.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="powershell">Whether the block comment and here-string forms apply.</param>
    /// <returns>One entry per literal, without its delimiters.</returns>
    private static List<string> ShellLiterals(string text, bool powershell)
    {
        var found = new List<string>();

        for (var i = 0; i < text.Length;)
        {
            var c = text[i];

            if (powershell && c is '<' && text.AsSpan(i).StartsWith("<#"))
            {
                var end = text.IndexOf("#>", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                continue;
            }

            if (powershell && c is '@' && i + 1 < text.Length && text[i + 1] is '"' or '\'')
            {
                var close = text[i + 1] is '\'' ? "'@" : "\"@";
                var end = text.IndexOf(close, i + 2, StringComparison.Ordinal);
                found.Add(text[(i + 2)..(end < 0 ? text.Length : end)]);
                i = end < 0 ? text.Length : end + 2;
                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                var j = i + 1;
                while (j < text.Length && text[j] != quote)
                {
                    j += (powershell ? text[j] is '`' : text[j] is '\\') ? 2 : 1;
                }

                found.Add(text[(i + 1)..Math.Min(j, text.Length)]);
                i = j + 1;
                continue;
            }

            if (c is '#')
            {
                var end = text.IndexOf('\n', i);
                i = end < 0 ? text.Length : end;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>Every attribute value and element body outside a comment.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>One entry per value.</returns>
    private static List<string> XmlText(string text)
    {
        var without = new StringBuilder();

        for (var i = 0; i < text.Length;)
        {
            var start = text.IndexOf("<!--", i, StringComparison.Ordinal);

            if (start < 0)
            {
                _ = without.Append(text[i..]);
                break;
            }

            _ = without.Append(text[i..start]);
            var end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            i = end < 0 ? text.Length : end + 3;
        }

        var body = without.ToString();
        var found = new List<string>();

        for (var i = body.IndexOf('"', 0); i >= 0; i = body.IndexOf('"', i + 1))
        {
            var close = body.IndexOf('"', i + 1);

            if (close < 0)
            {
                break;
            }

            found.Add(body[(i + 1)..close]);
            i = close;
        }

        foreach (var between in body.Split('>'))
        {
            var at = between.IndexOf('<', StringComparison.Ordinal);
            var inner = at < 0 ? between : between[..at];

            if (inner.Trim().Length > 0)
            {
                found.Add(inner);
            }
        }

        return found;
    }
}

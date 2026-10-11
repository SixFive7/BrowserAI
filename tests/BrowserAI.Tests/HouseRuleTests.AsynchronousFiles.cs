// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>The house rule on the files a stream is opened over: asynchronous I/O, asked for.</summary>
internal sealed partial class HouseRuleTests
{
    /// <summary>The constructor and the calls this scan reads, composed so this file is not its own offender.</summary>
    private static readonly string[] OpenerNeedles = ["new " + "FileStream(", "File." + "Create("];

    /// <summary>The options object whose initialiser this scan reads.</summary>
    private const string OptionsNeedle = "new " + "FileStreamOptions";

    /// <summary>
    /// Every <c>FileStream</c> this tree opens asks for asynchronous I/O.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's decision 63, in his words verbatim: <i>"convert disk writes as
    /// well. Some users have slow spinning rust."</i></b> <c>build/BannedSymbols.txt</c> bans
    /// every <c>FileStream</c> constructor that cannot open for asynchronous I/O and leaves
    /// the ones that take a <c>FileOptions</c>, a <c>FileStreamOptions</c> or an
    /// <c>isAsync</c>, because each of those can. Whether one DOES is an argument's value,
    /// which an analyzer cannot read, so this reads it: every <c>new FileStream(</c> and
    /// every <c>File.Create(</c> in <c>src</c> and <c>tests</c> names
    /// <c>FileOptions.Asynchronous</c>, passes <c>isAsync: true</c>, or is handed a
    /// <c>FileStreamOptions</c> whose initialiser names it. A stream opened without it serves
    /// every <c>ReadAsync</c> with a synchronous read on a pool thread, which is the
    /// starvation decision 54 bans by another name.
    /// </para>
    /// <para>
    /// <b>What it cannot see</b>: an options value built in a variable and handed over,
    /// which it reports as an offence, so a shape it cannot read stops the build instead of
    /// passing it; and <c>File.OpenHandle</c>, which this tree calls to ask a sharing
    /// question of a file it never reads, and once for the process log's synchronous sink,
    /// which is listed for review with <c>Logging/RollingFileWriter.cs</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFileStreamInTheTreeIsOpenedForAsynchronousIo()
    {
        var offenders = new List<string>();
        var opens = 0;

        foreach (var file in RepositoryLayout.SourceFilesUnder(["src", "tests"], ["*.cs"]))
        {
            var text = await File.ReadAllTextAsync(file.FullName);
            var found = SynchronousOpens(text);

            opens += found.Opens;
            offenders.AddRange(found.Offences.Select(offence => $"{Relative(file)}: {offence}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous over the tree: the conversion of 2026-10-11 left well over a
        // hundred opens behind it, and a scan that found none had stopped reading.
        await Assert.That(opens).IsGreaterThan(100);

        // ⚠️ THE CONTROLS, synthetic because a converted tree is indistinguishable from
        // a scan whose needle stopped matching. Composed from parts, as the needles are.
        var stream = "new " + "FileStream(";
        var create = "File." + "Create(";
        var options = "new " + "FileStreamOptions";

        await Assert.That(SynchronousOpens($"var s = {stream}path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);").Offences).IsEmpty();
        await Assert.That(SynchronousOpens($"var s = {stream}path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);").Offences).IsEmpty();
        await Assert.That(SynchronousOpens($"var s = {stream}handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);").Offences).IsEmpty();
        await Assert.That(SynchronousOpens($"var s = {stream}path, {options} {{ Mode = FileMode.Open, Options = FileOptions.Asynchronous }});").Offences).IsEmpty();

        await Assert.That(SynchronousOpens($"var s = {stream}path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);").Offences.Count).IsEqualTo(1);
        await Assert.That(SynchronousOpens($"var s = {stream}path, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);").Offences.Count).IsEqualTo(1);
        await Assert.That(SynchronousOpens($"var s = {stream}handle, FileAccess.ReadWrite, 0, isAsync: false);").Offences.Count).IsEqualTo(1);
        await Assert.That(SynchronousOpens($"var s = {stream}path, chosen);").Offences.Count).IsEqualTo(1);
        await Assert.That(SynchronousOpens($"var s = {create}path, 4096, FileOptions.None);").Offences.Count).IsEqualTo(1);

        // An options object that does not ask is an offence of its own, and the stream
        // it is handed to is one too: the scan reads the initialiser, not its name.
        await Assert.That(SynchronousOpens($"var s = {stream}path, {options} {{ Mode = FileMode.Open }});").Offences.Count).IsEqualTo(2);

        // And a mention in a comment or a string is not an open.
        var mentioned = SynchronousOpens($"// {stream}path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);\nvar t = \"{stream}path, x)\";\nvar u = @\"{stream}path, \"\"y\"\")\";");

        await Assert.That(mentioned.Opens).IsEqualTo(0);
        await Assert.That(mentioned.Offences).IsEmpty();
    }

    /// <summary>Every open in one file's code that does not ask for asynchronous I/O.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>How many opens it read, and one line per offence.</returns>
    private static (int Opens, IReadOnlyList<string> Offences) SynchronousOpens(string text)
    {
        var code = CodeOf(text);
        var offences = new List<string>();
        var opens = 0;

        foreach (var needle in OpenerNeedles)
        {
            for (var at = code.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = code.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                var open = at + needle.Length - 1;

                if (ClosingParenthesis(code, open) is not { } close)
                {
                    offences.Add($"'{needle}' with no closing parenthesis");
                    continue;
                }

                opens++;
                var arguments = text[(open + 1)..close];

                if (!arguments.Contains("FileOptions.Asynchronous", StringComparison.Ordinal)
                    && !arguments.Contains("isAsync: true", StringComparison.Ordinal)
                    && !arguments.Contains(OptionsNeedle, StringComparison.Ordinal))
                {
                    offences.Add($"{needle}{Shortened(arguments)}) does not ask for FileOptions.Asynchronous");
                }
            }
        }

        for (var at = code.IndexOf(OptionsNeedle, StringComparison.Ordinal); at >= 0; at = code.IndexOf(OptionsNeedle, at + OptionsNeedle.Length, StringComparison.Ordinal))
        {
            var brace = code.IndexOf('{', at);
            var end = brace < 0 ? -1 : ClosingBrace(code, brace);

            if (end < 0 || !text[brace..end].Contains("FileOptions.Asynchronous", StringComparison.Ordinal))
            {
                offences.Add($"{OptionsNeedle} does not set Options to FileOptions.Asynchronous");
            }
        }

        // An options object handed to a stream in the same call answers for it.
        var unanswered = offences.Count(offence => offence.StartsWith(OptionsNeedle, StringComparison.Ordinal));

        if (unanswered > 0)
        {
            foreach (var needle in OpenerNeedles)
            {
                for (var at = code.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = code.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
                {
                    var open = at + needle.Length - 1;

                    if (ClosingParenthesis(code, open) is { } close
                        && text[(open + 1)..close] is var arguments
                        && arguments.Contains(OptionsNeedle, StringComparison.Ordinal)
                        && !arguments.Contains("FileOptions.Asynchronous", StringComparison.Ordinal))
                    {
                        offences.Add($"{needle}{Shortened(arguments)}) is handed options that do not ask for FileOptions.Asynchronous");
                    }
                }
            }
        }

        return (opens, offences);
    }

    /// <summary>The text with every comment and the inside of every literal blanked, its offsets kept.</summary>
    /// <param name="text">A C# file's text.</param>
    /// <returns>The code, the same length as the text.</returns>
    private static string CodeOf(string text)
    {
        var code = new StringBuilder(text);

        void blank(int start, int end)
        {
            for (var i = start; i < end && i < code.Length; i++)
            {
                if (code[i] is not '\n')
                {
                    code[i] = ' ';
                }
            }
        }

        // The comments first, read once: the walk below skips what they cover.
        var comment = new bool[text.Length];

        foreach (var (start, end) in Commentary.SpansOf(text, ".cs"))
        {
            blank(start, end);

            for (var i = start; i < end && i < comment.Length; i++)
            {
                comment[i] = true;
            }
        }

        for (var i = 0; i < text.Length;)
        {
            if (comment[i])
            {
                i++;
                continue;
            }

            var c = text[i];

            if (c is '"' && text.AsSpan(i).StartsWith("\"\"\""))
            {
                var fence = 0;

                while (i + fence < text.Length && text[i + fence] is '"')
                {
                    fence++;
                }

                var close = text.IndexOf(new string('"', fence), i + fence, StringComparison.Ordinal);
                var end = close < 0 ? text.Length : close + fence;

                blank(i, end);
                i = end;
                continue;
            }

            if (c is '"' && i > 0 && text[i - 1] is '@' or '$' && text.AsSpan(Math.Max(0, i - 2), Math.Min(2, i)).Contains('@'))
            {
                // Verbatim: a doubled quote is a quote, and a backslash is a backslash.
                var j = i + 1;

                while (j < text.Length)
                {
                    if (text[j] is '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] is '"')
                        {
                            j += 2;
                            continue;
                        }

                        break;
                    }

                    j++;
                }

                var end = Math.Min(j + 1, text.Length);

                blank(i, end);
                i = end;
                continue;
            }

            if (c is '"' or '\'')
            {
                var j = i + 1;

                while (j < text.Length && text[j] != c)
                {
                    j += text[j] is '\\' ? 2 : 1;
                }

                var end = Math.Min(j + 1, text.Length);

                blank(i, end);
                i = end;
                continue;
            }

            i++;
        }

        return code.ToString();
    }

    /// <summary>The parenthesis that closes the one at a position, in code with its literals blanked.</summary>
    /// <param name="code">The code.</param>
    /// <param name="open">The opening parenthesis.</param>
    /// <returns>Its partner, or <see langword="null"/>.</returns>
    private static int? ClosingParenthesis(string code, int open)
    {
        var depth = 0;

        for (var i = open; i < code.Length; i++)
        {
            depth += code[i] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };

            if (depth is 0)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The brace that closes the one at a position, in code with its literals blanked.</summary>
    /// <param name="code">The code.</param>
    /// <param name="open">The opening brace.</param>
    /// <returns>Its partner, or -1.</returns>
    private static int ClosingBrace(string code, int open)
    {
        var depth = 0;

        for (var i = open; i < code.Length; i++)
        {
            depth += code[i] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            if (depth is 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>An argument list on one line, cut short for a failure message.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The shortened text.</returns>
    private static string Shortened(string arguments)
    {
        var flat = string.Join(' ', arguments.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(static part => part.Trim()));

        return flat.Length > 160 ? flat[..160] + "..." : flat;
    }
}

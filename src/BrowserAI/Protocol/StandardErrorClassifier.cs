// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;

namespace BrowserAI.Protocol;

/// <summary>
/// Tells an error-shaped line the child wrote to stderr from the benign output a
/// healthy start always produces.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is row 5 of the charter's opening table, not a refinement of it.</b>
/// The setup BrowserAI replaces warned on <i>any</i> stderr, and
/// <c>@playwright/mcp</c> prints a benign <c>Session: &lt;path&gt;</c> line on
/// every healthy start with session logging on — so the warning fired on every
/// good run, and a reader who has learned to ignore a warning is a reader who
/// will ignore the real one. Shipping without this does not leave a gap; it
/// reintroduces a fixed bug in the product built to stop it.
/// </para>
/// <para>
/// <b>The two patterns are ported verbatim and must stay verbatim.</b> They come
/// from <c>SixFive7/Workspace657</c>, <c>playwright/launch.ps1</c> at commit
/// <c>a9ac747</c>, and a copy of those two lines is committed beside this file as
/// <c>StandardErrorClassifier.reference.ps1</c>. This is behaviour being copied
/// deliberately, so a transcription difference is a silent behaviour change —
/// which is why <c>StandardErrorClassifierTests</c> compares the constants below
/// against that copy character by character rather than trusting the eye.
/// </para>
/// <para>
/// <b>Two groups, and the asymmetry between them is the whole design.</b> Prefix
/// words — <c>error</c>, <c>fatal</c>, <c>unknown option</c> — count only at the
/// start of a line, so prose like <i>"no errors"</i> does not trip them. Phrases
/// specific enough to be unambiguous match anywhere, because the missing-browser
/// diagnostic reports mid-sentence.
/// </para>
/// <para>
/// <b>Applied per line here, against the whole buffer there, and the two agree.</b>
/// The reference reads its captured stderr file whole; BrowserAI's pump is
/// line-oriented and already exists, so this classifies what that reader already
/// delivers rather than adding a second reader. The verdicts cannot differ: the
/// first pattern is multiline-anchored, so a line that matches within a buffer
/// matches on its own, and the second is unanchored, so it matches the same text
/// either way. <c>ATextClassifiesTheSameWholeAsItDoesLineByLine</c> is what keeps
/// that from being an argument.
/// </para>
/// <para>
/// <b>A verdict is a log level, not a session error.</b> Nothing here reaches a
/// caller: <see cref="Sessions.SessionErrors"/> is the catalogue of refusals a
/// model reads and acts on, and an error-shaped stderr line names no tool, no
/// recovery and often no session — the child that writes <c>error: unknown
/// option</c> dies before any session exists. It belongs in the log, at a level a
/// human tailing it would notice, which is exactly what the reference does with
/// its own <c>WARNING</c> verdict.
/// </para>
/// </remarks>
internal static partial class StandardErrorClassifier
{
    /// <summary>
    /// Prefix words, at the start of a line only. Verbatim from the reference
    /// implementation; see the type's remarks before changing a character.
    /// </summary>
    public const string ErrorPrefixPattern = @"(?im)^\s*(error\b|fatal\b|unknown option)";

    /// <summary>
    /// Phrases unambiguous enough to count anywhere in a line. Verbatim from the
    /// reference implementation; see the type's remarks before changing a
    /// character.
    /// </summary>
    public const string ErrorPhrasePattern = @"(?i)(is not installed|cannot find|ENOENT|EACCES)";

    /// <summary>Whether a child's stderr output looks like a diagnostic.</summary>
    /// <param name="text">One stderr line, or a whole captured buffer.</param>
    /// <returns>
    /// <see langword="true"/> when the text is error-shaped; <see langword="false"/>
    /// for the benign output a healthy start produces, and for nothing at all.
    /// </returns>
    public static bool LooksLikeError(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (ErrorPrefix().IsMatch(text) || ErrorPhrase().IsMatch(text));

    [GeneratedRegex(ErrorPrefixPattern)]
    private static partial Regex ErrorPrefix();

    [GeneratedRegex(ErrorPhrasePattern)]
    private static partial Regex ErrorPhrase();
}

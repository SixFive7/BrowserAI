// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// This repository's <c>(previously "…")</c> clause, and the one definition of
/// it the document gates share.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>CLAUDE.md</c> calls the clause the load-bearing half of a
/// correction</b> — it is what tells a reader who learned the old value that it
/// was reviewed and replaced rather than lost, and it requires the superseded
/// text to be quoted <i>verbatim</i>. Every gate that reads a document for
/// claims therefore has to read around it, because the quoted text is a record
/// of what a row used to say and not a claim that it still says it.
/// </para>
/// <para>
/// ⚠️ <b>It lives here because two gates read the same clause and only one of
/// them knew about it.</b> <c>HazardIndexTests</c> stripped it before resolving
/// the symbols a row names; <c>ReVerificationIndexTests</c> did not, so a
/// superseded test name quoted in backticks failed that gate while the identical
/// correction passed the other. The visible cost was in the document rather than
/// in the build: two rows of <c>kb/re-verification.md</c> ended up quoting dead
/// test names <i>without</i> backticks and explaining in prose that the gate was
/// the reason. **Two conventions that cannot both hold is a defect in one of
/// them**, and the convention wins — so the reading moved here and both gates
/// ask it the same question.
/// </para>
/// <para>
/// <b>What it deliberately does not match.</b> A <c>previously</c> followed by
/// anything other than a quotation — <c>previously three arms —</c>,
/// <c>previously `Foo.Bar`, which was deleted</c> — is prose rather than the
/// convention, and widening the pattern to cover it would strip live claims out
/// of any cell that happened to use the word. A correction that wants the
/// clause's protection writes the clause.
/// </para>
/// </remarks>
internal static partial class CorrectionClause
{
    /// <summary>Removes every <c>previously "…"</c> clause from a document cell.</summary>
    /// <param name="text">The cell, corrections and all.</param>
    /// <returns>What the cell claims now.</returns>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Pattern().Replace(text, string.Empty);
    }

    /// <summary>Whether a cell carries the clause at all.</summary>
    /// <param name="text">The cell.</param>
    /// <returns>Whether a <c>previously "…"</c> clause is in it.</returns>
    public static bool IsCarriedBy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Pattern().IsMatch(text);
    }

    /// <summary>
    /// The <c>(previously "…")</c> half of this repository's correction
    /// convention, which quotes the superseded text verbatim.
    /// </summary>
    [GeneratedRegex(@"previously\s*""[^""]*""")]
    private static partial Regex Pattern();
}

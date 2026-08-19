// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What <c>browserai_destroy</c> actually promises about the tree it was aimed
/// at, written once so that two tests cannot hold it to two different contracts.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>It does not promise the tree is gone.</b> Windows will not unlink a file
/// a browser is still mapping, and the release lags the process by however long
/// the kernel takes — so <c>destroy</c> returns <c>isError: false</c> in two
/// shapes: everything went, or <i>"BUT N item(s) could not be removed"</i>
/// followed by the list. <b>What it promises is that the answer and the disk
/// agree</b>, and that is the property this type asserts.
/// </para>
/// <para>
/// <b>Written 2026-08-19, after <c>FirefoxSessionTests</c> asserted
/// <c>Directory.Exists(session)</c> was <see langword="false"/> and failed three
/// consecutive CI runs on a four-core runner while passing nine local ones.</b>
/// A survivor is the ordinary case against Firefox on a slow machine and the
/// unlucky one on a fast machine, which is exactly the shape that gets a retry
/// rather than a reader.
/// </para>
/// <para>
/// <b>The heading, the cap and the truncation note are read from
/// <see cref="SessionManager"/> rather than re-typed.</b> A test carrying its own
/// copy of the product's prose stops recognising the arm the day somebody rewords
/// it — and then passes, by never reaching the assertions underneath, which is
/// the green-when-broken failure this suite exists to eliminate.
/// </para>
/// </remarks>
internal static class DestroyAnswer
{
    /// <summary>What <see cref="TreeDeleteIndent"/> exists for.</summary>
    /// <remarks>
    /// <c>TreeDelete</c> indents every node it names by two spaces, and the
    /// sentence after the list starts at column zero. That is what ends the
    /// listing, and it is why nothing here has to parse a Windows path out of a
    /// line that also contains a drive letter and an OS error message.
    /// </remarks>
    private const string TreeDeleteIndent = "  ";

    /// <summary>
    /// Holds one <c>browserai_destroy</c> answer to what it promised about the
    /// directory it was given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The agreement is asserted in both directions.</b> A destroy that left
    /// the tree standing and said nothing fails — that is the property that
    /// matters, and the only one the old <c>Directory.Exists</c> assertion could
    /// see. So does one that <i>reported</i> survivors it does not have, one
    /// whose survivor list is a tally with nothing named under it, and one that
    /// names a path outside the directory it was aimed at.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not assert is that the tree becomes
    /// deletable.</b> That is a property of the browser's teardown rather than of
    /// this tool, it needs a bounded wait, and a caller that wants it has
    /// <see cref="ScratchDirectory.RemoveTreeWhenReleasedAsync"/>.
    /// </para>
    /// </remarks>
    /// <param name="answer">The whole text of the answer.</param>
    /// <param name="session">The session directory the call was given.</param>
    /// <returns>The assertion task.</returns>
    public static async Task AccountsForWhatItLeftAsync(string answer, string session)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(session);

        var survivors = SurvivorsNamedIn(answer);
        var leftBehind = Directory.Exists(session);

        await Assert.That(survivors is not null)
            .IsEqualTo(leftBehind)
            .Because(
                $"'{SessionToolSurface.Destroy}' must account for what it left behind: '{session}' {(leftBehind ? "still exists" : "is gone")} and the answer {(survivors is null ? "named no survivors" : "named survivors")}.\n\n{answer}");

        if (survivors is { } named)
        {
            // Named USEFULLY. A tally, one line per item up to the cap, and every
            // one of them inside the directory this call was aimed at -- a
            // survivor named outside it would mean the walk left the tree it was
            // given, which is the failure TreeDelete's reparse-point check
            // exists against.
            await Assert.That(named.Stated).IsGreaterThan(0).Because(answer);
            await Assert.That(named.Listed.Count)
                .IsEqualTo(Math.Min(named.Stated, SessionManager.SurvivorsNamed))
                .Because(answer);

            // ⚠️ AND IT SAYS SO WHEN IT CUT THE LIST. Added 2026-08-19: the two
            // assertions above were satisfied by an answer that stated 25 and
            // printed twenty lines with nothing anywhere saying the list had been
            // cut, which is a complete list to any reader who does not do the
            // arithmetic -- and this answer is written for a model. Asserted in
            // both directions, because a note printed on an uncut list is the
            // same defect wearing the other sign: it would tell a caller that
            // items exist which do not.
            await Assert.That(answer.Contains(SessionManager.TruncationNote(named.Stated), StringComparison.Ordinal))
                .IsEqualTo(named.Stated > SessionManager.SurvivorsNamed)
                .Because(answer);

            var elsewhere = named.Listed.Where(line => !line.StartsWith(session, StringComparison.OrdinalIgnoreCase));

            await Assert.That(string.Join(Environment.NewLine, elsewhere)).IsEmpty().Because(answer);
        }

        // Both arms promise this and only one of them says it out loud: the
        // survivor arm's own sentence is "The session's record is gone, so the
        // directory is no longer a session". A lock.json that outlived the answer
        // saying so leaves a directory the next resume would take.
        await Assert.That(File.Exists(Path.Combine(session, SessionLayout.LockFileName)))
            .IsFalse()
            .Because($"'{SessionToolSurface.Destroy}' says the record is gone.\n\n{answer}");
    }

    /// <summary>
    /// The survivor paragraph out of an answer, or <see langword="null"/> when it
    /// made no such claim.
    /// </summary>
    /// <remarks>
    /// The tally is read out of the sentence rather than assumed, so a count and
    /// a listing that disagree is a failure rather than a detail.
    /// </remarks>
    /// <param name="answer">The whole text of the answer.</param>
    /// <returns>The tally it stated and the nodes it named, unindented.</returns>
    public static (int Stated, IReadOnlyList<string> Listed)? SurvivorsNamedIn(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var lines = answer.Split('\n');
        var heading = Array.FindIndex(lines, line => line.Contains(SessionManager.SurvivorsHeading, StringComparison.Ordinal));

        if (heading < 0)
        {
            return null;
        }

        var stated = lines[heading]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (int?)null)
            .FirstOrDefault(value => value is not null);

        var listed = lines[(heading + 1)..]
            .TakeWhile(line => line.StartsWith(TreeDeleteIndent, StringComparison.Ordinal))
            .Select(line => line[TreeDeleteIndent.Length..])
            .ToList();

        // A heading with no number in it is a survivor arm that never says how
        // many, which the caller must fail on rather than skip.
        return (stated ?? 0, listed);
    }
}

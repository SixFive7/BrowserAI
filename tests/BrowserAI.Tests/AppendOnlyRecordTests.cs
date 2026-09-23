// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// The dated records -- released <c>CHANGELOG.md</c> sections and the bodies
/// under <c>docs/reviews/</c> -- still begin with what they said when they were
/// sealed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this exists for happened.</b> The <c>lock.json</c> →
/// <c>browserai.json</c> rename of 2026-08-20 swept the whole tree, reached
/// <c>docs/reviews/</c> and <c>CHANGELOG.md</c>'s released sections, and rewrote
/// history: a 2026-08-18 review came out claiming a filename that did not exist
/// for another two days. Nothing failed. It was caught by a human reading the
/// diff, and the only thing standing between the next sweep and the same outcome
/// was the sentence in
/// [the reviews' own README](../../docs/reviews/README.md) -- <i>"they are dated
/// records of what was true when they were written, and rewriting one would
/// destroy the only account of the reasoning"</i>. Prose does not stop a
/// find-and-replace.
/// </para>
/// <para>
/// <b>Append-only, deliberately, and not frozen.</b> A blanket no-edit rule
/// would be the wrong mechanism: a typo fix in a review is legitimate, and the
/// review index's status table in <c>docs/reviews/README.md</c> is explicitly
/// meant to be updated as findings are acted on -- so <c>README.md</c> is not a
/// dated record and is not sealed. What is sealed is the <b>prefix</b>: the
/// characters a record already had. Appending an addendum passes; rewriting a
/// sentence in the middle does not, and neither does truncating one.
/// </para>
/// <para>
/// ⚠️ <b>EVERY SEAL IN THE LIST BELOW MOVED ONCE, ON 2026-09-23, AND THE
/// GRANT THAT MOVED THEM IS QUOTED HERE IN FULL, NOT SUMMARISED.</b> The
/// maintainer, verbatim: <i>"When it comes to no semantic differences and only
/// removing traces of AI (both in wording and character use) then I hereby grant
/// and instruct you the right and instruction to edit sealed documents."</i>
/// What was done under it is one thing and only one: the five characters a
/// person does not type -- em and en dashes, ellipsis characters, curly quotes
/// and a non-breaking space -- were replaced with their ASCII forms in every
/// record. <b>No fact, number, date, name, claim or quotation of somebody else's
/// text moved</b>, and the seals were re-recorded in the same commit, which is
/// what separates this from the 2026-08-20 rename sweep that this class exists
/// to stop: that one was silent and nobody chose it. <b>The grant is narrow.</b>
/// It is not a licence to edit a record for any other reason, and a second
/// purpose would be a new grant, not a precedent.
/// </para>
/// <para>
/// ⚠️ <b>AND ALL ELEVEN MOVED A SECOND TIME, ON THE SAME DAY, FOR THE
/// WORDING HALF OF THE SAME DIRECTIVE, UNDER THE SAME GRANT</b> -- quoted again
/// because a reader meeting one of these numbers should not have to go looking:
/// <i>"When it comes to no semantic differences and only removing traces of AI
/// (both in wording and character use) then I hereby grant and instruct you the
/// right and instruction to edit sealed documents."</i> What moved was stock
/// phrasing: the <c>X rather than Y</c> frame, imperative <c>Note that</c>
/// lead-ins, and in the changelog 103 detail sentences that repeated their own
/// headline. <b>No fact, number, date, name, claim or quotation moved.</b>
/// </para>
/// <para>
/// ⚠️ <b>HARVEST EVERY REPLACEMENT SEAL BY PARKING EVERY SEAL, NEVER BY
/// COLLECTING THE COMPLAINTS.</b> A record that got SHORTER takes
/// <see cref="Explain"/>'s TRUNCATED branch, which prints no replacement line at
/// all -- so a pass that greps the failures for <c>new(...)</c> silently misses
/// exactly the records a wording sweep produces. Setting every recorded length
/// to 1 first makes all of them take the reseal branch. On 2026-09-23 that was
/// the difference between eleven seals and the five a shard had counted.
/// </para>
/// <para>
/// <b>A deliberate edit is possible and is never silent.</b> The seal carries a
/// character count and a SHA-256, and changing a sealed record means changing
/// the numbers here in the same commit -- which is a line in the diff, aimed at
/// exactly the failure mode above, where the sweep's own diff was the only
/// witness. This is the same trade <c>upstream-review.json</c> takes, and the
/// same warning applies: <b>re-sealing a record to make this test pass is
/// rewriting history with an extra step.</b> The failure message prints the
/// replacement seal because refusing to would only mean it was computed by hand;
/// what it cannot do is decide whether the edit was a typo fix or a sweep.
/// </para>
/// <para>
/// <b>Why a seal and not the file's git history.</b> <c>git log --numstat</c>
/// would say for free whether a file has ever had a line deleted, and it was the
/// first design. It fails on both halves: a legitimate typo fix deletes a line,
/// and the changelog's protection is per-<i>section</i> -- its
/// <c>[Unreleased]</c> section is rewritten daily and is not a record of
/// anything yet -- which no whole-file history check can express.
/// </para>
/// </remarks>
internal sealed partial class AppendOnlyRecordTests
{
    /// <summary>
    /// One dated record, and what it said when it was sealed.
    /// </summary>
    /// <param name="Record">
    /// The record's key: a repository-relative path, or
    /// <c>CHANGELOG.md#&lt;version&gt;</c> for one released section of it.
    /// </param>
    /// <param name="Characters">
    /// How many characters were sealed, with line endings normalised to
    /// <c>\n</c>. Published and not derived, so that a truncation is an
    /// arithmetic mismatch a reader can see, and not only a digest that differs.
    /// </param>
    /// <param name="Sha256">The digest of those characters, lower-case hex.</param>
    /// <param name="BodySha256">
    /// The digest of the same characters <b>without their first line</b>.
    /// </param>
    /// <remarks>
    /// %s <b>The second digest exists to tell one failure apart from every
    /// other, and that failure is a DATE -- 2026-09-16.</b> A sealed record
    /// starts at its heading, so <c>## [1.0.0] - 2026-09-15</c> is inside the
    /// prefix: changing the release date at the cut breaks the seal, and the
    /// failure message read <i>REWRITTEN -- a dated record says what was true
    /// when it was written</i>, which is exactly the wrong advice for the one
    /// edit the checklist requires. With the body digest the test can say
    /// <b>only the heading line moved</b>, which is a different instruction.
    /// </remarks>
    private readonly record struct Seal(string Record, int Characters, string Sha256, string BodySha256);

    /// <summary>
    /// Every dated record in the tree, sealed 2026-08-20 at the state the
    /// rename sweep was reverted to, and each later record sealed in the change
    /// that added it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both 2026-08-24 reviews were sealed on the day each was copied in</b>,
    /// which is what the second arm of this class exists to force: a record added
    /// without a seal is one nothing would notice being rewritten, and the newest
    /// record is the likeliest to be the one nobody registered.
    /// </para>
    /// <para>
    /// ⚠️ <b>A record is edited before it is sealed or not at all, and the
    /// narrow re-review is the case that proves the ordering matters.</b> It was
    /// written in <c>.work/</c> with one upstream pointer shape spelled as a
    /// Markdown link inside a code span; <c>DocumentationLinkTests</c> reads raw
    /// text and does not skip code spans, so registering it verbatim would have
    /// added a relative link into <c>docs/reviews/</c> that resolves to nothing.
    /// The span was rewritten, the change is named in the record's own opening
    /// note, and the seal was taken afterwards -- which is the only order that
    /// does not mean re-sealing a record to make a test pass.
    /// </para>
    /// <para>
    /// <b>The 2026-08-26 post-course-correction review is what that lesson looks
    /// like once it has been learned</b>: it was written in <c>.work/</c> with
    /// <b>zero</b> Markdown links, deliberately, so its link shapes were final
    /// before it moved and it was registered byte-identical to what was handed
    /// over. It carries no editorial note because there was no editorial change.
    /// </para>
    /// <para>
    /// ⚠️ <b>BOTH CHANGELOG SEALS WERE LIFTED ONCE, ON 2026-09-15, AT THE
    /// MAINTAINER'S EXPLICIT INSTRUCTION (Q193), AND RE-TAKEN AT THE END OF THE
    /// SAME BATCH.</b> <c>Corrected 2026-09-15 (previously sealed at
    /// <c>CHANGELOG.md#1.0.0</c> 236,567 characters / <c>c8163746...</c> and
    /// <c>CHANGELOG.md#0.1.0</c> 3,850 characters / <c>4e939d92...</c>)</c>. Every
    /// entry in both sections was re-shaped into
    /// <c>- &lt;icon&gt; **Headline.** &lt;the rest&gt;</c> for the 1.0.0 re-ship, and
    /// the entries that had accumulated under <c>[Unreleased]</c> were merged
    /// into 1.0.0's groups, so the prefix of each record moved by construction.
    /// </para>
    /// <para>
    /// ⚠️ <b>AND AGAIN ON 2026-09-16, FOR THE SECOND CUT OF THE SAME
    /// VERSION.</b> <i>Corrected 2026-09-16 (previously sealed at
    /// <c>CHANGELOG.md#1.0.0</c> <c>281,709</c> characters /
    /// <c>6ac6a8b6...</c> / <c>1cc93037...</c>)</i>. <c>1.0.0</c> is re-shipped in
    /// place on 2026-09-16 at the maintainer's instruction, so the same two
    /// edits the re-ship case requires were taken again and in one commit: the
    /// <b>heading date</b> moved <c>2026-09-15</c> → <c>2026-09-16</c>, which is
    /// inside the sealed prefix by construction, and the <b>twenty-two entries</b>
    /// that had accumulated under <c>[Unreleased]</c> since the first cut -- three
    /// <c>Added</c>, one <c>Changed</c>, eighteen <c>Fixed</c> -- were merged into the
    /// matching <c>1.0.0</c> groups. The record grew <c>281,709</c> →
    /// <c>310,215</c> characters, which is an append at the end of each group
    /// and a one-line change at the top, and the whole prefix moves because a
    /// seal starts at the heading.
    /// </para>
    /// <para>
    /// ⚠️ <b>AND A THIRD TIME ON 2026-09-17, FOR THE THIRD IN-PLACE CUT OF
    /// THE SAME VERSION, AND FOR ONE EDIT ONLY.</b> <i>Corrected 2026-09-17
    /// (previously sealed at <c>CHANGELOG.md#1.0.0</c> <c>310,215</c> characters
    /// / <c>c2dd7dde...</c> / <c>79e46755...</c>)</i>. The order is the
    /// maintainer's, in writing and in advance: <i>"Let's work out this and the
    /// other open issues and then re-release v1.0.0 ... the intro text of the
    /// release post is very much reading like AI. ... add to the release rules
    /// the following directive: 'Ensure there is no trace of AI both in wording
    /// and character use.'"</i> The <b>preamble</b> is rewritten in plain words
    /// and nothing else in the section is touched: no entry, no heading, no
    /// date. The whole prefix moves because a seal starts at the heading.
    /// <b>No fact was rewritten</b> -- the new preamble says the same four things
    /// the old one did, in shorter sentences and without the three em dashes
    /// that made
    /// <see cref="ChangelogTests.NothingThatReachesAReleaseBodyCarriesACharacterAPersonWouldNotType"/>
    /// red.
    /// </para>
    /// <para>
    /// ⚠️ <b>AND A FOURTH TIME ON 2026-09-17, LATER THE SAME DAY, FOR THE
    /// FOURTH IN-PLACE CUT OF THE SAME VERSION.</b> <i>Corrected 2026-09-17
    /// (previously sealed at <c>CHANGELOG.md#1.0.0</c> <c>310,216</c> characters
    /// / <c>06336d69...</c> / <c>47751897...</c>, which was that morning's preamble
    /// rewrite)</i>. The order is the maintainer's and is the same one: <i>"Let's
    /// work out this and the other open issues and then re-release v1.0.0.
    /// Because I very much like the way the changelog is reading now."</i>, with
    /// <i>"keep moving the release tag until everything is fully green and we
    /// have a full build and release"</i> standing behind it. <b>Two edits and
    /// no others</b>, exactly as the re-ship case requires and in one commit: the
    /// <b>heading date</b> moved <c>2026-09-16</c> → <c>2026-09-17</c>, which is
    /// inside the sealed prefix by construction, and the <b>twenty-eight
    /// entries</b> that had accumulated under <c>[Unreleased]</c> since that
    /// morning -- six <c>Added</c>, sixteen <c>Changed</c>, one <c>Removed</c>,
    /// five <c>Fixed</c> -- were merged into the matching <c>1.0.0</c> groups.
    /// The record grew <c>310,216</c> → <c>349,897</c> characters, which is an
    /// append at the end of each group and a one-line change at the top; the
    /// whole prefix moves because a seal starts at the heading. <b>No entry was
    /// re-shaped and no sentence of the existing body was rewritten.</b>
    /// </para>
    /// <para>
    /// <b>Three lifts is not a rule that lifts are free; it is the same version
    /// being cut a third time.</b> Each has been ordered in advance by the
    /// person who owns the record, and each has been narrower than the one
    /// before: entries re-shaped, then entries merged, then one paragraph
    /// rewritten. The way to need none is the ordinary case -- a NEW version,
    /// stamped by <c>Get-ReleaseNotes.ps1</c>, whose section nobody has sealed.
    /// </para>
    /// <para>
    /// <b>This is a SECOND lift, not a precedent that lifts are routine.</b> It
    /// has the same authority as the first -- the maintainer's instruction to
    /// re-ship <c>1.0.0</c> so that it carries the fix -- and it is narrower: no
    /// entry was re-shaped and no sentence of the 2026-09-15 body was rewritten.
    /// <b>A third one is another decision and belongs to whoever owns the
    /// rule.</b> What would make it unnecessary is the ordinary case: a NEW
    /// version, stamped by <c>Get-ReleaseNotes.ps1</c>, whose section nobody has
    /// sealed yet.
    /// </para>
    /// <para>
    /// <b>What the lift does NOT mean, said here because this is where somebody
    /// will read it next time.</b> The rule is unchanged and the warning above
    /// stands: <i>re-sealing a record to make this test pass is rewriting
    /// history with an extra step.</i> What made this one legitimate is that a
    /// human ordered the re-shaping in advance, in writing, and that <b>no fact
    /// was rewritten</b> -- where a headline is new, the sentence it replaces is
    /// the first thing in its own detail, word for word, and that was checked
    /// over all 236 entries mechanically, not by reading. <b>A second
    /// lift is not a precedent; it is another decision, and it belongs to
    /// whoever owns the rule.</b>
    /// </para>
    /// <para>
    /// <b>The changelog carries two released sections</b>, and the
    /// <c>[Unreleased]</c> section is deliberately not in the list: it is not a
    /// record of what shipped until a release stamps it. <i>Corrected 2026-09-15
    /// (previously "The changelog carries exactly one released section today,
    /// and the <c>[Unreleased]</c> section -- 143,210 characters of it on the day
    /// this was written -- is deliberately not in the list.")</i> -- the 1.0.0 cut
    /// of 2026-09-15 stamped 236,567 characters of unreleased work into a
    /// released section and sealed it in the same commit, which is the ordering
    /// [the release checklist](../../RELEASING.md#10-the-changelogs-unreleased-section-is-not-empty)
    /// requires and the reason that section is now the largest record here by
    /// two orders of magnitude.
    /// </para>
    /// </remarks>
    private static readonly Seal[] Sealed =
    [
        new("CHANGELOG.md#0.1.0", 3691, "06cc0bf329ac09ccefe767e0f967a7a8a123f94693be8d044189d7e9a262efa7", "bb565ea0ffbac9ba51823dd1c3737b263bd1e0453c86d2b38ed8fa7b271c2a39"),
        new("CHANGELOG.md#1.0.0", 342298, "6c7835b29858a31cc2daef7e4549aea62e3816e81399d64e80568e0882fb1cae", "cf24f4aa0bed803a15f2504dc83b179c77568802a8fd98261bf183ec9bb7a520"),
        new("CHANGELOG.md#1.1.0", 65555, "9aacb1bf3421013dafae5ee5a8624ee7e2b1460836b058a43a8ff681b62ee63c", "7d81b6e9a41df62a3258146255c56ecfb8af9685e78feec1c5f1158b9e1784e5"),
        new("docs/reviews/2026-08-18-adversarial-locking.md", 39697, "0b7cdccb8d7784118eabe52b86894d10b292c078ed75545ca1adec10d5a0d43b", "9bf8d7cae9df67b961bb7079d327e573c0c8ad994646e7406c1cd604323cfe81"),
        new("docs/reviews/2026-08-18-adversarial-processes.md", 28565, "19a20cc649fe8bbe28099c206bcfe2736838e8abbeef1b50ce6ec5cd1398b995", "b37235076bc2c506e539a41bc0c43e703ad58b57d637b63b22ad7727bcd28cd1"),
        new("docs/reviews/2026-08-18-truncation-findings.md", 14209, "ca661b3f8e57cf219598035fc8bcb4368fab16c37904f9589194aad2d1d7dd93", "ffa615e2ae80a9611439819df97a6abfd28fec19279df7a59330f1729cd2d5df"),
        new("docs/reviews/2026-08-18-truncation-prompt-for-sibling-project.md", 17241, "39b1cb434f655222e2148a1157e224b31bde953423b8802d0695c1c3046552a2", "4760ff6130253c39c89ea814e6bee37ee6f0576949d779140a3e32366aa25d91"),
        new("docs/reviews/2026-08-19-auth-transfer-and-session-modes.md", 11038, "b869118fabf6c940ddc667c3ce53ccd250d487d929b2dc9a5cb4c4af2d1756e0", "29481805fa92aec981b59f66520e93ec7d636100771ec1b7b8e4f0b6f9eabc09"),
        new("docs/reviews/2026-08-24-adversarial-narrow-since-the-six-fixes.md", 29866, "1b94021cfb026d7d9888ec02c864b5106730779806db31eb5b0b9513a44a435c", "ab874d409a384e7c497fba5ff2503601b63dc9808720d6980894054aa535bb1c"),
        new("docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md", 32400, "9d114b0932bb163acfaea229b2cdcccc6ec03312e5829e141ac4c9e5edcc8354", "01c4800add58d57b0d2cb0f8330fb706c48c3ae3936a37b640bc4f5bc4b6213a"),
        new("docs/reviews/2026-08-26-post-course-correction.md", 39984, "f273c440064a0f263ca1d30b14398e878cb1dd4a53c9382541af7387aa308643", "957ca1dc289e00b9ab2457d6249b19ee7b77baa221cd29a9dc0cac57d6e715ce"),
    ];

    /// <summary>
    /// Every sealed record still starts with the characters it was sealed on.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryDatedRecordStillStartsWithWhatItSaidWhenItWasSealed()
    {
        var records = await RecordsAsync();
        var broken = new List<string>();

        foreach (var seal in Sealed)
        {
            if (!records.TryGetValue(seal.Record, out var text))
            {
                broken.Add($"{seal.Record}: sealed, and no longer in the tree at all -- a dated record was deleted or its heading was renamed");
                continue;
            }

            if (Explain(seal, text) is { } complaint)
            {
                broken.Add(complaint);
            }
        }

        await Assert.That(string.Join(Environment.NewLine, broken)).IsEmpty();
    }

    /// <summary>
    /// What is wrong with one record, or <see langword="null"/> when nothing is.
    /// </summary>
    /// <remarks>
    /// <b>Separate from the loop so that the message itself can be asserted.</b>
    /// The tree can only ever be in the passing state, so the only way to hold
    /// this to anything is to hand it text nobody committed.
    /// </remarks>
    /// <param name="seal">What the record said when it was sealed.</param>
    /// <param name="text">What it says now, line endings already normalised.</param>
    /// <returns>The complaint, or <see langword="null"/>.</returns>
    private static string? Explain(Seal seal, string text)
    {
        if (text.Length < seal.Characters)
        {
            return $"{seal.Record}: TRUNCATED -- {text.Length} characters where {seal.Characters} were sealed. "
                + "A dated record does not get shorter; something removed part of the account.";
        }

        var prefix = text[..seal.Characters];

        if (string.Equals(Digest(prefix), seal.Sha256, StringComparison.Ordinal))
        {
            return null;
        }

        var reseal =
            $"new(\"{seal.Record}\", {text.Length.ToString(CultureInfo.InvariantCulture)}, \"{Digest(text)}\", \"{Digest(Body(text))}\")";

        // ⚠️ THE HEADING LINE, ON ITS OWN. A sealed record starts at its
        // heading, so a changelog section's DATE is inside the prefix -- and
        // changing that date at the cut is a step the release checklist
        // REQUIRES. Told apart from a rewrite by the body digest, because the
        // advice is opposite: one is "revert it", the other is "re-seal it, in
        // the same commit".
        if (string.Equals(Digest(Body(prefix)), seal.BodySha256, StringComparison.Ordinal))
        {
            return $"{seal.Record}: the HEADING LINE changed and nothing else did -- now '{FirstLine(prefix)}'. "
                + "If this is a release date being set at the cut, that is expected: the heading is inside the sealed prefix, "
                + "so the date change and the re-seal are ONE commit and this is the other half of it. Re-seal it here: "
                + reseal;
        }

        return $"{seal.Record}: REWRITTEN -- the first {seal.Characters} characters are no longer what they were. "
            + "If a sweep did this, revert it: a dated record says what was true when it was written. "
            + $"If the edit was deliberate, re-seal it here: {reseal}";
    }

    /// <summary>Some text without its first line.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Everything after the first newline, or nothing.</returns>
    private static string Body(string text)
    {
        var breakAt = text.IndexOf('\n', StringComparison.Ordinal);

        return breakAt < 0 ? string.Empty : text[(breakAt + 1)..];
    }

    /// <summary>The first line of some text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Everything before the first newline.</returns>
    private static string FirstLine(string text)
    {
        var breakAt = text.IndexOf('\n', StringComparison.Ordinal);

        return breakAt < 0 ? text : text[..breakAt];
    }

    /// <summary>
    /// A heading line that moved on its own is reported as a heading and not as
    /// a rewrite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The failure this exists for is a DATE, and it is a step the
    /// release checklist requires.</b> A sealed record starts at its heading, so
    /// <c>## [1.0.0] - 2026-09-17</c> is inside the 349,897 sealed characters
    /// -- <i>corrected 2026-09-17 (previously "<c>## [1.0.0] - 2026-09-16</c> is
    /// inside the 310,215 sealed characters")</i>, by the fourth cut of the same
    /// version doing it again -- <i>corrected 2026-09-16 (previously
    /// "<c>## [1.0.0] - 2026-09-15</c> is inside the 281,709 sealed
    /// characters")</i>, by the second cut of the same
    /// version doing exactly what this paragraph describes:
    /// setting the real release date at the cut breaks the seal, and the message
    /// used to say <i>REWRITTEN ... a dated record says what was true when it was
    /// written</i>. That is the right sentence for a sweep and exactly the wrong
    /// one here -- it reads as <i>revert this</i> for the one edit that must be
    /// made. <i>Added 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>Over doctored text, because the tree can only ever be in the passing
    /// state.</b> The real 1.0.0 seal is taken and its heading is replaced with
    /// a different date, which is precisely what the cut does.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADateSetAtTheCutIsReportedAsAHeadingRatherThanAsARewrite()
    {
        const char TestNewline = (char)10;

        var records = await RecordsAsync();
        var seal = Sealed.Single(entry => string.Equals(entry.Record, "CHANGELOG.md#1.0.0", StringComparison.Ordinal));
        var real = records[seal.Record];

        // The control: untouched, nothing is wrong with it.
        await Assert.That(Explain(seal, real)).IsNull();

        // The date at the cut: the heading line, and nothing else.
        //
        // ⚠️ THE DOCTORED DATE IS DERIVED FROM THE RECORD, NEVER TYPED. This
        // doctored with the literal "2026-09-16" until 2026-09-16, when the
        // second cut of 1.0.0 set exactly that date on the real heading -- so
        // the doctored copy became BYTE-IDENTICAL to its subject, Explain
        // returned null, and a control that cannot differ from what it is
        // testing proves nothing. Watched red in that shape on the day, and the
        // last character is flipped instead so that the two can never agree
        // again whatever date the next cut carries.
        var realHeading = FirstLine(real);
        var otherHeading = realHeading[^1] == '1' ? realHeading[..^1] + '2' : realHeading[..^1] + '1';
        var redated = otherHeading + real[real.IndexOf(TestNewline, StringComparison.Ordinal)..];
        var heading = Explain(seal, redated);

        await Assert.That(otherHeading).IsNotEqualTo(realHeading);
        await Assert.That(heading).IsNotNull();
        await Assert.That(heading!).Contains("HEADING LINE");
        await Assert.That(heading).Contains(otherHeading[(otherHeading.LastIndexOf(' ') + 1)..]);
        await Assert.That(heading).Contains("ONE commit");
        await Assert.That(heading).Contains("Re-seal it here");

        // And it is NOT the sweep's sentence, which says revert.
        await Assert.That(heading.Contains("REWRITTEN", StringComparison.Ordinal)).IsFalse();

        // The other direction: a body edit under an untouched heading is still
        // a rewrite, so the branch above is about the heading and not about
        // every mismatch.
        var body = real[..2000] + "x" + real[2001..];
        var rewritten = Explain(seal, body);

        await Assert.That(rewritten).IsNotNull();
        await Assert.That(rewritten!).Contains("REWRITTEN");
        await Assert.That(rewritten.Contains("HEADING LINE", StringComparison.Ordinal)).IsFalse();

        // And a shorter record is still a truncation, whatever its heading says.
        var cut = Explain(seal, real[..(seal.Characters - 1)]);

        await Assert.That(cut).IsNotNull();
        await Assert.That(cut!).Contains("TRUNCATED");
    }

    /// <summary>
    /// Every dated record in the tree is sealed, and nothing sealed has
    /// vanished.
    /// </summary>
    /// <remarks>
    /// Without this the mechanism protects only what somebody remembered to
    /// list, and the newest review -- the one a sweep is most likely to be run
    /// beside -- would be the one thing it did not cover. A new review or a newly
    /// stamped release is registered here in the same change that creates it;
    /// [the release checklist](../../RELEASING.md) says so at the step that stamps
    /// the version.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryDatedRecordIsSealedAndNothingSealedHasVanished()
    {
        var records = await RecordsAsync();
        var sealedKeys = Sealed.Select(entry => entry.Record).ToHashSet(StringComparer.Ordinal);
        var unsealed = records.Keys
            .Where(key => !sealedKeys.Contains(key))
            .Order(StringComparer.Ordinal)
            .Select(key => $"{key}: a dated record with no seal, so nothing would notice it being rewritten. Add it to {nameof(Sealed)}.")
            .ToList();

        var vanished = sealedKeys
            .Where(key => !records.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .Select(key => $"{key}: sealed and not in the tree -- deleted, renamed, or its changelog heading changed.")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, unsealed.Concat(vanished))).IsEmpty();

        // Not vacuous: the scan really did find the two kinds of record. A
        // regex that stopped matching changelog headings would otherwise report
        // a clean tree by looking at nothing.
        await Assert.That(records.Keys.Any(key => key.StartsWith("CHANGELOG.md#", StringComparison.Ordinal))).IsTrue();
        await Assert.That(records.Keys.Count(key => key.StartsWith("docs/reviews/", StringComparison.Ordinal))).IsGreaterThan(1);

        // And the index itself is NOT one of them. It carries the status table,
        // which is meant to be updated as findings are acted on -- sealing it
        // would make the one document that has to change the one that cannot.
        await Assert.That(records.ContainsKey("docs/reviews/README.md")).IsFalse();
    }

    /// <summary>
    /// Every dated record in the tree, keyed the way <see cref="Sealed"/> keys
    /// them.
    /// </summary>
    /// <returns>The records, by key.</returns>
    private static async Task<Dictionary<string, string>> RecordsAsync()
    {
        var records = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in ReviewFiles())
        {
            records[$"docs/reviews/{file.Name}"] = Normalise(await File.ReadAllTextAsync(file.FullName));
        }

        foreach (var (version, section) in ReleasedSections(Normalise(await File.ReadAllTextAsync(ChangelogPath))))
        {
            records[$"CHANGELOG.md#{version}"] = section;
        }

        return records;
    }

    /// <summary>
    /// The review bodies: everything under <c>docs/reviews/</c> except the
    /// index.
    /// </summary>
    /// <returns>The files, ordered by name.</returns>
    private static IEnumerable<FileInfo> ReviewFiles() =>
        new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "docs", "reviews"))
            .EnumerateFiles("*.md", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(file.Name, "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.Ordinal);

    /// <summary>
    /// The changelog's released sections, each running from its own heading to
    /// the next one.
    /// </summary>
    /// <param name="changelog">The whole file, line endings already normalised.</param>
    /// <returns>The version and the section text, in file order.</returns>
    private static IEnumerable<(string Version, string Section)> ReleasedSections(string changelog)
    {
        var headings = VersionHeading().Matches(changelog);

        for (var index = 0; index < headings.Count; index++)
        {
            var version = headings[index].Groups["version"].Value;

            if (string.Equals(version, "Unreleased", StringComparison.Ordinal))
            {
                continue;
            }

            var start = headings[index].Index;
            var end = index + 1 < headings.Count ? headings[index + 1].Index : changelog.Length;

            yield return (version, changelog[start..end]);
        }
    }

    /// <summary>The changelog, at the root.</summary>
    private static string ChangelogPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "CHANGELOG.md");

    /// <summary>Line endings to <c>\n</c>, so a seal survives a CRLF checkout.</summary>
    /// <param name="text">The text as it was read.</param>
    /// <returns>The text with <c>\n</c> line endings.</returns>
    private static string Normalise(string text) => text.ReplaceLineEndings("\n");

    /// <summary>The lower-case hex SHA-256 of some text, as UTF-8.</summary>
    /// <param name="text">The text to digest.</param>
    /// <returns>The digest.</returns>
    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>A changelog version heading, at the start of a line.</summary>
    [GeneratedRegex(@"^## \[(?<version>[^\]]+)\]", RegexOptions.Multiline)]
    private static partial Regex VersionHeading();
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// The dated records — released <c>CHANGELOG.md</c> sections and the bodies
/// under <c>docs/reviews/</c> — still begin with what they said when they were
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
/// [the reviews' own README](../../docs/reviews/README.md) — <i>"they are dated
/// records of what was true when they were written, and rewriting one would
/// destroy the only account of the reasoning"</i>. Prose does not stop a
/// find-and-replace.
/// </para>
/// <para>
/// <b>Append-only, deliberately, rather than frozen.</b> A blanket no-edit rule
/// would be the wrong mechanism: a typo fix in a review is legitimate, and the
/// review index's status table in <c>docs/reviews/README.md</c> is explicitly
/// meant to be updated as findings are acted on — so <c>README.md</c> is not a
/// dated record and is not sealed. What is sealed is the <b>prefix</b>: the
/// characters a record already had. Appending an addendum passes; rewriting a
/// sentence in the middle does not, and neither does truncating one.
/// </para>
/// <para>
/// <b>A deliberate edit is possible and is never silent.</b> The seal carries a
/// character count and a SHA-256, and changing a sealed record means changing
/// the numbers here in the same commit — which is a line in the diff, aimed at
/// exactly the failure mode above, where the sweep's own diff was the only
/// witness. This is the same trade <c>upstream-review.json</c> takes, and the
/// same warning applies: <b>re-sealing a record to make this test pass is
/// rewriting history with an extra step.</b> The failure message prints the
/// replacement seal because refusing to would only mean it was computed by hand;
/// what it cannot do is decide whether the edit was a typo fix or a sweep.
/// </para>
/// <para>
/// <b>Why a seal rather than the file's git history.</b> <c>git log --numstat</c>
/// would say for free whether a file has ever had a line deleted, and it was the
/// first design. It fails on both halves: a legitimate typo fix deletes a line,
/// and the changelog's protection is per-<i>section</i> — its
/// <c>[Unreleased]</c> section is rewritten daily and is not a record of
/// anything yet — which no whole-file history check can express.
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
    /// <c>\n</c>. Published rather than derived so that a truncation is an
    /// arithmetic mismatch a reader can see, and not only a digest that differs.
    /// </param>
    /// <param name="Sha256">The digest of those characters, lower-case hex.</param>
    private readonly record struct Seal(string Record, int Characters, string Sha256);

    /// <summary>
    /// Every dated record in the tree, sealed 2026-08-20 at the state the
    /// rename sweep was reverted to.
    /// </summary>
    /// <remarks>
    /// The changelog carries exactly one released section today, and the
    /// <c>[Unreleased]</c> section — 143,210 characters of it on the day this
    /// was written — is deliberately not in the list. It is not a record of what
    /// shipped until a release stamps it.
    /// </remarks>
    private static readonly Seal[] Sealed =
    [
        new("CHANGELOG.md#0.1.0", 3850, "4e939d92358aaefda03d33dff615bbc14209a52f35be7bf8d2985a54f4c3f7ac"),
        new("docs/reviews/2026-08-18-adversarial-locking.md", 39613, "42770a171c3ceab3c840a29fd1c798b79c59aa9984b30680c7ba00f581a1de94"),
        new("docs/reviews/2026-08-18-adversarial-processes.md", 28536, "1d5e690df3c8b880ea5afc33b9cf435fb3cdda6bc43bc247e3d0116b98e6b1fa"),
        new("docs/reviews/2026-08-18-truncation-findings.md", 13366, "78cb79bc2a5c8419de09d59ce7c13c35839298c0daf34f7d94816401184d84ea"),
        new("docs/reviews/2026-08-18-truncation-prompt-for-sibling-project.md", 17223, "f0fd2b224ac80b033a17b518ca500730b1bfc2ded5ae3a546d6d193cdca3fc30"),
        new("docs/reviews/2026-08-19-auth-transfer-and-session-modes.md", 11022, "1a5b9733e0f023de5c0a8a5879ac20298fd7193277b88bac075838c31fea1a65"),
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
                broken.Add($"{seal.Record}: sealed, and no longer in the tree at all — a dated record was deleted or its heading was renamed");
                continue;
            }

            if (text.Length < seal.Characters)
            {
                broken.Add(
                    $"{seal.Record}: TRUNCATED — {text.Length} characters where {seal.Characters} were sealed. "
                    + "A dated record does not get shorter; something removed part of the account.");
                continue;
            }

            var prefix = text[..seal.Characters];
            var actual = Digest(prefix);

            if (!string.Equals(actual, seal.Sha256, StringComparison.Ordinal))
            {
                broken.Add(
                    $"{seal.Record}: REWRITTEN — the first {seal.Characters} characters are no longer what they were. "
                    + $"If a sweep did this, revert it: a dated record says what was true when it was written. "
                    + $"If the edit was deliberate, re-seal it here: new(\"{seal.Record}\", {text.Length.ToString(CultureInfo.InvariantCulture)}, \"{Digest(text)}\")");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, broken)).IsEmpty();
    }

    /// <summary>
    /// Every dated record in the tree is sealed, and nothing sealed has
    /// vanished.
    /// </summary>
    /// <remarks>
    /// Without this the mechanism protects only what somebody remembered to
    /// list, and the newest review — the one a sweep is most likely to be run
    /// beside — would be the one thing it did not cover. A new review or a newly
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
            .Select(key => $"{key}: sealed and not in the tree — deleted, renamed, or its changelog heading changed.")
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

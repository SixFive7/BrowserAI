// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// Every relative Markdown link in the repository points at something that
/// exists — in the prose, in the scripts, and in the XML doc comments that make
/// up most of this product's design record.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a real failure and not a hypothetical one.</b> Measured 2026-08-17
/// at <c>01b0910</c>: of 151 relative Markdown links in the tracked <c>.cs</c>
/// files, <b>91 did not resolve</b> — every one the same defect, a file three
/// directories below the root writing <c>../../</c> where <c>../../../</c> is
/// required. They spanned nine directories and 42 files and no build, no test
/// and no review had ever reported one of them.
/// </para>
/// <para>
/// <b>Why it survived a suite of 396 tests: the scans read code, and a link
/// lives in a comment.</b> <see cref="RepositoryLayout.ReadCodeAsync"/> blanks
/// comment-only lines deliberately, so that writing down why a rule exists does
/// not violate the rule — and every scan in this suite used it. A cross
/// reference on a <c>///</c> line was invisible to all of them by construction.
/// <b>So this test reads the raw file text, and must go on doing so.</b>
/// <see cref="TheScanReadsRawTextAndNotCode"/> fails if anyone routes it
/// through the comment-stripping reader, because that change would leave every
/// assertion here passing and the corpus nearly empty.
/// </para>
/// <para>
/// <b>Written before the documentation restructure, not after.</b> That
/// restructure moved and deleted several hundred kilobytes of build-session
/// narrative — the whole implementation plan, its index, a work list that was
/// mostly closed items, and the log of the first release run. Until this existed
/// there was no way to tell a link the restructure broke from one that had been
/// broken all along, because nothing counted either.
/// </para>
/// <para>
/// <b>The <c>#anchor</c> half is checked too, since 2026-08-18</b>, and the
/// honesty of the gap that used to be stated here bought nothing.
/// <i>Previously: "Resolving one means reproducing the heading-to-slug rule of
/// whichever renderer is reading, and a wrong slug rule would report failures
/// that are not real — which is the one outcome worse than the gap."</i> What
/// happened instead is that a documentation restructure retitled four headings
/// and moved <b>53 anchored links across 20 files, four of them under
/// <c>src\</c></b>. Not one would have gone red. They were found and repaired by
/// hand, which is the mechanism this repository exists to replace.
/// <see cref="EveryLinkFragmentResolvesToAHeadingThatExists"/> is that half, and
/// <see cref="TheSlugRuleIsTheOneGitHubApplies"/> is what keeps the fear above
/// from coming true — the slug rule is asserted against worked examples rather
/// than trusted.
/// </para>
/// </remarks>
internal sealed partial class DocumentationLinkTests
{
    /// <summary>
    /// Extensions this repository tracks no file of, so a link naming one cannot
    /// be a link into this tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is exactly one such target today and it is not a link at all:
    /// <c>LosslessPassthroughTests</c> holds <c>[Screenshot](café.png)</c> inside
    /// a JSON string literal, as a tool result being passed through unaltered.
    /// It is a link in somebody else's document that this repository is
    /// forbidden to rewrite, which is the whole point of the test carrying it.
    /// </para>
    /// <para>
    /// <b><see cref="TheAssetExclusionHidesNothing"/> is what keeps this list
    /// from becoming a hiding place.</b> It asserts the repository still tracks
    /// no file of any of these kinds — so the exclusion is vacuous, and the day
    /// somebody adds an image, that test goes red and this list must lose the
    /// entry rather than quietly excusing a real broken link.
    /// </para>
    /// </remarks>
    private static readonly string[] NotThisRepositorysKind =
        [".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".bmp", ".webp", ".zip", ".exe", ".dll", ".pdf"];

    /// <summary>
    /// Where a hand-written file can live. Used only by
    /// <see cref="TheAssetExclusionHidesNothing"/>, which must look wider than
    /// the link scan does: an image would be committed beside the prose that
    /// shows it, not necessarily into a directory the scan already reads.
    /// </summary>
    private static readonly string[] HandWrittenDirectories =
        ["src", "tests", "build", "kb", ".claude", ".github"];

    [Test]
    public async Task EveryRelativeLinkResolvesToSomethingThatExists()
    {
        var offenders = new List<string>();

        foreach (var (file, line, number, target) in await LinksAsync())
        {
            var resolved = Path.GetFullPath(Path.Combine(file.DirectoryName!, target.Replace('/', Path.DirectorySeparatorChar)));

            if (File.Exists(resolved) || Directory.Exists(resolved))
            {
                continue;
            }

            // The offender list IS the message: path, line and target, which is
            // everything needed to fix one without opening anything.
            offenders.Add(
                $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}:{number}: '{target}' does not exist"
                + $" — {line.Trim()}");
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task EveryLinkFragmentResolvesToAHeadingThatExists()
    {
        var offenders = new List<string>();
        var anchorsOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var resolved = 0;
        var fromCode = 0;
        var withinOneDocument = 0;

        foreach (var (file, line, number, path, fragment) in await FragmentsAsync())
        {
            FileInfo document;

            if (path.Length is 0)
            {
                // A same-file fragment resolves against the document it is
                // written in — and a file with no headings has nothing for it to
                // resolve to, so one in a .cs or a .ps1 is reported rather than
                // waved through. There are none today; this is the rule with no
                // hiding place in it rather than an exception waiting to be
                // added.
                if (!IsMarkdown(file))
                {
                    offenders.Add(Offence(file, number, line, $"'#{fragment}' is a same-file fragment in a file that has no headings"));
                    continue;
                }

                document = file;
                withinOneDocument++;
            }
            else
            {
                document = new FileInfo(Path.GetFullPath(Path.Combine(file.DirectoryName!, path.Replace('/', Path.DirectorySeparatorChar))));

                // A fragment on something that is not Markdown is somebody
                // else's rule — GitHub's `Program.cs#L42` line anchors are the
                // real case. This repository writes none, and guessing at one
                // would be the wrong-slug-rule failure in a second form.
                if (!IsMarkdown(document))
                {
                    continue;
                }

                // A target that does not exist is EveryRelativeLinkResolves...'s
                // to report, and reporting it twice would have one fix clear two
                // offender lists.
                if (!document.Exists)
                {
                    continue;
                }

                fromCode += file.Extension is ".cs" ? 1 : 0;
            }

            if (!anchorsOf.TryGetValue(document.FullName, out var anchors))
            {
                anchors = await AnchorsAsync(document);
                anchorsOf[document.FullName] = anchors;
            }

            resolved++;

            if (!anchors.Contains(fragment))
            {
                // The offender list IS the message, as above — and the nearest
                // surviving heading is what turns "this is wrong" into "this is
                // what it was renamed to".
                var nearest = anchors
                    .Where(anchor => anchor.StartsWith(fragment[..Math.Min(fragment.Length, 12)], StringComparison.Ordinal))
                    .Take(3)
                    .ToList();

                offenders.Add(Offence(
                    file,
                    number,
                    line,
                    $"'{Path.GetRelativePath(RepositoryLayout.Root.FullName, document.FullName)}#{fragment}' names no heading"
                    + (nearest.Count is 0 ? string.Empty : $" — nearest: {string.Join(", ", nearest)}")));
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous, and not vacuous in each half separately. Re-measured
        // 2026-08-18 by the procedure below to **568 fragments — 512 across
        // documents, 56 within one, and 76 of the 512 written in a `.cs` doc
        // comment** (previously "556: 501, 55, and 77", and "554: 500, 54, and
        // 76" before that, both earlier the same day). That half is the one no
        // renderer ever displays and the one the retitling incident broke four
        // of.
        //
        // ⚠️ **The `.cs` figure went 77 → 76 and no `.cs` file in this change
        // set gained or lost a fragment link**, checked file by file against
        // HEAD. The cause is not established and is deliberately not guessed at;
        // the number above is what the scan returned. The total is separately
        // asserted against the sentence in `CLAUDE.md` by
        // `RecordedCountTests.TheFragmentCountInClaudeMdIsWhatTheScanFinds`, so
        // it is now a red build rather than a stamp somebody has to remember. The floors are a long way under
        // these numbers on purpose: they exist to catch a narrowing that empties
        // the scan, not to pin counts that move whenever a document is written.
        //
        // ⚠️ These three are MEASURED, never derived from the last figure by
        // counting the links in a diff. Re-establish by temporarily asserting
        // `IsEqualTo(-1).Because($"{resolved} {fromCode} {withinOneDocument}")`
        // and running this test alone; the failure prints all three. Adjusting
        // them by arithmetic would make a stamp that reads exactly like a
        // measurement and is not one.
        //
        // ⚠️ Those three numbers are what found the prune defect recorded on
        // RepositoryLayout.NotOursAtTheRoot: this scan counted 552 where a
        // script counting the same corpus outside the suite counted 554, and the
        // gap was two files nothing in the suite could see. A count that nobody
        // compares against a second, independent count is not evidence.
        await Assert.That(resolved).IsGreaterThan(300);
        await Assert.That(fromCode).IsGreaterThan(20);
        await Assert.That(withinOneDocument).IsGreaterThan(20);
    }

    [Test]
    [Arguments("The scope boundary", "the-scope-boundary")]
    [Arguments("BrowserAI — working instructions", "browserai--working-instructions")]
    [Arguments("Install → update → rollback, end to end", "install--update--rollback-end-to-end")]
    [Arguments("7. `ApplyUpdatesAndRestart(null)` as a bare restart", "7-applyupdatesandrestartnull-as-a-bare-restart")]
    [Arguments("**Bold**, *italic* and `code`", "bold-italic-and-code")]
    [Arguments("A [linked](https://example.invalid) word", "a-linked-word")]
    [Arguments("Under_scores and hyphen-ated", "under_scores-and-hyphen-ated")]
    [Arguments("Trailing spaces   ", "trailing-spaces")]
    [Arguments("<b>Inline HTML</b> vanishes", "inline-html-vanishes")]
    [Arguments("`Setup.exe -- <args>` hangs forever", "setupexe----args-hangs-forever")]
    public async Task TheSlugRuleIsTheOneGitHubApplies(string heading, string expected)
    {
        // The fear this file used to state — "a wrong slug rule would report
        // failures that are not real" — is answered here rather than by
        // confidence. Every case is a heading this repository actually carries
        // or a shape one of them is made of, and the two-hyphen answers are the
        // ones a hand-written rule gets wrong: an em-dash and an arrow are
        // dropped as characters while the spaces on either side of them each
        // still become a hyphen.
        await Assert.That(Slug(heading)).IsEqualTo(expected);
    }

    [Test]
    public async Task RepeatedHeadingsGetTheSuffixGitHubGivesThem()
    {
        // CHANGELOG.md carries "Added", "Changed" and "Fixed" once per release,
        // so this is not a hypothetical rule: without the suffix every link into
        // the second one of a pair would resolve to the first, and the check
        // would pass while pointing at the wrong section.
        var anchors = AnchorsIn(["## Fixed", "text", "## Fixed", "text", "## Fixed", "```", "## Fenced", "```", "#Nothashheading", "## <a id=\"pinned\"></a>Explicit"]);

        await Assert.That(anchors.Contains("fixed")).IsTrue();
        await Assert.That(anchors.Contains("fixed-1")).IsTrue();
        await Assert.That(anchors.Contains("fixed-2")).IsTrue();
        await Assert.That(anchors.Contains("fixed-3")).IsFalse();

        // A heading inside a fenced block is a heading in an example, not a
        // heading — nothing links to it and nothing may resolve to it.
        await Assert.That(anchors.Contains("fenced")).IsFalse();

        // A hash with no space after it is not a heading either.
        await Assert.That(anchors.Contains("nothashheading")).IsFalse();

        // An explicit HTML anchor is honoured, because GitHub honours it. This
        // repository writes none today — it is here so that writing the first
        // one is not a red build.
        await Assert.That(anchors.Contains("pinned")).IsTrue();
        await Assert.That(anchors.Contains("explicit")).IsTrue();
    }

    [Test]
    public async Task TheScanReadsRawTextAndNotCode()
    {
        // The defect this file exists for lived entirely on comment-only lines.
        // Routing the scan through ReadCodeAsync -- the reader every other scan
        // in this suite uses, and the obvious thing for a later editor to
        // "tidy" it into -- would blank those lines and leave the assertion
        // above passing over almost nothing.
        //
        // So: count the links the raw text carries, count what survives the
        // comment-stripping reader, and require the first to be far larger. It
        // is not a ratio anybody tuned; on 2026-08-17 it was 1413 against 4.
        //
        // Corrected 2026-08-17 (previously IsGreaterThan(1000)). The
        // documentation restructure deleted the whole implementation plan, and
        // the corpus went 1413 -> 725 in one commit -- so the floor was
        // measuring the plan rather than the corpus this guards. The number is
        // deliberately a long way under 725: it exists to catch a narrowing that
        // empties the scan, not to pin a count that legitimately moves whenever
        // a document is added or retired. The RATIO below is the real
        // assertion, and it is untouched.
        var raw = (await LinksAsync()).Count;

        var throughTheCodeReader = 0;
        foreach (var file in RepositoryLayout.LinkBearingFiles.Where(file => file.Extension is ".cs"))
        {
            throughTheCodeReader += MarkdownLink().Count(await RepositoryLayout.ReadCodeAsync(file));
        }

        await Assert.That(raw).IsGreaterThan(500);
        await Assert.That(throughTheCodeReader * 10).IsLessThan(raw);
    }

    [Test]
    public async Task TheScanCoversEveryKindOfFileThatCarriesALink()
    {
        // A scan looking at four files proves nothing, and the enumeration
        // narrowing is a change that otherwise leaves every assertion here
        // green. Each clause below is a kind of file that carried at least one
        // of the 91 broken links or the prose they pointed at.
        var scanned = RepositoryLayout.LinkBearingFiles;

        await Assert.That(scanned.Count).IsGreaterThan(150);
        await Assert.That(scanned.Any(file => file.Extension is ".md")).IsTrue();
        await Assert.That(scanned.Any(file => file.Extension is ".cs")).IsTrue();
        await Assert.That(scanned.Any(file => file.Extension is ".ps1")).IsTrue();

        // Both halves of the tree, and the repository root, which is where the
        // charter and this file's own instructions live.
        var relative = scanned
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .ToList();

        await Assert.That(relative.Any(path => path.StartsWith("src" + Path.DirectorySeparatorChar, StringComparison.Ordinal))).IsTrue();
        await Assert.That(relative.Any(path => path.StartsWith("tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))).IsTrue();
        await Assert.That(relative.Any(path => !path.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))).IsTrue();

        // ⚠️ AND NOTHING OF OURS IS MISSING, checked against a second
        // enumeration that reaches the tree by a different route. Added
        // 2026-08-18, after the prune list — which matched a directory NAME at
        // any depth, case-insensitively — quietly took src\BrowserAI\Artifacts\
        // out of every scan built on this walk, because the repository root has
        // an artifacts\ build-output directory. Five product source files, three
        // tests, no symptom. A prune reports nothing when it removes the wrong
        // thing, so the only defence is a second count that disagrees.
        var missing = RepositoryLayout.SourceAndScriptFiles
            .Select(file => file.FullName)
            .Except(scanned.Select(file => file.FullName), StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(RepositoryLayout.Root.FullName, path))
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // And nothing that is not ours. A build-output tree carries copies of
        // this repository's own files, so a scan that reached one would report
        // an offender nobody can fix and pass again after the next clean.
        var notOurs = relative
            .Where(path => path.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is ".git" or ".work" or "payload" or "artifacts" or "bin" or "obj" or "node_modules"))
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, notOurs)).IsEmpty();
    }

    [Test]
    public async Task TheAssetExclusionHidesNothing()
    {
        // NotThisRepositorysKind is justified by a fact about the tree, not by
        // taste, so the fact is asserted rather than remembered. The day an
        // image is committed this goes red, and the honest fix is to delete the
        // entry -- never to add the new file's directory to an ignore list.
        var assets = RepositoryLayout.LinkBearingFiles
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Concat(Walk())
            .Where(path => NotThisRepositorysKind.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, assets)).IsEmpty();
    }

    /// <summary>Every relative link target in the repository, with where it was written.</summary>
    /// <returns>The file, the whole line, the 1-based line number, and the target with any anchor removed.</returns>
    private static async Task<List<(FileInfo File, string Line, int Number, string Target)>> LinksAsync()
    {
        var found = new List<(FileInfo, string, int, string)>();

        foreach (var file in RepositoryLayout.LinkBearingFiles)
        {
            // RAW text. Never RepositoryLayout.ReadCodeAsync -- see the remarks
            // on this class, and TheScanReadsRawTextAndNotCode, which fails if
            // this line is ever changed.
            var lines = await File.ReadAllLinesAsync(file.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var match in MarkdownLink().Matches(lines[index]).Cast<Match>())
                {
                    var target = match.Groups["target"].Value;

                    // An absolute URL is somebody else's to keep working, and a
                    // bare fragment is a link within one document.
                    if (Uri.TryCreate(target, UriKind.Absolute, out _) || target.StartsWith('#'))
                    {
                        continue;
                    }

                    var path = target.Split('#', 2)[0];
                    if (path.Length == 0 || NotThisRepositorysKind.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    found.Add((file, lines[index], index + 1, path));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// How many <c>#fragment</c> links the repository carries, for the sentence
    /// in <c>CLAUDE.md</c> that publishes the number.
    /// </summary>
    /// <remarks>
    /// <b>Exposed so the published count and its check come from one
    /// implementation.</b> A second scan written beside the sentence would be a
    /// second definition of "a fragment", and the two would eventually answer
    /// different questions over the same tree — which is exactly the accident
    /// <c>RecordedCountTests</c> exists to stop.
    /// </remarks>
    /// <returns>The count.</returns>
    internal static async Task<int> FragmentCountAsync() => (await FragmentsAsync()).Count;

    /// <summary>Every link in the repository that carries a <c>#fragment</c>.</summary>
    /// <remarks>
    /// The sibling of <see cref="LinksAsync"/>, which throws the fragment away.
    /// Both walk the same raw text for the same reason, and a link with a
    /// fragment appears in both lists — once for its file half and once for its
    /// anchor half.
    /// </remarks>
    /// <returns>
    /// The file, the whole line, the 1-based line number, the file half — empty
    /// for a same-document link — and the fragment, without its <c>#</c>.
    /// </returns>
    private static async Task<List<(FileInfo File, string Line, int Number, string Path, string Fragment)>> FragmentsAsync()
    {
        var found = new List<(FileInfo, string, int, string, string)>();

        foreach (var file in RepositoryLayout.LinkBearingFiles)
        {
            // RAW text, for the reason given on LinksAsync and on this class.
            var lines = await File.ReadAllLinesAsync(file.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var match in MarkdownLink().Matches(lines[index]).Cast<Match>())
                {
                    var target = match.Groups["target"].Value;

                    // An absolute URL is somebody else's to keep working — and
                    // that includes its anchor.
                    if (Uri.TryCreate(target, UriKind.Absolute, out _))
                    {
                        continue;
                    }

                    var halves = target.Split('#', 2);

                    if (halves.Length is not 2 || halves[1].Length is 0)
                    {
                        continue;
                    }

                    found.Add((file, lines[index], index + 1, halves[0], halves[1]));
                }
            }
        }

        return found;
    }

    /// <summary>Every anchor a document offers, as GitHub would compute them.</summary>
    /// <param name="document">The Markdown file to read.</param>
    /// <returns>The anchors, without their <c>#</c>.</returns>
    private static async Task<HashSet<string>> AnchorsAsync(FileInfo document) =>
        AnchorsIn(await File.ReadAllLinesAsync(document.FullName));

    /// <summary>
    /// The anchors a Markdown document's lines offer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separated from the file read so that the collision and fencing rules can
    /// be asserted against a literal rather than against a scratch file — the
    /// rules are what a mis-port would get wrong, and they are worth stating in
    /// a test that cannot fail for a reason to do with the disk.
    /// </para>
    /// <para>
    /// <b>Fenced blocks are skipped and explicit HTML anchors are honoured.</b>
    /// A <c>## Heading</c> inside a fenced example is an example; an
    /// <c>&lt;a id="…"&gt;</c> is a real anchor on GitHub. This repository
    /// writes none of the second kind today, and it is handled anyway so that
    /// writing the first one is not a red build.
    /// </para>
    /// </remarks>
    /// <param name="lines">The document's lines.</param>
    /// <returns>The anchors, without their <c>#</c>.</returns>
    private static HashSet<string> AnchorsIn(IEnumerable<string> lines)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fenced = false;

        foreach (var line in lines)
        {
            foreach (var pinned in ExplicitAnchor().Matches(line).Cast<Match>())
            {
                _ = anchors.Add(pinned.Groups["id"].Value);
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced || AtxHeading().Match(line) is not { Success: true } heading)
            {
                continue;
            }

            var slug = Slug(heading.Groups["title"].Value);

            _ = seen.TryGetValue(slug, out var before);
            seen[slug] = before + 1;

            // GitHub's de-duplication: the first occurrence keeps the slug and
            // every later one takes the next free suffix.
            _ = anchors.Add(before is 0 ? slug : $"{slug}-{before}");
        }

        return anchors;
    }

    /// <summary>
    /// A heading's text as GitHub's anchor slug.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ported from a working implementation rather than derived</b>
    /// (<c>.work\check-anchors.py</c>, 2026-08-18), because the risk this whole
    /// check carries is a slug rule that is nearly right.
    /// <see cref="TheSlugRuleIsTheOneGitHubApplies"/> pins it to worked
    /// examples.
    /// </para>
    /// <para>
    /// The rule: a link becomes its own text, inline HTML disappears, code and
    /// emphasis markers are dropped, then lower-case; keep letters, digits,
    /// hyphens and underscores, turn each space into a hyphen, and drop
    /// everything else. <b>Combining marks are kept</b> because GitHub keeps
    /// them — a branch this corpus does not exercise, since the only non-ASCII
    /// characters in any heading here are an em-dash and an arrow, both dropped.
    /// </para>
    /// </remarks>
    /// <param name="heading">The heading text, without its leading hashes.</param>
    /// <returns>The slug.</returns>
    private static string Slug(string heading)
    {
        // ⚠️ CODE SPANS FIRST, and this order is the whole reason the rule
        // agrees with GitHub. `Setup.exe -- <args>` in kb/packaging/velopack.md
        // is a heading whose code span holds an angle-bracketed word: to a
        // renderer that is four literal characters, to an HTML stripper it is a
        // tag. Strip the tags first and the two rules produce different slugs —
        // which HAZARDS.md recorded on 2026-08-17 by deliberately writing that
        // link WITHOUT its anchor, the one link in the repository that had to
        // avoid the check. Emptying the span of its angle brackets here, before
        // anything looks for a tag, is what closed that.
        var text = EmphasisAndCode().Replace(
            InlineHtml().Replace(
                LinkText().Replace(CodeSpan().Replace(heading, Literally), "$1"),
                string.Empty),
            string.Empty);

        var slug = new StringBuilder(text.Length);

        // Lower-cased one character at a time rather than by lower-casing the
        // whole string: CA1308 forbids the second at error severity, and the
        // classification below does not depend on case, so the two are the same
        // answer by a route the analyzer permits.
        foreach (var character in text.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                _ = slug.Append(char.ToLowerInvariant(character));
            }
            else if (character is ' ' or '\t')
            {
                _ = slug.Append('-');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(character)
                is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                _ = slug.Append(character);
            }
        }

        return slug.ToString();
    }

    /// <summary>
    /// A code span reduced to the text a reader sees, with the two characters
    /// that would otherwise be mistaken for a tag removed.
    /// </summary>
    /// <remarks>
    /// They are removed rather than replaced by a space because GitHub drops
    /// them as characters — a space would become a hyphen and the slug would be
    /// wrong in the other direction.
    /// </remarks>
    /// <param name="span">The matched code span, backticks included.</param>
    /// <returns>Its contents, without angle brackets.</returns>
    private static string Literally(Match span) =>
        span.Groups["text"].Value.Replace("<", string.Empty, StringComparison.Ordinal).Replace(">", string.Empty, StringComparison.Ordinal);

    /// <summary>Whether a file is Markdown, and so has headings at all.</summary>
    /// <param name="file">The file.</param>
    /// <returns><c>true</c> if it is a <c>.md</c> file.</returns>
    private static bool IsMarkdown(FileInfo file) =>
        file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>One offender line: where it is, what is wrong, and the text that carries it.</summary>
    /// <param name="file">The file the link is written in.</param>
    /// <param name="number">The 1-based line number.</param>
    /// <param name="line">The whole line.</param>
    /// <param name="complaint">What is wrong with it.</param>
    /// <returns>The message.</returns>
    private static string Offence(FileInfo file, int number, string line, string complaint) =>
        $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}:{number}: {complaint} — {line.Trim()}";

    /// <summary>Every file in the repository, so the asset claim covers more than the scan does.</summary>
    private static IEnumerable<string> Walk() =>
        RepositoryLayout.Root.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Concat(HandWrittenDirectories
                .Select(name => new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, name)))
                .Where(directory => directory.Exists)
                .SelectMany(directory => directory.EnumerateFiles("*", SearchOption.AllDirectories))
                .Where(file => !Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)
                    .Split(Path.DirectorySeparatorChar)
                    .Any(segment => segment is "bin" or "obj" or "node_modules")))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName));

    /// <summary>
    /// An inline Markdown link's target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Whitespace is excluded from the target on purpose, and it is what
    /// keeps PowerShell out of the results.</b> <c>$measured.Sum ?? 0</c> in
    /// <c>build/Build-Payload.ps1</c> and <c>[datetime]::UtcNow - $startedUtc</c>
    /// beside it land between a closing bracket and a parenthesis by coincidence
    /// of syntax, not because anybody wrote a link. Both carry a space, and so
    /// does the third. Verified 2026-08-17: this leaves all three unmatched and
    /// matches every real link.
    /// </para>
    /// <para>
    /// <b>No paragraph in this file may spell the pattern it matches.</b> The
    /// first draft of this remark quoted the bracket-parenthesis pair to explain
    /// itself, which made this file an offender in its own scan — with the
    /// ellipsis inside it as the target that did not resolve. It is the same
    /// trap <c>NeverByImageNameTests</c> composes its needles at run time to
    /// avoid, and the honest way out is to describe the syntax rather than to
    /// exclude this file, because an exclusion here would be the one place in
    /// the repository where a broken link is allowed to live.
    /// </para>
    /// <para>
    /// The tree holds no angle-bracketed target, no titled link and no
    /// reference-style definition, so those forms are not handled.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex MarkdownLink();

    /// <summary>
    /// An ATX heading: one to six hashes, whitespace, then the text.
    /// </summary>
    /// <remarks>
    /// <b>The whitespace after the hashes is required, and so is the anchor at
    /// the start of the line.</b> Without the first, a Markdown line beginning
    /// <c>#nothing</c> would register an anchor no renderer offers; without the
    /// second, a hash mid-sentence would. The tree carries no setext heading —
    /// the underlined form — and no closing hash sequence, verified 2026-08-18
    /// across all 258 headings, so neither is handled.
    /// </remarks>
    [GeneratedRegex(@"^#{1,6}\s+(?<title>.*?)\s*$")]
    private static partial Regex AtxHeading();

    /// <summary>An explicit HTML anchor, which GitHub honours alongside the headings.</summary>
    [GeneratedRegex(@"<a\s+(?:id|name)=""(?<id>[^""]+)""")]
    private static partial Regex ExplicitAnchor();

    /// <summary>A Markdown link inside a heading, reduced to the text a reader sees.</summary>
    /// <remarks>
    /// Written with every bracket escaped so that this pattern is not itself a
    /// link — the same trap as <see cref="MarkdownLink"/>, and the reason that
    /// remark says no paragraph here may spell what it matches.
    /// </remarks>
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkText();

    /// <summary>Inline HTML in a heading, which contributes nothing to the slug.</summary>
    /// <remarks>
    /// Applied only after <see cref="CodeSpan"/> has emptied the spans of their
    /// angle brackets, so that a tag-shaped word inside a code span is never
    /// read as a tag.
    /// </remarks>
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex InlineHtml();

    /// <summary>An inline code span, backticks included.</summary>
    [GeneratedRegex("`(?<text>[^`]*)`")]
    private static partial Regex CodeSpan();

    /// <summary>
    /// Code and emphasis markers, which are stripped before a character is
    /// looked at.
    /// </summary>
    /// <remarks>
    /// One character class rather than the ported alternation of backtick,
    /// double star and single star: removing every asterisk is exactly what
    /// removing both star forms does, and it cannot be read as ordered.
    /// </remarks>
    [GeneratedRegex("[`*]")]
    private static partial Regex EmphasisAndCode();
}

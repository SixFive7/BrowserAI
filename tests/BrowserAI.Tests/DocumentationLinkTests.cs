// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
/// <b>What this deliberately does not check:</b> the <c>#anchor</c> half of a
/// link. Resolving one means reproducing the heading-to-slug rule of whichever
/// renderer is reading, and a wrong slug rule would report failures that are not
/// real — which is the one outcome worse than the gap. The file half is checked;
/// the fragment is not, and that is a stated gap rather than a silent one.
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
}

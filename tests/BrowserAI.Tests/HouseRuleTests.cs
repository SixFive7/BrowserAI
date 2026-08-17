// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests;

/// <summary>
/// Two rules from <c>CLAUDE.md</c> that were held by habit alone until
/// 2026-08-17.
/// </summary>
/// <remarks>
/// <b>"Prefer a mechanism over a habit. If a rule can be a failing test, a hook
/// or an analyzer, make it one."</b> Both rules below could be, and neither was.
/// The SPDX rule was kept by 222 files out of 224 — which is what a habit looks
/// like right up to the moment somebody adds the 225th.
/// </remarks>
internal sealed class HouseRuleTests
{
    [Test]
    public async Task EverySourceFileCarriesTheTwoLineSpdxHeader()
    {
        // "Source files carry the two-line SPDX header used at the top of this
        // file. Use the LicenseRef- form; the bare FSL-1.1-MIT identifier is
        // forbidden by the licence."
        var offenders = new List<string>();

        foreach (var file in Scope())
        {
            // The head only. A header is a header because it is at the top, and
            // matching anywhere would pass on a file that merely mentions SPDX
            // in prose -- which several files in this repository do.
            var head = string.Join('\n', (await File.ReadAllLinesAsync(file.FullName)).Take(4));

            if (!head.Contains("SPDX-FileCopyrightText", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: no SPDX-FileCopyrightText line in the first four");
            }

            if (!head.Contains("SPDX-License-Identifier: LicenseRef-", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: no SPDX-License-Identifier line in the LicenseRef- form");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous. A scope that stopped resolving would leave the assertion
        // above green over nothing at all, which is the standing failure mode of
        // every test that reads the tree rather than the code.
        await Assert.That(Scope().Count).IsGreaterThan(200);
    }

    [Test]
    public async Task NoTestInTheTreeIsSkipped()
    {
        // "No release with a skipped, quarantined or conditionally-ignored test.
        // A Skip in the tree at release time is a red build wearing a disguise."
        //
        // ⚠️ The needle is composed rather than spelled, so this file does not
        // match its own scan -- the trap NeverByImageNameTests assembles its
        // needles to avoid, and one this session fell into twice before
        // learning. Comments are stripped as well, so that the sentence above
        // quoting the rule is not itself a violation of it.
        var needle = "[" + "Skip";
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(file => file.Extension is ".cs"))
        {
            if ((await RepositoryLayout.ReadCodeAsync(file)).Contains(needle, StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: carries a skip attribute");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // What this does NOT cover, said rather than implied: a test skipped at
        // run time because something it depends on failed. TUnit reports that as
        // "Skipped due to failed dependencies", and it is a consequence of a red
        // test rather than a second defect -- the run summary already carries it,
        // and the failing dependency is what has to be fixed.
        await Assert.That(RepositoryLayout.SourceAndScriptFiles.Count(file => file.Extension is ".cs")).IsGreaterThan(100);
    }

    /// <summary>
    /// Every file this repository hand-writes in a form that can carry a comment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code, the scripts, the prose and the build files. <b>What is outside
    /// it is outside for a reason, and the reasons are not interchangeable:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>JSON has no comment syntax at all</b> — twelve tracked files, four of
    /// them lock files nobody writes by hand. <c>drift-check.json</c> carries a
    /// <c>_license</c> key instead, which is the closest the format allows.
    /// </item>
    /// <item>
    /// <b><c>upstream-snapshots/</c> is foreign bytes</b>, exempted from
    /// normalisation by <c>.gitattributes</c> and compared byte-for-byte against
    /// what the build regenerates. A header would make the gate permanently red
    /// on a difference that is not one.
    /// </item>
    /// <item>
    /// <b><c>LICENSE</c> is the licence</b>, and <c>THIRD-PARTY-NOTICES.txt</c>
    /// carries other people's terms. Stamping this project's identifier on
    /// either would be a claim about somebody else's text.
    /// </item>
    /// </list>
    /// <para>
    /// <c>.gitignore</c>, <c>.gitattributes</c> and <c>BrowserAI.slnx</c> could
    /// carry one and do not. That is a gap rather than a decision, and it is left
    /// open here rather than closed quietly, because widening the scope is a
    /// change to what the rule means and belongs to whoever owns the rule.
    /// </para>
    /// </remarks>
    /// <returns>The files, deduplicated, in path order.</returns>
    private static IReadOnlyList<FileInfo> Scope() =>
    [
        .. RepositoryLayout.LinkBearingFiles
            .Concat(RepositoryLayout.BuildFiles)
            .DistinctBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Where(file => !Relative(file).StartsWith("upstream-snapshots", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName);
}

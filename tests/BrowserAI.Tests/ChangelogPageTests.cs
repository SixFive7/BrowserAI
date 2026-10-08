// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.App.Page;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The dashboard's changelog page: the installed version's section of the changelog
/// shipped inside the build, each entry folded under its headline.
/// </summary>
/// <remarks>
/// <b>T, decided 2026-10-08:</b> the installed toast's <i>Changelog</i> opens a page
/// from the changelog shipped in the build, which works for development builds too.
/// A release reads its own section and any other build the unreleased one.
/// </remarks>
internal sealed class ChangelogPageTests
{
    /// <summary>A small changelog in the real one's shape.</summary>
    private const string Sample = """
        # Changelog

        Everything notable.

        ## [Unreleased]

        ### Added

        - ✨ **Toasts with a countdown.**
          The ready toast counts down.

        ## [1.2.0] - 2026-10-10

        ### Fixed

        - 🐛 **A fix.** It also says more on the same line.
          A detail with `code`, *a quotation*, **bold**, [a page](https://example.invalid/x?a=1&b=2) and [a file](../../kb/README.md).

          A second paragraph with <script>alert(1)</script> in it.

        - 📝 **Only a headline.**

        ## [1.1.0] - 2026-09-23

        ### Added

        - ✨ **Old.**
        """;

    /// <summary>
    /// A release reads the section its heading names, build metadata ignored; any
    /// other version reads the unreleased section; and a changelog with neither gives
    /// nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReleaseReadsItsOwnSectionAndAnyOtherBuildTheUnreleasedOne()
    {
        var release = ChangelogPageContent.SectionFor(Sample, "1.2.0");

        await Assert.That(release).IsNotNull();
        await Assert.That(release!.Version).IsEqualTo("1.2.0");
        await Assert.That(release.Date).IsEqualTo("2026-10-10");
        await Assert.That(release.Markdown).Contains("A fix.");
        await Assert.That(release.Markdown).DoesNotContain("Old.");
        await Assert.That(release.Markdown).DoesNotContain("Toasts with a countdown.");

        await Assert.That(ChangelogPageContent.SectionFor(Sample, "1.2.0+3f2a1b")?.Version).IsEqualTo("1.2.0");

        var development = ChangelogPageContent.SectionFor(Sample, "1.2.1-alpha.0.7");

        await Assert.That(development).IsNotNull();
        await Assert.That(development!.Version).IsNull();
        await Assert.That(development.Markdown).Contains("Toasts with a countdown.");
        await Assert.That(development.Markdown).DoesNotContain("A fix.");

        await Assert.That(ChangelogPageContent.SectionFor("# Changelog\n\n## [1.0.0] - 2026-09-17\n\n- x\n", "2.0.0")).IsNull();
    }

    /// <summary>
    /// Each entry is folded under its headline, its group is a heading, the inline
    /// Markdown is read, a link to a file in the repository is shown as its text, and
    /// whatever the changelog holds is written as text.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachEntryIsFoldedUnderItsHeadlineAndWrittenAsText()
    {
        var html = new StringBuilder();

        ChangelogPageContent.AppendBody(html, ChangelogPageContent.SectionFor(Sample, "1.2.0")!.Markdown);

        var text = html.ToString();

        await Assert.That(text).Contains("<h2>Fixed</h2>");
        await Assert.That(text).Contains($"<details class=\"entry\"><summary>{PageContent.Text("🐛")} <strong>A fix.</strong></summary>");
        await Assert.That(text).Contains("<p>It also says more on the same line. A detail with <code>code</code>, <em>a quotation</em>, <strong>bold</strong>, ");
        await Assert.That(text).Contains($"<a href=\"{PageContent.Text("https://example.invalid/x?a=1&b=2")}\" target=\"_blank\" rel=\"noopener noreferrer\">a page</a> and a file.</p>");
        await Assert.That(text).Contains("<p>A second paragraph with &lt;script&gt;alert(1)&lt;/script&gt; in it.</p>");
        await Assert.That(text).Contains($"<p class=\"entry\">{PageContent.Text("📝")} <strong>Only a headline.</strong></p>");
        await Assert.That(text).DoesNotContain("<script>");
        await Assert.That(text).DoesNotContain("kb/README.md");
    }

    /// <summary>
    /// The page names the release it shows and its date, or says that a development
    /// build shows what it has over the last release, or that the build carries no
    /// section; it is served at its own route.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageNamesWhatItShowsAndIsServedAtItsOwnRoute()
    {
        using var rig = new PageRig(changelog: ChangelogPageContent.SectionFor(Sample, "1.2.0"));

        var page = await PageRig.GetAsync(rig.HandOut(PageKind.Changelog));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<title>BrowserAI changelog</title>");
        await Assert.That(page.Body).Contains("<h1>What changed in BrowserAI 1.2.0</h1>");
        await Assert.That(page.Body).Contains("Released 2026-10-10.");
        await Assert.That(page.Body).Contains("<a href=\"changelog?tab=1\" aria-current=\"page\">Changelog</a>");

        using var development = new PageRig(changelog: ChangelogPageContent.SectionFor(Sample, "9.0.1-alpha.0.3"));

        var unreleased = await PageRig.GetAsync(development.HandOut(PageKind.Changelog));

        await Assert.That(unreleased.Body).Contains("<h1>What changed since the last release</h1>");
        await Assert.That(unreleased.Body).Contains("This is a development build, 9.0.0. What it has over the last release is what the changelog lists as not yet released.");
        await Assert.That(unreleased.Body).Contains("Toasts with a countdown.");

        using var none = new PageRig();

        var empty = await PageRig.GetAsync(none.HandOut(PageKind.Changelog));

        await Assert.That(empty.Body).Contains("This build of BrowserAI, 9.0.0, carries no changelog section for its version.");
    }

    /// <summary>
    /// The changelog the build carries is this repository's own, byte for byte, and it
    /// has the unreleased section a development build reads and at least one release.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheChangelogShippedInTheBuildIsTheRepositorysOwn()
    {
        var shipped = ShippedChangelog.Text();
        var repository = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "CHANGELOG.md"));

        await Assert.That(shipped).IsNotNull();
        await Assert.That(shipped).IsEqualTo(repository);
        await Assert.That(ChangelogPageContent.SectionFor(shipped!, "0.0.0-alpha.0.1")).IsNotNull();
        await Assert.That(ChangelogPageContent.SectionFor(shipped!, "0.0.0-alpha.0.1")!.Version).IsNull();
        await Assert.That(ChangelogPageContent.SectionFor(shipped!, "1.1.0")?.Version).IsEqualTo("1.1.0");
    }
}

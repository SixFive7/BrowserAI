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
    /// <summary>
    /// What opens a Markdown link's target, for the fixtures whose links are written relative to
    /// the repository's root: spelled through this constant, a link in a fixture is not read by
    /// <see cref="DocumentationLinkTests"/> as this file's own, which it would resolve from here.
    /// </summary>
    private const string To = "](";

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
    /// section; it is served at its own route and begins with the way back.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously the title "BrowserAI changelog", the heading
    /// "What changed in BrowserAI 1.2.0" and the navigation's Changelog link marked as this
    /// page)</i>: the maintainer's 40 a, direction A's <i>What is new</i> page, reached from
    /// the home's footer and the install card and begun with a way back.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageNamesWhatItShowsAndIsServedAtItsOwnRoute()
    {
        await using var rig = await PageRig.CreateAsync(changelog: ChangelogPageContent.SectionFor(Sample, "1.2.0"));

        var page = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Changelog));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<title>What is new - BrowserAI</title>");
        await Assert.That(page.Body).Contains(DashboardTests.Crumbs(1));
        await Assert.That(page.Body).Contains("<span aria-current=\"page\">What is new in 1.2.0</span>");
        await Assert.That(page.Body).Contains("<h1 class=\"page-title\">What is new in BrowserAI 1.2.0</h1>");
        await Assert.That(page.Body).Contains("<p class=\"lede\">Released 2026-10-10. Open a line to read more.</p>");

        await using var development = await PageRig.CreateAsync(changelog: ChangelogPageContent.SectionFor(Sample, "9.0.1-alpha.0.3"));

        var unreleased = await PageRig.GetAsync(await development.HandOutAsync(PageKind.Changelog));

        await Assert.That(unreleased.Body).Contains("<h1 class=\"page-title\">What is new since the last release</h1>");
        await Assert.That(unreleased.Body).Contains("This is a development build, 9.0.0. Below is what the changelog lists as not yet released.");
        await Assert.That(unreleased.Body).Contains("Toasts with a countdown.");

        await using var none = await PageRig.CreateAsync();

        var empty = await PageRig.GetAsync(await none.HandOutAsync(PageKind.Changelog));

        await Assert.That(empty.Body).Contains("<h1 class=\"page-title\">What is new</h1>");
        await Assert.That(empty.Body).Contains("This build of BrowserAI, 9.0.0, carries no changelog section for its version.");
    }

    /// <summary>
    /// The page has a sidebar of every release the changelog holds, in the changelog's own
    /// order, with the installed one chosen and marked; every other one is a link to an
    /// address of its own, which shows that release and which a new state keeps.
    /// </summary>
    /// <remarks>
    /// <b>56 a, the maintainer's choice of 2026-10-11, in his words verbatim: <i>"56 a"</i></b>:
    /// the changelog page gets a sidebar of every release in the embedded changelog, the
    /// installed one selected, every other one browsable.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSidebarListsEveryReleaseWithTheInstalledOneChosenAndEveryOtherOneBrowsable()
    {
        await using var rig = await PageRig.CreateAsync(
            changelog: ChangelogPageContent.SectionFor(Sample, "1.2.0"),
            releases: ChangelogPageContent.Releases(Sample));

        var page = (await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Changelog))).Body;

        await Assert.That(page).Contains("<nav class=\"releases\" aria-label=\"Every release\">");

        var unreleased = page.IndexOf("id=\"l-release-unreleased\"", StringComparison.Ordinal);
        var installed = page.IndexOf("id=\"l-release-1.2.0\"", StringComparison.Ordinal);
        var older = page.IndexOf("id=\"l-release-1.1.0\"", StringComparison.Ordinal);

        await Assert.That(unreleased).IsGreaterThan(-1);
        await Assert.That(unreleased).IsLessThan(installed);
        await Assert.That(installed).IsLessThan(older);

        await Assert.That(page).Contains("<a id=\"l-release-1.2.0\" href=\"changelog?tab=1&amp;version=1.2.0\" aria-current=\"page\"><b>1.2.0</b><span class=\"small\">2026-10-10</span><span class=\"tag installed\">Installed</span></a>");
        await Assert.That(page).Contains("<a id=\"l-release-1.1.0\" href=\"changelog?tab=1&amp;version=1.1.0\"><b>1.1.0</b><span class=\"small\">2026-09-23</span></a>");
        await Assert.That(page).Contains("<a id=\"l-release-unreleased\" href=\"changelog?tab=1&amp;version=unreleased\"><b>Not yet released</b></a>");
        await Assert.That(page).Contains("<h1 class=\"page-title\">What is new in BrowserAI 1.2.0</h1>");

        // Another release, at its own address.
        var other = (await PageRig.GetAsync(rig.Root + "changelog?tab=1&version=1.1.0")).Body;

        await Assert.That(other).Contains("<h1 class=\"page-title\">What is new in BrowserAI 1.1.0</h1>");
        await Assert.That(other).Contains("<p class=\"lede\">Released 2026-09-23. Open a line to read more.</p>");
        await Assert.That(other).Contains("Old.");
        await Assert.That(other).DoesNotContain("A fix.");
        await Assert.That(other).Contains("<a id=\"l-release-1.1.0\" href=\"changelog?tab=1&amp;version=1.1.0\" aria-current=\"page\">");
        await Assert.That(other).Contains("data-subject=\"1.1.0\"");

        // A new state keeps it: the stream names it the way the page's script does.
        using (var stream = await rig.StreamAsync(rig.Gate.Root, 1, "changelog", "id=1.1.0"))
        {
            var state = await DashboardTests.StateContainingAsync(stream, "What is new in BrowserAI");

            await Assert.That(state).IsNotNull();
            await Assert.That(state!).Contains("What is new in BrowserAI 1.1.0");
        }

        // What is not yet released.
        var next = (await PageRig.GetAsync(rig.Root + "changelog?tab=1&version=unreleased")).Body;

        await Assert.That(next).Contains("<h1 class=\"page-title\">What is new since the last release</h1>");
        await Assert.That(next).Contains("<p class=\"lede\">Below is what the changelog lists as not yet released.</p>");
        await Assert.That(next).Contains("Toasts with a countdown.");

        // A release the changelog does not hold shows the installed one, and says so.
        var none = (await PageRig.GetAsync(rig.Root + "changelog?tab=1&version=7.7.7")).Body;

        await Assert.That(none).Contains("<h1 class=\"page-title\">What is new in BrowserAI 1.2.0</h1>");
        await Assert.That(none).Contains("This build's changelog holds no release 7.7.7, so the page shows the installed one.");

        // A development build: what is not yet released is the installed one.
        await using var development = await PageRig.CreateAsync(
            changelog: ChangelogPageContent.SectionFor(Sample, "9.0.1-alpha.0.3"),
            releases: ChangelogPageContent.Releases(Sample));

        var dev = (await PageRig.GetAsync(await development.HandOutAsync(PageKind.Changelog))).Body;

        await Assert.That(dev).Contains("<a id=\"l-release-unreleased\" href=\"changelog?tab=1&amp;version=unreleased\" aria-current=\"page\"><b>Not yet released</b><span class=\"tag installed\">Installed</span></a>");
        await Assert.That(dev).Contains("<a id=\"l-release-1.2.0\" href=\"changelog?tab=1&amp;version=1.2.0\"><b>1.2.0</b>");
        await Assert.That(dev).Contains("This is a development build, 9.0.0. Below is what the changelog lists as not yet released.");
    }

    /// <summary>
    /// A link into this repository opens that file on GitHub at the build's ref, in a new tab,
    /// with its label written as text: the release's tag for a release build, and
    /// <c>master</c> for a pre-release build, which carries no commit; a link to an address
    /// keeps its treatment, and a path that leaves the repository or names a scheme stays its
    /// label.
    /// </summary>
    /// <remarks>
    /// <b>88 c, the maintainer's answer of 2026-10-12, in his words verbatim:
    /// <i>"88 a+c"</i></b>: the page showed a link into the repository as its label alone, so a
    /// pointer such as <i>The hazard row</i> read as a fragment of a sentence.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkIntoTheRepositoryOpensThatFileOnGitHubAtTheBuildsRef()
    {
        const string Linked = $$"""
            # Changelog

            ## [Unreleased]

            ### Added

            - ✨ **A change.**
              [The hazard row{{To}}HAZARDS.md#hazard-index).

            ## [1.2.0] - 2026-10-10

            ### Fixed

            - 🐛 **A fix.**
              [The hazard row{{To}}HAZARDS.md#hazard-index), [the kb{{To}}kb/packaging/velopack.md), [a folder{{To}}docs/probes/), [this file{{To}}#unreleased), [a page{{To}}https://example.invalid/x), [outside{{To}}../outside.md), [a script{{To}}javascript:alert) and [a <b>bold</b> label{{To}}DECISIONS.md#the-dashboard-decided-2026-10-11).
            """;

        await using var release = await PageRig.CreateAsync(changelog: ChangelogPageContent.SectionFor(Linked, "1.2.0"), facts: facts => facts with { Version = "1.2.0" });

        var page = (await PageRig.GetAsync(await release.HandOutAsync(PageKind.Changelog))).Body;

        await Assert.That(page).Contains("<a href=\"https://github.com/SixFive7/BrowserAI/blob/v1.2.0/HAZARDS.md#hazard-index\" target=\"_blank\" rel=\"noopener noreferrer\">The hazard row</a>");
        await Assert.That(page).Contains("<a href=\"https://github.com/SixFive7/BrowserAI/blob/v1.2.0/kb/packaging/velopack.md\" target=\"_blank\" rel=\"noopener noreferrer\">the kb</a>");
        await Assert.That(page).Contains("<a href=\"https://github.com/SixFive7/BrowserAI/blob/v1.2.0/docs/probes/\" target=\"_blank\" rel=\"noopener noreferrer\">a folder</a>");
        await Assert.That(page).Contains("<a href=\"https://github.com/SixFive7/BrowserAI/blob/v1.2.0/CHANGELOG.md#unreleased\" target=\"_blank\" rel=\"noopener noreferrer\">this file</a>");
        await Assert.That(page).Contains($"<a href=\"{PageContent.Text("https://example.invalid/x")}\" target=\"_blank\" rel=\"noopener noreferrer\">a page</a>");
        await Assert.That(page).Contains(", outside, a script and ");
        await Assert.That(page).DoesNotContain("outside.md");
        await Assert.That(page).DoesNotContain("javascript:");
        await Assert.That(page).Contains("<a href=\"https://github.com/SixFive7/BrowserAI/blob/v1.2.0/DECISIONS.md#the-dashboard-decided-2026-10-11\" target=\"_blank\" rel=\"noopener noreferrer\">a &lt;b&gt;bold&lt;/b&gt; label</a>");

        // A pre-release build has no tag of its own and carries no commit: what it has not yet
        // released links at master.
        await using var development = await PageRig.CreateAsync(changelog: ChangelogPageContent.SectionFor(Linked, "1.2.1-alpha.0.4"), facts: facts => facts with { Version = "1.2.1-alpha.0.4" });

        var dev = (await PageRig.GetAsync(await development.HandOutAsync(PageKind.Changelog))).Body;

        await Assert.That(dev).Contains("<a href=\"https://github.com/SixFive7/BrowserAI/blob/master/HAZARDS.md#hazard-index\" target=\"_blank\" rel=\"noopener noreferrer\">The hazard row</a>");
        await Assert.That(dev).DoesNotContain("/blob/v");
    }

    /// <summary>
    /// Each release's section links into the repository at that release's own tag, and only
    /// what is not yet released links at the build's ref, whichever build shows it.
    /// </summary>
    /// <remarks>
    /// <b>92 b, the maintainer's answer of 2026-10-12, in his words verbatim: <i>"92 b"</i></b>,
    /// to the build's question whether an older release's section, reached from the sidebar,
    /// should link at the build's ref or at its own tag: its anchors are the ones that release
    /// was written against.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachReleasesLinksPointAtItsOwnTagAndOnlyWhatIsNotYetReleasedAtTheBuilds()
    {
        const string Linked = $$"""
            # Changelog

            ## [Unreleased]

            ### Added

            - ✨ **Next.**
              [The row{{To}}HAZARDS.md#hazard-index).

            ## [1.2.0] - 2026-10-10

            ### Fixed

            - 🐛 **Now.**
              [The row{{To}}HAZARDS.md#hazard-index).

            ## [1.1.0] - 2026-09-23

            ### Added

            - ✨ **Then.**
              [The row{{To}}HAZARDS.md#hazard-index).
            """;

        static string row(string at) =>
            $"<a href=\"https://github.com/SixFive7/BrowserAI/blob/{at}/HAZARDS.md#hazard-index\" target=\"_blank\" rel=\"noopener noreferrer\">The row</a>";

        // A release build, 1.2.0: its own section, an older one, and what is not yet released.
        await using var release = await PageRig.CreateAsync(
            changelog: ChangelogPageContent.SectionFor(Linked, "1.2.0"),
            releases: ChangelogPageContent.Releases(Linked),
            facts: facts => facts with { Version = "1.2.0" });

        _ = await release.HandOutAsync(PageKind.Changelog);

        await Assert.That((await PageRig.GetAsync(release.Root + "changelog?tab=1")).Body).Contains(row("v1.2.0"));
        await Assert.That((await PageRig.GetAsync(release.Root + "changelog?tab=1&version=1.1.0")).Body).Contains(row("v1.1.0"));
        await Assert.That((await PageRig.GetAsync(release.Root + "changelog?tab=1&version=unreleased")).Body).Contains(row("v1.2.0"));

        // A pre-release build: what it has not yet released at master, every release at its tag.
        await using var development = await PageRig.CreateAsync(
            changelog: ChangelogPageContent.SectionFor(Linked, "1.2.1-alpha.0.4"),
            releases: ChangelogPageContent.Releases(Linked),
            facts: facts => facts with { Version = "1.2.1-alpha.0.4" });

        _ = await development.HandOutAsync(PageKind.Changelog);

        await Assert.That((await PageRig.GetAsync(development.Root + "changelog?tab=1")).Body).Contains(row("master"));
        await Assert.That((await PageRig.GetAsync(development.Root + "changelog?tab=1&version=1.2.0")).Body).Contains(row("v1.2.0"));
        await Assert.That((await PageRig.GetAsync(development.Root + "changelog?tab=1&version=1.1.0")).Body).Contains(row("v1.1.0"));
    }

    /// <summary>
    /// An address that asks for what is not yet released, on a build whose changelog lists
    /// nothing there, says that in words and shows the installed release.
    /// </summary>
    /// <remarks>
    /// <b>Found on a reading of the page's words on 2026-10-12</b>: the page said "holds no
    /// release unreleased", the address's own key, which an older tab carries across an update
    /// to a release build.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnAddressForWhatIsNotYetReleasedOnABuildThatListsNoneSaysSo()
    {
        const string Released = """
            # Changelog

            ## [Unreleased]

            ## [1.2.0] - 2026-10-10

            ### Fixed

            - 🐛 **Now.**

            ## [1.1.0] - 2026-09-23

            ### Added

            - ✨ **Then.**
            """;

        await using var rig = await PageRig.CreateAsync(
            changelog: ChangelogPageContent.SectionFor(Released, "1.2.0"),
            releases: ChangelogPageContent.Releases(Released),
            facts: facts => facts with { Version = "1.2.0" });

        _ = await rig.HandOutAsync(PageKind.Changelog);

        var page = (await PageRig.GetAsync(rig.Root + "changelog?tab=1&version=unreleased")).Body;

        await Assert.That(page).Contains("<h1 class=\"page-title\">What is new in BrowserAI 1.2.0</h1>");
        await Assert.That(page).Contains("This build's changelog lists nothing as not yet released, so the page shows the installed one.");
        await Assert.That(page).DoesNotContain("holds no release unreleased");
    }

    /// <summary>
    /// Every release the changelog holds is read, in its order, with what is not yet released
    /// first when it holds an entry and left out when it holds none.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryReleaseTheChangelogHoldsIsReadInItsOrder()
    {
        var releases = ChangelogPageContent.Releases(Sample);

        await Assert.That(string.Join("|", releases.Select(static release => release.Version ?? "Unreleased"))).IsEqualTo("Unreleased|1.2.0|1.1.0");
        await Assert.That(releases[1].Date).IsEqualTo("2026-10-10");
        await Assert.That(releases[1].Markdown).Contains("A fix.");
        await Assert.That(releases[1].Markdown).DoesNotContain("Old.");

        var empty = ChangelogPageContent.Releases("# Changelog\n\n## [Unreleased]\n\n## [1.0.0] - 2026-09-17\n\n- x\n");

        await Assert.That(string.Join("|", empty.Select(static release => release.Version ?? "Unreleased"))).IsEqualTo("1.0.0");

        // The shipped changelog: every section heading it holds, in order.
        var shipped = ShippedChangelog.Text()!;
        var headings = System.Text.RegularExpressions.Regex.Matches(shipped, @"^## \[(?<version>[^\]]+)\]", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(static match => match.Groups["version"].Value)
            .ToList();

        await Assert.That(headings.Count).IsGreaterThanOrEqualTo(4).Because("the positive control: the shipped changelog holds what is not yet released and at least three releases");
        await Assert.That(string.Join("|", ShippedChangelog.Releases().Select(static release => release.Version ?? "Unreleased"))).IsEqualTo(string.Join("|", headings));
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

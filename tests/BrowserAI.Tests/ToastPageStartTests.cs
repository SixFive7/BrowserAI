// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App;
using BrowserAI.App.Page;

namespace BrowserAI.Tests;

/// <summary>
/// The road from a click on an update toast to a tab on the page it names: the
/// start the toast activator makes, the page name that start hands the background,
/// and the page that name opens.
/// </summary>
/// <remarks>
/// <para>
/// <b>T, decided 2026-10-08:</b> <i>Install now</i> opens the dashboard's update page
/// and <i>Changelog</i> its changelog page. The activator makes a person's start for
/// the page, the way a Start Menu click makes one for the status page, so a click
/// with no background running and a click with one running end on the same page.
/// </para>
/// <para>
/// ⚠️ <i>Corrected 2026-10-08 (previously the road went through the coordinator's
/// start modes and its pipe's <c>update</c> and <c>changelog</c> verbs, protocol
/// 3)</i>: the one resident background took the coordinator's place (S a), and a
/// person's start hands it <c>browserai/show</c> with the page's name, which the
/// background reads with the same <see cref="PageNames"/> the tabs use.
/// </para>
/// </remarks>
internal sealed class ToastPageStartTests
{
    /// <summary>
    /// <c>--update</c> and <c>--changelog</c> are a person's starts whose page names the
    /// background reads as the update and the changelog pages, and the activator's page
    /// names are the arguments that make them.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-08</b>, on request, with
    /// <c>PageNameOf</c> naming no page for <c>--changelog</c>, so the Changelog
    /// button would open the status page: red at the changelog row, with no name where
    /// <c>changelog</c> was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachToastPageIsAPersonsStartForAPageOfItsOwn()
    {
        (string Page, string Argument, PageKind Kind)[] pages =
        [
            ("update", "--update", PageKind.Update),
            ("changelog", "--changelog", PageKind.Changelog),
            ("sessions", "--sessions", PageKind.Sessions),
        ];

        foreach (var (page, argument, kind) in pages)
        {
            var arguments = StartModes.ArgumentsFor(page);

            await Assert.That(string.Join(" ", arguments)).IsEqualTo(argument);
            await Assert.That(BrowserAI.App.Program.PageNameOf(arguments)).IsEqualTo(page);
            await Assert.That(PageNames.Parse(BrowserAI.App.Program.PageNameOf(arguments))).IsEqualTo(kind);
            await Assert.That(PageNames.Of(kind)).IsEqualTo(page);
        }

        // A page name the activator does not know is the status page's start, which is
        // a person's start with no argument at all.
        await Assert.That(StartModes.ArgumentsFor("nonsense")).IsEmpty();
        await Assert.That(BrowserAI.App.Program.PageNameOf(StartModes.ArgumentsFor("nonsense"))).IsNull();
        await Assert.That(PageNames.Parse(null)).IsEqualTo(PageKind.Status);
    }
}

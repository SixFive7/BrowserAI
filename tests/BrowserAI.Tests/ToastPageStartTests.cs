// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Coordination;

namespace BrowserAI.Tests;

/// <summary>
/// The road from a click on an update toast to a tab on the page it names: the
/// start the toast activator makes, the verb it hands a running coordinator, and the
/// page that verb opens.
/// </summary>
/// <remarks>
/// <b>T, decided 2026-10-08:</b> <i>Install now</i> opens the dashboard's update page
/// and <i>Changelog</i> its changelog page. The activator makes a person's start for
/// the page, the way a Start Menu click makes one for the status page, so a click
/// with no BrowserAI running and a click with a coordinator running end on the same
/// page.
/// </remarks>
internal sealed class ToastPageStartTests
{
    /// <summary>
    /// <c>--update</c> and <c>--changelog</c> are a person's starts, each handing a
    /// verb of its own to a running coordinator, each verb asking for a tab and naming
    /// its page; and the activator's page names are the arguments that make them.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachToastPageIsAPersonsStartWithAVerbOfItsOwn()
    {
        (string Page, string Argument, StartMode Mode, CoordinatorVerb Verb, string Spelling, PageKind Kind)[] pages =
        [
            ("update", "--update", StartMode.Update, CoordinatorVerb.Update, "update", PageKind.Update),
            ("changelog", "--changelog", StartMode.Changelog, CoordinatorVerb.Changelog, "changelog", PageKind.Changelog),
        ];

        foreach (var (page, argument, mode, verb, spelling, kind) in pages)
        {
            await Assert.That(string.Join(" ", StartModes.ArgumentsFor(page))).IsEqualTo(argument);
            await Assert.That(StartModes.Of(StartModes.ArgumentsFor(page))).IsEqualTo(mode);
            await Assert.That(StartModes.IsAPersons(mode)).IsTrue();
            await Assert.That(StartModes.VerbOf(mode)).IsEqualTo(verb);
            await Assert.That(CoordinatorProtocol.AsksForATab(verb)).IsTrue();
            await Assert.That(CoordinatorProtocol.Spelling(verb)).IsEqualTo(spelling);
            await Assert.That(CoordinatorProtocol.Parse(spelling)).IsEqualTo(verb);
            await Assert.That(Encoding.ASCII.GetString(CoordinatorProtocol.Request(verb))).IsEqualTo(spelling + "\n");
            await Assert.That(StartModes.PageOf(verb)).IsEqualTo(kind);
            await Assert.That(PageNames.Of(kind)).IsEqualTo(page);
            await Assert.That(PageNames.Parse(page)).IsEqualTo(kind);
        }

        // A page name the activator does not know is the status page's start, which is
        // a person's start with no argument at all.
        await Assert.That(StartModes.ArgumentsFor("nonsense")).IsEmpty();
        await Assert.That(StartModes.Of(StartModes.ArgumentsFor("nonsense"))).IsEqualTo(StartMode.User);

        // And an acknowledgement says which protocol knows the two verbs.
        await Assert.That(Encoding.UTF8.GetString(CoordinatorProtocol.Acknowledged(CoordinatorVerb.Update, 1, "http://127.0.0.1:1/x/update?tab=1")))
            .Contains("\"protocol\":3");
    }
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.App.Page;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// A broken install, told to the person directly: one toast per background run, whose
/// button opens the dashboard, and a notice on the dashboard while the condition stands,
/// beside the refusal each session meets.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's decision of 2026-10-10, verbatim: <i>"10 b"</i></b>, on the check
/// lane S1 built on 2026-10-08: a session whose browser server lists different tools
/// from the list compiled into the binary is refused, the refusal names the first tool
/// that differs and tells the model the install is broken, and everything else goes on.
/// Option b keeps all of that and tells the person as well that BrowserAI needs
/// reinstalling.
/// </para>
/// <para>
/// <b>No toast reaches the screen</b>: every arm hands the notice a surface that records
/// what Windows would have been asked, as <c>UpdateToastsTests</c> does.
/// </para>
/// </remarks>
internal sealed class BrokenInstallTests
{
    /// <summary>The first difference the rig's doctored child produces.</summary>
    private const string Difference = "'browser_snapshot', whose definition differs";

    /// <summary>
    /// The first refusal raises one reminder under a tag of its own, whose button and
    /// body open the status page; a second raises no second, the latest difference is
    /// kept for the dashboard, and a session whose child matches again takes both away.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a notice that raised a toast for every
    /// refusal.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABrokenInstallRaisesOneReminderAndAnotherRefusalRaisesNoSecond()
    {
        var surface = new RecordingSurface();
        var changes = 0;

        using var notice = new BrokenInstallNotice(surface, NullLogger.Instance) { Changed = () => changes++ };

        await Assert.That(notice.Difference).IsNull();

        notice.Broken(Difference);
        notice.Broken("'browser_click', which this BrowserAI does not have");

        await Assert.That(surface.Lines()).IsEqualTo($"show {InstallToastContent.Tag} {InstallToastContent.Group}");

        var toast = surface.Shown.Single();

        await Assert.That(toast.Xml).StartsWith("<toast scenario=\"reminder\" launch=\"action=status-page\">");
        await Assert.That(toast.Xml).Contains("<text>BrowserAI needs reinstalling</text>");
        // The installer as the release ships it and README names it, since 2026-10-10
        // (previously "Run BrowserAI-win-Setup.exe again", a name no release carries).
        await Assert.That(toast.Xml).Contains("Download BrowserAI.exe from the latest release and run it");
        await Assert.That(toast.Xml).DoesNotContain("Setup.exe");
        await Assert.That(toast.Xml).Contains("<action content=\"How to reinstall\" arguments=\"action=status-page\" activationType=\"background\"/>");
        await Assert.That(toast.SuppressPopup).IsFalse();
        await Assert.That(notice.Difference).IsEqualTo("'browser_click', which this BrowserAI does not have");
        await Assert.That(changes).IsEqualTo(2);

        // Matched again: the notice goes, and so does the toast; nothing is raised again.
        notice.Intact();
        notice.Intact();

        await Assert.That(notice.Difference).IsNull();
        await Assert.That(surface.Lines()).IsEqualTo($"show {InstallToastContent.Tag} {InstallToastContent.Group}\nremove {InstallToastContent.Tag} {InstallToastContent.Group}");
        await Assert.That(changes).IsEqualTo(3);

        notice.Broken(Difference);

        await Assert.That(surface.Shown.Count).IsEqualTo(1).Because("a toast was raised twice in one background run");
        await Assert.That(notice.Difference).IsEqualTo(Difference);
    }

    /// <summary>
    /// A session's child whose list differs from the binary's is refused, raises one
    /// toast and puts the notice on the dashboard, and a second refused session raises no
    /// second toast.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the product's own session host</b>, with the rig's child answering the
    /// rig's list with one description changed, the way
    /// <c>ErrorCatalogueTests.TheInstallIsBrokenRowIsEmittedByASessionChildWhoseListDiffers</c>
    /// provokes the refusal; the notice is the one the background composes, on a
    /// recording surface, and the dashboard reads it the way the background's page does.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a session host that told the notice nothing,
    /// which refused both sessions, raised no toast and left the dashboard silent.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionChildWhoseListDiffersRaisesOneToastAndTheNoticeAndASecondRaisesNoSecond()
    {
        var surface = new RecordingSurface();

        using var notice = new BrokenInstallNotice(surface, NullLogger.Instance);

        await using var sessions = RigSessionEnvironment.Create(
            child => child.ToolsListResult = child.ToolsListResult.Replace("Capture an accessibility snapshot", "Capture an accessibility snapshot, changed", StringComparison.Ordinal),
            opensDefaultSession: false,
            installHealth: notice);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        foreach (var name in new[] { "broken-install-first", "broken-install-second" })
        {
            var directory = Path.Combine(sessions.Root, name);

            var answer = await rig.Client.RoundTripAsync("tools/call", new JsonObject
            {
                ["name"] = SessionToolSurface.Init,
                ["arguments"] = new JsonObject
                {
                    ["directory"] = directory,
                    ["purpose"] = "meets a browser server that lists different tools",
                    ["headed"] = false,
                    ["transcript"] = false,
                    ["captureNetwork"] = false,
                    ["idleMinutes"] = 10,
                },
            });

            await Assert.That((bool?)answer["isError"]).IsTrue();
            await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.InstallIsBroken(directory, Difference));
        }

        await Assert.That(surface.Lines()).IsEqualTo($"show {InstallToastContent.Tag} {InstallToastContent.Group}").Because("one background run raised the toast more than once, or not at all");
        await Assert.That(notice.Difference).IsEqualTo(Difference);

        using var page = new PageRig(install: notice);

        var status = PageContent.Fragment(page.Page.View(PageKind.Status), PageKind.Status, 1, Occasion.Ordinary, page.Clock.GetUtcNow());

        await Assert.That(status).Contains("BrowserAI needs reinstalling.");
        await Assert.That(status).Contains(PageContent.Text(Difference));
    }

    /// <summary>
    /// While the install is broken every page of the dashboard says so first, with the
    /// difference, what to run and what is kept; once it no longer stands no page says
    /// anything of it.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a page that rendered no notice.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryPageSaysHowToReinstallWhileTheInstallIsBroken()
    {
        using var notice = new BrokenInstallNotice(new RecordingSurface(), NullLogger.Instance);
        using var rig = new PageRig(install: notice);

        notice.Broken(Difference);

        foreach (var kind in new[] { PageKind.Status, PageKind.Sessions, PageKind.Update, PageKind.Changelog })
        {
            var html = PageContent.Fragment(rig.Page.View(kind), kind, 1, Occasion.Ordinary, rig.Clock.GetUtcNow());

            await Assert.That(html).StartsWith("<div class=\"note broken\" role=\"alert\">").Because(PageNames.Of(kind));
            await Assert.That(html).Contains("BrowserAI needs reinstalling.");
            await Assert.That(html).Contains(PageContent.Text(Difference));
            await Assert.That(html).Contains("To reinstall, download BrowserAI.exe from");
            await Assert.That(html).DoesNotContain("Setup.exe");
            await Assert.That(html).Contains(PageContent.ReleasesUrl);
        }

        notice.Intact();

        await Assert.That(PageContent.Fragment(rig.Page.View(PageKind.Status), PageKind.Status, 1, Occasion.Ordinary, rig.Clock.GetUtcNow()))
            .DoesNotContain("BrowserAI needs reinstalling.");
    }

    /// <summary>
    /// An open tab is sent its new state the moment the install is found broken, before
    /// anything else changes on the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The barrier is a push about something else</b>, a downloaded version the
    /// background holds after the refusal, read by the page's once-a-second watch: the
    /// first state after the refusal must be the one the refusal sent, which does not
    /// yet name that version. A page told nothing would send the barrier's state first.
    /// <i>Corrected 2026-10-10 (previously a version staged through the page's own
    /// staged setter, deleted with the page's own update machinery under the
    /// maintainer's "9 a").</i>
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a page whose install change sent no tab
    /// anything.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOpenTabIsToldTheMomentTheInstallIsFoundBroken()
    {
        const string Barrier = "9.9.9-barrier";

        using var notice = new BrokenInstallNotice(new RecordingSurface(), NullLogger.Instance);

        var holds = new BarrierHolds();

        using var rig = new PageRig(holds: holds, install: notice);

        notice.Changed = rig.Page.InstallHealthChanged;

        _ = rig.HandOut(PageKind.Status);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1);

        // The state the tab opened with, before anything is wrong.
        var opened = await stream.NextNamedAsync(PageEvents.State);

        await Assert.That(opened).IsNotNull();
        await Assert.That(opened!.Member("html")).DoesNotContain("BrowserAI needs reinstalling.");

        notice.Broken(Difference);
        holds.Snapshot = new UpdateHoldSnapshot(rig.Clock.GetUtcNow(), UpdateHoldState.Held, Barrier, [], [], []);
        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        var first = await stream.NextNamedAsync(PageEvents.State);

        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Member("html")).DoesNotContain(Barrier).Because("the tab heard of the broken install only with the next change");
    }

    /// <summary>A click on the toast, or on its button, opens the status page, where the notice is.</summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against an activator that did not know the action and
    /// opened nothing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheToastsClickOpensTheStatusPage()
    {
        var click = UpdateToastContent.Parse("action=status-page");

        await Assert.That(click).IsEqualTo(new ToastClick(ToastAction.StatusPage, null));

        var opened = new List<string>();
        var exit = ToastActivation.Act(click, new NoMemory(), page =>
        {
            opened.Add(page);
            return 0;
        });

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(string.Join(",", opened)).IsEqualTo(ToastActivation.StatusPage);
        await Assert.That(App.StartModes.ArgumentsFor(ToastActivation.StatusPage)).IsEmpty().Because("the status page is a start with no argument");
    }

    private static string TextOf(JsonObject answer) =>
        string.Concat((answer["content"]?.AsArray() ?? []).Select(block => (string?)block?["text"] ?? string.Empty));

    /// <summary>Records what Windows would have been asked, one line per call.</summary>
    private sealed class RecordingSurface : IToastSurface
    {
        private readonly List<string> _lines = [];

        public List<ToastRequest> Shown { get; } = [];

        public string Lines() => string.Join('\n', _lines);

        public void Show(string tag, string group, ToastRequest toast, uint sequence)
        {
            Shown.Add(toast);
            _lines.Add($"show {tag} {group}");
        }

        public ToastUpdateResult Update(string tag, string group, IReadOnlyDictionary<string, string> values, uint sequence)
        {
            _lines.Add($"update {tag} {group}");
            return ToastUpdateResult.Succeeded;
        }

        public void Remove(string tag, string group) => _lines.Add($"remove {tag} {group}");
    }

    /// <summary>What holds an update, as the arm sets it.</summary>
    private sealed class BarrierHolds : IUpdateHolds
    {
        public UpdateHoldSnapshot Snapshot { get; set; } = UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch);

        public UpdateHoldSnapshot Read() => Snapshot;

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("No update is downloaded and waiting, so there is nothing to install.");
    }

    /// <summary>A memory of a person's wait that keeps nothing.</summary>
    private sealed class NoMemory : IUpdateToastMemory
    {
        public string? WaitedFor() => null;

        public void Waited(string version)
        {
        }

        public void Forget()
        {
        }
    }
}

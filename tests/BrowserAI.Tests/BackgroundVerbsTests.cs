// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Background;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// What the background answers a person's start and a stop with before its page
/// exists, while it installs an update and while it stops, and what its toasts read
/// before the update core exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>The background's own verbs and the holds its toasts read</b>, the two pieces
/// <c>Program.RunTheBackgroundAsync</c> builds before the page and the update core exist.
/// A person's start that arrives in that moment, or during an update or a stop, is
/// told why no tab opens; a stop never turns an update into a stop, because the record
/// written at the end says which of the two it was.
/// </para>
/// <para>
/// <b>Nothing is started</b>: the verbs are made with no page, and the holds are handed
/// a stand-in for the update core.
/// </para>
/// </remarks>
internal sealed class BackgroundVerbsTests
{
    /// <summary>
    /// The verbs refuse a tab before the page exists, while an update installs and while
    /// the background stops, each in its own words; a stop marks a serving background
    /// stopping and ends its wait, and leaves an update an update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The background marks its record <c>Stopped</c> only when its state is
    /// stopping</b>, and the update core marks it <c>Update</c> before it ends the
    /// background, so a stop that arrived during an update and turned it into a stop
    /// would record the update as an uninstall's stop.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against verbs whose stop left a serving background
    /// serving.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATabIsRefusedInItsOwnWordsUntilThePageExistsAndWhileUpdatingOrStopping()
    {
        using var logs = new CapturingLoggerProvider();

        var stops = 0;
        var verbs = new BackgroundVerbs(logs.CreateLogger("BrowserAI.Background")) { StopRequested = () => stops++ };

        await Assert.That(verbs.State).IsEqualTo(BackgroundState.Serving);

        var (address, refusal) = await verbs.ShowAsync(page: null);

        await Assert.That(address).IsNull();
        await Assert.That(refusal).Contains("still starting");

        verbs.State = BackgroundState.Updating;
        (address, refusal) = await verbs.ShowAsync("sessions");

        await Assert.That(address).IsNull();
        await Assert.That(refusal).Contains("installing an update");

        // A stop during an update ends the wait and leaves the update an update.
        verbs.Stop();

        await Assert.That(stops).IsEqualTo(1);
        await Assert.That(verbs.State).IsEqualTo(BackgroundState.Updating);

        // A stop while serving marks the background stopping, and its tabs are refused.
        var serving = new BackgroundVerbs(logs.CreateLogger("BrowserAI.Background")) { StopRequested = () => stops++ };

        serving.Stop();

        await Assert.That(stops).IsEqualTo(2);
        await Assert.That(serving.State).IsEqualTo(BackgroundState.Stopping);

        (address, refusal) = await serving.ShowAsync(page: null);

        await Assert.That(address).IsNull();
        await Assert.That(refusal).Contains("stopping");
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 20)).IsEqualTo(2);
    }

    /// <summary>
    /// A folder link that arrives before the page exists, while an update installs or while the
    /// background stops is told what a person's start is told in the same state.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-12, from the reader's pass over the folder links' texts</b>: a link that
    /// arrived during an update was told the background was stopping, while a start at the same
    /// moment was told it was installing an update.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderLinkIsToldWhatAStartIsToldWhileTheBackgroundStartsUpdatesOrStops()
    {
        using var logs = new CapturingLoggerProvider();

        var ask = new LinkAsk(LinkVerb.OpenFolder, PageGate.NewToken(), Folder: "data");
        var verbs = new BackgroundVerbs(logs.CreateLogger("BrowserAI.Background"));

        var starting = await verbs.LinkAsync(ask);

        await Assert.That(starting).IsTypeOf<LinkAnswer.Refused>();
        await Assert.That(((LinkAnswer.Refused)starting).Sentence).StartsWith("BrowserAI's background is still starting, so it has no page");

        (string State, BackgroundState Value, string Opening)[] states =
        [
            ("an update", BackgroundState.Updating, "BrowserAI is installing an update, so it opens no "),
            ("a stop", BackgroundState.Stopping, "BrowserAI's background is stopping, so it opens no "),
        ];

        foreach (var (state, value, opening) in states)
        {
            verbs.State = value;

            var (address, tab) = await verbs.ShowAsync(page: null);
            var link = await verbs.LinkAsync(ask);

            await Assert.That(address).IsNull();
            await Assert.That(tab).StartsWith(opening).Because(state);
            await Assert.That(link).IsTypeOf<LinkAnswer.Refused>();
            await Assert.That(((LinkAnswer.Refused)link).Sentence).StartsWith(opening).Because(state);
        }
    }

    /// <summary>
    /// The holds the toasts read say nothing is held and install nothing until the update
    /// core exists, and from then on are the core's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The toasts and the update core need each other</b>: the core raises the toasts,
    /// and the ready toast reads the core's holds once a second. The toasts are built
    /// first and read through the deferred holds, which the background points at the
    /// core on the next line.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against deferred holds that went on answering
    /// nothing once the core existed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheToastsReadNothingHeldUntilTheUpdateCoreExistsAndThenTheCoresHolds()
    {
        var deferred = new DeferredUpdateHolds();
        var before = await deferred.ReadAsync();

        await Assert.That(before.State).IsEqualTo(UpdateHoldState.None);
        await Assert.That(before.Version).IsNull();
        await Assert.That(before.Relays).IsEmpty();

        var core = new StandInHolds();

        deferred.Target = core;

        await Assert.That(await deferred.ReadAsync()).IsSameReferenceAs(core.Snapshot);

        // Corrected 2026-10-10 (previously the deferred holds answered an install-now with
        // "still starting" and then passed it on): #101 of the texts review. The toasts
        // never install, so the deferred holds only read; the page's install-now goes to
        // the update core itself.
    }

    /// <summary>The update core's holds, as the arm sets them.</summary>
    private sealed class StandInHolds : IUpdateHoldsReader
    {
        /// <summary>What every read answers.</summary>
        public UpdateHoldSnapshot Snapshot { get; } =
            UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch) with { State = UpdateHoldState.Held, Version = "9.9.10-verbs-tests" };

        /// <inheritdoc />
        public Task<UpdateHoldSnapshot> ReadAsync() => Task.FromResult(Snapshot);

        /// <inheritdoc />
        public UpdateHoldSnapshot ReadCountdown() => Snapshot;
    }
}

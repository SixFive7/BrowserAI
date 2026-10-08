// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
/// <c>Program.RunTheBackground</c> builds before the page and the update core exist.
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

        var (address, refusal) = verbs.Show(page: null);

        await Assert.That(address).IsNull();
        await Assert.That(refusal).Contains("still starting");

        verbs.State = BackgroundState.Updating;
        (address, refusal) = verbs.Show("sessions");

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

        (address, refusal) = serving.Show(page: null);

        await Assert.That(address).IsNull();
        await Assert.That(refusal).Contains("stopping");
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 20)).IsEqualTo(2);
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
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        var deferred = new DeferredUpdateHolds();
        var before = deferred.Read();

        await Assert.That(before.State).IsEqualTo(UpdateHoldState.None);
        await Assert.That(before.Version).IsNull();
        await Assert.That(before.Relays).IsEmpty();
        await Assert.That(await deferred.InstallNowAsync("9.9.10-verbs-tests", hang.Token)).Contains("still starting");

        var core = new StandInHolds();

        deferred.Target = core;

        await Assert.That(deferred.Read()).IsSameReferenceAs(core.Snapshot);
        await Assert.That(await deferred.InstallNowAsync("9.9.10-verbs-tests", hang.Token)).IsNull();
        await Assert.That(string.Join(" | ", core.InstallsAsked)).IsEqualTo("9.9.10-verbs-tests");
    }

    /// <summary>The update core's holds, as the arm sets them, with a record of every install asked for.</summary>
    private sealed class StandInHolds : IUpdateHolds
    {
        /// <summary>What every read answers.</summary>
        public UpdateHoldSnapshot Snapshot { get; } =
            UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch) with { State = UpdateHoldState.Held, Version = "9.9.10-verbs-tests" };

        /// <summary>Every version an install was asked for, in order.</summary>
        public List<string> InstallsAsked { get; } = [];

        /// <inheritdoc />
        public UpdateHoldSnapshot Read() => Snapshot;

        /// <inheritdoc />
        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken)
        {
            InstallsAsked.Add(version);
            return Task.FromResult<string?>(null);
        }
    }
}

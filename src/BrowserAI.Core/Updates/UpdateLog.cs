// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

// ⚠️ MOVED HERE 2026-10-08 FROM UpdateService.cs, unchanged, when the server's own
// update lane went with the in-process server (S a). The census and every mode's
// startup still write these records under the category they always had, and their
// event ids are a log query's subject, so none moves and none is reused.

/// <summary>Source-generated log messages for the update path.</summary>
internal static partial class UpdateLog
{
    /// <summary>This process is not an installed one, so it will never update.</summary>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "This BrowserAI was not installed by Velopack, so it does not check for updates. That is the normal state under a checkout, a `dotnet run` and every test host.")]
    public static partial void NotAnInstall(ILogger logger);

    /// <summary>A pre-release build refuses to update itself.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version this build carries.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "BrowserAI {Version} is a pre-release build and does not self-update. A version carrying a suffix was built from a commit that no tag points at, so there is no release for it to be newer or older than.")]
    public static partial void PreReleaseBuildDoesNotUpdate(ILogger logger, string version);

    /// <summary>A check is starting, and where it is looking.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="manifestUrl">The composed manifest URL.</param>
    /// <remarks>
    /// <b>The composed URL is logged and not the base URL</b>, because the
    /// feed-URL landmine is invisible in the base: it only becomes wrong once
    /// Velopack has appended <c>releases.{channel}.json</c> to it. Nothing in
    /// the deployment that lost auto-update for three versions ever printed this
    /// line.
    /// </remarks>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Checking for updates at {ManifestUrl}.")]
    public static partial void Checking(ILogger logger, string manifestUrl);

    /// <summary>The feed answered, and had nothing.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="manifestUrl">Where it looked.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "No update available at {ManifestUrl}.")]
    public static partial void NothingAvailable(ILogger logger, string manifestUrl);

    /// <summary>Something is on offer.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version offered.</param>
    /// <param name="isDowngrade">Whether it is a rollback.</param>
    /// <param name="deltaCount">How many deltas stand between here and there; zero means a full download.</param>
    /// <param name="fullPackageSize">The full package's size in bytes.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Update {Version} is available. rollback={IsDowngrade} deltas={DeltaCount} fullPackageBytes={FullPackageSize}")]
    public static partial void Found(ILogger logger, string version, bool isDowngrade, int deltaCount, long fullPackageSize);

    /// <summary>How far the download has got.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What is being downloaded.</param>
    /// <param name="percent">0 to 100.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Debug,
        Message = "Downloading update {Version}: {Percent}%.")]
    public static partial void DownloadProgress(ILogger logger, string version, int percent);

    /// <summary>The package is on disk.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What was downloaded.</param>
    /// <param name="seconds">How long the whole pass took to this point.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "Update {Version} downloaded and staged in {Seconds:F1}s.")]
    public static partial void Downloaded(ILogger logger, string version, double seconds);

    /// <summary>Staged, but somebody else is running.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What is staged.</param>
    /// <param name="waitingOn">What the apply is waiting on, in the census's own terms.</param>
    /// <param name="size">What the staged package weighs.</param>
    /// <param name="seconds">How long the download that produced it took.</param>
    /// <remarks>
    /// <para>
    /// Information, not Warning, and the sentence says why nothing is
    /// wrong: applying would kill every other BrowserAI's browsers, and the
    /// staged package costs nothing to leave where it is.
    /// </para>
    /// <para>
    /// ⚠️ <b>It says what it is waiting on and how far in it got, since
    /// 2026-08-20</b> *(previously "because another BrowserAI is running out of
    /// this install", and nothing else)*. The old line read the same whether one
    /// peer was up or forty, and read the same again when the census could not
    /// be taken at all -- which is not a wait, it is a permanent block, and the
    /// two need different actions from whoever finds the line. The size and the
    /// elapsed seconds are the <i>how far in</i> half: the work is done and
    /// staged, so what is left is the exit of every other instance and not
    /// any more bytes.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "Update {Version} is staged and was NOT applied: {WaitingOn}. Applying would terminate every process under the install root, including other agents' browsers. Nothing more has to be downloaded -- {Size} was fetched in {Seconds:F1} s and is waiting on disk -- so the last instance to exit applies it.")]
    public static partial void StagedButNotAlone(ILogger logger, string version, string waitingOn, string size, double seconds);

    /// <summary>The apply is armed and this process must now end.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What is being applied.</param>
    /// <param name="processId">The pid Update.exe is waiting on.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "Update {Version} will be applied by Update.exe once this process exits. It is waiting on pid {ProcessId}; BrowserAI is shutting down so the session locks release first.")]
    public static partial void Applying(ILogger logger, string version, int processId);

    /// <summary>The pass threw.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The update check failed. Nothing was changed and BrowserAI is unaffected; the next start will try again.")]
    public static partial void PassFailed(ILogger logger, Exception failure);

    /// <summary>The manifest check outran its own budget.</summary>
    /// <remarks>
    /// ⚠️ <b>ITS OWN EVENT ID, AND THAT IS THE POINT OF THE CHANGE.</b> Before
    /// 2026-09-24 a stalled or timed-out check reported as <c>TripwireFired</c>,
    /// event 11, which says the inner timers failed -- so the one line that was
    /// supposed to mean "this is a defect" was also the line an ordinary network
    /// timeout produced. Nothing is renumbered: event ids never change, and this
    /// is a new one.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="budgetMinutes">The check's budget.</param>
    /// <param name="elapsedMinutes">How long the pass ran before it was abandoned.</param>
    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Warning,
        Message = "The update check did not answer within its budget of {BudgetMinutes} minutes and was abandoned after {ElapsedMinutes:F1} minutes. That is a slow or dead feed, not a defect here; nothing was changed and the next start will try again.")]
    public static partial void CheckTimedOut(ILogger logger, double budgetMinutes, double elapsedMinutes);

    /// <summary>The outer deadline fired, which means the inner three did not.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tripwireMinutes">The deadline.</param>
    /// <param name="elapsedMinutes">How long the pass actually ran.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Error,
        Message = "The update pass hit its outer deadline of {TripwireMinutes} minutes after {ElapsedMinutes:F1} minutes. That deadline is a crash tripwire, not a budget, so reaching it means the absolute and stall timers did not fire when they should have. Nothing was applied.")]
    public static partial void TripwireFired(ILogger logger, double tripwireMinutes, double elapsedMinutes);

    /// <summary>This process could not announce itself in the live set.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The live-instance directory.</param>
    /// <param name="failure">Why, when there is a reason to give.</param>
    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Could not join the live-instance set under {Directory}. BrowserAI serves normally; it simply will not apply an update, because it cannot prove no other instance would be terminated by one.")]
    public static partial void CouldNotJoinLiveSet(ILogger logger, string directory, Exception? failure);

    /// <summary>The census failed, so solitude could not be established.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The live-instance directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "Could not count live BrowserAI instances under {Directory}, so this one is treated as not alone and no update is applied.")]
    public static partial void CouldNotCensusLiveSet(ILogger logger, string directory, Exception failure);

    /// <summary>How many other instances the census found.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="others">How many.</param>
    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Debug,
        Message = "{Others} other BrowserAI instance(s) are running out of this install.")]
    public static partial void NotAlone(ILogger logger, int others);

    /// <summary>Velopack said something.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="message">What Velopack said.</param>
    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Debug,
        Message = "velopack: {Message}")]
    public static partial void Velopack(ILogger logger, string message);

    /// <summary>Velopack reported a problem.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="message">What Velopack said.</param>
    /// <param name="failure">The exception it carried, when it carried one.</param>
    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Warning,
        Message = "velopack: {Message}")]
    public static partial void VelopackProblem(ILogger logger, string message, Exception? failure);

    /// <summary>The install root, the channel and the version the locator reports.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="rootAppDir">The install root, which is the parent of current\.</param>
    /// <param name="channel">The channel this install came from.</param>
    /// <param name="manifestVersion">The version the package manifest carries.</param>
    /// <param name="assemblyVersion">The version the binary carries.</param>
    /// <remarks>
    /// <b>Both versions are logged because they come from different
    /// mechanisms</b> -- one from <c>vpk</c>'s manifest, one from MinVer's
    /// assembly attribute -- and a build packed at one and compiled at the other
    /// is exactly the state that made a fleet download the binary it was already
    /// running, hourly, forever.
    /// </remarks>
    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Information,
        Message = "Installed at {RootAppDir}. channel={Channel} manifestVersion={ManifestVersion} assemblyVersion={AssemblyVersion}")]
    public static partial void Installed(ILogger logger, string rootAppDir, string channel, string manifestVersion, string assemblyVersion);

    /// <summary>One reclaim pass over the live-marker directory, on one line.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="summary">The pass's own census.</param>
    /// <remarks>
    /// <b>Debug, because the healthy case is a pass that removed nothing.</b>
    /// The counts are what makes an unhealthy one visible: a
    /// <c>reclaimed=</c> that keeps growing is a fleet that keeps being killed
    /// from outside, and an <c>undetermined=</c> that is not zero is a
    /// permissions problem on a path the sentence names.
    /// </remarks>
    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Debug,
        Message = "live-marker reclaim: {Summary}")]
    public static partial void ReclaimedLiveMarkers(ILogger logger, string summary);

    /// <summary>Somebody else holds the gate, so this process did not reclaim.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="mutex">The gate's name.</param>
    /// <remarks>
    /// Not a missed reclaim: whoever holds the gate is walking the same
    /// directory, which is the same argument the stray sweep's own skip rests
    /// on.
    /// </remarks>
    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Debug,
        Message = "Another process holds {Mutex} and is reclaiming live markers; this one skipped instead of waiting.")]
    public static partial void LiveMarkerReclaimSkipped(ILogger logger, string mutex);

    /// <summary>The reclaim could not run, or could not finish.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The live-instance directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "Could not reclaim stale live-instance markers under {Directory}. Nothing was removed; BrowserAI is unaffected and the next pass tries again.")]
    public static partial void CouldNotReclaimLiveMarkers(ILogger logger, string directory, Exception failure);
}

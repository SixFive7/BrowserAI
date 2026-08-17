// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Hosting;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>What one update pass concluded.</summary>
internal enum UpdateOutcome
{
    /// <summary>The feed had nothing newer, or nothing at all.</summary>
    NothingToDo,

    /// <summary>Downloaded and staged, but another BrowserAI is live, so nothing was applied.</summary>
    StagedButNotAlone,

    /// <summary>Downloaded, staged, and <c>Update.exe</c> is waiting on this process to exit.</summary>
    Applying,

    /// <summary>The pass failed. It is a log line and nothing else.</summary>
    Failed,
}

/// <summary>
/// Check, download, and — only when nothing else is running — stage the apply
/// and ask the process to end.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here runs on the message loop.</b> A <c>tools/call</c> has to stay
/// answerable while a package is in flight, so the whole pass is started on a
/// background thread and never awaited by anything on the request path — the
/// same shape as the stray sweep, and for the same reason: a BrowserAI that
/// cannot update is degraded, one that will not answer is broken.
/// </para>
/// <para>
/// <b>Three independent timers, because one cannot do the job.</b> A single
/// timeout either aborts a healthy slow link or hangs forever on a stalled one:
/// </para>
/// <list type="number">
///   <item><description><see cref="AbsoluteBudget"/> — the whole download, however fast it is going.</description></item>
///   <item><description><see cref="StallBudget"/> — reset on <b>every progress callback</b>. This is the one that catches a link that went away, and it is the reason the progress callback is wired to a timer rather than to a log line.</description></item>
///   <item><description><see cref="CrashTripwire"/> — an outer deadline that is <b>not flow control</b>. Nothing is expected to reach it; if anything does, the pass is wedged in a way the other two did not model, and the point is that it says so rather than living forever.</description></item>
/// </list>
/// <para>
/// <b>They are sized against a link speed, not against a package size, because
/// the package size was never measured</b> until this step and will move with
/// every Node release. The reasoning is written on each constant.
/// </para>
/// <para>
/// <b>An update is never applied by a process that is not alone.</b> That is
/// <see cref="LiveInstances"/>, and the reason is
/// <c>force_stop_package</c> — see that type. The check happens
/// <i>after</i> the download and again nowhere else: downloading is harmless and
/// leaves the package staged for whichever instance is last to go.
/// </para>
/// </remarks>
internal sealed class UpdateService
{
    /// <summary>
    /// The whole download, end to end.
    /// </summary>
    /// <remarks>
    /// Sized against a link rather than a payload: 30 minutes carries
    /// <b>112.4 MB</b> at ~500 kbit/s, which is slower than any link this
    /// product is usable on — a first-run browser provisioning of 203.8 MB has
    /// to succeed on the same connection before BrowserAI works at all. It is a
    /// bound on a pathology, not a service level.
    /// <para>
    /// <b>Corrected 2026-08-16 at the plan's final audit (previously "the
    /// measured **112.4 MB** full package").</b> 112.4 MB is what the budget
    /// <i>carries</i>, derived from 30 minutes × 500 kbit/s; it is a link
    /// budget and was never a measurement of anything. The full package is
    /// <b>49,050,382 bytes</b>, measured 2026-08-16 — less than half of it, so
    /// the headroom is larger than the sentence claimed, which is why nothing
    /// downstream broke. A derived number wearing the word <i>measured</i> is
    /// indistinguishable from a real one, which is the exact failure this
    /// repository forbids.
    /// </para>
    /// </remarks>
    public static TimeSpan AbsoluteBudget => TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long the download may make no progress at all before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Reset on every progress callback. 60 s is twice upstream Playwright's own
    /// per-socket <c>NET_DEFAULT_TIMEOUT</c> of 30 s, so a transport that is
    /// going to recover has already recovered; anything longer is a socket that
    /// is not coming back.
    /// </remarks>
    public static TimeSpan StallBudget => TimeSpan.FromSeconds(60);

    /// <summary>
    /// The outer deadline. <b>A crash tripwire, never flow control.</b>
    /// </summary>
    /// <remarks>
    /// It is deliberately far outside <see cref="AbsoluteBudget"/> plus
    /// <see cref="StallBudget"/>: nothing that is working can reach it, so
    /// reaching it means the two inner timers did not fire when they should
    /// have, which is a defect rather than a slow link. It exists because the
    /// alternative to a wedged background pass is a thread that never ends and
    /// never says so.
    /// </remarks>
    public static TimeSpan CrashTripwire => TimeSpan.FromMinutes(45);

    private readonly IUpdateClient _client;
    private readonly LiveInstances? _live;
    private readonly ILogger _logger;
    private readonly Action _requestShutdown;

    /// <summary>Builds a pass.</summary>
    /// <param name="client">The Velopack seam.</param>
    /// <param name="live">
    /// This process's registration in the live set, taken at startup and held
    /// for its whole life. <see langword="null"/> means it could not be taken,
    /// which means solitude cannot be proven, which means nothing is ever
    /// applied.
    /// </param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="requestShutdown">
    /// Asked to end the process once the apply is staged. Never
    /// <c>Environment.Exit</c>: the ordinary shutdown path has to run so the
    /// session locks release, the job objects close and the log flushes —
    /// <c>Update.exe</c> is waiting on this pid and will not swap until it is
    /// gone.
    /// </param>
    public UpdateService(IUpdateClient client, LiveInstances? live, ILogger logger, Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        _client = client;
        _live = live;
        _logger = logger;
        _requestShutdown = requestShutdown;
    }

    /// <summary>
    /// Runs one pass on a background thread and returns immediately.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget with the result observed. Nothing on the request path
    /// waits for this or can be blocked by it, and a discarded <c>Task</c> would
    /// be a discarded exception on the one path that runs when something has
    /// already gone wrong.
    /// </remarks>
    /// <param name="build">This build's version, refused if it is a pre-release.</param>
    /// <param name="isInstalled">Whether this process is an installed one.</param>
    /// <param name="lifetime">Cancelled when the process is going down.</param>
    public void StartInBackground(string build, bool isInstalled, CancellationToken lifetime)
    {
        if (!isInstalled)
        {
            // Not a defect and not worth a warning: this is what a developer
            // build, a `dotnet run` and every test host look like.
            UpdateLog.NotAnInstall(_logger);
            return;
        }

        // NEVER SELF-UPDATE FROM A BUILD THAT IS NOT A RELEASE. An untagged
        // build carries its own pre-release suffix from the same mechanism that
        // produced the version, so this cannot be forgotten on the build where
        // it matters.
        if (BuildVersion.HasPreReleaseSuffix(build))
        {
            UpdateLog.PreReleaseBuildDoesNotUpdate(_logger, build);
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    _ = await RunOnceAsync(lifetime).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // The thread boundary reports every failure the same way. An update failure is a log line, never a crash and never a protocol error.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    UpdateLog.PassFailed(_logger, failure);
                }
            },
            CancellationToken.None);
    }

    /// <summary>Runs one pass, synchronously from the caller's point of view.</summary>
    /// <param name="lifetime">Cancelled when the process is going down.</param>
    /// <returns>What the pass concluded.</returns>
    public async Task<UpdateOutcome> RunOnceAsync(CancellationToken lifetime)
    {
        // The tripwire is linked to the process lifetime, so a shutdown ends the
        // pass rather than leaving a background thread holding a download.
        using var tripwire = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        tripwire.CancelAfter(CrashTripwire);

        var clock = Stopwatch.StartNew();

        try
        {
            UpdateLog.Checking(_logger, _client.ManifestUrl);

            var candidate = await _client.CheckAsync(tripwire.Token).ConfigureAwait(false);

            if (candidate is null)
            {
                UpdateLog.NothingAvailable(_logger, _client.ManifestUrl);
                return UpdateOutcome.NothingToDo;
            }

            UpdateLog.Found(_logger, candidate.Version, candidate.IsDowngrade, candidate.DeltaCount, candidate.FullPackageSize);

            await DownloadAsync(candidate, tripwire.Token).ConfigureAwait(false);

            UpdateLog.Downloaded(_logger, candidate.Version, clock.Elapsed.TotalSeconds);

            // Only now. A download is harmless and leaves the package staged for
            // whichever instance turns out to be last; an apply is not.
            if (_live is null || !_live.AmIAlone())
            {
                UpdateLog.StagedButNotAlone(_logger, candidate.Version);
                return UpdateOutcome.StagedButNotAlone;
            }

            _client.ApplyAfterThisProcessExits(candidate);
            UpdateLog.Applying(_logger, candidate.Version, Environment.ProcessId);

            _requestShutdown();
            return UpdateOutcome.Applying;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // The process is going down. Nothing to report: the pass was
            // abandoned on purpose and the package, if any, stays staged.
            return UpdateOutcome.NothingToDo;
        }
        catch (OperationCanceledException)
        {
            UpdateLog.TripwireFired(_logger, CrashTripwire.TotalMinutes, clock.Elapsed.TotalMinutes);
            return UpdateOutcome.Failed;
        }
#pragma warning disable CA1031 // Every failure of an update is the same thing to this process: a log line and a pass that did nothing.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateLog.PassFailed(_logger, failure);
            return UpdateOutcome.Failed;
        }
    }

    private async Task DownloadAsync(UpdateCandidate candidate, CancellationToken tripwire)
    {
        using var absolute = CancellationTokenSource.CreateLinkedTokenSource(tripwire);
        absolute.CancelAfter(AbsoluteBudget);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(absolute.Token);
        stall.CancelAfter(StallBudget);

        var lastReported = -1;

        void onProgress(int percent)
        {
            // THE RESET IS THE MECHANISM. A stall timer that is not reset by the
            // thing it is watching is an absolute timeout wearing a second name.
            try
            {
                stall.CancelAfter(StallBudget);
            }
            catch (ObjectDisposedException)
            {
                // The download finished and the source went with it. A late
                // callback is not a failure.
                return;
            }

            // Deciles only. A progress line per percent is 100 lines in a log
            // whose whole point is being readable after something went wrong.
            var decile = percent / 10;

            if (decile > lastReported)
            {
                lastReported = decile;
                UpdateLog.DownloadProgress(_logger, candidate.Version, percent);
            }
        }

        await _client.DownloadAsync(candidate, onProgress, stall.Token).ConfigureAwait(false);
    }
}

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
    /// <b>The composed URL is logged rather than the base URL</b>, because the
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
    /// <remarks>
    /// Information rather than Warning, and the sentence says why nothing is
    /// wrong: applying would kill every other BrowserAI's browsers, and the
    /// staged package costs nothing to leave where it is.
    /// </remarks>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "Update {Version} is staged and was NOT applied, because another BrowserAI is running out of this install. Applying would terminate every process under the install root, including other agents' browsers. The last instance to exit applies it.")]
    public static partial void StagedButNotAlone(ILogger logger, string version);

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

    /// <summary>The outer deadline fired, which means the inner two did not.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tripwireMinutes">The deadline.</param>
    /// <param name="elapsedMinutes">How long the pass actually ran.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Error,
        Message = "The update pass hit its outer deadline of {TripwireMinutes} minutes after {ElapsedMinutes:F1} minutes. That deadline is a crash tripwire rather than a budget, so reaching it means the absolute and stall timers did not fire when they should have. Nothing was applied.")]
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
    /// mechanisms</b> — one from <c>vpk</c>'s manifest, one from MinVer's
    /// assembly attribute — and a build packed at one and compiled at the other
    /// is exactly the state that made a fleet download the binary it was already
    /// running, hourly, forever.
    /// </remarks>
    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Information,
        Message = "Installed at {RootAppDir}. channel={Channel} manifestVersion={ManifestVersion} assemblyVersion={AssemblyVersion}")]
    public static partial void Installed(ILogger logger, string rootAppDir, string channel, string manifestVersion, string assemblyVersion);
}

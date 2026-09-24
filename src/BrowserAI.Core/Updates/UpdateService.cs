// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
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
/// Check, download, and -- only when nothing else is running -- stage the apply
/// and ask the process to end.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here runs on the message loop.</b> A <c>tools/call</c> has to stay
/// answerable while a package is in flight, so the whole pass is started on a
/// background thread and never awaited by anything on the request path -- the
/// same shape as the stray sweep, and for the same reason: a BrowserAI that
/// cannot update is degraded, one that will not answer is broken.
/// </para>
/// <para>
/// <b>Three independent timers, because one cannot do the job.</b> A single
/// timeout either aborts a healthy slow link or hangs forever on a stalled one:
/// </para>
/// <list type="number">
///   <item><description><see cref="AbsoluteBudget"/> -- the whole download, however fast it is going.</description></item>
///   <item><description><see cref="StallBudget"/> -- reset on <b>every progress callback</b>. This is the one that catches a link that went away, and it is the reason the progress callback is wired to a timer and not to a log line.</description></item>
///   <item><description><see cref="CrashTripwire"/> -- an outer deadline that is <b>not flow control</b>. Nothing is expected to reach it; if anything does, the pass is wedged in a way the other two did not model, and the point is that it says so instead of living forever.</description></item>
/// </list>
/// <para>
/// <b>They are sized against a link speed, not against a package size, because
/// the package size was never measured</b> until this step and will move with
/// every Node release. The reasoning is written on each constant.
/// </para>
/// <para>
/// <b>An update is never applied by a process that is not alone.</b> That is
/// <see cref="LiveInstances"/>, and the reason is
/// <c>force_stop_package</c> -- see that type. The check happens
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
    /// Sized against a link, not a payload: 30 minutes carries
    /// <b>112.4 MB</b> at ~500 kbit/s, which is slower than any link this
    /// product is usable on -- a first-run browser provisioning of 207.3 MB has
    /// to succeed on the same connection before BrowserAI works at all
    /// (<i>corrected 2026-09-17, previously "203.8 MB"; re-measured 2026-09-16 at
    /// chromium 1244, and the figure the server renders is
    /// <c>BrowserProvisioner.FirstRunDownloadSizes</c> and not this
    /// sentence</i>). It is a
    /// bound on a pathology, not a service level.
    /// <para>
    /// <b>Corrected 2026-08-16 at the plan's final audit (previously "the
    /// measured **112.4 MB** full package").</b> 112.4 MB is what the budget
    /// <i>carries</i>, derived from 30 minutes × 500 kbit/s; it is a link
    /// budget and was never a measurement of anything. The full package is
    /// <b>49,050,382 bytes</b>, measured 2026-08-16 -- less than half of it, so
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
    /// Reset on every progress callback, and it is the <b>only</b> stall bound
    /// that exists anywhere on this download.
    /// <para>
    /// <b>The downloader underneath bounds a half-dead socket at nothing at
    /// all</b> -- Velopack's only bound is a 30-minute <c>HttpClient.Timeout</c>
    /// that stops at the response headers, measured 2026-09-23 @ Velopack
    /// 1.2.158
    /// ([kb](../../../kb/packaging/velopack.md#what-bounds-a-stalled-download-and-a-stalled-check----measured-2026-09-23)).
    /// So 60 s is a number this product
    /// <i>chooses</i>: long enough that a link which is coming back has come
    /// back, short enough that <see cref="AbsoluteBudget"/> is not the first
    /// thing to notice.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-09-23 (previously "60 s is twice upstream Playwright's
    /// own per-socket <c>NET_DEFAULT_TIMEOUT</c> of 30 s, so a transport that is
    /// going to recover has already recovered").</i> That constant is real --
    /// <c>NET_DEFAULT_TIMEOUT = 3e4</c> at
    /// <c>payload/mcp/node_modules/playwright-core/lib/coreBundle.js:9087</c> @
    /// playwright-core 1.64.0-alpha-1789764292000 -- but it is read exactly once,
    /// at line 34415, as the socket timeout for a <i>browser download in Node</i>.
    /// A different downloader in a different runtime, bounding nothing this
    /// constant governs.
    /// </para>
    /// </remarks>
    public static TimeSpan StallBudget => TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the manifest check may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's decision, 2026-09-24, verbatim:</b> <i>"Wrap the check
    /// in its own timer. So all three timers sit in the tripwire's time."</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>WITHOUT IT THE CHECK WAS BOUNDED BY NOTHING THIS PRODUCT
    /// CONTROLS.</b> Velopack 1.2.158's <c>UpdateManager.CheckForUpdatesAsync()</c>
    /// takes no <see cref="CancellationToken"/> at all, so the token handed to
    /// <c>IUpdateClient.CheckAsync</c> was read once on entry and never again: a
    /// stalled manifest fetch ended only on Velopack's own
    /// <c>HttpClient.Timeout</c>, <b>30 minutes</b>, taken as the default
    /// ([kb](../../../kb/packaging/velopack.md#what-bounds-a-stalled-download-and-a-stalled-check----measured-2026-09-23)).
    /// 30 minutes of check plus a download inside its own 30-minute
    /// <see cref="AbsoluteBudget"/> is 60 against a 45-minute
    /// <see cref="CrashTripwire"/>, so a pass in which every timer behaved could
    /// reach the tripwire -- which is exactly what the tripwire is supposed to
    /// prove cannot happen.
    /// </para>
    /// <para>
    /// <b>15 minutes, and the number is derived and not chosen.</b> The
    /// arithmetic the tripwire needs is <c>this + AbsoluteBudget &lt;
    /// CrashTripwire</c>, and at 30 and 45 that leaves 15 as the ceiling. It is
    /// taken whole and not shaded down, because the check is one small HTTPS
    /// GET of a JSON feed and 15 minutes is already three orders of magnitude
    /// above it: **a bound this far out is a hang detector and not a promptness
    /// claim**, which is the only kind of duration assertion this project allows.
    /// The margin the tripwire keeps is therefore exactly zero by arithmetic and
    /// the whole of <see cref="StallBudget"/> in practice, since a download that
    /// is moving resets that timer and a download that is not never reaches
    /// <see cref="AbsoluteBudget"/>.
    /// </para>
    /// <para>
    /// ⚠️ <b>IT IS APPLIED BY WAITING, NOT BY PASSING A TOKEN, because passing
    /// one does not work.</b> <c>CheckAsync</c> honours cancellation only up to
    /// the point where it calls Velopack; past that the token is inert. So the
    /// call is awaited through <c>WaitAsync</c> with this budget's token, which
    /// ends THIS pass on time and leaves Velopack's own call to finish into
    /// nothing. That is a deliberate trade: the alternative is a pass that cannot
    /// be ended at all.
    /// </para>
    /// </remarks>
    public static TimeSpan CheckBudget => TimeSpan.FromMinutes(15);

    /// <summary>
    /// The outer deadline. <b>A crash tripwire, never flow control.</b>
    /// </summary>
    /// <remarks>
    /// It is deliberately far outside <see cref="CheckBudget"/> plus
    /// <see cref="AbsoluteBudget"/> plus <see cref="StallBudget"/>. It exists
    /// because the alternative to a wedged background pass is a thread that never
    /// ends and never says so.
    /// <para>
    /// ⚠️ <b>ALL THREE TIMERS SIT INSIDE IT NOW, AND REACHING IT IS A DEFECT
    /// AGAIN.</b> <i>Corrected 2026-09-24 (previously "What it does NOT cover is
    /// the check, and a working pass can reach it ... so reaching this deadline is
    /// not by itself evidence that an inner timer failed"), when the maintainer
    /// took the decision quoted on <see cref="CheckBudget"/>.</i> The check is
    /// bounded by <see cref="CheckBudget"/> at 15 minutes and the download by
    /// <see cref="AbsoluteBudget"/> at 30, so the arithmetic is 45 against this
    /// 45 and no pass in which every timer behaved can arrive here. It is the
    /// sentence this constant is named for, and it is true again.
    /// </para>
    /// <para>
    /// <b>And a check timeout no longer arrives wearing this deadline's name.</b>
    /// <c>GetStringAsync</c> ends its <c>HttpClient.Timeout</c> by throwing a
    /// <c>TaskCanceledException</c>, which is an
    /// <see cref="OperationCanceledException"/>, so before the budget existed
    /// every network timeout on the check reported as the tripwire firing.
    /// <c>RunOnceAsync</c> now discriminates: a cancellation whose source is the
    /// check budget logs <c>UpdateLog.CheckTimedOut</c>, and only a cancellation
    /// that is neither the lifetime nor the check reaches
    /// <c>UpdateLog.TripwireFired</c>.
    /// </para>
    /// </remarks>
    public static TimeSpan CrashTripwire => TimeSpan.FromMinutes(45);

    private readonly IUpdateClient _client;
    private readonly UpdateBudgets _budgets;
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
    /// session locks release, the job objects close and the log flushes --
    /// <c>Update.exe</c> is waiting on this pid and will not swap until it is
    /// gone.
    /// </param>
    /// <param name="budgets">
    /// The four timers, or <see langword="null"/> for the product's own. Nothing
    /// in the product passes anything else; the parameter exists so a test can
    /// ask WHICH timer fired without waiting three quarters of an hour to find
    /// out, and <c>UpdateBudgets.Scaled</c> keeps the relationships between them
    /// while it does.
    /// </param>
    public UpdateService(
        IUpdateClient client,
        LiveInstances? live,
        ILogger logger,
        Action requestShutdown,
        UpdateBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        _client = client;
        _live = live;
        _logger = logger;
        _requestShutdown = requestShutdown;
        _budgets = budgets ?? UpdateBudgets.Default;
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
        // pass instead of leaving a background thread holding a download.
        using var tripwire = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        tripwire.CancelAfter(_budgets.Tripwire);

        // The check's own timer, linked to the tripwire so the outer deadline and
        // a shutdown both still end it. See CheckBudget for why the call is
        // AWAITED through this token instead of being handed it: past the point
        // where CheckAsync calls Velopack, a token is inert.
        using var check = CancellationTokenSource.CreateLinkedTokenSource(tripwire.Token);
        check.CancelAfter(_budgets.Check);

        var clock = Stopwatch.StartNew();

        try
        {
            UpdateLog.Checking(_logger, _client.ManifestUrl);

            var candidate = await _client.CheckAsync(check.Token)
                .WaitAsync(check.Token)
                .ConfigureAwait(false);

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
            //
            // ⚠️ The census is read ONCE and its answer is carried into the
            // sentence, added 2026-08-20. Asking `AmIAlone` and then asking
            // again for a number to print would be two questions about a
            // machine that moves between them, and the line would then be able
            // to say "0 others" beside a refusal that happened because there
            // was one.
            var census = _live?.Census() ?? LivenessAnswer.Undetermined(
                "this process never joined the live set, so it cannot prove anything about the others");

            if (census.State is not Liveness.Alone)
            {
                // Guarded because the two clauses are composed and not
                // formatted, which CA1873 is right about in general: with the
                // level off, neither is built.
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var waitingOn = WhatItIsWaitingOn(census);
                    var size = Megabytes(candidate.FullPackageSize);

                    UpdateLog.StagedButNotAlone(_logger, candidate.Version, waitingOn, size, clock.Elapsed.TotalSeconds);
                }

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
        catch (OperationCanceledException) when (check.IsCancellationRequested && !tripwire.IsCancellationRequested)
        {
            // ⚠️ DISCRIMINATED, and the order of the two clauses is the whole
            // point: the check's source is LINKED to the tripwire, so a tripwire
            // firing cancels this one too and both flags are set. Asking the
            // check first without also asking whether the tripwire fired would
            // report every tripwire as a check timeout.
            UpdateLog.CheckTimedOut(_logger, _budgets.Check.TotalMinutes, clock.Elapsed.TotalMinutes);
            return UpdateOutcome.Failed;
        }
        catch (OperationCanceledException)
        {
            UpdateLog.TripwireFired(_logger, _budgets.Tripwire.TotalMinutes, clock.Elapsed.TotalMinutes);
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

    /// <summary>
    /// What the staged apply is waiting on, in the census's own terms.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Added 2026-08-20, and it is the same correction the reinstall
    /// refusal got on the same day.</b> The line this replaces said only that
    /// <i>another BrowserAI is running</i> -- which reads identically whether one
    /// peer is up or forty, and identically again when the census could not be
    /// taken at all and the apply is therefore permanently blocked and not
    /// temporarily. Those two states need different actions from whoever reads
    /// the log, so the line has to distinguish them.
    /// </remarks>
    /// <param name="census">What the live-instance census answered.</param>
    /// <returns>One clause.</returns>
    private static string WhatItIsWaitingOn(LivenessAnswer census) =>
        census.State is Liveness.Undetermined
            ? $"the census could not be taken, so solitude cannot be proven and nothing will be applied until it can: {census.Why}"
            : $"at least {census.Others.ToString(CultureInfo.InvariantCulture)} other BrowserAI process(es) are running out of this install, each proven by a marker file the kernel would not hand over";

    /// <summary>A package size a person can read.</summary>
    /// <param name="bytes">The count, or 0 when the feed did not say.</param>
    /// <returns>The figure.</returns>
    private static string Megabytes(long bytes) =>
        bytes <= 0
            ? "a size the feed did not state"
            : $"{(bytes / 1_000_000d).ToString("F1", CultureInfo.InvariantCulture)} MB";

    private async Task DownloadAsync(UpdateCandidate candidate, CancellationToken tripwire)
    {
        using var absolute = CancellationTokenSource.CreateLinkedTokenSource(tripwire);
        absolute.CancelAfter(_budgets.Absolute);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(absolute.Token);
        stall.CancelAfter(_budgets.Stall);

        var lastReported = -1;

        void onProgress(int percent)
        {
            // THE RESET IS THE MECHANISM. A stall timer that is not reset by the
            // thing it is watching is an absolute timeout wearing a second name.
            try
            {
                stall.CancelAfter(_budgets.Stall);
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>
/// The update core of the one resident background: when to check, what to hold,
/// when to ask the relays, and how to hand over to the installer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the background checks for updates, on a timer, at most once every
/// <see cref="CheckInterval"/></b> (D9, decided 2026-10-08). The time of each check
/// is kept in <see cref="IUpdateCheckStamp"/>, so a crash or a restart adds no
/// check: a start reads it and waits out the rest of the interval.
/// </para>
/// <para>
/// <b>A downloaded update is HELD while anything uses BrowserAI</b> (H1 as
/// adjusted, RESOLUTIONS 13). Every listed session holds it while its countdown
/// has not run out, whether or not its browser has started, and so does one an
/// agent set never to close; every connected relay holds it while its own
/// ten-minute countdown runs or while a call of its client is in flight. Nothing
/// ends for an update that is held.
/// </para>
/// <para>
/// <b>When nothing holds, the relays are asked, all at once, whether they may
/// end</b>, each answer bounded by <see cref="ReadyToEndBound"/>. Any no, any
/// missing answer, a relay that withdraws its yes, or a relay or a holder that
/// appears while the question is out calls the update off for every relay, and
/// nothing ends. Only when every relay said yes does the installing toast go up,
/// every relay end, the package go to <c>Update.exe</c> with
/// <see cref="AfterUpdate.RestartArguments"/>, and the background exit. With no
/// relay connected there is nobody to ask, and it installs.
/// </para>
/// <para>
/// <b>The core runs on its own clock.</b> <see cref="Changed"/> and
/// <see cref="RelayWithdrew"/> record what they were told and return; the work
/// they cause, and every timer, runs as a callback of the
/// <see cref="TimeProvider"/> the core was given. No seam is ever called while
/// the core holds its lock, which is what lets the background call into it from
/// inside its own bookkeeping.
/// </para>
/// </remarks>
internal sealed class BackgroundUpdates : IUpdateHolds, IDisposable
{
    private readonly UpdateInstall _install;
    private readonly UpdateSourceReading _source;
    private readonly Func<UpdateFeed, IBackgroundUpdateClient> _clientFor;
    private readonly IUpdateRelays _relays;
    private readonly IUpdateSessions _sessions;
    private readonly IUpdateToasts _toasts;
    private readonly IUpdateCheckStamp _stamp;
    private readonly Action _requestExit;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly UpdateBudgets _budgets;

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _ending;
    private readonly ITimer _checkTimer;
    private readonly ITimer _holdTimer;
    private readonly HashSet<string> _toasted = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

    private IBackgroundUpdateClient? _client;
    private Phase _phase;
    private UpdateCandidate? _held;
    private string? _downloading;
    private Attempt? _attempt;
    private DateTimeOffset? _tickAt;
    private DateTimeOffset? _retryAt;
    private bool _silent;
    private bool _passRunning;
    private bool _started;
    private bool _disposed;

    /// <summary>Builds the core. Nothing runs until <see cref="Start"/>.</summary>
    /// <param name="install">What this build is and where it was installed from.</param>
    /// <param name="source">What the background's arguments said about the update source.</param>
    /// <param name="clientFor">Builds the Velopack client for a feed; called once, and only when this build may check.</param>
    /// <param name="relays">The relays the background serves.</param>
    /// <param name="sessions">The background's sessions.</param>
    /// <param name="toasts">The update toasts.</param>
    /// <param name="stamp">The record of the last check.</param>
    /// <param name="requestExit">
    /// Asks the background to exit once the package is handed over. <b>It must be
    /// gone within 60 s</b>, the longest <c>Update.exe</c> waits for it.
    /// </param>
    /// <param name="clock">Every timer and every reading of the time.</param>
    /// <param name="logger">Where the core reports.</param>
    /// <param name="budgets">
    /// The check's and the download's bounds, or <see langword="null"/> for the
    /// product's own; a test passes others to ask which bound fired.
    /// </param>
    public BackgroundUpdates(
        UpdateInstall install,
        UpdateSourceReading source,
        Func<UpdateFeed, IBackgroundUpdateClient> clientFor,
        IUpdateRelays relays,
        IUpdateSessions sessions,
        IUpdateToasts toasts,
        IUpdateCheckStamp stamp,
        Action requestExit,
        TimeProvider clock,
        ILogger logger,
        UpdateBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clientFor);
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(stamp);
        ArgumentNullException.ThrowIfNull(requestExit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _install = install;
        _source = source;
        _clientFor = clientFor;
        _relays = relays;
        _sessions = sessions;
        _toasts = toasts;
        _stamp = stamp;
        _requestExit = requestExit;
        _clock = clock;
        _logger = logger;
        _budgets = budgets ?? UpdateBudgets.Default;
        _ending = _lifetime.Token;

        _checkTimer = clock.CreateTimer(static state => ((BackgroundUpdates)state!).OnCheckDue(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _holdTimer = clock.CreateTimer(static state => ((BackgroundUpdates)state!).OnTick(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Where the update stands inside the core.</summary>
    private enum Phase
    {
        /// <summary>Nothing is held. A check may be running.</summary>
        Idle,

        /// <summary>A package is staged and something holds it, or the core is about to ask.</summary>
        Held,

        /// <summary>The relays have been asked whether they may end.</summary>
        Asking,

        /// <summary>Committed: from here the update installs and the background exits.</summary>
        Installing,
    }

    /// <summary>Which way an install began.</summary>
    private enum InstallRoute
    {
        /// <summary>Every relay said yes.</summary>
        Agreed,

        /// <summary>Nothing held it and no relay was connected to ask.</summary>
        Unasked,

        /// <summary>The person chose <i>Install now</i>.</summary>
        Person,
    }

    /// <summary>
    /// How often the background asks its update source: <b>at most once every ten
    /// minutes</b>, across crashes and restarts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D9, decided 2026-10-08 by the maintainer, in his words verbatim:</b>
    /// <i>"Managed from the background only on a timer and at max once per 10 min.
    /// even across crashes and restarts."</i> It replaces Q225 b of 2026-09-22, one
    /// check per server start with nothing stored.
    /// </para>
    /// <para>
    /// <b>Nothing in Velopack does this for BrowserAI.</b> It never checks on its
    /// own, keeps no time and does not debounce: every <c>CheckForUpdatesAsync</c>
    /// reads the source once (<c>UpdateManager.cs:128-170</c>), read in Velopack
    /// 1.2.161's source twice on 2026-10-08, in step 0 of the one-binary build
    /// (<c>.work/step0-research/FINDINGS.md</c> A4 and
    /// <c>.work/step0-velopack/FINDINGS.md</c> item 5). So this timer and
    /// <see cref="IUpdateCheckStamp"/> are the whole rate limit.
    /// </para>
    /// <para>
    /// <b>It is also the slowest the core ever looks at a held update</b>: every
    /// check ends by looking at what holds the update again, so a countdown longer
    /// than this is looked at again before it runs out, and nothing waits on a
    /// timer longer than this one.
    /// </para>
    /// </remarks>
    public static TimeSpan CheckInterval { get; } = UpdateBudgets.CheckInterval;

    /// <summary>
    /// How long each relay's answer to <i>ready to end?</i> is waited for, and how
    /// long the relays are given to end: <b>10 s</b>.
    /// </summary>
    /// <remarks>
    /// <b>The same bound for the same reason as
    /// <see cref="Coordination.CoordinatorProtocol.HandOutBound"/></b>: it is a hang
    /// detector for a peer that answers out of its own memory over a pipe on this
    /// machine, and a relay that has not answered by then is not going to. A
    /// relay that does not answer calls the update off, so the cost of the bound
    /// being reached is a later install, never a relay ended by mistake.
    /// </remarks>
    public static TimeSpan ReadyToEndBound { get; } = UpdateBudgets.ReadyToEndBound;

    /// <summary>
    /// Decides whether this build checks at all, holds a package an earlier run left
    /// staged, and arms the first check.
    /// </summary>
    /// <remarks>
    /// <b>Three things stop every check</b>, each said once in the log: a build that
    /// is not installed (D11), an update source the install's argument named and
    /// this build refused, and a pre-release build whose source is a URL. A
    /// pre-release build checks a folder (H2, decided 2026-10-08).
    /// </remarks>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_started)
            {
                return;
            }

            _started = true;
        }

        if (!MayCheck(out var source))
        {
            return;
        }

        var client = _clientFor(source.Feed);

        lock (_gate)
        {
            _client = client;
        }

        BackgroundUpdateLog.Watching(_logger, client.ManifestUrl, CheckInterval.TotalMinutes);
        HoldWhatIsOnDisk(client, atStart: true);
        ArmCheck(FirstCheckDue());
    }

    /// <summary>
    /// The background says something the hold depends on changed: a relay connected,
    /// disconnected, started or ended a call, or a session opened, closed or moved
    /// its countdown.
    /// </summary>
    /// <remarks>
    /// Returns at once; the core looks again on its own clock.
    /// </remarks>
    public void Changed()
    {
        lock (_gate)
        {
            _silent = false;
        }

        ScheduleTick();
    }

    /// <summary>
    /// A relay that said yes heard from its client before every relay had agreed:
    /// the update is called off for everyone (RESOLUTIONS 13).
    /// </summary>
    /// <remarks>
    /// After the commit this is ignored, and the relay answers that message with the
    /// update sentence itself (U2).
    /// </remarks>
    /// <param name="relay">The relay's <see cref="RelayState.Id"/>.</param>
    public void RelayWithdrew(string relay)
    {
        ArgumentNullException.ThrowIfNull(relay);

        bool settle;

        lock (_gate)
        {
            settle = _phase is Phase.Asking
                && _attempt is { CalledOff: null } attempt
                && attempt.Asked.Contains(relay);

            if (settle)
            {
                _attempt!.CalledOff = $"relay {relay} withdrew its yes, because its client sent a message before every relay had agreed";
            }
        }

        if (settle)
        {
            ScheduleTick();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>From memory, with no waits.</b> The state and the version are the core's
    /// own; the sessions and the relays are what the background's two seams hold.
    /// </para>
    /// <para>
    /// <b>Every connected relay is listed, holding or not</b>, with what its client
    /// needs once the update ends it. Codex needs a new conversation, measured
    /// 2026-10-03 (<see cref="RelayReconnect"/>). ⚠️ <i>Corrected 2026-10-08
    /// (previously "Claude Code is reported as Unknown ... whether a relay can tell
    /// them apart from what it observes has not been measured yet")</i>: it was
    /// measured that day over 41 runs, and the relay judges it from its client's name,
    /// the command line of the process that started it and the entrypoint variable,
    /// the measurement's option d, and sends the answer in its greeting
    /// (<see cref="RelayState.Reconnect"/>). The client's name alone, which is all this
    /// core reads, still answers <see cref="RelayReconnect.Unknown"/> for Claude Code
    /// when a relay sent no answer.
    /// </para>
    /// <para>
    /// <b>Each relay's conversation is named</b>, added 2026-10-10: this snapshot is what
    /// the dashboard and the toast draw, so it is the read that reads the clients' own
    /// records (1.3 c), through <see cref="IUpdateRelays.ConnectedWithNames"/>; the core's
    /// own passes and the toast's countdown read none.
    /// </para>
    /// </remarks>
    public UpdateHoldSnapshot Read() => Read(named: true);

    /// <inheritdoc />
    public UpdateHoldSnapshot ReadCountdown() => Read(named: false);

    /// <summary>What holds the update now, each relay's conversation named or left unread.</summary>
    /// <param name="named">Whether to read every relay's conversation from its client's records.</param>
    /// <returns>The snapshot.</returns>
    private UpdateHoldSnapshot Read(bool named)
    {
        var now = _clock.GetUtcNow();
        Phase phase;
        string? version;
        bool older;

        lock (_gate)
        {
            phase = _phase;
            version = _held?.Version;

            // Q308 a: a rollback the feed offers is said to be older, by the update page
            // and the ready toast (built again 2026-10-10; Found logged it before).
            older = _held?.IsDowngrade ?? false;
        }

        if (version is null || phase is Phase.Idle)
        {
            return UpdateHoldSnapshot.Nothing(now);
        }

        var hidden = new List<HoldingSession>();
        var visible = new List<HoldingSession>();

        foreach (var session in _sessions.Countdowns())
        {
            (session.Visible ? visible : hidden).Add(new HoldingSession(session.Directory, session.Purpose, session.ClosesAt));
        }

        var relays = new List<HoldingRelay>();

        foreach (var relay in named ? _relays.ConnectedWithNames() : _relays.Connected())
        {
            relays.Add(new HoldingRelay(ClientOf(relay), relay.ProjectFolder, relay.IdleAt, relay.CallInFlight, relay.Reconnect is RelayReconnect.Unknown ? ReconnectOf(relay.ClientName) : relay.Reconnect, relay.Conversation, relay.Label, relay.Window));
        }

        return new UpdateHoldSnapshot(
            now,
            phase is Phase.Installing ? UpdateHoldState.Installing : UpdateHoldState.Held,
            version,
            hidden,
            visible,
            relays,
            older);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Every relay ends first, then every session closes cleanly</b>, each within
    /// <see cref="SessionTimes.BrowserCloseCap"/>; then the installing toast, the
    /// hand-over and the exit. The relays go first so that no client can open a
    /// session while the others close; a relay answers its calls in flight and what
    /// it held with the update sentence itself.
    /// </para>
    /// <para>
    /// <b>Once it has begun it runs to the end</b>, and
    /// <paramref name="cancellationToken"/> ends only the caller's wait: an install
    /// stopped half way would leave the relays ended and the update not installed.
    /// </para>
    /// </remarks>
    public async Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        UpdateCandidate? candidate = null;
        IBackgroundUpdateClient? client = null;
        Attempt? superseded = null;
        string? refusal;

        lock (_gate)
        {
            refusal = WhyNotNow(version);

            if (refusal is null)
            {
                candidate = _held;
                client = _client;
                superseded = _attempt;
                _attempt = null;
                _phase = Phase.Installing;
            }
        }

        if (refusal is not null || candidate is null || client is null)
        {
            refusal ??= "No update is downloaded and waiting, so there is nothing to install.";
            BackgroundUpdateLog.InstallNowRefused(_logger, version, refusal);
            return refusal;
        }

        superseded?.Dispose();
        Arm(_checkTimer, Timeout.InfiniteTimeSpan);
        BackgroundUpdateLog.InstallingNow(_logger, candidate.Version);

        return await InstallAsync(candidate, client, InstallRoute.Person).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops every timer and abandons a check or a download in flight.</summary>
    public void Dispose()
    {
        Attempt? attempt;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            attempt = _attempt;
            _attempt = null;
        }

        _checkTimer.Dispose();
        _holdTimer.Dispose();
        attempt?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>What the dashboard shows for a relay's client.</summary>
    /// <remarks>
    /// <b>The sessions page's own wording since 2026-10-10</b>, through
    /// <see cref="ClientNames"/>, so the two pages call one client one thing.
    /// </remarks>
    /// <param name="relay">The relay.</param>
    /// <returns>Its name and version, its name alone, or <see cref="ClientNames.Unnamed"/>.</returns>
    internal static string ClientOf(RelayState relay) => ClientNames.Of(relay.ClientName, relay.ClientVersion);

    /// <summary>What a client needs once an update has ended its relay, as far as its name tells.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>.</param>
    /// <returns><see cref="RelayReconnect.NewConversation"/> for Codex, and otherwise <see cref="RelayReconnect.Unknown"/>.</returns>
    internal static RelayReconnect ReconnectOf(string? clientName) =>
        KnownClients.Matches(clientName, KnownClients.Codex) ? RelayReconnect.NewConversation : RelayReconnect.Unknown;

    /// <summary>Whether this build checks at all, said once in the log when it does not.</summary>
    /// <param name="source">The source it checks, when it does.</param>
    /// <returns>Whether it checks.</returns>
    private bool MayCheck([NotNullWhen(true)] out UpdateSource? source)
    {
        source = _source.Source;

        if (!_install.IsInstalled)
        {
            BackgroundUpdateLog.NotInstalled(_logger);
            return false;
        }

        if (source is null)
        {
            BackgroundUpdateLog.SourceRefused(_logger, _source.Refusal ?? "the background was given no reading of its arguments");
            return false;
        }

        if (BuildVersion.HasPreReleaseSuffix(_install.Version) && !source.IsFolder)
        {
            BackgroundUpdateLog.PreReleaseNeverChecksAUrl(_logger, _install.Version, source.Feed.ManifestUrl);
            return false;
        }

        return true;
    }

    /// <summary>When the first check of this run is due, said in the log with the reason.</summary>
    /// <returns>Now, or the end of the interval the last recorded check began.</returns>
    private DateTimeOffset FirstCheckDue()
    {
        var now = _clock.GetUtcNow();
        var reading = _stamp.Read();

        if (reading.Unreadable is { } why)
        {
            BackgroundUpdateLog.FirstCheckUnreadable(_logger, _stamp.Where, why);
            return now;
        }

        if (reading.At is not { } last)
        {
            BackgroundUpdateLog.FirstCheckNoRecord(_logger, _stamp.Where);
            return now;
        }

        if (last > now)
        {
            BackgroundUpdateLog.FirstCheckRecordAhead(_logger, _stamp.Where, last);
            return now;
        }

        var due = last + CheckInterval;

        if (due <= now)
        {
            BackgroundUpdateLog.FirstCheckRecordOld(_logger, last, CheckInterval.TotalMinutes);
            return now;
        }

        BackgroundUpdateLog.FirstCheckLater(_logger, last, due);
        return due;
    }

    /// <summary>Arms the check timer for a moment, or not at all once the update installs.</summary>
    /// <param name="due">When the check is due.</param>
    private void ArmCheck(DateTimeOffset due)
    {
        lock (_gate)
        {
            if (_disposed || _phase is Phase.Installing)
            {
                return;
            }

            var wait = due - _clock.GetUtcNow();
            Arm(_checkTimer, wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
        }
    }

    /// <summary>Asks the core to look again now, on its own clock.</summary>
    private void ScheduleTick() => WakeAt(_clock.GetUtcNow());

    /// <summary>
    /// Asks the core to look again at a moment, unless it is already due to look
    /// sooner.
    /// </summary>
    /// <remarks>
    /// <b>Never later than a look already armed, and armed under the lock</b>: two
    /// threads arming one timer outside it could leave the later moment standing
    /// over the sooner one, and a change the background reported would then wait
    /// for a countdown it had nothing to do with.
    /// </remarks>
    /// <param name="at">When.</param>
    private void WakeAt(DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_disposed || (_tickAt is { } pending && pending <= at))
            {
                return;
            }

            _tickAt = at;
            var due = at - _clock.GetUtcNow();
            Arm(_holdTimer, due > TimeSpan.Zero ? due : TimeSpan.Zero);
        }
    }

    /// <summary>Re-arms a timer once, quietly when the core has already stopped.</summary>
    /// <param name="timer">The timer.</param>
    /// <param name="due">When it fires, or <see cref="Timeout.InfiniteTimeSpan"/> for never.</param>
    private static void Arm(ITimer timer, TimeSpan due)
    {
        try
        {
            _ = timer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // The core was disposed while this was being armed: nothing is to run.
        }
    }

    /// <summary>The check timer's callback.</summary>
    private void OnCheckDue() => _ = RunPassAsync();

    /// <summary>
    /// One check, and the download it may lead to. Never two at once, and none once
    /// the update installs.
    /// </summary>
    /// <returns>The pass; it never faults.</returns>
    private async Task RunPassAsync()
    {
        IBackgroundUpdateClient? client;

        lock (_gate)
        {
            if (_disposed || _passRunning || _phase is Phase.Installing || _client is null)
            {
                return;
            }

            _passRunning = true;
            client = _client;
        }

        var started = _clock.GetUtcNow();

        try
        {
            RecordCheck(started);
            await PassAsync(client).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_ending.IsCancellationRequested)
        {
            // The background is going down, and the pass was abandoned on purpose.
        }
#pragma warning disable CA1031 // The pass is a callback of the core's clock: a failure is a log line and the next check, never a crash of the background.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.PassFailed(_logger, CheckInterval.TotalMinutes, failure);
        }
        finally
        {
            lock (_gate)
            {
                _passRunning = false;
                _downloading = null;
                _silent = false;
            }

            ArmCheck(started + CheckInterval);
            ScheduleTick();
        }
    }

    /// <summary>
    /// Records that a check starts now, <b>before</b> it asks anything.
    /// </summary>
    /// <remarks>
    /// <b>Before and not after, and the difference is a crash.</b> A record written
    /// once the check returned would be missing for a check the process died in, and
    /// the restart would check again at once, which is the extra check D9 rules out.
    /// Written first, every check that began counts whatever became of it. A record
    /// that cannot be written is a warning: within this run the timer still keeps the
    /// interval, and only a restart before it runs out could check early.
    /// </remarks>
    /// <param name="at">When the check starts.</param>
    private void RecordCheck(DateTimeOffset at)
    {
        try
        {
            _stamp.Write(at);
        }
        catch (IOException failure)
        {
            BackgroundUpdateLog.StampUnwritable(_logger, _stamp.Where, CheckInterval.TotalMinutes, failure.Message);
        }
        catch (UnauthorizedAccessException failure)
        {
            BackgroundUpdateLog.StampUnwritable(_logger, _stamp.Where, CheckInterval.TotalMinutes, failure.Message);
        }
    }

    /// <summary>Asks the source, and downloads what it offers when the core may hold it.</summary>
    /// <param name="client">The Velopack client.</param>
    /// <returns>The pass.</returns>
    private async Task PassAsync(IBackgroundUpdateClient client)
    {
        BackgroundUpdateLog.Checking(_logger, client.ManifestUrl);

        UpdateCandidate? candidate;

        try
        {
            // Bounded by WAITING and not by the token: Velopack's own check takes
            // no token, so past the point where the client calls it the token is
            // inert, and an abandoned check writes nothing but the staging id.
            candidate = await client.CheckAsync(_ending).WaitAsync(_budgets.Check, _clock, _ending).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            BackgroundUpdateLog.CheckTimedOut(_logger, _budgets.Check.TotalMinutes);
            return;
        }

        if (candidate is null)
        {
            BackgroundUpdateLog.NothingAvailable(_logger, client.ManifestUrl);
            return;
        }

        BackgroundUpdateLog.Found(_logger, candidate.Version, candidate.PackId ?? "none", candidate.IsDowngrade, candidate.DeltaCount, candidate.FullPackageSize);

        if (!Admit(candidate))
        {
            return;
        }

        bool busy;

        lock (_gate)
        {
            if (_held is { } held && string.Equals(held.Version, candidate.Version, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            busy = _phase is Phase.Asking or Phase.Installing;

            if (!busy)
            {
                _downloading = candidate.Version;
            }
        }

        if (busy)
        {
            BackgroundUpdateLog.FoundWhileBusy(_logger, candidate.Version);
            return;
        }

        var downloaded = false;

        try
        {
            downloaded = await DownloadAsync(client, candidate).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _downloading = null;
            }

            if (downloaded)
            {
                // ⚠️ THE STAGE ALREADY CHANGED THE INSTALL. Velopack's download
                // rewrites Update.exe and deletes every other package, the installed
                // version's own included: UpdateManager.cs:290-313 at 1.2.161, read
                // by the step-0 measurements of 2026-10-08
                // (.work/step0-velopack/FINDINGS.md). A package that is staged and
                // held has touched the install although nothing is applied.
                BackgroundUpdateLog.Staged(_logger, candidate.Version);
                Hold(candidate);
            }
            else
            {
                HoldWhatIsOnDisk(client, atStart: false);
            }
        }
    }

    /// <summary>Downloads a candidate within the absolute and the stall bound.</summary>
    /// <param name="client">The Velopack client.</param>
    /// <param name="candidate">What the source offered.</param>
    /// <returns>Whether the package is on disk.</returns>
    private async Task<bool> DownloadAsync(IBackgroundUpdateClient client, UpdateCandidate candidate)
    {
        using var deadlines = new DownloadDeadlines(_clock, _budgets.Absolute, _budgets.Stall, _ending);
        var reported = -1;

        void onProgress(int percent)
        {
            // THE RESET IS THE MECHANISM: a stall bound the download does not push
            // back is an absolute bound under a second name.
            deadlines.Progressed();

            var decile = percent / 10;

            if (decile > reported)
            {
                reported = decile;
                BackgroundUpdateLog.DownloadProgress(_logger, candidate.Version, percent);
            }
        }

        try
        {
            await client.DownloadAsync(candidate, onProgress, deadlines.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (deadlines.Expired is { } why && !_ending.IsCancellationRequested)
        {
            BackgroundUpdateLog.DownloadAbandoned(_logger, candidate.Version, why);
            return false;
        }
    }

    /// <summary>
    /// Whether a candidate may be downloaded, held or applied: <b>only the
    /// installed pack</b>, and never a version whose hand-over failed in this run.
    /// </summary>
    /// <param name="candidate">What a source or the packages directory offered.</param>
    /// <returns>Whether it is admitted; a refusal is a log line.</returns>
    private bool Admit(UpdateCandidate candidate)
    {
        if (candidate.PackId is null
            || _install.PackId is null
            || !string.Equals(candidate.PackId, _install.PackId, StringComparison.OrdinalIgnoreCase))
        {
            BackgroundUpdateLog.ForeignPackage(_logger, candidate.Version, candidate.PackId ?? "none", _install.PackId ?? "none");
            return false;
        }

        bool failedBefore;

        lock (_gate)
        {
            failedBefore = _failed.Contains(candidate.Version);
        }

        if (failedBefore)
        {
            BackgroundUpdateLog.FailedBefore(_logger, candidate.Version);
            return false;
        }

        return true;
    }

    /// <summary>Holds a staged package, raising the ready toast once per version.</summary>
    /// <param name="candidate">The package.</param>
    private void Hold(UpdateCandidate candidate)
    {
        bool raise;

        lock (_gate)
        {
            if (_disposed || _phase is Phase.Asking or Phase.Installing)
            {
                return;
            }

            _held = candidate;
            _phase = Phase.Held;
            _retryAt = null;
            _silent = false;
            raise = _toasted.Add(candidate.Version);
        }

        BackgroundUpdateLog.Held(_logger, candidate.Version);

        if (raise)
        {
            Raise(static (toasts, version) => toasts.Held(version), candidate.Version, "ready");
        }

        ScheduleTick();
    }

    /// <summary>
    /// Holds what is staged on disk, or lets go of what was held when nothing is:
    /// <b>what is held is what is on disk</b>.
    /// </summary>
    /// <remarks>
    /// Read at the start of a run, and after a download that did not finish, because
    /// Velopack deletes every other package once a download ends, finished or not
    /// (<c>CleanPackagesExcept</c> in <c>DownloadUpdatesAsync</c>'s <c>finally</c>,
    /// Velopack 1.2.161): a package held before it may be gone.
    /// </remarks>
    /// <param name="client">The Velopack client.</param>
    /// <param name="atStart">Whether this is the first look of the run.</param>
    private void HoldWhatIsOnDisk(IBackgroundUpdateClient client, bool atStart)
    {
        UpdateCandidate? staged;

        try
        {
            staged = client.Staged();
        }
#pragma warning disable CA1031 // A packages directory that cannot be read holds nothing the core may apply; it is a log line.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.SeamFailed(_logger, "Reading the staged package", failure);
            staged = null;
        }

        if (staged is not null && Admit(staged))
        {
            if (atStart)
            {
                BackgroundUpdateLog.StagedAtStart(_logger, staged.Version);
            }

            Hold(staged);
            return;
        }

        UpdateCandidate? dropped = null;

        lock (_gate)
        {
            if (_phase is Phase.Held)
            {
                dropped = _held;
                _held = null;
                _phase = Phase.Idle;
            }
        }

        if (dropped is not null)
        {
            BackgroundUpdateLog.HeldPackageGone(_logger, dropped.Version);
        }
    }

    /// <summary>Raises one toast; a toast that fails is a log line and stops nothing.</summary>
    /// <param name="raise">The call.</param>
    /// <param name="version">Its version.</param>
    /// <param name="which">Which toast, for the log.</param>
    private void Raise(Action<IUpdateToasts, string> raise, string version, string which)
    {
        try
        {
            raise(_toasts, version);
        }
#pragma warning disable CA1031 // A toast is information for the person: one that cannot be shown must not stop an update.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.ToastFailed(_logger, which, failure);
        }
    }

    /// <summary>The hold timer's callback: settle a question, then look again.</summary>
    private void OnTick()
    {
        try
        {
            lock (_gate)
            {
                _tickAt = null;
            }

            Settle();
            Evaluate();
        }
#pragma warning disable CA1031 // An exception out of a timer callback ends the process; a failed look is a log line, and the next change or check looks again.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.TickFailed(_logger, failure);
        }
    }

    /// <summary>Acts on a question that has been answered or called off.</summary>
    private void Settle()
    {
        var (attempt, agreed) = TakeSettled();

        if (attempt is null)
        {
            return;
        }

        if (agreed)
        {
            Commit(attempt);
        }
        else
        {
            CallOff(attempt);
        }
    }

    /// <summary>
    /// Takes the question once it is over, for <see cref="Settle"/> to act on outside
    /// the lock.
    /// </summary>
    /// <returns>
    /// The question and whether every relay agreed, or no question when none is over
    /// or the asking is still being sent.
    /// </returns>
    private (Attempt? Attempt, bool Agreed) TakeSettled()
    {
        lock (_gate)
        {
            if (_attempt is not { Settled: false, Dispatched: true } attempt)
            {
                return (null, false);
            }

            if (attempt.CalledOff is not null)
            {
                attempt.Settled = true;
                EndAttempt(attempt);
                return (attempt, false);
            }

            if (attempt.AllYes)
            {
                attempt.Settled = true;
                return (attempt, true);
            }

            return (null, false);
        }
    }

    /// <summary>Returns from a question to holding. Under <see cref="_gate"/>.</summary>
    /// <param name="attempt">The question.</param>
    private void EndAttempt(Attempt attempt)
    {
        _attempt = null;
        _phase = Phase.Held;
        _retryAt = attempt.RetryAt;
        _silent = attempt.Silent;
    }

    /// <summary>Tells every relay the update is off. Nothing ends.</summary>
    /// <param name="attempt">The question that was called off.</param>
    private void CallOff(Attempt attempt)
    {
        BackgroundUpdateLog.CalledOff(_logger, attempt.Version, attempt.CalledOff ?? "it was called off");

        try
        {
            _relays.CallOff(attempt.Version);
        }
#pragma warning disable CA1031 // A relay seam that fails to call off is a log line; the update stays held and nothing ends.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.SeamFailed(_logger, "Calling the update off", failure);
        }

        // After the call off and not before it: the relays hear that it is over
        // even if ending the asks still waiting were to fail.
        attempt.Dispose();
    }

    /// <summary>
    /// Every relay said yes: one last look at what holds, then the commit, after
    /// which nothing calls the update off.
    /// </summary>
    /// <param name="attempt">The question every relay agreed to.</param>
    private void Commit(Attempt attempt)
    {
        var began = WhatBeganToHold(attempt);
        IBackgroundUpdateClient? client = null;
        var committed = false;

        lock (_gate)
        {
            if (!ReferenceEquals(_attempt, attempt))
            {
                // The person's install-now took the question over.
                return;
            }

            attempt.CalledOff ??= began;

            if (attempt.CalledOff is null)
            {
                _attempt = null;
                _phase = Phase.Installing;
                client = _client;
                committed = true;
            }
            else
            {
                EndAttempt(attempt);
            }
        }

        if (!committed || client is null)
        {
            CallOff(attempt);
            return;
        }

        Arm(_checkTimer, Timeout.InfiniteTimeSpan);
        BackgroundUpdateLog.InstallingAgreed(_logger, attempt.Version);
        _ = InstallAsync(attempt.Candidate, client, InstallRoute.Agreed);
        attempt.Dispose();
    }

    /// <summary>What began to hold while the question was out, or <see langword="null"/>.</summary>
    /// <param name="attempt">The question.</param>
    /// <returns>One clause for the log, or <see langword="null"/> when nothing did.</returns>
    private string? WhatBeganToHold(Attempt attempt)
    {
        if (!TryRead(out var sessions, out var relays))
        {
            return "the sessions or the relays could not be read";
        }

        foreach (var relay in relays)
        {
            if (!attempt.Asked.Contains(relay.Id))
            {
                return $"relay {relay.Id} connected while the question was out";
            }
        }

        var holding = Holding.Of(_clock.GetUtcNow(), sessions, relays);

        return holding.Any ? $"{holding.Summary} began to hold while the question was out" : null;
    }

    /// <summary>Looks at what holds the update, and asks or installs when nothing does.</summary>
    private void Evaluate()
    {
        lock (_gate)
        {
            if (_disposed || _phase is Phase.Idle or Phase.Installing)
            {
                return;
            }
        }

        if (!TryRead(out var sessions, out var relays))
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var holding = Holding.Of(now, sessions, relays);
        var lookAgain = Timeout.InfiniteTimeSpan;
        var calledOff = false;
        UpdateCandidate? unasked = null;
        IBackgroundUpdateClient? client = null;
        Attempt? asking = null;

        lock (_gate)
        {
            if (_phase is Phase.Asking)
            {
                calledOff = CallOffForNewcomers(holding, relays);
            }
            else if (_phase is Phase.Held && _held is not null && _downloading is null)
            {
                if (holding.Any)
                {
                    lookAgain = holding.Deadline is { } deadline ? deadline - now : CheckInterval;
                }
                else if (_retryAt is { } retry && retry > now)
                {
                    lookAgain = retry - now;
                }
                else if (_silent)
                {
                    // A relay did not answer: the next change the background reports,
                    // or the next check, asks again. Asking again at once would keep
                    // every other relay holding its client's messages for nothing.
                }
                else if (relays.Count is 0)
                {
                    _phase = Phase.Installing;
                    unasked = _held;
                    client = _client;
                }
                else
                {
                    asking = new Attempt(_held, relays);
                    _attempt = asking;
                    _phase = Phase.Asking;
                }
            }
        }

        if (calledOff)
        {
            ScheduleTick();
            return;
        }

        if (lookAgain != Timeout.InfiniteTimeSpan)
        {
            WakeAt(now + (lookAgain < CheckInterval ? lookAgain : CheckInterval));
        }

        if (unasked is not null && client is not null)
        {
            Arm(_checkTimer, Timeout.InfiniteTimeSpan);
            BackgroundUpdateLog.InstallingUnasked(_logger, unasked.Version);
            _ = InstallAsync(unasked, client, InstallRoute.Unasked);
        }

        if (asking is not null)
        {
            Ask(asking, relays);
        }
    }

    /// <summary>
    /// While the question is out, a holder or a relay that was not asked calls it off.
    /// Under <see cref="_gate"/>.
    /// </summary>
    /// <param name="holding">What holds now.</param>
    /// <param name="relays">Every connected relay.</param>
    /// <returns>Whether this call called it off.</returns>
    private bool CallOffForNewcomers(Holding holding, IReadOnlyList<RelayState> relays)
    {
        if (_attempt is not { CalledOff: null } attempt)
        {
            return false;
        }

        if (holding.Any)
        {
            attempt.CalledOff = $"{holding.Summary} began to hold while the question was out";
            return true;
        }

        foreach (var relay in relays)
        {
            if (!attempt.Asked.Contains(relay.Id))
            {
                attempt.CalledOff = $"relay {relay.Id} connected while the question was out";
                return true;
            }
        }

        return false;
    }

    /// <summary>Asks every relay at once, under one deadline.</summary>
    /// <param name="attempt">The question.</param>
    /// <param name="relays">The relays, in the order the background listed them.</param>
    private void Ask(Attempt attempt, IReadOnlyList<RelayState> relays)
    {
        BackgroundUpdateLog.Asking(_logger, attempt.Version, relays.Count);
        attempt.StartDeadline(_clock, state => OnAskDeadline((Attempt)state!), ReadyToEndBound);

        foreach (var relay in relays)
        {
            _ = AskOneAsync(attempt, relay.Id);
        }

        // ⚠️ ONLY NOW MAY THE QUESTION SETTLE. Every relay has been asked, so a call
        // off sent from here reaches each one after its question, never before it,
        // and no relay is left holding its client's messages for a question that
        // was already over.
        lock (_gate)
        {
            attempt.Dispatched = true;
        }

        ScheduleTick();
    }

    /// <summary>Asks one relay and records its answer.</summary>
    /// <param name="attempt">The question.</param>
    /// <param name="relay">The relay.</param>
    /// <returns>The ask; it never faults.</returns>
    private async Task AskOneAsync(Attempt attempt, string relay)
    {
        RelayReadiness? answer;

        try
        {
            answer = await _relays.AskReadyToEndAsync(relay, attempt.Version, attempt.Token).WaitAsync(attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (attempt.Token.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // A relay that fails to answer has not answered: the update is called off for it, as for silence.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.SeamFailed(_logger, "Asking a relay whether it may end", failure);
            answer = null;
        }

        Answered(attempt, relay, answer);
    }

    /// <summary>Records one relay's answer.</summary>
    /// <param name="attempt">The question.</param>
    /// <param name="relay">The relay.</param>
    /// <param name="answer">Its answer, or <see langword="null"/> when asking it failed.</param>
    private void Answered(Attempt attempt, string relay, RelayReadiness? answer)
    {
        var now = _clock.GetUtcNow();
        bool settle;

        lock (_gate)
        {
            if (!ReferenceEquals(_attempt, attempt) || attempt.CalledOff is not null || attempt.AllYes)
            {
                return;
            }

            if (answer is null)
            {
                attempt.CalledOff = $"asking relay {relay} failed";
                attempt.Silent = true;
            }
            else if (!answer.Ready || answer.CallInFlight || answer.IdleAt > now)
            {
                // A yes that contradicts itself, a call in flight or a countdown
                // still running, is read as the no it describes.
                attempt.CalledOff = $"relay {relay} said no";
                attempt.RetryAt = answer.IdleAt > now ? answer.IdleAt : null;
            }
            else
            {
                _ = attempt.Yes.Add(relay);
                attempt.AllYes = attempt.Yes.Count == attempt.Asked.Count;
            }

            settle = attempt.CalledOff is not null || attempt.AllYes;
        }

        if (settle)
        {
            ScheduleTick();
        }
    }

    /// <summary>The question's deadline: whoever has not answered by now has not answered.</summary>
    /// <param name="attempt">The question.</param>
    private void OnAskDeadline(Attempt attempt)
    {
        try
        {
            bool settle;

            lock (_gate)
            {
                settle = ReferenceEquals(_attempt, attempt) && attempt.CalledOff is null && !attempt.AllYes;

                if (settle)
                {
                    var silent = string.Join(", ", attempt.Asked.Where(relay => !attempt.Yes.Contains(relay)));

                    attempt.CalledOff = string.Create(
                        CultureInfo.InvariantCulture,
                        $"relay {silent} did not answer within {ReadyToEndBound.TotalSeconds} s");
                    attempt.Silent = true;
                }
            }

            if (settle)
            {
                ScheduleTick();
            }
        }
#pragma warning disable CA1031 // An exception out of a timer callback ends the process; this one is a log line.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.TickFailed(_logger, failure);
        }
    }

    /// <summary>Reads both seams, or says why it could not.</summary>
    /// <param name="sessions">The sessions.</param>
    /// <param name="relays">The relays.</param>
    /// <returns>Whether both were read; when not, nothing is asked and nothing installs.</returns>
    private bool TryRead(out IReadOnlyList<ListedSession> sessions, out IReadOnlyList<RelayState> relays)
    {
        try
        {
            sessions = _sessions.Countdowns();
            relays = _relays.Connected();
            return true;
        }
#pragma warning disable CA1031 // What cannot be read cannot be shown to hold nothing, so the update stays held; a log line says why.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.SeamFailed(_logger, "Reading the sessions and the relays", failure);
            sessions = [];
            relays = [];
            return false;
        }
    }

    /// <summary>
    /// Why the person's install-now cannot start, as one sentence, or
    /// <see langword="null"/>. Under <see cref="_gate"/>.
    /// </summary>
    /// <param name="version">The version the page showed.</param>
    /// <returns>The sentence, or <see langword="null"/>.</returns>
    private string? WhyNotNow(string version)
    {
        // ⚠️ No tab hears this one, said 2026-10-10 for round 2 of the texts review, #125:
        // only Dispose sets the flag, and the background disposes its page first, after
        // telling every tab that it stops (Program.Background: the page's using
        // declaration follows this one's). It stays because a click that raced the stop
        // must not reach a disposed client, and the refusal is also the log's
        // InstallNowRefused line.
        if (_disposed)
        {
            return "BrowserAI is shutting down, so it installs nothing now.";
        }

        // The phase is Installing only while a package is held: _held goes back to null
        // only where the phase goes back to Idle. Corrected 2026-10-10, round 2 of the
        // texts review, #124 (previously a second form, "BrowserAI is already installing
        // an update.", for an Installing phase with nothing held, which nothing reaches).
        if (_phase is Phase.Installing)
        {
            return $"BrowserAI is already installing update {_held!.Version}.";
        }

        if (_held is not { } held || _client is null)
        {
            return "No update is downloaded and waiting, so there is nothing to install.";
        }

        // Corrected 2026-10-10, round 2 of the texts review, #122 (previously "..., so
        // reload the page to see what is waiting now."): every open tab is sent the new
        // version within a second of its arriving, so the page has already moved on, and
        // a reload would show this note again.
        if (!string.Equals(held.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            // ⚠️ And the texts polish of the same day, page #124 (previously it went on
            // "This page shows {waiting} now, with its own button."): the note stays until
            // the next Install now, so that sentence goes false, and the page under it
            // shows the waiting version with its own button.
            return $"The update waiting is {held.Version}, not {version}, so nothing was installed.";
        }

        return _downloading is { } newer
            ? $"BrowserAI is downloading update {newer}, so try again once the download has finished."
            : null;
    }

    /// <summary>Ends the relays, closes the sessions when the person asked, and hands over.</summary>
    /// <param name="candidate">The package.</param>
    /// <param name="client">The Velopack client.</param>
    /// <param name="route">Which way the install began.</param>
    /// <returns><see langword="null"/> once the package is handed over, and otherwise why it was not.</returns>
    private async Task<string?> InstallAsync(UpdateCandidate candidate, IBackgroundUpdateClient client, InstallRoute route)
    {
        var version = candidate.Version;

        try
        {
            if (route is InstallRoute.Person)
            {
                await EndRelaysAsync(version, now: true).ConfigureAwait(false);
                await CloseSessionsAsync().ConfigureAwait(false);
                Raise(static (toasts, version) => toasts.Installing(version), version, "installing");
            }
            else
            {
                Raise(static (toasts, version) => toasts.Installing(version), version, "installing");
                await EndRelaysAsync(version, now: false).ConfigureAwait(false);
            }

            return HandOver(candidate, client);
        }
        catch (OperationCanceledException) when (_ending.IsCancellationRequested)
        {
            // ⚠️ No tab hears this one either, said 2026-10-10 for round 2 of the texts
            // review, #126: only Dispose cancels _ending, after the page has gone, so the
            // sentence goes back to an install-now nobody shows. It stays as the answer a
            // stop that raced the install owes its caller, beside the log's own line.
            BackgroundUpdateLog.InstallAbandoned(_logger, version);
            return "BrowserAI stopped before the update could be handed to the installer, so nothing was installed.";
        }
    }

    /// <summary>Ends every relay, waiting for them at most <see cref="ReadyToEndBound"/>.</summary>
    /// <param name="version">The version that installs.</param>
    /// <param name="now">Whether the person's install-now asked for it.</param>
    /// <returns>The wait.</returns>
    private async Task EndRelaysAsync(string version, bool now)
    {
        try
        {
            await _relays.EndAllAsync(version, now, _ending).WaitAsync(ReadyToEndBound, _clock, _ending).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            BackgroundUpdateLog.RelaysDidNotEnd(_logger, ReadyToEndBound.TotalSeconds);
        }
#pragma warning disable CA1031 // A relay seam that fails to end the relays is a log line: Update.exe ends whatever is left under the install root.
        catch (Exception failure) when (failure is not OperationCanceledException || !_ending.IsCancellationRequested)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.SeamFailed(_logger, "Ending the relays", failure);
        }
    }

    /// <summary>Closes every session, waiting at most <see cref="SessionTimes.BrowserCloseCap"/>.</summary>
    /// <returns>The wait.</returns>
    private async Task CloseSessionsAsync()
    {
        try
        {
            await _sessions.CloseAllAsync(_ending).WaitAsync(SessionTimes.BrowserCloseCap, _clock, _ending).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            BackgroundUpdateLog.SessionsDidNotClose(_logger, SessionTimes.BrowserCloseCap.TotalSeconds);
        }
#pragma warning disable CA1031 // A session seam that fails to close is a log line: what is left ends when the background exits.
        catch (Exception failure) when (failure is not OperationCanceledException || !_ending.IsCancellationRequested)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.SeamFailed(_logger, "Closing the sessions", failure);
        }
    }

    /// <summary>Hands the package to <c>Update.exe</c> and asks the background to exit.</summary>
    /// <param name="candidate">The package.</param>
    /// <param name="client">The Velopack client.</param>
    /// <returns><see langword="null"/> once handed over, and otherwise why it was not.</returns>
    private string? HandOver(UpdateCandidate candidate, IBackgroundUpdateClient client)
    {
        var version = candidate.Version;
        var arguments = AfterUpdate.RestartArguments(version);

        try
        {
            client.ApplyAndRestartAfterThisProcessExits(candidate, arguments);
        }
#pragma warning disable CA1031 // A hand-over that throws installs nothing; the failed toast and a log line say so, and the background keeps running.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.HandOverFailed(_logger, version, failure);

            lock (_gate)
            {
                _ = _failed.Add(version);
                _held = null;
                _phase = Phase.Idle;
            }

            Raise(static (toasts, version) => toasts.Failed(version), version, "failed");
            ArmCheck(_clock.GetUtcNow() + CheckInterval);

            return $"The update to {version} could not be handed to the installer, so nothing was installed: {failure.Message}";
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var restartedWith = string.Join(' ', arguments);
            BackgroundUpdateLog.HandedOver(_logger, version, Environment.ProcessId, restartedWith);
        }

        try
        {
            _requestExit();
        }
#pragma warning disable CA1031 // The package is handed over either way: Update.exe ends this process after its 60 s wait.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.ExitRequestFailed(_logger, failure);
        }

        return null;
    }

    /// <summary>What holds the update at one moment.</summary>
    /// <param name="Sessions">How many sessions hold it.</param>
    /// <param name="Relays">How many relays hold it.</param>
    /// <param name="Deadline">The earliest countdown among them, or <see langword="null"/> when none of them has one.</param>
    private readonly record struct Holding(int Sessions, int Relays, DateTimeOffset? Deadline)
    {
        /// <summary>Whether anything holds it.</summary>
        public bool Any => Sessions + Relays > 0;

        /// <summary>One clause for the log.</summary>
        public string Summary => string.Create(CultureInfo.InvariantCulture, $"{Sessions} session(s) and {Relays} relay(s)");

        /// <summary>Reads the holders out of the two seams' lists.</summary>
        /// <param name="now">The moment.</param>
        /// <param name="sessions">Every listed session.</param>
        /// <param name="relays">Every connected relay.</param>
        /// <returns>What holds.</returns>
        public static Holding Of(DateTimeOffset now, IReadOnlyList<ListedSession> sessions, IReadOnlyList<RelayState> relays)
        {
            var bySessions = 0;
            var byRelays = 0;
            DateTimeOffset? earliest = null;

            foreach (var session in sessions)
            {
                if (session.ClosesAt is not { } closes)
                {
                    bySessions++;
                }
                else if (closes > now)
                {
                    bySessions++;
                    earliest = earliest is { } known && known <= closes ? known : closes;
                }
            }

            foreach (var relay in relays)
            {
                if (relay.IdleAt > now)
                {
                    byRelays++;
                    earliest = earliest is { } known && known <= relay.IdleAt ? known : relay.IdleAt;
                }
                else if (relay.CallInFlight)
                {
                    byRelays++;
                }
            }

            return new Holding(bySessions, byRelays, earliest);
        }
    }

    /// <summary>Two bounds on one download, on the core's clock.</summary>
    private sealed class DownloadDeadlines : IDisposable
    {
        private readonly CancellationTokenSource _source;
        private readonly ITimer _absolute;
        private readonly ITimer _stall;
        private readonly TimeSpan _stallAfter;
        private string? _expired;

        /// <summary>Arms both bounds.</summary>
        /// <param name="clock">The core's clock.</param>
        /// <param name="absolute">The whole download, however fast it goes.</param>
        /// <param name="stall">How long it may make no progress.</param>
        /// <param name="ending">Cancelled when the background goes down.</param>
        public DownloadDeadlines(TimeProvider clock, TimeSpan absolute, TimeSpan stall, CancellationToken ending)
        {
            _source = CancellationTokenSource.CreateLinkedTokenSource(ending);
            Token = _source.Token;
            _stallAfter = stall;
            _absolute = clock.CreateTimer(static state => ((DownloadDeadlines)state!).Expire("its absolute budget ran out"), this, absolute, Timeout.InfiniteTimeSpan);
            _stall = clock.CreateTimer(static state => ((DownloadDeadlines)state!).Expire("it made no progress for the length of its stall budget"), this, stall, Timeout.InfiniteTimeSpan);
        }

        /// <summary>What the download is handed.</summary>
        public CancellationToken Token { get; }

        /// <summary>Which bound ran out, or <see langword="null"/>.</summary>
        public string? Expired => Volatile.Read(ref _expired);

        /// <summary>The download moved: the stall bound starts again.</summary>
        public void Progressed() => Arm(_stall, _stallAfter);

        /// <inheritdoc />
        public void Dispose()
        {
            _absolute.Dispose();
            _stall.Dispose();
            _source.Dispose();
        }

        /// <summary>One bound ran out: the download is cancelled once.</summary>
        /// <param name="why">Which, as a clause.</param>
        private void Expire(string why)
        {
            if (Interlocked.CompareExchange(ref _expired, why, null) is not null)
            {
                return;
            }

            try
            {
                _source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The download had finished and the bounds were disposed under it.
            }
        }
    }

    /// <summary>One question to the relays, from the ask to its settling.</summary>
    private sealed class Attempt : IDisposable
    {
        private readonly CancellationTokenSource _asks = new();
        private ITimer? _deadline;
        private int _disposed;

        /// <summary>Opens a question about one package to a set of relays.</summary>
        /// <param name="candidate">The package.</param>
        /// <param name="relays">The relays asked.</param>
        public Attempt(UpdateCandidate candidate, IReadOnlyList<RelayState> relays)
        {
            Candidate = candidate;
            Asked = [.. relays.Select(relay => relay.Id)];
            Token = _asks.Token;
        }

        /// <summary>The package the question is about.</summary>
        public UpdateCandidate Candidate { get; }

        /// <summary>Its version.</summary>
        public string Version => Candidate.Version;

        /// <summary>The relays asked.</summary>
        public HashSet<string> Asked { get; }

        /// <summary>The relays that said yes.</summary>
        public HashSet<string> Yes { get; } = new(StringComparer.Ordinal);

        /// <summary>Cancelled once the question is settled either way.</summary>
        public CancellationToken Token { get; }

        /// <summary>Why it was called off, once something did.</summary>
        public string? CalledOff { get; set; }

        /// <summary>Whether every relay asked said yes.</summary>
        public bool AllYes { get; set; }

        /// <summary>Whether every relay has been asked.</summary>
        public bool Dispatched { get; set; }

        /// <summary>Whether the core has acted on how it ended.</summary>
        public bool Settled { get; set; }

        /// <summary>The earliest countdown a no carried, before which nobody is asked again.</summary>
        public DateTimeOffset? RetryAt { get; set; }

        /// <summary>Whether a relay did not answer.</summary>
        public bool Silent { get; set; }

        /// <summary>Arms the deadline every answer must beat.</summary>
        /// <param name="clock">The core's clock.</param>
        /// <param name="expired">What runs when it passes.</param>
        /// <param name="after">How long from now.</param>
        public void StartDeadline(TimeProvider clock, TimerCallback expired, TimeSpan after) =>
            _deadline = clock.CreateTimer(expired, this, after, Timeout.InfiniteTimeSpan);

        /// <summary>Ends the asks still waiting and the deadline. Once.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            _deadline?.Dispose();

            try
            {
                _asks.Cancel();
            }
            finally
            {
                _asks.Dispose();
            }
        }
    }
}

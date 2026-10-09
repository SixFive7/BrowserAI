// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>What Windows answered an update of a toast's bound fields.</summary>
/// <remarks>The values of <c>Windows.UI.Notifications.NotificationUpdateResult</c>.</remarks>
internal enum ToastUpdateResult
{
    /// <summary>The toast took the values.</summary>
    Succeeded = 0,

    /// <summary>The update failed for a reason Windows does not name.</summary>
    Failed = 1,

    /// <summary>There is no such toast: the person acted on it or dismissed it, or it expired.</summary>
    NotificationNotFound = 2,
}

/// <summary>
/// What shows, updates and removes BrowserAI's toasts: Windows in the product, a
/// recording in the suite.
/// </summary>
/// <remarks>
/// <b>The seam that keeps the suite off the screen</b> (Q278): everything above it
/// is ordinary code the suite drives with a clock it moves, and the one
/// implementation that reaches Windows is <c>WindowsToastSurface</c>.
/// </remarks>
internal interface IToastSurface
{
    /// <summary>Shows a toast under a tag and group, replacing any toast that has both.</summary>
    /// <param name="tag">Its tag.</param>
    /// <param name="group">Its group.</param>
    /// <param name="toast">What to show.</param>
    /// <param name="sequence">The sequence number of its first values, when it binds any.</param>
    void Show(string tag, string group, ToastRequest toast, uint sequence);

    /// <summary>Gives a shown toast's bound fields new values.</summary>
    /// <param name="tag">Its tag.</param>
    /// <param name="group">Its group.</param>
    /// <param name="values">The values.</param>
    /// <param name="sequence">Higher than every sequence number before it, so Windows shows the newest.</param>
    /// <returns>What Windows answered.</returns>
    ToastUpdateResult Update(string tag, string group, IReadOnlyDictionary<string, string> values, uint sequence);

    /// <summary>Removes a toast from the screen and from the Notification Centre.</summary>
    /// <param name="tag">Its tag.</param>
    /// <param name="group">Its group.</param>
    void Remove(string tag, string group);
}

/// <summary>Which version the person answered <i>Wait for inactivity</i> for, kept across processes.</summary>
/// <remarks>
/// <b>The click and the toast belong to two processes</b>: a click starts the
/// activator, and the background raises the toast. The background reads what the
/// activator wrote when it raises the ready toast for a version again.
/// </remarks>
internal interface IUpdateToastMemory
{
    /// <summary>The version the person last chose to wait for, or <see langword="null"/>.</summary>
    /// <returns>The version.</returns>
    string? WaitedFor();

    /// <summary>Records that the person chose to wait for a version.</summary>
    /// <param name="version">The version.</param>
    void Waited(string version);

    /// <summary>Forgets it: the update installed or failed.</summary>
    void Forget();
}

/// <summary>
/// <see cref="IUpdateToastMemory"/> as one small file in the data root, holding the
/// version and nothing else.
/// </summary>
/// <param name="path">The file.</param>
internal sealed class UpdateToastMemoryFile(string path) : IUpdateToastMemory
{
    /// <summary>The file's name in the data root.</summary>
    public const string FileName = "update-toast-wait.txt";

    /// <summary>The memory of one data root.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <returns>The memory.</returns>
    public static UpdateToastMemoryFile In(string dataRoot) => new(Path.Combine(dataRoot, FileName));

    /// <inheritdoc />
    public string? WaitedFor() =>
        File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } version ? version : null;

    /// <inheritdoc />
    public void Waited(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, version);
    }

    /// <inheritdoc />
    public void Forget() => File.Delete(path);
}

/// <summary>No toasts: a process that runs under no application id raises none.</summary>
internal sealed class NoUpdateToasts : IUpdateToasts
{
    /// <summary>The one instance.</summary>
    public static NoUpdateToasts Instance { get; } = new();

    /// <inheritdoc />
    public void Held(string version)
    {
    }

    /// <inheritdoc />
    public void Installing(string version)
    {
    }

    /// <inheritdoc />
    public void Installed(string version)
    {
    }

    /// <inheritdoc />
    public void Failed(string version)
    {
    }
}

/// <summary>What the failed toast names, which only the process that raises it knows.</summary>
/// <param name="RunningVersion">The version this process is, which a failed update leaves installed.</param>
/// <param name="VelopackLog">Where Velopack's log is.</param>
/// <param name="BrowserAiLog">Where BrowserAI's log is.</param>
internal sealed record UpdateToastFacts(string RunningVersion, string VelopackLog, string BrowserAiLog);

/// <summary>
/// The four update toasts, raised through a <see cref="IToastSurface"/>, with the
/// ready toast's countdown kept live once a second.
/// </summary>
/// <remarks>
/// <para>
/// <b>The countdown is read from <see cref="IUpdateHolds"/> every second</b>, which
/// the background answers from its own memory, and written to the toast through
/// <see cref="IToastSurface.Update"/> with a sequence number one higher each time.
/// It stops when Windows answers that the toast is gone, which is what a person's
/// click or dismissal leaves, and when the update stops waiting.
/// </para>
/// <para>
/// <b>The ready toast is raised once per version in a process's life.</b> A
/// version the person answered <i>Wait for inactivity</i> for is raised again
/// straight into the Notification Centre, with no banner: they have already said
/// they will wait.
/// </para>
/// <para>
/// <b>Nothing here throws into its caller.</b> A toast that Windows refuses is a
/// log record; the background and the after-update start go on either way.
/// </para>
/// </remarks>
internal sealed partial class UpdateToasts : IUpdateToasts, IDisposable
{
    /// <summary>How often the countdown is written: <b>once a second</b>, the unit it counts in.</summary>
    public static TimeSpan Tick { get; } = UpdateBudgets.ToastTick;

    private readonly Lock _gate = new();
    private readonly IToastSurface _surface;
    private readonly IUpdateHolds? _holds;
    private readonly IUpdateToastMemory _memory;
    private readonly UpdateToastFacts _facts;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private readonly ILogger _logger;

    private readonly IDisposable? _owned;

    private string? _raisedFor;
    private ITimer? _countdown;
    private CountdownTrack _track;
    private uint _sequence;

    /// <summary>The toasts for one process.</summary>
    /// <param name="surface">What shows them.</param>
    /// <param name="holds">What holds the update, read for the ready toast; <see langword="null"/> in a process that only raises the last two.</param>
    /// <param name="memory">Which version the person chose to wait for.</param>
    /// <param name="facts">What the failed toast names.</param>
    /// <param name="clock">The clock the countdown runs on.</param>
    /// <param name="zone">The time zone the install time is said in.</param>
    /// <param name="logger">Where refusals are recorded.</param>
    /// <param name="owned">What this disposes with itself: the product's surface, which it made.</param>
    public UpdateToasts(
        IToastSurface surface,
        IUpdateHolds? holds,
        IUpdateToastMemory memory,
        UpdateToastFacts facts,
        TimeProvider clock,
        TimeZoneInfo zone,
        ILogger logger,
        IDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(logger);

        _surface = surface;
        _holds = holds;
        _memory = memory;
        _facts = facts;
        _clock = clock;
        _zone = zone;
        _logger = logger;
        _owned = owned;
    }

    /// <summary>Whether the ready toast's countdown is running.</summary>
    public bool Counting
    {
        get
        {
            lock (_gate)
            {
                return _countdown is not null;
            }
        }
    }

    /// <inheritdoc />
    public void Held(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (_holds is null)
        {
            return;
        }

        lock (_gate)
        {
            if (string.Equals(_raisedFor, version, StringComparison.Ordinal))
            {
                return;
            }
        }

        // Read OUTSIDE this lock: the background answers it under a lock of its own,
        // and may call in here while it holds that one.
        if (Read() is not { } holds)
        {
            return;
        }

        lock (_gate)
        {
            if (string.Equals(_raisedFor, version, StringComparison.Ordinal))
            {
                return;
            }

            StopCounting();
            _raisedFor = version;

            var (values, track) = UpdateToastContent.ReadyData(holds, _clock.GetUtcNow(), _zone, default);
            var waited = string.Equals(Remembered(), version, StringComparison.Ordinal);

            _track = track;
            _sequence = 1;

            if (!Raise(
                UpdateToastContent.ReadyTag,
                new ToastRequest(UpdateToastContent.Ready(version, holds), values, SuppressPopup: waited, ExpiresOnReboot: true),
                _sequence))
            {
                return;
            }

            _countdown = _clock.CreateTimer(_ => Count(), null, Tick, Tick);
        }
    }

    /// <inheritdoc />
    public void Installing(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        lock (_gate)
        {
            StopCounting();
            _ = Raise(UpdateToastContent.InstallingTag, new ToastRequest(UpdateToastContent.Installing(version), null, SuppressPopup: false, ExpiresOnReboot: true), 0);
        }
    }

    /// <inheritdoc />
    public void Installed(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        lock (_gate)
        {
            StopCounting();
            Forget();
            _ = Raise(UpdateToastContent.InstalledTag, new ToastRequest(UpdateToastContent.Installed(version), null, SuppressPopup: false, ExpiresOnReboot: false), 0);
        }
    }

    /// <inheritdoc />
    public void Failed(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        lock (_gate)
        {
            StopCounting();
            Forget();
            _ = Raise(
                UpdateToastContent.FailedTag,
                new ToastRequest(UpdateToastContent.Failed(version, _facts.RunningVersion, _facts.VelopackLog, _facts.BrowserAiLog), null, SuppressPopup: false, ExpiresOnReboot: false),
                0);
        }
    }

    /// <summary>
    /// The toasts of this process, raised through Windows under the application id
    /// it runs under, or none when it runs under no id.
    /// </summary>
    /// <remarks>
    /// <b>What the background and the after-update start call</b>: the background
    /// hands over what holds its update, and the after-update start, which raises
    /// only the installed and failed toasts, hands over nothing.
    /// </remarks>
    /// <param name="holds">What holds the update, or <see langword="null"/>.</param>
    /// <param name="paths">Where the data root and the log are.</param>
    /// <param name="logger">Where refusals are recorded.</param>
    /// <returns>The toasts; dispose them when the process ends.</returns>
    public static IUpdateToasts ForThisProcess(IUpdateHolds? holds, IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (ToastInterop.CurrentAppUserModelId() is not { } id)
        {
            return NoUpdateToasts.Instance;
        }

        var surface = new WindowsToastSurface(id);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var facts = new UpdateToastFacts(BuildVersion.Current, VelopackLogFor(id), Shortened(paths.LogDirectory, local));

        return new UpdateToasts(surface, holds, UpdateToastMemoryFile.In(paths.RootAppDir), facts, TimeProvider.System, TimeZoneInfo.Local, logger, owned: surface);
    }

    /// <summary>
    /// Velopack's own log for an application id, as a person types it:
    /// <c>%LocalAppData%\velopack\velopack_&lt;pack id&gt;.log</c>, the pack id being the
    /// id without Velopack's <c>velopack.</c> prefix.
    /// </summary>
    /// <param name="appUserModelId">The application id.</param>
    /// <returns>The path.</returns>
    public static string VelopackLogFor(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);

        const string Prefix = "velopack.";
        var pack = appUserModelId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? appUserModelId[Prefix.Length..] : appUserModelId;

        return $@"%LocalAppData%\velopack\velopack_{pack}.log";
    }

    /// <summary>A path under the local application data folder, written the way a person types it.</summary>
    /// <param name="path">The path.</param>
    /// <param name="localAppData">The local application data folder.</param>
    /// <returns><c>%LocalAppData%\...</c>, or the path unchanged when it is elsewhere.</returns>
    public static string Shortened(string path, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(localAppData);

        var root = localAppData.TrimEnd('\\') + '\\';

        return localAppData.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? @"%LocalAppData%\" + path[root.Length..]
            : path;
    }

    /// <summary>Stops the countdown, and lets go of the surface when this owns it.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            StopCounting();
        }

        _owned?.Dispose();
    }

    /// <summary>One second of the countdown.</summary>
    private void Count()
    {
        string? version;

        lock (_gate)
        {
            version = _countdown is null ? null : _raisedFor;
        }

        // Read outside the lock, for the reason Held gives.
        if (version is null || Read() is not { } holds)
        {
            return;
        }

        lock (_gate)
        {
            if (_countdown is null || !string.Equals(_raisedFor, version, StringComparison.Ordinal))
            {
                return;
            }

            if (holds.State is UpdateHoldState.None || !string.Equals(holds.Version, version, StringComparison.Ordinal))
            {
                // The update this toast is about no longer waits, and nothing else will
                // replace the toast: it goes, so nothing counts down to an update that
                // no longer waits.
                StopCounting();
                Remove(UpdateToastContent.ReadyTag);
                UpdateToastsLog.NoLongerWaiting(_logger, version);
                return;
            }

            if (holds.State is UpdateHoldState.Installing)
            {
                // The installing toast is on its way from the background.
                StopCounting();
                return;
            }

            var (values, track) = UpdateToastContent.ReadyData(holds, _clock.GetUtcNow(), _zone, _track);

            _track = track;

            ToastUpdateResult result;

            try
            {
                result = _surface.Update(UpdateToastContent.ReadyTag, UpdateToastContent.Group, values, ++_sequence);
            }
#pragma warning disable CA1031 // An update Windows threw on is a log record; the next second tries again.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                UpdateToastsLog.UpdateThrew(_logger, failure);
                return;
            }

            if (result is ToastUpdateResult.NotificationNotFound)
            {
                // The person clicked it or dismissed it, or it expired: there is nothing
                // left to count down on.
                StopCounting();
                UpdateToastsLog.Gone(_logger, version);
            }
            else if (result is ToastUpdateResult.Failed)
            {
                UpdateToastsLog.UpdateFailed(_logger, _sequence);
            }
        }
    }

    /// <summary>What holds the update, or <see langword="null"/> when it could not be read.</summary>
    /// <returns>The snapshot.</returns>
    private UpdateHoldSnapshot? Read()
    {
        try
        {
            return _holds?.Read();
        }
#pragma warning disable CA1031 // A read that failed is a toast not raised or one second without an update, never a background that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateToastsLog.ReadFailed(_logger, failure);
            return null;
        }
    }

    /// <summary>Removes every other update toast, then shows one. Called under the lock.</summary>
    /// <param name="tag">The toast's tag.</param>
    /// <param name="toast">What to show.</param>
    /// <param name="sequence">The sequence number of its first values.</param>
    /// <returns>Whether Windows took it.</returns>
    private bool Raise(string tag, ToastRequest toast, uint sequence)
    {
        foreach (var other in UpdateToastContent.Tags.Where(other => !string.Equals(other, tag, StringComparison.Ordinal)))
        {
            Remove(other);
        }

        try
        {
            _surface.Show(tag, UpdateToastContent.Group, toast, sequence);
            UpdateToastsLog.Shown(_logger, tag, toast.SuppressPopup);
            return true;
        }
#pragma warning disable CA1031 // A toast Windows refused is a log record, never a background that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateToastsLog.ShowThrew(_logger, tag, failure);
            return false;
        }
    }

    private void Remove(string tag)
    {
        try
        {
            _surface.Remove(tag, UpdateToastContent.Group);
        }
#pragma warning disable CA1031 // A toast that could not be removed is a log record.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateToastsLog.RemoveThrew(_logger, tag, failure);
        }
    }

    private string? Remembered()
    {
        try
        {
            return _memory.WaitedFor();
        }
#pragma warning disable CA1031 // A memory that cannot be read means the banner shows.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateToastsLog.MemoryFailed(_logger, failure);
            return null;
        }
    }

    private void Forget()
    {
        try
        {
            _memory.Forget();
        }
#pragma warning disable CA1031 // A memory that cannot be cleared costs one silent ready toast later.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateToastsLog.MemoryFailed(_logger, failure);
        }
    }

    private void StopCounting()
    {
        _countdown?.Dispose();
        _countdown = null;
    }
}

/// <summary>The update toasts' records.</summary>
internal static partial class UpdateToastsLog
{
    /// <summary>A toast was shown.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="tag">Which.</param>
    /// <param name="silent">Whether it went to the Notification Centre without a banner.</param>
    [LoggerMessage(EventId = 7101, Level = LogLevel.Information, Message = "Raised the update toast '{Tag}' (no banner: {Silent}).")]
    public static partial void Shown(ILogger logger, string tag, bool silent);

    /// <summary>Windows threw on a show.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="tag">Which.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "Windows did not show the update toast '{Tag}'.")]
    public static partial void ShowThrew(ILogger logger, string tag, Exception failure);

    /// <summary>Windows threw on a removal.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="tag">Which.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning, Message = "Windows did not remove the update toast '{Tag}'.")]
    public static partial void RemoveThrew(ILogger logger, string tag, Exception failure);

    /// <summary>Windows threw on an update.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(EventId = 7104, Level = LogLevel.Warning, Message = "Windows threw on an update of the ready toast's countdown.")]
    public static partial void UpdateThrew(ILogger logger, Exception failure);

    /// <summary>Windows answered an update with a failure.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="sequence">The sequence number it refused.</param>
    [LoggerMessage(EventId = 7105, Level = LogLevel.Warning, Message = "Windows refused update {Sequence} of the ready toast's countdown.")]
    public static partial void UpdateFailed(ILogger logger, uint sequence);

    /// <summary>The ready toast is gone, so its countdown stopped.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="version">The version it was about.</param>
    [LoggerMessage(EventId = 7106, Level = LogLevel.Information, Message = "The ready toast for {Version} is gone, so its countdown stopped.")]
    public static partial void Gone(ILogger logger, string version);

    /// <summary>The update no longer waits, so its ready toast was removed.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="version">The version it was about.</param>
    [LoggerMessage(EventId = 7107, Level = LogLevel.Information, Message = "{Version} no longer waits to install, so its ready toast was removed.")]
    public static partial void NoLongerWaiting(ILogger logger, string version);

    /// <summary>What holds the update could not be read.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(EventId = 7108, Level = LogLevel.Warning, Message = "What holds the update could not be read for the ready toast's countdown.")]
    public static partial void ReadFailed(ILogger logger, Exception failure);

    /// <summary>The memory of a person's wait could not be read or cleared.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(EventId = 7109, Level = LogLevel.Warning, Message = "The record of which update the person chose to wait for could not be read or cleared.")]
    public static partial void MemoryFailed(ILogger logger, Exception failure);
}

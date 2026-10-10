// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.Updates;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The background's update core with every seam a double and the clock a
/// <see cref="ManualClock"/>: nothing is started, nothing touches Velopack, and
/// nothing waits on real time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every double writes what it was asked into one ordered record</b>,
/// <see cref="Events"/>, so an arm can assert both that a step happened and in
/// which order the steps came: the toast before the relays end, the relays before
/// the hand-over, the hand-over before the exit.
/// </para>
/// <para>
/// <b>The core runs on its clock</b>, so a step it schedules runs when the clock is
/// moved: <see cref="Settle"/> moves it by nothing, which runs everything that is
/// due now and every step that step schedules in turn.
/// </para>
/// </remarks>
internal sealed class BackgroundUpdateRig : IDisposable
{
    /// <summary>The pack id the rig's install came from.</summary>
    public const string PackId = "BrowserAI.app";

    /// <summary>A pack id the rig's install did not come from: the suite's own test pack.</summary>
    public const string OtherPackId = "BrowserAI.app.test";

    private readonly List<BackgroundUpdates> _built = [];

    /// <summary>Builds a rig whose clock reads a fixed day and whose seams hold nothing.</summary>
    public BackgroundUpdateRig()
    {
        // A day in, so that "the last check was eleven minutes ago" is a moment
        // after the epoch and every timestamp in a failure message reads as a time.
        Clock.Advance(TimeSpan.FromDays(1));

        Client = new ScriptedBackgroundClient(Events);
        Relays = new ScriptedRelays(Events);
        Sessions = new ScriptedSessions(Events);
        Toasts = new RecordedToasts(Events);
    }

    /// <summary>The clock every core this rig builds runs on.</summary>
    public ManualClock Clock { get; } = new();

    /// <summary>What every double was asked, in order.</summary>
    public ConcurrentQueue<string> Events { get; } = new();

    /// <summary>The Velopack double.</summary>
    public ScriptedBackgroundClient Client { get; }

    /// <summary>The relays double.</summary>
    public ScriptedRelays Relays { get; }

    /// <summary>The sessions double.</summary>
    public ScriptedSessions Sessions { get; }

    /// <summary>The toasts double.</summary>
    public RecordedToasts Toasts { get; }

    /// <summary>Every log record the cores wrote.</summary>
    public CapturingLoggerProvider Log { get; } = new();

    /// <summary>How many times a core asked the background to exit.</summary>
    public int ExitRequests => Events.Count(entry => entry is "exit");

    /// <summary>How often the client factory was asked for a client.</summary>
    public int ClientsBuilt { get; private set; }

    /// <summary>The install a core is built for: installed, a release, the rig's pack.</summary>
    public UpdateInstall Install { get; set; } = new(true, PackId, "1.1.0");

    /// <summary>The source a core is built with: a folder, unless an arm says otherwise.</summary>
    public UpdateSourceReading Source { get; set; } = UpdateSource.Read([UpdateSource.Argument, @"C:\BrowserAI-feed"]);

    /// <summary>The record of the last check: in memory, unless an arm hands a file.</summary>
    public IUpdateCheckStamp Stamp { get; set; } = new MemoryCheckStamp();

    /// <summary>A candidate of the rig's own pack.</summary>
    /// <param name="version">Its version.</param>
    /// <param name="packId">Its pack id.</param>
    /// <returns>The candidate.</returns>
    public static UpdateCandidate Candidate(string version, string? packId = PackId) => new()
    {
        Version = version,
        IsDowngrade = false,
        DeltaCount = 0,
        FullPackageSize = 1,
        PackId = packId,
    };

    /// <summary>Builds a core over the rig's seams and starts it, then runs what is due now.</summary>
    /// <returns>The core.</returns>
    public BackgroundUpdates Start()
    {
        var updates = Build();
        updates.Start();
        Settle();
        return updates;
    }

    /// <summary>Builds a core over the rig's seams without starting it.</summary>
    /// <returns>The core; the rig disposes it.</returns>
    public BackgroundUpdates Build()
    {
        var updates = new BackgroundUpdates(
            Install,
            Source,
            _ =>
            {
                ClientsBuilt++;
                return Client;
            },
            Relays,
            Sessions,
            Toasts,
            Stamp,
            () => Events.Enqueue("exit"),
            Clock,
            Log.CreateLogger("BrowserAI.Updates"));

        _built.Add(updates);
        return updates;
    }

    /// <summary>Runs everything the cores have made due by now.</summary>
    public void Settle() => Clock.Advance(TimeSpan.Zero);

    /// <summary>The recorded events that start with a prefix, in order.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The events.</returns>
    public List<string> EventsStartingWith(string prefix) =>
        [.. Events.Where(entry => entry.StartsWith(prefix, StringComparison.Ordinal))];

    /// <summary>Where an event first appears in the record, or -1.</summary>
    /// <param name="entry">The event.</param>
    /// <returns>Its index.</returns>
    public int IndexOf(string entry) => Events.ToList().IndexOf(entry);

    /// <summary>Whether any record carries an event id.</summary>
    /// <param name="eventId">The id.</param>
    /// <returns>Whether one does.</returns>
    public bool Logged(int eventId) => Log.Records.Any(record => record.EventId.Id == eventId);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var updates in _built)
        {
            updates.Dispose();
        }

        Log.Dispose();
    }
}

/// <summary>A Velopack client that offers what the arm scripts and records what it is asked.</summary>
/// <param name="events">The rig's record.</param>
internal sealed class ScriptedBackgroundClient(ConcurrentQueue<string> events) : IBackgroundUpdateClient
{
    /// <inheritdoc />
    public string ManifestUrl => @"C:\BrowserAI-feed\releases.win.json";

    /// <summary>What the next check offers.</summary>
    public UpdateCandidate? Offer { get; set; }

    /// <summary>What the packages directory holds that is above the installed version.</summary>
    public UpdateCandidate? StagedCandidate { get; set; }

    /// <summary>What a hand-over throws, if anything.</summary>
    public Exception? ApplyFailure { get; set; }

    /// <summary>What a download throws, if anything; a download that throws leaves the packages directory as it was.</summary>
    public Exception? DownloadFailure { get; set; }

    /// <summary>How many checks ran.</summary>
    public int Checks => events.Count(entry => entry is "check");

    /// <summary>How many downloads ran.</summary>
    public int Downloads => events.Count(entry => entry.StartsWith("download ", StringComparison.Ordinal));

    /// <summary>What a check throws, as a feed that is down does, or <see langword="null"/>.</summary>
    public Exception? CheckFailure { get; set; }

    /// <summary>
    /// Whether a check never answers and ignores its token, as Velopack's own check
    /// does once it has started.
    /// </summary>
    public bool CheckHangs { get; set; }

    /// <summary>
    /// Whether a download waits for the arm: it reports progress through
    /// <see cref="ReportProgress"/> and ends at <see cref="FinishDownload"/> or when
    /// its token fires.
    /// </summary>
    public bool DownloadWaits { get; set; }

    private Action<int>? _progress;
    private TaskCompletionSource? _download;

    /// <summary>Reports progress for the download that waits.</summary>
    /// <param name="percent">How far it is.</param>
    public void ReportProgress(int percent) =>
        (_progress ?? throw new InvalidOperationException("No download is waiting."))(percent);

    /// <summary>Lets the download that waits finish.</summary>
    public void FinishDownload() =>
        _ = (_download ?? throw new InvalidOperationException("No download is waiting.")).TrySetResult();

    /// <inheritdoc />
    public Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken)
    {
        events.Enqueue("check");

        if (CheckFailure is { } failure)
        {
            return Task.FromException<UpdateCandidate?>(failure);
        }

        return CheckHangs ? new TaskCompletionSource<UpdateCandidate?>().Task : Task.FromResult(Offer);
    }

    /// <inheritdoc />
    public Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(progress);

        events.Enqueue($"download {candidate.Version}");

        if (DownloadFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        if (DownloadWaits)
        {
            return WaitForTheArmAsync(candidate, progress, cancellationToken);
        }

        progress(100);
        StagedCandidate = candidate;
        return Task.CompletedTask;
    }

    /// <summary>A download that moves only when the arm says so.</summary>
    /// <param name="candidate">What is downloaded.</param>
    /// <param name="progress">Where its progress goes.</param>
    /// <param name="cancellationToken">The core's deadlines.</param>
    /// <returns>The download; it ends cancelled when the token fires first.</returns>
    private async Task WaitForTheArmAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource();
        _progress = progress;
        _download = finished;

        using (cancellationToken.Register(() => finished.TrySetCanceled(cancellationToken)))
        {
            await finished.Task;
        }

        StagedCandidate = candidate;
    }

    /// <inheritdoc />
    public UpdateCandidate? Staged() => StagedCandidate;

    /// <inheritdoc />
    public void ApplyAndRestartAfterThisProcessExits(UpdateCandidate candidate, IReadOnlyList<string> restartArguments)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(restartArguments);

        if (ApplyFailure is { } failure)
        {
            throw failure;
        }

        events.Enqueue($"apply {candidate.Version} with [{string.Join(' ', restartArguments)}]");
    }
}

/// <summary>Relays the arm lists and answers for, recording what they are told.</summary>
/// <param name="events">The rig's record.</param>
internal sealed class ScriptedRelays(ConcurrentQueue<string> events) : IUpdateRelays
{
    private readonly Lock _gate = new();
    private readonly List<RelayState> _listed = [];
    private readonly Dictionary<string, TaskCompletionSource<RelayReadiness>> _waiting = new(StringComparer.Ordinal);
    private int _namedReads;

    /// <summary>
    /// How a relay answers <i>ready to end?</i>: an answer at once, or
    /// <see langword="null"/> for no answer until the arm gives one through
    /// <see cref="AnswerLater"/>.
    /// </summary>
    public Func<string, RelayReadiness?> Answer { get; set; } = _ => null;

    /// <summary>Lists a relay as connected, replacing one with the same id.</summary>
    /// <param name="relay">The relay.</param>
    public void Connect(RelayState relay)
    {
        lock (_gate)
        {
            _ = _listed.RemoveAll(listed => listed.Id == relay.Id);
            _listed.Add(relay);
        }
    }

    /// <summary>Removes a relay.</summary>
    /// <param name="id">Its id.</param>
    public void Disconnect(string id)
    {
        lock (_gate)
        {
            _ = _listed.RemoveAll(listed => listed.Id == id);
        }
    }

    /// <summary>Answers a question a relay left waiting.</summary>
    /// <param name="id">The relay.</param>
    /// <param name="answer">Its answer.</param>
    /// <returns>Whether a question was waiting.</returns>
    public bool AnswerLater(string id, RelayReadiness answer)
    {
        TaskCompletionSource<RelayReadiness>? waiting;

        lock (_gate)
        {
            _ = _waiting.Remove(id, out waiting);
        }

        return waiting?.TrySetResult(answer) ?? false;
    }

    /// <inheritdoc />
    public IReadOnlyList<RelayState> Connected()
    {
        lock (_gate)
        {
            return [.. _listed];
        }
    }

    /// <summary>
    /// What <see cref="ConnectedWithNames"/> gives a relay, by its id, as a background
    /// that read its client's records would: its conversation, its name and its window.
    /// </summary>
    public ConcurrentDictionary<string, (string? Conversation, ConversationName? Label, ClientWindow? Window)> Names { get; } = new(StringComparer.Ordinal);

    /// <summary>How many times the core asked for the relays with their names, which is a read of every client's records.</summary>
    public int NamedReads => Volatile.Read(ref _namedReads);

    /// <inheritdoc />
    public IReadOnlyList<RelayState> ConnectedWithNames()
    {
        _ = Interlocked.Increment(ref _namedReads);

        lock (_gate)
        {
            return
            [
                .. _listed.Select(relay => Names.TryGetValue(relay.Id, out var name)
                    ? relay with { Conversation = name.Conversation, Label = name.Label, Window = name.Window }
                    : relay),
            ];
        }
    }

    /// <inheritdoc />
    public Task<RelayReadiness> AskReadyToEndAsync(string relay, string version, CancellationToken cancellationToken)
    {
        events.Enqueue($"ask {relay} {version}");

        if (Answer(relay) is { } now)
        {
            return Task.FromResult(now);
        }

        var later = new TaskCompletionSource<RelayReadiness>();

        lock (_gate)
        {
            _waiting[relay] = later;
        }

        return later.Task;
    }

    /// <inheritdoc />
    public void CallOff(string version) => events.Enqueue($"call off {version}");

    /// <inheritdoc />
    public Task EndAllAsync(string version, bool now, CancellationToken cancellationToken)
    {
        events.Enqueue(now ? $"end relays now {version}" : $"end relays {version}");
        return Task.CompletedTask;
    }
}

/// <summary>Sessions the arm lists, recording a close.</summary>
/// <param name="events">The rig's record.</param>
internal sealed class ScriptedSessions(ConcurrentQueue<string> events) : IUpdateSessions
{
    private readonly Lock _gate = new();
    private readonly List<ListedSession> _listed = [];

    /// <summary>Lists a session.</summary>
    /// <param name="session">The session.</param>
    public void Open(ListedSession session)
    {
        lock (_gate)
        {
            _listed.Add(session);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ListedSession> Countdowns()
    {
        lock (_gate)
        {
            return [.. _listed];
        }
    }

    /// <inheritdoc />
    public Task CloseAllAsync(CancellationToken cancellationToken)
    {
        events.Enqueue("close sessions");

        lock (_gate)
        {
            _listed.Clear();
        }

        return Task.CompletedTask;
    }
}

/// <summary>Toasts that are recorded and never shown.</summary>
/// <param name="events">The rig's record.</param>
internal sealed class RecordedToasts(ConcurrentQueue<string> events) : IUpdateToasts
{
    /// <inheritdoc />
    public void Held(string version) => events.Enqueue($"toast ready {version}");

    /// <inheritdoc />
    public void Installing(string version) => events.Enqueue($"toast installing {version}");

    /// <inheritdoc />
    public void Installed(string version) => events.Enqueue($"toast installed {version}");

    /// <inheritdoc />
    public void Failed(string version) => events.Enqueue($"toast failed {version}");
}

/// <summary>The record of the last check, in memory.</summary>
internal sealed class MemoryCheckStamp : IUpdateCheckStamp
{
    /// <inheritdoc />
    public string Where => "memory";

    /// <summary>What a read returns.</summary>
    public UpdateCheckStampReading Reading { get; set; }

    /// <inheritdoc />
    public UpdateCheckStampReading Read() => Reading;

    /// <inheritdoc />
    public void Write(DateTimeOffset at) => Reading = new UpdateCheckStampReading(at, null);
}

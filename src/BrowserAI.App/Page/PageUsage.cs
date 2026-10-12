// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.App.Page;

/// <summary>What the page reads of the CPU and memory BrowserAI's own processes use.</summary>
/// <remarks>
/// <b>The maintainer's 59 b, 62 a and 65 a, 2026-10-11</b>: the background measures and the
/// page draws what it read. A reading asks Windows about every one of BrowserAI's processes,
/// none of which has an asynchronous form, so the reader runs it on a thread of its own and
/// the page awaits it: no pool worker waits in a reading (54).
/// </remarks>
internal interface IPageUsage
{
    /// <summary>Reads every one of BrowserAI's processes once.</summary>
    /// <returns>The reading.</returns>
    Task<UsageReading> ReadAsync();
}

/// <summary>What one process used up to one moment, as Windows reported it.</summary>
/// <param name="CpuTicks">Its CPU time since it started, kernel and user together, in 100-nanosecond ticks.</param>
/// <param name="OwnBytes">The memory only it holds: its private working set, in bytes.</param>
/// <param name="SharedBytes">The memory it shares with other processes, in bytes.</param>
internal readonly record struct ProcessCounters(long CpuTicks, long OwnBytes, long SharedBytes);

/// <summary>One relay, the process a conversation's app talks to BrowserAI through, as one reading found it.</summary>
/// <param name="ProcessId">The relay's pid.</param>
/// <param name="CreatedFileTime">Its creation time, the other half of its identity.</param>
/// <param name="Counters">What it used.</param>
internal sealed record RelayReading(int ProcessId, long CreatedFileTime, ProcessCounters Counters);

/// <summary>One open session's processes, its browser and the browser server that drives it, as one reading found them.</summary>
/// <param name="Id">The session's opaque name, <see cref="SessionEntry.Id"/>.</param>
/// <param name="CpuTicks">
/// The CPU time of every process its job has held, the ended ones included, kernel and user
/// together, in 100-nanosecond ticks: Windows keeps the total for the job.
/// </param>
/// <param name="BrowserBytes">The private working sets of its browser's processes, added up.</param>
/// <param name="ServerBytes">The private working sets of its other processes, the browser server and its console host, added up.</param>
/// <param name="SharedBytes">The largest shared memory any one of its processes holds.</param>
internal sealed record SessionReading(string Id, long CpuTicks, long BrowserBytes, long ServerBytes, long SharedBytes);

/// <summary>One reading of every one of BrowserAI's processes.</summary>
/// <param name="Background">The background itself, which serves the page.</param>
/// <param name="Relays">Each relay, one per conversation.</param>
/// <param name="Sessions">Each open session's processes.</param>
/// <param name="Processors">How many logical processors this PC has, which a share of its processor is a share of.</param>
internal sealed record UsageReading(
    ProcessCounters Background,
    IReadOnlyList<RelayReading> Relays,
    IReadOnlyList<SessionReading> Sessions,
    int Processors)
{
    /// <summary>
    /// Whether Windows would not report the background's own figures, which every reading
    /// reads first: on a Windows without <c>PROCESS_MEMORY_COUNTERS_EX2</c> no process can be
    /// read, and the figures are not measured, never zero.
    /// </summary>
    public bool Unreadable { get; init; }
}

/// <summary>Own memory, the memory only BrowserAI's processes hold, in its four parts.</summary>
/// <param name="Browsers">Every open session's browser.</param>
/// <param name="BrowserServers">The browser server that drives each, and its console host.</param>
/// <param name="Background">The background.</param>
/// <param name="Connections">The relays, one per conversation (65 a).</param>
internal sealed record UsageMemory(long Browsers, long BrowserServers, long Background, long Connections)
{
    /// <summary>Nothing measured.</summary>
    public static UsageMemory None { get; } = new(0, 0, 0, 0);

    /// <summary>The four parts together.</summary>
    public long Total => Browsers + BrowserServers + Background + Connections;
}

/// <summary>What one open session uses, as the page draws it.</summary>
/// <param name="Cpu">
/// The share of this PC's processor its processes used between the last two readings, from
/// 0 to 1, all cores together; <see langword="null"/> until a second reading has seen it.
/// </param>
/// <param name="BrowserBytes">Its browser's own memory.</param>
/// <param name="ServerBytes">Its browser server's own memory.</param>
internal sealed record SessionUsage(double? Cpu, long BrowserBytes, long ServerBytes)
{
    /// <summary>Its own memory, browser and server together: exact, since no other session's process is in its job (62 a).</summary>
    public long OwnBytes => BrowserBytes + ServerBytes;
}

/// <summary>What one relay uses, as the page draws it.</summary>
/// <param name="Cpu">Its share of this PC's processor between the last two readings, or <see langword="null"/>.</param>
/// <param name="OwnBytes">Its own memory.</param>
internal sealed record RelayUsage(double? Cpu, long OwnBytes);

/// <summary>One reading on the charts: when, the CPU then, and own memory then.</summary>
/// <param name="At">When the reading was taken.</param>
/// <param name="Cpu">The share of this PC's processor, or <see langword="null"/> for the first reading.</param>
/// <param name="OwnBytes">Own memory, all four parts together.</param>
internal readonly record struct UsagePoint(DateTimeOffset At, double? Cpu, long OwnBytes);

/// <summary>
/// What the page draws of the CPU and memory BrowserAI's own processes use: the newest
/// reading, what each process used since the reading before it, and the readings of the
/// last window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Own memory is exact and shared memory is said once (62 a).</b> A process's own memory
/// is its private working set, which no other process holds, so adding them up counts
/// nothing twice: per session, since every process of a session is in that session's job,
/// and for all of BrowserAI. Windows says how much of each process's memory is shared and
/// not with whom, so the shared figure is the largest any one process holds, which is an
/// estimate of the whole and is said with <i>about</i>.
/// </para>
/// <para>
/// <b>CPU is what was used between two readings</b>, as a share of the whole PC's processor:
/// the CPU time each process or job added, divided by the time between the readings and by
/// the number of logical processors. A process or a job the reading before did not see
/// counts from its start, and one whose time went down is a new one with an old number. A
/// first reading, or one longer than the window after the one before, has no CPU.
/// </para>
/// </remarks>
internal sealed record PageUsage
{
    /// <summary>When the newest reading was taken.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// The share of this PC's processor all of BrowserAI's processes used between the last two
    /// readings, from 0 to 1, all cores together; <see langword="null"/> before a second reading.
    /// </summary>
    public double? Cpu { get; init; }

    /// <summary>Own memory, in its four parts.</summary>
    public UsageMemory Own { get; init; } = UsageMemory.None;

    /// <summary>The largest shared memory any one of BrowserAI's processes holds: the figure said with <i>about</i> (62 a).</summary>
    public long Shared { get; init; }

    /// <summary>Each open session's figures, by its opaque name.</summary>
    public IReadOnlyDictionary<string, SessionUsage> Sessions { get; init; } = new Dictionary<string, SessionUsage>(StringComparer.Ordinal);

    /// <summary>Each relay's figures, by its pid.</summary>
    public IReadOnlyDictionary<int, RelayUsage> Relays { get; init; } = new Dictionary<int, RelayUsage>();

    /// <summary>The readings of the last window, oldest first, the newest among them.</summary>
    public IReadOnlyList<UsagePoint> History { get; init; } = [];

    /// <summary>The reading these figures were drawn from, which the next reading's CPU is counted against.</summary>
    public UsageReading? Reading { get; init; }

    /// <summary>Whether Windows would not report the figures, so that none is measured (<see cref="UsageReading.Unreadable"/>).</summary>
    public bool Unreadable { get; init; }

    /// <summary>One session's figures, or <see langword="null"/> where no reading has seen it.</summary>
    /// <param name="id">The session's opaque name.</param>
    /// <returns>Its figures.</returns>
    public SessionUsage? SessionOf(string id) => Sessions.TryGetValue(id, out var usage) ? usage : null;

    /// <summary>The figures after one more reading.</summary>
    /// <param name="before">The figures before it, or <see langword="null"/> for the first.</param>
    /// <param name="reading">The new reading.</param>
    /// <param name="at">When it was taken.</param>
    /// <param name="window">How far back the charts reach; a reading further than this after the one before has no CPU.</param>
    /// <returns>The figures.</returns>
    public static PageUsage Next(PageUsage? before, UsageReading reading, DateTimeOffset at, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var previous = before?.Reading;
        var elapsed = before is null ? TimeSpan.Zero : at - before.At;
        var compared = previous is not null && elapsed > TimeSpan.Zero && elapsed <= window;
        var processors = Math.Max(1, reading.Processors);

        double? shareOf(long ticks) => compared ? Math.Clamp(ticks / (double)elapsed.Ticks / processors, 0, 1) : null;

        var relaysBefore = previous?.Relays.ToDictionary(static relay => (relay.ProcessId, relay.CreatedFileTime), static relay => relay.Counters.CpuTicks) ?? [];
        var sessionsBefore = previous?.Sessions.ToDictionary(static session => session.Id, static session => session.CpuTicks, StringComparer.Ordinal) ?? [];

        var background = Added(reading.Background.CpuTicks, previous?.Background.CpuTicks);
        var relays = new Dictionary<int, RelayUsage>();
        var sessions = new Dictionary<string, SessionUsage>(StringComparer.Ordinal);
        var used = background;

        foreach (var relay in reading.Relays)
        {
            var added = Added(relay.Counters.CpuTicks, relaysBefore.TryGetValue((relay.ProcessId, relay.CreatedFileTime), out var ticks) ? ticks : null);

            used += added;
            relays[relay.ProcessId] = new RelayUsage(shareOf(added), relay.Counters.OwnBytes);
        }

        foreach (var session in reading.Sessions)
        {
            var added = Added(session.CpuTicks, sessionsBefore.TryGetValue(session.Id, out var ticks) ? ticks : null);

            used += added;
            sessions[session.Id] = new SessionUsage(shareOf(added), session.BrowserBytes, session.ServerBytes);
        }

        var own = new UsageMemory(
            reading.Sessions.Sum(static session => session.BrowserBytes),
            reading.Sessions.Sum(static session => session.ServerBytes),
            reading.Background.OwnBytes,
            reading.Relays.Sum(static relay => relay.Counters.OwnBytes));

        var shared = Math.Max(
            reading.Background.SharedBytes,
            Math.Max(
                reading.Relays.Select(static relay => relay.Counters.SharedBytes).DefaultIfEmpty().Max(),
                reading.Sessions.Select(static session => session.SharedBytes).DefaultIfEmpty().Max()));

        var cpu = shareOf(used);
        var since = at - window;

        return new PageUsage
        {
            At = at,
            Cpu = cpu,
            Own = own,
            Shared = shared,
            Sessions = sessions,
            Relays = relays,
            History = reading.Unreadable
                ? []
                : [.. (before?.History ?? []).Where(point => point.At > since && point.At < at), new UsagePoint(at, cpu, own.Total)],
            Reading = reading,
            Unreadable = reading.Unreadable,
        };
    }

    /// <summary>The CPU time a process or a job added since the reading before, counting a new one from its start.</summary>
    private static long Added(long now, long? before) => before is { } then && now >= then ? now - then : now;
}

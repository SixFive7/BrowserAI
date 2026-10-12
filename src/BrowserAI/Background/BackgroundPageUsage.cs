// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using BrowserAI.App.Page;
using BrowserAI.Interop;
using BrowserAI.Runtime;

namespace BrowserAI.Background;

/// <summary>
/// The page's CPU and memory, read by the background from Windows: itself, each relay and
/// each open session's job.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 59 b, 62 a and 65 a, 2026-10-11</b>, and his words verbatim: <i>"Show
/// this all with realtime updates. Show the separate process memory and the shared base
/// separately. The page has to show it all clearly."</i> What a session uses is what its
/// job holds: the node child, its console host and every browser process it started, and
/// nothing else, since a job is one session's. A relay is read through its process, which the
/// roster holds from its connection. The background reads itself.
/// </para>
/// <para>
/// <b>A browser is told from its server by its full image path</b>, under the browsers
/// root in any spelling Windows answers with, the way <see cref="BrowserProcesses.RunningFrom"/>
/// matches one, and never by an image name. A member's image is read once, the first
/// reading that meets the process, and kept by its pid and creation time.
/// </para>
/// <para>
/// <b>Every reading runs on a thread of its own</b>, <c>BrowserAI usage</c>: none of the
/// calls has an asynchronous form, and the page awaits the reading, so no pool worker waits
/// in one (54). What a reading costs is in
/// <see href="../../../kb/windows/processes.md">the kb</see>.
/// </para>
/// </remarks>
internal sealed class BackgroundPageUsage : IPageUsage, IDisposable
{
    private readonly DedicatedThread _thread = new("BrowserAI usage");
    private readonly Func<IReadOnlyList<(string Directory, JobObject? Job)>> _jobs;
    private readonly Func<IReadOnlyList<HeldProcess>> _relays;
    private readonly string _browsersRoot;

    /// <summary>Whether each member met so far runs a browser, by its pid and creation time. Read and written on the reading's thread alone.</summary>
    private readonly Dictionary<(int ProcessId, long CreatedFileTime), bool> _browsers = [];

    /// <summary>Every spelling of the browsers root, each ending in a separator, found by the first reading. On the reading's thread alone.</summary>
    private string[]? _browserPrefixes;

    /// <summary>A reader over the background's sessions and relays.</summary>
    /// <param name="jobs">Every held session's directory and its child's job.</param>
    /// <param name="relays">Every connected relay's process.</param>
    /// <param name="browsersRoot">The browsers root, under which every browser BrowserAI runs is.</param>
    public BackgroundPageUsage(
        Func<IReadOnlyList<(string Directory, JobObject? Job)>> jobs,
        Func<IReadOnlyList<HeldProcess>> relays,
        string browsersRoot)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersRoot);

        _jobs = jobs;
        _relays = relays;
        _browsersRoot = browsersRoot;
    }

    /// <inheritdoc />
    public Task<UsageReading> ReadAsync() => _thread.RunAsync(Read);

    /// <summary>Lets the reading's thread end once a reading in hand has run.</summary>
    public void Dispose() => _thread.Dispose();

    /// <summary>One reading, on the reading's thread.</summary>
    private UsageReading Read()
    {
        // A Windows that will not report this process will not report any: the figures are
        // then not measured, and never zero.
        if (ProcessUsage.OfThisProcess() is not { } self)
        {
            return new UsageReading(default, [], [], Environment.ProcessorCount) { Unreadable = true };
        }

        var background = Counters(self);
        var relays = new List<RelayReading>();

        foreach (var relay in _relays())
        {
            try
            {
                if (ProcessUsage.Read(relay.SafeWaitHandle) is { } use)
                {
                    relays.Add(new RelayReading(relay.ProcessId, relay.CreatedFileTime, Counters(use)));
                }
            }
            catch (ObjectDisposedException)
            {
                // The relay went while it was read; the next reading does not meet it.
            }
        }

        var sessions = new List<SessionReading>();
        var met = new HashSet<(int ProcessId, long CreatedFileTime)>();

        foreach (var (directory, job) in _jobs())
        {
            if (job is null)
            {
                continue;
            }

            try
            {
                sessions.Add(ReadSession(directory, job, met));
            }
            catch (Exception failure) when (failure is Win32Exception or ObjectDisposedException)
            {
                // The session closed while it was read, and its job with it.
            }
        }

        // A process this reading did not meet has ended; its pid may come back as another.
        foreach (var gone in _browsers.Keys.Where(key => !met.Contains(key)).ToList())
        {
            _ = _browsers.Remove(gone);
        }

        return new UsageReading(background, relays, sessions, Environment.ProcessorCount);
    }

    /// <summary>One session's job: its CPU total, and each member's memory, browser or server by its image.</summary>
    private SessionReading ReadSession(string directory, JobObject job, HashSet<(int ProcessId, long CreatedFileTime)> met)
    {
        var cpu = job.CpuTicks;
        long browser = 0;
        long server = 0;
        long shared = 0;

        foreach (var processId in job.ProcessIds())
        {
            if (ProcessUsage.ReadMember(job, processId, (pid, created) => !_browsers.ContainsKey((pid, created))) is not { } member)
            {
                continue;
            }

            var key = (member.ProcessId, member.CreatedFileTime);

            if (member.ImagePath is { } image)
            {
                _browsers[key] = IsBrowser(image);
            }

            _ = met.Add(key);

            if (_browsers.TryGetValue(key, out var isBrowser) && isBrowser)
            {
                browser += member.Use.PrivateWorkingSet;
            }
            else
            {
                server += member.Use.PrivateWorkingSet;
            }

            shared = Math.Max(shared, member.Use.SharedWorkingSet);
        }

        return new SessionReading(CensusPageSessions.IdOf(directory), cpu, browser, server, shared);
    }

    /// <summary>Whether an image is under the browsers root, in any spelling Windows answers with.</summary>
    private bool IsBrowser(string image)
    {
        _browserPrefixes ??= [.. ImageSpellings.OfDirectory(_browsersRoot).Matched
            .Select(static spelling => spelling.EndsWith(Path.DirectorySeparatorChar) ? spelling : spelling + Path.DirectorySeparatorChar)];

        return Array.Exists(_browserPrefixes, prefix => image.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A process's counters as the page reads them: its own memory is its private working set, and its shared memory the rest of its working set.</summary>
    private static ProcessCounters Counters(ProcessUse use) => new(use.CpuTicks, use.PrivateWorkingSet, use.SharedWorkingSet);
}

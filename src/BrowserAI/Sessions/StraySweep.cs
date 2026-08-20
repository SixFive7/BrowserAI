// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// One pass over the machine looking for browsers BrowserAI started and no
/// session accounts for — and, for anything it is sure about, ending them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Designed for ~100 concurrent BrowserAI processes, not for one.</b> Eight
/// editor windows with a dozen agent sessions each is a normal working day and
/// every session spawns its own server, so a sweep that is merely <i>correct</i>
/// for a single process is wrong here: ninety-six of them sweeping at startup is
/// a thundering herd, and ninety-six racing to kill the same stray is a
/// correctness problem. One machine-wide mutex at <b>zero timeout</b> answers
/// both — one process sweeps and the rest pay a mutex acquire and leave. A
/// skipped sweep is not a missed sweep: whoever holds the mutex is looking at
/// the same machine.
/// </para>
/// <para>
/// <b>Detection decides and is fully documented; attribution may fail and must
/// fail safe.</b> <see cref="BrowserProcesses.ScanFor"/> is the first guard and
/// it is a full-image-path match against binaries BrowserAI provisioned.
/// <see cref="MessageWindows"/> is the second and it rests on undocumented
/// behaviour — so it is deliberately <b>not</b> load-bearing: when it comes back
/// empty this refuses to kill and reports. The undocumented path can only ever
/// cause BrowserAI to decline to act and say so; it can never cause a wrong kill
/// and it can never cause silence.
/// </para>
/// <para>
/// <b>Both guards must agree, and the second one is the entire safety
/// boundary.</b> Enumeration hands back strangers' paths — Docker Desktop,
/// Discord, Signal, 1Password, Steam, Teams, WhatsApp and ChatGPT all publish
/// real <c>userDataDir</c>s on that channel — and the class is forgeable by any
/// process that cares to register it. So a candidate becomes a stray only when
/// its attributed directory holds a <c>lock.json</c> this sweeper can take
/// itself, which is a directory BrowserAI created and nothing else can be.
/// </para>
/// <para>
/// <b>Nothing here may reach <c>stdout</c>, be awaited, or gate anything.</b> It
/// runs on a background thread with a catch-all at the boundary; a sweep failure
/// is a log line, never a crash and never a protocol error. A BrowserAI that
/// cannot sweep is degraded; a BrowserAI that will not start is broken.
/// </para>
/// </remarks>
internal sealed class StraySweep
{
    private readonly IReadOnlyList<string> _images;
    private readonly HashSet<string> _profileLockImages;
    private readonly SessionIndex? _index;
    private readonly IAppPaths? _paths;
    private readonly ILogger _logger;

    /// <summary>Creates a sweep over one set of browser executables.</summary>
    /// <param name="browserImages">
    /// The absolute executable paths that count as ours, from
    /// <see cref="Runtime.ProvisionedBrowsers.Executables"/>.
    /// </param>
    /// <param name="index">The session index to self-clean, or <see langword="null"/> to skip that half.</param>
    /// <param name="logger">Where the pass is recorded. Never <c>stdout</c>.</param>
    /// <param name="profileLockImages">
    /// The subset of <paramref name="browserImages"/> whose profile is
    /// identified through <c>parent.lock</c> rather than through a message
    /// window — Firefox, from
    /// <see cref="Runtime.ProvisionedBrowsers.ExecutablesFor"/>. Empty means the
    /// second path is not attempted, which costs attribution and never safety.
    /// </param>
    /// <param name="paths">
    /// The app-paths seam, or <see langword="null"/> to skip the live-marker
    /// reclaim. Its only use here is
    /// <see cref="Updates.LiveInstances.ReclaimStaleMarkers"/>, which is added to
    /// this pass rather than given a sweeper of its own because this one is
    /// already machine-wide, already mutex-serialised and already skips instantly
    /// when a peer holds the gate — the three properties a marker reclaim needs
    /// and the reason not to invent a second discipline for it.
    /// </param>
    public StraySweep(
        IReadOnlyList<string> browserImages,
        SessionIndex? index,
        ILogger logger,
        IReadOnlyCollection<string>? profileLockImages = null,
        IAppPaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(browserImages);
        ArgumentNullException.ThrowIfNull(logger);

        _images = browserImages;
        _profileLockImages = new HashSet<string>(profileLockImages ?? [], StringComparer.OrdinalIgnoreCase);
        _index = index;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Runs one pass on a background thread and returns immediately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The catch-all is at the thread boundary and it is the point of this
    /// method</b> (race <b>R11</b>). An exception escaping a background thread
    /// tears the process down, and this process is an MCP server whose caller
    /// would see a transport that simply stopped. The sweep is not important
    /// enough to end anything.
    /// </para>
    /// <para>
    /// <b>The factory runs inside the catch too.</b> Constructing the sweep
    /// reads the payload manifest and composes paths, either of which can throw
    /// on a broken install — and a failure there is exactly as fatal as a
    /// failure inside the pass, which is to say not at all.
    /// </para>
    /// <para>
    /// A background thread rather than a pool work item: it must not keep the
    /// process alive on the way out, and it must not be something anything else
    /// could accidentally await.
    /// </para>
    /// </remarks>
    /// <param name="create">Produces the sweep, on the sweep's own thread.</param>
    /// <param name="logger">Where a failure is reported.</param>
    public static void StartInBackground(Func<StraySweep> create, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(logger);

        var thread = new Thread(() =>
        {
            try
            {
                // Run() writes the census itself, so nothing is logged here: a
                // second line would be the same fact twice, and the one that
                // survives a rewrite would be the one further from the evidence.
                _ = create().Run();
            }
#pragma warning disable CA1031 // The whole purpose of this method: nothing the sweep can do may end the process.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                try
                {
                    SweepLog.Failed(logger, failure);
                }
#pragma warning disable CA1031 // A logger that throws must not defeat the catch-all that was reporting through it.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        })
        {
            IsBackground = true,
            Name = "BrowserAI stray sweep",
        };

        thread.Start();
    }

    /// <summary>Runs one pass, synchronously.</summary>
    /// <returns>What the pass found and what it did.</returns>
    public StraySweepResult Run()
    {
        var clock = Stopwatch.StartNew();

        // Declared before the try and disposed unconditionally in the finally:
        // the pattern CA2000 asks for, and the one the rest of this namespace
        // already uses around a named object created inside a guarded region.
        MachineMutex? mutex = null;

        try
        {
            try
            {
                mutex = MachineMutex.Create(LockScopes.Sweep);
            }
            catch (Exception failure) when (failure
                is UnauthorizedAccessException
                or WaitHandleCannotBeOpenedException
                or IOException
                or NotSupportedException)
            {
                // Degraded, never fatal. A session refuses outright when it
                // cannot have its own lock, because there the alternative is two
                // browsers in one profile; here the alternative is a stray
                // nobody noticed, and refusing to start over that would be the
                // worse trade.
                SweepLog.NoSweepLock(_logger, LockScopes.Sweep, failure);
                return new StraySweepResult { Outcome = StraySweepOutcome.NoLock, Elapsed = clock.Elapsed };
            }

            var acquisition = mutex.Acquire(LockScopes.NeverWaits);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                // R9 as well as the herd: a pass that overruns the ten-minute
                // re-check simply means the re-check does nothing. No pile-up
                // is possible because nothing ever queues here.
                SweepLog.AlreadyRunning(_logger, LockScopes.Sweep);
                return new StraySweepResult { Outcome = StraySweepOutcome.Skipped, Elapsed = clock.Elapsed };
            }

            if (acquisition is MutexAcquisition.AcquiredAbandoned)
            {
                // R3. The wait SUCCEEDED. Unhandled, an AbandonedMutexException
                // disables sweeping permanently after the first crash and
                // nothing reports it -- so this is a log line and a flag on the
                // result, never a reason to stop.
                SweepLog.SweepLockWasAbandoned(_logger, LockScopes.Sweep);
            }

            try
            {
                var result = Pass(clock) with { GateWasAbandoned = acquisition is MutexAcquisition.AcquiredAbandoned };
                SweepLog.Finished(_logger, result.Summary);
                return result;
            }
            finally
            {
                mutex.Release();
            }
        }
        finally
        {
            mutex?.Dispose();
        }
    }

    /// <summary>
    /// Whether a window title may be handed to the filesystem at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the sweep's single largest availability risk, and it is
    /// closed by a string check.</b> The title is an untrusted string published
    /// by any process that registered the class, and the next thing that happens
    /// to it is a filesystem call. Measured: <c>File.Exists</c> on a local path
    /// costs 0.56 ms and on an unmapped drive letter 0.01 ms — but
    /// <c>\\10.255.255.1\share</c> costs <b>21,037 ms</b> and a dead hostname
    /// <b>22,225 ms</b>. One UNC title stalls the whole pass for twenty-one
    /// seconds.
    /// </para>
    /// <para>
    /// So the check is on the characters and nothing else: a drive letter, a
    /// colon and a separator. It runs before <c>Path.GetFullPath</c> as well as
    /// before <c>File.Exists</c>, because a rejected title must cost microseconds
    /// rather than being rejected somewhere further in.
    /// </para>
    /// </remarks>
    /// <param name="title">The title, exactly as the window published it.</param>
    /// <returns>Whether it is a rooted local drive-letter path.</returns>
    public static bool IsRootedLocalDriveLetterPath(string title) =>
        title is { Length: >= 3 }
        && char.IsAsciiLetter(title[0])
        && title[1] is ':'
        && title[2] is '\\' or '/'
        && !title.Contains('\0', StringComparison.Ordinal);

    private StraySweepResult Pass(Stopwatch clock)
    {
        using var scan = BrowserProcesses.ScanFor(_images);
        var walk = MessageWindows.Walk(MessageWindows.ChromiumSingletonClass);

        var attributed = new Dictionary<int, string>();
        var rejected = new List<string>();
        var titled = 0;

        foreach (var window in walk.Windows)
        {
            if (window.ProcessId is 0 || MessageWindows.TitleOf(window.Handle) is not { } title)
            {
                continue;
            }

            titled++;

            // Every title, not only a candidate's: rejecting a hostile one is a
            // property of the walk rather than of who owns it, and a stranger's
            // UNC title would stall this loop just as effectively.
            if (!IsRootedLocalDriveLetterPath(title))
            {
                rejected.Add(title);
                continue;
            }

            attributed[window.ProcessId] = title;
        }

        var terminated = new List<StrayTermination>();
        var spared = new List<StraySpared>();
        var unattributable = new List<StrayCandidate>();

        foreach (var candidate in scan.Candidates)
        {
            if (!attributed.TryGetValue(candidate.ProcessId, out var title))
            {
                unattributable.Add(candidate);
                continue;
            }

            Judge(candidate, title, terminated, spared);
        }

        // The second detection path. Firefox publishes no message window at all,
        // so every Firefox candidate arrives here unattributed and would
        // otherwise be reported and left forever.
        var byProfileLock = AttributeByProfileLock(unattributable, terminated, spared);

        if (unattributable.Count is not 0)
        {
            // Reported loudly and never acted on. A browser tree publishes its
            // profile from ONE process -- the one that owns the singleton
            // window -- so the helpers of a browser that is perfectly well
            // accounted for land here too, which the sentence says.
            SweepLog.CouldNotAttribute(
                _logger,
                SessionErrors.StrayCannotBeAttributed(
                    [.. unattributable.Select(candidate => (candidate.ProcessId, candidate.ImagePath))]));
        }

        return new StraySweepResult
        {
            Outcome = StraySweepOutcome.Ran,
            Elapsed = clock.Elapsed,
            ProcessesEnumerated = scan.Enumerated,
            ProcessesOpened = scan.Opened,
            Candidates = scan.Candidates.Count,
            WindowsWalked = walk.Windows.Count,
            TitledWindows = titled,
            WalkRestarts = walk.Restarts,
            WalkTruncated = walk.Truncated,
            RejectedTitles = rejected,
            Terminated = terminated,
            Spared = spared,
            AttributedByProfileLock = byProfileLock,
            Unattributable = [.. unattributable.Select(candidate => (candidate.ProcessId, candidate.ImagePath))],
            Index = _index?.Sweep(),

            // Last, and inside the sweep gate this pass already holds. It takes
            // the live set's OWN gate as well -- at zero timeout -- because the
            // two scopes protect different things: this one stops ninety-six
            // BrowserAIs sweeping the machine at once, that one stops a walk
            // racing a peer's join. Neither substitutes for the other.
            LiveMarkers = _paths is null ? null : LiveInstances.ReclaimStaleMarkers(_paths, _logger),
        };
    }

    /// <summary>
    /// Attributes the candidates no window claimed by asking each known session
    /// directory's Firefox profile lock who holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The direction is inverted, and it has to be.</b> Chromium's attribution
    /// runs process → profile, because the process publishes the path. Nothing a
    /// Firefox process exposes names its profile, so this runs profile → process:
    /// for each session BrowserAI knows about, ask the Restart Manager which
    /// processes hold <c>&lt;session&gt;\profile\parent.lock</c>, and keep the
    /// answers that are already candidates.
    /// </para>
    /// <para>
    /// ⚠️ <b>Intersected with the candidate set, never trusted on its own — and
    /// this is the entire safety boundary of the second path.</b> The Restart
    /// Manager answers about whatever file it is handed, so pointing it at a
    /// user's own Firefox profile would name the user's own browser. A holder
    /// becomes actionable only when it is already one of
    /// <see cref="BrowserProcesses.ScanFor"/>'s candidates — a full-image-path
    /// match against a binary BrowserAI provisioned — <b>and</b> its start time
    /// matches the one recorded when it was found.
    /// Both guards are the same ones the Chromium path uses; the third — the
    /// session's own <c>lock.json</c> being takeable — is applied by
    /// <see cref="ActOn"/> after this.
    /// </para>
    /// <para>
    /// <b>The index is the source of directories, and a null one costs
    /// attribution rather than safety.</b> Without it there is nothing to ask
    /// about, so every Firefox candidate stays unattributable and is reported —
    /// which is the direction this whole subsystem is allowed to be wrong in.
    /// </para>
    /// <para>
    /// <b>It costs nothing on a machine with no Firefox candidate</b>, because
    /// the candidate filter runs before the index walk. One Restart Manager
    /// session per known session directory is only ever paid when there is
    /// something to attribute.
    /// </para>
    /// </remarks>
    /// <param name="unattributable">
    /// The candidates no window claimed. Any that this attributes are
    /// <b>removed</b> from it.
    /// </param>
    /// <param name="terminated">Where a termination is recorded.</param>
    /// <param name="spared">Where a refusal to act is recorded.</param>
    /// <returns>How many candidates this path attributed.</returns>
    private int AttributeByProfileLock(
        List<StrayCandidate> unattributable,
        List<StrayTermination> terminated,
        List<StraySpared> spared)
    {
        if (_index is null || _profileLockImages.Count is 0)
        {
            return 0;
        }

        var pending = unattributable
            .Where(candidate => _profileLockImages.Contains(candidate.ImagePath))
            .ToList();

        if (pending.Count is 0)
        {
            return 0;
        }

        var attributed = 0;

        foreach (var entry in _index.Follow())
        {
            if (pending.Count is 0)
            {
                break;
            }

            if (entry.Session is not { } location || entry.Record is null)
            {
                continue;
            }

            var profile = Path.Combine(location.FullPath, SessionLayout.ProfileFolderName);

            // ⚠️ The cheap question first, and it is not an optimisation to be
            // tidied away. Measured 2026-08-16 on this machine: one Restart
            // Manager query costs **638 ms**, because it walks every handle on
            // the machine -- against 0.56 ms for a local File.Exists. The sweep
            // runs at every BrowserAI startup and ~100 of those are a normal
            // working day, so asking it per session directory unconditionally
            // would put minutes of machine-wide handle enumeration into a pass
            // that is otherwise ~27 ms.
            //
            // The guard is sound in the one direction that matters. Firefox
            // NEVER deletes parent.lock, so its absence proves no Firefox has
            // ever run in this profile and there is nothing to attribute; its
            // presence proves nothing at all, which is why the expensive
            // question still has to be asked when it is there.
            if (!File.Exists(FirefoxProfile.LockFileIn(profile)))
            {
                continue;
            }

            IReadOnlyList<FileHolder> holders;

            try
            {
                holders = FirefoxProfile.HoldersOf(profile);
            }
            catch (Exception failure) when (failure is Win32Exception or ArgumentException or IOException)
            {
                // Attribution failed, which is not the same as "nothing holds
                // it". Reported and skipped: the candidate stays unattributable
                // and nothing is terminated on the strength of a question that
                // did not get an answer.
                SweepLog.ProfileLockUnreadable(_logger, profile, failure);
                continue;
            }

            if (holders.Count is 0)
            {
                continue;
            }

            foreach (var candidate in pending.ToList())
            {
                // The pid-reuse guard, and it is the reason RM_UNIQUE_PROCESS
                // carries a start time at all: a pid that matches and a start
                // time that does not is a different process wearing a recycled
                // number.
                if (!holders.Any(holder =>
                        holder.ProcessId == candidate.ProcessId
                        && holder.StartedFileTime == candidate.CreatedFileTime))
                {
                    continue;
                }

                attributed++;
                _ = pending.Remove(candidate);
                _ = unattributable.Remove(candidate);

                SweepLog.AttributedByProfileLock(_logger, candidate.ProcessId, location.FullPath);
                ActOn(candidate, location, profile, terminated, spared);
            }
        }

        return attributed;
    }

    private void Judge(StrayCandidate candidate, string title, List<StrayTermination> terminated, List<StraySpared> spared)
    {
        if (SessionDirectoryFrom(title) is not { } directory)
        {
            spared.Add(new StraySpared(candidate.ProcessId, title, $"'{title}' holds no '{SessionLayout.LockFileName}', so it is not a BrowserAI session directory"));
            return;
        }

        SessionPath location;

        try
        {
            location = SessionPath.Resolve(directory);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            spared.Add(new StraySpared(candidate.ProcessId, title, failure.Message));
            return;
        }

        ActOn(candidate, location, title, terminated, spared);
    }

    /// <summary>
    /// The last guard and the kill, shared by both attribution paths.
    /// </summary>
    /// <remarks>
    /// <b>Shared deliberately rather than duplicated per browser family.</b> The
    /// two paths differ only in how they answer <i>which profile</i>; what makes
    /// a candidate safe to terminate is the same for both, and a second copy of
    /// this is a second place for R1's "hold the lock across the whole kill" to
    /// be got wrong.
    /// </remarks>
    /// <param name="candidate">The process, holding the handle that pins its pid.</param>
    /// <param name="location">The session directory it was attributed to.</param>
    /// <param name="published">What attribution had to work with, for the record.</param>
    /// <param name="terminated">Where a termination is recorded.</param>
    /// <param name="spared">Where a refusal to act is recorded.</param>
    private void ActOn(
        StrayCandidate candidate,
        SessionPath location,
        string published,
        List<StrayTermination> terminated,
        List<StraySpared> spared)
    {
        SessionDirectoryHold? hold = null;

        try
        {
            // R1. The directory lock is held for the WHOLE kill, and it is taken
            // without writing anything: a sweeper is not opening a session, and
            // rewriting lock.json would overwrite the crashed session's own
            // record with a janitor's.
            if (SessionLock.TryHoldUnowned(location, out hold) is { } refusal)
            {
                spared.Add(new StraySpared(candidate.ProcessId, published, refusal));
                return;
            }

            if (candidate.TryTerminate(out var why))
            {
                SweepLog.Terminated(_logger, candidate.ProcessId, candidate.ImagePath, location.FullPath);
                terminated.Add(new StrayTermination(candidate.ProcessId, candidate.ImagePath, location.FullPath));
            }
            else
            {
                spared.Add(new StraySpared(candidate.ProcessId, published, why ?? "it could not be terminated"));
            }
        }
        finally
        {
            hold?.Dispose();
        }
    }

    /// <summary>
    /// The session directory a published profile path belongs to, or
    /// <see langword="null"/> when it belongs to none.
    /// </summary>
    /// <remarks>
    /// <b>A browser publishes its <c>userDataDir</c>, and ours is a subfolder of
    /// the session.</b> BrowserAI passes <c>&lt;session&gt;\profile</c>, so the
    /// title names the profile and the <c>lock.json</c> that proves ownership is
    /// one level up. The climb happens only when the leaf is exactly the profile
    /// folder name and only to look for a lock file — it can never reach a
    /// personal Chrome profile, whose parent holds no <c>lock.json</c> either.
    /// </remarks>
    /// <param name="title">A title already known to be a rooted local drive-letter path.</param>
    /// <returns>The session directory, or <see langword="null"/>.</returns>
    private static string? SessionDirectoryFrom(string title)
    {
        string full;

        try
        {
            full = Path.GetFullPath(title).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (File.Exists(Path.Combine(full, SessionLayout.LockFileName)))
        {
            return full;
        }

        return string.Equals(Path.GetFileName(full), SessionLayout.ProfileFolderName, StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(full) is { Length: > 0 } parent
            && File.Exists(Path.Combine(parent, SessionLayout.LockFileName))
                ? parent
                : null;
    }
}

/// <summary>How a pass ended.</summary>
internal enum StraySweepOutcome
{
    /// <summary>It ran.</summary>
    Ran,

    /// <summary>Another process was already sweeping. Not a missed sweep.</summary>
    Skipped,

    /// <summary>The machine-wide lock could not be created, so nothing was looked at.</summary>
    NoLock,
}

/// <summary>A process the sweep ended.</summary>
/// <param name="ProcessId">Its pid.</param>
/// <param name="ImagePath">The binary it was running.</param>
/// <param name="Directory">The session directory it was attributed to.</param>
internal sealed record StrayTermination(int ProcessId, string ImagePath, string Directory);

/// <summary>A candidate the sweep deliberately left running.</summary>
/// <param name="ProcessId">Its pid.</param>
/// <param name="Title">The directory it published.</param>
/// <param name="Why">Why it was left alone. Every one of these is a refusal to act.</param>
internal sealed record StraySpared(int ProcessId, string Title, string Why);

/// <summary>What one pass found and what it did.</summary>
internal sealed record StraySweepResult
{
    /// <summary>How the pass ended.</summary>
    public required StraySweepOutcome Outcome { get; init; }

    /// <summary>How long it took, including the mutex acquire.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Whether the sweep mutex was found abandoned by a dead holder (R3).</summary>
    public bool GateWasAbandoned { get; init; }

    /// <summary>How many pids the machine reported.</summary>
    public int ProcessesEnumerated { get; init; }

    /// <summary>How many of them could be opened at all.</summary>
    public int ProcessesOpened { get; init; }

    /// <summary>How many were running one of our binaries.</summary>
    public int Candidates { get; init; }

    /// <summary>How many message-only windows of the class were walked.</summary>
    public int WindowsWalked { get; init; }

    /// <summary>
    /// How many of them carried a name at all.
    /// </summary>
    /// <remarks>
    /// Reported beside the walk count because the two answer different
    /// questions, and only the second says whether attribution had anything to
    /// work with. Every embedder owns several anonymous message windows plus at
    /// most one titled singleton, so a walk that found dozens of windows and
    /// <i>none</i> named is what a broken title read looks like — and it would
    /// otherwise be indistinguishable from a clean machine.
    /// </remarks>
    public int TitledWindows { get; init; }

    /// <summary>How many times a window destroyed mid-walk forced a restart.</summary>
    public int WalkRestarts { get; init; }

    /// <summary>Whether the walk gave up, meaning attribution is incomplete.</summary>
    public bool WalkTruncated { get; init; }

    /// <summary>Titles refused before any filesystem call touched them.</summary>
    public IReadOnlyList<string> RejectedTitles { get; init; } = [];

    /// <summary>Processes the sweep ended.</summary>
    public IReadOnlyList<StrayTermination> Terminated { get; init; } = [];

    /// <summary>Candidates left running, each with the reason.</summary>
    public IReadOnlyList<StraySpared> Spared { get; init; } = [];

    /// <summary>
    /// How many candidates were attributed through a Firefox profile lock rather
    /// than through a message window.
    /// </summary>
    /// <remarks>
    /// Reported separately because the two paths fail differently: a zero here
    /// on a machine with Firefox candidates means the Restart Manager route
    /// found nothing, which is a different problem from a title that could not
    /// be read.
    /// </remarks>
    public int AttributedByProfileLock { get; init; }

    /// <summary>Candidates no window attributed. Reported, never touched.</summary>
    public IReadOnlyList<(int ProcessId, string ImagePath)> Unattributable { get; init; } = [];

    /// <summary>What the index self-clean did, when one ran.</summary>
    public SessionIndexSweep? Index { get; init; }

    /// <summary>
    /// What the live-marker reclaim did, when one ran.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when the sweep was built without an
    /// <see cref="Hosting.IAppPaths"/>, which is a sweep that had no marker
    /// directory to be told about rather than one that declined to look.
    /// </remarks>
    public LiveMarkerReclaim? LiveMarkers { get; init; }

    /// <summary>One line for the log.</summary>
    public string Summary =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"outcome={Outcome} elapsed={Elapsed.TotalMilliseconds:F1}ms processes={ProcessesEnumerated}/{ProcessesOpened} candidates={Candidates} windows={WindowsWalked} titled={TitledWindows} restarts={WalkRestarts} truncated={WalkTruncated} terminated={Terminated.Count} spared={Spared.Count} byProfileLock={AttributedByProfileLock} unattributable={Unattributable.Count} rejectedTitles={RejectedTitles.Count} indexRemoved={Index?.Removed.Count ?? 0} liveMarkers=[{LiveMarkers?.Summary ?? "-"}] abandonedGate={GateWasAbandoned}");
}

/// <summary>Source-generated log messages for the stray sweep.</summary>
internal static partial class SweepLog
{
    /// <summary>One pass finished, with its whole census on one line.</summary>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Stray sweep: {Summary}")]
    public static partial void Finished(ILogger logger, string summary);

    /// <summary>Another process holds the sweep mutex, so this one does nothing.</summary>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "A stray sweep is already running under '{Mutex}', so this process skipped its own. A skipped sweep is not a missed one.")]
    public static partial void AlreadyRunning(ILogger logger, string mutex);

    /// <summary>
    /// The sweep mutex was abandoned by a holder that died inside a pass
    /// (race <b>R3</b>).
    /// </summary>
    /// <remarks>
    /// Warning rather than Debug: the acquisition itself was never in doubt, but
    /// a previous sweeper died part-way through a pass, and that pass may have
    /// terminated some of a tree and not the rest.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="mutex">The object that was abandoned.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "'{Mutex}' was abandoned by a previous sweeper that died holding it. It is acquired and this pass is proceeding; the previous pass did not finish.")]
    public static partial void SweepLockWasAbandoned(ILogger logger, string mutex);

    /// <summary>The machine-wide lock could not be created, so no pass ran.</summary>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "The stray sweep did not run: '{Mutex}' could not be created. BrowserAI is degraded rather than broken — sessions still work, but a browser nothing claims will not be found.")]
    public static partial void NoSweepLock(ILogger logger, string mutex, Exception failure);

    /// <summary>A stray was ended.</summary>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Terminated a stray browser: pid={ProcessId} image={ImagePath} session={Directory}. Its session directory was unlocked, so nothing owned it.")]
    public static partial void Terminated(ILogger logger, int processId, string imagePath, string directory);

    /// <summary>
    /// Candidates that could not be attributed to any directory.
    /// </summary>
    /// <remarks>
    /// The whole sentence is the catalogue's, so what a person reads in the log
    /// is the same text the catalogue is tested against.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="report">The catalogue row.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "{Report}")]
    public static partial void CouldNotAttribute(ILogger logger, string report);

    /// <summary>
    /// A candidate no window claimed was attributed through a session's Firefox
    /// profile lock.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="processId">The candidate.</param>
    /// <param name="directory">The session directory whose profile it holds.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "Attributed pid={ProcessId} to session={Directory} through its Firefox profile lock; no message window names a Firefox.")]
    public static partial void AttributedByProfileLock(ILogger logger, int processId, string directory);

    /// <summary>
    /// A session's profile lock could not be asked about, so nothing was
    /// attributed to it.
    /// </summary>
    /// <remarks>
    /// Warning rather than Debug, and the sentence says which way the failure
    /// resolves: a question that got no answer leaves a candidate reported and
    /// alive, never terminated.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="profile">The profile directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Could not ask which process holds the profile lock under {Profile}; nothing was attributed to that session and nothing was terminated.")]
    public static partial void ProfileLockUnreadable(ILogger logger, string profile, Exception failure);

    /// <summary>The pass threw, and the thread boundary caught it.</summary>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "The stray sweep failed. Nothing was terminated and nothing else is affected; the next BrowserAI startup will try again.")]
    public static partial void Failed(ILogger logger, Exception failure);
}

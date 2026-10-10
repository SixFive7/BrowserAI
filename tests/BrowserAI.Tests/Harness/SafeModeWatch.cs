// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using System.Text;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>Why a process in safe mode counts as the suite's, or why it does not.</summary>
internal enum SafeModeOwnership
{
    /// <summary>Somebody else's: no live parent chain reaches the host and no suite root holds its image.</summary>
    NotOurs,

    /// <summary>Created before this session began, so nothing in this run started it.</summary>
    BeforeTheRun,

    /// <summary>A process descending from the test host by a live parent chain.</summary>
    Descendant,

    /// <summary>An image under a root only the suite runs from.</summary>
    UnderSuiteRoot,
}

/// <summary>What <see cref="SafeModeWatch"/> concluded about a whole run.</summary>
internal enum SafeModeVerdict
{
    /// <summary>No scan ever completed, so the run cannot say what it started.</summary>
    Unwatched,

    /// <summary>Watched, and no Firefox of the suite's started in safe mode.</summary>
    Clean,

    /// <summary>Watched, and a Firefox of the suite's started in safe mode.</summary>
    SafeMode,
}

/// <summary>One process whose command line carries a safe-mode switch, read the moment a scan met it.</summary>
/// <param name="ProcessId">The pid.</param>
/// <param name="CreatedFileTime">Its creation time as a FILETIME.</param>
/// <param name="ImagePath">Its full image path.</param>
/// <param name="CommandLine">What it was started with.</param>
/// <param name="Switch">The safe-mode argument the command line carries, as written there.</param>
/// <param name="At">When the scan read it, UTC.</param>
/// <param name="Ancestry">The live parent chain above it, nearest first.</param>
internal sealed record SafeModeSighting(
    int ProcessId,
    long CreatedFileTime,
    string ImagePath,
    string CommandLine,
    string Switch,
    DateTime At,
    IReadOnlyList<ProcessMark> Ancestry);

/// <summary>What a sighting is judged against.</summary>
/// <param name="Host">The test host.</param>
/// <param name="SessionStartFileTime">When the session began, as a FILETIME: nothing created before it is this run's.</param>
/// <param name="SuiteRoots">The roots only the suite runs images from.</param>
internal sealed record SafeModeWatchContext(
    ProcessMark Host,
    long SessionStartFileTime,
    IReadOnlyList<string> SuiteRoots);

/// <summary>Everything the watch saw, frozen at one moment.</summary>
/// <param name="Polls">How many scans completed.</param>
/// <param name="Failure">Why the last scan that failed failed, when one did.</param>
/// <param name="Examined">How many processes under the scanned roots had their command line read.</param>
/// <param name="Sightings">Every process in safe mode the scans met, with whose it is.</param>
internal sealed record SafeModeWatchReading(
    int Polls,
    string? Failure,
    int Examined,
    IReadOnlyList<(SafeModeSighting Sighting, SafeModeOwnership Ownership)> Sightings);

/// <summary>
/// One scan's state: which processes it has already read, and what it found.
/// </summary>
/// <remarks>
/// <b>An instance and not only the session's static watch</b>, so that
/// <c>SafeModeWatchTests</c> can point one at a scratch root with a planted switch
/// and prove the plumbing -- the enumeration, the command-line read and the parent
/// chain -- against a real process, without the session's own watch ever seeing a
/// switch it would fail the run for.
/// </remarks>
/// <param name="context">The host, the session's start and the suite's roots.</param>
/// <param name="scanRoots">The roots whose processes are read.</param>
/// <param name="switches">The command-line arguments that mean safe mode, matched whole and ignoring case.</param>
internal sealed class SafeModeScan(SafeModeWatchContext context, IReadOnlyList<string> scanRoots, IReadOnlyList<string> switches)
{
    private readonly HashSet<ProcessMark> _read = [];

    /// <summary>How many processes have had their command line read.</summary>
    public int Examined => _read.Count;

    /// <summary>The context every sighting is judged against.</summary>
    public SafeModeWatchContext Context => context;

    /// <summary>Reads every process under the roots that this scan has not read before.</summary>
    /// <returns>The new sightings, each with whose it is.</returns>
    /// <exception cref="Win32Exception">The machine's process list could not be read.</exception>
    public List<(SafeModeSighting Sighting, SafeModeOwnership Ownership)> Poll()
    {
        var found = new List<(SafeModeSighting, SafeModeOwnership)>();

        foreach (var root in scanRoots)
        {
            foreach (var image in BrowserProcesses.RunningFrom(root))
            {
                var mark = new ProcessMark(image.ProcessId, image.CreatedFileTime);

                if (!_read.Add(mark))
                {
                    continue;
                }

                string? commandLine;

                try
                {
                    commandLine = ProcessCommandLine.Of(image.ProcessId);
                }
                catch (Win32Exception)
                {
                    // Gone between the enumeration and the read, or refused: a
                    // process this token cannot read is not one this run started.
                    continue;
                }

                if (commandLine is null || SafeModeWatch.MatchedSwitch(commandLine, switches) is not { } matched)
                {
                    continue;
                }

                var sighting = new SafeModeSighting(
                    image.ProcessId,
                    image.CreatedFileTime,
                    image.ImagePath,
                    commandLine,
                    matched,
                    DateTime.UtcNow,
                    WindowWatch.AncestryOf(image.ProcessId));

                found.Add((sighting, SafeModeWatch.Classify(sighting, context)));
            }
        }

        return found;
    }
}

/// <summary>
/// Watches for a Firefox of the suite's starting in safe mode, for the whole
/// session, and fails a run in which one did.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Q312 b, decided 2026-10-03 by the maintainer, in his words: <i>"Q312
/// b"</i>.</b> A Firefox that starts in safe mode opens a modal window before any
/// browser window, headless or not, never answers juggler's
/// <c>Browser.enable</c>, and the launch waits out Playwright's own 180 s timeout:
/// the stuck launch of 2026-09-22 and 2026-09-24, read until then as contention.
/// <c>ChildEnvironment.Forced</c> sets <c>MOZ_DISABLE_SAFE_MODE_KEY=1</c>, which
/// covers the Shift key and no other route, and that line cannot be planted red
/// without holding a key down -- the second named exception to the plant-it-red
/// rule. <b>This watch is the half that CAN be planted red</b>:
/// <c>MOZ_SAFE_MODE_RESTART=1</c> forces safe mode, 3 of 3 on 2026-09-25.
/// </para>
/// <para>
/// <b>What it reads is the switch Firefox puts on a content process.</b> A browser
/// in safe mode starts its content processes with <c>-safeMode</c>
/// (<c>dom/ipc/ContentParent.cpp</c>, <c>toolkit/xre/GeckoArgs.h</c>): 3 of 3
/// organic stalls and 5 of 5 forced ones carried it, and 0 of 287 healthy trees
/// did, measured 2026-09-25 at firefox 1549. A browser started with the user-facing
/// <c>-safe-mode</c> argument is matched too. The browser process itself carries
/// nothing when the key or the variable put it there, which is why the content
/// process is the witness.
/// </para>
/// <para>
/// <b>Scanned by full image path, never by image name</b>: every two seconds,
/// <see cref="BrowserProcesses.RunningFrom"/> lists what runs from the machine's
/// browsers root and from the two scratch roots, and each process is read once.
/// Two seconds against a symptom that lasts 180 s is a margin, not a race. <b>A
/// process is the suite's</b> by <see cref="WindowWatch"/>'s rules: created after
/// the session began, and either descending from the host by a live parent chain or
/// running an image under a scratch root. A Firefox in safe mode that is not the
/// suite's -- the maintainer's own BrowserAI, another worktree's run -- is printed in
/// the row and does not fail this run.
/// </para>
/// </remarks>
internal static class SafeModeWatch
{
    /// <summary>The label this row carries in the coverage block.</summary>
    public const string Title = "firefox safe mode";

    /// <summary>The state word for a watched run that started no Firefox in safe mode.</summary>
    public const string CleanState = "CLEAN  ";

    /// <summary>The state word for a run that did.</summary>
    public const string SafeModeState = "SAFE   ";

    /// <summary>The state word for a run no scan covered.</summary>
    public const string UnwatchedState = "UNWATCH";

    /// <summary>A sentence the refusal carries and nothing else in a run prints.</summary>
    public const string RefusalMarker = "No suite run may start a Firefox in safe mode.";

    /// <summary>How long a scan waits before the next one.</summary>
    /// <remarks>
    /// <b>Two seconds against a symptom that lasts 180.</b> A Firefox in safe mode
    /// keeps its content process until Playwright's launch timeout ends it, so the
    /// witness lives ninety times longer than the gap between two scans.
    /// </remarks>
    public static TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(2);

    /// <summary>The arguments that mean safe mode: the content process's and the user's.</summary>
    public static IReadOnlyList<string> Switches { get; } = ["-safeMode", "-safe-mode"];

    private static readonly Lock Gate = new();
    private static readonly List<(SafeModeSighting Sighting, SafeModeOwnership Ownership)> SightingList = [];
    private static readonly ManualResetEventSlim Stopping = new(initialState: false);

    private static SafeModeScan? _scan;
    private static Thread? _thread;
    private static int _polls;
    private static string? _failure;
    private static bool _stopped;

    /// <summary>This run's reading, as it stands now.</summary>
    public static SafeModeWatchReading Reading
    {
        get
        {
            lock (Gate)
            {
                return new SafeModeWatchReading(_polls, _failure, _scan?.Examined ?? 0, [.. SightingList]);
            }
        }
    }

    /// <summary>This run's row in the coverage block.</summary>
    public static string CoverageRow => RowFor(Reading);

    /// <summary>Starts the scan thread. Called once, from the session hook.</summary>
    public static void Start()
    {
        var thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "BrowserAI suite safe-mode watch",
        };

        lock (Gate)
        {
            if (_thread is not null || _failure is not null)
            {
                return;
            }

            try
            {
                var context = new SafeModeWatchContext(
                    new ProcessMark(Environment.ProcessId, ProcessIdentity.CreationTimeOf(Environment.ProcessId)),
                    DateTime.UtcNow.ToFileTimeUtc(),
                    SuiteRoots());

                _scan = new SafeModeScan(context, [BrowserAiPaths.BrowsersDirectory, .. context.SuiteRoots], Switches);
            }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or IOException)
            {
                _failure = $"the watch could not be set up: {failure.Message}";
                return;
            }

            _thread = thread;
        }

        thread.Start();
    }

    /// <summary>Stops the scan thread and scans once more. Called once, from the session hook.</summary>
    public static void Stop()
    {
        Thread? thread;

        lock (Gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            thread = _thread;
        }

        Stopping.Set();
        _ = thread?.Join(TestDefaults.ProcessHang);

        // The closing scan: a process that started after the last tick is read here.
        PollOnce();
    }

    /// <summary>Whether a command line carries one of the switches as a whole argument.</summary>
    /// <param name="commandLine">The command line.</param>
    /// <param name="switches">The arguments to look for, matched whole and ignoring case.</param>
    /// <returns><see langword="true"/> when one is there.</returns>
    public static bool CarriesASwitch(string commandLine, IReadOnlyList<string> switches) =>
        MatchedSwitch(commandLine, switches) is not null;

    /// <summary>The first of the switches a command line carries as a whole argument, as written there.</summary>
    /// <param name="commandLine">The command line.</param>
    /// <param name="switches">The arguments to look for, matched whole and ignoring case.</param>
    /// <returns>The argument, or <see langword="null"/> when none is there.</returns>
    public static string? MatchedSwitch(string commandLine, IReadOnlyList<string> switches)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(switches);

        foreach (var argument in commandLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = argument.Trim('"');

            foreach (var wanted in switches)
            {
                if (string.Equals(bare, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return bare;
                }
            }
        }

        return null;
    }

    /// <summary>Whose a process in safe mode is, as a pure function of what was read about it.</summary>
    /// <param name="sighting">The process.</param>
    /// <param name="context">The host, the session's start and the suite's roots.</param>
    /// <returns>Why it is the suite's, or that it is not.</returns>
    public static SafeModeOwnership Classify(SafeModeSighting sighting, SafeModeWatchContext context)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        ArgumentNullException.ThrowIfNull(context);

        if (sighting.CreatedFileTime < context.SessionStartFileTime)
        {
            return SafeModeOwnership.BeforeTheRun;
        }

        if (WindowWatch.DescendsFrom(new ProcessMark(sighting.ProcessId, sighting.CreatedFileTime), sighting.Ancestry, context.Host))
        {
            return SafeModeOwnership.Descendant;
        }

        return context.SuiteRoots.Any(root => WindowWatch.IsUnder(sighting.ImagePath, root))
            ? SafeModeOwnership.UnderSuiteRoot
            : SafeModeOwnership.NotOurs;
    }

    /// <summary>Whether an ownership is one this run has to answer for.</summary>
    /// <param name="ownership">The classification.</param>
    /// <returns><see langword="true"/> for the two ways a process is the suite's.</returns>
    public static bool IsOffence(SafeModeOwnership ownership) =>
        ownership is SafeModeOwnership.Descendant or SafeModeOwnership.UnderSuiteRoot;

    /// <summary>What a reading says about the run.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The verdict.</returns>
    public static SafeModeVerdict Judge(SafeModeWatchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading.Polls is 0
            ? SafeModeVerdict.Unwatched
            : reading.Sightings.Any(sighting => IsOffence(sighting.Ownership)) ? SafeModeVerdict.SafeMode : SafeModeVerdict.Clean;
    }

    /// <summary>The seven-character state the block prints.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The word.</returns>
    public static string StateWord(SafeModeVerdict verdict) => verdict switch
    {
        SafeModeVerdict.Clean => CleanState,
        SafeModeVerdict.SafeMode => SafeModeState,
        _ => UnwatchedState,
    };

    /// <summary>The row, and under it one line per process in safe mode.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The text.</returns>
    public static string RowFor(SafeModeWatchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);
        var row = new StringBuilder("  ").Append(Title.PadRight(20)).Append(StateWord(verdict)).Append("  ");

        _ = verdict switch
        {
            SafeModeVerdict.Clean => row.Append(CultureInfo.InvariantCulture, $"no Firefox this run started carried -safeMode or -safe-mode: {reading.Polls} scans, {reading.Examined} processes read under the browsers root and the scratch roots"),
            SafeModeVerdict.SafeMode => row.Append(CultureInfo.InvariantCulture, $"⚠️ {reading.Sightings.Count(sighting => IsOffence(sighting.Ownership))} process(es) of this run's own in safe mode, over {reading.Polls} scans:"),
            _ => row.Append(reading.Failure ?? "no scan completed").Append(", so this run cannot say whether a Firefox it started was in safe mode, and it fails for it"),
        };

        foreach (var (sighting, ownership) in reading.Sightings)
        {
            _ = row.Append('\n').Append("      ").Append(Describe(sighting, ownership));
        }

        if (verdict is SafeModeVerdict.SafeMode)
        {
            _ = row.Append('\n')
                .Append("      ⚠️  A FIREFOX THIS RUN STARTED WAS IN SAFE MODE, and the run fails for it (Q312 b).\n")
                .Append("      A held Shift key is switched off by MOZ_DISABLE_SAFE_MODE_KEY in ChildEnvironment.Forced;\n")
                .Append("      MOZ_SAFE_MODE_RESTART and a -safe-mode argument are not, so look for those first.");
        }

        return row.ToString();
    }

    /// <summary>The sentence the session hook fails a run with.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The refusal, or <see langword="null"/> when there is nothing to refuse.</returns>
    public static string? Refusal(SafeModeWatchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);

        if (verdict is SafeModeVerdict.Clean)
        {
            return null;
        }

        if (verdict is SafeModeVerdict.Unwatched)
        {
            return $"The safe-mode watch completed no scan ({reading.Failure ?? "never started"}), so this run cannot say whether a Firefox it started was in safe mode (Q312 b).";
        }

        var offences = string.Join(
            "; ",
            reading.Sightings
                .Where(sighting => IsOffence(sighting.Ownership))
                .Select(sighting => Describe(sighting.Sighting, sighting.Ownership)));

        return $"This run started a Firefox in safe mode: {offences}. "
            + RefusalMarker
            + " A Firefox in safe mode opens a modal window before any browser window and never answers Playwright, so its launch waits out the 180 s timeout. "
            + $"Q312 b, the maintainer's words verbatim: \"Q312 b\". The coverage block's {Title} row names each process.";
    }

    /// <summary>One process, the way the row prints it.</summary>
    /// <param name="sighting">The process.</param>
    /// <param name="ownership">Whose it is.</param>
    /// <returns>The line.</returns>
    public static string Describe(SafeModeSighting sighting, SafeModeOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(sighting);

        var why = ownership switch
        {
            SafeModeOwnership.Descendant => "a descendant of the test host",
            SafeModeOwnership.UnderSuiteRoot => "an image under a suite root",
            SafeModeOwnership.BeforeTheRun => "created before this run began, not this run's",
            _ => "not this run's",
        };

        var commandLine = sighting.CommandLine.Length > 160 ? sighting.CommandLine[..160] + " [cut]" : sighting.CommandLine;

        return $"pid {sighting.ProcessId.ToString(CultureInfo.InvariantCulture)}@{sighting.CreatedFileTime.ToString(CultureInfo.InvariantCulture)} "
            + $"{sighting.ImagePath} at {sighting.At.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}Z, {why}, carries {sighting.Switch}: {commandLine}";
    }

    /// <summary>The roots only the suite runs images from, as composed and never swept.</summary>
    /// <returns>The roots.</returns>
    private static List<string> SuiteRoots() =>
    [
        // As composed and never swept: a session hook must not run the reclaim.
        ScratchRoot.PathAsComposed,
        ScratchRoot.ProfileScratchAsComposed,
    ];

    /// <summary>The scan thread: one scan, then a wait, until the stop.</summary>
    private static void Loop()
    {
        do
        {
            PollOnce();
        }
        while (!Stopping.Wait(PollInterval));
    }

    /// <summary>One scan, recorded, never thrown.</summary>
    private static void PollOnce()
    {
        SafeModeScan? scan;

        lock (Gate)
        {
            scan = _scan;
        }

        if (scan is null)
        {
            return;
        }

        try
        {
            List<(SafeModeSighting, SafeModeOwnership)> found;

            // One scan at a time: the closing scan and the last tick must not read
            // the same process twice into the list.
            lock (scan)
            {
                found = scan.Poll();
            }

            lock (Gate)
            {
                _polls++;
                SightingList.AddRange(found);
            }
        }
#pragma warning disable CA1031 // The watch's own thread boundary: a scan that fails is recorded in the row and counted out of the polls, and never ends the test host.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            lock (Gate)
            {
                _failure = $"a scan failed: {failure.GetType().Name}: {failure.Message}";
            }
        }
    }
}

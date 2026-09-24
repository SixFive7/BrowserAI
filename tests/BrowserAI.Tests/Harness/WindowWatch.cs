// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>What a WinEvent reported about a window, or what the closing sweep found.</summary>
internal enum WindowEventKind
{
    /// <summary><c>EVENT_OBJECT_CREATE</c>, for a window already visible when it was created.</summary>
    Created,

    /// <summary><c>EVENT_OBJECT_SHOW</c>.</summary>
    Shown,

    /// <summary><c>EVENT_SYSTEM_FOREGROUND</c>.</summary>
    Foreground,

    /// <summary>Visible when the session ended, found by the closing sweep.</summary>
    StillOpen,
}

/// <summary>Why a window counts as the suite's, or why it does not.</summary>
internal enum WindowOwnership
{
    /// <summary>Somebody else's window.</summary>
    NotOurs,

    /// <summary>Visible before the session started, so nothing in this run showed it.</summary>
    Baseline,

    /// <summary>The test host's own.</summary>
    Host,

    /// <summary>A process descending from the test host by a live parent chain.</summary>
    Descendant,

    /// <summary>An image under a root only the suite runs from.</summary>
    UnderSuiteRoot,
}

/// <summary>What <see cref="WindowWatch"/> concluded about a whole run.</summary>
internal enum WindowWatchVerdict
{
    /// <summary>The watch never ran, so the run cannot say what it showed.</summary>
    Unwatched,

    /// <summary>Watched, and nothing of the suite's was shown or brought forward.</summary>
    Clean,

    /// <summary>Watched, and the suite put a window on the screen or took the foreground.</summary>
    Shown,
}

/// <summary>Whether a run's window reading lets it pass.</summary>
internal enum WindowWatchDecision
{
    /// <summary>Nothing to refuse.</summary>
    Proceed,

    /// <summary>The session hook fails the run after the block is written.</summary>
    Refuse,
}

/// <summary>A process, by the pair that makes it one.</summary>
/// <param name="ProcessId">The pid.</param>
/// <param name="CreatedFileTime">Its creation time as a FILETIME.</param>
internal readonly record struct ProcessMark(int ProcessId, long CreatedFileTime);

/// <summary>One visible root window, read the moment it was reported.</summary>
/// <param name="Kind">What was reported.</param>
/// <param name="Window">The handle.</param>
/// <param name="ProcessId">The owning pid.</param>
/// <param name="CreatedFileTime">Its creation time, or <see langword="null"/> when the process could not be opened.</param>
/// <param name="ThreadId">The owning thread, which is the identity left when the creation time is not.</param>
/// <param name="ClassName">The window class.</param>
/// <param name="Title">The kernel-side name, read without sending a message.</param>
/// <param name="Rectangle">Where it was, as <c>x,y wxh</c>.</param>
/// <param name="ImagePath">The owning image, or <see langword="null"/> when it could not be read.</param>
/// <param name="At">When it was read, UTC.</param>
/// <param name="Ancestry">The live parent chain above the owner, nearest first.</param>
internal sealed record WindowSighting(
    WindowEventKind Kind,
    long Window,
    int ProcessId,
    long? CreatedFileTime,
    int ThreadId,
    string ClassName,
    string Title,
    string Rectangle,
    string? ImagePath,
    DateTime At,
    IReadOnlyList<ProcessMark> Ancestry);

/// <summary>What a sighting is judged against.</summary>
/// <param name="Host">The test host.</param>
/// <param name="SuiteRoots">The roots only the suite runs images from.</param>
/// <param name="SharedBrowsersRoot">The machine's own browsers root, which is never the suite's by path.</param>
/// <param name="Baseline">The windows that were visible when the session started, with their owners.</param>
internal sealed record WindowWatchContext(
    ProcessMark Host,
    IReadOnlyList<string> SuiteRoots,
    string SharedBrowsersRoot,
    IReadOnlySet<(long Window, int ProcessId)> Baseline);

/// <summary>Everything the watch saw, frozen at one moment.</summary>
/// <param name="Watched">Whether both hooks were installed.</param>
/// <param name="Failure">Why not, when they were not.</param>
/// <param name="Desktop">The desktop the hook thread was on.</param>
/// <param name="Events">Every event delivered, before any filter.</param>
/// <param name="VisibleRoots">Every visible root window the filter kept.</param>
/// <param name="Baseline">How many windows were visible when the session started.</param>
/// <param name="Offences">The suite's own, each with why it is the suite's.</param>
internal sealed record WindowWatchReading(
    bool Watched,
    string? Failure,
    string Desktop,
    int Events,
    int VisibleRoots,
    int Baseline,
    IReadOnlyList<(WindowSighting Sighting, WindowOwnership Ownership)> Offences);

/// <summary>
/// Watches the test host's desktop for the whole session and fails a run whose own
/// processes put a window on the screen or took the foreground.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's directive, 2026-09-24, verbatim: <i>"make sure this focus
/// stealing is not something that ends up in the testbed."</i></b> It followed a
/// focus grab on his screen during a measurement, and the audit that came after
/// found the suite had been doing it itself since 2026-09-15:
/// <c>RealInstallerTests.TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow</c>
/// put the configuration app's <c>#32770</c> task dialog on the interactive desktop
/// in every full run that had the release installer, and asked for the foreground.
/// Every green gate in those nine days passed through it and said nothing, because
/// nothing in a run looked. <c>kb/windows/detection.md</c>'s 2026-08-17 reading of
/// <i>zero visible windows</i> was true of the suite it measured and false of the
/// one that followed.
/// </para>
/// <para>
/// <b>In the host, raised from the session hooks, and so it cannot be filtered
/// away.</b> <c>[Before(TestSession)]</c> takes a baseline of the visible top-level
/// windows and starts one background thread, which installs two out-of-context
/// WinEvent hooks -- <c>EVENT_OBJECT_CREATE</c> through <c>EVENT_OBJECT_SHOW</c>,
/// and <c>EVENT_SYSTEM_FOREGROUND</c> -- and pumps <c>GetMessage</c>.
/// <c>WINEVENT_SKIPOWNPROCESS</c> is deliberately absent: a window the host shows
/// itself is the suite's too. <c>[After(TestSession)]</c> stops the thread, sweeps
/// for windows still open, adds the <see cref="Title"/> row to the coverage block
/// and then throws, so the host exits 10.
/// </para>
/// <para>
/// <b>What it keeps.</b> Visible root windows only (<c>OBJID_WINDOW</c>,
/// <c>CHILDID_SELF</c>, <c>GetAncestor(GA_ROOT)</c> equal to the window), each with
/// its pid and creation time -- the thread id when the process cannot be opened --
/// its class, title, rectangle, image and time. <b>Create, show and foreground are
/// kept as separate events about one handle</b>, which is the lesson of the first
/// watcher this repository wrote: it keyed on the handle alone and reported 308
/// creates and 0 shows for a run that showed windows.
/// </para>
/// <para>
/// <b>Whose a window is, in three cases and no fourth</b>: the host's own; a process
/// descending from the host by a live parent chain, each parent created before its
/// child; or an image under a root only the suite runs from -- the repository,
/// <see cref="ScratchRoot.Path"/> and <see cref="ScratchRoot.ProfileScratch"/>.
/// <b>The machine's own browsers root is never one of them</b>, because the
/// maintainer's own sessions run Chromium from it; a suite-started browser still
/// counts through its parent chain. The rule is <see cref="Classify"/>, a pure
/// function, driven both ways by synthetic events on every run.
/// </para>
/// <para>
/// <b>What it cannot see, stated and not implied.</b> Only the desktop its thread is
/// on, which is the property that makes <see cref="PrivateDesktop"/> work and the
/// reason a child started on one is invisible to it. A window drawn by a process
/// that is none of the three -- a Windows Terminal window hosting a console the
/// suite forgot to hide, or a toast the shell draws -- is not attributable here;
/// the <c>CreateNoWindow</c> scan and the tests-only toast ban are what cover those.
/// </para>
/// </remarks>
internal static partial class WindowWatch
{
    /// <summary>The label this row carries in the coverage block.</summary>
    public const string Title = "windows";

    /// <summary>The state word for a watched run that showed nothing.</summary>
    public const string CleanState = "CLEAN  ";

    /// <summary>The state word for a run that showed something of its own.</summary>
    public const string ShownState = "SHOWN  ";

    /// <summary>The state word for a run the watch never covered.</summary>
    public const string UnwatchedState = "UNWATCH";

    /// <summary>
    /// A sentence the refusal carries and nothing else in a run prints, so a
    /// child's console can be read for it.
    /// </summary>
    public const string RefusalMarker = "No suite run may put a window on the screen or take the foreground.";

    /// <summary>
    /// Set by one launcher only, the positive control in <c>WindowWatchTests</c>: the
    /// class the planted arm shows, in a child started on a private desktop.
    /// </summary>
    public const string PlantVariable = "BROWSERAI_SUITE_WINDOW_PLANT";

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint GetAncestorRoot = 2;
    private const uint WmQuit = 0x0012;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int UoiName = 2;

    /// <summary>How far up a parent chain is walked before it is given up on.</summary>
    private const int AncestryLimit = 32;

    private static readonly Lock Gate = new();
    private static readonly List<(WindowSighting Sighting, WindowOwnership Ownership)> OffenceList = [];
    private static readonly HashSet<string> SeenClasses = new(StringComparer.Ordinal);

    /// <summary>
    /// Set by the hook thread once its hooks are in or have failed. Static and
    /// never disposed: a thread that reports after the start gave up waiting would
    /// otherwise set a disposed event, and an exception on that thread ends the
    /// process.
    /// </summary>
    private static readonly ManualResetEventSlim Ready = new(initialState: false);

    private static Thread? _thread;
    private static uint _threadId;
    private static WindowWatchContext? _context;
    private static string? _failure;
    private static bool _watched;
    private static bool _stopped;
    private static string _desktop = "<unread>";
    private static int _events;
    private static int _visibleRoots;

    /// <summary>This run's reading, as it stands now.</summary>
    public static WindowWatchReading Reading
    {
        get
        {
            lock (Gate)
            {
                return new WindowWatchReading(
                    _watched,
                    _failure,
                    _desktop,
                    Volatile.Read(ref _events),
                    _visibleRoots,
                    _context?.Baseline.Count ?? 0,
                    [.. OffenceList]);
            }
        }
    }

    /// <summary>This run's row in the coverage block.</summary>
    public static string CoverageRow => RowFor(Reading);

    /// <summary>What this run's reading costs it.</summary>
    public static WindowWatchDecision Decision => Decide(Judge(Reading), SuiteEnvironment.IsReleaseRun);

    /// <summary>
    /// Whether this process saw a visible root window of this class, whoever it
    /// belonged to.
    /// </summary>
    /// <param name="className">The class.</param>
    /// <returns><see langword="true"/> when one was reported.</returns>
    public static bool SawClass(string className)
    {
        lock (Gate)
        {
            return SeenClasses.Contains(className);
        }
    }

    /// <summary>
    /// Takes the baseline and starts the hook thread. Called once, from the
    /// session hook.
    /// </summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_thread is not null || _failure is not null)
            {
                return;
            }

            try
            {
                _context = new WindowWatchContext(
                    new ProcessMark(Environment.ProcessId, ProcessIdentity.CreationTimeOf(Environment.ProcessId)),
                    SuiteRoots(),
                    BrowserAiPaths.BrowsersDirectory,
                    TakeTheBaseline());
            }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or IOException)
            {
                _failure = $"the baseline could not be taken: {failure.Message}";
                return;
            }
        }

        var thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "BrowserAI suite window watch",
        };

        lock (Gate)
        {
            _thread = thread;
        }

        thread.Start();

        // A hang detector: the thread installs two hooks and signals, whether they
        // went in or not, so a wait that runs out means it never got that far.
        if (!Ready.Wait(TestDefaults.ProcessHang))
        {
            lock (Gate)
            {
                _failure = "the hook thread did not report within the suite's own hang detector";
            }
        }
    }

    /// <summary>
    /// Stops the hook thread and sweeps for any window of the suite's still open.
    /// Called once, from the session hook, before the block is written.
    /// </summary>
    public static void Stop()
    {
        Thread? thread;
        uint threadId;
        bool watched;

        lock (Gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            thread = _thread;
            threadId = _threadId;
            watched = _watched;
        }

        if (thread is not null)
        {
            // WM_QUIT is retrieved after the events already queued ahead of it are
            // delivered, so the reading below includes them.
            if (watched)
            {
                _ = PostThreadMessageW(threadId, WmQuit, nint.Zero, nint.Zero);
            }

            _ = thread.Join(TestDefaults.ProcessHang);
        }

        if (_context is not { } context)
        {
            return;
        }

        // ⚠️ THE CLOSING SWEEP. A window of the suite's that is still open at the
        // end was shown by this run whether or not its event arrived, so it is
        // counted either way -- once, when its show was not already recorded.
        foreach (var window in TopLevelWindows.All())
        {
            if (!TopLevelWindows.IsVisible(window) || GetAncestor(window, GetAncestorRoot) != window)
            {
                continue;
            }

            var sighting = Read(WindowEventKind.StillOpen, window);
            var ownership = Classify(sighting, context);

            if (!IsOffence(ownership))
            {
                continue;
            }

            lock (Gate)
            {
                if (!OffenceList.Exists(offence => offence.Sighting.Window == sighting.Window && offence.Sighting.ProcessId == sighting.ProcessId))
                {
                    OffenceList.Add((sighting, ownership));
                    _ = SeenClasses.Add(sighting.ClassName);
                }
            }
        }
    }

    /// <summary>
    /// Whose a window is, as a pure function of what was read about it and what
    /// it is judged against.
    /// </summary>
    /// <param name="sighting">The window.</param>
    /// <param name="context">The host, the suite's roots, the shared browsers root and the baseline.</param>
    /// <returns>Why it is the suite's, or that it is not.</returns>
    public static WindowOwnership Classify(WindowSighting sighting, WindowWatchContext context)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Baseline.Contains((sighting.Window, sighting.ProcessId)))
        {
            return WindowOwnership.Baseline;
        }

        if (sighting.ProcessId == context.Host.ProcessId
            && (sighting.CreatedFileTime is null || sighting.CreatedFileTime == context.Host.CreatedFileTime))
        {
            return WindowOwnership.Host;
        }

        if (sighting.CreatedFileTime is { } created
            && DescendsFrom(new ProcessMark(sighting.ProcessId, created), sighting.Ancestry, context.Host))
        {
            return WindowOwnership.Descendant;
        }

        return sighting.ImagePath is { } image
            && !IsUnder(image, context.SharedBrowsersRoot)
            && context.SuiteRoots.Any(root => IsUnder(image, root))
            ? WindowOwnership.UnderSuiteRoot
            : WindowOwnership.NotOurs;
    }

    /// <summary>
    /// Whether a live parent chain reaches the host, every parent created no later
    /// than its child.
    /// </summary>
    /// <remarks>
    /// <b>The order is what makes a chain an identity.</b> A parent pid names
    /// whoever holds that number now, and Windows reuses numbers: a "parent"
    /// created after its child is a stranger that inherited the pid of one that
    /// has gone, and the chain stops there.
    /// </remarks>
    /// <param name="self">The window's owner.</param>
    /// <param name="ancestry">Its chain, nearest first.</param>
    /// <param name="host">The test host.</param>
    /// <returns>Whether it descends from the host.</returns>
    public static bool DescendsFrom(ProcessMark self, IReadOnlyList<ProcessMark> ancestry, ProcessMark host)
    {
        ArgumentNullException.ThrowIfNull(ancestry);

        var child = self;

        foreach (var parent in ancestry)
        {
            if (parent.CreatedFileTime > child.CreatedFileTime)
            {
                return false;
            }

            if (parent == host)
            {
                return true;
            }

            child = parent;
        }

        return false;
    }

    /// <summary>Whether a path lies under a root, whole segments only.</summary>
    /// <param name="path">An absolute path.</param>
    /// <param name="root">An absolute directory.</param>
    /// <returns>Whether the path is inside it.</returns>
    public static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
        {
            return false;
        }

        var normalisedRoot = root.Replace('/', '\\').TrimEnd('\\') + "\\";

        return path.Replace('/', '\\').StartsWith(normalisedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether an ownership is one this run has to answer for.</summary>
    /// <param name="ownership">The classification.</param>
    /// <returns><see langword="true"/> for the three ways a window is the suite's.</returns>
    public static bool IsOffence(WindowOwnership ownership) =>
        ownership is WindowOwnership.Host or WindowOwnership.Descendant or WindowOwnership.UnderSuiteRoot;

    /// <summary>What a reading says about the run.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The verdict.</returns>
    public static WindowWatchVerdict Judge(WindowWatchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return !reading.Watched
            ? WindowWatchVerdict.Unwatched
            : reading.Offences.Count is not 0 ? WindowWatchVerdict.Shown : WindowWatchVerdict.Clean;
    }

    /// <summary>
    /// What a verdict costs: a shown window fails every run, and a run nobody
    /// watched fails only a release.
    /// </summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="isReleaseRun">Whether this run asked to be a release.</param>
    /// <returns>The decision.</returns>
    public static WindowWatchDecision Decide(WindowWatchVerdict verdict, bool isReleaseRun) => verdict switch
    {
        WindowWatchVerdict.Clean => WindowWatchDecision.Proceed,
        WindowWatchVerdict.Shown => WindowWatchDecision.Refuse,
        _ => isReleaseRun ? WindowWatchDecision.Refuse : WindowWatchDecision.Proceed,
    };

    /// <summary>The seven-character state the block prints.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The word.</returns>
    public static string StateWord(WindowWatchVerdict verdict) => verdict switch
    {
        WindowWatchVerdict.Clean => CleanState,
        WindowWatchVerdict.Shown => ShownState,
        _ => UnwatchedState,
    };

    /// <summary>The row, and under a shown verdict one line per window.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The text.</returns>
    public static string RowFor(WindowWatchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);
        var row = new StringBuilder("  ").Append(Title.PadRight(20)).Append(StateWord(verdict)).Append("  ");

        switch (verdict)
        {
            case WindowWatchVerdict.Clean:
                _ = row.Append(CultureInfo.InvariantCulture, $"nothing this run started showed a window or took the foreground on desktop '{reading.Desktop}': ")
                    .Append(CultureInfo.InvariantCulture, $"{reading.Events} events, {reading.VisibleRoots} about visible root windows and none of them this run's, {reading.Baseline} windows in the baseline");
                break;

            case WindowWatchVerdict.Shown:
                _ = row.Append(CultureInfo.InvariantCulture, $"⚠️ {reading.Offences.Count} window event(s) of this run's own on desktop '{reading.Desktop}':");

                foreach (var (sighting, ownership) in reading.Offences)
                {
                    _ = row.Append('\n').Append("      ").Append(Describe(sighting, ownership));
                }

                _ = row.Append('\n')
                    .Append("      ⚠️  THIS RUN PUT A WINDOW ON THE SCREEN OR TOOK THE FOREGROUND, and it fails for it.\n")
                    .Append("      The maintainer, 2026-09-24: \"make sure this focus stealing is not something that\n")
                    .Append("      ends up in the testbed.\" Start a Windows-subsystem child on a PrivateDesktop.");
                break;

            default:
                _ = row.Append(reading.Failure ?? "the watch was never started")
                    .Append(", so this run cannot say whether it showed a window; ")
                    .Append(SuiteEnvironment.ReleaseRunVariable)
                    .Append("=1 makes this state a failure");
                break;
        }

        return row.ToString();
    }

    /// <summary>The sentence the session hook fails a run with.</summary>
    /// <param name="reading">The reading.</param>
    /// <param name="isReleaseRun">Whether this run asked to be a release.</param>
    /// <returns>The refusal, or <see langword="null"/> when there is nothing to refuse.</returns>
    public static string? Refusal(WindowWatchReading reading, bool isReleaseRun)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);

        if (Decide(verdict, isReleaseRun) is WindowWatchDecision.Proceed)
        {
            return null;
        }

        if (verdict is WindowWatchVerdict.Unwatched)
        {
            return $"The window watch did not run ({reading.Failure ?? "never started"}), and a release run may not claim a screen it never watched. "
                + $"Unset {SuiteEnvironment.ReleaseRunVariable} or run where SetWinEventHook answers.";
        }

        var shown = string.Join(
            "; ",
            reading.Offences.Select(offence =>
                $"{offence.Sighting.ClassName} '{offence.Sighting.Title}' from {offence.Sighting.ImagePath ?? "an image that could not be read"}"));

        return $"This run showed {reading.Offences.Count} window event(s) from its own processes on desktop '{reading.Desktop}': {shown}. "
            + RefusalMarker
            + " The maintainer, 2026-09-24: \"make sure this focus stealing is not something that ends up in the testbed.\" "
            + $"The coverage block's {Title} row names each one with its pid and time; start the child on a PrivateDesktop, or stop it showing.";
    }

    /// <summary>One window, the way the row prints it.</summary>
    /// <param name="sighting">The window.</param>
    /// <param name="ownership">Why it is the suite's.</param>
    /// <returns>The line.</returns>
    public static string Describe(WindowSighting sighting, WindowOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(sighting);

        var identity = sighting.CreatedFileTime is { } created
            ? $"pid {sighting.ProcessId.ToString(CultureInfo.InvariantCulture)}@{created.ToString(CultureInfo.InvariantCulture)}"
            : $"pid {sighting.ProcessId.ToString(CultureInfo.InvariantCulture)} thread {sighting.ThreadId.ToString(CultureInfo.InvariantCulture)}";

        var why = ownership switch
        {
            WindowOwnership.Host => "the test host's own",
            WindowOwnership.Descendant => "a descendant of the test host",
            WindowOwnership.UnderSuiteRoot => "an image under a suite root",
            WindowOwnership.Baseline => "in the baseline",
            _ => "not the suite's",
        };

        var what = sighting.Kind switch
        {
            WindowEventKind.Created => "created visible",
            WindowEventKind.Shown => "shown",
            WindowEventKind.Foreground => "took the foreground",
            _ => "still open at the end",
        };

        return $"{what}: {sighting.ClassName} '{sighting.Title}' {identity} "
            + $"{sighting.ImagePath ?? "<image unread>"} at {sighting.Rectangle}, "
            + $"{sighting.At.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}Z, {why}";
    }

    /// <summary>The three roots only the suite runs images from.</summary>
    /// <returns>The roots.</returns>
    private static List<string> SuiteRoots() =>
    [
        RepositoryLayout.Root.FullName,

        // As composed and never swept: see ScratchRoot.PathAsComposed for why a
        // session hook must not run the reclaim.
        ScratchRoot.PathAsComposed,
        ScratchRoot.ProfileScratchAsComposed,
    ];

    /// <summary>Every visible top-level window on this desktop, with its owner.</summary>
    /// <returns>The baseline.</returns>
    private static HashSet<(long Window, int ProcessId)> TakeTheBaseline() =>
    [
        .. TopLevelWindows.All()
            .Where(TopLevelWindows.IsVisible)
            .Select(window => ((long)window, TopLevelWindows.ProcessIdOf(window))),
    ];

    /// <summary>The hook thread: two hooks, then a message loop until WM_QUIT.</summary>
    private static unsafe void Pump()
    {
        var threadId = GetCurrentThreadId();
        var desktop = DesktopName(GetThreadDesktop(threadId));

        var objects = SetWinEventHook(EventObjectCreate, EventObjectShow, nint.Zero, &OnEvent, 0, 0, WinEventOutOfContext);
        var objectsError = Marshal.GetLastPInvokeError();
        var foreground = SetWinEventHook(EventSystemForeground, EventSystemForeground, nint.Zero, &OnEvent, 0, 0, WinEventOutOfContext);
        var foregroundError = Marshal.GetLastPInvokeError();

        lock (Gate)
        {
            _threadId = threadId;
            _desktop = desktop;

            if (objects == nint.Zero || foreground == nint.Zero)
            {
                _failure = $"SetWinEventHook refused: object events {(objects == nint.Zero ? $"failed with Win32 error {objectsError}" : "installed")}, "
                    + $"foreground events {(foreground == nint.Zero ? $"failed with Win32 error {foregroundError}" : "installed")}";
            }
            else
            {
                _watched = true;
            }
        }

        Ready.Set();

        try
        {
            if (objects == nint.Zero || foreground == nint.Zero)
            {
                return;
            }

            while (GetMessageW(out var message, nint.Zero, 0, 0) > 0)
            {
                _ = DispatchMessageW(ref message);
            }
        }
        finally
        {
            if (objects != nint.Zero)
            {
                _ = UnhookWinEvent(objects);
            }

            if (foreground != nint.Zero)
            {
                _ = UnhookWinEvent(foreground);
            }
        }
    }

    /// <summary>The WinEvent callback, which must never throw across the native boundary.</summary>
    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        _ = Interlocked.Increment(ref _events);

        try
        {
            var kind = eventType switch
            {
                EventObjectCreate => WindowEventKind.Created,
                EventObjectShow => WindowEventKind.Shown,
                EventSystemForeground => WindowEventKind.Foreground,
                _ => (WindowEventKind?)null,
            };

            if (kind is null
                || window == nint.Zero
                || objectId is not ObjectIdWindow
                || childId is not ChildIdSelf
                || GetAncestor(window, GetAncestorRoot) != window
                || !TopLevelWindows.IsVisible(window)
                || Volatile.Read(ref _context) is not { } context)
            {
                return;
            }

            var sighting = Read(kind.Value, window);
            var ownership = Classify(sighting, context);

            lock (Gate)
            {
                _visibleRoots++;
                _ = SeenClasses.Add(sighting.ClassName);

                if (IsOffence(ownership))
                {
                    OffenceList.Add((sighting, ownership));
                }
            }
        }
#pragma warning disable CA1031 // An exception out of an UnmanagedCallersOnly method ends the process; one event lost is recorded in the count and nothing else.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Reads one window and its owner.</summary>
    /// <param name="kind">What was reported.</param>
    /// <param name="window">The window.</param>
    /// <returns>The sighting.</returns>
    private static WindowSighting Read(WindowEventKind kind, nint window)
    {
        var threadId = GetWindowThreadProcessId(window, out var processId);
        var owner = (int)processId;

        long? created = null;

        try
        {
            created = ProcessIdentity.CreationTimeOf(owner);
        }
        catch (Win32Exception)
        {
        }

        return new WindowSighting(
            kind,
            window,
            owner,
            created,
            (int)threadId,
            TopLevelWindows.ClassNameOf(window),
            TitleOf(window),
            RectangleOf(window),
            ImageOf(owner),
            DateTime.UtcNow,
            created is null ? [] : AncestryOf(owner));
    }

    /// <summary>The live parent chain above a process, nearest first.</summary>
    /// <param name="processId">The process.</param>
    /// <returns>Each parent that could still be opened, until one could not.</returns>
    private static List<ProcessMark> AncestryOf(int processId)
    {
        var chain = new List<ProcessMark>();
        var current = processId;

        for (var depth = 0; depth < AncestryLimit; depth++)
        {
            try
            {
                var parent = ParentProcess.IdOf(current);

                if (parent is 0 || parent == current)
                {
                    break;
                }

                chain.Add(new ProcessMark(parent, ProcessIdentity.CreationTimeOf(parent)));
                current = parent;
            }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
            {
                // A parent that is gone, or one this user may not open, ends the
                // chain -- and a chain that ends before the host is not a descent.
                break;
            }
        }

        return chain;
    }

    /// <summary>A window's kernel-side name, read without sending it a message.</summary>
    private static unsafe string TitleOf(nint window)
    {
        var buffer = new char[256];

        fixed (char* start = buffer)
        {
            var copied = InternalGetWindowText(window, start, buffer.Length);

            return copied <= 0 ? string.Empty : new string(start, 0, copied);
        }
    }

    /// <summary>Where a window is, as <c>x,y wxh</c>.</summary>
    private static string RectangleOf(nint window) =>
        GetWindowRect(window, out var rectangle)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{rectangle.Left},{rectangle.Top} {rectangle.Right - rectangle.Left}x{rectangle.Bottom - rectangle.Top}")
            : "<unread>";

    /// <summary>A process's image path, or <see langword="null"/> when it cannot be opened.</summary>
    private static unsafe string? ImageOf(int processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);

        if (process.IsInvalid)
        {
            return null;
        }

        var buffer = new char[1024];
        var length = (uint)buffer.Length;

        fixed (char* start = buffer)
        {
            return QueryFullProcessImageNameW(process, 0, start, ref length) ? new string(start, 0, (int)length) : null;
        }
    }

    /// <summary>A desktop's name.</summary>
    private static unsafe string DesktopName(nint desktop)
    {
        var buffer = new char[256];

        fixed (char* start = buffer)
        {
            return desktop != nint.Zero && GetUserObjectInformationW(desktop, UoiName, start, (uint)(buffer.Length * sizeof(char)), out _)
                ? new string(start)
                : "<unread>";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback,
        uint processId,
        uint threadId,
        uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetMessageW(out WindowMessage message, nint window, uint filterMin, uint filterMax);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref WindowMessage message);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetThreadDesktop(uint threadId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetAncestor(nint window, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial int InternalGetWindowText(nint window, char* text, int maxCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out WindowRectangle rectangle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* name, ref uint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(nint handle, int index, char* information, uint length, out uint needed);
}

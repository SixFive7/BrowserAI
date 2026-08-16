// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>
/// The message-only windows Chromium publishes for its own single-instance
/// logic, and the titles they carry — which is how a running browser is tied to
/// the profile directory it opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is attribution, and attribution is allowed to fail.</b>
/// [Detection](BrowserProcesses.cs) is the fully documented half and it is what
/// decides: a process is a candidate because its <i>full image path</i> is a
/// binary BrowserAI provisioned. Everything here only answers <i>which
/// directory</i>, so that the ownership test can run and a report can name the
/// session. When it comes back empty the sweep refuses to kill and says so —
/// the undocumented path can only ever cause BrowserAI to decline to act.
/// </para>
/// <para>
/// ⚠️ <b>We depend on undocumented behaviour of a documented function, and this
/// is where it lives.</b> <c>GetWindowTextW</c>'s contract says a window owned
/// by another process <i>and having a caption</i> is read, and that a window
/// with no caption returns a null string. A <c>Chrome_MessageWindow</c> is
/// created with <c>dwStyle = 0</c> and has no caption, so by the documentation
/// this should return nothing. It does not: cross-process,
/// <c>GetWindowTextW</c> never sends <c>WM_GETTEXT</c> at all — it reads the
/// kernel-side window name set at <c>CreateWindowExW</c>, which is why a hung,
/// suspended or deliberately hostile owner cannot defeat it. Measured across
/// ~1,550 windows, every integrity level, a thread blocked 15 s inside its own
/// WndProc and a fully suspended Chromium
/// ([kb](../../kb/windows/detection.md#cross-process-title-reads--settled-by-two-independent-agents)).
/// </para>
/// <para>
/// <b><see cref="InternalWindowText"/> is the fallback, and it is the documented
/// spelling of the same read.</b> It is declared unguarded in the public SDK,
/// documented on MS Learn as copying the window text <i>without sending
/// <c>WM_GETTEXT</c></i>, and measured to agree with <c>GetWindowTextW</c> on
/// every one of those ~1,550 windows. Its caveat is availability rather than
/// semantics, and it is reached only when the documented API returned nothing —
/// so on this machine, today, it never runs. It is kept because the day
/// <c>GetWindowTextW</c> starts honouring its own contract is the day the sweep
/// goes blind, and this is the API that would still answer.
/// </para>
/// <para>
/// <b><c>SendMessageTimeoutW(WM_GETTEXT)</c> must never be used here.</b> It is
/// the one API a stray in exactly the state we care about — hung, wedged,
/// mid-crash — can defeat, returning an empty string against a suppressing
/// WndProc and failing outright after a full timeout against one that does not
/// pump. <c>SMTO_ABORTIFHUNG</c> does not abort early.
/// </para>
/// </remarks>
internal static partial class MessageWindows
{
    /// <summary>
    /// The window class Chromium's <c>ProcessSingleton</c> publishes.
    /// </summary>
    /// <remarks>
    /// <b>The class alone is ambiguous and the title match is load-bearing.</b>
    /// The same process also owns a <c>Chrome_MessageWindow</c> titled
    /// <c>DeviceMonitorMessageWindow</c> and several nameless ones, and 28
    /// unrelated Electron embedders on this machine publish 55 between them. It
    /// is also <b>forgeable</b>: window classes are per-process, so any program
    /// can register this name and publish any path it likes. Neither fact
    /// matters, because nothing here decides ownership — see the type's remarks.
    /// </remarks>
    public const string ChromiumSingletonClass = "Chrome_MessageWindow";

    /// <summary>
    /// How many times the walk restarts before reporting itself truncated.
    /// </summary>
    /// <remarks>
    /// A restart happens when a window died between two iterations, which is a
    /// live condition rather than a fault — it is exactly what browsers exiting
    /// looks like. Bounded so that a machine churning windows continuously
    /// produces a report saying the walk was incomplete rather than a loop.
    /// </remarks>
    public const int RestartBudget = 5;

    private const int ErrorInvalidWindowHandle = 1400;
    private const int MaximumTitleLength = 32768;

    // HWND_MESSAGE. A parent of this value scopes FindWindowExW to the
    // message-only windows of THIS window station and desktop -- which is also
    // why a sweeper must run in the user's interactive session and never in
    // session 0, where it would find nothing and report success forever.
    private static readonly nint HwndMessage = -3;

    /// <summary>
    /// Every message-only window of one class, with the process that owns it.
    /// </summary>
    /// <param name="className">
    /// The class to walk. <b>Mandatory</b>: a <see langword="null"/> class
    /// returns nothing at all from this parent, as does
    /// <c>EnumChildWindows(HWND_MESSAGE, …)</c>, and <c>EnumWindows</c> — which
    /// finds several hundred top-level windows — has <i>zero</i> overlap with
    /// this set. A walk that dropped the class would silently find none.
    /// </param>
    /// <returns>The windows, and whether the walk had to restart.</returns>
    public static MessageWindowWalk Walk(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        for (var attempt = 0; attempt <= RestartBudget; attempt++)
        {
            var windows = new List<MessageWindow>();
            var previous = nint.Zero;
            bool restart;

            while (true)
            {
                var window = FindWindowExW(HwndMessage, previous, className, null);

                if (window == nint.Zero)
                {
                    // THE DISCRIMINATOR, and the whole reason this is a loop
                    // rather than a walk. Normal exhaustion returns NULL with
                    // last error 0; a `previous` handle destroyed between two
                    // iterations returns NULL with ERROR_INVALID_WINDOW_HANDLE
                    // and stops the walk early. Unchecked, the sweep
                    // under-reports EXACTLY when browsers are exiting, which is
                    // when it is most likely to be running.
                    restart = Marshal.GetLastPInvokeError() is ErrorInvalidWindowHandle;
                    break;
                }

                windows.Add(new MessageWindow(window, ProcessIdOf(window)));
                previous = window;
            }

            if (!restart)
            {
                return new MessageWindowWalk(windows, attempt, Truncated: false);
            }
        }

        return new MessageWindowWalk([], RestartBudget, Truncated: true);
    }

    /// <summary>
    /// The one window of a class carrying exactly this title, if it exists.
    /// </summary>
    /// <remarks>
    /// The exact-title probe, as distinct from <see cref="Walk"/>. It is
    /// structurally incapable of returning a profile the caller did not name,
    /// which the enumerating walk is not — enumeration hands back strangers'
    /// paths, and there the ownership test is the entire safety boundary.
    /// </remarks>
    /// <param name="className">The window class.</param>
    /// <param name="title">The exact title. The comparison Windows makes is case-insensitive.</param>
    /// <returns>The window handle, or <see cref="nint.Zero"/>.</returns>
    public static nint FindExactly(string className, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(title);

        return FindWindowExW(HwndMessage, nint.Zero, className, title);
    }

    /// <summary>The process that owns a window.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>Its pid, or zero when the window has already gone.</returns>
    public static int ProcessIdOf(nint window) =>
        GetWindowThreadProcessId(window, out var processId) is 0 ? 0 : (int)processId;

    /// <summary>
    /// The window's title as the <b>documented</b> API reads it — which
    /// cross-process is the kernel-side name and not a <c>WM_GETTEXT</c>.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns>The title, or an empty string for a nameless or dead window.</returns>
    public static unsafe string WindowText(nint window)
    {
        // GetWindowTextLengthW is the cheapest "is this named at all" filter
        // there is -- 44 of the 55 windows on this machine are genuinely
        // nameless, and this skips every one of them before any allocation.
        var length = GetWindowTextLengthW(window);

        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[Math.Min(length, MaximumTitleLength) + 1];

        fixed (char* start = buffer)
        {
            var copied = GetWindowTextW(window, start, buffer.Length);

            return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
        }
    }

    /// <summary>
    /// The same title through <c>InternalGetWindowText</c> — the fallback, and
    /// the suite's oracle.
    /// </summary>
    /// <remarks>
    /// Kept as a separate member rather than folded into
    /// <see cref="TitleOf"/> so the suite can compare the two APIs on every
    /// window it enumerates. Measured zero divergences across ~1,550 windows;
    /// the comparison is what would say so if that ever stopped being true.
    /// </remarks>
    /// <param name="window">The window handle.</param>
    /// <returns>The title, or an empty string.</returns>
    public static unsafe string InternalWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        var buffer = new char[Math.Min(length <= 0 ? 260 : length, MaximumTitleLength) + 1];

        fixed (char* start = buffer)
        {
            var copied = InternalGetWindowText(window, start, buffer.Length);

            return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
        }
    }

    /// <summary>
    /// The window's title, documented API first and the fallback only when that
    /// returned nothing.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns>
    /// The title, or <see langword="null"/> when both reads came back empty —
    /// which is the ordinary case for the several nameless windows every
    /// embedder owns, and the case in which the sweep refuses to act.
    /// </returns>
    public static string? TitleOf(nint window)
    {
        var documented = WindowText(window);

        if (documented.Length is not 0)
        {
            return documented;
        }

        var fallback = InternalWindowText(window);

        return fallback.Length is 0 ? null : fallback;
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint hWndParent, nint hWndChildAfter, string lpszClass, string? lpszWindow);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    /// <summary>
    /// The documented API for reading a window's kernel-side name without
    /// sending it a message. See the type's remarks for why it is here at all.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static unsafe partial int InternalGetWindowText(nint hWnd, char* pString, int cchMaxCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}

/// <summary>One message-only window, and the process that owns it.</summary>
/// <param name="Handle">Its <c>HWND</c>.</param>
/// <param name="ProcessId">The owning pid, or zero if it had already gone.</param>
internal sealed record MessageWindow(nint Handle, int ProcessId);

/// <summary>What one class-qualified walk of the message-only windows found.</summary>
/// <param name="Windows">Every window of that class, in walk order.</param>
/// <param name="Restarts">How many times a destroyed handle forced a restart.</param>
/// <param name="Truncated">
/// Whether the walk gave up. <b>Reported rather than hidden</b>: an incomplete
/// attribution pass must read as incomplete, or the sweep silently reports
/// fewer strays than exist.
/// </param>
internal sealed record MessageWindowWalk(IReadOnlyList<MessageWindow> Windows, int Restarts, bool Truncated);

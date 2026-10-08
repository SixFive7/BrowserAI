// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>
/// The four reads behind a visible session's input check: the window in front,
/// the process that owns it, the tick count of the last keyboard or mouse input
/// this session received, and the tick count now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reads, and never a hook.</b> F4 a, decided 2026-10-08 by the maintainer: a
/// person's keyboard or mouse input in a visible session's window counts as activity
/// for that session, on his condition that the check does not lag the system. A
/// keyboard or mouse hook would put BrowserAI on the input path: Windows calls a
/// low-level hook each time an input event is about to be posted to a thread's input
/// queue, by sending a message to the thread that installed it
/// (<see href="https://learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc">LowLevelKeyboardProc</see>),
/// and Microsoft's own overview says hooks <i>"tend to slow down the system"</i>
/// (<see href="https://learn.microsoft.com/windows/win32/winmsg/about-hooks">Hooks Overview</see>).
/// These four read state Windows keeps anyway, at a moment BrowserAI chooses, so no
/// input event ever waits for BrowserAI.
/// </para>
/// <para>
/// <b>Four reads a check, however many visible sessions there are.</b> The check
/// reads the foreground window once and compares its owner with every session's
/// browser in memory. It never asks Windows about one session's window at a time,
/// which is the maintainer's second condition: <i>"No 100x fold checks just because
/// there are 100 windows open."</i> What one check costs on the reference machine,
/// and whether a read enters the kernel, is in
/// <see href="../../../kb/windows/processes.md">kb/windows/processes.md</see>.
/// </para>
/// <para>
/// <b>Both tick counts are 32-bit, and the last-input one need not grow.</b>
/// <c>GetTickCount</c> wraps to zero after 49.7 days, and <c>GetLastInputInfo</c>'s
/// tick <i>"is not guaranteed to be incremental"</i>: an event raised by
/// <c>SendInput</c> supplies its own. So a caller compares them as unsigned 32-bit
/// differences against <see cref="TickCount"/>, the counter the last-input tick is
/// documented against, and never against <c>GetTickCount64</c>
/// (<see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getlastinputinfo">GetLastInputInfo</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount">GetTickCount</see>).
/// </para>
/// </remarks>
internal static partial class InputActivity
{
    /// <summary>The window in front, or <see cref="nint.Zero"/> when there is none.</summary>
    /// <remarks>
    /// <i>"The foreground window can be NULL in certain circumstances, such as when a
    /// window is losing activation"</i>
    /// (<see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow">GetForegroundWindow</see>).
    /// A caller reads NULL as <i>unknown</i>.
    /// </remarks>
    /// <returns>The window's handle, or zero.</returns>
    public static nint ForegroundWindow() => GetForegroundWindow();

    /// <summary>The thread that created a window, and the process that owns it.</summary>
    /// <param name="window">The window.</param>
    /// <param name="processId">The owning pid, or zero when the thread is zero.</param>
    /// <returns>The thread's id, or zero when the window has gone.</returns>
    public static uint WindowThread(nint window, out int processId)
    {
        var thread = GetWindowThreadProcessId(window, out var owner);

        // A failed call leaves the pid where it found it, and a LibraryImport out
        // parameter starts as whatever was on the stack, so a zero thread answers
        // a zero pid and never that.
        processId = thread is 0 ? 0 : unchecked((int)owner);

        return thread;
    }

    /// <summary>The tick count of the last keyboard or mouse input this session received.</summary>
    /// <remarks>
    /// <i>"GetLastInputInfo provides session-specific user input information for only
    /// the session that invoked the function"</i>, which is the person's session,
    /// where BrowserAI and its visible browsers run.
    /// </remarks>
    /// <param name="tick">The tick, comparable with <see cref="TickCount"/>, or zero when Windows did not answer.</param>
    /// <returns>Whether Windows answered.</returns>
    public static bool LastInput(out uint tick)
    {
        var info = new LastInputInfo { Size = (uint)Unsafe.SizeOf<LastInputInfo>() };

        if (!GetLastInputInfo(ref info))
        {
            tick = 0;
            return false;
        }

        tick = info.Time;
        return true;
    }

    /// <summary>The milliseconds since Windows started, in 32 bits.</summary>
    /// <returns>The tick count.</returns>
    public static uint TickCount() => GetTickCount();

    // LASTINPUTINFO. Windows refuses the call unless the size is set first.
    // InteropLayoutTests holds the layout against Microsoft's metadata.
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    // System32 only, on every P/Invoke in this repository (CA5392). user32 and
    // kernel32 are KnownDLLs, so the attribute cannot change these four; it is
    // here because the rule is every declaration.
    //
    // No SetLastError: the page documents no error for this one.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    // SetLastError, because the page says to call GetLastError when this answers
    // zero. Nothing reads it: a zero thread is unknown, whatever the reason.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    // No SetLastError: the page documents a zero return and no error to read.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo plii);

    // No SetLastError: it cannot fail.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial uint GetTickCount();
}

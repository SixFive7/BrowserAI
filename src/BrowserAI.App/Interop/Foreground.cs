// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using BrowserAI.Coordination;

namespace BrowserAI.App.Interop;

/// <summary>
/// The two halves of bringing the coordinator's window forward: a second start
/// grants the right, and the coordinator uses it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it takes two processes.</b> The coordinator is usually started by the
/// task scheduler, and a process the task scheduler starts may not take the
/// foreground: <c>AllowSetForegroundWindow</c> on its own pid read false, error 5,
/// three times out of three on 2026-09-24, with this machine's foreground lock
/// timeout at 2147483647 ms
/// ([kb](../../../kb/windows/processes.md#a-process-a-task-starts-may-not-take-the-foreground)).
/// A start the person made from the Start Menu holds the right, so it grants it to
/// the coordinator's pid before it asks for the window, which is Q284 a's design.
/// </para>
/// <para>
/// <b>The pid is the pipe's, read while the connection is live</b>
/// (<c>GetNamedPipeServerProcessId</c>), so it names the process serving that
/// connection at that moment and cannot yet belong to anybody else; nothing but a
/// grant is made with it, and a grant to a pid that has since gone does nothing.
/// </para>
/// <para>
/// <b>The grant does not move anything on the screen.</b> It lets one process set
/// the foreground once, until the person's next input goes elsewhere; the window
/// moves only when the coordinator calls <see cref="Raise"/> on its own window.
/// </para>
/// </remarks>
internal static partial class Foreground
{
    /// <summary><c>SW_RESTORE</c>: un-minimise and activate.</summary>
    private const int ShowRestore = 9;

    /// <summary>The grant a start the person made hands the coordinator.</summary>
    public static IForegroundGrant Grant { get; } = new AllowSetForeground();

    /// <summary>Brings one of this process's windows to the foreground, restoring it first when it is minimised.</summary>
    /// <param name="window">The window.</param>
    /// <returns>Whether Windows made it the foreground window.</returns>
    public static bool Raise(nint window)
    {
        if (window is 0)
        {
            return false;
        }

        if (IsIconic(window))
        {
            _ = ShowWindow(window, ShowRestore);
        }

        return SetForegroundWindow(window);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "AllowSetForegroundWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "IsIconic", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    /// <summary>The real grant.</summary>
    private sealed class AllowSetForeground : IForegroundGrant
    {
        public bool Allow(int processId) => processId > 0 && AllowSetForegroundWindow((uint)processId);
    }
}

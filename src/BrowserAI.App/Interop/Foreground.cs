// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using BrowserAI.Coordination;

namespace BrowserAI.App.Interop;

/// <summary>
/// The grant a second start the person made hands the coordinator: the right to
/// set the foreground once.
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
/// the foreground once, until the person's next input goes elsewhere.
/// <i>Corrected 2026-10-03 (previously "the window moves only when the coordinator
/// calls Raise on its own window"): the configuration window is gone, and with it
/// <c>Raise</c> and the three calls it made. A person's start opens the browser tab
/// itself, so the coordinator has no window to bring forward and the grant moves
/// nothing today; it is kept because it is part of the pipe's hand-over, which the
/// coordinator's tests hold.</i>
/// </para>
/// </remarks>
internal static partial class Foreground
{
    /// <summary>The grant a start the person made hands the coordinator.</summary>
    public static IForegroundGrant Grant { get; } = new AllowSetForeground();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "AllowSetForegroundWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    /// <summary>The real grant.</summary>
    private sealed class AllowSetForeground : IForegroundGrant
    {
        public bool Allow(int processId) => processId > 0 && AllowSetForegroundWindow((uint)processId);
    }
}

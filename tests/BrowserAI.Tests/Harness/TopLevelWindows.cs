// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// <c>EnumWindows</c> and the class name of every window it returns — the
/// oracle that keeps the product's class-qualified message-window walk from
/// being "simplified" into a loop that silently finds nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two sets do not overlap, and that is the whole point.</b>
/// <c>EnumWindows</c> walks top-level windows; a message-only window has
/// <c>HWND_MESSAGE</c> as its parent and is not one. So the obvious-looking
/// rewrite — enumerate all the windows and filter by class — finds <b>zero</b>
/// <c>Chrome_MessageWindow</c>s, on a machine that has dozens of them. It would
/// not throw, it would not warn, and every sweep would report a clean machine
/// forever.
/// </para>
/// <para>
/// This lives in the harness rather than in the product because the product has
/// no reason to enumerate top-level windows at all, and an API kept only to be
/// asserted against belongs beside the assertion.
/// </para>
/// </remarks>
internal static partial class TopLevelWindows
{
    private static readonly Lock Gate = new();
    private static List<nint>? _collecting;

    /// <summary>Every top-level window on this window station and desktop.</summary>
    /// <returns>Their handles.</returns>
    public static unsafe IReadOnlyList<nint> All()
    {
        lock (Gate)
        {
            var collected = new List<nint>();
            _collecting = collected;

            try
            {
                _ = EnumWindows((delegate* unmanaged<nint, nint, int>)&Collect, nint.Zero);
                return collected;
            }
            finally
            {
                _collecting = null;
            }
        }
    }

    /// <summary>Which process owns a window.</summary>
    /// <remarks>
    /// Here so that "no dialog appeared" can be asserted about <b>a browser's</b>
    /// windows rather than about the desktop's. A live machine opens and closes
    /// top-level windows constantly, so a bare before-and-after count is a flaky
    /// assertion dressed as a strict one; the owning process is what makes it
    /// specific.
    /// </remarks>
    /// <param name="window">The window handle.</param>
    /// <returns>The owning pid, or zero if it could not be read.</returns>
    public static int ProcessIdOf(nint window) =>
        GetWindowThreadProcessId(window, out var processId) is 0 ? 0 : (int)processId;

    /// <summary>A window's class name.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>The class name, or an empty string if it could not be read.</returns>
    public static unsafe string ClassNameOf(nint window)
    {
        var buffer = new char[512];

        fixed (char* start = buffer)
        {
            var copied = GetClassNameW(window, start, buffer.Length);

            return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
        }
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint window, nint parameter)
    {
        _collecting?.Add(window);
        return 1;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint parameter);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial int GetClassNameW(nint window, char* className, int maxCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}

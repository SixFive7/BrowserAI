// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A desktop of the suite's own, in this process's window station, that nobody
/// is looking at -- so a child that shows a window shows it there and not on the
/// screen of the person using the machine.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The only thing that keeps a Windows-subsystem child's window off the
/// screen.</b> <c>CREATE_NO_WINDOW</c> governs a console and nothing else, and
/// <c>SW_SHOWNOACTIVATE</c> governs only a GUI child's first
/// <c>ShowWindow(SW_SHOWDEFAULT)</c>. A task dialog is neither: the configuration
/// app's <c>#32770</c> showed on the interactive desktop in every full run from
/// 2026-09-15 to 2026-09-24, and asked for the foreground. The maintainer's rule,
/// verbatim: <i>"make sure this focus stealing is not something that ends up in the
/// testbed."</i>
/// </para>
/// <para>
/// <b>What it costs, stated here because it is the reason the product never does
/// this.</b> Window enumeration, message-window search and WinEvent hooks are all
/// scoped to one desktop, so a process on this desktop is invisible to every one of
/// them run from the default desktop: the stray sweeper would find nothing, and the
/// suite's own window watch finds nothing. That is exactly the property wanted for
/// a test's dialog, and exactly the property that rules it out for a browser.
/// </para>
/// <para>
/// <b>Closed on dispose.</b> A desktop is destroyed when its last handle closes and
/// no thread is attached to it, so a child still running on it keeps it alive until
/// the child's job takes the child away; nothing here switches to it, so the screen
/// never shows it.
/// </para>
/// </remarks>
internal sealed partial class PrivateDesktop : IDisposable
{
    // The rights the suite uses and nothing else. DESKTOP_SWITCHDESKTOP is
    // deliberately absent: nothing here may ever put this desktop on the screen.
    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopCreateWindow = 0x0002;
    private const uint DesktopCreateMenu = 0x0004;
    private const uint DesktopHookControl = 0x0008;
    private const uint DesktopEnumerate = 0x0040;
    private const uint DesktopWriteObjects = 0x0080;
    private const uint ReadControl = 0x00020000;

    private const int UoiName = 2;
    private const uint WmClose = 0x0010;

    private nint _handle;

    private PrivateDesktop(nint handle, string name, string startupName)
    {
        _handle = handle;
        Name = name;
        StartupName = startupName;
    }

    /// <summary>The desktop's own name.</summary>
    public string Name { get; }

    /// <summary>
    /// <c>station\desktop</c>, which is what <c>STARTUPINFO.lpDesktop</c> takes.
    /// </summary>
    public string StartupName { get; }

    /// <summary>Creates a desktop nobody is looking at.</summary>
    /// <param name="purpose">A word for the name, so a leaked one says whose it was.</param>
    /// <returns>The desktop. Dispose it.</returns>
    /// <exception cref="Win32Exception">Windows refused the desktop.</exception>
    public static PrivateDesktop Create(string purpose)
    {
        var name = $"BrowserAI-suite-{purpose}-{Guid.NewGuid():N}";

        var handle = CreateDesktopW(
            name,
            nint.Zero,
            nint.Zero,
            0,
            DesktopReadObjects | DesktopCreateWindow | DesktopCreateMenu | DesktopHookControl
                | DesktopEnumerate | DesktopWriteObjects | ReadControl,
            nint.Zero);

        return handle == nint.Zero
            ? throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateDesktopW refused '{name}'.")
            : new PrivateDesktop(handle, name, $"{StationName()}\\{name}");
    }

    /// <summary>
    /// Starts a process on this desktop, inside a job, through the product's own
    /// launcher.
    /// </summary>
    /// <param name="job">The job it is created in.</param>
    /// <param name="command">The executable's absolute path.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <param name="environment">Its whole environment block.</param>
    /// <returns>The running child.</returns>
    public LaunchedProcess Launch(
        JobObject job,
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var launched = JobLauncher.Start(job, command, arguments, workingDirectory, environment, StartupName);

        // Written where the NEXT run can read it, as every other launch in the
        // suite does: the job contains the child while this run is alive, and the
        // record names it if this run is killed with the job.
        SpawnRecord.Add(launched.Id);

        return launched;
    }

    /// <summary>Every top-level window on this desktop.</summary>
    /// <returns>Their handles.</returns>
    public unsafe IReadOnlyList<nint> TopLevelWindows()
    {
        var collected = new List<nint>();
        var pinned = GCHandle.Alloc(collected);

        try
        {
            _ = EnumDesktopWindows(_handle, (delegate* unmanaged<nint, nint, int>)&Collect, GCHandle.ToIntPtr(pinned));
            return collected;
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>
    /// Asks a window on this desktop to close, from a thread attached to it.
    /// </summary>
    /// <remarks>
    /// <b>From a thread of this desktop and not from the caller's.</b>
    /// Microsoft's own page on desktops says <i>"Window messages can be sent only
    /// between processes that are on the same desktop"</i> (learn.microsoft.com,
    /// windows/win32/winstation/desktops, read 2026-09-24), so the post is made by
    /// a fresh thread that has joined this desktop first -- a thread with no
    /// window and no hook, which is what <c>SetThreadDesktop</c> requires. It is
    /// posted and not sent, for <see cref="Harness.TopLevelWindows.Close"/>'s
    /// reason.
    /// </remarks>
    /// <param name="window">The window.</param>
    /// <returns>Whether the thread joined the desktop and the message was posted.</returns>
    public bool Close(nint window)
    {
        var posted = false;

        var thread = new Thread(() =>
        {
            posted = SetThreadDesktop(_handle) && PostMessageW(window, WmClose, nint.Zero, nint.Zero);
        })
        {
            IsBackground = true,
            Name = "BrowserAI suite private-desktop close",
        };

        thread.Start();
        thread.Join();

        return posted;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, nint.Zero);

        if (handle != nint.Zero)
        {
            _ = CloseDesktop(handle);
        }
    }

    /// <summary>This process's window station, by name.</summary>
    /// <returns>Its name, <c>WinSta0</c> on an interactive session.</returns>
    private static unsafe string StationName()
    {
        var buffer = new char[256];

        fixed (char* start = buffer)
        {
            return GetUserObjectInformationW(GetProcessWindowStation(), UoiName, start, (uint)(buffer.Length * sizeof(char)), out _)
                ? new string(start)
                : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read this process's window station name.");
        }
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint window, nint parameter)
    {
        if (GCHandle.FromIntPtr(parameter).Target is List<nint> collected)
        {
            collected.Add(window);
        }

        return 1;
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateDesktopW(string desktop, nint device, nint devmode, uint flags, uint desiredAccess, nint attributes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumDesktopWindows(nint desktop, delegate* unmanaged<nint, nint, int> callback, nint parameter);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadDesktop(nint desktop);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetProcessWindowStation();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(nint handle, int index, char* information, uint length, out uint needed);
}

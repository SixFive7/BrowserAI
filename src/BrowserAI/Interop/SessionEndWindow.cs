// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>What Windows told the background's hidden window.</summary>
internal enum SessionEndNotice
{
    /// <summary><c>WM_ENDSESSION</c> with its flag set: the person is signing out, or Windows is shutting down.</summary>
    SessionEnding,

    /// <summary>
    /// <c>WM_CLOSE</c>: the Task Scheduler's End command, <c>IRunningTask::Stop</c> or
    /// <c>schtasks /end</c>, about a second before it terminates the process.
    /// </summary>
    CloseRequested,
}

/// <summary>
/// The background's one window: top-level, never shown, and there only so Windows
/// can tell the background that the session is ending or that the Task Scheduler is
/// ending it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Settled by the root session on 2026-10-08, from the step-0 research</b>: the
/// background needs a hidden top-level window to receive <c>WM_QUERYENDSESSION</c>
/// and <c>WM_ENDSESSION</c>, so a sign-out or a shutdown is recorded as a clean end
/// and not as a crash under R. Microsoft's own advice, read for that research:
/// "To receive events when a user signs out or the device shuts down in these
/// circumstances, create a hidden window in your console application, and then handle
/// the WM_QUERYENDSESSION and WM_ENDSESSION window messages that the hidden window
/// receives" (<c>SetConsoleCtrlHandler</c>); a message-only window "does not receive
/// broadcast messages" (Window Features); and an application without a visible window
/// "is automatically terminated if it does not respond to WM_QUERYENDSESSION or
/// WM_ENDSESSION within 5 seconds" (Shutdown Changes for Windows Vista).
/// </para>
/// <para>
/// <b>It also hears the Task Scheduler's End command</b>, measured 2026-10-08 with
/// stand-ins: a task process with a hidden top-level window gets <c>WM_COMMAND</c>
/// twice and <c>WM_CLOSE</c> within 45 to 96 ms of the request, and is terminated
/// about a second after it if it has not exited; one with no window gets no notice at
/// all, and the same termination. So <see cref="SessionEndNotice.CloseRequested"/> is
/// that command, and the second it leaves is enough to mark the record and nothing
/// more: the browsers in the background's kill-on-close jobs die with it either way.
/// </para>
/// <para>
/// <b>Its own thread, with its own message loop</b>, because a window's messages are
/// delivered to the thread that created it, and the background's other threads wait
/// on pipes, timers and the page. The callback runs on that thread and must return
/// quickly: Windows waits on the window procedure, and a sign-out gives it five
/// seconds in all.
/// </para>
/// <para>
/// <b>Never shown, never activated</b>: it is created without <c>WS_VISIBLE</c> and
/// no call here shows it, so it cannot take the focus or appear in Alt+Tab, and it
/// draws nothing.
/// </para>
/// <para>
/// ⚠️ <b>The one unsafe expression in this file is the window procedure's address</b>,
/// which the runtime requires to be taken in an unsafe context, as the task dialog's
/// callback was until 2026-10-03. Everything else passes through the marshaller.
/// </para>
/// </remarks>
internal sealed partial class SessionEndWindow : IDisposable
{
    private const uint WmClose = 0x0010;
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;

    /// <summary><c>WM_APP</c>: the one message this class posts to itself, to leave its loop.</summary>
    private const uint WmLeave = 0x8000;

    /// <summary><c>ERROR_CLASS_ALREADY_EXISTS</c>.</summary>
    private const int ErrorClassAlreadyExists = 1410;

    private const string ClassName = "BrowserAI.Background.SessionEnd";

    /// <summary>The one window of the process; the window procedure finds its owner through it.</summary>
    private static SessionEndWindow? _current;

    private readonly Action<SessionEndNotice> _notice;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _created = new(initialState: false);

    private nint _window;
    private int _disposed;
    private Exception? _failure;

    private SessionEndWindow(Action<SessionEndNotice> notice)
    {
        _notice = notice;
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "BrowserAI background session-end window",
        };
    }

    /// <summary>The window's handle, for the suite to post messages to.</summary>
    public nint Handle => Volatile.Read(ref _window);

    /// <summary>Creates the window on a thread of its own and waits until it exists.</summary>
    /// <param name="notice">What to do when Windows says something; runs on the window's thread, and must return quickly.</param>
    /// <returns>The window. Disposing it destroys the window and ends its thread.</returns>
    /// <exception cref="Win32Exception">The class or the window could not be created.</exception>
    /// <exception cref="InvalidOperationException">The process already has one.</exception>
    public static SessionEndWindow Create(Action<SessionEndNotice> notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var window = new SessionEndWindow(notice);

        if (Interlocked.CompareExchange(ref _current, window, null) is not null)
        {
            window._created.Dispose();
            throw new InvalidOperationException("This process already has its session-end window, and a second would hear every message twice.");
        }

        window._thread.Start();
        window._created.Wait();

        if (window._failure is { } failure)
        {
            _ = Interlocked.Exchange(ref _current, null);
            window._created.Dispose();
            throw failure;
        }

        return window;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        if (Handle is not 0)
        {
            _ = PostMessageW(Handle, WmLeave, 0, 0);
        }

        _ = _thread.Join(LeaveBound);
        _ = Interlocked.CompareExchange(ref _current, null, this);
        _created.Dispose();
    }

    /// <summary>
    /// How long disposal waits for the window's thread to leave its loop: a hang
    /// detector, since leaving is one posted message and one return.
    /// </summary>
    public static TimeSpan LeaveBound { get; } = ProcessBounds.SessionEndWindowLeaveBound;

    /// <summary>The window procedure's address, which only an unsafe context may take.</summary>
    /// <returns>The address.</returns>
    private static unsafe nint ProcedureAddress() =>
        (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WindowProcedure;

    private void Pump()
    {
        var className = Marshal.StringToHGlobalUni(ClassName);

        try
        {
            var module = GetModuleHandleW(null);

            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WindowProcedure = ProcedureAddress(),
                Instance = module,
                ClassName = className,
            };

            if (RegisterClassExW(ref windowClass) is 0)
            {
                var error = Marshal.GetLastPInvokeError();

                // A class left registered by an earlier window of this process is the
                // same class, and the window below can still be made of it.
                if (error is not ErrorClassAlreadyExists)
                {
                    throw new Win32Exception(error, "The background's session-end window class could not be registered.");
                }
            }

            // Top-level: no parent, and never HWND_MESSAGE, which receives no
            // broadcast. No WS_VISIBLE: the window is never shown.
            var window = CreateWindowExW(0, ClassName, ClassName, 0, 0, 0, 0, 0, 0, 0, module, 0);

            if (window is 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The background's session-end window could not be created.");
            }

            Volatile.Write(ref _window, window);
        }
#pragma warning disable CA1031 // The creating thread hands every failure to Create, which throws it on the caller's thread.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            _failure = failure;
            Marshal.FreeHGlobal(className);
            _created.Set();
            return;
        }

        _created.Set();

        try
        {
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.Message is WmLeave)
                {
                    break;
                }

                _ = DispatchMessageW(in message);
            }
        }
        finally
        {
            _ = DestroyWindow(Volatile.Read(ref _window));
            Marshal.FreeHGlobal(className);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmQueryEndSession:
                // Yes, end it: a process with no visible window cannot hold a
                // sign-out open, and asking to would only spend the five seconds.
                return 1;

            case WmEndSession when wParam is not 0:
                Tell(SessionEndNotice.SessionEnding);
                return 0;

            case WmClose:
                // The Task Scheduler's End. Not passed on: the window stays, so a
                // second close is heard too, and the process ends either way.
                Tell(SessionEndNotice.CloseRequested);
                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private static void Tell(SessionEndNotice notice)
    {
        try
        {
            Volatile.Read(ref _current)?._notice(notice);
        }
#pragma warning disable CA1031 // An exception may not cross into the window manager; the notice is best effort by nature.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary><c>WNDCLASSEXW</c>, checked against Microsoft's metadata by <c>InteropLayoutTests</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    /// <summary><c>MSG</c>, checked against Microsoft's metadata by <c>InteropLayoutTests</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WndClassEx windowClass);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetMessageW(out Msg message, nint window, uint filterMin, uint filterMax);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in Msg message);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}

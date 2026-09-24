// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;

namespace BrowserAI.TestProbe;

/// <summary>
/// A launcher that starts a program <b>suspended</b>, says which one, and exits,
/// so that whoever resumes it knows this launcher was gone first.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Added 2026-09-24, after a gate half went red on the ordering this
/// guarantees.</b> <c>OrphanedConsoleStart</c> used <c>cmd /c start /b</c> as the
/// launcher of a server that has to meet a parent which has <i>already exited</i>.
/// Nothing ordered cmd's exit before the server's look at it: the server read its
/// launcher alive, said <i>Watching the MCP client</i>, and the arm that asserts
/// the other sentence went red. The server was right, and the rig's premise was
/// not there yet.
/// </para>
/// <para>
/// <b>Suspended is what makes the order a fact.</b> The program cannot run a line
/// until its main thread is resumed, and the rig resumes it only after this
/// process has exited, so every look the program takes at its parent finds one
/// that is gone.
/// </para>
/// <para>
/// <b><c>CREATE_NO_WINDOW</c>, so the program gets a console of its own with no
/// window</b>: its standard input is a real console, which is the half of the
/// shape the server's decision also reads, and nothing reaches the screen.
/// </para>
/// </remarks>
internal static partial class LauncherProbe
{
    /// <summary><c>CREATE_SUSPENDED</c>: the main thread waits for a resume.</summary>
    private const uint CreateSuspended = 0x00000004;

    /// <summary><c>CREATE_NO_WINDOW</c>: a console with no window.</summary>
    private const uint CreateNoWindow = 0x08000000;

    /// <summary>
    /// Starts a program suspended and reports <c>&lt;pid&gt; &lt;thread id&gt;</c>.
    /// </summary>
    /// <param name="executable">The program's absolute path.</param>
    /// <param name="reportPath">Where the pid and the main thread's id go.</param>
    /// <returns>Zero when the program was created; one with the error in the report otherwise.</returns>
    public static int LaunchSuspended(string executable, string reportPath)
    {
        // CreateProcessW writes into the command line, so it is a buffer of its
        // own and never a string.
        Span<char> commandLine = [.. "\"" + executable + "\"", '\0'];
        var startup = new StartupInfo { Cb = Marshal.SizeOf<StartupInfo>() };

        if (!CreateProcessW(
                executable,
                commandLine,
                nint.Zero,
                nint.Zero,
                bInheritHandles: false,
                CreateSuspended | CreateNoWindow,
                nint.Zero,
                null,
                ref startup,
                out var information))
        {
            File.WriteAllText(
                reportPath,
                "error " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));

            return 1;
        }

        try
        {
            File.WriteAllText(
                reportPath,
                information.ProcessId.ToString(CultureInfo.InvariantCulture) + " "
                    + information.ThreadId.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _ = CloseHandle(information.Thread);
            _ = CloseHandle(information.Process);
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Length;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string? lpApplicationName,
        Span<char> lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}

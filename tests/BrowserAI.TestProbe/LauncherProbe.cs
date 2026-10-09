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

    /// <summary><c>STARTF_USESTDHANDLES</c>: the three standard handles are the struct's.</summary>
    private const uint StartFUseStdHandles = 0x00000100;

    /// <summary><c>STD_INPUT_HANDLE</c>.</summary>
    private const int StdInputHandle = -10;

    /// <summary><c>STD_OUTPUT_HANDLE</c>.</summary>
    private const int StdOutputHandle = -11;

    /// <summary><c>STD_ERROR_HANDLE</c>.</summary>
    private const int StdErrorHandle = -12;

    /// <summary>
    /// Starts a program suspended and reports <c>&lt;pid&gt; &lt;thread id&gt;</c>.
    /// </summary>
    /// <param name="executable">The program's absolute path.</param>
    /// <param name="reportPath">Where the pid and the main thread's id go.</param>
    /// <param name="arguments">
    /// What the program is started with after its own name: the one executable's
    /// mode since 2026-10-08. Each is one word with no space, tab or quote in it,
    /// and is refused otherwise, so nothing here has to quote one.
    /// </param>
    /// <returns>Zero when the program was created; one with the error in the report otherwise.</returns>
    public static int LaunchSuspended(string executable, string reportPath, string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (Array.Exists(arguments, argument => argument.Length is 0 || argument.AsSpan().IndexOfAny(" \t\"") >= 0))
        {
            File.WriteAllText(reportPath, "error an argument is empty or carries a space, a tab or a quote");
            return 1;
        }

        // CreateProcessW writes into the command line, so it is a buffer of its
        // own and never a string.
        var spelled = "\"" + executable + "\"" + string.Concat(arguments.Select(argument => " " + argument));
        Span<char> commandLine = [.. spelled, '\0'];
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

        return Report(reportPath, information);
    }

    /// <summary>
    /// Starts a program suspended with this launcher's own three standard handles as
    /// its own, reports <c>&lt;pid&gt; &lt;thread id&gt;</c>, and exits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-09 for the relay, whose start with no pipe on standard input
    /// ends before it looks at its launcher</b> (Startup[14]). A launcher that has
    /// exited and still opens is a question the relay asks of a client that gave it a
    /// pipe, so this launcher gives it one: the caller starts this probe with pipes on
    /// all three standard handles, and they become the program's, so the caller goes
    /// on holding the write end of the program's standard input after this launcher has
    /// gone.
    /// </para>
    /// <para>
    /// <b>Inheritance hands on these three and nothing else</b>, because the caller
    /// starts this probe through <c>JobLauncher</c>, whose handle list gives it exactly
    /// three inheritable handles. The program also lands in this probe's job, which
    /// is the caller's.
    /// </para>
    /// </remarks>
    /// <param name="executable">The program's absolute path.</param>
    /// <param name="reportPath">Where the pid and the main thread's id go.</param>
    /// <param name="arguments">What the program is started with after its own name, as <see cref="LaunchSuspended"/> takes them.</param>
    /// <returns>Zero when the program was created; one with the error in the report otherwise.</returns>
    public static int LaunchSuspendedHandingOn(string executable, string reportPath, string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (Array.Exists(arguments, argument => argument.Length is 0 || argument.AsSpan().IndexOfAny(" \t\"") >= 0))
        {
            File.WriteAllText(reportPath, "error an argument is empty or carries a space, a tab or a quote");
            return 1;
        }

        var spelled = "\"" + executable + "\"" + string.Concat(arguments.Select(argument => " " + argument));
        Span<char> commandLine = [.. spelled, '\0'];

        var handedOn = default(StartupInfo);
        handedOn.Cb = Marshal.SizeOf<StartupInfo>();
        handedOn.Flags = StartFUseStdHandles;
        handedOn.StdInput = GetStdHandle(StdInputHandle);
        handedOn.StdOutput = GetStdHandle(StdOutputHandle);
        handedOn.StdError = GetStdHandle(StdErrorHandle);

        if (!CreateProcessW(
                executable,
                commandLine,
                nint.Zero,
                nint.Zero,
                bInheritHandles: true,
                CreateSuspended | CreateNoWindow,
                nint.Zero,
                null,
                ref handedOn,
                out var information))
        {
            File.WriteAllText(
                reportPath,
                "error " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));

            return 1;
        }

        return Report(reportPath, information);
    }

    /// <summary>Writes <c>&lt;pid&gt; &lt;thread id&gt;</c> and closes both handles.</summary>
    /// <param name="reportPath">Where it goes.</param>
    /// <param name="information">What <c>CreateProcessW</c> answered.</param>
    /// <returns>Zero.</returns>
    private static int Report(string reportPath, ProcessInformation information)
    {
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

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);
}

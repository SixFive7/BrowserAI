// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using BrowserAI.Interop;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What says that a process is one the Task Scheduler started: the pid of the service
/// that runs the scheduler, and whether a process sits in a job.
/// </summary>
/// <remarks>
/// <para>
/// <b>The service is found through the service control manager, by its service name,
/// and never through the process table.</b> The scheduler runs inside a shared
/// <c>svchost.exe</c>, so an image path says nothing about which service a host carries,
/// and a match on an image name is the shape this repository refuses in every form.
/// <c>QueryServiceStatusEx</c> answers the pid of the process hosting the service to an
/// ordinary token: <c>SC_MANAGER_CONNECT</c> and <c>SERVICE_QUERY_STATUS</c> are granted
/// to every signed-in user (<c>sc.exe queryex Schedule</c> answered the same pid without
/// elevation on this machine, 2026-10-09).
/// </para>
/// <para>
/// <b>Measured 2026-10-04 with the real programs and 2026-10-08 with stand-ins</b>, in
/// <c>kb/windows/processes.md</c>: a process the scheduler starts has the
/// <c>svchost.exe</c> hosting <c>Schedule</c> as its parent and sits in a job the
/// scheduler shares across the session. The real-scheduler arms of
/// <c>RealInstallerTests</c> read both off the installed background.
/// </para>
/// </remarks>
internal static partial class ScheduledProcess
{
    /// <summary>The Task Scheduler's service name.</summary>
    public const string ServiceName = "Schedule";

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int StatusProcessInfo = 0;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>The pid of the process that hosts the Task Scheduler's service.</summary>
    /// <returns>The pid.</returns>
    /// <exception cref="Win32Exception">The service control manager refused a step, named in the message.</exception>
    public static int ServiceProcessId()
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);

        if (manager == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The service control manager could not be opened to read the Task Scheduler's pid.");
        }

        try
        {
            var service = OpenServiceW(manager, ServiceName, ServiceQueryStatus);

            if (service == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The service '{ServiceName}' could not be opened to read its pid.");
            }

            try
            {
                return QueryServiceStatusEx(service, StatusProcessInfo, out var status, (uint)Marshal.SizeOf<ServiceStatusProcess>(), out _)
                    ? (int)status.ProcessId
                    : throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The status of '{ServiceName}' could not be read.");
            }
            finally
            {
                _ = CloseServiceHandle(service);
            }
        }
        finally
        {
            _ = CloseServiceHandle(manager);
        }
    }

    /// <summary>Whether a live process belongs to any job at all.</summary>
    /// <param name="processId">The pid. It must not exit during the call.</param>
    /// <returns>Whether it is in a job.</returns>
    /// <exception cref="Win32Exception">The process could not be opened, or the question could not be asked.</exception>
    public static bool IsInAnyJob(int processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);

        return process.IsInvalid
            ? throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open process {processId} to ask whether it is in a job.")
            : JobObject.IsInAnyJob(process);
    }

    /// <summary><c>SERVICE_STATUS_PROCESS</c>: nine 32-bit fields, the eighth of which is the pid.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManagerW(string? machine, string? database, uint desiredAccess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenServiceW(nint manager, string serviceName, uint desiredAccess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(nint service, int infoLevel, out ServiceStatusProcess buffer, uint bufferSize, out uint bytesNeeded);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);
}

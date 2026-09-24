// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Which process hosts a Windows service, asked of the service control manager by
/// the service's own name.
/// </summary>
/// <remarks>
/// <b>Why it is asked this way.</b> The task scheduler's service runs in a
/// <c>svchost.exe</c> this user cannot open -- <c>OpenProcess</c> answered error 5
/// for it, measured 2026-09-25 -- so a process's parent cannot be recognised by
/// reading the parent's image. The service control manager says which pid hosts a
/// service, by <c>QueryServiceStatusEx</c>, and a user may ask it that much. Read
/// only: the manager and the service are opened for connect and status query.
/// </remarks>
internal static partial class ServiceHost
{
    private const uint ManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int StatusProcessInfo = 0;

    /// <summary>The size of <c>SERVICE_STATUS_PROCESS</c>: nine four-byte fields.</summary>
    private const int StatusProcessBytes = 36;

    /// <summary>Where <c>dwProcessId</c> sits in it: the eighth field.</summary>
    private const int ProcessIdOffset = 28;

    /// <summary>The pid of the process hosting a service, or zero when it is not running.</summary>
    /// <param name="service">The service's name, <c>Schedule</c> for the task scheduler.</param>
    /// <returns>The pid.</returns>
    /// <exception cref="Win32Exception">The manager or the service could not be asked.</exception>
    public static int ProcessIdOf(string service)
    {
        var manager = OpenSCManagerW(null, null, ManagerConnect);

        if (manager == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The service control manager could not be opened.");
        }

        try
        {
            var handle = OpenServiceW(manager, service, ServiceQueryStatus);

            if (handle == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The service '{service}' could not be opened.");
            }

            try
            {
                var status = new byte[StatusProcessBytes];

                return QueryServiceStatusEx(handle, StatusProcessInfo, status, (uint)status.Length, out _)
                    ? BinaryPrimitives.ReadInt32LittleEndian(status.AsSpan(ProcessIdOffset))
                    : throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The service '{service}' did not say which process hosts it.");
            }
            finally
            {
                _ = CloseServiceHandle(handle);
            }
        }
        finally
        {
            _ = CloseServiceHandle(manager);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenServiceW(nint manager, string serviceName, uint desiredAccess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(nint service, int infoLevel, [Out] byte[] buffer, uint bufferSize, out uint bytesNeeded);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}

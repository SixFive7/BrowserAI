// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Reads another process's environment block out of its memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-11-17 for the maintainer's 197 b</b>, whose arm reads what a started Firefox
/// really carries, whoever set it: BrowserAI, Playwright or nobody. Windows offers no call that
/// returns another process's environment, so this walks the documented layout the way a
/// debugger does: <c>NtQueryInformationProcess</c> for the process's PEB, its
/// <c>ProcessParameters</c> pointer at offset 0x20, and in those parameters the
/// <c>Environment</c> pointer at 0x80 and <c>EnvironmentSize</c> at 0x3F0, the x64 offsets,
/// which is the only build this suite runs as. The same reading, written in PowerShell, took the
/// measurement of that day, in the evidence batch <c>2026-11-17-adopted-playwright</c>.
/// </para>
/// <para>
/// It reads, and nothing else: the handle asks for query and read access only.
/// </para>
/// </remarks>
internal static partial class ProcessEnvironment
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const int ProcessBasicInformationClass = 0;
    private const int ProcessParametersOffset = 0x20;
    private const int EnvironmentOffset = 0x80;
    private const int EnvironmentSizeOffset = 0x3F0;

    /// <summary>Every <c>NAME=value</c> entry of one process's environment, in its order.</summary>
    /// <param name="processId">The process to read.</param>
    /// <returns>The entries.</returns>
    /// <exception cref="Win32Exception">The process could not be opened or read.</exception>
    public static IReadOnlyList<string> Of(int processId)
    {
        using var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"OpenProcess failed for pid {processId}.");
        }

        // PROCESS_BASIC_INFORMATION is six pointer-sized fields, and the PEB's address is
        // the second.
        var information = new nint[6];
        var status = NtQueryInformationProcess(handle, ProcessBasicInformationClass, information, information.Length * nint.Size, out _);

        if (status < 0)
        {
            throw new Win32Exception($"NtQueryInformationProcess(ProcessBasicInformation) failed for pid {processId} with 0x{status:X8}.");
        }

        var parameters = ReadPointer(handle, information[1] + ProcessParametersOffset, processId);
        var environment = ReadPointer(handle, parameters + EnvironmentOffset, processId);
        var size = (long)ReadPointer(handle, parameters + EnvironmentSizeOffset, processId);

        var block = new byte[size];

        if (!ReadProcessMemory(handle, environment, block, (nint)size, out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"ReadProcessMemory failed for the environment of pid {processId}.");
        }

        return Encoding.Unicode.GetString(block).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static nint ReadPointer(SafeProcessHandle handle, nint address, int processId)
    {
        var buffer = new byte[8];

        if (!ReadProcessMemory(handle, address, buffer, 8, out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"ReadProcessMemory failed at 0x{address:X} in pid {processId}.");
        }

        return (nint)BitConverter.ToInt64(buffer, 0);
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        SafeProcessHandle processHandle,
        int processInformationClass,
        [Out] nint[] processInformation,
        int processInformationLength,
        out int returnLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(
        SafeProcessHandle hProcess,
        nint lpBaseAddress,
        [Out] byte[] lpBuffer,
        nint nSize,
        out nint lpNumberOfBytesRead);
}

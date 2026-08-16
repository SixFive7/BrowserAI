// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What a live process was actually started with: its image path and its
/// command line, read from outside by pid.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because a config key is not evidence.</b> Upstream's
/// <c>chromiumSandbox</c> parses, validates and is then discarded, so a test
/// asserting on the config BrowserAI generated asserts on nothing. The only
/// thing that says whether the sandbox is on is the browser's own command line,
/// and the only place that exists is the running process.
/// </para>
/// <para>
/// <b>It reads by pid and reports a path; it never matches a name.</b> The image
/// path is what lets a caller say "this pid is the Chromium <i>we</i>
/// provisioned" by comparing against a path BrowserAI owns — the sanctioned
/// alternative to an image-name match, and the reason the never-by-image-name
/// rule costs nothing here.
/// </para>
/// <para>
/// <c>ProcessCommandLineInformation</c> rather than a PEB walk:
/// <c>NtQueryInformationProcess</c> has answered class 60 with a
/// <c>UNICODE_STRING</c> since Windows 8.1, it needs only
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, and it involves no
/// <c>ReadProcessMemory</c> and no 32/64-bit pointer arithmetic. WMI would also
/// work and costs milliseconds per call against microseconds here.
/// </para>
/// </remarks>
internal static partial class ProcessCommandLine
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    /// <summary>The full image path of a live process, or <see langword="null"/> if it cannot be read.</summary>
    /// <param name="processId">The pid to ask about.</param>
    /// <returns>The image path, or <see langword="null"/> when the process is gone or inaccessible.</returns>
    public static string? ImagePathOf(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[32768];
        var size = buffer.Length;

        return QueryFullProcessImageNameW(handle, 0, ref buffer[0], ref size)
            ? new string(buffer, 0, size)
            : null;
    }

    /// <summary>The command line a live process was started with.</summary>
    /// <param name="processId">The pid to ask about.</param>
    /// <returns>The command line, or <see langword="null"/> when the process is gone or inaccessible.</returns>
    /// <exception cref="Win32Exception">The query failed for a reason other than a short buffer.</exception>
    public static string? Of(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            return null;
        }

        // The documented two-call shape. The first call reports the size it
        // needs, which for a Chromium browser process is well over 4 KB of
        // switches, so a fixed guess would truncate exactly the arguments this
        // class exists to read.
        var length = 0;
        var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, nint.Zero, 0, ref length);

        if (status is not (StatusInfoLengthMismatch or StatusBufferTooSmall) || length <= 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(length);

        try
        {
            status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, ref length);

            if (status < 0)
            {
                throw new Win32Exception(
                    $"NtQueryInformationProcess(ProcessCommandLineInformation) failed for pid {processId} with 0x{status:X8}.");
            }

            // UNICODE_STRING: USHORT Length, USHORT MaximumLength, four bytes of
            // padding on x64, then PWSTR Buffer. Length is in BYTES.
            var bytes = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, 8);

            return bytes is 0 || text == nint.Zero ? string.Empty : Marshal.PtrToStringUni(text, bytes / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(
        SafeProcessHandle hProcess,
        uint dwFlags,
        ref char lpExeName,
        ref int lpdwSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        SafeProcessHandle processHandle,
        int processInformationClass,
        nint processInformation,
        int processInformationLength,
        ref int returnLength);
}

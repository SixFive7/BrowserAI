// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Who actually started a process, read from the kernel rather than inferred.
/// </summary>
/// <remarks>
/// <para>
/// This is the assertion build-order step 5 turns on: a child spawned through
/// BrowserAI's transport must have <b>BrowserAI as its direct parent</b>. The
/// failure it is aimed at is silent by nature — the SDK's own stdio transport
/// rewrites every Windows command into <c>cmd.exe /c …</c>, and the resulting
/// shell is invisible to everything except a parent-pid query. A test that
/// merely checked "the child started and answered" passes with the shell in
/// place.
/// </para>
/// <para>
/// <c>NtQueryInformationProcess</c> rather than WMI or a toolhelp walk:
/// ~0.77 µs per call against ~3.3 ms for <c>Process.GetProcessById</c> and
/// milliseconds for WMI, and it is what <c>dotnet/runtime</c> itself uses. It
/// is undocumented-but-permanent in the sense that matters here — the field
/// this reads has been at the same offset since Windows NT, and the call is
/// made against a process the caller already owns.
/// </para>
/// <para>
/// Test-only surface, so it lives in the harness. The product needs a job
/// object rather than a parent pid, and that arrives at step 6.
/// </para>
/// </remarks>
internal static partial class ParentProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessBasicInformationClass = 0;

    /// <summary>The process id that started <paramref name="processId"/>.</summary>
    /// <param name="processId">A live process. It must not exit during the call.</param>
    /// <returns>The parent's process id.</returns>
    /// <exception cref="Win32Exception">The process could not be opened.</exception>
    /// <exception cref="InvalidOperationException">The query failed.</exception>
    public static int IdOf(int processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);

        if (process.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"Could not open process {processId.ToString(System.Globalization.CultureInfo.InvariantCulture)} to read its parent.");
        }

        var status = NtQueryInformationProcess(
            process,
            ProcessBasicInformationClass,
            out var information,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);

        // NTSTATUS: anything below zero is a failure, and there is no
        // GetLastError to consult. Reported rather than defaulted, because a
        // parent-pid of zero would read as "no shell" and pass the very test
        // this exists to fail.
        return status < 0
            ? throw new InvalidOperationException(
                $"NtQueryInformationProcess failed with NTSTATUS 0x{status:X8} for process {processId}.")
            : (int)information.InheritedFromUniqueProcessId;
    }

    /// <summary>
    /// The image name of the process that started <paramref name="processId"/>,
    /// or <see langword="null"/> if that process has already exited.
    /// </summary>
    /// <param name="processId">A live process.</param>
    /// <returns>The parent's process name, without an extension.</returns>
    public static string? ParentImageNameOf(int processId)
    {
        try
        {
            using var parent = Process.GetProcessById(IdOf(processId));
            return parent.ProcessName;
        }
        catch (ArgumentException)
        {
            // The parent has exited and its id is no longer resolvable, which
            // is itself an answer: nothing is holding the child open.
            return null;
        }
    }

    // The documented x64 layout, spelled with the real C types rather than a
    // row of nints: ExitStatus and BasePriority are 32-bit and the compiler
    // supplies the same padding the header does, so this is 48 bytes on x64 and
    // 24 on x86 -- both correct.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public int ExitStatus;
        public nint PebBaseAddress;
        public nuint AffinityMask;
        public int BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
    }

    // System32 only, on every P/Invoke in this repository (CA5392). Without it
    // the loader searches the application directory first and a DLL dropped
    // beside the test host would be loaded in preference to the real one.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        SafeProcessHandle processHandle,
        int processInformationClass,
        out ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);
}

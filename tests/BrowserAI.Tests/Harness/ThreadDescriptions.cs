// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The names a process has given its threads, read from Windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-13 for the maintainer's 129 a</b>, whose arm asks whether a
/// session's browser started Chromium's own stack sampling profiler. Chromium names
/// each thread it starts with <c>SetThreadDescription</c>, and the profiler's is
/// <c>StackSamplingProfiler</c>; the browser's main thread is <c>CrBrowserMain</c>,
/// which is how an arm proves the reading saw the right process.
/// </para>
/// <para>
/// <b>Read for one process, named by its pid</b>, which the caller holds open so the
/// number cannot pass to another process during the read. A toolhelp snapshot of
/// threads lists every thread of the machine with the pid that owns it, and this keeps
/// the ones of that pid; no process is selected by its image name. A thread that ends
/// between the snapshot and its open is left out, as is one that has no description.
/// </para>
/// <para>
/// Test-only, so it lives in the harness: the product never asks what a browser's
/// threads are called.
/// </para>
/// </remarks>
internal static partial class ThreadDescriptions
{
    private const uint SnapThread = 0x00000004;
    private const uint ThreadQueryLimitedInformation = 0x0800;

    /// <summary>The description of every thread of the process that has one.</summary>
    /// <param name="processId">A process the caller holds open.</param>
    /// <returns>The descriptions, one per thread, in the order the snapshot lists them.</returns>
    /// <exception cref="Win32Exception">The snapshot could not be taken.</exception>
    public static IReadOnlyList<string> Of(int processId)
    {
        using var snapshot = CreateToolhelp32Snapshot(SnapThread, 0);

        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateToolhelp32Snapshot refused a snapshot of the machine's threads.");
        }

        var names = new List<string>();
        var entry = new ThreadEntry32 { Size = (uint)Marshal.SizeOf<ThreadEntry32>() };

        for (var more = Thread32First(snapshot, ref entry); more; more = Thread32Next(snapshot, ref entry))
        {
            if (entry.OwnerProcessId != (uint)processId)
            {
                continue;
            }

            using var thread = OpenThread(ThreadQueryLimitedInformation, inheritHandle: false, entry.ThreadId);

            if (thread.IsInvalid)
            {
                continue;
            }

            if (GetThreadDescription(thread, out var description) < 0 || description == nint.Zero)
            {
                continue;
            }

            try
            {
                if (Marshal.PtrToStringUni(description) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
            finally
            {
                _ = LocalFree(description);
            }
        }

        return names;
    }

    /// <summary>One row of a toolhelp snapshot of threads, as <c>THREADENTRY32</c> lays it out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ThreadId;
        public uint OwnerProcessId;
        public int BasePriority;
        public int DeltaPriority;
        public uint Flags;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Thread32First(SafeFileHandle hSnapshot, ref ThreadEntry32 lpte);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Thread32Next(SafeFileHandle hSnapshot, ref ThreadEntry32 lpte);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle OpenThread(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint dwThreadId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial int GetThreadDescription(SafeFileHandle hThread, out nint ppszThreadDescription);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint hMem);
}

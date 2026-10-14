// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using BrowserAI.Interop;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Every process of an arm's own job that runs one browser executable BrowserAI
/// provisioned, held suspended until this is disposed: a browser that does not run, for
/// an arm that needs a call to meet one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-14 with the maintainer's 171</b>, for the Firefox front-door arm's
/// screenshot, which upstream answers with its own action timeout when the browser does
/// not answer in 5,000 ms. The investigation of that day provoked the timeout this way,
/// 2 of 2, with the same text a gate's red read later
/// (<see href="../../../kb/playwright/tools-and-artifacts.md#a-firefox-screenshot-has-upstreams-five-seconds-and-a-loaded-machine-can-spend-most-of-them----measured-2026-10-14">kb</see>).
/// A fault injected into one browser of the arm's own, which loads nothing.
/// </para>
/// <para>
/// <b>Chosen by the job and the full image path, never by a name</b>: a process is held
/// only when it is in the job the arm's client owns and runs exactly the executable the
/// arm names, and its membership is read again once its handle is open, so a number
/// Windows gave to another process meanwhile is let go unheld.
/// </para>
/// </remarks>
internal sealed partial class SuspendedBrowser : IDisposable
{
    private const uint ProcessSuspendResume = 0x0800;

    private readonly List<SafeProcessHandle> _held;

    private SuspendedBrowser(List<SafeProcessHandle> held) => _held = held;

    /// <summary>How many processes are held.</summary>
    public int Count => _held.Count;

    /// <summary>Holds every process of the job that runs <paramref name="executable"/>.</summary>
    /// <param name="jobProcessIds">Reads the processes of the arm's job, now.</param>
    /// <param name="executable">The browser's full image path, under the browsers root BrowserAI provisioned.</param>
    /// <returns>The hold, which lets every process go when it is disposed.</returns>
    public static SuspendedBrowser Of(Func<IReadOnlyList<int>> jobProcessIds, string executable)
    {
        ArgumentNullException.ThrowIfNull(jobProcessIds);
        ArgumentNullException.ThrowIfNull(executable);

        var inTheJob = jobProcessIds().ToHashSet();
        var browser = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Where(process => inTheJob.Contains(process.ProcessId)
                && string.Equals(process.ImagePath, executable, StringComparison.OrdinalIgnoreCase))
            .Select(process => process.ProcessId)
            .ToList();

        var held = new List<SafeProcessHandle>();

        foreach (var processId in browser)
        {
            var handle = OpenProcess(ProcessSuspendResume, inheritHandle: false, (uint)processId);

            // Still in the job once the handle is held, so the number was not reused.
            if (handle.IsInvalid || !jobProcessIds().Contains(processId))
            {
                handle.Dispose();
                continue;
            }

            _ = NtSuspendProcess(handle);
            held.Add(handle);
        }

        return new SuspendedBrowser(held);
    }

    /// <summary>Lets every held process run again, once.</summary>
    public void Dispose()
    {
        foreach (var handle in _held)
        {
            _ = NtResumeProcess(handle);
            handle.Dispose();
        }

        _held.Clear();
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtSuspendProcess(SafeProcessHandle processHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtResumeProcess(SafeProcessHandle processHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Which live processes are running an executable BrowserAI owns — answered by
/// <b>full image path</b>, never by image name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Image path is the documented detection route; image name is the one this
/// project bans.</b> <c>chrome.exe</c> names the user's own Chrome, every other
/// Chromium on the machine, and ours. <c>&lt;browsers-root&gt;\chromium-1237\
/// chrome-win64\chrome.exe</c> names exactly one tree, the one BrowserAI
/// provisioned, and neither a prefix match against the browsers root nor an
/// exact match against one binary can reach a browser that came from anywhere
/// else. That is the whole difference between a question this product may ask
/// and one it may not.
/// </para>
/// <para>
/// <b>The path is <c>EnumProcesses</c> → <c>OpenProcess</c> →
/// <c>QueryFullProcessImageNameW</c>, and every API on it is documented and
/// supported.</b> <c>EnumProcesses</c> itself costs ~0.06 ms; the whole cost is
/// the per-process open, and the ~150 processes this token cannot open are
/// protected and SYSTEM-owned ones — nothing BrowserAI launched can be in that
/// set, because it runs as the user and non-elevated
/// ([kb](../../../kb/windows/detection.md#process-image-path--the-fully-documented-detection-path)).
/// </para>
/// <para>
/// <b>Terminating is <see cref="StrayCandidate"/>'s, and only after a second
/// guard agrees.</b> Nothing on the <see cref="RunningFrom"/> path can
/// terminate anything: its one caller is
/// <c>browserai_reinstall_browser</c>, which refuses rather than coordinates.
/// The sweep's scan does hold a handle with <c>PROCESS_TERMINATE</c>, and a
/// candidate is still only killed when its attributed directory holds a
/// <c>lock.json</c> whose lock the sweeper can take itself.
/// </para>
/// <para>
/// <b>Every row carries a creation time.</b> A pid alone is meaningless the
/// moment the process exits, and Windows reuses pids — so the pair is the
/// identity, exactly as it is for <see cref="ProcessLiveness"/>. A process that
/// exits between the snapshot and the image-path read is dropped rather than
/// reported with a name that may already belong to a stranger.
/// </para>
/// </remarks>
internal static partial class BrowserProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint ProcessTerminate = 0x00000001;

    /// <summary>Every live process whose executable sits under <paramref name="root"/>.</summary>
    /// <param name="root">
    /// An absolute directory. Matching is a case-insensitive prefix match on the
    /// process's full image path, with a separator appended so that a root of
    /// <c>…\browsers</c> cannot match <c>…\browsers-backup</c>.
    /// </param>
    /// <returns>
    /// The matching processes, which may be empty. Processes this token cannot
    /// open are absent: a process BrowserAI cannot see is not one it can claim
    /// anything about.
    /// </returns>
    /// <exception cref="Win32Exception">The process list could not be read at all.</exception>
    public static IReadOnlyList<RunningImage> RunningFrom(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var found = new List<RunningImage>();

        foreach (var processId in ProcessIds())
        {
            using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

            if (handle.IsInvalid)
            {
                continue;
            }

            var path = ImagePathOf(handle);

            if (path is null
                || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !GetProcessTimes(handle, out var created, out _, out _, out _))
            {
                continue;
            }

            found.Add(new RunningImage(processId, created, path));
        }

        return found;
    }

    /// <summary>
    /// Every live process running <b>exactly</b> one of the executables
    /// BrowserAI provisioned, each with an open handle held from this moment.
    /// </summary>
    /// <remarks>
    /// <b>The handle is what closes race R2.</b> Windows will not recycle a pid
    /// while a handle to that process is open, so a pid captured here still
    /// names the same process when the sweep gets round to acting on it. The
    /// creation time is re-verified regardless, immediately before terminating,
    /// because belt-and-braces is the only acceptable posture for a call that
    /// cannot be undone.
    /// </remarks>
    /// <param name="images">The absolute executable paths that count as ours.</param>
    /// <returns>The scan. <b>The caller owns it and must dispose it.</b></returns>
    /// <exception cref="Win32Exception">The process list could not be read at all.</exception>
    public static StrayScan ScanFor(IReadOnlyCollection<string> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        var wanted = new HashSet<string>(images, StringComparer.OrdinalIgnoreCase);
        var candidates = new List<StrayCandidate>();
        var enumerated = 0;
        var opened = 0;

        try
        {
            foreach (var processId in ProcessIds())
            {
                enumerated++;

                // PROCESS_TERMINATE is asked for here rather than later on
                // purpose: a handle acquired after the decision would be a
                // second chance for the pid to have become somebody else.
                var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessTerminate, bInheritHandle: false, (uint)processId);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    continue;
                }

                opened++;
                var path = ImagePathOf(handle);

                if (path is null || !wanted.Contains(path) || !GetProcessTimes(handle, out var created, out _, out _, out _))
                {
                    handle.Dispose();
                    continue;
                }

                candidates.Add(new StrayCandidate(processId, created, path, handle));
            }
        }
        catch
        {
            foreach (var candidate in candidates)
            {
                candidate.Dispose();
            }

            throw;
        }

        return new StrayScan(candidates, enumerated, opened);
    }

    /// <summary>Every pid on the machine, from <c>EnumProcesses</c>.</summary>
    /// <remarks>
    /// <b><c>K32EnumProcesses</c> is <c>EnumProcesses</c>.</b> The name in
    /// <c>psapi.h</c> maps to this kernel32 export under
    /// <c>PSAPI_VERSION = 2</c>; taking it directly avoids loading the
    /// <c>psapi.dll</c> forwarder and changes nothing else.
    /// <b><c>NtQuerySystemInformation</c> is deliberately not used</b> even
    /// though it would return every image name in one call: at this cost there
    /// is nothing to buy, and it would put an image-<i>name</i> comparison
    /// inside the detection path — which is the pattern that erodes into the
    /// rule this file exists to keep.
    /// </remarks>
    private static int[] ProcessIds()
    {
        var buffer = new uint[1024];

        while (true)
        {
            if (!K32EnumProcesses(buffer, (uint)(buffer.Length * sizeof(uint)), out var bytesReturned))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not read the machine's process list, so BrowserAI cannot tell whether a browser of its own is running.");
            }

            var returned = (int)(bytesReturned / sizeof(uint));

            // A full buffer means the list may have been truncated, and a
            // truncated list under-reports silently -- which is the one failure
            // shape this project exists to eliminate. Grow and ask again.
            if (returned == buffer.Length)
            {
                buffer = new uint[buffer.Length * 2];
                continue;
            }

            // Pid 0 is the idle process. It is not openable and never ours.
            return [.. buffer.Take(returned).Where(id => id is not 0).Select(id => (int)id)];
        }
    }

    private static unsafe string? ImagePathOf(SafeProcessHandle handle)
    {
        // Sized for the extended limit rather than MAX_PATH: the app manifest is
        // longPathAware and the browsers root is the caller's LocalAppData,
        // which can be arbitrarily deep.
        var buffer = new char[32768];
        var length = (uint)buffer.Length;

        fixed (char* start = buffer)
        {
            return QueryFullProcessImageNameW(handle, 0, start, ref length)
                ? new string(buffer, 0, (int)length)
                : null;
        }
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32EnumProcesses([Out] uint[] lpidProcess, uint cb, out uint lpcbNeeded);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(
        SafeProcessHandle hProcess,
        uint dwFlags,
        char* lpExeName,
        ref uint lpdwSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);
}

/// <summary>One live process running an image BrowserAI owns.</summary>
/// <param name="ProcessId">Its pid, meaningful only together with <paramref name="CreatedFileTime"/>.</param>
/// <param name="CreatedFileTime">Its creation time, which together with the pid is its identity.</param>
/// <param name="ImagePath">The full path of the executable it is running.</param>
internal sealed record RunningImage(int ProcessId, long CreatedFileTime, string ImagePath);

/// <summary>
/// One pass of the machine's process list, and the candidates it found.
/// </summary>
/// <param name="candidates">Every process running one of our binaries, each holding an open handle.</param>
/// <param name="enumerated">How many pids <c>EnumProcesses</c> returned.</param>
/// <param name="opened">How many of them this token could open. The rest are protected or SYSTEM-owned.</param>
internal sealed class StrayScan(IReadOnlyList<StrayCandidate> candidates, int enumerated, int opened) : IDisposable
{
    /// <summary>Every process running one of our binaries.</summary>
    public IReadOnlyList<StrayCandidate> Candidates { get; } = candidates;

    /// <summary>How many pids the machine reported.</summary>
    public int Enumerated { get; } = enumerated;

    /// <summary>How many of them could be opened at all.</summary>
    public int Opened { get; } = opened;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var candidate in Candidates)
        {
            candidate.Dispose();
        }
    }
}

/// <summary>
/// One process running a binary BrowserAI provisioned, with the handle that
/// pins its pid held from the moment it was found.
/// </summary>
/// <remarks>
/// A candidate is <b>not</b> a stray. It becomes one only when a second,
/// independent guard agrees: its attributed directory holds a
/// <c>lock.json</c> whose lock the sweeper can take itself. Everything here is
/// the first guard and the mechanics of acting on the second.
/// </remarks>
internal sealed partial class StrayCandidate : IDisposable
{
    private readonly SafeProcessHandle _handle;

    internal StrayCandidate(int processId, long createdFileTime, string imagePath, SafeProcessHandle handle)
    {
        ProcessId = processId;
        CreatedFileTime = createdFileTime;
        ImagePath = imagePath;
        _handle = handle;
    }

    /// <summary>Its pid, pinned by the open handle for as long as this object lives.</summary>
    public int ProcessId { get; }

    /// <summary>Its creation time, captured at detection. With the pid, this is its identity.</summary>
    public long CreatedFileTime { get; }

    /// <summary>The full path of the executable it is running.</summary>
    public string ImagePath { get; }

    /// <summary>
    /// Whether the process this handle names is still the one that was found.
    /// </summary>
    /// <returns><see langword="false"/> if the creation time no longer matches.</returns>
    public bool IsStillTheProcessThatWasFound() =>
        GetProcessTimes(_handle, out var created, out _, out _, out _) && created == CreatedFileTime;

    /// <summary>Terminates it, after re-checking that it is still itself.</summary>
    /// <param name="refusal">Why nothing was terminated, when nothing was.</param>
    /// <returns>Whether the process was terminated.</returns>
    /// <remarks>
    /// <b>The re-check is not redundant with the held handle.</b> The handle is
    /// what makes the pid safe; this is what makes a <i>stale candidate object</i>
    /// safe — one built before a wait, carried across a decision, or handed in by
    /// a caller who read the identity from somewhere else. It costs one syscall
    /// and it is the last thing between this product and terminating a stranger.
    /// </remarks>
    public bool TryTerminate(out string? refusal)
    {
        if (!IsStillTheProcessThatWasFound())
        {
            refusal = $"PID {ProcessId} is no longer the process that was found — its creation time has changed, so the pid now names something else. Nothing was terminated.";
            return false;
        }

        if (!TerminateProcess(_handle, 1))
        {
            refusal = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return false;
        }

        refusal = null;
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);
}

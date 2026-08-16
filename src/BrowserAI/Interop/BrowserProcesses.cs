// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Which live processes are running an executable that lives under a directory
/// BrowserAI owns — answered by <b>full image path</b>, never by image name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Image path is the documented detection route; image name is the one this
/// project bans.</b> <c>chrome.exe</c> names the user's own Chrome, every other
/// Chromium on the machine, and ours. <c>&lt;browsers-root&gt;\chromium-1237\
/// chrome-win64\chrome.exe</c> names exactly one tree, the one BrowserAI
/// provisioned, and a prefix match against the browsers root cannot reach a
/// browser that came from anywhere else. That is the whole difference between a
/// question this product may ask and one it may not.
/// </para>
/// <para>
/// <b>Nothing here terminates anything, and there is no code path that could.</b>
/// The one caller is <c>browserai_reinstall_browser</c>, which
/// [refuses rather than coordinates](../../plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser):
/// a browser still running out of the tree it is about to delete is a reason to
/// stop and say so, never a reason to kill somebody else's session. Deleting a
/// directory whose executables are open is refused by Windows anyway, so the
/// alternative to reporting is not "do it forcibly" — it is "fail obscurely".
/// </para>
/// <para>
/// <b>This is not [the stray sweep](../../plan/build-order.md#16-the-stray-sweep).</b>
/// It answers one narrow question about one directory, with no attribution, no
/// window enumeration, no machine-wide sweep lock and no reporting channel of
/// its own. The sweep is built on top of a question like this one; it is not
/// this one.
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
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x00001000;

    /// <summary>
    /// Every live process whose executable sits under <paramref name="root"/>.
    /// </summary>
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
    /// <exception cref="Win32Exception">The process snapshot could not be taken at all.</exception>
    public static IReadOnlyList<RunningImage> RunningFrom(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var found = new List<RunningImage>();

        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);

        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Could not take a process snapshot, so BrowserAI cannot tell whether a browser is still running out of the tree it was asked to replace.");
        }

        var entry = default(ProcessEntry32);
        entry.Size = (uint)Unsafe.SizeOf<ProcessEntry32>();

        if (!Process32FirstW(snapshot, ref entry))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read the first process in the snapshot.");
        }

        do
        {
            var processId = (int)entry.ProcessId;

            if (processId <= 0)
            {
                continue;
            }

            using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, entry.ProcessId);

            if (handle.IsInvalid)
            {
                // A process in another session, or one this token cannot open.
                // Skipped rather than guessed at: the image path is the only
                // thing that could make it ours, and it cannot be read.
                continue;
            }

            var path = ImagePathOf(handle);

            if (path is null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!GetProcessTimes(handle, out var created, out _, out _, out _))
            {
                // Exited between the snapshot and this call. Its pid is now
                // meaningless, and reporting it would name a stranger.
                continue;
            }

            found.Add(new RunningImage(processId, created, path));
        }
        while (Process32NextW(snapshot, ref entry));

        return found;
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

    /// <summary>
    /// The 260-character image-name field, declared so the structure is the size
    /// Windows expects and <b>never read</b>.
    /// </summary>
    /// <remarks>
    /// The same deliberate blindness as the containment probe's: the rule is
    /// never to match a process by image name, and a field that is never
    /// projected into a string cannot be compared to one. <see cref="ushort"/>
    /// rather than <c>char</c> because <c>char</c> is not blittable under runtime
    /// marshalling.
    /// </remarks>
    [InlineArray(260)]
    private struct ImageNameWeDoNotRead
    {
        private ushort _element0;
    }

#pragma warning disable CS0649 // Filled in by the kernel; several fields exist only to make the struct the size Windows expects.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public ImageNameWeDoNotRead ImageName;
    }
#pragma warning restore CS0649

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeSnapshot CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(SafeSnapshot hSnapshot, ref ProcessEntry32 lppe);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(SafeSnapshot hSnapshot, ref ProcessEntry32 lppe);

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

/// <summary>A toolhelp snapshot handle that closes itself.</summary>
internal sealed partial class SafeSnapshot : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an invalid handle for the marshaller to fill in.</summary>
    public SafeSnapshot()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}

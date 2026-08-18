// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What the machine was carrying at the instant something failed on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>For failure messages, and only for them.</b> No assertion may read any of
/// this: every number here is a property of whatever else is running on the
/// machine, so an assertion on one would be a test that passes or fails
/// depending on the developer's other windows. What it is for is the sentence a
/// test prints when a process it launched died for no reason it can see —
/// <i>"a real Chromium exited with code 1 having written nothing"</i> is a
/// finding with no content until you know whether the machine was idle or was
/// carrying eight hundred processes at the time.
/// </para>
/// <para>
/// <b>One call, and no process is ever named.</b> <c>GetPerformanceInfo</c>
/// answers the whole system in a single syscall: the process, thread and handle
/// counts, and the commit and physical figures a launch failure is most likely
/// to be about. Deliberately <i>not</i> a walk that groups processes by image
/// name — the house rule forbids matching, counting or terminating by name, and
/// a diagnostic is not an exception to it. The system-wide totals answer the
/// question that matters ("was this machine saturated") without asking the one
/// that is forbidden.
/// </para>
/// <para>
/// ⚠️ <b>Desktop heap is the one ceiling this cannot see, and it is named as a
/// gap rather than left to be assumed covered.</b> A Chromium that cannot create
/// a window station object fails in exactly the way being investigated, and
/// there is no documented API that reports desktop-heap usage — it is readable
/// only with a kernel debugger extension. So a launch failure with these numbers
/// all healthy does not exonerate the machine.
/// </para>
/// </remarks>
internal static partial class MachineLoad
{
    /// <summary>The whole system, in one line per figure.</summary>
    /// <returns>What the machine was carrying, or why it could not be read.</returns>
    public static string Describe()
    {
        var information = default(PerformanceInformation);
        information.Size = (uint)Marshal.SizeOf<PerformanceInformation>();

        if (!GetPerformanceInfo(ref information, information.Size))
        {
            return $"<GetPerformanceInfo failed: {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}>";
        }

        var page = (double)information.PageSize;

        return string.Join(
            Environment.NewLine,
            $"  processes:        {information.ProcessCount.ToString(CultureInfo.InvariantCulture)}",
            $"  threads:          {information.ThreadCount.ToString(CultureInfo.InvariantCulture)}",
            $"  kernel handles:   {information.HandleCount.ToString(CultureInfo.InvariantCulture)}",
            $"  physical total:   {Mib(information.PhysicalTotal, page)} MiB",
            $"  physical free:    {Mib(information.PhysicalAvailable, page)} MiB",
            $"  commit total:     {Mib(information.CommitTotal, page)} MiB",
            $"  commit limit:     {Mib(information.CommitLimit, page)} MiB",
            $"  commit peak:      {Mib(information.CommitPeak, page)} MiB",
            $"  processors:       {Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)}",
            "  desktop heap:     <not readable without a kernel debugger; a Chromium that cannot create a window object fails this way and none of the figures above would show it>");
    }

    private static string Mib(nuint pages, double pageSize) =>
        (pages * pageSize / (1024 * 1024)).ToString("F0", CultureInfo.InvariantCulture);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPerformanceInfo(ref PerformanceInformation information, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }
}

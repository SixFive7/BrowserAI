// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// One Windows job object, holding one child tree, configured so that closing
/// its handle terminates every member.
/// </summary>
/// <remarks>
/// <para>
/// <b>The intuition runs backwards, so read this before changing a flag.</b> Job
/// membership is inherited automatically by every descendant created with
/// <c>CreateProcess</c>. Escaping requires <c>CREATE_BREAKAWAY_FROM_JOB</c> on
/// the child <i>and</i> a breakaway flag on the job — and when a child asks for
/// it from a job that does not permit it, <c>CreateProcessW</c> fails with
/// <c>ERROR_ACCESS_DENIED</c> rather than escaping. A job granting no breakaway
/// flags therefore converts every escape attempt into a launch failure, which is
/// the fact the whole containment guarantee rests on.
/// </para>
/// <para>
/// Two configuration mistakes are proven fatal by measurement, and both are one
/// keystroke away:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>An inheritable handle.</b> Redirecting stdio forces
/// <c>bInheritHandles=TRUE</c>, so a job handle marked inheritable is duplicated
/// into the child; ours is then no longer the last handle,
/// <c>KILL_ON_JOB_CLOSE</c> never fires on our death, and <b>every child
/// survives</b>. <see cref="CreateKillOnClose"/> passes <c>NULL</c> security
/// attributes, and <see cref="HandleIsInheritable"/> exists so a test can prove
/// it rather than trust it.
/// </item>
/// <item>
/// <b>A name.</b> An unnamed job has exactly one door — the handle this object
/// holds. A named one is an <c>OpenJobObject</c> away from a handle any process
/// running as the same user can take, and a handle to this job is a handle to
/// every browser BrowserAI has spawned.
/// </item>
/// </list>
/// <para>
/// <b>Never add a flag here.</b> <c>BREAKAWAY_OK</c> does not merely permit an
/// escape, it causes one: Firefox's <c>NeedToBreakAwayFromJob()</c> returns true
/// only for a job carrying <i>both</i> <c>KILL_ON_JOB_CLOSE</c> and
/// <c>BREAKAWAY_OK</c>, so ours is the configuration it checks and declines.
/// <c>JobObjectBasicUIRestrictions</c> is equally forbidden — jobs nest only if
/// neither sets UI limits, and Chromium's sandbox job has to nest inside ours.
/// </para>
/// </remarks>
internal sealed partial class JobObject : IDisposable
{
    /// <summary>
    /// The only limit this project sets. Closing the last handle terminates
    /// every process in the job, which is what makes BrowserAI's own death — by
    /// crash, by <c>TerminateProcess</c>, by a session limit — take the browser
    /// tree with it.
    /// </summary>
    public const uint KillOnJobClose = 0x00002000;

    /// <summary>Permits a member to leave the job on request. Never set here.</summary>
    public const uint BreakawayOk = 0x00000800;

    /// <summary>Lets a member leave the job silently. Never set here.</summary>
    public const uint SilentBreakawayOk = 0x00001000;

    private const int JobObjectBasicProcessIdListClass = 3;
    private const int JobObjectBasicUiRestrictionsClass = 4;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private const uint HandleFlagInherit = 0x00000001;
    private const int ErrorMoreData = 234;

    private JobObject(SafeJobHandle handle) => Handle = handle;

    /// <summary>
    /// Creates an unnamed job whose handle cannot be inherited, carrying
    /// <see cref="KillOnJobClose"/> and nothing else.
    /// </summary>
    /// <returns>The new job. Hold it for the whole life of the child tree.</returns>
    /// <exception cref="Win32Exception">Windows refused to create or configure it.</exception>
    public static JobObject CreateKillOnClose()
    {
        // NULL security attributes: the handle is not inheritable. NULL name:
        // the object is anonymous. Both are the point of this call rather than
        // defaults nobody chose.
        var handle = CreateJobObjectW(nint.Zero, lpName: null);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the job object that contains the browser tree.");
        }

        var job = new JobObject(handle);

        try
        {
            var information = default(JobObjectExtendedLimitInformation);
            information.BasicLimitInformation.LimitFlags = KillOnJobClose;

            // Checked and thrown, never logged and continued. libuv swallows
            // ERROR_ACCESS_DENIED from its own job calls; a swallowed failure
            // here is a process that reports containment it does not have.
            if (!SetInformationJobObject(
                    handle,
                    JobObjectExtendedLimitInformationClass,
                    ref information,
                    (uint)Unsafe.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not set KILL_ON_JOB_CLOSE on the job object.");
            }

            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The limit flags Windows reports for this job — read back, never the
    /// value that was written.
    /// </summary>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public uint LimitFlags
    {
        get
        {
            var information = default(JobObjectExtendedLimitInformation);

            return QueryInformationJobObject(
                Handle,
                JobObjectExtendedLimitInformationClass,
                ref information,
                (uint)Unsafe.SizeOf<JobObjectExtendedLimitInformation>(),
                out _)
                ? information.BasicLimitInformation.LimitFlags
                : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read the job object's limit flags.");
        }
    }

    /// <summary>
    /// The job's UI restriction class, which must stay zero: jobs nest only if
    /// neither sets UI limits.
    /// </summary>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public uint UiRestrictions
    {
        get
        {
            var restrictions = default(JobObjectBasicUiRestrictions);

            return QueryInformationJobObject(
                Handle,
                JobObjectBasicUiRestrictionsClass,
                ref restrictions,
                (uint)Unsafe.SizeOf<JobObjectBasicUiRestrictions>(),
                out _)
                ? restrictions.UiRestrictionsClass
                : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read the job object's UI restrictions.");
        }
    }

    /// <summary>
    /// Whether this job's handle would be inherited by a child started with
    /// <c>bInheritHandles=TRUE</c>. It must be <see langword="false"/>.
    /// </summary>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public bool HandleIsInheritable
    {
        get
        {
            if (!GetHandleInformation(Handle, out var flags))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read the job handle's inheritance flag.");
            }

            return (flags & HandleFlagInherit) is not 0;
        }
    }

    /// <summary>The raw handle, for the one call that needs it: process creation.</summary>
    internal SafeJobHandle Handle { get; }

    /// <summary>
    /// Every process id the kernel currently reports as a member.
    /// </summary>
    /// <remarks>
    /// This is the only inventory of the tree that cannot be wrong: it is not a
    /// parent-child walk, which is re-parentable, and not a name match, which is
    /// forbidden outright. A process that exited is simply absent.
    /// </remarks>
    /// <returns>The member process ids, in the order Windows reported them.</returns>
    /// <exception cref="Win32Exception">The query failed for a reason other than a short buffer.</exception>
    public IReadOnlyList<int> ProcessIds()
    {
        // JOBOBJECT_BASIC_PROCESS_ID_LIST is two DWORDs followed by a
        // variable-length ULONG_PTR array, so it cannot be a struct and the
        // buffer has to grow until it fits. Windows fills what it can and
        // reports ERROR_MORE_DATA, so a caller that ignores the return value
        // silently under-reports exactly when the tree is largest.
        var capacity = 64;

        while (true)
        {
            var buffer = new byte[8 + (capacity * nint.Size)];

            if (!QueryInformationJobObject(
                    Handle,
                    JobObjectBasicProcessIdListClass,
                    ref buffer[0],
                    (uint)buffer.Length,
                    out _))
            {
                var error = Marshal.GetLastPInvokeError();

                if (error is not ErrorMoreData || capacity >= 65536)
                {
                    throw new Win32Exception(error, "Could not read the job object's process id list.");
                }

                capacity *= 2;
                continue;
            }

            var assigned = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            var inList = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));

            if (inList < assigned && capacity < 65536)
            {
                capacity *= 2;
                continue;
            }

            var ids = new int[inList];

            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = (int)MemoryMarshal.Read<nuint>(buffer.AsSpan(8 + (i * nint.Size)));
            }

            return ids;
        }
    }

    /// <summary>Whether a process is a member of this job.</summary>
    /// <param name="process">An open handle to the process being asked about.</param>
    /// <returns><see langword="true"/> if the process belongs to this job.</returns>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public bool Contains(SafeProcessHandle process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!IsProcessInJob(process, Handle.DangerousGetHandle(), out var result))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not ask whether a process belongs to the job.");
        }

        GC.KeepAlive(Handle);
        return result;
    }

    /// <summary>Whether a process belongs to any job at all.</summary>
    /// <param name="process">An open handle to the process being asked about.</param>
    /// <returns><see langword="true"/> if the process belongs to some job.</returns>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public static bool IsInAnyJob(SafeProcessHandle process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return IsProcessInJob(process, nint.Zero, out var result)
            ? result
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not ask whether a process belongs to a job.");
    }

    /// <summary>
    /// Closes the job handle, which terminates every process still in it.
    /// </summary>
    /// <remarks>
    /// This <b>is</b> the kill path. There is no enumerate-and-terminate step
    /// and no name match anywhere in it: the kernel tears the whole job down at
    /// once, so a member respawned mid-teardown is already contained. An
    /// enumerate-and-kill sweep cannot win that race at any repetition count.
    /// </remarks>
    public void Dispose() => Handle.Dispose();

    // The documented layouts. Every field is blittable and the declaration
    // order is the header's, so Unsafe.SizeOf<T>() above equals the native
    // size and no marshalling stub is generated -- which is what keeps this
    // working under NativeAOT.
    //
    // CS0649 is disabled across them: these are filled in by the kernel, so
    // most of their fields are never assigned in C# and several are never read
    // at all. They exist to make the struct the size Windows expects.
#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicUiRestrictions
    {
        public uint UiRestrictionsClass;
    }
#pragma warning restore CS0649

    // LibraryImport rather than DllImport on every declaration: DllImport
    // relies on runtime IL-stub generation, which NativeAOT does not do.
    // System32 only, because without it the loader searches the application
    // directory first and a kernel32.dll dropped beside the binary would win
    // (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeJobHandle CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryInformationJobObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectBasicUiRestrictions lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryInformationJobObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref byte lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(
        SafeProcessHandle processHandle,
        nint jobHandle,
        [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetHandleInformation(SafeJobHandle hObject, out uint lpdwFlags);
}

/// <summary>A job object handle that closes itself, and kills the job when it does.</summary>
internal sealed partial class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an unowned, invalid handle for the marshaller to fill in.</summary>
    public SafeJobHandle()
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

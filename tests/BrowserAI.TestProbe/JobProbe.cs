// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.TestProbe;

/// <summary>
/// The three process roles the job-containment acceptance test needs, ported
/// from the <c>.work/jobtest/</c> prototype that measured 16 runs, 106
/// processes, 0 escapees and 0 survivors.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the launcher is a separate process at all.</b> The property under
/// test is what happens when the process holding the job handle is killed from
/// outside, and a test host cannot be that process. So the host starts this
/// launcher, the launcher starts the real child inside a job created by
/// <b>product code</b>, and the host then terminates the launcher and asks
/// whether anything survived.
/// </para>
/// <para>
/// <b>Why the launcher does the enumeration.</b> <c>IsProcessInJob</c> needs a
/// handle to the job, and the job is unnamed and never duplicated, so the only
/// process that can answer the question is the one that created it. The report
/// it writes is the evidence; the test host reads that file and acts only on
/// the pids and creation times in it.
/// </para>
/// <para>
/// <b>No step of this file matches a process by image name.</b> The toolhelp
/// walk deliberately declares <c>szExeFile</c> as an opaque buffer and never
/// reads it — the structure has to be the size Windows expects, but nothing
/// here can compare it to <c>chrome.exe</c> even by accident.
/// </para>
/// </remarks>
internal static partial class JobProbe
{
    private const uint JobObjectMessageNewProcess = 6;
    private const int JobObjectAssociateCompletionPortClass = 7;
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateNoWindow = 0x08000000;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectLimitBreakawayOk = 0x00000800;
    private const uint JobObjectLimitSilentBreakawayOk = 0x00001000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private static readonly HashSet<int> EverInJob = [];

    /// <summary>
    /// Creates a job with the product's own code, starts a child in it, proves
    /// what is inside it, and then waits to be killed.
    /// </summary>
    /// <param name="outputDirectory">Where the report and the done marker are written.</param>
    /// <param name="readyFile">The file the child writes once its own tree is up.</param>
    /// <param name="command">The executable to start inside the job.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Never, on the happy path: the process parks until it is terminated.</returns>
    public static int Launcher(string outputDirectory, string readyFile, string command, IReadOnlyList<string> arguments)
    {
        _ = Directory.CreateDirectory(outputDirectory);

        // Product code, not a copy of it. If this line stops producing a
        // correctly configured job, this test fails rather than a comment
        // becoming untrue.
        using var job = JobObject.CreateKillOnClose();

        // Associated BEFORE the child exists, so the very first membership
        // message is captured. This is what makes a process whose parent has
        // already exited reachable: the walk below is seeded with every pid the
        // job ever reported, not only with the ones still linked to the root.
        var port = AssociateCompletionPort(job);
        var drain = new Thread(() => DrainCompletionPort(port)) { IsBackground = true };
        drain.Start();

        using var process = JobLauncher.Start(job, command, arguments, outputDirectory, ChildEnvironment.Build());

        // The child's own stdio is a pipe nobody is draining in this process;
        // reading it here keeps a chatty child from blocking on a full buffer.
        Drain(process.StandardOutput);
        Drain(process.StandardError);

        WaitForFile(readyFile, TimeSpan.FromSeconds(60));

        // Descendants a browser or a runtime starts asynchronously -- renderers,
        // GPU, crashpad -- appear after the child reports ready.
        Thread.Sleep(1500);

        // ⚠️ Two snapshots of the job's own membership, taken either side of the
        // walk, because a live browser is not a static tree: Chromium starts and
        // retires helpers continuously, so a single list taken AFTER the walk
        // reports every process born during it as one the walk missed. Measured
        // 2026-08-16 against a real Chromium -- one phantom "missed" member,
        // reproducibly, against a node tree that had never produced any.
        //
        // The per-row check uses the union, so a process born during the walk is
        // still recognised as a member. The "did the walk miss anything"
        // direction uses the INTERSECTION -- members present both before and
        // after -- which is the only set whose absence from the walk means the
        // seeding actually failed.
        var jobBefore = job.ProcessIds();
        var walk = Walk(process.Id);
        var jobAfter = job.ProcessIds();

        var jobProcessIds = jobAfter;
        var jobProcessIdSet = jobBefore.Concat(jobAfter).ToHashSet();
        var stableJobMembers = jobBefore.Where(jobAfter.Contains).ToList();

        var rows = new JsonArray();
        var escapees = 0;

        foreach (var entry in walk)
        {
            var inOurJob = default(bool?);
            var inAnyJob = default(bool?);
            var note = string.Empty;

            using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)entry.ProcessId);

            if (handle.IsInvalid)
            {
                note = $"OpenProcess failed with {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}";
            }
            else
            {
                inOurJob = job.Contains(handle);
                inAnyJob = JobObject.IsInAnyJob(handle);

                if (inOurJob is false)
                {
                    escapees++;
                }
            }

            rows.Add(new JsonObject
            {
                ["pid"] = entry.ProcessId,
                ["parentPid"] = entry.ParentProcessId,
                ["createdFileTime"] = entry.CreatedFileTime,
                ["depth"] = entry.Depth,
                ["inOurJob"] = inOurJob,
                ["inAnyJob"] = inAnyJob,
                ["inJobProcessIdList"] = jobProcessIdSet.Contains(entry.ProcessId),
                ["note"] = note,
            });
        }

        var walked = walk.Select(entry => entry.ProcessId).ToHashSet();

        var report = new JsonObject
        {
            ["launcherPid"] = Environment.ProcessId,
            ["rootChildPid"] = process.Id,
            ["limitFlags"] = job.LimitFlags,
            ["uiRestrictions"] = job.UiRestrictions,
            ["handleIsInheritable"] = job.HandleIsInheritable,
            ["jobProcessIds"] = new JsonArray([.. jobProcessIds.Select(id => JsonValue.Create(id))]),
            ["jobMembersTheWalkMissed"] = new JsonArray(
                [.. stableJobMembers.Where(id => !walked.Contains(id)).Select(id => JsonValue.Create(id))]),
            ["escapees"] = escapees,
            ["walk"] = rows,
            ["childReport"] = ReadChildReport(readyFile),
        };

        File.WriteAllText(Path.Combine(outputDirectory, "report.json"), report.ToJsonString(), new UTF8Encoding(false));

        // The done marker is written last and is what the test waits on, so a
        // partially written report can never be read as a complete one.
        File.WriteAllText(Path.Combine(outputDirectory, "done"), "ok", new UTF8Encoding(false));

        // Parks until the test terminates it from outside, which is the event
        // the whole acceptance test exists to observe.
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// <summary>
    /// Stands in for <c>node</c>: reproduces libuv's permissive job around
    /// itself, spawns grandchildren into it, and proves that a breakaway is
    /// refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The nested permissive job is the point, not decoration.</b> libuv
    /// creates a global job carrying <c>BREAKAWAY_OK</c> and
    /// <c>SILENT_BREAKAWAY_OK</c> and assigns every non-detached child to it,
    /// and Playwright spawns the browser with <c>detached: false</c> on Windows
    /// — so this is the exact configuration that sits between BrowserAI's job
    /// and a real browser, and the exact one that would leak if the outer job
    /// were misconfigured. Reproducing it here makes the acceptance test prove
    /// that on a machine with no payload built.
    /// </para>
    /// <para>
    /// The grandchildren are started the ordinary way, with no job call of any
    /// kind, because that is the case the guarantee rests on: membership is
    /// inherited automatically, and the component that "just spawns normally"
    /// is precisely the one that cannot escape.
    /// </para>
    /// </remarks>
    /// <param name="readyFile">Where to report this process's own tree.</param>
    /// <param name="grandchildCount">How many grandchildren to start.</param>
    /// <returns>Never, on the happy path: the process parks until it is terminated.</returns>
    public static int Child(string readyFile, int grandchildCount)
    {
        // FIRST, while the only jobs above this process are BrowserAI's and the
        // suite's -- neither of which permits breakaway. This is the plain
        // configuration the guarantee is stated for.
        var (aloneError, alonePid) = AttemptBreakaway();

#pragma warning disable CA2000 // Held for the life of the process on purpose: closing this handle would terminate the grandchildren it exists to contain.
        var libuvShaped = CreateJobObjectW(nint.Zero, null);
#pragma warning restore CA2000

        if (libuvShaped.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The probe could not create its libuv-shaped job.");
        }

        SetLimitFlags(libuvShaped, JobObjectLimitKillOnJobClose | JobObjectLimitBreakawayOk | JobObjectLimitSilentBreakawayOk);

        if (!AssignProcessToJobObject(libuvShaped, GetCurrentProcess()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The probe could not join its own libuv-shaped job.");
        }

        var grandchildren = new JsonArray();

        for (var i = 0; i < grandchildCount; i++)
        {
            var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(readyFile)!,
                ArgumentList = { "job-grandchild" },
            })!;

            grandchildren.Add(grandchild.Id);
        }

        // SECOND, now that a permissive job sits between this process and
        // BrowserAI's. This is the production shape: libuv's job is always in
        // the chain, because Playwright spawns the browser with
        // `detached: false` on Windows.
        var (nestedError, nestedPid) = AttemptBreakaway();

        var report = new JsonObject
        {
            ["childPid"] = Environment.ProcessId,
            ["grandchildPids"] = grandchildren,
            ["breakawayFromOurJobAlone"] = new JsonObject
            {
                ["error"] = aloneError,
                ["pid"] = alonePid,
            },
            ["breakawayInsideNestedJob"] = new JsonObject
            {
                ["error"] = nestedError,
                ["pid"] = nestedPid,
            },
        };

        File.WriteAllText(readyFile, report.ToJsonString(), new UTF8Encoding(false));

        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// <summary>A leaf process that does nothing but exist until it is killed.</summary>
    /// <returns>Never: the process parks until it is terminated.</returns>
    public static int Grandchild()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// <summary>
    /// Tries to start a process that asks to leave the job, and reports the
    /// Win32 error and the pid rather than a verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both outcomes are reported because both are correct, in different
    /// configurations, and the test asserts which one belongs where.</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// With no permissive job in the chain, the launch <b>fails</b> with
    /// <c>ERROR_ACCESS_DENIED</c> (5). That is the fact the guarantee rests on:
    /// a job granting no breakaway converts an escape attempt into a launch
    /// failure.
    /// </item>
    /// <item>
    /// With a permissive job nested inside ours — libuv's, which is always
    /// there in production — the launch <b>succeeds</b> and the new process
    /// stops at the first job that does not permit breakaway, which is ours. It
    /// is contained, not escaped. A verdict of "0 means we failed" would be
    /// wrong here, which is why this returns the pid too: what matters is where
    /// the process ended up, not whether the call returned.
    /// </item>
    /// </list>
    /// </remarks>
    private static (int Error, int ProcessId) AttemptBreakaway()
    {
        var commandLine = new char[Environment.ProcessPath!.Length + " job-grandchild".Length + 3];
        var text = $"\"{Environment.ProcessPath}\" job-grandchild";
        text.CopyTo(0, commandLine, 0, text.Length);

        var startupInfo = default(StartupInfo);
        startupInfo.Cb = Unsafe.SizeOf<StartupInfo>();

        if (!CreateProcessW(
                Environment.ProcessPath,
                ref commandLine[0],
                nint.Zero,
                nint.Zero,
                bInheritHandles: false,
                CreateBreakawayFromJob | CreateNoWindow,
                nint.Zero,
                null,
                ref startupInfo,
                out var information))
        {
            return (Marshal.GetLastPInvokeError(), 0);
        }

        // Left running on purpose: the walk has to reach it, so that where it
        // ended up is measured rather than assumed.
        _ = CloseHandle(information.Thread);
        _ = CloseHandle(information.Process);
        return (0, (int)information.ProcessId);
    }

    private static JsonNode? ReadChildReport(string readyFile)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(readyFile));
        }
#pragma warning disable CA1031 // A missing or malformed child report is reported as absent; the walk is the real evidence.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static void Drain(Stream stream) =>
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];

            while (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
            {
                // Discarded on purpose: what the child says is not this test's
                // subject, but a full pipe buffer would stop it saying anything.
            }
        });

    private static void WaitForFile(string path, TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < patience)
        {
            if (File.Exists(path))
            {
                return;
            }

            Thread.Sleep(25);
        }

        throw new TimeoutException($"The child never wrote '{path}'.");
    }

    private static nint AssociateCompletionPort(JobObject job)
    {
        var port = CreateIoCompletionPort(-1, nint.Zero, nuint.Zero, 1);

        if (port == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the completion port for the job.");
        }

        var association = new JobObjectAssociateCompletionPort
        {
            CompletionKey = 0x4242,
            CompletionPort = port,
        };

        return SetInformationJobObject(
            job.Handle,
            JobObjectAssociateCompletionPortClass,
            ref association,
            (uint)Unsafe.SizeOf<JobObjectAssociateCompletionPort>())
            ? port
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not associate a completion port with the job.");
    }

    private static void DrainCompletionPort(nint port)
    {
        while (true)
        {
            if (GetQueuedCompletionStatus(port, out var message, out _, out var overlapped, 500) && message == JobObjectMessageNewProcess)
            {
                lock (EverInJob)
                {
                    // The "overlapped" pointer is the pid for job messages.
                    _ = EverInJob.Add((int)overlapped);
                }
            }
        }
    }

    private static void SetLimitFlags(SafeJobHandle job, uint limitFlags)
    {
        var information = default(JobObjectExtendedLimitInformation);
        information.BasicLimitInformation.LimitFlags = limitFlags;

        if (!SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformationClass,
                ref information,
                (uint)Unsafe.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The probe could not configure its libuv-shaped job.");
        }
    }

    /// <summary>
    /// A toolhelp descendant walk, seeded with the root child <b>and</b> every
    /// pid the job ever reported.
    /// </summary>
    /// <remarks>
    /// The seeding is what makes this a cross-check rather than a second
    /// opinion from the same source: a process whose parent has already exited
    /// is re-parented and would be invisible to a pure parent-child walk, so it
    /// is reached from the completion port instead. A child that predates its
    /// parent is skipped, because a pid the kernel has reused is not a
    /// descendant.
    /// </remarks>
    private static List<ProcessEntry> Walk(int rootProcessId)
    {
        var all = Snapshot();
        var byProcessId = all.ToDictionary(entry => entry.ProcessId);
        var children = all.GroupBy(entry => entry.ParentProcessId).ToDictionary(group => group.Key, group => group.ToList());

        var seeds = new List<int> { rootProcessId };

        lock (EverInJob)
        {
            seeds.AddRange(EverInJob.Where(id => id != rootProcessId));
        }

        var result = new List<ProcessEntry>();
        var seen = new HashSet<int>();
        var pending = new Stack<ProcessEntry>();

        foreach (var seed in seeds.Where(byProcessId.ContainsKey))
        {
            pending.Push(byProcessId[seed] with { Depth = 0 });
        }

        while (pending.Count > 0)
        {
            var entry = pending.Pop();

            if (!seen.Add(entry.ProcessId))
            {
                continue;
            }

            result.Add(entry);

            if (!children.TryGetValue(entry.ProcessId, out var descendants))
            {
                continue;
            }

            foreach (var descendant in descendants.Where(candidate => candidate.ProcessId != entry.ProcessId))
            {
                if (descendant.CreatedFileTime is not 0 && entry.CreatedFileTime is not 0 && descendant.CreatedFileTime < entry.CreatedFileTime)
                {
                    // A pid the kernel reused: it cannot be a child of a
                    // process that started after it.
                    continue;
                }

                pending.Push(descendant with { Depth = entry.Depth + 1 });
            }
        }

        return [.. result.OrderBy(entry => entry.Depth).ThenBy(entry => entry.ProcessId)];
    }

    private static List<ProcessEntry> Snapshot()
    {
        var entries = new List<ProcessEntry>();

        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);

        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not take a process snapshot.");
        }

        var entry = default(ProcessEntry32);
        entry.Size = (uint)Unsafe.SizeOf<ProcessEntry32>();

        if (!Process32FirstW(snapshot, ref entry))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read the first process in the snapshot.");
        }

        do
        {
            entries.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, CreationTimeOf((int)entry.ProcessId), 0));
        }
        while (Process32NextW(snapshot, ref entry));

        return entries;
    }

    private static long CreationTimeOf(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        return !handle.IsInvalid && GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : 0;
    }

    /// <summary>One process in the snapshot: identity only, never a name.</summary>
    private readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, long CreatedFileTime, int Depth);

    /// <summary>
    /// The 260-character image-name field, declared so the structure is the
    /// size Windows expects and never read. <b>That is deliberate:</b> the rule
    /// is never to match a process by image name, and a field that is never
    /// projected into a string cannot be compared to one.
    /// </summary>
    /// <remarks>
    /// The element type is <see cref="ushort"/> rather than <see cref="char"/>
    /// because <c>char</c> is not blittable under runtime marshalling — the
    /// generator refuses it with SYSLIB1051 — and because UTF-16 code units
    /// nobody decodes is exactly what this field is.
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

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectAssociateCompletionPort
    {
        public nuint CompletionKey;
        public nint CompletionPort;
    }

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
    private struct StartupInfo
    {
        public int Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Length;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
#pragma warning restore CS0649

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateIoCompletionPort(nint fileHandle, nint existingCompletionPort, nuint completionKey, uint numberOfConcurrentThreads);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetQueuedCompletionStatus(
        nint completionPort,
        out uint lpNumberOfBytesTransferred,
        out nuint lpCompletionKey,
        out nint lpOverlapped,
        uint dwMilliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectAssociateCompletionPort lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "SetInformationJobObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeJobHandle CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeJobHandle hJob, nint hProcess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeSnapshotHandle CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(SafeSnapshotHandle hSnapshot, ref ProcessEntry32 lppe);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(SafeSnapshotHandle hSnapshot, ref ProcessEntry32 lppe);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

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
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string? lpApplicationName,
        ref char lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}

/// <summary>A toolhelp snapshot handle that closes itself.</summary>
internal sealed partial class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an invalid handle for the marshaller to fill in.</summary>
    public SafeSnapshotHandle()
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

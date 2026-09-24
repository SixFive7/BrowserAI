// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace BrowserAI.Interop;

/// <summary>
/// The Task Scheduler's COM entry point, declared once, and the four interfaces
/// BrowserAI calls through it.
/// </summary>
/// <remarks>
/// <para>
/// <b>COM through <c>[GeneratedComInterface]</c>, and not <c>schtasks.exe</c>
/// -- decided 2026-09-25 with the per-user logon task, Q282 a.</b> The deciding
/// fact is the on-demand start: a blocked server runs the task with the
/// coordinator's argument, and only <c>IRegisteredTask::Run</c> passes one --
/// it fills the action's <c>$(Arg0)</c>, measured 2026-09-25 -- while
/// <c>schtasks /run</c> and PowerShell's <c>Start-ScheduledTask</c> take none. The
/// same interface then registers, reads back and removes, with no temporary XML
/// file, no child process started from a server whose standard handles belong to
/// its client, and an <c>HRESULT</c> for every failure instead of a localised line
/// of console output. Q273 b and Q274 c allow the generator.
/// </para>
/// <para>
/// ⚠️ <b>A vtable slot is its position, and nothing at run time checks it.</b> Each
/// interface declares every method up to the last one BrowserAI calls, in the
/// order <c>taskschd.h</c> declares them (Windows SDK 10.0.26100.0), after the four
/// <c>IDispatch</c> slots. <c>InteropLayoutTests</c> compares the order with
/// Microsoft's own metadata, which is the check a wrong slot would otherwise only
/// meet as the wrong function running.
/// </para>
/// <para>
/// <b>A <c>VARIANT</c> is <see cref="Variant"/>, hand-written and blittable.</b>
/// The framework's <c>ComVariant</c> needs runtime marshalling disabled for the
/// whole assembly to cross a generated interface by value (SYSLIB1051, measured
/// 2026-09-25), and this assembly's other declarations were not written for that.
/// Only two shapes are ever passed: empty, and one <c>BSTR</c>.
/// </para>
/// </remarks>
internal static partial class TaskSchedulerInterop
{
    /// <summary><c>TASK_CREATE_OR_UPDATE</c>.</summary>
    public const int CreateOrUpdate = 6;

    /// <summary><c>TASK_LOGON_INTERACTIVE_TOKEN</c>: run as the user, only while the user is signed in.</summary>
    public const int InteractiveToken = 3;

    /// <summary><c>VT_BSTR</c>.</summary>
    public const ushort VariantString = 8;

    /// <summary>What the scheduler answers for a task or folder that is not there.</summary>
    public const int NotFound = unchecked((int)0x80070002);

    /// <summary><c>RPC_E_CHANGED_MODE</c>: the thread is already in the other apartment.</summary>
    public const int ChangedMode = unchecked((int)0x80010106);

    /// <summary><c>COINIT_MULTITHREADED</c>.</summary>
    public const uint MultiThreaded = 0;

    /// <summary><c>CLSCTX_INPROC_SERVER</c>.</summary>
    private const uint InProcessServer = 1;

    /// <summary><c>CLSID_TaskScheduler</c>, from <c>taskschd.h</c>.</summary>
    private static readonly Guid TaskSchedulerClass = new("0f87369f-a4e5-4cfc-bd3e-73e6154572dd");

    /// <summary><c>IID_ITaskService</c>, from <c>taskschd.h</c>.</summary>
    private static readonly Guid TaskServiceInterface = new("2faba4c7-4da9-4013-9697-20cc3fd40f85");

    /// <summary>Creates the Task Scheduler's service object on this thread.</summary>
    /// <param name="service">The service, when it was created.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    public static int CreateService(out ITaskService service) =>
        CoCreateInstance(TaskSchedulerClass, 0, InProcessServer, TaskServiceInterface, out service);

    /// <summary><c>VARIANT</c>, in the two shapes BrowserAI passes.</summary>
    /// <remarks>
    /// Checked against Microsoft's own metadata by <c>InteropLayoutTests</c>: 24
    /// bytes on x64, the type in the first two, the value from byte 8.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Variant
    {
        /// <summary>The <c>VARTYPE</c>; zero is <c>VT_EMPTY</c>.</summary>
        public ushort Type;

        /// <summary>Reserved.</summary>
        public ushort Reserved1;

        /// <summary>Reserved.</summary>
        public ushort Reserved2;

        /// <summary>Reserved.</summary>
        public ushort Reserved3;

        /// <summary>The value's first pointer-sized half: the <c>BSTR</c> for <see cref="VariantString"/>.</summary>
        public nint Value;

        /// <summary>The value's second half, unused by both shapes.</summary>
        public nint Record;
    }

    /// <summary>Joins this thread to the multi-threaded apartment.</summary>
    /// <param name="reserved">Zero.</param>
    /// <param name="concurrency">One of the <c>COINIT</c> values.</param>
    /// <returns><c>S_OK</c> or <c>S_FALSE</c> when joined, <see cref="ChangedMode"/> when the thread is already in another.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx", SetLastError = false)]
    internal static partial int CoInitializeEx(nint reserved, uint concurrency);

    /// <summary>Leaves the apartment a successful <see cref="CoInitializeEx"/> joined.</summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize", SetLastError = false)]
    internal static partial void CoUninitialize();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance", SetLastError = false)]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out ITaskService service);
}

/// <summary>
/// The four <c>IDispatch</c> slots every Task Scheduler interface begins with.
/// BrowserAI never calls them; they are declared so that the slots after them
/// are counted from the right place.
/// </summary>
[GeneratedComInterface]
[Guid("00020400-0000-0000-c000-000000000046")]
internal partial interface IDispatchSlots
{
    /// <summary>Slot 3.</summary>
    /// <param name="count">Never read.</param>
    void GetTypeInfoCount(out uint count);

    /// <summary>Slot 4.</summary>
    /// <param name="index">Never passed.</param>
    /// <param name="locale">Never passed.</param>
    /// <param name="typeInfo">Never read.</param>
    void GetTypeInfo(uint index, uint locale, out nint typeInfo);

    /// <summary>Slot 5.</summary>
    /// <param name="interfaceId">Never passed.</param>
    /// <param name="names">Never passed.</param>
    /// <param name="count">Never passed.</param>
    /// <param name="locale">Never passed.</param>
    /// <param name="dispatchIds">Never passed.</param>
    void GetIDsOfNames(in Guid interfaceId, nint names, uint count, uint locale, nint dispatchIds);

    /// <summary>Slot 6.</summary>
    /// <param name="dispatchId">Never passed.</param>
    /// <param name="interfaceId">Never passed.</param>
    /// <param name="locale">Never passed.</param>
    /// <param name="flags">Never passed.</param>
    /// <param name="parameters">Never passed.</param>
    /// <param name="result">Never passed.</param>
    /// <param name="exception">Never passed.</param>
    /// <param name="argumentError">Never passed.</param>
    void Invoke(int dispatchId, in Guid interfaceId, uint locale, ushort flags, nint parameters, nint result, nint exception, nint argumentError);
}

/// <summary><c>ITaskService</c>, to its fourth method.</summary>
[GeneratedComInterface]
[Guid("2faba4c7-4da9-4013-9697-20cc3fd40f85")]
internal partial interface ITaskService : IDispatchSlots
{
    /// <summary>A folder of registered tasks; <c>\</c> is the root.</summary>
    /// <param name="path">The folder's path.</param>
    /// <returns>The folder.</returns>
    ITaskFolder GetFolder([MarshalAs(UnmanagedType.BStr)] string path);

    /// <summary>Never called.</summary>
    /// <param name="flags">Never passed.</param>
    /// <returns>Never read.</returns>
    nint GetRunningTasks(int flags);

    /// <summary>Never called.</summary>
    /// <param name="flags">Never passed.</param>
    /// <returns>Never read.</returns>
    nint NewTask(uint flags);

    /// <summary>Connects to this machine's scheduler as the calling user, when every argument is empty.</summary>
    /// <param name="serverName">Empty for this machine.</param>
    /// <param name="user">Empty for the calling user.</param>
    /// <param name="domain">Empty.</param>
    /// <param name="password">Empty.</param>
    void Connect(TaskSchedulerInterop.Variant serverName, TaskSchedulerInterop.Variant user, TaskSchedulerInterop.Variant domain, TaskSchedulerInterop.Variant password);
}

/// <summary><c>ITaskFolder</c>, to its tenth method.</summary>
[GeneratedComInterface]
[Guid("8cfac062-a080-4c15-9a88-aa7c2af80dfc")]
internal partial interface ITaskFolder : IDispatchSlots
{
    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetPath();

    /// <summary>Never called.</summary>
    /// <param name="path">Never passed.</param>
    /// <returns>Never read.</returns>
    nint GetFolder([MarshalAs(UnmanagedType.BStr)] string path);

    /// <summary>Never called.</summary>
    /// <param name="flags">Never passed.</param>
    /// <returns>Never read.</returns>
    nint GetFolders(int flags);

    /// <summary>Never called.</summary>
    /// <param name="name">Never passed.</param>
    /// <param name="descriptor">Never passed.</param>
    /// <returns>Never read.</returns>
    nint CreateFolder([MarshalAs(UnmanagedType.BStr)] string name, TaskSchedulerInterop.Variant descriptor);

    /// <summary>Never called.</summary>
    /// <param name="name">Never passed.</param>
    /// <param name="flags">Never passed.</param>
    void DeleteFolder([MarshalAs(UnmanagedType.BStr)] string name, int flags);

    /// <summary>One registered task in this folder.</summary>
    /// <param name="path">The task's name.</param>
    /// <returns>The task. A task that is not there is <c>0x80070002</c>.</returns>
    IRegisteredTask GetTask([MarshalAs(UnmanagedType.BStr)] string path);

    /// <summary>Never called.</summary>
    /// <param name="flags">Never passed.</param>
    /// <returns>Never read.</returns>
    nint GetTasks(int flags);

    /// <summary>Deletes a task. A task that is not there is <c>0x80070002</c>.</summary>
    /// <param name="name">The task's name.</param>
    /// <param name="flags">Zero.</param>
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string name, int flags);

    /// <summary>Registers, or replaces, a task from its XML definition.</summary>
    /// <param name="path">The task's name.</param>
    /// <param name="definition">The Task Scheduler 1.2 XML.</param>
    /// <param name="flags"><see cref="TaskSchedulerInterop.CreateOrUpdate"/>.</param>
    /// <param name="user">Empty: the definition's principal says who.</param>
    /// <param name="password">Empty.</param>
    /// <param name="logonType"><see cref="TaskSchedulerInterop.InteractiveToken"/>.</param>
    /// <param name="descriptor">Empty.</param>
    /// <returns>The registered task.</returns>
    IRegisteredTask RegisterTask(
        [MarshalAs(UnmanagedType.BStr)] string path,
        [MarshalAs(UnmanagedType.BStr)] string definition,
        int flags,
        TaskSchedulerInterop.Variant user,
        TaskSchedulerInterop.Variant password,
        int logonType,
        TaskSchedulerInterop.Variant descriptor);
}

/// <summary><c>IRegisteredTask</c>, to its fourteenth method.</summary>
[GeneratedComInterface]
[Guid("9c86f320-dee3-4dd1-b972-a303f26b061e")]
internal partial interface IRegisteredTask : IDispatchSlots
{
    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetPath();

    /// <summary>The task's <c>TASK_STATE</c>.</summary>
    /// <returns>The state.</returns>
    int GetState();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    short GetEnabled();

    /// <summary>Never called.</summary>
    /// <param name="enabled">Never passed.</param>
    void PutEnabled(short enabled);

    /// <summary>Starts the task now, with its parameter filling <c>$(Arg0)</c>.</summary>
    /// <param name="parameters">Empty, or one <c>BSTR</c>.</param>
    /// <returns>The instance that was started.</returns>
    IRunningTask Run(TaskSchedulerInterop.Variant parameters);

    /// <summary>Never called.</summary>
    /// <param name="parameters">Never passed.</param>
    /// <param name="flags">Never passed.</param>
    /// <param name="sessionId">Never passed.</param>
    /// <param name="user">Never passed.</param>
    /// <returns>Never read.</returns>
    nint RunEx(TaskSchedulerInterop.Variant parameters, int flags, int sessionId, [MarshalAs(UnmanagedType.BStr)] string user);

    /// <summary>Never called.</summary>
    /// <param name="flags">Never passed.</param>
    /// <returns>Never read.</returns>
    nint GetInstances(int flags);

    /// <summary>When the task last ran, as an OLE automation date.</summary>
    /// <returns>The date.</returns>
    double GetLastRunTime();

    /// <summary>The last run's result: its exit code, or one of the scheduler's own states.</summary>
    /// <returns>The result.</returns>
    int GetLastTaskResult();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    int GetNumberOfMissedRuns();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    double GetNextRunTime();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    nint GetDefinition();

    /// <summary>The task's definition, as the scheduler stored it.</summary>
    /// <returns>The XML.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetXml();
}

/// <summary><c>IRunningTask</c>, which <see cref="IRegisteredTask.Run"/> returns and BrowserAI only lets go of.</summary>
[GeneratedComInterface]
[Guid("653758fb-7b9a-4f1e-a471-beeb8e9b834e")]
internal partial interface IRunningTask : IDispatchSlots
{
    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    /// <summary>The instance's identifier, for the record.</summary>
    /// <returns>A GUID in braces.</returns>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetInstanceGuid();
}

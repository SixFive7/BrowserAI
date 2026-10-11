// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

internal static unsafe class N
{
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, SYNCHRONIZE = 0x00100000, WRITE_DAC = 0x00040000;
    public const uint FILE_READ_ATTRIBUTES = 0x80, FILE_WRITE_ATTRIBUTES = 0x100;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3, CREATE_ALWAYS = 2;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000, FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    public const uint PIPE_ACCESS_INBOUND = 1, PIPE_ACCESS_OUTBOUND = 2;

    // NT
    public const uint FILE_CREATE = 2;
    public const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x20, FILE_NON_DIRECTORY_FILE = 0x40;
    public const int FileAccessInformation = 8, FileModeInformation = 16, FilePipeLocalInformation = 24;
    public const int FileNameInfo = 2;

    public static readonly IntPtr Invalid = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances, uint outSize, uint inSize, uint timeout, IntPtr sa);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr ReOpenFile(IntPtr original, uint access, uint share, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr sa, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetFileType(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandleEx(IntPtr h, int cls, byte* buf, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PeekNamedPipe(IntPtr h, IntPtr buf, uint size, IntPtr read, out uint avail, IntPtr left);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr h, byte* buf, uint n, out uint read, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr h, byte* buf, uint n, out uint written, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateIoCompletionPort(IntPtr file, IntPtr existing, UIntPtr key, uint threads);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelSynchronousIo(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenThread(uint access, bool inherit, uint tid);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr sa, bool manual, bool initial, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(IntPtr h, NativeOverlapped* ov, out uint n, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(IntPtr h, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr h, uint ms);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationFile(IntPtr h, out IoStatusBlock iosb, byte* info, uint length, int cls);

    [DllImport("ntdll.dll")]
    public static extern int NtSetInformationFile(IntPtr h, out IoStatusBlock iosb, byte* info, uint length, int cls);

    [DllImport("ntdll.dll")]
    public static extern int NtOpenFile(out IntPtr h, uint access, ref ObjectAttributes oa, out IoStatusBlock iosb, uint share, uint options);

    [DllImport("ntdll.dll")]
    public static extern int NtCreateNamedPipeFile(out IntPtr h, uint access, ref ObjectAttributes oa, out IoStatusBlock iosb, uint share, uint disposition, uint options,
        uint pipeType, uint readMode, uint completionMode, uint maxInstances, uint inQuota, uint outQuota, ref long timeout);

    [DllImport("ntdll.dll")]
    public static extern uint RtlNtStatusToDosError(int status);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryObject(IntPtr h, int cls, void* info, int size, out int ret);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(IntPtr h, int cls, void* info, int size, out int ret);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, char* buf, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, void* buf, nuint size, out nuint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DuplicateHandle(IntPtr srcProc, IntPtr src, IntPtr dstProc, out IntPtr dst, uint access, bool inherit, uint options);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    public struct IoStatusBlock { public IntPtr Status; public IntPtr Information; }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public int ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public int BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}

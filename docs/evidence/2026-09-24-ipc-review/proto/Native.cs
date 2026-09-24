// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static unsafe partial class Native
{
    public const uint LOCKFILE_FAIL_IMMEDIATELY = 0x1;
    public const uint LOCKFILE_EXCLUSIVE_LOCK = 0x2;

    public const uint PIPE_ACCESS_DUPLEX = 0x3;
    public const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const uint PIPE_TYPE_BYTE = 0x0;
    public const uint PIPE_REJECT_REMOTE_CLIENTS = 0x8;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint DELETE = 0x00010000;
    public const uint OPEN_EXISTING = 3;
    public const uint CREATE_ALWAYS = 2;

    public const int ERROR_PIPE_CONNECTED = 535;
    public const int ERROR_PIPE_BUSY = 231;
    public const int ERROR_BROKEN_PIPE = 109;
    public const int ERROR_NO_DATA = 232;
    public const int ERROR_PIPE_NOT_CONNECTED = 233;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_VM_READ = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct Overlapped
    {
        public nuint Internal;
        public nuint InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public nint hEvent;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockFileEx(SafeFileHandle h, uint flags, uint reserved, uint lenLow, uint lenHigh, ref Overlapped o);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnlockFileEx(SafeFileHandle h, uint reserved, uint lenLow, uint lenHigh, ref Overlapped o);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBuf, uint inBuf, uint defaultTimeout, nint securityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConnectNamedPipe(SafeFileHandle h, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DisconnectNamedPipe(SafeFileHandle h);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(SafeFileHandle h, byte* buffer, int count, out int read, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(SafeFileHandle h, byte* buffer, int count, out int written, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint pid);

    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WaitNamedPipe(string name, uint timeoutMs);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(SafeProcessHandle h, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(SafeProcessHandle h, uint flags, char* buffer, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(SafeProcessHandle h, nint address, void* buffer, nuint size, out nuint read);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(nint h, uint code);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(SafeProcessHandle h, int infoClass, void* info, int length, out int returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, void* info, uint size);


    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeInfo(SafeHandle pipe, out uint flags, out uint outBuf, out uint inBuf, out uint maxInstances);

    // --- security descriptors ---
    [LibraryImport("advapi32.dll")]
    public static partial uint GetSecurityInfo(SafeHandle h, int objectType, uint securityInfo, out nint owner, out nint group, out nint dacl, out nint sacl, out nint sd);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertSdToString(nint sd, uint revision, uint securityInfo, out nint text, out uint length);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringToSd(string sddl, uint revision, out nint sd, out uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int infoClass, void* info, uint length, out uint returned);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertSidToStringSid(nint sid, out nint text);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint mem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint h);

    [StructLayout(LayoutKind.Sequential)]
    public struct SecurityAttributes
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    public static string CurrentUserSid()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /* TOKEN_QUERY */, out var token))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        try
        {
            var buffer = stackalloc byte[256];
            if (!GetTokenInformation(token, 1 /* TokenUser */, buffer, 256, out _))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            var sid = *(nint*)buffer; // TOKEN_USER.User.Sid
            if (!ConvertSidToStringSid(sid, out var text))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            try
            {
                return Marshal.PtrToStringUni(text)!;
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static string SddlOf(SafeHandle handle, int objectType = 6 /* SE_KERNEL_OBJECT */)
    {
        const uint OWNER = 0x1, DACL = 0x4;
        var rc = GetSecurityInfo(handle, objectType, OWNER | DACL, out _, out _, out _, out _, out var sd);
        if (rc != 0)
        {
            return $"GetSecurityInfo failed {rc}";
        }

        try
        {
            if (!ConvertSdToString(sd, 1, OWNER | DACL, out var text, out _))
            {
                return $"convert failed {Marshal.GetLastPInvokeError()}";
            }

            try
            {
                return Marshal.PtrToStringUni(text)!;
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            LocalFree(sd);
        }
    }
}

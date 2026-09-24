// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// The named-pipe calls BrowserAI's own processes talk to each other through,
/// and the security descriptor every pipe of ours is created with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw <c>CreateNamedPipeW</c>, never <c>System.IO.Pipes</c>, and the
/// reason is measured.</b> The framework's server stream cannot set
/// <c>PIPE_REJECT_REMOTE_CLIENTS</c> at all, and it costs the NativeAOT server
/// about 120 KB against about 3.5 KB for the raw call: 19,440,640 bytes for the
/// synchronous stream and 19,322,368 for the raw call, over a 19,318,784-byte
/// baseline, one probe each, 2026-09-24
/// ([kb](../../../kb/windows/processes.md#a-per-server-named-pipe-answers-from-memory-and-cannot-tear----measured-2026-09-24)).
/// </para>
/// <para>
/// <b>Three properties are set at creation and nowhere else.</b>
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, so a name somebody else created first
/// is refused and this process never becomes a second instance of a pipe it
/// does not own; <c>PIPE_REJECT_REMOTE_CLIENTS</c>, so a connection from another
/// machine is refused by the kernel; and a DACL whose one allow entry is the
/// current user. The default DACL a pipe gets without one also grants
/// <c>Everyone</c> and <c>ANONYMOUS LOGON</c> read, measured the same day.
/// </para>
/// </remarks>
internal static partial class NamedPipes
{
    /// <summary>Server and client both read and write.</summary>
    public const uint PipeAccessDuplex = 0x00000003;

    /// <summary>
    /// Refuse the creation when any instance of the name already exists,
    /// whoever created it.
    /// </summary>
    public const uint FileFlagFirstPipeInstance = 0x00080000;

    /// <summary>The handle is opened for overlapped I/O.</summary>
    public const uint FileFlagOverlapped = 0x40000000;

    /// <summary>
    /// Refuse a client connecting from another machine. Byte mode, blocking
    /// mode and byte reads are all zero, so this is the whole pipe mode.
    /// </summary>
    public const uint PipeRejectRemoteClients = 0x00000008;

    /// <summary>Read access, for the client end.</summary>
    public const uint GenericRead = 0x80000000;

    /// <summary>Write access, for the client end.</summary>
    public const uint GenericWrite = 0x40000000;

    /// <summary><c>CreateFileW</c>'s disposition for a pipe: it has to exist.</summary>
    public const uint OpenExisting = 3;

    /// <summary>No pipe of that name exists: nobody is serving it.</summary>
    public const int ErrorFileNotFound = 2;

    /// <summary>The wait for a free instance ran out.</summary>
    public const int ErrorSemaphoreTimeout = 121;

    /// <summary>Every instance of the name is busy with another client.</summary>
    public const int ErrorPipeBusy = 231;

    /// <summary>A client connected between the pipe's creation and the wait for one.</summary>
    public const int ErrorPipeConnected = 535;

    /// <summary>
    /// How much of a reply the pipe buffers before a server's write waits for
    /// its client to read.
    /// </summary>
    /// <remarks>
    /// A description with twenty sessions is about 5 KB (4,858 bytes of JSON,
    /// measured 2026-09-24), so a reply fits many times over and a server's
    /// write returns without waiting on the reader.
    /// </remarks>
    public const int OutBufferBytes = 64 * 1024;

    /// <summary>How much of a request the pipe buffers. A request is one short line.</summary>
    public const int InBufferBytes = 4 * 1024;

    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;
    private const int ErrorInsufficientBuffer = 122;
    private const uint SddlRevision1 = 1;

    /// <summary>
    /// Creates the one and only instance of a pipe, readable and writable by the
    /// current user and nobody else, and refused to other machines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One instance, synchronous, and that shape is the whole server.</b>
    /// A second client waits for the first to finish; eight simultaneous
    /// clients against one instance were served one after another, the last in
    /// 1.08 ms, measured 2026-09-24. The handle is not overlapped because the one
    /// thread that serves it blocks on it and does nothing else.
    /// </para>
    /// <para>
    /// ⚠️ <b>It throws the HRESULT Windows answered, so a caller can tell a
    /// name in use from anything else.</b> A second instance under a name this
    /// process or another already serves is <c>0x800700E7</c>,
    /// <c>ERROR_PIPE_BUSY</c>.
    /// </para>
    /// </remarks>
    /// <param name="name">The full pipe name, <c>\\.\pipe\...</c>.</param>
    /// <returns>The server end. The caller owns it.</returns>
    /// <exception cref="IOException">The pipe was not created; <see cref="Exception.HResult"/> says why.</exception>
    /// <exception cref="Win32Exception">The current user's security descriptor could not be built.</exception>
    public static SafeFileHandle CreateServer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var descriptor = CurrentUserOnlyDescriptor();

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = (uint)Unsafe.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = 0,
            };

            var handle = CreateNamedPipeW(
                name,
                PipeAccessDuplex | FileFlagFirstPipeInstance,
                PipeRejectRemoteClients,
                nMaxInstances: 1,
                (uint)OutBufferBytes,
                (uint)InBufferBytes,
                nDefaultTimeOut: 0,
                ref attributes);

            if (handle.IsInvalid)
            {
                // Read before Dispose can replace it with its own.
                var error = Marshal.GetLastPInvokeError();

                handle.Dispose();

                throw new IOException(
                    $"The pipe '{name}' could not be created: {new Win32Exception(error).Message}",
                    HResultFromWin32(error));
            }

            return handle;
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    /// <summary>Waits for a client, or answers at once when one is already connected.</summary>
    /// <param name="server">The server end.</param>
    /// <returns><see langword="true"/> when a client is connected.</returns>
    public static bool WaitForClient(SafeFileHandle server)
    {
        if (ConnectNamedPipe(server, nint.Zero))
        {
            return true;
        }

        // A client that connected between the pipe's creation (or the last
        // disconnect) and this call is connected already; Windows says so with
        // an error code and not with a success.
        return Marshal.GetLastPInvokeError() is ErrorPipeConnected;
    }

    /// <summary>Ends the current client's connection so the instance can serve the next.</summary>
    /// <param name="server">The server end.</param>
    public static void Disconnect(SafeFileHandle server) => _ = DisconnectNamedPipe(server);

    /// <summary>Opens the client end of a pipe, overlapped, for reading and writing.</summary>
    /// <param name="name">The full pipe name.</param>
    /// <param name="error">The Win32 error when the open failed; zero when it did not.</param>
    /// <returns>The client end, or an invalid handle the caller must still dispose.</returns>
    public static SafeFileHandle OpenClient(string name, out int error)
    {
        var handle = CreateFileW(name, GenericRead | GenericWrite, 0, nint.Zero, OpenExisting, FileFlagOverlapped, nint.Zero);

        error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
        return handle;
    }

    /// <summary>Waits for an instance of a busy pipe to become free.</summary>
    /// <param name="name">The full pipe name.</param>
    /// <param name="milliseconds">How long to wait.</param>
    /// <returns><see langword="true"/> when an instance is free to open.</returns>
    public static bool WaitForFreeInstance(string name, uint milliseconds) => WaitNamedPipeW(name, milliseconds);

    /// <summary>The pid of the process serving the other end of a client handle.</summary>
    /// <param name="client">The client end.</param>
    /// <returns>The pid, or <see langword="null"/> when Windows would not say.</returns>
    public static int? ServerProcessIdOf(SafeFileHandle client) =>
        GetNamedPipeServerProcessId(client, out var processId) ? (int)processId : null;

    /// <summary>The pid of the process on the client end of a connected server handle.</summary>
    /// <remarks>
    /// <b>Read for the record, never for a decision.</b> The coordinator logs who
    /// asked it to show its window or to look again; what it does is the same
    /// whoever asked, because the pipe's DACL already decided who may ask.
    /// </remarks>
    /// <param name="server">The server end, connected.</param>
    /// <returns>The pid, or <see langword="null"/> when Windows would not say.</returns>
    public static int? ClientProcessIdOf(SafeFileHandle server) =>
        GetNamedPipeClientProcessId(server, out var processId) ? (int)processId : null;

    /// <summary>The HRESULT a Win32 error code is reported as.</summary>
    /// <param name="error">The Win32 error.</param>
    /// <returns><c>HRESULT_FROM_WIN32(error)</c>.</returns>
    public static int HResultFromWin32(int error) =>
        error <= 0 ? error : unchecked((int)(0x80070000u | (uint)(error & 0xFFFF)));

    /// <summary>
    /// A security descriptor whose DACL is protected and grants the current
    /// user everything and nobody else anything.
    /// </summary>
    /// <remarks>
    /// <b>Built from SDDL naming the token's own user SID</b> --
    /// <c>D:P(A;;GA;;;S-1-5-21-...)</c> -- and not from an alias. <c>OW</c>, the
    /// owner-rights SID, would follow the object's owner, and an elevated
    /// administrator token's default owner is <c>BUILTIN\Administrators</c>, not
    /// the user, so a pipe created by an elevated server would lock out the
    /// same user's unelevated processes.
    /// </remarks>
    /// <returns>A <c>LocalAlloc</c>ed descriptor the caller frees with <c>LocalFree</c>.</returns>
    /// <exception cref="Win32Exception">The token or the conversion failed.</exception>
    private static nint CurrentUserOnlyDescriptor()
    {
        var sddl = $"D:P(A;;GA;;;{CurrentUserSid()})";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SddlRevision1, out var descriptor, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not build the security descriptor for BrowserAI's pipe.");
        }

        return descriptor;
    }

    /// <summary>The current process token's user SID, in its string form.</summary>
    /// <remarks>
    /// <b>Two readers since 2026-09-25</b>: every pipe's DACL, and the per-user logon
    /// task, whose trigger and principal name the installing user by it.
    /// </remarks>
    /// <returns>A SID such as <c>S-1-5-21-...-1001</c>.</returns>
    /// <exception cref="Win32Exception">The token could not be read.</exception>
    internal static string CurrentUserSid()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not open this process's own token.");
        }

        try
        {
            // The first call asks how big the answer is and fails by design.
            if (GetTokenInformation(token, TokenUserClass, [], 0, out var needed)
                || Marshal.GetLastPInvokeError() is not ErrorInsufficientBuffer)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not size this process's token user.");
            }

            // ⚠️ PINNED FOR ITS WHOLE LIFE, because the SID the answer points at
            // lives INSIDE this buffer: TOKEN_USER carries a pointer to bytes a
            // few lines further on in the same allocation, and an ordinary array
            // the collector moved between the two calls would leave that pointer
            // aimed at whatever took its place.
            var buffer = GC.AllocateArray<byte>((int)needed, pinned: true);

            if (!GetTokenInformation(token, TokenUserClass, buffer, needed, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read this process's token user.");
            }

            var user = MemoryMarshal.Read<TokenUser>(buffer);

            if (!ConvertSidToStringSidW(user.Sid, out var text))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not spell this process's user SID.");
            }

            try
            {
                return Marshal.PtrToStringUni(text)
                    ?? throw new Win32Exception("Windows spelled this process's user SID as nothing at all.");
            }
            finally
            {
                _ = LocalFree(text);
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    /// <summary><c>SECURITY_ATTRIBUTES</c>, carrying the pipe's descriptor.</summary>
    /// <remarks>
    /// Checked against Microsoft's own metadata by <c>InteropLayoutTests</c>,
    /// which is this directory's rule for every struct written here.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        /// <summary>The size of this struct, in bytes.</summary>
        public uint Length;

        /// <summary>The <c>LocalAlloc</c>ed descriptor.</summary>
        public nint SecurityDescriptor;

        /// <summary>Zero: the pipe handle is never inherited.</summary>
        public int InheritHandle;
    }

#pragma warning disable CS0649 // Filled in by Windows: read out of the buffer GetTokenInformation wrote, and never assigned in C#.
    /// <summary><c>TOKEN_USER</c>, which is one <c>SID_AND_ATTRIBUTES</c>.</summary>
    /// <remarks>
    /// Checked against Microsoft's own metadata by <c>InteropLayoutTests</c>.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TokenUser
    {
        /// <summary>The user's SID, inside the buffer this struct was read out of.</summary>
        public readonly nint Sid;

        /// <summary>Always zero for a user SID.</summary>
        public readonly uint Attributes;
    }
#pragma warning restore CS0649

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateNamedPipeW(
        string lpName,
        uint dwOpenMode,
        uint dwPipeMode,
        uint nMaxInstances,
        uint nOutBufferSize,
        uint nInBufferSize,
        uint nDefaultTimeOut,
        ref SecurityAttributes lpSecurityAttributes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConnectNamedPipe(SafeFileHandle hNamedPipe, nint lpOverlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DisconnectNamedPipe(SafeFileHandle hNamedPipe);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitNamedPipeW(string lpNamedPipeName, uint nTimeOut);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafeFileHandle pipe, out uint serverProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafeFileHandle pipe, out uint clientProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint LocalFree(nint hMem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        [Out] byte[] tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSidToStringSidW(nint sid, out nint stringSid);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out nint securityDescriptor,
        nint securityDescriptorSize);
}

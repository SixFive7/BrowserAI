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

    /// <summary>
    /// What <c>CreateNamedPipeW</c> answers with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>
    /// when another process already serves the name, measured 2026-10-03 on the
    /// coordinator's pipe as <c>0x80070005</c>.
    /// </summary>
    public const int ErrorAccessDenied = 5;

    /// <summary>The wait for a free instance ran out.</summary>
    public const int ErrorSemaphoreTimeout = 121;

    /// <summary>Every instance of the name is busy with another client.</summary>
    public const int ErrorPipeBusy = 231;

    /// <summary>A client connected between the pipe's creation and the wait for one.</summary>
    public const int ErrorPipeConnected = 535;

    /// <summary>An overlapped operation was started and has not finished yet.</summary>
    public const int ErrorIoPending = 997;

    /// <summary>An overlapped operation was cancelled before it finished.</summary>
    public const int ErrorOperationAborted = 995;

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

    /// <summary>
    /// How many instances a pipe that serves its connections in parallel may have
    /// at once: <c>PIPE_UNLIMITED_INSTANCES</c>, whose value is 255 and which leaves
    /// the number limited only by system resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every instance of one name has to be created with the same number</b>, so
    /// it is a constant and not a choice made per instance.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-03 (previously "<c>PIPE_UNLIMITED_INSTANCES</c>, which
    /// is Windows' own ceiling of 255").</b> 255 is the value of the constant, and
    /// the constant means no ceiling. Microsoft's documentation of
    /// <c>CreateNamedPipeW</c>, read 2026-10-03: <i>"Acceptable values are in the
    /// range 1 through PIPE_UNLIMITED_INSTANCES (255). If this parameter is
    /// PIPE_UNLIMITED_INSTANCES, the number of pipe instances that can be created
    /// is limited only by the availability of system resources."</i> Measured the
    /// same day on Windows 11 10.0.26300 with a probe passing exactly the arguments
    /// <see cref="Create"/> passes: 300, 600, 1,000 and 2,000 instances held
    /// connected at once on one name, and one more caller each, with no refusal;
    /// and the probe's positive control, the same pipe with a cap of 254, had its
    /// 255th instance refused with <c>ERROR_PIPE_BUSY</c>
    /// (<see href="../../../kb/windows/processes.md#a-pipe-created-with-pipe_unlimited_instances-holds-more-than-255-callers----measured-2026-10-03">kb</see>).
    /// </para>
    /// </remarks>
    public const uint UnlimitedInstances = 255;

    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;
    private const int ErrorInsufficientBuffer = 122;
    private const uint SddlRevision1 = 1;

    // ⚠️ CreateParallelServer AND CreateParallelInstance WERE DELETED 2026-10-10, by the
    // maintainer's decision "9 a", with no caller left: they made a server's own pipe
    // and the coordinator's (Q297 b and Q368 a, 2026-10-03), and both went with S a on
    // 2026-10-08. The background's pipe is a stream pipe, and what their remarks held
    // about the first-instance flag and about one more instance is on
    // CreateStreamServer and CreateStreamInstance below.


    /// <summary>
    /// How much a stream pipe buffers in each direction: the session host's, which
    /// carries whole MCP conversations and not one-line requests. The server pipe's
    /// reply buffer, <see cref="OutBufferBytes"/>.
    /// </summary>
    /// <remarks>
    /// <b>Advisory, as every pipe buffer size is, so it takes the size this file
    /// already gives a pipe</b>: a write larger than what the reader has drained waits
    /// for it, which is the backpressure a conversation should have, and a size only
    /// decides how much is in flight before it does. A frame of every size passes
    /// through any buffer; a screenshot's is larger than any of them.
    /// </remarks>
    public static int StreamBufferBytes { get; } = OutBufferBytes;

    /// <summary>
    /// Creates the first instance of a stream pipe: overlapped, parallel, with the
    /// three properties every pipe of ours has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q366 b, 2026-10-03.</b> The session host serves one MCP conversation per
    /// connection for as long as the client lives, and there may be hundreds of them.
    /// A synchronous handle holds a thread in every idle read, so this one is opened
    /// for overlapped I/O: <see cref="WaitForClientAsync(SafeFileHandle, CancellationToken)"/>
    /// accepts on it, and a <see cref="FileStream"/> opened asynchronously over it
    /// reads and writes through the thread pool's completion port.
    /// </para>
    /// <para>
    /// <b>Instances are not capped</b>: <see cref="UnlimitedInstances"/>, which
    /// Microsoft documents as limited only by the system's resources, and which held
    /// 2,000 connected instances of one name when it was measured on 2026-10-03 (see
    /// that constant). So the host's sessions are bounded by nothing here: a
    /// connection is one per running client, and it carries any number of sessions.
    /// </para>
    /// <para>
    /// ⚠️ <b><c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> is the whole of what keeps a second
    /// server off the name</b>, moved here 2026-10-10 by addition from the parallel
    /// pipe's creation, deleted that day: a second process asking for the first
    /// instance of a name that already has one is refused with <c>0x80070005</c>,
    /// <c>ERROR_ACCESS_DENIED</c>, and so is this one when somebody else created the
    /// name first. The background reads that refusal as another background already
    /// serving its roots, and exits.
    /// </para>
    /// </remarks>
    /// <param name="name">The full pipe name, <c>\\.\pipe\...</c>.</param>
    /// <returns>The first server end. The caller owns it.</returns>
    /// <exception cref="IOException">The pipe was not created; <see cref="Exception.HResult"/> says why.</exception>
    /// <exception cref="Win32Exception">The current user's security descriptor could not be built.</exception>
    public static SafeFileHandle CreateStreamServer(string name) =>
        Create(name, FileFlagFirstPipeInstance | FileFlagOverlapped, UnlimitedInstances, StreamBufferBytes);

    /// <summary>
    /// Creates one more instance of a stream pipe this process already serves.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Without <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, and safe only while the
    /// caller still holds an instance of the name.</b> A creation without the flag
    /// joins whatever pipe carries the name, so it is made only by the thread that
    /// holds an instance open at that moment: the name has then never been without one
    /// of ours since <see cref="CreateStreamServer"/> made it, and nobody else can have
    /// created it in between. <i>Moved here 2026-10-10 by addition (previously "on the
    /// rule <c>CreateParallelInstance</c> states"), when that method was deleted.</i>
    /// </remarks>
    /// <param name="name">The full pipe name, exactly as <see cref="CreateStreamServer"/> was given it.</param>
    /// <returns>The new server end. The caller owns it.</returns>
    /// <exception cref="IOException">The instance was not created; <see cref="Exception.HResult"/> says why.</exception>
    /// <exception cref="Win32Exception">The current user's security descriptor could not be built.</exception>
    public static SafeFileHandle CreateStreamInstance(string name) =>
        Create(name, FileFlagOverlapped, UnlimitedInstances, StreamBufferBytes);

    /// <summary>
    /// Waits on an overlapped server end for a client to connect, or for the token, without
    /// a thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The OVERLAPPED lives in a pinned array</b>, so the address Windows writes
    /// the result to never moves while the connect is pending, and an event is its
    /// completion signal. Nothing is bound to the thread pool's completion port here: the
    /// handle is bound once, by the <see cref="FileStream"/> the caller opens over it after
    /// this returns.
    /// </para>
    /// <para>
    /// ⚠️ <b>The event is waited on by the pool's wait thread, and no worker waits</b>, the
    /// maintainer's decision 54, in his words verbatim: <i>"Never block a worker
    /// anywhere."</i> <i>Corrected 2026-10-11 (previously <c>WaitForClient(server,
    /// stop)</c>, which parked its caller's thread in a wait on the event and on a stop
    /// handle, and <c>WaitForClient(server)</c>, which called <c>ConnectNamedPipe</c> with
    /// no OVERLAPPED and so parked its thread until a client came)</i>: the background's
    /// listener held a thread of its own in that wait for the whole of its life.
    /// </para>
    /// <para>
    /// <b>A cancellation cancels the pending connect and waits for Windows to say so</b>
    /// before the array is let go, because memory a pending operation still names is memory
    /// Windows may still write into. A connect that completed in the instant before the
    /// cancel still answers <see langword="true"/>, and the caller drops that client with the
    /// instance.
    /// </para>
    /// </remarks>
    /// <param name="server">An overlapped server end from <see cref="CreateStreamServer"/> or <see cref="CreateStreamInstance"/>.</param>
    /// <param name="cancellationToken">Set when the caller stops listening.</param>
    /// <returns><see langword="true"/> when a client is connected; <see langword="false"/> when the token came first.</returns>
    /// <exception cref="IOException">The connect failed for a reason other than the token.</exception>
    public static async Task<bool> WaitForClientAsync(SafeFileHandle server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var overlapped = GC.AllocateArray<Overlapped>(1, pinned: true);

        using var connected = new ManualResetEvent(initialState: false);

        var signal = connected.SafeWaitHandle;
        var added = false;

        // ⚠️ THE EVENT'S RAW VALUE SITS IN THE OVERLAPPED FOR AS LONG AS THE
        // CONNECT IS PENDING, so the handle is held open by a reference count
        // until Windows has finished with it -- the rule for any raw handle that
        // outlives the expression that read it.
        signal.DangerousAddRef(ref added);

        try
        {
            overlapped[0].EventHandle = signal.DangerousGetHandle();

            if (ConnectNamedPipe(server, ref overlapped[0]))
            {
                return true;
            }

            var error = Marshal.GetLastPInvokeError();

            if (error is ErrorPipeConnected)
            {
                return true;
            }

            if (error is not ErrorIoPending)
            {
                throw new IOException($"Waiting for a client on a pipe failed: {new Win32Exception(error).Message}", HResultFromWin32(error));
            }

            // The connect ends its event whichever way it ends: a client, a failure, or the
            // cancel this registration asks for. The wait is for that and nothing else, so the
            // pinned OVERLAPPED is never let go while Windows still names it.
            await using (cancellationToken.Register(() => _ = CancelIoEx(server, ref overlapped[0])).ConfigureAwait(false))
            {
                _ = await HandleWaits.WhenSignaledAsync(connected, Timeout.InfiniteTimeSpan, CancellationToken.None).ConfigureAwait(false);
            }

            if (GetOverlappedResult(server, ref overlapped[0], out _, bWait: false))
            {
                return true;
            }

            var failed = Marshal.GetLastPInvokeError();

            if (failed is ErrorOperationAborted && cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            throw new IOException($"A client's connect to a pipe failed: {new Win32Exception(failed).Message}", HResultFromWin32(failed));
        }
        finally
        {
            if (added)
            {
                signal.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Creates one pipe for a child's standard stream: the parent's end overlapped, the
    /// child's end synchronous and inheritable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-10-11, the maintainer's decision 54, in his words verbatim: <i>"Never
    /// block a worker anywhere."</i></b> Until that day a child's three streams were
    /// <c>CreatePipe</c>'s anonymous pipes, which Windows makes synchronous at both ends:
    /// every asynchronous read of a child's output was carried out by a pool thread parked in
    /// <c>ReadFile</c> for as long as the child said nothing, two for every child the product
    /// and the suite started (<c>FileStream.IsAsync</c> read <c>False</c> on a redirected
    /// stream of .NET 10.0.12, 2026-10-11). The parent's end here is opened for overlapped
    /// I/O, so a read that waits is a pending I/O request and holds no thread.
    /// </para>
    /// <para>
    /// <b>The child's end is synchronous, and it has to be</b>: a process reads and writes
    /// its standard handles with no OVERLAPPED, which a handle opened for overlapped I/O does
    /// not allow. It is the shape libuv gives every child node starts: an overlapped server
    /// end with one instance, a client end opened without <c>FILE_FLAG_OVERLAPPED</c>, the
    /// reading end with <c>FILE_WRITE_ATTRIBUTES</c> and the writing end with
    /// <c>FILE_READ_ATTRIBUTES</c>, so the child can ask for and set its pipe's state.
    /// </para>
    /// <para>
    /// <b>The pipe has the three properties every pipe of ours has</b>: the first and only
    /// instance of a name nobody else can know, remote clients refused, and a DACL whose one
    /// entry is the current user. The client end is opened at once, so the one instance is
    /// connected before this returns and no other process can take it.
    /// </para>
    /// </remarks>
    /// <param name="childReads">Whether the child reads this pipe: its standard input.</param>
    /// <returns>The parent's end, which the caller owns, and the child's end as a raw, inheritable handle the caller closes once the child has it.</returns>
    /// <exception cref="IOException">The pipe or its client end could not be created.</exception>
    /// <exception cref="Win32Exception">The current user's security descriptor could not be built.</exception>
    internal static (SafeFileHandle Parent, nint Child) CreateChildPipe(bool childReads)
    {
        var name = $@"\\.\pipe\BrowserAI-child-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var parent = Create(name, FileFlagFirstPipeInstance | FileFlagOverlapped, maxInstances: 1, ChildPipeBufferBytes, childReads ? PipeAccessOutbound : PipeAccessInbound);

        try
        {
            var inheritable = new SecurityAttributes
            {
                Length = (uint)Unsafe.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = nint.Zero,
                InheritHandle = 1,
            };

            var child = CreateFileW(
                name,
                childReads ? GenericRead | FileWriteAttributes : GenericWrite | FileReadAttributes,
                0,
                ref inheritable,
                OpenExisting,
                0,
                nint.Zero);

            if (child == InvalidHandleValue)
            {
                var error = Marshal.GetLastPInvokeError();

                throw new IOException(
                    $"The child's end of the pipe '{name}' could not be opened: {new Win32Exception(error).Message}",
                    HResultFromWin32(error));
            }

            return (parent, child);
        }
        catch
        {
            parent.Dispose();
            throw;
        }
    }

    /// <summary>How much a child's standard stream buffers: libuv's 64 KiB for the same pipe.</summary>
    private const int ChildPipeBufferBytes = 64 * 1024;

    /// <summary>The server end only reads: the child writes.</summary>
    private const uint PipeAccessInbound = 0x00000001;

    /// <summary>The server end only writes: the child reads.</summary>
    private const uint PipeAccessOutbound = 0x00000002;

    /// <summary>Lets the child read its pipe's state.</summary>
    private const uint FileReadAttributes = 0x00000080;

    /// <summary>Lets the child set its pipe's state.</summary>
    private const uint FileWriteAttributes = 0x00000100;

    /// <summary>What <c>CreateFileW</c> answers when it could not open.</summary>
    private static readonly nint InvalidHandleValue = -1;

    /// <summary>The one creation every pipe of ours goes through.</summary>
    /// <param name="name">The full pipe name.</param>
    /// <param name="flags"><see cref="FileFlagFirstPipeInstance"/> for a first instance, and <see cref="FileFlagOverlapped"/> for a stream pipe.</param>
    /// <param name="maxInstances">How many instances the name may have at once.</param>
    /// <param name="buffers">How much each direction buffers, or zero for the request-and-answer pipe's own sizes.</param>
    /// <param name="access">Which way the server end moves data: both ways, or one for a child's standard stream.</param>
    /// <returns>The server end. The caller owns it.</returns>
    /// <exception cref="IOException">The pipe was not created; <see cref="Exception.HResult"/> says why.</exception>
    /// <exception cref="Win32Exception">The current user's security descriptor could not be built.</exception>
    private static SafeFileHandle Create(string name, uint flags, uint maxInstances, int buffers = 0, uint access = PipeAccessDuplex)
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
                access | flags,
                PipeRejectRemoteClients,
                maxInstances,
                (uint)(buffers is 0 ? OutBufferBytes : buffers),
                (uint)(buffers is 0 ? InBufferBytes : buffers),
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

    /// <summary>Whether a pipe of that name has an instance free to open, asked without connecting to it.</summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-10-11 (previously <c>WaitForFreeInstance(name, milliseconds)</c>,
    /// which parked its caller for as long as it was given while every instance was busy)</b>,
    /// the maintainer's decision 54. What is left is the question with the shortest bound
    /// <c>WaitNamedPipeW</c> takes, one millisecond: zero would mean the pipe's own default.
    /// A caller that must wait for a free instance waits between two asks, awaiting a delay.
    /// </remarks>
    /// <param name="name">The full pipe name.</param>
    /// <returns><see langword="true"/> when an instance was free to open at the instant of the ask.</returns>
    public static bool HasFreeInstanceNow(string name) => WaitNamedPipeW(name, ShortestPipeWait);

    /// <summary>The shortest bound <c>WaitNamedPipeW</c> takes, in milliseconds: zero is its default wait.</summary>
    private const uint ShortestPipeWait = 1;

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

    /// <summary><c>OVERLAPPED</c>, for the stream pipe's pending connect.</summary>
    /// <remarks>
    /// Checked against Microsoft's own definition by <c>InteropLayoutTests</c>,
    /// whose oracle for this one is <see cref="NativeOverlapped"/>, as it is for
    /// <c>NativeFile</c>'s: CsWin32 refuses to generate the name.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped
    {
        /// <summary>Reserved; the status, when Windows uses it.</summary>
        public nuint Internal;

        /// <summary>Reserved; the byte count, when Windows uses it.</summary>
        public nuint InternalHigh;

        /// <summary>The low half of an offset; unused on a pipe.</summary>
        public uint Offset;

        /// <summary>The high half of an offset; unused on a pipe.</summary>
        public uint OffsetHigh;

        /// <summary>The event Windows sets when the operation finishes.</summary>
        public nint EventHandle;
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
    [LibraryImport("kernel32.dll", EntryPoint = "ConnectNamedPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConnectNamedPipe(SafeFileHandle hNamedPipe, ref Overlapped lpOverlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOverlappedResult(
        SafeFileHandle hFile,
        ref Overlapped lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelIoEx(SafeFileHandle hFile, ref Overlapped lpOverlapped);

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
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        ref SecurityAttributes lpSecurityAttributes,
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

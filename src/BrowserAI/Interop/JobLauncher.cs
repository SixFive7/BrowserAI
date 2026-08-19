// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Starts a child process that is a member of a job object <b>from the instant
/// it exists</b>, with its three standard streams redirected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a P/Invoke and not <c>Process.Start</c>.</b> The correct
/// pattern cannot be expressed in .NET at all: <c>ProcessStartInfo</c> has no
/// creation-flags surface and the framework exposes no job-object API. The
/// obvious substitute — start the process, then call
/// <c>AssignProcessToJobObject</c> — was measured leaking <b>2 escapees</b>,
/// because the child spawns grandchildren in the window before the assignment
/// lands. <c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c> makes membership part of process
/// creation, so that window does not exist rather than being closed afterwards.
/// It also beats <c>CREATE_SUSPENDED</c> → assign → <c>ResumeThread</c>, which
/// measured equally clean but leaks a suspended process if the parent dies
/// mid-sequence.
/// </para>
/// <para>
/// <b>The command line is a <see cref="char"/> buffer, not a string.</b>
/// <c>CreateProcessW</c> mutates the buffer it is given, so passing a managed
/// string is undefined behaviour, and <c>[LibraryImport]</c> does not support
/// <c>StringBuilder</c> — the usual workaround. A writable array is the only
/// shape that is correct in both directions.
/// </para>
/// <para>
/// ⚠️ <b>And the declaration says <i>buffer</i> rather than <i>one char</i>
/// since 2026-08-19 (previously <c>ref char lpCommandLine</c> and
/// <c>ref char lpEnvironment</c>, called as <c>ref commandLine[0]</c>).</b>
/// Microsoft's own Win32 metadata generates a span for this parameter, and
/// <c>ref char</c> was weaker than the vendor's in three ways at once: it
/// carried no length, it made an empty buffer an
/// <see cref="IndexOutOfRangeException"/> at the indexer rather than a
/// <see langword="null"/> the API accepts, and it said nothing about which of
/// the two buffers Windows writes back into. <c>Span&lt;char&gt;</c> for the
/// command line and <c>ReadOnlySpan&lt;char&gt;</c> for the environment now say
/// exactly that: the first is mutated in place — <b>which is our array, because
/// the span is pinned rather than copied</b> — and the second is not.
/// <b>Nothing was known to be wrong with the old shape and nothing changed at
/// the call</b>; it was a signature that presents as a plausible wrong answer
/// rather than as an error, which is the class this repository spends the most
/// on. The invariants the old form relied on and never stated are now stated
/// and asserted: <c>BuildCommandLine</c> and <c>BuildEnvironmentBlock</c> both
/// return a NUL-terminated, never-empty buffer, and
/// <c>InteropLayoutTests.TheTwoBuffersHandedToCreateProcessAreTerminatedAndNeverEmpty</c>
/// is red if either stops.
/// </para>
/// <para>
/// <b>Redirection forces <c>bInheritHandles=TRUE</c></b>, which is precisely why
/// <see cref="JobObject"/> is so careful about its own handle: with these three
/// pipes in play, an inheritable job handle would be duplicated into the child
/// and containment would fail silently. Only the three pipe ends the child needs
/// are left inheritable; ours are cleared before the process exists.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-18 (previously the paragraph above stood alone, and
/// its last sentence was true of one launch and false of the process).</b>
/// <c>bInheritHandles=TRUE</c> duplicates every inheritable handle <i>in the
/// process</i>, and with several sessions opening at once the pipe ends of a
/// launch in flight on another thread are inheritable too — so each child got
/// its siblings' stdout and stderr write ends and held them for its whole life.
/// <c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c> makes the inherited set exact rather
/// than ambient; see <see cref="ProcessAttributeList"/> for what that closed.
/// </para>
/// </remarks>
internal static partial class JobLauncher
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartFUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const int ErrorInsufficientBuffer = 122;

    // ProcThreadAttributeJobList = 13, marked as a thread-creation attribute:
    // 13 | PROC_THREAD_ATTRIBUTE_INPUT (0x00020000).
    private static readonly nuint ProcThreadAttributeJobList = 0x0002000D;

    // ProcThreadAttributeHandleList = 2, same encoding: 2 | 0x00020000. It makes
    // the inherited set EXACT rather than "every inheritable handle in the
    // process", which is what bInheritHandles alone means.
    private static readonly nuint ProcThreadAttributeHandleList = 0x00020002;

    /// <summary>
    /// Starts <paramref name="command"/> inside <paramref name="job"/>.
    /// </summary>
    /// <param name="job">The job the child is created in. Not modified.</param>
    /// <param name="command">The executable's absolute path. Nothing resolves it and no shell sees it.</param>
    /// <param name="arguments">Arguments, quoted for <c>CreateProcessW</c> here.</param>
    /// <param name="workingDirectory">The child's working directory. Required, never inherited.</param>
    /// <param name="environment">The child's complete environment block. It replaces ours rather than adding to it.</param>
    /// <returns>The running child, with its three streams.</returns>
    /// <exception cref="Win32Exception">Windows refused some step of the launch, named in the message.</exception>
    public static LaunchedProcess Start(
        JobObject job,
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(environment);

        var pipes = ChildPipes.Create();

        try
        {
            // The job AND the exact set of handles this child may inherit. The
            // three named here are the three the STARTUPINFO below points at,
            // which the handle-list attribute requires.
            using var attributes = ProcessAttributeList.For(
                job,
                [pipes.ChildStandardInput, pipes.ChildStandardOutput, pipes.ChildStandardError]);

            var commandLine = BuildCommandLine(command, arguments);
            var environmentBlock = BuildEnvironmentBlock(environment);

            var startupInfo = default(StartupInfoEx);
            startupInfo.StartupInfo.Cb = Unsafe.SizeOf<StartupInfoEx>();
            startupInfo.StartupInfo.Flags = StartFUseStdHandles;
            startupInfo.StartupInfo.StdInput = pipes.ChildStandardInput;
            startupInfo.StartupInfo.StdOutput = pipes.ChildStandardOutput;
            startupInfo.StartupInfo.StdError = pipes.ChildStandardError;
            startupInfo.AttributeList = attributes.Pointer;

            // lpApplicationName is set as well as argv[0], so the executable is
            // never resolved through PATH and never re-split at a space -- the
            // failure mode that made `C:\Program Files\...` unlaunchable through
            // the SDK's shell-wrapping transport.
            if (!CreateProcessW(
                    command,
                    commandLine,
                    nint.Zero,
                    nint.Zero,
                    bInheritHandles: true,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment | CreateNoWindow,
                    environmentBlock,
                    workingDirectory,
                    ref startupInfo,
                    out var information))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"CreateProcessW could not start '{command}' in '{workingDirectory}'.");
            }

            // The thread handle is of no use to anyone: the child is running,
            // not suspended, so there is nothing to resume.
            _ = CloseHandle(information.Thread);

            // Our ends of the child's three pipes stay open; the child's ends
            // must not. A stdout read that never sees EOF because the parent
            // still holds the write end is a hang with no error anywhere.
            var (standardInput, standardOutput, standardError) = pipes.ReleaseParentEnds();

#pragma warning disable CA2000 // LaunchedProcess takes ownership of the handle and the three streams, and closes all four; the caller owns it.
            return new LaunchedProcess(
                new SafeProcessHandle(information.Process, ownsHandle: true),
                (int)information.ProcessId,
                standardInput,
                standardOutput,
                standardError);
#pragma warning restore CA2000
        }
        finally
        {
            pipes.Dispose();
        }
    }

    /// <summary>
    /// Quotes an argument list the way <c>CommandLineToArgvW</c> unquotes it,
    /// into a buffer <c>CreateProcessW</c> is allowed to write into.
    /// </summary>
    /// <remarks>
    /// <b>Internal so the two invariants the span signature rests on can be
    /// asserted rather than read.</b> The buffer is never empty and its last
    /// character is NUL — both true by construction here, and neither stated
    /// anywhere until the declaration stopped carrying a length.
    /// </remarks>
    /// <param name="command">The executable, which becomes argv[0].</param>
    /// <param name="arguments">The arguments, quoted here.</param>
    /// <returns>A NUL-terminated buffer Windows may write into.</returns>
    internal static char[] BuildCommandLine(string command, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, command);

        foreach (var argument in arguments)
        {
            AppendArgument(builder, argument);
        }

        // Null-terminated, because the buffer is passed as a pointer rather
        // than as a marshalled string.
        var buffer = new char[builder.Length + 1];
        builder.CopyTo(0, buffer, 0, builder.Length);
        return buffer;
    }

    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (builder.Length is not 0)
        {
            _ = builder.Append(' ');
        }

        if (argument.Length is not 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            _ = builder.Append(argument);
            return;
        }

        _ = builder.Append('"');

        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;

            while (i < argument.Length && argument[i] is '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                // Trailing backslashes are doubled so the closing quote is not
                // escaped by them.
                _ = builder.Append('\\', backslashes * 2);
                break;
            }

            _ = argument[i] is '"'
                ? builder.Append('\\', (backslashes * 2) + 1).Append('"')
                : builder.Append('\\', backslashes).Append(argument[i]);
        }

        _ = builder.Append('"');
    }

    /// <summary>
    /// Builds the <c>name=value\0…\0\0</c> block <c>CREATE_UNICODE_ENVIRONMENT</c>
    /// expects, sorted the way Windows expects to find it.
    /// </summary>
    /// <remarks>
    /// <b>Internal for the same reason as <see cref="BuildCommandLine"/>:</b>
    /// an empty environment still produces one NUL, so the block is never a
    /// zero-length buffer — which would reach Windows as <see langword="null"/>
    /// and mean <i>inherit the parent's environment</i>, the opposite of what an
    /// explicitly empty environment asks for.
    /// </remarks>
    /// <param name="environment">The child's whole environment.</param>
    /// <returns>A double-NUL-terminated buffer Windows only reads.</returns>
    internal static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();

        foreach (var (name, value) in environment.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            _ = builder.Append(name).Append('=').Append(value).Append('\0');
        }

        _ = builder.Append('\0');

        var buffer = new char[builder.Length];
        builder.CopyTo(0, buffer, 0, builder.Length);
        return buffer;
    }

#pragma warning disable CS0649 // Filled in by the kernel; several fields exist only to make the struct the size Windows expects.
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
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }
#pragma warning restore CS0649

    /// <summary>
    /// The attribute list that carries the job <b>and the exact set of handles
    /// the child may inherit</b>, alive for exactly as long as
    /// <c>CreateProcessW</c> needs to read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The handle list was added 2026-08-18; before it there was one
    /// attribute and the inherited set was "everything".</b>
    /// <c>bInheritHandles: true</c> duplicates <b>every inheritable handle in the
    /// process</b> into the child, not just this launch's three — and
    /// <see cref="ChildPipes.Create"/> makes both ends of all three pipes
    /// inheritable before clearing the parent's, because
    /// <c>SetHandleInformation</c> cannot add inheritance to a handle created
    /// without it. With several sessions in one process and no lock anywhere
    /// between <c>ChildPipes.Create</c> and <c>CreateProcessW</c>, thread B's
    /// <c>node.exe</c> — and therefore B's whole Chromium tree — inherited
    /// thread A's stdout and stderr write ends and held them for B's entire life.
    /// </para>
    /// <para>
    /// <b>That is the hang this file already documents, closed for the parent and
    /// left open for a sibling</b>: <i>"a stdout read that never sees EOF because
    /// the parent still holds the write end is a hang with no error anywhere."</i>
    /// It also broke the graceful close — A's stdin never reaching EOF means
    /// every teardown falls through to the 5 s timeout and the job kill — and it
    /// put <b>our own stdout handle</b>, the JSON-RPC channel, inside a Chromium
    /// tree, which is a route to stdout no banned-symbol analyzer can see. One
    /// attribute closes all three. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-processes.md),
    /// finding 5.
    /// </para>
    /// <para>
    /// <b>The constraints the list imposes are all already met.</b> Every handle
    /// in it must be inheritable and must be valid;
    /// <c>bInheritHandles</c> must stay <c>TRUE</c>; and if
    /// <c>STARTUPINFO</c> names standard handles they must be in the list. The
    /// three passed here are exactly those three.
    /// </para>
    /// </remarks>
    private sealed class ProcessAttributeList : IDisposable
    {
        /// <summary>The job, then the handle list.</summary>
        private const int AttributeCount = 2;

        private readonly nint _jobHandleStorage;
        private readonly nint _handleListStorage;

        private ProcessAttributeList(nint list, nint jobHandleStorage, nint handleListStorage)
        {
            Pointer = list;
            _jobHandleStorage = jobHandleStorage;
            _handleListStorage = handleListStorage;
        }

        /// <summary>The attribute list, for the one call that reads it.</summary>
        public nint Pointer { get; }

        public static ProcessAttributeList For(JobObject job, IReadOnlyList<nint> inheritable)
        {
            // The documented two-call shape: the first call fails with
            // ERROR_INSUFFICIENT_BUFFER and reports the size.
            var size = nuint.Zero;

            if (InitializeProcThreadAttributeList(nint.Zero, AttributeCount, 0, ref size))
            {
                throw new Win32Exception("InitializeProcThreadAttributeList unexpectedly succeeded while sizing the buffer.");
            }

            var error = Marshal.GetLastPInvokeError();

            if (error is not ErrorInsufficientBuffer)
            {
                throw new Win32Exception(error, "Could not size the process attribute list.");
            }

            var list = Marshal.AllocHGlobal((nint)size);
            var jobStorage = nint.Zero;
            var handleStorage = nint.Zero;

            try
            {
                if (!InitializeProcThreadAttributeList(list, AttributeCount, 0, ref size))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not initialise the process attribute list.");
                }

                // Both buffers have to stay at a fixed address until
                // CreateProcessW returns -- the attribute list stores pointers,
                // not copies -- so they live on the native heap rather than as
                // pinned managed locals.
                jobStorage = Marshal.AllocHGlobal(nint.Size);
                Marshal.WriteIntPtr(jobStorage, job.Handle.DangerousGetHandle());

                if (!UpdateProcThreadAttribute(list, 0, ProcThreadAttributeJobList, jobStorage, (nuint)nint.Size, nint.Zero, nint.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not put the job object on the process attribute list.");
                }

                var handleListBytes = nint.Size * inheritable.Count;
                handleStorage = Marshal.AllocHGlobal(handleListBytes);

                for (var i = 0; i < inheritable.Count; i++)
                {
                    Marshal.WriteIntPtr(handleStorage, i * nint.Size, inheritable[i]);
                }

                if (!UpdateProcThreadAttribute(list, 0, ProcThreadAttributeHandleList, handleStorage, (nuint)handleListBytes, nint.Zero, nint.Zero))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "Could not restrict the child's inherited handles to this launch's own three. Every handle in the list must be inheritable and valid.");
                }

                GC.KeepAlive(job);
                return new ProcessAttributeList(list, jobStorage, handleStorage);
            }
            catch
            {
                Free(jobStorage);
                Free(handleStorage);
                DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(list);
                throw;
            }
        }

        public void Dispose()
        {
            DeleteProcThreadAttributeList(Pointer);
            Marshal.FreeHGlobal(Pointer);
            Free(_jobHandleStorage);
            Free(_handleListStorage);
        }

        private static void Free(nint storage)
        {
            if (storage != nint.Zero)
            {
                Marshal.FreeHGlobal(storage);
            }
        }
    }

    /// <summary>
    /// The three pipe pairs, with the inheritance flags that make redirection
    /// safe rather than a containment hole.
    /// </summary>
    private sealed class ChildPipes : IDisposable
    {
        private nint _parentStandardInput;
        private nint _parentStandardOutput;
        private nint _parentStandardError;
        private nint _childStandardInput;
        private nint _childStandardOutput;
        private nint _childStandardError;

        public nint ChildStandardInput => _childStandardInput;

        public nint ChildStandardOutput => _childStandardOutput;

        public nint ChildStandardError => _childStandardError;

        public static ChildPipes Create()
        {
            var pipes = new ChildPipes();

            try
            {
                // bInheritHandle on the security attributes makes BOTH ends
                // inheritable; the parent's end is cleared immediately
                // afterwards. Doing it in that order is what makes the child's
                // end inheritable at all -- SetHandleInformation cannot add
                // inheritance to a handle created without it.
                var attributes = new SecurityAttributes
                {
                    Length = (uint)Unsafe.SizeOf<SecurityAttributes>(),
                    InheritHandle = 1,
                };

                (pipes._childStandardInput, pipes._parentStandardInput) = CreateOnePipe(ref attributes, parentEndIsRead: false);
                (pipes._parentStandardOutput, pipes._childStandardOutput) = CreateOnePipe(ref attributes, parentEndIsRead: true);
                (pipes._parentStandardError, pipes._childStandardError) = CreateOnePipe(ref attributes, parentEndIsRead: true);

                return pipes;
            }
            catch
            {
                pipes.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Hands the parent's three ends to the caller as streams and forgets
        /// them, so <see cref="Dispose"/> closes only the child's.
        /// </summary>
        public (Stream StandardInput, Stream StandardOutput, Stream StandardError) ReleaseParentEnds()
        {
#pragma warning disable CA2000 // The FileStream takes ownership of the SafeFileHandle and closes it; the caller owns the stream.
            var input = new FileStream(new SafeFileHandle(_parentStandardInput, ownsHandle: true), FileAccess.Write, bufferSize: 1);
            _parentStandardInput = nint.Zero;

            var output = new FileStream(new SafeFileHandle(_parentStandardOutput, ownsHandle: true), FileAccess.Read, bufferSize: 1);
            _parentStandardOutput = nint.Zero;

            var error = new FileStream(new SafeFileHandle(_parentStandardError, ownsHandle: true), FileAccess.Read, bufferSize: 1);
            _parentStandardError = nint.Zero;
#pragma warning restore CA2000

            return (input, output, error);
        }

        public void Dispose()
        {
            Close(ref _parentStandardInput);
            Close(ref _parentStandardOutput);
            Close(ref _parentStandardError);
            Close(ref _childStandardInput);
            Close(ref _childStandardOutput);
            Close(ref _childStandardError);
        }

        private static (nint Read, nint Write) CreateOnePipe(ref SecurityAttributes attributes, bool parentEndIsRead)
        {
            if (!CreatePipe(out var read, out var write, ref attributes, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create a pipe for the child's standard streams.");
            }

            var parentEnd = parentEndIsRead ? read : write;

            if (!SetHandleInformation(parentEnd, HandleFlagInherit, 0))
            {
                var error = Marshal.GetLastPInvokeError();
                _ = CloseHandle(read);
                _ = CloseHandle(write);
                throw new Win32Exception(error, "Could not clear inheritance on our end of a pipe.");
            }

            return (read, write);
        }

        private static void Close(ref nint handle)
        {
            if (handle != nint.Zero)
            {
                _ = CloseHandle(handle);
                handle = nint.Zero;
            }
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string? lpApplicationName,
        Span<char> lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        ReadOnlySpan<char> lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(
        nint lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nuint lpSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        nint lpAttributeList,
        uint dwFlags,
        nuint attribute,
        nint lpValue,
        nuint cbSize,
        nint lpPreviousValue,
        nint lpReturnSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(
        out nint hReadPipe,
        out nint hWritePipe,
        ref SecurityAttributes lpPipeAttributes,
        uint nSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint hObject, uint dwMask, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;
using BrowserAI.Tests.Harness;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests;

/// <summary>
/// The two kinds of standard handle the clients hand a stdio server, rebuilt the way each
/// client's source makes them, and what <c>ReOpenFile</c> with <c>FILE_FLAG_OVERLAPPED</c>
/// answers for each.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-12 for the maintainer's 78 c</b>, in his words verbatim:
/// <i>"78-82 your recommendation"</i>, which is a test that rebuilds both clients' pipe kinds
/// and fails the day <c>ReOpenFile</c> with <c>FILE_FLAG_OVERLAPPED</c> stops refusing them.
/// That refusal is why <c>Protocol/StdioChannel</c> keeps a thread for each direction (78 a):
/// the day it fails, the relay's input and output could be read and written overlapped, and
/// the two kept waits are worth looking at again.
/// </para>
/// <para>
/// <b>The two kinds, as measured 2026-10-11</b> (kb, windows/processes.md): Claude Code's is
/// the client end of a libuv named pipe, one instance, duplex, 64 KiB each way, opened
/// without <c>FILE_FLAG_OVERLAPPED</c>; Codex's is the client end of an unnamed pipe Rust's
/// <c>child_pipe</c> makes, one instance, one way, opened <c>FILE_SYNCHRONOUS_IO_NONALERT</c>.
/// A disk file shared for reading and writing is the positive control: it reopens, so a
/// refusal here is the call's answer and not an arm that cannot see a success.
/// </para>
/// </remarks>
internal sealed partial class StdioPipeKindsTests
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint Synchronize = 0x00100000;
    private const uint WriteDac = 0x00040000;
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint PipeAccessDuplex = 3;
    private const uint FileCreate = 2;
    private const uint FileSynchronousIoNonAlert = 0x20;
    private const uint FileNonDirectoryFile = 0x40;
    private const int ErrorPipeBusy = 231;

    /// <summary>
    /// <c>ReOpenFile</c> with <c>FILE_FLAG_OVERLAPPED</c> refuses Claude Code's and Codex's
    /// standard handle kinds, for reading and for writing, with <c>ERROR_PIPE_BUSY</c>, and
    /// reopens a disk file.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ReOpenFileStillRefusesBothClientsPipeKindsOverlapped()
    {
        using var scratch = await ScratchDirectory.CreateAsync("stdio-pipe-kinds");

        var file = Path.Combine(scratch.Path, "a-disk-file.txt");
        await File.WriteAllTextAsync(file, "the positive control");

        using (var control = CreateFile(file, GenericRead, FileShareRead | FileShareWrite, nint.Zero, OpenExisting, 0, nint.Zero))
        {
            await Assert.That(control.IsInvalid).IsFalse();

            using var reopened = ReOpenFile(control, GenericRead, FileShareRead | FileShareWrite, FileFlagOverlapped);

            await Assert.That(reopened.IsInvalid).IsFalse().Because("a disk file did not reopen overlapped, so this arm cannot see a reopen that works");
        }

        foreach (var (kind, access) in new[]
        {
            ("Claude Code's standard input, a libuv named pipe's client end", GenericRead),
            ("Claude Code's standard output, a libuv named pipe's client end", GenericWrite),
        })
        {
            var (server, client) = LibuvPipe();

            using (server)
            using (client)
            {
                await Assert.That(RefusalOf(client, access)).IsEqualTo(ErrorPipeBusy).Because(Changed(kind));
            }
        }

        foreach (var (kind, oursReadable, access) in new[]
        {
            ("Codex's standard input, a Rust child pipe's end", false, GenericRead),
            ("Codex's standard output, a Rust child pipe's end", true, GenericWrite),
        })
        {
            var (ours, theirs) = RustChildPipe(oursReadable);

            using (ours)
            using (theirs)
            {
                await Assert.That(RefusalOf(theirs, access)).IsEqualTo(ErrorPipeBusy).Because(Changed(kind));
            }
        }
    }

    /// <summary>What a reopen failing for a pipe kind means, for the failure message.</summary>
    private static string Changed(string kind) =>
        $"{kind} no longer answers a reopen with FILE_FLAG_OVERLAPPED by ERROR_PIPE_BUSY. If it reopened, the relay's standard input and output "
        + "could be read and written overlapped, and Protocol/StdioChannel's two kept threads are worth looking at again (the maintainer's 78 a and c)";

    /// <summary>
    /// The error <c>ReOpenFile</c> with <c>FILE_FLAG_OVERLAPPED</c> answers for a handle, or
    /// zero when it reopened it.
    /// </summary>
    private static int RefusalOf(SafeFileHandle handle, uint access)
    {
        using var reopened = ReOpenFile(handle, access, FileShareRead | FileShareWrite, FileFlagOverlapped);

        return reopened.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
    }

    /// <summary>
    /// A named pipe made the way libuv's <c>uv__create_pipe_pair</c> makes a child's
    /// standard handle under Bun's flags for descriptors 0 to 2: an overlapped server end
    /// with one instance and a client end opened with no <c>FILE_FLAG_OVERLAPPED</c>.
    /// </summary>
    private static (SafeFileHandle Server, SafeFileHandle Client) LibuvPipe()
    {
        var name = string.Create(CultureInfo.InvariantCulture, $@"\\?\pipe\uv\{Guid.NewGuid():N}-{Environment.ProcessId}");
        var server = CreateNamedPipe(name, PipeAccessDuplex | FileFlagOverlapped | WriteDac | FileFlagFirstPipeInstance, 0, 1, 65536, 65536, 0, nint.Zero);

        if (server.IsInvalid)
        {
            throw new IOException($"the server end of '{name}' could not be made: error {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}");
        }

        var client = CreateFile(name, GenericRead | GenericWrite | WriteDac, 0, nint.Zero, OpenExisting, 0, nint.Zero);

        if (client.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            server.Dispose();

            throw new IOException($"the client end of '{name}' could not be opened: error {error.ToString(CultureInfo.InvariantCulture)}");
        }

        return (server, client);
    }

    /// <summary>
    /// An unnamed pipe made the way Rust's <c>child_pipe</c> makes a child's standard handle:
    /// <c>NtCreateNamedPipeFile</c> under the named-pipe device with an empty name, its own
    /// end overlapped, and the child's end opened relative to it
    /// <c>FILE_SYNCHRONOUS_IO_NONALERT</c>.
    /// </summary>
    /// <param name="oursReadable">Whether the parent's end reads, which makes the child's end its standard output.</param>
    private static (SafeFileHandle Ours, SafeFileHandle Theirs) RustChildPipe(bool oursReadable)
    {
        var device = Marshal.StringToHGlobalUni(@"\Device\NamedPipe\");
        var deviceName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        var emptyName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());

        try
        {
            Marshal.StructureToPtr(new UnicodeString { Length = (ushort)(@"\Device\NamedPipe\".Length * 2), MaximumLength = (ushort)(@"\Device\NamedPipe\".Length * 2), Buffer = device }, deviceName, fDeleteOld: false);
            Marshal.StructureToPtr(default(UnicodeString), emptyName, fDeleteOld: false);

            var root = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), ObjectName = deviceName };
            Require(NtOpenFile(out var rootHandle, Synchronize | GenericRead, ref root, out _, FileShareRead | FileShareWrite, FileSynchronousIoNonAlert), "the named-pipe device");

            using var pipes = new SafeFileHandle(rootHandle, ownsHandle: true);

            var created = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = rootHandle, ObjectName = emptyName };
            var timeout = -50L * 10_000;
            Require(
                NtCreateNamedPipeFile(out var oursHandle, Synchronize | (oursReadable ? GenericRead : GenericWrite), ref created, out _, oursReadable ? FileShareWrite : FileShareRead, FileCreate, 0, 0, 0, 0, 1, 65536, 65536, ref timeout),
                "the parent's end");

            var ours = new SafeFileHandle(oursHandle, ownsHandle: true);

            try
            {
                var relative = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = oursHandle, ObjectName = emptyName };
                Require(
                    NtOpenFile(out var theirsHandle, Synchronize | (oursReadable ? GenericWrite | FileReadAttributes : GenericRead), ref relative, out _, 0, FileNonDirectoryFile | FileSynchronousIoNonAlert),
                    "the child's end");

                return (ours, new SafeFileHandle(theirsHandle, ownsHandle: true));
            }
            catch
            {
                ours.Dispose();
                throw;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(emptyName);
            Marshal.FreeHGlobal(deviceName);
            Marshal.FreeHGlobal(device);
        }
    }

    /// <summary>Throws naming what could not be made when an NT call did not succeed.</summary>
    private static void Require(int status, string what)
    {
        if (status is not 0)
        {
            throw new IOException($"{what} could not be made: NTSTATUS 0x{status.ToString("X8", CultureInfo.InvariantCulture)}");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize, uint defaultTimeout, nint securityAttributes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint securityAttributes, uint disposition, uint flags, nint template);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle ReOpenFile(SafeFileHandle original, uint access, uint share, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtOpenFile(out nint handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock status, uint share, uint options);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtCreateNamedPipeFile(
        out nint handle,
        uint access,
        ref ObjectAttributes attributes,
        out IoStatusBlock status,
        uint share,
        uint disposition,
        uint options,
        uint pipeType,
        uint readMode,
        uint completionMode,
        uint maxInstances,
        uint inboundQuota,
        uint outboundQuota,
        ref long defaultTimeout);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public nint Status;
        public nint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public nint RootDirectory;
        public nint ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }
}

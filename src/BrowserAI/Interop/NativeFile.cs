// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Opening a file so that concurrent processes can append to it without losing
/// each other's bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured 2026-08-16, and it is why this file exists.</b> Eight processes
/// each writing 25 records through <c>new FileStream(path, FileMode.Append,
/// FileAccess.Write, FileShare.ReadWrite)</c> lost <b>70 of the 200</b>. .NET's
/// append mode seeks to the end <i>at open</i> and then tracks the position
/// itself, so two writers that opened at the same length overwrite each other's
/// records. Nothing reports it: every write returns success and the file grows.
/// </para>
/// <para>
/// The fix is the platform's own guarantee rather than a lock. A handle opened
/// with <c>FILE_APPEND_DATA</c> and <b>without</b> <c>FILE_WRITE_DATA</c> has
/// its writes placed at the end of the file by the filesystem, atomically, no
/// matter how many other handles are open. Requesting <c>GENERIC_WRITE</c>
/// silently forfeits it, which is why the access mask below is spelled out one
/// flag at a time.
/// </para>
/// <para>
/// This matters because the design has ~100 concurrent BrowserAI processes
/// sharing one process log. A lock would work and would also make logging able
/// to block, which is the one thing the sink may never do.
/// </para>
/// </remarks>
internal static partial class NativeFile
{
    private const uint FileAppendData = 0x0004;
    private const uint Synchronize = 0x00100000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenAlways = 4;
    private const uint FileAttributeNormal = 0x80;

    /// <summary>
    /// Opens (or creates) a file whose writes are always appended atomically.
    /// </summary>
    public static SafeFileHandle OpenForAtomicAppend(string path)
    {
        var handle = CreateFileW(
            path,
            // FILE_APPEND_DATA only. Adding FILE_WRITE_DATA -- which is what
            // GENERIC_WRITE expands to -- turns the guarantee off.
            FileAppendData | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenAlways,
            FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open '{path}' for append.");
        }

        return handle;
    }

    /// <summary>Appends a whole buffer, looping until every byte is written.</summary>
    public static void Append(SafeFileHandle handle, ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (!WriteFile(handle, ref Unsafe.AsRef(in bytes[0]), (uint)bytes.Length, out var written, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not append to the log file.");
            }

            if (written is 0)
            {
                throw new IOException("A log append wrote zero bytes, which would loop forever.");
            }

            bytes = bytes[(int)written..];
        }
    }

    // LibraryImport rather than DllImport, because it is Microsoft's documented
    // first recommendation for .NET 7+ and the marshalling it generates is
    // readable C# rather than a stub the toolchain hides.
    //
    // Corrected 2026-08-17 (previously: "DllImport relies on runtime IL-stub
    // generation, which NativeAOT does not do"). That is false on Windows: ILC
    // compiles DllImport stubs ahead of time. Measured on SDK 10.0.400 / ILC
    // 10.0.11 with a 38-declaration probe that published NativeAOT with zero
    // warnings and ran correctly. The rule is unchanged; the reason was wrong.
    // System32 only. Without it the loader searches the application directory
    // first, and a DLL dropped beside the binary would be loaded in preference
    // to the real kernel32 (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(
        SafeFileHandle hFile,
        ref byte lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);
}

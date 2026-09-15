// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Opening a log file so that concurrent processes append to it under one
/// cross-process lock, and taking that lock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured 2026-08-16, and it is why this file exists at all.</b> Eight
/// processes each writing 25 records through <c>new FileStream(path,
/// FileMode.Append, FileAccess.Write, FileShare.ReadWrite)</c> lost <b>70 of the
/// 200</b>. .NET's append mode seeks to the end <i>at open</i> and then tracks
/// the position itself, so two writers that opened at the same length overwrite
/// each other's records. Nothing reports it: every write returns success and the
/// file grows.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-24 (previously <c>OpenForAtomicAppend</c> +
/// <c>Append</c>: a handle opened <c>FILE_APPEND_DATA</c> <i>without</i>
/// <c>FILE_WRITE_DATA</c>, whose writes the filesystem placed at the end
/// atomically "no matter how many other handles are open", plus a completion
/// loop that reissued <c>WriteFile</c> on a short write).</b> That guarantee is
/// <b>per <c>WriteFile</c> call</b>, so the loop's second call landed after
/// whatever another of the ~100 processes had written in between: the record was
/// torn and interleaved and every call returned success
/// ([review](../../../docs/reviews/2026-08-18-adversarial-processes.md), finding
/// 9). The machinery is deleted rather than repaired. Under the lock below there
/// is no size bound to exceed and nothing can interleave, so a short write is
/// resumed at the right offset by construction and
/// <see cref="System.IO.RandomAccess"/>'s own loop is correct where ours was
/// not. The <b>lost-record</b> measurement above still stands and is still what
/// forbids <c>FileMode.Append</c>.
/// </para>
/// <para>
/// <b>A file lock rather than a named object, for the reason
/// <c>Runtime.MaintenanceLock</c> already gives.</b> The kernel releases it
/// however the holder dies — clean exit, <c>TerminateProcess</c>, a bugcheck —
/// whereas a named semaphore's count is not restored, so one crashed writer
/// would wedge every BrowserAI on the machine until the next reboot. It has no
/// thread affinity either, which a named mutex does.
/// </para>
/// <para>
/// <b>The locked range is one byte beyond any possible end of file, and that is
/// what keeps readers out of it.</b> A byte-range lock on Windows is enforced
/// against <c>ReadFile</c> as well as <c>WriteFile</c>, so locking <c>[0,
/// ∞)</c> — the obvious spelling — would fail every concurrent reader of the log
/// with <c>ERROR_LOCK_VIOLATION</c>, and *a reader must never be locked out of
/// the log it came to read*. Locking a region past the end of a file is
/// explicitly legal and costs nothing, so the region is a pure semaphore that
/// overlaps no byte anybody reads.
/// </para>
/// </remarks>
internal static partial class NativeFile
{
    /// <summary>
    /// The byte the writers' lock is taken on: one past the largest offset a
    /// file can have, so it can never overlap a record.
    /// </summary>
    private const long GateOffset = long.MaxValue;

    private const uint LockfileExclusiveLock = 0x00000002;

    /// <summary>
    /// Opens (or creates) a log file for appending under
    /// <see cref="TakeGate"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>FileAccess.Write</c> is <c>GENERIC_WRITE</c>, which
    /// <c>LockFileEx</c> requires</b> — it refuses a handle carrying neither
    /// <c>GENERIC_READ</c> nor <c>GENERIC_WRITE</c>, which is exactly what the
    /// old <c>FILE_APPEND_DATA</c>-only mask was.
    /// </para>
    /// <para>
    /// ⚠️ <b><paramref name="shareDelete"/> is a decision each caller makes and
    /// states, not a default.</b> Granting it lets anything unlink the file out
    /// from under every writer, after which each write succeeds into an unlinked
    /// file object and nothing fails
    /// ([review](../../../docs/reviews/2026-08-18-adversarial-processes.md),
    /// finding 10); withholding it makes the file undeletable while any handle
    /// is open. Neither is free, and which cost is the right one depends on what
    /// the file is — see the two call sites, which each say.
    /// </para>
    /// </remarks>
    /// <param name="path">The file. Created when it is not there.</param>
    /// <param name="shareDelete">Whether other processes may delete or rename the file while this handle is open.</param>
    /// <returns>The handle. The caller owns it.</returns>
    public static SafeFileHandle OpenForLockedAppend(string path, bool shareDelete) =>
        File.OpenHandle(
            path,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            shareDelete
                ? FileShare.Read | FileShare.Write | FileShare.Delete
                : FileShare.Read | FileShare.Write);

    /// <summary>
    /// Takes the cross-process write gate on a log file, waiting for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It blocks, and that is the point.</b> Every writer of this file reads
    /// the file's length, stamps the instant and writes inside the same claim,
    /// so write order and timestamp order coincide and the file is sorted by
    /// construction. The hold is one <c>WriteFile</c> of a few hundred bytes,
    /// and the kernel releases the claim however the holder dies, so there is no
    /// shape here in which a waiter waits forever.
    /// </para>
    /// <para>
    /// <c>LockFileEx</c> rather than <c>LockFile</c> or
    /// <see cref="FileStream"/>'s <c>Lock</c>, both of which fail immediately on
    /// a conflict and would need a retry loop — which is a spin under exactly
    /// the contention the lock exists for.
    /// </para>
    /// </remarks>
    /// <param name="handle">The open log handle.</param>
    /// <returns>The claim. Dispose it to release.</returns>
    /// <exception cref="Win32Exception">The lock could not be taken.</exception>
    public static WriteGate TakeGate(SafeFileHandle handle) => new(handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockFileEx(
        SafeFileHandle hFile,
        uint dwFlags,
        uint dwReserved,
        uint nNumberOfBytesToLockLow,
        uint nNumberOfBytesToLockHigh,
        ref Overlapped lpOverlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnlockFile(
        SafeFileHandle hFile,
        uint dwFileOffsetLow,
        uint dwFileOffsetHigh,
        uint nNumberOfBytesToUnlockLow,
        uint nNumberOfBytesToUnlockHigh);

    /// <summary>
    /// <c>OVERLAPPED</c>, carrying nothing but the offset of the byte the gate
    /// is taken on.
    /// </summary>
    /// <remarks>
    /// <b>Hand-written rather than <see cref="NativeOverlapped"/>, and not by
    /// preference.</b> <c>LibraryImport</c> refuses to marshal the framework's
    /// struct without <c>DisableRuntimeMarshallingAttribute</c> on the whole
    /// assembly (<c>SYSLIB1051</c>), which is a project-wide change to satisfy
    /// one parameter. This is five blittable fields, and it is checked against
    /// Microsoft's own metadata by <c>InteropLayoutTests</c> — which is this
    /// directory's rule for any struct written here, and the only mechanism that
    /// can see a field that slid four bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped
    {
        /// <summary>Reserved; the status, when Windows uses it.</summary>
        public nuint Internal;

        /// <summary>Reserved; the byte count, when Windows uses it.</summary>
        public nuint InternalHigh;

        /// <summary>The low half of the offset the operation applies to.</summary>
        public uint Offset;

        /// <summary>The high half of that offset.</summary>
        public uint OffsetHigh;

        /// <summary>The completion event. Zero here: the handle is synchronous.</summary>
        public nint EventHandle;
    }

    /// <summary>One held write gate.</summary>
    /// <remarks>
    /// A <see langword="ref"/> struct so that it cannot outlive the
    /// <c>using</c> that took it, be boxed, or be captured by a closure that
    /// releases it on another thread.
    /// </remarks>
    internal readonly ref struct WriteGate
    {
        private readonly SafeFileHandle _handle;

        /// <summary>Takes the gate, waiting for whoever holds it.</summary>
        /// <param name="handle">The open log handle.</param>
        /// <exception cref="Win32Exception">The lock could not be taken.</exception>
        public WriteGate(SafeFileHandle handle)
        {
            _handle = handle;

            var overlapped = default(Overlapped);
            overlapped.Offset = unchecked((uint)GateOffset);
            overlapped.OffsetHigh = unchecked((uint)(GateOffset >> 32));

            if (!LockFileEx(handle, LockfileExclusiveLock, 0, 1, 0, ref overlapped))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not take the log file's write gate.");
            }
        }

        /// <summary>Releases the gate.</summary>
        /// <exception cref="Win32Exception">The unlock failed, which would leave the file gated forever.</exception>
        public void Dispose()
        {
            if (!UnlockFile(
                _handle,
                unchecked((uint)GateOffset),
                unchecked((uint)(GateOffset >> 32)),
                1,
                0))
            {
                // Never swallowed. A gate that was taken and not released stalls
                // every other BrowserAI on the machine at its next record, and
                // the sink's own catch closes the handle -- which is what
                // actually releases it -- only because this said so.
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not release the log file's write gate.");
            }
        }
    }
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Storage;

/// <summary>
/// A <c>sqlite3*</c> connection, closed by whoever lets go of it last.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see cref="SafeHandle"/> rather than an <see cref="IntPtr"/>, and the
/// reason is the reason this repository has a house rule about it.</b> A raw
/// pointer roots nothing, stops no concurrent disposal, and cannot tell a
/// caller that the thing it names has already gone. Handing the handle type
/// itself to every <c>[LibraryImport]</c> declaration makes the source
/// generator take a reference count for the duration of each call, which is the
/// property <c>HouseRuleTests.EveryRawHandleThatOutlivesItsExpressionIsRefCounted</c>
/// exists to keep — and here it is free, because nothing in this layer ever
/// reads the raw value out.
/// </para>
/// <para>
/// ⚠️ <b>Zero is the only invalid value, so this cannot be
/// <c>SafeHandleZeroOrMinusOneIsInvalid</c>.</b> That base type also rejects
/// <c>-1</c>, which for a Win32 handle is <c>INVALID_HANDLE_VALUE</c> and for a
/// heap pointer is an address like any other. Nothing would go wrong today;
/// what would go wrong is the day an allocator hands back that address and a
/// live connection reads as invalid.
/// </para>
/// </remarks>
internal sealed class SqliteDatabaseHandle : SafeHandle
{
    /// <summary>Creates an owning, invalid handle for the marshaller to fill in.</summary>
    /// <remarks>
    /// Public and parameterless because that is what the <c>[LibraryImport]</c>
    /// source generator requires of an <c>out</c> handle parameter: it
    /// constructs the instance itself before the call and sets the value after.
    /// </remarks>
    public SqliteDatabaseHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <inheritdoc />
    /// <remarks>
    /// <b><c>close_v2</c>, and the result is deliberately discarded.</b> That
    /// entry point never refuses: given a connection with statements still
    /// live it marks it a zombie and finishes when the last one goes, so the
    /// connection is always released and <see langword="true"/> is always the
    /// honest answer. Its older sibling <c>sqlite3_close</c> answers
    /// <c>SQLITE_BUSY</c> in that case and leaks, which is why it is not used:
    /// finalizers run in an order nothing guarantees, so the refusing variant
    /// would leak on exactly the path nobody exercises.
    /// </remarks>
    protected override bool ReleaseHandle()
    {
        _ = Sqlite.CloseV2(handle);
        return true;
    }
}

/// <summary>
/// A <c>sqlite3_stmt*</c> prepared statement, finalized by whoever lets go of it.
/// </summary>
/// <remarks>
/// See <see cref="SqliteDatabaseHandle"/> for why this is a
/// <see cref="SafeHandle"/> and why <c>IntPtr.Zero</c> is the only invalid
/// value.
/// </remarks>
internal sealed class SqliteStatementHandle : SafeHandle
{
    /// <summary>Creates an owning, invalid handle for the marshaller to fill in.</summary>
    public SqliteStatementHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b><c>sqlite3_finalize</c> answers with the last <c>step</c>'s error
    /// rather than with this call's</b>, and it destroys the statement either
    /// way. Reporting that stale code as a failed release would say the
    /// resource is still held when it is not, so the result is discarded here
    /// and read where it belongs — at the step that produced it.
    /// </remarks>
    protected override bool ReleaseHandle()
    {
        _ = Sqlite.FinalizeStatement(handle);
        return true;
    }
}

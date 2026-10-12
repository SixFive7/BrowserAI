// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;
using BrowserAI.Storage;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A session store's statements as the suite makes them: each handed to the store's own
/// thread and awaited.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-12 with the maintainer's 82 b</b>, which put every statement on the
/// store's thread and on <see cref="SessionStoreConnection"/>, whose members refuse any
/// other. Each member here is one item, so an arm reads as it did before the change.
/// </remarks>
internal static class SessionStores
{
    /// <summary>Every statement, oldest first.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The statements.</returns>
    public static Task<IReadOnlyList<StoredStatement>> StatementsAsync(this SessionStore store) =>
        store.RunAsync(connection => connection.Statements());

    /// <summary>Log rows, oldest first.</summary>
    /// <param name="store">The store.</param>
    /// <param name="skip">How many rows to pass over.</param>
    /// <param name="take">How many to return; a negative number means all of them.</param>
    /// <returns>The rows.</returns>
    public static Task<IReadOnlyList<StoredLogEntry>> LogAsync(this SessionStore store, long skip = 0, long take = -1) =>
        store.RunAsync(connection => connection.Log(skip, take));

    /// <summary>How many log rows there are.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The count.</returns>
    public static Task<long> LogLengthAsync(this SessionStore store) =>
        store.RunAsync(connection => connection.LogLength());

    /// <summary>Appends one log row.</summary>
    /// <param name="store">The store.</param>
    /// <param name="at">When.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="why">Why.</param>
    /// <param name="outcome">Its outcome.</param>
    /// <returns>The row's id.</returns>
    public static Task<long> AppendLogAsync(this SessionStore store, string at, string tool, string why, string outcome) =>
        store.RunAsync(connection => connection.AppendLog(at, tool, why, outcome));

    /// <summary>Settles a log row.</summary>
    /// <param name="store">The store.</param>
    /// <param name="id">The row.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="settledAt">When.</param>
    /// <param name="failure">The failure payload, or <see langword="null"/>.</param>
    /// <returns>Whether a row was updated.</returns>
    public static Task<bool> SettleAsync(this SessionStore store, long id, string outcome, string settledAt, byte[]? failure) =>
        store.RunAsync(connection => connection.Settle(id, outcome, settledAt, failure));

    /// <summary>Appends one statement.</summary>
    /// <param name="store">The store.</param>
    /// <param name="statement">The statement.</param>
    /// <returns>The append.</returns>
    public static Task AppendAsync(this SessionStore store, StoredStatement statement) =>
        store.RunAsync(connection => connection.Append(statement));

    /// <summary>Writes one acquisition's statements, all of them or none.</summary>
    /// <param name="store">The store.</param>
    /// <param name="statements">The statements.</param>
    /// <returns>The write.</returns>
    public static Task RecordAcquisitionAsync(this SessionStore store, IReadOnlyList<StoredStatement> statements) =>
        store.RunAsync(connection => connection.RecordAcquisition(statements));

    /// <summary>The journal mode.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The mode.</returns>
    public static Task<string> JournalModeAsync(this SessionStore store) =>
        store.RunAsync(connection => connection.JournalMode());

    /// <summary>The busy timeout, in milliseconds.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The value SQLite reports.</returns>
    public static Task<long> BusyTimeoutInMillisecondsAsync(this SessionStore store) =>
        store.RunAsync(connection => connection.BusyTimeoutInMilliseconds());

    /// <summary>The schema version recorded in the file.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The version.</returns>
    public static Task<long> RecordedSchemaVersionAsync(this SessionStore store) =>
        store.RunAsync(connection => connection.RecordedSchemaVersion());

    /// <summary>The record, assembled from everything the store holds.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The record.</returns>
    public static Task<SessionRecord> RecordAsync(this SessionStore store) =>
        store.RunAsync(SessionRecordReader.Read);

    /// <summary>One page of the log, as the record reader gives it.</summary>
    /// <param name="store">The store.</param>
    /// <param name="skip">How many rows to pass over.</param>
    /// <param name="take">How many to take; negative means all of them.</param>
    /// <returns>The rows.</returns>
    public static Task<IReadOnlyList<SessionLogRow>> LogRowsAsync(this SessionStore store, long skip, long take) =>
        store.RunAsync(connection => SessionRecordReader.Log(connection, skip, take));
}

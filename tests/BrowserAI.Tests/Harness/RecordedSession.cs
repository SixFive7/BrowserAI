// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;
using BrowserAI.Storage;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Reads a session's log the way a third process does: a read-only open of
/// <c>browserai.data</c>, with nothing taken.
/// </summary>
/// <remarks>
/// <para>
/// <b>One helper because every test that asks does it the same way.</b> The log
/// is not on <see cref="SessionRecord"/> — deliberately, because a session with
/// ten thousand calls in it would otherwise have to be held in memory to answer
/// <i>what browser is this</i> — so a test that wants the rows opens the store.
/// A second spelling of that open is how two tests come to ask different
/// questions about one file.
/// </para>
/// <para>
/// ⚠️ <b>Reading is not side-effect-free and this is the shape that shows
/// it.</b> Against a crashed holder's uncheckpointed write-ahead log the open
/// recovers the log and leaves a <c>-shm</c> beside the store. Any test that
/// asserts on a session directory's file list after calling this is asserting on
/// a list this call may have changed.
/// </para>
/// </remarks>
internal static class RecordedSession
{
    /// <summary>Every row of one session's log, oldest first.</summary>
    /// <param name="session">The session directory.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<SessionLogRow> LogOf(SessionPath session)
    {
        ArgumentNullException.ThrowIfNull(session);

        using var store = SessionStore.OpenForReading(session.DataFile);

        return SessionRecordReader.Log(store, 0, -1);
    }

    /// <summary>Every row of one session's log, oldest first.</summary>
    /// <param name="directory">The session directory, as a path.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<SessionLogRow> LogOf(string directory) => LogOf(SessionPath.Resolve(directory));
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Logging;

/// <summary>
/// Somewhere a formatted log record can be written.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>There is one, and there were two until 2026-08-26</b> — <i>previously
/// "There are two: the machine-wide rolling process log, and one file per session
/// beside its <c>browserai.json</c> … the two files are read side by side while
/// somebody works out what a session did and what the machine was doing to it"</i>.
/// The per-session log file is gone: everything it carried is on stderr, which the
/// session's logging stack already wrote to at every level, and what the session
/// itself did is rows in <c>browserai.data</c>. The seam stays because it is what
/// keeps the record format in <see cref="FileLoggerProvider"/> rather than in a
/// sink — a second formatter would drift — and because a second sink is the shape
/// this interface exists to make cheap.
/// </para>
/// <para>
/// <b>An implementation stamps the write time itself</b>, from inside whatever
/// gate it writes under —
/// <see cref="FileLoggerProvider.WriteStamp(System.DateTime)"/> is the one
/// spelling, and it is the sink's because only the sink knows the instant the
/// bytes actually went down.
/// </para>
/// </remarks>
internal interface ILogSink
{
    /// <summary>Appends one already-formatted record. Never throws.</summary>
    /// <param name="record">The record, without its line terminator.</param>
    void Write(string record);
}

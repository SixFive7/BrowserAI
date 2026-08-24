// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Logging;

/// <summary>
/// Somewhere a formatted log record can be written.
/// </summary>
/// <remarks>
/// <para>
/// There are two: the machine-wide rolling process log, and one file per session
/// beside its <c>browserai.json</c>. The seam exists so both get the <b>same</b>
/// record format from <see cref="FileLoggerProvider"/> — a second formatter would
/// drift, and the two files are read side by side while somebody works out what a
/// session did and what the machine was doing to it.
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

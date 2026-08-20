// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Logging;

/// <summary>
/// Somewhere a formatted log record can be written.
/// </summary>
/// <remarks>
/// There are two: the machine-wide rolling process log, and one file per session
/// beside its <c>browserai.json</c>. The seam exists so both get the <b>same</b>
/// record format from <see cref="FileLoggerProvider"/> — a second formatter would
/// drift, and the first thing to drift would be the scope that names the session,
/// which is the only thing making ~100 interleaved processes readable.
/// </remarks>
internal interface ILogSink
{
    /// <summary>Appends one already-formatted record. Never throws.</summary>
    /// <param name="record">The record, without its line terminator.</param>
    void Write(string record);
}

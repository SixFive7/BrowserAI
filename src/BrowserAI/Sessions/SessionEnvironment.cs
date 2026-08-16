// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Runtime;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// Everything <see cref="SessionManager"/> needs from the process around it,
/// behind one record.
/// </summary>
/// <remarks>
/// The seam exists so the suite can point a manager at a scratch app root: the
/// session index is machine-wide state, and a test that wrote into the real one
/// would put its throwaway directories into a developer's own
/// <c>browserai_list</c> and leave them there.
/// </remarks>
internal sealed record SessionEnvironment
{
    /// <summary>Where the index, the browsers and this run's own directory live.</summary>
    public required IAppPaths Paths { get; init; }

    /// <summary>Where <c>node.exe</c> and <c>cli.js</c> live.</summary>
    public required PayloadLayout Payload { get; init; }

    /// <summary>
    /// This run's own directory, which is where a session's <b>generated
    /// config</b> is written.
    /// </summary>
    /// <remarks>
    /// Never inside the session directory. <c>lock.json</c> and the session log
    /// are the only files at a session's root; a third would make the one that
    /// matters missable, and a config file is a per-run artifact rather than
    /// part of the session's durable state.
    /// </remarks>
    public required string InstanceDirectory { get; init; }

    /// <summary>Opens one session's logging stack: its own file, plus the process log and stderr.</summary>
    public required Func<string, LogLevel, SessionLogging> OpenSessionLog { get; init; }
}

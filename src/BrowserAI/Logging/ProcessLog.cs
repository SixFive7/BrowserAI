// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Logging;

/// <summary>
/// The process-wide logging stack: an <see cref="ILoggerFactory"/> whose every
/// record reaches stderr and a rolling file, and neither of which can reach
/// stdout.
/// </summary>
/// <remarks>
/// <para>
/// <b>stderr is enforced by configuration, not by rule.</b>
/// <c>LogToStandardErrorThreshold</c> is set to the lowest level that exists,
/// so no severity has a path to stdout at all — not <c>Information</c>, not
/// <c>Trace</c>, and not a line added in three months by someone who never read
/// a design document. The banned-symbol list stops direct console writes that
/// never reach a logger; this stops everything that does. Both are kept,
/// because only one of them is enforcement.
/// </para>
/// <para>
/// <b>The file is outside <c>current\</c>.</b> Applying an update is itself one
/// of the events that logs here, so the destination has to outlive the update
/// by construction. See <see cref="IAppPaths"/>.
/// </para>
/// <para>
/// <b>Disposal closes the file, and it takes an explicit call to do it.</b> The
/// obvious reading — the factory disposes its providers, the provider owns the
/// sink — is false, and was measured false: see <see cref="Dispose"/>.
/// </para>
/// <para>
/// <b>There is no flush-on-exit hook, and that is deliberate.</b>
/// <see cref="RollingFileWriter"/> buffers nothing: each record is one
/// unbuffered write against a synchronous handle, so a record that has been
/// logged is already on disk. A <c>ProcessExit</c> handler here would be a
/// mechanism that only looks like one, and the property it appears to provide
/// would silently stop being true the day the sink gained a queue.
/// </para>
/// </remarks>
internal sealed class ProcessLog : IDisposable
{
    private readonly RollingFileWriter _writer;
    private readonly UnhandledExceptionEventHandler _onUnhandled;
    private int _disposed;

    private ProcessLog(ILoggerFactory factory, RollingFileWriter writer)
    {
        Factory = factory;
        _writer = writer;

        var log = factory.CreateLogger("BrowserAI.Crash");

        // The crash path. An unhandled exception is not guaranteed to unwind
        // the stack, so no `finally` and no Dispose can be relied on here --
        // this handler is the only thing that runs, and the record it writes is
        // durable the moment it is written.
        _onUnhandled = (_, e) => CrashLog.Unhandled(log, e.IsTerminating, e.ExceptionObject as Exception);

        AppDomain.CurrentDomain.UnhandledException += _onUnhandled;
    }

    /// <summary>The factory every component takes its <see cref="ILogger"/> from.</summary>
    public ILoggerFactory Factory { get; }

    /// <summary>The file records are currently appended to, for the suite to read back.</summary>
    public string? CurrentFile => _writer.CurrentFile;

    /// <summary>Builds the stack over the given paths.</summary>
    public static ProcessLog Create(IAppPaths paths, LogLevel minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var writer = new RollingFileWriter(paths.LogDirectory);

        try
        {
            var factory = LoggerFactory.Create(builder =>
            {
                _ = builder.SetMinimumLevel(minimumLevel);
                _ = builder.AddProvider(new FileLoggerProvider(writer));
                _ = builder.AddConsole(options =>
                    // Everything, at every level, to stderr. This single line is
                    // what makes stdout unreachable from the logging stack.
                    options.LogToStandardErrorThreshold = LogLevel.Trace);
            });

            return new ProcessLog(factory, writer);
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds one session's logging stack: its own file beside <c>lock.json</c>,
    /// the machine-wide process log, and stderr.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three destinations rather than a redirect.</b> A session's records
    /// belong in the session directory, where whoever is debugging that session
    /// will look — and equally in the process log, because the interesting
    /// question is often "what were the other ninety-five doing". The scope
    /// <see cref="Sessions.SessionLock"/> pushes is what keeps the second
    /// readable.
    /// </para>
    /// <para>
    /// <b><paramref name="minimumLevel"/> is per session, which is the whole
    /// point of the <c>debug</c> argument.</b> Turning diagnostics on for the
    /// session that is misbehaving does not drown the ninety-five that are not,
    /// and it needs no restart, no environment variable and no second
    /// registration.
    /// </para>
    /// </remarks>
    /// <param name="sessionDirectory">The session directory the log file goes in.</param>
    /// <param name="minimumLevel">The level for this session alone.</param>
    /// <returns>The session's logging stack. Dispose it when the session ends.</returns>
    public SessionLogging OpenSessionLog(string sessionDirectory, LogLevel minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(sessionDirectory);

        var file = new SessionLogFile(sessionDirectory);

        try
        {
            // CA2000 is disabled for this one statement and nothing else.
            // Ownership moves into the returned SessionLogging, whose Dispose
            // calls Factory.Dispose -- the same transfer Create() above makes and
            // the rule accepts there. The difference the rule reacts to is that
            // this is an instance method rather than a static factory of the type
            // being returned, which is not a difference in ownership.
#pragma warning disable CA2000
            var factory = LoggerFactory.Create(builder =>
            {
                _ = builder.SetMinimumLevel(minimumLevel);
                _ = builder.AddProvider(new FileLoggerProvider(file));

                // Not owned: the process log outlives every session in it, and
                // disposing this provider would close the machine-wide handle
                // the moment the first session ended.
                _ = builder.AddProvider(new FileLoggerProvider(_writer, ownsSink: false));
                _ = builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            });
#pragma warning restore CA2000

            return new SessionLogging(factory, file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-16 (previously "The factory disposes its
    /// providers, and the file provider owns the writer, so this closes the
    /// handle too" — and it did not).</b> Measured twice on
    /// <c>Microsoft.Extensions.Logging</c> 10.0.x, by planting a provider that
    /// counts its own disposals: <b><c>LoggerFactory.Create(b =&gt;
    /// b.AddProvider(instance))</c> followed by <c>factory.Dispose()</c> calls
    /// that provider's <c>Dispose</c> <i>zero</i> times</b> — a container never
    /// disposes an instance it did not create — so the rolling file handle
    /// survived every disposal, and opening the log afterwards with
    /// <c>FileShare.None</c> was refused. It cost nothing in <c>Main</c>, where
    /// the process exits immediately afterwards, and it was found the first time
    /// something short-lived opened one and then read it back: a Velopack hook.
    /// <see cref="SessionLogging"/> was already immune, because it disposes its
    /// file explicitly after the factory — that belt-and-braces second call was
    /// the mechanism, not the redundancy it read as
    /// ([kb](../../../kb/windows/processes.md#interop-and-the-toolchain)).
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= _onUnhandled;

        // The factory first, so nothing can still be routing records at a sink
        // that is about to close -- then the writer, explicitly, because the
        // factory does not reach it. See the remarks: this is measured rather
        // than defensive.
        Factory.Dispose();
        _writer.Dispose();
    }
}

/// <summary>One session's logging stack, and the file it owns.</summary>
/// <param name="factory">Where every component of that session takes its logger from.</param>
/// <param name="file">The session's own log file.</param>
internal sealed class SessionLogging(ILoggerFactory factory, SessionLogFile file) : IDisposable
{
    /// <summary>The factory for this session.</summary>
    public ILoggerFactory Factory { get; } = factory;

    /// <summary>The file this session's records are appended to.</summary>
    public string Path { get; } = file.Path;

    /// <inheritdoc />
    public void Dispose()
    {
        // The factory first: it disposes the provider that owns the file.
        Factory.Dispose();
        file.Dispose();
    }
}

/// <summary>Source-generated log messages for the crash path.</summary>
internal static partial class CrashLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Critical,
        Message = "Unhandled exception reached the process boundary. Terminating: {IsTerminating}.")]
    public static partial void Unhandled(ILogger logger, bool isTerminating, Exception? exception);
}

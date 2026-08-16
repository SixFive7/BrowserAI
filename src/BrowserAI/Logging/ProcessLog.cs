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

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= _onUnhandled;

        // The factory disposes its providers, and the file provider owns the
        // writer, so this closes the handle too.
        Factory.Dispose();
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

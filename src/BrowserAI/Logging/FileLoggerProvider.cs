// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Logging;

/// <summary>
/// Routes <see cref="ILogger"/> records into an <see cref="ILogSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scope support is wired from the start because the process log is written by
/// ~100 processes at once and, from step 10, by N sessions inside each: every
/// record carries its session so the interleaving is readable at the moment it
/// matters.
/// </para>
/// <para>
/// <b>Ownership of the sink is a constructor argument rather than an
/// assumption.</b> A session's logger factory carries two of these — one over
/// its own <see cref="SessionLogFile"/>, which it owns, and one over the
/// machine-wide <see cref="RollingFileWriter"/>, which
/// <see cref="ProcessLog"/> owns and outlives it. Disposing that second one
/// would take the process log down with the first session that ended.
/// </para>
/// </remarks>
/// <param name="sink">Where records go.</param>
/// <param name="ownsSink">Whether disposing this provider disposes the sink.</param>
internal sealed class FileLoggerProvider(ILogSink sink, bool ownsSink = true) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopes;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(sink, categoryName, () => _scopes);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsSink && sink is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed class FileLogger(ILogSink sink, string category, Func<IExternalScopeProvider?> scopes) : ILogger
    {
        private static readonly int ProcessId = Environment.ProcessId;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            scopes()?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var record = new StringBuilder(256)
                .Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(Abbreviate(logLevel))
                .Append("  pid=")
                .Append(ProcessId.ToString(CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(category);

            if (eventId.Id is not 0)
            {
                record.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(']');
            }

            scopes()?.ForEachScope(
                static (scope, builder) => builder.Append("  {").Append(scope).Append('}'),
                record);

            record.Append("  ").Append(formatter(state, exception));

            if (exception is not null)
            {
                // The whole exception, indented, on following lines. A log that
                // records only the message of an exception has recorded the
                // half that is easiest to guess.
                record.Append('\n').Append("    ").Append(exception.ToString().Replace("\n", "\n    ", StringComparison.Ordinal));
            }

            sink.Write(record.ToString());
        }

        /// <summary>Fixed-width level names, so the columns line up in a text editor.</summary>
        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "?????",
        };
    }
}

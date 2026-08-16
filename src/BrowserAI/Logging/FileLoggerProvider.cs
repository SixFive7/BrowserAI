// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Logging;

/// <summary>
/// Routes <see cref="ILogger"/> records into <see cref="RollingFileWriter"/>.
/// </summary>
/// <remarks>
/// Scope support is wired from the start because the process log is written by
/// ~100 processes at once and, from step 10, by N sessions inside each: every
/// record carries its session so the interleaving is readable at the moment it
/// matters. Nothing pushes a scope yet, and the plumbing costs one interface.
/// </remarks>
internal sealed class FileLoggerProvider(RollingFileWriter writer) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopes;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(writer, categoryName, () => _scopes);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    /// <inheritdoc />
    public void Dispose() => writer.Dispose();

    private sealed class FileLogger(RollingFileWriter writer, string category, Func<IExternalScopeProvider?> scopes) : ILogger
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

            writer.Write(record.ToString());
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

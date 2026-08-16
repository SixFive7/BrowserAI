// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests.Harness;

/// <summary>One captured log record.</summary>
/// <param name="Category">The logger's category.</param>
/// <param name="Level">The severity it was written at.</param>
/// <param name="EventId">Its event id.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception attached to it, if any.</param>
internal sealed record LogRecord(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

/// <summary>
/// Captures every log record for assertions.
/// </summary>
/// <remarks>
/// <b>Observability is a feature requirement here, so it is asserted like
/// one.</b> Every defect in this project's founding table reported healthy
/// while broken, and the answer to that is a log line that says what happened —
/// which is only a mechanism if something fails when it stops being written.
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<LogRecord> _records = [];

    /// <summary>Everything logged so far, in order.</summary>
    public IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_records)
            {
                return [.. _records];
            }
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    /// <summary>Whether any record's message contains the given text.</summary>
    /// <param name="text">The text to look for.</param>
    /// <returns>Whether a record carries it.</returns>
    public bool Logged(string text) =>
        Records.Any(record => record.Message.Contains(text, StringComparison.Ordinal));

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);

    private void Add(LogRecord record)
    {
        lock (_records)
        {
            _records.Add(record);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            provider.Add(new LogRecord(category, logLevel, eventId, formatter(state, exception), exception));
        }
    }
}

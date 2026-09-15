// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Logging;

/// <summary>
/// Routes <see cref="ILogger"/> records into an <see cref="ILogSink"/>, and owns
/// the one record format both sinks write.
/// </summary>
/// <remarks>
/// <para>
/// Scope support is wired from the start because a session's records carry the
/// session that produced them, and the same factory serves N sessions inside one
/// process.
/// </para>
/// <para>
/// <b>Two times per record, and neither replaces the other.</b> The leading
/// column is when the record was <i>written</i>, taken by the sink inside the
/// file's write gate immediately before the bytes go down — so write order and
/// timestamp order coincide and the file is sorted <b>by construction</b> rather
/// than by anybody sorting it. <c>made=</c> is when the record was
/// <i>created</i>, stamped here. The two differ only by however long a writer
/// waited at the gate, which is the one thing a reader investigating contention
/// wants and the one thing a single timestamp cannot show.
/// </para>
/// <para>
/// <b>The writer is <c>pid=&lt;n&gt;@&lt;createdFileTime&gt;</c>, never a bare
/// pid.</b> Windows reuses pids, and the machine-wide log outlives the processes
/// in it by thirty days — so a bare pid in a month-old record eventually names a
/// stranger. The pair is this repository's standing identity for a process:
/// it is what <see cref="ProcessLiveness.IsAlive(int, long)"/> takes, and it is
/// spelled here exactly as <c>browserai.lock</c> spells
/// <c>processCreatedFileTime</c>, so a log line and a lock record name the same
/// writer with the same characters.
/// </para>
/// <para>
/// ⚠️ <b><c>@0</c> means this process could not read its own creation time</b>,
/// which is a pair a reader must not feed to a liveness check: it would answer
/// <i>not running</i> for a process that is. It has never been observed —
/// <c>GetProcessTimes</c> on the current-process pseudo-handle has no documented
/// failure — and it is spelled rather than thrown because a logger that cannot
/// construct itself takes the process with it.
/// </para>
/// </remarks>
/// <param name="sink">Where records go.</param>
internal sealed class FileLoggerProvider(ILogSink sink) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopes;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(sink, categoryName, () => _scopes);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    /// <summary>
    /// The leading column of a record: the instant it reached the file.
    /// </summary>
    /// <remarks>
    /// <b>Called from inside a sink's write gate and nowhere else.</b> Read
    /// anywhere earlier it would be a creation time wearing the sort key's
    /// column, and the ordering claim above would quietly stop being true.
    /// </remarks>
    /// <param name="written">The instant, UTC.</param>
    /// <returns>The column, separator included.</returns>
    public static string WriteStamp(DateTime written) =>
        written.ToString("O", CultureInfo.InvariantCulture) + "  ";

    /// <inheritdoc />
    public void Dispose()
    {
        if (sink is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed class FileLogger(ILogSink sink, string category, Func<IExternalScopeProvider?> scopes) : ILogger
    {
        private static readonly int ProcessId = Environment.ProcessId;
        private static readonly long ProcessCreatedFileTime = ReadOwnCreationTime();

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
                .Append("made=")
                .Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(Abbreviate(logLevel))
                .Append("  pid=")
                .Append(ProcessId.ToString(CultureInfo.InvariantCulture))
                .Append('@')
                .Append(ProcessCreatedFileTime.ToString(CultureInfo.InvariantCulture))
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

        /// <summary>This process's creation time, or 0 if it could not be read.</summary>
        /// <remarks>See the type's remarks for what <c>@0</c> tells a reader.</remarks>
        /// <returns>A Windows FILETIME, or 0.</returns>
        private static long ReadOwnCreationTime()
        {
            try
            {
                return ProcessLiveness.CreationTimeOfThisProcess();
            }
            catch (Win32Exception)
            {
                return 0;
            }
        }
    }
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Routes every <see cref="ILogger"/> record into the running test's own
/// output.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failing test in this layer is otherwise mute.</b> Nothing here is a
/// process, so there is no stderr to read afterwards and no log file on disk;
/// the product's account of what it did exists only in an
/// <see cref="ILoggerFactory"/> the test constructed. Attaching it to the test
/// context means a red run carries the proxy's own narration — the negotiated
/// revision, the dropped frame, the transport that was already disconnected —
/// instead of an assertion message on its own.
/// </para>
/// <para>
/// Writes are per-test rather than process-wide, so this stays correct with the
/// whole layer running in parallel.
/// </para>
/// </remarks>
internal sealed class TUnitLoggerProvider : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new TUnitLogger(categoryName);

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);

    private sealed class TUnitLogger(string category) : ILogger
    {
        /// <summary>
        /// Serialises writes across every logger in the process. A
        /// <see cref="TextWriter"/> is not safe to lock on — it has weak
        /// identity, so a lock taken on it can be taken by unrelated code
        /// through the same object — and the writer is TUnit's rather than
        /// ours.
        /// </summary>
        private static readonly Lock OutputLock = new();

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

            var writer = TestContext.Current?.OutputWriter;

            if (writer is null)
            {
                // Logging outside a test — a class-level hook, or a background
                // task that outlived the test that started it. Dropping the
                // record is right; throwing from a logger would turn a
                // diagnostic into the failure.
                return;
            }

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"[{logLevel}] {category}[{eventId.Id}] {formatter(state, exception)}");

            lock (OutputLock)
            {
                writer.WriteLine(line);

                if (exception is not null)
                {
                    writer.WriteLine(exception.ToString());
                }
            }
        }
    }
}

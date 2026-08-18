// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

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
    /// First-run browser provisioning: what <c>init</c> starts and never waits
    /// for, and what <c>browserai_reinstall_browser</c> drives.
    /// </summary>
    /// <remarks>
    /// <b>Required rather than defaulted, and that is on purpose.</b> A
    /// provisioner conjured at the call site would point at whatever browsers
    /// root happened to be in scope — and the one thing that must never happen in
    /// a test is a 203.8 MB download nobody asked for. Handing it in makes the
    /// browsers root and the installer an explicit decision of whoever builds the
    /// environment.
    /// </remarks>
    public required BrowserProvisioner Provisioner { get; init; }

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

    /// <summary>
    /// How long a session's browser may sit unused before
    /// <see cref="BrowserIdleTimer"/> closes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A seam for one reason: the shipped period is ten minutes, and no test
    /// may wait that long.</b> Everything the timer decides — reset on every
    /// forwarded call, never fire while a call is outstanding, close once and
    /// stay disarmed — is the same code at 200 ms as at ten minutes, so the suite
    /// drives it in milliseconds and the product never sees this set.
    /// </para>
    /// <para>
    /// <b>The default is the shipped value, and it is asserted separately.</b> A
    /// test-friendly period leaking into the product would be invisible: a
    /// browser closing every twenty seconds is silently relaunched by the next
    /// call, so nothing would ever go red. <c>BrowserIdleTimerTests</c> therefore
    /// asserts both the constant and that nothing in <c>src/</c> assigns this
    /// property.
    /// </para>
    /// </remarks>
    public TimeSpan BrowserIdlePeriod { get; init; } = BrowserIdleTimer.DefaultIdlePeriod;

    /// <summary>
    /// The clock <see cref="BrowserIdleTimer"/> reads and schedules against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A seam of exactly the same kind as <see cref="BrowserIdlePeriod"/>, and
    /// it is here because shortening the period was not enough.</b> Every claim
    /// the timer makes is a claim about <i>when</i>, and a test that establishes
    /// one by letting real time pass is measuring the machine's scheduler rather
    /// than the product. Measured 2026-08-17 with the suite running all 416 tests
    /// at once: one in-process round trip took <b>1.51 s and 2.27 s</b> against
    /// an 800 ms period, so the driving test concluded — correctly — that the
    /// session had gone idle, and went red five times in twenty runs while the
    /// product was right every time.
    /// </para>
    /// <para>
    /// <b>The default is the real clock, and that it stays the real clock is
    /// asserted rather than assumed.</b> A manual clock leaking into a shipped
    /// build would stop the only timer in the product from ever firing, and
    /// nothing would go red: a browser that is never closed looks exactly like a
    /// browser that is being used. <c>BrowserIdleTimerTests</c> asserts that
    /// nothing under <c>src/</c> assigns this, which is the same guard, written
    /// the same way, that <see cref="BrowserIdlePeriod"/> already carries.
    /// </para>
    /// </remarks>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// Starts one session's <c>@playwright/mcp</c> child and completes the
    /// handshake with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A seam of exactly the same kind as <see cref="OpenSessionLog"/>, and it
    /// exists for one test that could not otherwise be written.</b> The
    /// routing has to be driven across sessions of
    /// <i>different</i> modes at once, and doing that against real children costs
    /// three node processes and a browser per assertion — which is slow enough
    /// that the concurrency would be tested once rather than at every level of
    /// contention. Substituted, the same product code runs against in-process
    /// doubles in milliseconds.
    /// </para>
    /// <para>
    /// The default is the real thing, and the suite proves the real thing
    /// separately against the published binary — so this cannot become the only
    /// path anybody exercises.
    /// </para>
    /// </remarks>
    public Func<ChildProcessOptions, ILoggerFactory, string, Func<JsonRpcNotification, CancellationToken, ValueTask>, CancellationToken, Task<ChildConnection>> ConnectChild { get; init; } =
        static (options, loggerFactory, idPrefix, relay, cancellationToken) =>
            ChildConnection.ConnectAsync(
                new DirectStdioClientTransport(options, loggerFactory),
                loggerFactory,
                idPrefix,
                relay,
                cancellationToken);

    /// <summary>
    /// How much room the volume holding a path has, or <see langword="null"/> if
    /// it cannot be asked in one call.
    /// </summary>
    /// <remarks>
    /// <b>O(1), and only ever O(1).</b> A directory walk here would make the check
    /// slower than the failure it prevents, and <c>init</c> is on the hot path of
    /// every session. It is a seam so the suite can trigger
    /// <see cref="SessionErrors.InsufficientDisk"/> through the
    /// real refusal path rather than by asserting a literal — a full volume is not
    /// something a test can arrange, and a row nobody can reach is documentation
    /// rather than behaviour.
    /// </remarks>
    public Func<string, long?> FreeBytesOn { get; init; } = static path =>
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(path) ?? path).AvailableFreeSpace;
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A network share, most often. Reported as unknown rather than as
            // zero, because zero would refuse every session on it.
            return null;
        }
    };
}

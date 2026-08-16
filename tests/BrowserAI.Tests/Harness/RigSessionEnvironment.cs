// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The <see cref="SessionEnvironment"/> the in-process rig hands
/// <c>BrowserProxy</c>, and the session children it stands up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each session gets a real <c>ChildConnection</c> over a real hop to its own
/// double</b>, substituted through <see cref="SessionEnvironment.ConnectChild"/>.
/// Everything above that seam is the product: the lock, the record, the session
/// log, the index entry, the generated config, the routing and — the reason this
/// exists — the <c>(tool, mode)</c> decision. What is absent is
/// <c>ChildProcessSession</c>: the launcher, the job, the stderr pump and the
/// cached exit code, which steps 5, 6 and 7 prove against real processes and
/// which no assertion in this layer is evidence about.
/// </para>
/// <para>
/// <b>Why it is worth the seam.</b> The lookup that decides whether a call may
/// touch cookies has to be driven across sessions of different modes <i>at the
/// same time</i>, at a level of contention that would actually expose a race.
/// Against real children that is three node processes per assertion and a suite
/// measured in minutes; here it is milliseconds, so the concurrency is exercised
/// on every run rather than once.
/// </para>
/// <para>
/// The paths point into the suite's own scratch root rather than at
/// <c>%LocalAppData%\BrowserAI</c>, because the session index is machine-wide
/// state and a rig that reached it would put throwaway directories into a
/// developer's own <c>browserai_list</c> and leave them there.
/// </para>
/// </remarks>
internal sealed class RigSessionEnvironment : IAsyncDisposable
{
    private readonly List<PipeDuplex> _hops = [];
    private readonly List<FakePlaywrightChild> _children = [];
    private readonly List<SessionLogging> _logs = [];
    private readonly List<ChildProcessOptions> _launches = [];
    private readonly Lock _gate = new();

    private int _disposed;

    private RigSessionEnvironment(string root, Action<FakePlaywrightChild>? configure, long? freeBytes)
    {
        Root = root;

        // Created here because the product does not create it: Program.cs makes
        // this run's own directory before the proxy exists, and a session's
        // GENERATED CONFIG is written into it. Left out, every init fails with a
        // DirectoryNotFoundException naming a config file -- which is what
        // happened, and it reported as "this BrowserAI is not driving that
        // session" three layers away.
        var instances = Path.Combine(root, "app-root", "instances");
        _ = Directory.CreateDirectory(instances);

        Environment = new SessionEnvironment
        {
            Paths = new LocalAppDataPaths(Path.Combine(root, "app-root")),
            Payload = RepositoryPayload.Layout,
            InstanceDirectory = instances,
            OpenSessionLog = OpenSessionLog,
            FreeBytesOn = _ => freeBytes,
            ConnectChild = async (options, loggerFactory, idPrefix, relay, cancellationToken) =>
            {
                var hop = new PipeDuplex("session hop (BrowserAI ↔ fake session child)");
                var child = new FakePlaywrightChild(hop);
                configure?.Invoke(child);
                child.Start();

                lock (_gate)
                {
                    _hops.Add(hop);
                    _children.Add(child);
                    _launches.Add(options);
                }

                return await ChildConnection.ConnectAsync(
                    new PipeClientTransport(hop, loggerFactory),
                    loggerFactory,
                    idPrefix,
                    relay,
                    cancellationToken).ConfigureAwait(false);
            },
        };
    }

    /// <summary>What the proxy is handed.</summary>
    public SessionEnvironment Environment { get; private init; }

    /// <summary>The scratch tree every session this rig opens lives under.</summary>
    public string Root { get; }

    /// <summary>
    /// Whether a session can be opened in this environment at all.
    /// </summary>
    /// <remarks>
    /// False for <see cref="Failing"/>, whose whole purpose is that it cannot —
    /// so the rig does not open its default session there and turn one test's
    /// deliberate failure into every test's setup failure.
    /// </remarks>
    public bool CanOpenSessions { get; private init; } = true;

    /// <summary>Every double this rig stood up, one per session.</summary>
    public IReadOnlyList<FakePlaywrightChild> SessionChildren
    {
        get
        {
            lock (_gate)
            {
                return [.. _children];
            }
        }
    }

    /// <summary>
    /// What each session's child <i>would</i> have been started with.
    /// </summary>
    /// <remarks>
    /// The options are the product's, built by <c>SessionManager</c> and handed
    /// to this seam, so an assertion about the working directory or the
    /// environment is an assertion about the product even though no process ever
    /// starts.
    /// </remarks>
    public IReadOnlyList<ChildProcessOptions> Launches
    {
        get
        {
            lock (_gate)
            {
                return [.. _launches];
            }
        }
    }

    /// <summary>Builds an environment for one rig.</summary>
    /// <param name="configure">Programs each session's double before it is started.</param>
    /// <param name="freeBytes">
    /// What the volume reports as free. <see langword="null"/> is "cannot be
    /// asked", which is how a network share behaves and is never a refusal.
    /// </param>
    /// <returns>The environment, which the rig owns and disposes.</returns>
    public static RigSessionEnvironment Create(
        Action<FakePlaywrightChild>? configure = null,
        long? freeBytes = long.MaxValue) =>
        new(Path.Combine(ScratchRoot.Path, $"rig-{Guid.NewGuid():N}"), configure, freeBytes)
        {
            // A volume this environment reports as full refuses every init,
            // including the rig's own -- so the rig does not open a default
            // session there and turn one test's deliberate refusal into a
            // setup failure with somebody else's name on it.
            CanOpenSessions = freeBytes is null or >= SessionManager.RequiredFreeBytes,
        };

    /// <summary>
    /// An environment whose session children refuse to start, the way a missing
    /// payload or a broken <c>node</c> does.
    /// </summary>
    /// <remarks>
    /// The failure is injected at the one seam and nothing else changes, so the
    /// refusal, the released lock and the record left on disk are all the
    /// product's own behaviour rather than a double's.
    /// </remarks>
    /// <param name="reason">What the failure says, so the refusal can be matched to it.</param>
    /// <returns>The environment.</returns>
    public static RigSessionEnvironment Failing(string reason) =>
        new(Path.Combine(ScratchRoot.Path, $"rig-{Guid.NewGuid():N}"), reason);

    private RigSessionEnvironment(string root, string reason)
        : this(root, configure: null, freeBytes: long.MaxValue)
    {
        CanOpenSessions = false;

        Environment = Environment with
        {
            ConnectChild = (_, _, _, _, _) => throw new IOException(reason),
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        foreach (var hop in _hops)
        {
            await hop.CompleteWritersAsync();
        }

        foreach (var child in _children)
        {
            await child.DisposeAsync();
        }

        foreach (var log in _logs)
        {
            log.Dispose();
        }
    }

    /// <summary>Anything this rig's session hops left live, which must be nothing.</summary>
    /// <returns>One line per defect.</returns>
    public IReadOnlyList<string> WhatIsStillLive() =>
    [
        .. _hops.Select(hop => hop.WhatIsStillLive()).OfType<string>(),
        .. _children.Where(child => !child.HasStopped).Select(_ => "a session's fake child is still running"),
    ];

    private SessionLogging OpenSessionLog(string sessionDirectory, LogLevel minimumLevel)
    {
        // The product's own session log, into the session's own directory: the
        // file is one of the two §C allows at a session root, and a rig that
        // faked it would leave `ASessionWritesItsOwnLogBesideItsLockFile`
        // covering a thing this layer never exercises.
#pragma warning disable CA2000 // Ownership moves into the SessionLogging below, which this rig disposes; the rule's dataflow does not follow the transfer.
        var file = new SessionLogFile(sessionDirectory);
#pragma warning restore CA2000

        SessionLogging logging;

        try
        {
#pragma warning disable CA2000 // Ownership moves into the SessionLogging below, which this rig disposes.
            var factory = LoggerFactory.Create(builder =>
            {
                _ = builder.SetMinimumLevel(minimumLevel);
                _ = builder.AddProvider(new FileLoggerProvider(file));
                _ = builder.AddProvider(new TUnitLoggerProvider());
            });
#pragma warning restore CA2000

            logging = new SessionLogging(factory, file);
        }
        catch
        {
            file.Dispose();
            throw;
        }

        lock (_gate)
        {
            _logs.Add(logging);
        }

        return logging;
    }
}

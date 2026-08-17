// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
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
    private readonly List<ChildConnection> _realChildren = [];
    private readonly List<SessionLogging> _logs = [];
    private readonly List<ChildProcessOptions> _launches = [];
    private readonly Lock _gate = new();
    private readonly ILoggerFactory _provisioningLog;

    private int _disposed;

    private RigSessionEnvironment(
        string root,
        Action<FakePlaywrightChild>? configure,
        long? freeBytes,
        Func<string, string, IInstallerRun>? installer,
        ProvisioningTimers? timers,
        TimeSpan? browserIdlePeriod,
        bool realSessionChildren)
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

        // ⚠️ The one arm that points at the developer's REAL browsers root, and
        // it is the arm whose subject is a real browser. Everything else about
        // the app root -- the index, the logs, the instance directory -- stays
        // in the scratch tree, because the index is machine-wide state and a rig
        // that wrote into the real one would leave its throwaway directories in
        // a developer's own `browserai_list`. Only the browsers root is shared,
        // and nothing in this rig writes to it.
        IAppPaths paths = realSessionChildren
            ? new RigPaths(Path.Combine(root, "app-root"), BrowserAiPaths.BrowsersDirectory)
            : new LocalAppDataPaths(Path.Combine(root, "app-root"));

        // ⚠️ The browsers root is inside the scratch tree and the installer is a
        // double, and BOTH halves are load-bearing. Left at the real root, every
        // rig test would take a dependency on a developer's machine having a
        // browser; left with the real installer, the first rig test to meet an
        // empty scratch root would start a 203.8 MB download that nobody asked
        // for and that no assertion is about.
        _provisioningLog = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));

        Provisioner = new BrowserProvisioner(RepositoryPayload.Layout, paths.BrowsersDirectory, _provisioningLog, timers)
        {
            // A test that does not name an installer gets one that refuses to
            // run at all, and a tree that is already complete below. That pairing
            // is the assertion: a rig which is not ABOUT provisioning must not be
            // able to start one by accident, and this is what turns that from a
            // hope into a failure with a name on it.
            StartInstaller = installer ?? ((browser, browsersRoot) =>
                throw new InvalidOperationException(
                    $"A rig with no installer of its own tried to provision '{browser}' into '{browsersRoot}'. Its browsers root is pre-seeded as complete, so reaching this line means something asked for a download that no assertion in this test is about.")),
        };

        if (installer is null && !realSessionChildren)
        {
            // The state a developer's machine is in, and the state every step
            // before this one assumed: the browser is there. Written the way
            // upstream writes it -- the directory, then the marker last -- so a
            // product check that looked at the wrong one would still fail here.
            // Skipped for the real-browser arm, whose root is the developer's
            // own and is already complete: writing into it would be this rig
            // touching a tree it does not own.
            InstallationMarker.Write(Path.Combine(paths.BrowsersDirectory, ChromiumDirectoryName));
        }

        Environment = new SessionEnvironment
        {
            Paths = paths,
            Payload = RepositoryPayload.Layout,
            Provisioner = Provisioner,
            InstanceDirectory = instances,
            OpenSessionLog = OpenSessionLog,
            FreeBytesOn = _ => freeBytes,
        };

        if (browserIdlePeriod is { } period)
        {
            Environment = Environment with { BrowserIdlePeriod = period };
        }

        if (realSessionChildren)
        {
            // ⚠️ The product's own default, spelled again here for one reason:
            // the connection has to be RECORDED. A real session's node pid and
            // the membership of its job object are the only per-session facts
            // about browser processes this suite can ask for — an image-path
            // scan of the machine cannot tell one session's Chromium from
            // another's, and this suite runs browsers in parallel. Everything
            // below the ConnectAsync call is the product's.
            Environment = Environment with
            {
                ConnectChild = async (options, loggerFactory, idPrefix, relay, cancellationToken) =>
                {
                    var child = await ChildConnection.ConnectAsync(
                        new DirectStdioClientTransport(options, loggerFactory),
                        loggerFactory,
                        idPrefix,
                        relay,
                        cancellationToken).ConfigureAwait(false);

                    lock (_gate)
                    {
                        _realChildren.Add(child);
                        _launches.Add(options);
                    }

                    return child;
                },
            };

            return;
        }

        Environment = Environment with
        {
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

    /// <summary>The provisioner this rig's sessions ask about their browser.</summary>
    public BrowserProvisioner Provisioner { get; }

    /// <summary>
    /// The directory a chromium install lands in, spelled from the committed
    /// snapshot rather than as a literal, so a revision bump moves it.
    /// </summary>
    public static string ChromiumDirectoryName { get; } = $"chromium-{BrowserAiPaths.ChromiumRevision}";

    /// <summary>Where this rig's chromium tree is, or would be.</summary>
    public string ChromiumDirectory => Path.Combine(Environment.Paths.BrowsersDirectory, ChromiumDirectoryName);

    /// <summary>
    /// Whether a session can be opened in this environment at all.
    /// </summary>
    /// <remarks>
    /// False for <see cref="Failing"/>, whose whole purpose is that it cannot —
    /// so the rig does not open its default session there and turn one test's
    /// deliberate failure into every test's setup failure.
    /// </remarks>
    public bool CanOpenSessions { get; private init; } = true;

    /// <summary>
    /// Whether the rig opens its own session up front.
    /// </summary>
    /// <remarks>
    /// <b>Separate from <see cref="CanOpenSessions"/>, because they are different
    /// facts.</b> That one says a session <i>cannot</i> be opened here; this one
    /// says one should not be. The test that needs it is
    /// [row 13](../../../plan/H-model-surface.md#h4-the-error-catalogue) — a browser
    /// running out of our tree that <b>no session claims</b> — which is
    /// unreachable while any session is open, including the rig's own.
    /// </remarks>
    public bool OpensDefaultSession { get; private init; } = true;

    /// <summary>
    /// Every <b>real</b> session child this rig stood up, in the order they were
    /// opened.
    /// </summary>
    /// <remarks>
    /// Empty unless the rig was created with real session children. Each one is
    /// owned by the product's <c>SessionManager</c>, not by this rig: reading its
    /// pid and its job membership is all a test may do with it.
    /// </remarks>
    public IReadOnlyList<ChildConnection> RealSessionChildren
    {
        get
        {
            lock (_gate)
            {
                return [.. _realChildren];
            }
        }
    }

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
    /// <param name="installer">
    /// How the provisioner starts an install. The default lays a complete tree
    /// down instantly, so every test that is not <i>about</i> provisioning behaves
    /// as though the browser were already there — which is the state a developer's
    /// machine is in and the state every earlier step's tests assumed.
    /// </param>
    /// <param name="timers">The caps, shrunk when a test is about one of them.</param>
    /// <param name="opensDefaultSession">
    /// Whether the rig opens a session of its own. False for the one test whose
    /// subject is what happens when <b>no</b> session is open.
    /// </param>
    /// <param name="browserIdlePeriod">
    /// How long a session's browser may sit unused before it is closed. Left
    /// unset, the product's shipped ten minutes applies and no test in this
    /// suite ever reaches it; the tests whose subject <i>is</i> the timer pass
    /// milliseconds.
    /// </param>
    /// <param name="realSessionChildren">
    /// Whether each session gets a real <c>node.exe</c> out of the payload, in a
    /// real job, against the developer's real browsers root — rather than an
    /// in-process double. True for the one arm that has to observe an actual
    /// browser going away.
    /// </param>
    public static RigSessionEnvironment Create(
        Action<FakePlaywrightChild>? configure = null,
        long? freeBytes = long.MaxValue,
        Func<string, string, IInstallerRun>? installer = null,
        ProvisioningTimers? timers = null,
        bool opensDefaultSession = true,
        TimeSpan? browserIdlePeriod = null,
        bool realSessionChildren = false) =>
        new(Path.Combine(ScratchRoot.Path, $"rig-{Guid.NewGuid():N}"), configure, freeBytes, installer, timers, browserIdlePeriod, realSessionChildren)
        {
            OpensDefaultSession = opensDefaultSession,
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
        : this(root, configure: null, freeBytes: long.MaxValue, installer: null, timers: null, browserIdlePeriod: null, realSessionChildren: false)
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

        // Last: disposing it signals any watcher still polling, which is what
        // stops a deliberately-hanging installer double outliving its test.
        Provisioner.Dispose();
        _provisioningLog.Dispose();
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

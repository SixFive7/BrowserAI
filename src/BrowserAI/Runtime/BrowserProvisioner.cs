// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Runtime;

/// <summary>Where a browser family stands on this machine, right now.</summary>
internal enum ProvisioningState
{
    /// <summary>
    /// The tree is there and complete — <c>INSTALLATION_COMPLETE</c> is present,
    /// which is the check upstream never makes at launch.
    /// </summary>
    Installed,

    /// <summary>
    /// A download is running, here or in another BrowserAI process. Browser calls
    /// are refused with
    /// <see cref="Sessions.SessionErrors.ProvisioningInProgress"/> rather than
    /// blocked.
    /// </summary>
    Downloading,

    /// <summary>The last attempt ended badly, and the reason is in the status.</summary>
    Failed,
}

/// <summary>What the browsers root holds for one family, and why.</summary>
/// <param name="Browser">The family asked about.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Directory">Where the tree is, or would be.</param>
/// <param name="Detail">
/// One sentence a person or a model can act on. Never <see langword="null"/>,
/// because a state with no reason is the shape this project exists to remove.
/// </param>
internal sealed record ProvisioningStatus(string Browser, ProvisioningState State, string Directory, string Detail);

/// <summary>
/// The four clocks first-run provisioning runs against, gathered so a test can
/// shrink them without the product carrying a debug branch.
/// </summary>
/// <remarks>
/// <b>The first one is not ours and is deliberately not here as a setting.</b>
/// Playwright's own per-socket stall timeout is
/// <c>NET_DEFAULT_TIMEOUT = 30_000</c> ms, overridable through
/// <c>PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT</c> — read out of the resolved
/// bundle 2026-08-16 rather than from memory. BrowserAI sets nothing, so the
/// figure stays upstream's; <see cref="BrowserProvisioner.UpstreamStallTimeout"/>
/// records what we are relying on and
/// <see cref="BrowserProvisioner.UpstreamStallTimeoutVariable"/> names the
/// variable a test asserts is absent from the installer's environment. A
/// duplicate of the number in our own configuration would drift silently the day
/// upstream changed theirs.
/// </remarks>
internal sealed record ProvisioningTimers
{
    /// <summary>
    /// How long the installer child may run before its job is closed.
    /// </summary>
    /// <remarks>
    /// 45 minutes is <b>27 minutes of headroom over the 1 Mbps arithmetic</b>
    /// for a 203.8 MB download, so it never fires on a slow link — which is what
    /// a cap has to be, because a cap that fires on a legitimate case teaches
    /// callers to retry into it.
    /// </remarks>
    public TimeSpan AbsoluteCap { get; init; } = TimeSpan.FromMinutes(45);

    /// <summary>
    /// How long everything after the browser's own directory first appears may
    /// take.
    /// </summary>
    /// <remarks>
    /// <b>The phase boundary is observable, which is why this cap can exist at
    /// all.</b> Upstream downloads into a temp directory and only creates
    /// <c>&lt;browsers-root&gt;\&lt;browser&gt;-&lt;rev&gt;\</c> when it starts
    /// unzipping, so that directory appearing <i>is</i> the transition from
    /// network to disk. What the cap covers is therefore extraction plus the two
    /// small companion downloads that follow it (<c>ffmpeg</c>, and
    /// <c>winldd</c> which Windows pulls in with it) — measured 2026-08-16 at
    /// 1.5 s of the 12 s total, so ten minutes is three orders of magnitude of
    /// headroom rather than a guess.
    /// </remarks>
    public TimeSpan ExtractionCap { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The crash tripwire on the whole attempt, including the wait on another
    /// process that is doing the install.
    /// </summary>
    /// <remarks>
    /// <b>It should never fire, and that is the point.</b> The absolute cap is
    /// 45 minutes and this is 60, so anything that reaches it is a bug in the
    /// caps above rather than a slow network — and it is logged at
    /// <see cref="LogLevel.Critical"/> for exactly that reason. A provisioning
    /// task that simply never completes is otherwise invisible: <c>init</c> keeps
    /// answering "downloading" forever and nothing anywhere says the download
    /// died.
    /// </remarks>
    public TimeSpan OuterDeadline { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>How often the phase watcher looks.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// First-run browser provisioning: it downloads, it never blocks, and it says
/// which of those it is doing.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>init</c> must not block, and that constraint shapes the whole type.</b>
/// A caller that waits three minutes inside one tool call has had whatever
/// timing it was managing corrupted, with nothing to read and no way to decide
/// whether waiting is worth it. So <see cref="Ensure"/> returns immediately with
/// <see cref="ProvisioningState.Downloading"/> and every upstream call is
/// refused with <see cref="Sessions.SessionErrors.ProvisioningInProgress"/>,
/// which names the download size and says the same call will work shortly. The
/// same child then navigates once the install lands — no restart, because
/// nothing about the child depended on the browser existing when it started.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously "and <c>browser_get_config</c> keeps
/// working because it needs no browser").</b> It does not. Measured twice
/// against the child directly with an empty browsers root, it answers
/// <c>isError: true</c> from <c>throwIfExecutableMissing</c> — it never
/// <i>launches</i> a browser, which is why the round trip is cheap on a
/// provisioned machine, but the executable has to exist
/// ([kb](../../../kb/playwright/configuration.md#browser-provisioning)). So
/// <b>every</b> upstream tool is refused meanwhile, that one included, and
/// letting it through would have bought a worse answer rather than a working
/// one. What keeps a downloading session inspectable is BrowserAI's <b>own</b>
/// tools — <c>browserai_list</c>, <c>browserai_resume</c> and
/// <c>browserai_set_purpose</c> all answer throughout, because none of them
/// needs a browser.
/// </para>
/// <para>
/// <b>Browsers live outside <c>current\</c>, resolved through
/// <see cref="Hosting.IAppPaths"/>.</b> Inside it, every update would re-download
/// 203.8 MB. The path also has to be <b>absolute</b>: it reaches the child as
/// <c>PLAYWRIGHT_BROWSERS_PATH</c>, and a relative value there resolves against
/// <c>INIT_CWD</c> — inherited from whatever npm ancestor last ran — before the
/// child's own working directory.
/// </para>
/// <para>
/// <b>The installer is upstream's own, run out of the payload.</b>
/// <c>node.exe cli.js install-browser &lt;browser&gt; --no-shell --no-progress</c>,
/// so the revision comes from the vendored <c>browsers.json</c> rather than from
/// a URL anybody typed. <c>--no-shell</c> is load-bearing:
/// <c>chrome-headless-shell</c> is never provisioned, which is what makes the
/// chromium-alias channel mandatory rather than a preference.
/// </para>
/// <para>
/// ⚠️ <b><c>PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD</c> does not gate this.</b>
/// Measured 2026-08-16: the variable is read in exactly two places —
/// <c>installBrowsersForNpmInstall</c> and <c>ensureConfiguredBrowserInstalled</c>
/// — and <c>registry.install()</c>, which <c>install-browser</c> calls, does not
/// consult it. It stays in the child environment for the reason it was always
/// mandated (the <i>server</i> must not provision behind our back) and is not a
/// kill switch this code could rely on.
/// </para>
/// <para>
/// <b>One install per machine, not one per process.</b> The mutex is
/// <c>Global\</c>-scoped, taken at a zero timeout, and a process that does not
/// get it does not queue a second download — it watches for the marker the
/// winner will write. Two concurrent <c>registry.install()</c> runs against one
/// root is precisely how a half-extracted tree acquires an
/// <c>INSTALLATION_COMPLETE</c>.
/// </para>
/// <para>
/// <b>The install runs on its own thread, and that is a correctness requirement
/// rather than a performance one.</b> A named mutex is owned by the
/// <i>thread</i> that waited on it, so a continuation resuming on a different
/// pool thread makes the release throw about "an unsynchronized block of code" —
/// naming nothing relevant and pointing nowhere near the cause.
/// </para>
/// </remarks>
internal sealed class BrowserProvisioner : IDisposable
{
    /// <summary>
    /// Playwright's own per-socket stall timeout, which BrowserAI deliberately
    /// leaves alone.
    /// </summary>
    /// <remarks>
    /// Read 2026-08-16 out of the resolved <c>playwright-core</c> bundle:
    /// <c>NET_DEFAULT_TIMEOUT = 3e4</c>, applied as
    /// <c>+(PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT || "0") || NET_DEFAULT_TIMEOUT</c>.
    /// It is recorded here so that the caps above can be reasoned about against
    /// a stated figure, and it is never set, so it can never drift from
    /// upstream's.
    /// </remarks>
    public static TimeSpan UpstreamStallTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The variable that would override it, named so its absence is testable.</summary>
    public const string UpstreamStallTimeoutVariable = "PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT";

    /// <summary>The mutex prefix that makes an install machine-wide rather than per process.</summary>
    public const string MutexPrefix = $@"{LockScopes.GlobalPrefix}BrowserAI-Provision-";

    /// <summary>
    /// The machine-wide name that serialises installs of one browser into one
    /// browsers root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keyed on the root as well as the family, and the root is the half that
    /// is easy to leave out.</b> Two BrowserAI installations with different
    /// browsers roots are genuinely independent: their downloads write to
    /// different directories and neither can corrupt the other. A name carrying
    /// only the family would serialise them anyway — and worse, the loser would
    /// then sit watching for a marker in <i>its own</i> root that the winner is
    /// never going to write, so it would report "downloading" until the outer
    /// deadline. Found by the suite, where every rig has a browsers root of its
    /// own.
    /// </para>
    /// <para>
    /// 128 bits of a SHA-256, exactly as
    /// [the per-directory gate](../Sessions/SessionPath.cs) does it, and
    /// <c>Global\</c> because there is no <c>Local\</c> fallback anywhere in this
    /// product: a logon-session-scoped name would let a Remote Desktop session and
    /// the console session install into one directory at once, each reporting
    /// success.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <param name="browser">The family.</param>
    /// <returns>The mutex name.</returns>
    public static string MutexNameFor(string browsersDirectory, string browser)
    {
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(browser);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{browsersDirectory.ToUpperInvariant()}|{browser.ToUpperInvariant()}"));

        return MutexPrefix + Convert.ToHexString(digest)[..LockScopes.PerDirectoryHashLength];
    }

    /// <summary>
    /// How large the first-run download is, quoted to a caller deciding whether
    /// waiting is worth it.
    /// </summary>
    /// <remarks>
    /// <b>Measured, dated, and re-established by asking the CDN rather than by
    /// reasoning.</b> 202,283,919 + 1,411,741 + 128,684 = 203,824,344 B, from the
    /// exact <c>content-length</c> of <c>chrome-win64.zip</c>,
    /// <c>ffmpeg-win64.zip</c> and <c>winldd-win64.zip</c> — re-measured
    /// 2026-08-16 at chromium rev 1237 / 152.0.7977.8, unchanged from 2026-08-15.
    /// It is a string rather than a number because the only thing done with it is
    /// putting it in a sentence, and a byte count formatted at the call site is a
    /// second place for it to drift.
    /// [kb](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
    /// carries the figure and how to re-establish it.
    /// </remarks>
    public const string FirstRunDownloadSize = "203.8 MB";

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly PayloadLayout _payload;
    private readonly ILogger _logger;
    private readonly ProvisioningTimers _timers;
    private readonly CancellationTokenSource _stopping = new();
    private BrowsersManifest? _manifest;
    private int _disposed;

    /// <summary>Creates the provisioner for one process.</summary>
    /// <param name="payload">Where <c>node.exe</c>, <c>cli.js</c> and <c>browsers.json</c> live.</param>
    /// <param name="browsersDirectory">The browsers root. <b>Must be absolute.</b></param>
    /// <param name="loggerFactory">Where progress and failures go.</param>
    /// <param name="timers">The caps. The default is production's.</param>
    /// <exception cref="ArgumentException"><paramref name="browsersDirectory"/> is not absolute.</exception>
    public BrowserProvisioner(
        PayloadLayout payload,
        string browsersDirectory,
        ILoggerFactory loggerFactory,
        ProvisioningTimers? timers = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (!Path.IsPathFullyQualified(browsersDirectory))
        {
            throw new ArgumentException(
                $"The browsers root must be absolute, and '{browsersDirectory}' is not: it reaches the child as {ChildLaunch.BrowsersPathVariable}, and a relative value there resolves against INIT_CWD first.",
                nameof(browsersDirectory));
        }

        _payload = payload;
        BrowsersDirectory = browsersDirectory;
        _logger = loggerFactory.CreateLogger<BrowserProvisioner>();
        _timers = timers ?? new ProvisioningTimers();

        // Assigned here rather than as a property initialiser, which cannot see
        // the payload. An object initialiser still replaces it, which is what
        // makes it a seam.
        StartInstaller = (browser, root) => new NodeInstallerRun(_payload, browser, root);
        PruneRevisions = browser => _ = RevisionPrune.Run(BrowsersDirectory, Manifest(), _logger, familyAlreadyHeld: browser);
    }

    /// <summary>
    /// How the installer is started, so the suite can drive the state machine and
    /// the three caps without downloading 203.8 MB per assertion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seam is the launch and nothing above it.</b> Everything that
    /// decides behaviour — the marker check, the machine-wide mutex, the phase
    /// watcher, every cap, the removal of a partial tree — is on this side of the
    /// line and runs identically against a double. What a substitute replaces is
    /// one <c>CreateProcessW</c>.
    /// </para>
    /// <para>
    /// <b>The default is the real thing, and the real thing is proven
    /// separately</b> against an empty browsers root through the published
    /// binary — so this cannot become the only path anybody exercises.
    /// </para>
    /// </remarks>
    public Func<string, string, IInstallerRun> StartInstaller { get; init; }

    /// <summary>
    /// What a successful provision does about superseded revisions, so the suite
    /// can prove that a prune which throws never fails the provision it followed.
    /// </summary>
    /// <remarks>
    /// <b>The seam exists for exactly one property, and that property is
    /// otherwise unassertable.</b> Nothing on
    /// <see cref="RevisionPrune.Run(string, BrowsersManifest, ILogger, string?)"/>'s
    /// own path throws by design — the process census, the enumeration and the
    /// sizing all catch — so the <c>catch</c> at the call site is a guard against
    /// a future edit rather than against a reachable case today, and a guard
    /// nothing exercises is the shape this project's audit keeps finding. The
    /// argument is the family whose mutex the calling thread already holds.
    /// </remarks>
    public Action<string> PruneRevisions { get; init; }

    /// <summary>The browsers root this provisioner installs into. Always absolute.</summary>
    public string BrowsersDirectory { get; }

    /// <summary>Where a family's tree is, or would be.</summary>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>The absolute directory.</returns>
    public string DirectoryFor(string browser) =>
        Path.Combine(BrowsersDirectory, Manifest().For(browser).DirectoryName);

    /// <summary>
    /// Reports a family's state without starting anything.
    /// </summary>
    /// <remarks>
    /// The read-only half, for callers that are deciding what to say rather than
    /// what to do — <c>browserai_list</c>, a status line, a refusal.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>Where it stands.</returns>
    public ProvisioningStatus Peek(string browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var revision = Manifest().For(browser);
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);

        if (IsComplete(directory))
        {
            return new ProvisioningStatus(browser, ProvisioningState.Installed, directory, $"{revision.Description} is installed at '{directory}'.");
        }

        if (_attempts.TryGetValue(browser, out var attempt) && !attempt.Task.IsCompleted)
        {
            return Downloading(browser, directory, revision, attempt);
        }

        if (attempt is { Task.IsCompleted: true } finished && finished.Task.Result is { } result && !result.Succeeded)
        {
            return new ProvisioningStatus(browser, ProvisioningState.Failed, directory, result.Detail);
        }

        return new ProvisioningStatus(
            browser,
            ProvisioningState.Failed,
            directory,
            $"{revision.Description} is not installed at '{directory}' and no download is running.");
    }

    /// <summary>
    /// Makes sure a family is being provisioned, and <b>returns immediately</b>.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe to call on every <c>init</c>: an installed family
    /// costs one <c>File.Exists</c>, and a download already running is joined
    /// rather than started again.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>Where it stands, as of this instant.</returns>
    public ProvisioningStatus Ensure(string browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var revision = Manifest().For(browser);
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);

        // The hot path, and it has to stay one syscall: this runs on every
        // session start.
        if (IsComplete(directory))
        {
            return new ProvisioningStatus(browser, ProvisioningState.Installed, directory, $"{revision.Description} is installed at '{directory}'.");
        }

        var attempt = _attempts.AddOrUpdate(
            browser,
            static (key, state) => state.Provisioner.Start(key, state.Revision),
            static (key, existing, state) => existing.Task.IsCompleted && !Result(existing).Succeeded
                ? state.Provisioner.Start(key, state.Revision)
                : existing,
            (Provisioner: this, Revision: revision));

        return attempt.Task.IsCompleted
            ? Peek(browser)
            : Downloading(browser, directory, revision, attempt);
    }

    /// <summary>
    /// Waits for a family to finish provisioning, for callers that legitimately
    /// block — <c>browserai_reinstall_browser</c>, and the suite.
    /// </summary>
    /// <remarks>
    /// <b>No tool a model calls goes through here except the reinstall</b>, which
    /// is explicitly an operation the caller asked to have happen. Everything on
    /// the <c>init</c> path uses <see cref="Ensure"/> and is answered
    /// immediately.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>Where it ended up.</returns>
    public async Task<ProvisioningStatus> WaitAsync(string browser, CancellationToken cancellationToken = default)
    {
        var status = Ensure(browser);

        if (status.State is not ProvisioningState.Downloading || !_attempts.TryGetValue(browser, out var attempt))
        {
            return status;
        }

        _ = await attempt.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Peek(browser);
    }

    /// <summary>
    /// Deletes a family's tree and provisions it again, in that order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Delete-then-download rather than download-beside-and-swap, and Windows
    /// decides that rather than taste.</b> A directory holding open executables
    /// cannot be renamed, so the swap that would make this atomic is not
    /// available: the window in which no browser is installed is unavoidable, and
    /// the caller is told the operation is destructive rather than being sold an
    /// atomicity that does not exist.
    /// </para>
    /// <para>
    /// The caller is responsible for having established that nothing is using the
    /// tree. This method refuses nothing; refusing is
    /// <c>browserai_reinstall_browser</c>'s job, and it does it before calling
    /// here.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>What was removed, what would not go, and where the new tree stands.</returns>
    public async Task<ReinstallOutcome> ReinstallAsync(string browser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var revision = Manifest().For(browser);
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);
        var removedBytes = SizeOf(directory);
        var failures = new List<string>();

        ProvisioningLog.Reinstalling(_logger, browser, directory);

        // §E's routine, not a second one: a per-node try/catch, so one locked
        // file costs that file rather than the whole tree.
        TreeDelete.Remove(directory, failures);

        // A completed attempt would otherwise make Ensure believe the tree it
        // just deleted is still there.
        _ = _attempts.TryRemove(browser, out _);

        if (failures.Count is not 0)
        {
            // Re-provisioning on top of a tree that would not delete is how a
            // half-old, half-new browser directory gets an INSTALLATION_COMPLETE
            // written over it. Stop instead, and say what is holding it.
            return new ReinstallOutcome(
                browser,
                directory,
                removedBytes,
                failures,
                new ProvisioningStatus(
                    browser,
                    ProvisioningState.Failed,
                    directory,
                    $"'{directory}' was not fully removed, so nothing was re-downloaded on top of it."));
        }

        var status = await WaitAsync(browser, cancellationToken).ConfigureAwait(false);

        return new ReinstallOutcome(browser, directory, removedBytes, failures, status);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // Signals the phase watcher to close the installer's job, which is what
        // stops a 200 MB download when the process serving the caller goes away.
        _stopping.Cancel();
        _stopping.Dispose();
    }

    /// <summary>Whether a browser directory is complete rather than merely present.</summary>
    /// <remarks>
    /// <b>The marker is the whole check, and Playwright never makes it at
    /// launch.</b> A partial tree without it produces <c>spawn EFTYPE</c>, after
    /// which upstream writes <c>DEPENDENCIES_VALIDATED</c> into the corrupt
    /// directory and suppresses revalidation for thirty days.
    /// </remarks>
    private static bool IsComplete(string directory) =>
        File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker));

    private static ProvisioningResult Result(Attempt attempt) =>
        attempt.Task.IsCompletedSuccessfully
            ? attempt.Task.Result
            : new ProvisioningResult(false, "The provisioning task itself failed.");

    private static long SizeOf(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
                : 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A size that cannot be measured is reported as unknown rather than
            // as zero, which would read as "there was nothing there".
            return -1;
        }
    }

    private static ProvisioningStatus Downloading(string browser, string directory, BrowserRevision revision, Attempt attempt) =>
        new(
            browser,
            ProvisioningState.Downloading,
            directory,
            $"{revision.Description} is being downloaded into '{directory}'; started {attempt.Started.ToString("O", CultureInfo.InvariantCulture)}.");

    private BrowsersManifest Manifest() => _manifest ??= BrowsersManifest.Read(_payload);

    /// <summary>
    /// Removes browser revisions the resolved manifest no longer names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pruning is ours because we turned upstream's off.</b>
    /// <c>PLAYWRIGHT_SKIP_BROWSER_GC=1</c> is mandated in the child environment,
    /// so nothing else will ever remove a superseded revision — and a browser
    /// tree is ~430 MiB, stranded per bump, per machine, forever.
    /// </para>
    /// <para>
    /// <b><paramref name="browser"/> is passed as the family already held.</b>
    /// This runs inside the per-family mutex, so the pruner must not try to
    /// re-acquire it; a zero-timeout acquire would refuse and a waiting one
    /// would deadlock against this very call.
    /// </para>
    /// <para>
    /// <b>The <c>catch</c> is the whole reason this is a method rather than a
    /// line</b>, and what it prevents was measured rather than assumed. It runs
    /// inside <see cref="Install"/>'s catch-all, so without it a prune that threw
    /// would be logged as
    /// <see cref="ProvisioningLog.Failed"/> — <i>"Provisioning chromium failed"</i>
    /// at <see cref="LogLevel.Error"/>, carrying the pruner's exception — over a
    /// 203.8 MB download that had just succeeded, and nothing anywhere would say
    /// the disk went unreclaimed. Planted 2026-08-17: the status the caller sees
    /// stays <see cref="ProvisioningState.Installed"/>, because
    /// <see cref="Peek"/> reads the completion marker before it reads the cached
    /// result — so the damage is a confident wrong answer in the log rather than
    /// a wrong answer to the model, which is exactly the kind that survives.
    /// <see cref="PruneLog.PassFailed"/> was written for this and was called from
    /// nowhere until 2026-08-17; the audit of the reconstructed call site is what
    /// found it.
    /// </para>
    /// <para>
    /// Reconstructed 2026-08-17 after the agent building it was cut off
    /// mid-edit by an API limit, having reverted this call site and not yet
    /// restored it. The build was red on exactly one symbol, which is the
    /// cheapest possible way for that to present.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family whose mutex the caller already holds.</param>
    private void Prune(string browser)
    {
        try
        {
            PruneRevisions(browser);
        }
#pragma warning disable CA1031 // Reclaiming disk is never urgent and never worth failing an install that succeeded; every failure shape means the same thing here.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PruneLog.PassFailed(_logger, failure);
        }
    }

    private Attempt Start(string browser, BrowserRevision revision)
    {
        var started = DateTimeOffset.Now;

        // LongRunning, so this gets a thread of its own rather than a pool
        // thread: the body takes a named mutex, and a named mutex is owned by
        // the thread that waited on it.
        var task = Task.Factory.StartNew(
            () => Install(browser, revision),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return new Attempt(task, started);
    }

    private ProvisioningResult Install(string browser, BrowserRevision revision)
    {
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);
        var deadline = Stopwatch.StartNew();

        try
        {
            using var mutex = MachineMutex.Create(MutexNameFor(BrowsersDirectory, browser));

            // Zero, deliberately: a process that does not get the mutex must not
            // queue a SECOND download of the same 203.8 MB, it must watch for the
            // marker the winner is about to write.
            var acquisition = mutex.Acquire(LockScopes.NeverWaits);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                ProvisioningLog.AnotherProcessIsInstalling(_logger, browser);
                return WaitForAnotherProcess(browser, directory, deadline);
            }

            try
            {
                if (acquisition is MutexAcquisition.AcquiredAbandoned)
                {
                    // The previous holder died mid-install. Whatever it left is
                    // unmarked and therefore unusable, and re-running on top of
                    // it is the same call again -- so the tree goes first, which
                    // is what makes this a recovery rather than a retry.
                    ProvisioningLog.AbandonedInstallFound(_logger, browser, directory);
                    TreeDelete.Remove(directory, []);
                }

                // Re-checked under the mutex: another process may have finished
                // between the hot-path check and this line, and re-downloading
                // 203.8 MB on top of a complete tree is the cost of not looking.
                var result = IsComplete(directory)
                    ? new ProvisioningResult(true, $"{revision.Description} was already installed at '{directory}'.")
                    : RunInstaller(browser, revision, directory, deadline);

                if (result.Succeeded)
                {
                    // §A: PLAYWRIGHT_SKIP_BROWSER_GC=1 is mandated, so pruning old
                    // revisions is BrowserAI's job. Here rather than at startup
                    // because this is the one moment the answer can have changed —
                    // a revision becomes superseded when a new one lands.
                    Prune(browser);
                }

                return result;
            }
            finally
            {
                mutex.Release();
            }
        }
#pragma warning disable CA1031 // A provisioning failure is reported to the caller as a state with a reason; there is no path on which it may take the process down.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            ProvisioningLog.Failed(_logger, browser, failure);
            return new ProvisioningResult(false, $"Provisioning {browser} failed: {failure.GetType().Name}: {failure.Message}");
        }
    }

    private ProvisioningResult WaitForAnotherProcess(string browser, string directory, Stopwatch deadline)
    {
        while (!IsComplete(directory))
        {
            if (deadline.Elapsed > _timers.OuterDeadline)
            {
                // The tripwire. It should be unreachable -- the installing
                // process caps itself 15 minutes earlier -- so reaching it means
                // that process died without releasing, or the caps are wrong.
                ProvisioningLog.OuterDeadlineReached(_logger, browser, (int)deadline.Elapsed.TotalMinutes);

                return new ProvisioningResult(
                    false,
                    $"Another process has been provisioning {browser} for more than {_timers.OuterDeadline.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} minutes and has not finished. Nothing was downloaded here.");
            }

            if (_stopping.IsCancellationRequested)
            {
                return new ProvisioningResult(false, $"BrowserAI is shutting down; the wait for another process to finish provisioning {browser} was abandoned.");
            }

            Thread.Sleep(_timers.Poll);
        }

        return new ProvisioningResult(true, $"{browser} was provisioned by another BrowserAI process into '{directory}'.");
    }

    private ProvisioningResult RunInstaller(string browser, BrowserRevision revision, string directory, Stopwatch deadline)
    {
        _ = Directory.CreateDirectory(BrowsersDirectory);

        ProvisioningLog.Downloading(_logger, browser, revision.Revision, BrowsersDirectory);

        using var run = StartInstaller(browser, BrowsersDirectory);

        var failure = Watch(run, browser, directory, deadline);

        if (failure is not null)
        {
            // Closing the job is the kill: TerminateProcess on the installer
            // alone would leave the download helper it forked behind.
            run.Stop();

            // A partial tree is unmarked and would be re-downloaded anyway, but
            // leaving it costs 400 MB and makes the next attempt's progress
            // unreadable.
            TreeDelete.Remove(directory, []);

            return new ProvisioningResult(false, failure);
        }

        var exitCode = run.ExitCode;

        // Both halves, and the second is the one upstream never checks. An
        // installer that exits 0 having written no marker has left a tree that
        // launches as `spawn EFTYPE` and never re-downloads.
        if (exitCode is not 0 || !IsComplete(directory))
        {
            ProvisioningLog.InstallerRefused(_logger, browser, exitCode, Tail(run.Output));
            TreeDelete.Remove(directory, []);

            return new ProvisioningResult(
                false,
                $"The installer for {revision.Description} exited with code {exitCode.ToString(CultureInfo.InvariantCulture)} and left no '{BrowsersManifest.InstallationCompleteMarker}' in '{directory}'. {Tail(run.Output)}");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var said = run.Output.Trim();
            ProvisioningLog.InstallerSaid(_logger, browser, said);
        }

        ProvisioningLog.Installed(_logger, browser, revision.Revision, (int)deadline.Elapsed.TotalSeconds, directory);

        return new ProvisioningResult(true, $"{revision.Description} was downloaded into '{directory}' in {deadline.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
    }

    /// <summary>
    /// Watches one installer run against the three caps, and answers with the
    /// cap that fired.
    /// </summary>
    /// <returns><see langword="null"/> when the installer exited on its own.</returns>
    private string? Watch(IInstallerRun run, string browser, string directory, Stopwatch deadline)
    {
        var extraction = default(TimeSpan?);

        while (!run.HasExited)
        {
            if (_stopping.IsCancellationRequested)
            {
                return $"BrowserAI is shutting down; the download of {browser} was stopped.";
            }

            // The phase boundary, and it is observable rather than inferred:
            // upstream downloads into a temp directory and creates this one only
            // when it starts unzipping.
            if (extraction is null && Directory.Exists(directory))
            {
                extraction = deadline.Elapsed;
                ProvisioningLog.Extracting(_logger, browser, (int)deadline.Elapsed.TotalSeconds);
            }

            if (deadline.Elapsed > _timers.AbsoluteCap)
            {
                ProvisioningLog.CapReached(_logger, browser, "absolute", (int)_timers.AbsoluteCap.TotalMinutes);

                return $"The download of {browser} passed the {Minutes(_timers.AbsoluteCap)}-minute cap and was stopped. Nothing usable was left on disk.";
            }

            if (extraction is { } began && deadline.Elapsed - began > _timers.ExtractionCap)
            {
                ProvisioningLog.CapReached(_logger, browser, "extraction", (int)_timers.ExtractionCap.TotalMinutes);

                return $"Extracting {browser} passed the {Minutes(_timers.ExtractionCap)}-minute cap and was stopped. Nothing usable was left on disk.";
            }

            Thread.Sleep(_timers.Poll);
        }

        return null;
    }

    private static string Minutes(TimeSpan span) =>
        span.TotalMinutes.ToString(span.TotalMinutes < 1 ? "F2" : "F0", CultureInfo.InvariantCulture);

    private static string Tail(string output)
    {
        var text = output.Trim();

        return text.Length switch
        {
            0 => "The installer wrote nothing.",
            > 800 => "The installer said: " + text[^800..],
            _ => "The installer said: " + text,
        };
    }

    /// <summary>One provisioning attempt in this process.</summary>
    private sealed record Attempt(Task<ProvisioningResult> Task, DateTimeOffset Started);

    /// <summary>How one attempt ended.</summary>
    private sealed record ProvisioningResult(bool Succeeded, string Detail);
}

/// <summary>One running installer, seen only through what the watcher needs.</summary>
/// <remarks>
/// Deliberately four members. Anything richer would let the watcher reach past
/// the seam into a <c>Process</c>, and the point of the seam is that everything
/// deciding behaviour stays on the provisioner's side of it.
/// </remarks>
internal interface IInstallerRun : IDisposable
{
    /// <summary>Whether the installer has finished, however it finished.</summary>
    bool HasExited { get; }

    /// <summary>Its exit code, valid once <see cref="HasExited"/> is true.</summary>
    int ExitCode { get; }

    /// <summary>Everything it has written so far, for a failure message to quote.</summary>
    string Output { get; }

    /// <summary>Stops it and everything it started.</summary>
    void Stop();
}

/// <summary>
/// The real installer: upstream's own <c>install-browser</c>, run out of the
/// payload, inside a job.
/// </summary>
/// <remarks>
/// <para>
/// <b>The job is not optional.</b> Upstream forks a separate
/// <c>oopBrowserDownload.js</c> process to do the transfer, so terminating the
/// <c>node</c> we started would leave a grandchild pulling 200 MB with nobody to
/// receive it. Closing the job takes the whole tree, which is the same mechanism
/// every other child in this product is contained by.
/// </para>
/// <para>
/// <b>Both streams are pumped from the moment the process starts.</b> A child
/// whose pipe buffer fills stops making progress, and a download stalled on its
/// own stdout is indistinguishable from a network stall — which is precisely the
/// failure the caps above would then mis-attribute.
/// </para>
/// </remarks>
internal sealed class NodeInstallerRun : IInstallerRun
{
    private readonly JobObject _job;
    private readonly LaunchedProcess _process;
    private readonly StringBuilder _output = new();
    private int _disposed;

    /// <summary>Starts the installer.</summary>
    /// <param name="payload">Where <c>node.exe</c> and <c>cli.js</c> live.</param>
    /// <param name="browser">The family to install.</param>
    /// <param name="browsersDirectory">The absolute browsers root.</param>
    public NodeInstallerRun(PayloadLayout payload, string browser, string browsersDirectory)
    {
        ArgumentNullException.ThrowIfNull(payload);
        payload.Verify();

        _job = JobObject.CreateKillOnClose();

        try
        {
            _process = JobLauncher.Start(
                _job,
                payload.NodeExecutable,
                [
                    payload.PlaywrightMcpCli,
                    "install-browser",
                    browser,

                    // Load-bearing: chrome-headless-shell is never provisioned,
                    // which is what makes the chromium-alias channel in the
                    // generated config mandatory rather than a preference.
                    "--no-shell",

                    // A progress bar on a pipe nobody renders is noise in the log
                    // and bytes in a buffer.
                    "--no-progress",
                ],
                browsersDirectory,

                // The same allowlist every other child gets, plus the absolute
                // browsers root. Note what it does NOT carry: no
                // PLAYWRIGHT_DOWNLOAD_HOST variant, so the five retries rotate
                // through whatever mirror list upstream has for that download,
                // and no PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT, so the
                // per-socket stall timeout stays upstream's 30 s.
                //
                // Corrected 2026-08-17 (previously "the five retries really do
                // rotate through upstream's mirror list"). Chrome for Testing
                // resolves through `cftUrl`, whose list is ONE host, so the
                // rotation protects ffmpeg, winldd and Firefox and not the
                // 202 MB half. The strip is still right; the sentence claimed
                // more than the measurement. §A carried this correction from
                // 2026-08-16 and it was never swept into the shipped comments.
                ChildEnvironment.Build([new KeyValuePair<string, string>(ChildLaunch.BrowsersPathVariable, browsersDirectory)]));
        }
        catch
        {
            _job.Dispose();
            throw;
        }

        Pump(_process.StandardOutput);
        Pump(_process.StandardError);
    }

    /// <inheritdoc />
    public bool HasExited
    {
        get
        {
            if (!_process.HasExited)
            {
                return false;
            }

            // Cached as an int the moment it exists: Process.ExitCode's
            // equivalent throws after disposal, which turns "why did the
            // installer die" into an exception on the reporting path.
            ExitCode = _process.TryReadExitCode() ?? ExitCode;
            return true;
        }
    }

    /// <inheritdoc />
    public int ExitCode { get; private set; } = -1;

    /// <inheritdoc />
    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    /// <inheritdoc />
    public void Stop() => _job.Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        _process.Dispose();

        // Last: closing the job is what kills anything still running, and the
        // process object's streams should be closed before that happens.
        _job.Dispose();
    }

    private void Pump(Stream stream) =>
        _ = Task.Run(async () =>
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length is 0)
                {
                    continue;
                }

                lock (_output)
                {
                    _ = _output.AppendLine(line);
                }
            }
        });
}

/// <summary>What <c>browserai_reinstall_browser</c> did.</summary>
/// <param name="Browser">The family.</param>
/// <param name="Directory">The tree that was removed and re-created.</param>
/// <param name="RemovedBytes">What was there before, or -1 when it could not be measured.</param>
/// <param name="Failures">What would not delete, one line each. Empty on the ordinary path.</param>
/// <param name="Status">Where the family stands now.</param>
internal sealed record ReinstallOutcome(
    string Browser,
    string Directory,
    long RemovedBytes,
    IReadOnlyList<string> Failures,
    ProvisioningStatus Status);

/// <summary>Source-generated log messages for provisioning.</summary>
/// <remarks>Event ids start at 60, after <see cref="Sessions.SessionToolLog"/>'s 40s.</remarks>
internal static partial class ProvisioningLog
{
    /// <summary>A download has started.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="revision">The revision the payload's browsers.json names.</param>
    /// <param name="root">The browsers root.</param>
    [LoggerMessage(
        EventId = 60,
        Level = LogLevel.Information,
        Message = "Provisioning {Browser} revision {Revision} into {Root}. init does not wait for this; browser calls are refused until it lands.")]
    public static partial void Downloading(ILogger logger, string browser, string revision, string root);

    /// <summary>The browser's own directory appeared, so extraction has begun.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="afterSeconds">How long the download took.</param>
    [LoggerMessage(
        EventId = 61,
        Level = LogLevel.Information,
        Message = "Extracting {Browser}; the download took {AfterSeconds} s.")]
    public static partial void Extracting(ILogger logger, string browser, int afterSeconds);

    /// <summary>A download finished and left the marker behind.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="revision">The revision.</param>
    /// <param name="seconds">How long the whole install took.</param>
    /// <param name="directory">Where it landed.</param>
    [LoggerMessage(
        EventId = 62,
        Level = LogLevel.Information,
        Message = "Provisioned {Browser} revision {Revision} in {Seconds} s into {Directory}.")]
    public static partial void Installed(ILogger logger, string browser, string revision, int seconds, string directory);

    /// <summary>Everything the installer wrote, kept for the log rather than discarded.</summary>
    /// <remarks>
    /// Its lines name the exact CDN URL each component came from, which is the
    /// only record of <i>which mirror</i> a machine actually reached — the thing
    /// nobody can reconstruct afterwards when a download fails behind a proxy.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="output">What it said.</param>
    [LoggerMessage(EventId = 63, Level = LogLevel.Debug, Message = "install-browser {Browser} wrote: {Output}")]
    public static partial void InstallerSaid(ILogger logger, string browser, string output);

    /// <summary>The installer exited badly, or left no marker.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="exitCode">Its exit code.</param>
    /// <param name="tail">The tail of what it wrote.</param>
    [LoggerMessage(
        EventId = 64,
        Level = LogLevel.Error,
        Message = "The installer for {Browser} exited {ExitCode} without completing. {Tail}")]
    public static partial void InstallerRefused(ILogger logger, string browser, int exitCode, string tail);

    /// <summary>One of the caps fired and the installer was stopped.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="cap">Which cap.</param>
    /// <param name="minutes">What it was set to.</param>
    [LoggerMessage(
        EventId = 65,
        Level = LogLevel.Error,
        Message = "The {Cap} cap of {Minutes} minutes fired while provisioning {Browser}; its job was closed and the partial tree removed.")]
    public static partial void CapReached(ILogger logger, string browser, string cap, int minutes);

    /// <summary>Another process holds the machine-wide provisioning mutex.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    [LoggerMessage(
        EventId = 66,
        Level = LogLevel.Information,
        Message = "Another BrowserAI process is already provisioning {Browser}; watching for its marker rather than downloading a second copy.")]
    public static partial void AnotherProcessIsInstalling(ILogger logger, string browser);

    /// <summary>The crash tripwire fired.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="minutes">How long it waited.</param>
    [LoggerMessage(
        EventId = 67,
        Level = LogLevel.Critical,
        Message = "Provisioning {Browser} has not completed after {Minutes} minutes. This is past every cap, so it is a defect rather than a slow link.")]
    public static partial void OuterDeadlineReached(ILogger logger, string browser, int minutes);

    /// <summary>The previous holder of the provisioning mutex died mid-install.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The tree it left.</param>
    [LoggerMessage(
        EventId = 68,
        Level = LogLevel.Warning,
        Message = "The process that was provisioning {Browser} died holding the mutex; removing whatever it left in {Directory} before starting again.")]
    public static partial void AbandonedInstallFound(ILogger logger, string browser, string directory);

    /// <summary>A reinstall was asked for and is deleting the tree.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The tree.</param>
    [LoggerMessage(
        EventId = 69,
        Level = LogLevel.Warning,
        Message = "browserai_reinstall_browser is deleting {Directory} and re-provisioning {Browser}. Nothing on this machine has a browser open from it.")]
    public static partial void Reinstalling(ILogger logger, string browser, string directory);

    /// <summary>
    /// Provisioning threw rather than failing, which is the one shape the caller
    /// cannot infer from a state.
    /// </summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="exception">Why.</param>
    [LoggerMessage(
        EventId = 70,
        Level = LogLevel.Error,
        Message = "Provisioning {Browser} failed. The state is reported to the caller; this is the cause.")]
    public static partial void Failed(ILogger logger, string browser, Exception exception);
}

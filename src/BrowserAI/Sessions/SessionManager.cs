// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Artifacts;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Sessions;

/// <summary>
/// The five authored session tools, and the live sessions they create.
/// </summary>
/// <remarks>
/// <para>
/// <b>The directory is the identity.</b> There is no registry, no mint and no
/// bearer token: a session is a directory, and the caller names it on every call.
/// A token's entire value was guaranteeing the <c>resume</c> warning was
/// displayed, and it is precisely the state that evaporates when a model is
/// compacted — whereas a path is always reconstructible.
/// </para>
/// <para>
/// <b>Every refusal names a recovery that is not the call that just failed.</b>
/// A retry that repeats the failed call is not a recovery, and offering one to a
/// model is how a caller ends up in a loop that never terminates and never
/// explains itself. The wording of every refusal belongs to
/// <see cref="SessionErrors"/>; what this type fixes is that a cause and a route
/// out reach the caller at all.
/// </para>
/// </remarks>
internal sealed class SessionManager : IAsyncDisposable
{
    /// <summary>
    /// How much room a volume must have before a session is created there.
    /// </summary>
    /// <remarks>
    /// First-run browser provisioning needs 203.8 MB down and 430.48 MiB
    /// extracted — both re-measured 2026-08-16 — so peak usage is ~640 MiB while
    /// both the archive and the tree exist. A
    /// refusal here that names the number is recoverable in one turn; a failure
    /// partway through the download is the <c>spawn EFTYPE</c> shape — success
    /// shaped, stderr empty, discovered at first navigation.
    /// </remarks>
    public const long RequiredFreeBytes = 640L * 1024 * 1024;

    /// <summary>The only browser family this build can create a session for.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-16 (previously "Firefox is a later step").</b> The
    /// Firefox <i>locking</i> half is built — the <c>parent.lock</c> preflight,
    /// Restart Manager attribution and the restart-registration preference on
    /// every Firefox launch (<c>FirefoxTests</c>). <b>Offering Firefox as a choice
    /// on <c>init</c> is a different question and is still owed</b>: it needs a
    /// per-family download size for
    /// <see cref="SessionErrors.ProvisioningInProgress"/> (this build quotes
    /// Chromium's 203.8 MB) and a decision about what
    /// <c>browserai_reinstall_browser</c> reinstalls when there are two trees,
    /// neither of which has been taken. Carried in
    /// [TODO.md](../../../TODO.md).
    /// </para>
    /// <para>
    /// <b>The rest of the product is family-parameterised regardless.</b>
    /// Provisioning, the config generator, the launch preflight and the stray
    /// sweep all take the family from the session's own <c>lock.json</c>, so a
    /// record that names Firefox is honoured on <c>resume</c> rather than
    /// silently run as Chromium against a Firefox profile.
    /// </para>
    /// </remarks>
    public const string SupportedBrowser = ProvisionedBrowsers.Chromium;

    private readonly ConcurrentDictionary<string, LiveSession> _live = new(StringComparer.Ordinal);
    private readonly SessionEnvironment _environment;
    private readonly SessionIndex _index;
    private readonly ILogger _logger;
    private readonly Func<JsonRpcNotification, CancellationToken, ValueTask> _relay;
    private int _disposed;

    /// <summary>Creates the manager over one process's environment.</summary>
    /// <param name="environment">Where the index, the payload and the browsers are.</param>
    /// <param name="loggerFactory">The process-wide factory. Each session also gets its own.</param>
    /// <param name="relay">Where a session child's progress notifications go.</param>
    public SessionManager(
        SessionEnvironment environment,
        ILoggerFactory loggerFactory,
        Func<JsonRpcNotification, CancellationToken, ValueTask> relay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(relay);

        _environment = environment;
        _logger = loggerFactory.CreateLogger<SessionManager>();
        _index = new SessionIndex(environment.Paths, _logger);
        _relay = relay;
    }

    /// <summary>
    /// The browsers root, so a refusal can name the directory a human would
    /// actually delete.
    /// </summary>
    public string BrowsersDirectory => _environment.Paths.BrowsersDirectory;

    /// <summary>The session driving a directory in this process, if there is one.</summary>
    /// <param name="directory">The <c>session</c> argument, as it arrived.</param>
    /// <returns>The live session, or <see langword="null"/>.</returns>
    public LiveSession? Find(string? directory)
    {
        if (directory is null)
        {
            return null;
        }

        SessionPath location;

        try
        {
            location = Resolve(directory, "session");
        }
        catch (SessionToolException)
        {
            return null;
        }

        return _live.TryGetValue(location.Key, out var session) ? session : null;
    }

    /// <summary>
    /// Why a <c>session</c> argument resolved to nothing, in terms the caller can
    /// act on in one turn.
    /// </summary>
    /// <remarks>
    /// <b>Three distinguishable causes, three different recoveries.</b> A path
    /// that is not absolute is <see cref="SessionErrors.DirectoryNotAbsolute"/>;
    /// a path with no <c>lock.json</c> is
    /// <see cref="SessionErrors.SessionNamesNoSession"/> and wants <c>init</c>; a
    /// path that <i>is</i> a session this process is not driving wants
    /// <c>resume</c>. Collapsing the last two — as this once did — sends half the
    /// callers to a tool that will refuse them on the next turn with
    /// <see cref="SessionErrors.SessionAlreadyExists"/>.
    /// </remarks>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The <c>session</c> argument, as it arrived.</param>
    /// <returns>The refusal to answer with.</returns>
    public static string ExplainUnknownSession(string tool, string session)
    {
        SessionPath location;

        try
        {
            location = Resolve(session, SessionToolSurface.SessionParameter);
        }
        catch (SessionToolException failure)
        {
            return failure.Message;
        }

        try
        {
            return SessionLock.ReadRecord(location) is null
                ? SessionErrors.SessionNamesNoSession(tool, location.FullPath)
                : SessionErrors.SessionNotOpen(tool, location.FullPath);
        }
        catch (LockFileException failure)
        {
            // A lock.json that cannot be read is still a session, and saying so
            // is more useful than reporting it as absent: the recovery is to fix
            // or destroy the directory, never to init over it.
            return $"'{location.FullPath}' holds a '{SessionLayout.LockFileName}' this build cannot read, so '{tool}' was not run and nothing was changed. {failure.Message}";
        }
    }

    /// <summary>
    /// Why a browser-needing call cannot run yet, or <see langword="null"/> when
    /// it can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every upstream tool, with no exception — and the exception is what the
    /// measurement removed.</b> This was written to let
    /// <c>browser_get_config</c> through, on the strength of a design
    /// claim that <c>browser_get_config</c> keeps working while the download runs
    /// ([kb](../../../kb/playwright/configuration.md#browser-provisioning)). Measured
    /// 2026-08-16 @ <c>@playwright/mcp</c> 0.0.79, twice, against the child
    /// directly with an empty browsers root: it does <b>not</b>. The tool
    /// resolves the browser before it answers and fails
    /// <c>throwIfExecutableMissing</c> — it does not <i>launch</i> anything,
    /// which is why it is cheap on a provisioned machine, but the executable has
    /// to exist.
    /// </para>
    /// <para>
    /// So letting it through bought a worse answer rather than a working one: a
    /// caller would get upstream's "not installed" error, whose advice is to
    /// provision — which is already happening — instead of
    /// <see cref="SessionErrors.ProvisioningInProgress"/>, which says
    /// how large the download is and that the same call will work shortly. What
    /// keeps a downloading session inspectable is BrowserAI's <b>own</b> tools:
    /// <c>browserai_list</c>, <c>browserai_resume</c> and
    /// <c>browserai_set_purpose</c> answer throughout, because none of them needs
    /// a browser.
    /// </para>
    /// <para>
    /// <b>It refuses rather than waiting, and that is the whole non-blocking
    /// design seen from the other end.</b> The session is open, its child is
    /// running, and the very same call succeeds on the next turn once the
    /// download lands — so the recovery is "call this again", which is the one
    /// recovery a model needs no help to perform.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool being called.</param>
    /// <param name="session">The session it named.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public string? ProvisioningRefusal(string tool, LiveSession session)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(session);

        var browser = session.Lock.Record.Browser;

        ProvisioningStatus status;

        try
        {
            status = _environment.Provisioner.Ensure(browser);
        }
#pragma warning disable CA1031 // A probe that throws must not refuse the call: the browser may well be there, and the launch failure below names its own cause.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            SessionToolLog.ProvisioningUnreadable(_logger, browser, failure);
            return null;
        }

        if (status.State is ProvisioningState.Installed)
        {
            return null;
        }

        SessionToolLog.CallRefusedWhileProvisioning(_logger, tool, browser, status.State);

        return status.State is ProvisioningState.Provisioning
            ? SessionErrors.ProvisioningInProgress(tool, browser, status.Directory, BrowserProvisioner.FirstRunDownloadSize)
            : SessionErrors.BrowserRuntimeDidNotStart(session.Location.FullPath, status.Detail);
    }

    /// <summary>Runs one of the authored tools.</summary>
    /// <param name="tool">The tool name, <c>browserai_</c> prefixed.</param>
    /// <param name="arguments">Its arguments, as they arrived.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>What to tell the caller, and whether it is a refusal.</returns>
    public async Task<ToolOutcome> InvokeAsync(string tool, JsonObject? arguments, CancellationToken cancellationToken)
    {
        try
        {
            return tool switch
            {
                SessionToolSurface.Init => await InitAsync(arguments, cancellationToken).ConfigureAwait(false),
                SessionToolSurface.Resume => await ResumeAsync(arguments, cancellationToken).ConfigureAwait(false),
                SessionToolSurface.List => List(arguments),
                SessionToolSurface.Destroy => await DestroyAsync(arguments).ConfigureAwait(false),
                SessionToolSurface.SetPurpose => SetPurpose(arguments),
                SessionToolSurface.ReinstallBrowser => await ReinstallBrowserAsync(cancellationToken).ConfigureAwait(false),
                _ => new ToolOutcome($"'{tool}' is not a BrowserAI session tool. The session tools are: {string.Join(", ", SessionToolSurface.Names)}.", IsError: true),
            };
        }
        catch (SessionToolException failure)
        {
            return new ToolOutcome(failure.Message, IsError: true);
        }
        catch (LockFileException failure)
        {
            return new ToolOutcome(failure.Message, IsError: true);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        foreach (var key in _live.Keys)
        {
            if (_live.TryRemove(key, out var session))
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<ToolOutcome> InitAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var location = Resolve(Required(arguments, "directory"), "directory");
        var purpose = Required(arguments, "purpose");
        var mode = Mode(arguments);
        var browser = Browser(arguments);
        var tracing = Flag(arguments, "tracing") ?? false;
        var consoleLevel = ConsoleLevel(arguments);
        var debug = Flag(arguments, "debug") ?? false;

        // Being made to say "resume" is the point: it converts an accidental
        // collision into a stated intent. There is deliberately no difference
        // between a lost session, a neatly closed one and one this very process
        // has open -- all three must be resumed, and all three get the same
        // refusal naming the purpose, the mode and the date, because the reason
        // a session ended stops being a thing anyone has to model. An earlier
        // version special-cased "already open in this process" and answered
        // first with a shorter message, which hid the informative one behind an
        // accident of who happened to hold the directory.
        if (Existing(location) is { } existing)
        {
            return new ToolOutcome(existing, IsError: true);
        }

        if (FreeSpaceRefusal(location) is { } refusal)
        {
            return new ToolOutcome(refusal, IsError: true);
        }

        try
        {
            SessionLayout.Create(location);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ToolOutcome(
                $"'{location.FullPath}' could not be created ({failure.Message}). Nothing was changed. Name a directory BrowserAI can write to.",
                IsError: true);
        }

        return await OpenAsync(
            location,
            new SessionLockRequest { Mode = mode.Name, Browser = browser, Purpose = purpose },
            mode,
            tracing,
            consoleLevel,
            debug,
            createdHere: true,
            notes: [],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolOutcome> ResumeAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var location = Resolve(Required(arguments, "directory"), "directory");
        var appended = Optional(arguments, "purpose");
        var debug = Flag(arguments, "debug") ?? false;
        var tracing = Flag(arguments, "tracing");
        var consoleLevel = ConsoleLevel(arguments);
        var acknowledgeCopy = Flag(arguments, "acknowledgeCopy") ?? false;

        // A profile is browser-specific and a session cannot change what it is,
        // so a caller asking to resume a Firefox directory as Chromium is
        // stating something impossible. Answering "sure" would be the wrong kind
        // of helpful.
        Refuse(arguments, "mode", "the mode is bound at init and recorded in lock.json");
        Refuse(arguments, "browser", "the browser is bound at init and the profile on disk belongs to it");

        if (_live.TryGetValue(location.Key, out var already))
        {
            return new ToolOutcome(
                Describe(already, ["This session is already open in this BrowserAI; nothing was changed."], RefreshRollUp(location)),
                IsError: false);
        }

        var record = SessionLock.ReadRecord(location)
            ?? throw new SessionToolException(
                $"'{location.FullPath}' has no '{SessionLayout.LockFileName}', so it is not a BrowserAI session and there is nothing to resume. "
                + $"Call {SessionToolSurface.Init} to create one there, or name the directory of a session that exists — {SessionToolSurface.List} will show what is under a path.");

        var mode = SessionModes.Recorded(record.Mode);
        var notes = new List<string>();
        string? movedFrom = null;

        // The directory is the identity; the path in lock.json is provenance. A
        // move leaves nothing behind and a copy leaves the original standing, so
        // the recorded path already discriminates and no fingerprint field is
        // needed.
        if (!SamePath(record.Directory, location))
        {
            if (Directory.Exists(record.Directory) && !acknowledgeCopy)
            {
                return new ToolOutcome(
                    SessionErrors.DirectoryIsACopy(location.FullPath, record.Directory, record.Mode, record.Purpose),
                    IsError: true);
            }

            notes.Add(Directory.Exists(record.Directory)
                ? $"This directory is a COPY of the session at '{record.Directory}', which still exists, and you acknowledged that. Its recorded purpose and history describe the original, not this copy."
                : $"This directory was moved or renamed: its record said '{record.Directory}', which no longer exists. The record has been repaired to '{location.FullPath}'.");

            // Recorded rather than logged here. The interesting log is the
            // SESSION's own, beside its lock.json, and that file does not exist
            // until OpenAsync has opened it -- so the line is written there,
            // where whoever is looking into this directory will find it.
            movedFrom = Directory.Exists(record.Directory) ? null : record.Directory;
        }

        var purpose = appended is null
            ? record.Purpose
            : $"{record.Purpose} | {appended}";

        return await OpenAsync(
            location,
            new SessionLockRequest { Mode = record.Mode, Browser = record.Browser, Purpose = purpose },
            mode,
            tracing ?? false,
            consoleLevel,
            debug,
            createdHere: false,
            notes,
            cancellationToken,
            movedFrom).ConfigureAwait(false);
    }

    private ToolOutcome List(JsonObject? arguments)
    {
        var (root, prefix) = Subtree(Required(arguments, "directory"));
        var lines = new List<string>();
        var found = 0;

        foreach (var entry in _index.Follow())
        {
            if (entry.Session is not { } session || entry.Record is not { } record || !IsUnder(session, prefix))
            {
                continue;
            }

            found++;
            var size = SessionLayout.SizeOnDisk(session.FullPath);

            lines.Add(
                $"{session.FullPath}\n"
                + $"  mode: {record.Mode}   browser: {record.Browser}   size on disk: {Megabytes(size)}\n"
                + $"  created: {Stamp(record.Created)}   last used: {Stamp(record.LastUsed)}\n"
                + $"  {SessionErrors.Recorded(record.Purpose)}");
        }

        return found is 0
            ? new ToolOutcome(
                $"No BrowserAI sessions under '{root}'. That is an answer rather than an error: sessions live wherever a caller put them, and this tool only reports what is under the path you named.",
                IsError: false)
            : new ToolOutcome(
                $"{found.ToString(CultureInfo.InvariantCulture)} session(s) under '{root}':\n\n" + string.Join("\n\n", lines),
                IsError: false);
    }

    private async Task<ToolOutcome> DestroyAsync(JsonObject? arguments)
    {
        var location = Resolve(Required(arguments, "directory"), "directory");

        if (_live.TryRemove(location.Key, out var live))
        {
            // Torn down first: the browser has to go before the tree it is
            // writing into, and this process's own handles on lock.json and the
            // session log have to be closed before either can be deleted.
            await live.DisposeAsync().ConfigureAwait(false);
        }

        // The single check that makes it safe to hand a model a tool that
        // deletes trees: it cannot be aimed at Documents\.
        var record = SessionLock.ReadRecord(location)
            ?? throw new SessionToolException(
                $"'{location.FullPath}' has no '{SessionLayout.LockFileName}', so it is not a BrowserAI session and {SessionToolSurface.Destroy} will not touch it. "
                + "This refusal is what makes the tool safe: it deletes session directories and nothing else. Nothing was changed.");

        var taken = SessionLock.TryAcquire(
            location,
            new SessionLockRequest { Mode = record.Mode, Browser = record.Browser, Purpose = record.Purpose },
            _logger);

        if (taken.Acquired is not { } held)
        {
            return new ToolOutcome($"'{location.FullPath}' was not destroyed. {taken.Message}", IsError: true);
        }

        // ⚠️ Corrected 2026-08-18 (previously `held.Dispose()` on this line,
        // defended as "Windows will not remove a file this process is holding
        // open, and the lock has done its job the moment ownership is proven").
        // The first half is true of lock.json and the second half is the defect:
        // ownership was proven for an instant and the DELETE happened outside
        // it, with the recursive walk below in between. A peer that resumed in
        // that gap was told it owned the directory, launched a browser into
        // profile\, and had its tree removed underneath it -- while this call
        // reported the peer's own open files as "something still has them open".
        // The window is a full walk of a Chromium profile wide, not microseconds
        // (docs/reviews/2026-08-18-adversarial-locking.md, A1).
        var size = SessionLayout.SizeOnDisk(location.FullPath);
        var failures = new List<string>();

        // §E's one shared routine, which browserai_reinstall_browser also uses:
        // a post-order walk with a try/catch per node, so one file a browser or
        // a virus scanner still holds open costs that file rather than the whole
        // call. A destroy that silently left half a tree would be the founding
        // failure shape.
        //
        // This pass runs with lock.json STILL HELD, so it removes everything
        // except lock.json and the directory above it. Its failure list is
        // discarded because those two are in it by construction and neither is
        // news; the pass below is the one whose survivors the caller is told
        // about, and it re-tries anything this one could not take.
        TreeDelete.Remove(location.FullPath, []);

        // Release and finish inside one hold of the per-directory gate, so the
        // instant in which lock.json is unheld and still on disk is an instant
        // no peer's create-or-take can be inside. The gate is held across two
        // unlinks in the ordinary case -- everything else is already gone.
        held.ReleaseAndDelete(() => TreeDelete.Remove(location.FullPath, failures));

        _index.Forget(location);
        var rollUp = RefreshRollUp(location);
        SessionToolLog.Destroyed(_logger, location.FullPath, failures.Count);

        var summary =
            $"Destroyed the '{record.Mode}' session at '{location.FullPath}' ({Megabytes(size)}). Its purpose was: {record.Purpose}";

        if (!rollUp.RolledUp && Path.GetDirectoryName(location.FullPath) is { Length: > 0 } parent)
        {
            // The destroyed session is still listed in the file until this
            // succeeds, and a roll-up naming a directory that is gone is worse
            // than one that is merely behind.
            summary +=
                $"\n\n⚠️ The roll-up at '{Path.Combine(parent, ArtifactRouter.RollUpFileName)}' could not be rewritten, so it still lists this session. Nothing else depends on it: browserai_list reads BrowserAI's own index.";
        }

        return failures.Count is 0
            ? new ToolOutcome(summary, IsError: false)
            : new ToolOutcome(
                $"{summary}\n\nBUT {failures.Count.ToString(CultureInfo.InvariantCulture)} item(s) could not be removed, because something still has them open:\n"
                + string.Join("\n", failures.Take(20))
                + "\nThe session's record is gone, so the directory is no longer a session; delete what is left once whatever holds it has exited.",
                IsError: false);
    }

    private ToolOutcome SetPurpose(JsonObject? arguments)
    {
        var location = Resolve(Required(arguments, "session"), "session");
        var purpose = LockRecord.SanitisePurpose(Required(arguments, "purpose"));

        if (_live.TryGetValue(location.Key, out var live))
        {
            var previous = live.Lock.Record.Purpose;
            live.Lock.Rewrite(record => Repurpose(record, purpose));
            SessionToolLog.PurposeChanged(_logger, location.FullPath, previous, purpose);

            return new ToolOutcome($"Purpose of '{location.FullPath}' is now: {purpose}\nIt was: {previous}", IsError: false);
        }

        var recorded = SessionLock.ReadRecord(location)
            ?? throw new SessionToolException(
                $"'{location.FullPath}' has no '{SessionLayout.LockFileName}', so it is not a BrowserAI session and has no purpose to set. Nothing was changed.");

        var taken = SessionLock.TryAcquire(
            location,
            new SessionLockRequest { Mode = recorded.Mode, Browser = recorded.Browser, Purpose = purpose },
            _logger);

        if (taken.Acquired is not { } held)
        {
            return new ToolOutcome($"The purpose of '{location.FullPath}' was not changed. {taken.Message}", IsError: true);
        }

        held.Dispose();
        SessionToolLog.PurposeChanged(_logger, location.FullPath, recorded.Purpose, purpose);

        return new ToolOutcome(
            $"Purpose of '{location.FullPath}' is now: {purpose}\nIt was: {recorded.Purpose}\nThe session was not running, so it was updated in place and left closed.",
            IsError: false);
    }

    /// <summary>
    /// The sixth authored tool: delete the shared browser tree and download it
    /// again, or refuse and say what is using it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It refuses rather than coordinates, and that is the design rather than
    /// a limitation.</b> The browser install is shared by every session on the
    /// machine, so "make this safe" would mean terminating browsers other agents
    /// are driving. There is deliberately no force argument: force here has
    /// exactly one meaning and it is the wrong one.
    /// </para>
    /// <para>
    /// <b>Download-beside-and-swap does not work on Windows</b>, which is what
    /// makes the refusal load-bearing rather than merely polite: a directory
    /// holding open executables cannot be renamed, so there is no arrangement in
    /// which the old tree keeps serving while a new one is fetched. The window
    /// with no browser installed is unavoidable and is stated.
    /// </para>
    /// <para>
    /// <b>The check here answers "is anything RUNNING FROM the tree", and that is
    /// half the question.</b> A session that opened a browser between the check
    /// and the delete makes the delete fail on an open executable, and the
    /// outcome reports exactly which files survived — so <i>that</i> race
    /// produces a refusal with evidence rather than a corrupted tree.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously "No extra lock is taken around the
    /// check-then-delete, and the reason is that the delete is itself the guard …
    /// Taking the provisioning mutex here instead would deadlock against the
    /// installer, which takes it on its own thread").</b> Both halves were wrong.
    /// The delete guards against running <b>executables</b> and the case that
    /// corrupts is a <b>writer</b>: a concurrent installer is <c>node.exe</c> out
    /// of the payload, extracting <i>into</i> the tree, which
    /// <see cref="BrowserProcesses.RunningFrom"/> cannot see at all. And a
    /// different thread taking a mutex is a wait rather than a deadlock — the
    /// real obstacle was thread affinity in an <c>async</c> method, which
    /// <c>BrowserProvisioner.ReinstallAsync</c> now solves the same way
    /// <c>BrowserProvisioner.Start</c> always did. <b>The second question is
    /// asked there, under the provisioning mutex</b>, because that is where the
    /// answer can be held across the delete.
    /// </para>
    /// </remarks>
    private async Task<ToolOutcome> ReinstallBrowserAsync(CancellationToken cancellationToken)
    {
        var browser = SupportedBrowser;
        var directory = _environment.Provisioner.DirectoryFor(browser);

        // Every process running an executable out of the tree about to be
        // deleted, found by full image path and never by image name.
        IReadOnlyList<RunningImage> running;

        try
        {
            running = BrowserProcesses.RunningFrom(directory);
        }
        catch (Win32Exception failure)
        {
            return new ToolOutcome(
                $"{SessionToolSurface.ReinstallBrowser} was not run: BrowserAI could not enumerate processes to check whether a browser is still using '{directory}' ({failure.Message}). Nothing was changed. "
                + "It refuses rather than guessing, because deleting a browser tree that something is running from leaves a directory that is neither the old install nor the new one.",
                IsError: true);
        }

        if (running.Count is not 0)
        {
            var claimants = LiveSessions();

            if (claimants.Count is not 0)
            {
                return new ToolOutcome(
                    $"{SessionToolSurface.ReinstallBrowser} was not run: {running.Count.ToString(CultureInfo.InvariantCulture)} process(es) are running from '{directory}', and these sessions are open on this machine:\n"
                    + string.Join("\n", claimants.Take(20))
                    + "\nNothing was changed and nothing was terminated. There is deliberately no force option — forcing here means killing browsers other agents are driving. Close those sessions, or wait, and call this tool again.",
                    IsError: true);
            }

            // Live browsers, and no session anywhere accounts for them. §H.4
            // row 13: reported, never terminated.
            return new ToolOutcome(
                SessionErrors.UnattributableBrowserRunning(
                    SessionToolSurface.ReinstallBrowser,
                    directory,
                    [.. running.Select(entry => (entry.ProcessId, entry.ImagePath))]),
                IsError: true);
        }

        var outcome = await _environment.Provisioner.ReinstallAsync(browser, cancellationToken).ConfigureAwait(false);

        if (!outcome.Deleted)
        {
            // Nothing happened, and the answer says only that. Every sentence
            // below asserts a delete, and a tool answer that claims a
            // destructive act it did not perform is worse than a refusal.
            return new ToolOutcome(
                $"{SessionToolSurface.ReinstallBrowser} was not run and nothing was changed. {outcome.Status.Detail}",
                IsError: true);
        }

        var removed = outcome.RemovedBytes < 0
            ? "an amount that could not be measured"
            : Megabytes(outcome.RemovedBytes);

        if (outcome.Failures.Count is not 0)
        {
            return new ToolOutcome(
                $"'{outcome.Directory}' was only partly removed, so nothing was downloaded on top of it and the browser install is now incomplete. {outcome.Failures.Count.ToString(CultureInfo.InvariantCulture)} item(s) survived:\n"
                + string.Join("\n", outcome.Failures.Take(20))
                + $"\nSomething still has those files open. Once it has exited, call {SessionToolSurface.ReinstallBrowser} again — it will delete what is left and download a complete tree.",
                IsError: true);
        }

        return outcome.Status.State is ProvisioningState.Installed
            ? new ToolOutcome(
                $"Re-provisioned {browser}. '{outcome.Directory}' was deleted ({removed}) and downloaded again. {outcome.Status.Detail}",
                IsError: false)
            : new ToolOutcome(
                $"'{outcome.Directory}' was deleted ({removed}) and the download that should have replaced it did not complete, so there is no browser installed now. {outcome.Status.Detail} "
                + $"Call {SessionToolSurface.ReinstallBrowser} again once the cause is fixed; {SessionToolSurface.Init} also starts a download and returns immediately.",
                IsError: true);
    }

    /// <summary>
    /// Every session open on this machine, as lines a refusal can name.
    /// </summary>
    /// <remarks>
    /// <b>Two sources, because neither alone is "anywhere".</b> The sessions this
    /// process is driving are known directly; sessions driven by <i>another</i>
    /// BrowserAI are found through the index, whose entries are followed to a
    /// <c>lock.json</c> whose holder is checked with
    /// <see cref="ProcessLiveness.IsAlive"/> — pid and creation time together,
    /// never a pid alone, because Windows reuses pids and a reclaim keyed on one
    /// eventually reads a stranger as the holder.
    /// </remarks>
    private List<string> LiveSessions()
    {
        var lines = new List<string>();

        foreach (var session in _live.Values)
        {
            lines.Add($"  {session.Location.FullPath} — open in this BrowserAI (mode '{session.Mode.Name}')");
        }

        foreach (var entry in _index.Follow())
        {
            if (entry.Session is not { } session || entry.Record is not { } record)
            {
                continue;
            }

            if (_live.ContainsKey(session.Key))
            {
                continue;
            }

            if (ProcessLiveness.IsAlive(record.Holder.ProcessId, record.Holder.ProcessCreatedFileTime))
            {
                lines.Add($"  {session.FullPath} — held by PID {record.Holder.ProcessId.ToString(CultureInfo.InvariantCulture)} since {Stamp(record.LastUsed)}");
            }
        }

        return lines;
    }

    private static LockRecord Repurpose(LockRecord record, string purpose)
    {
        var history = new List<string>(record.PurposeHistory);

        if (history.Count is 0 || !string.Equals(history[^1], purpose, StringComparison.Ordinal))
        {
            history.Add(purpose);
        }

        return record with { Purpose = purpose, PurposeHistory = history, LastUsed = DateTimeOffset.Now };
    }

    private async Task<ToolOutcome> OpenAsync(
        SessionPath location,
        SessionLockRequest request,
        SessionModeDefinition mode,
        bool tracing,
        string consoleLevel,
        bool debug,
        bool createdHere,
        IReadOnlyList<string> notes,
        CancellationToken cancellationToken,
        string? movedFrom = null)
    {
        // Nothing below is owned by anyone until the session is in the
        // dictionary, and the finally disposes whatever is left. That is the only
        // shape in which a half-built session cannot leave a directory locked, a
        // node child running, or a log file open.
        var handedOver = false;
        SessionLogging? logging = null;
        SessionLock? acquired = null;
        ChildConnection? child = null;
        LiveSession? session = null;

        try
        {
            // The session's own log is opened FIRST, before the lock, so that
            // taking the lock is one of the things it records. Every line about
            // this directory then lands beside its lock.json, where whoever is
            // debugging this session will look, as well as in the machine-wide
            // process log. An earlier version acquired first and logged the
            // acquisition to the process log alone, which left the session's own
            // file starting mid-story.
            logging = _environment.OpenSessionLog(location.FullPath, debug ? LogLevel.Debug : LogLevel.Information);
            var sessionLogger = logging.Factory.CreateLogger<SessionManager>();

            if (movedFrom is not null)
            {
                SessionToolLog.DirectoryMoved(sessionLogger, movedFrom, location.FullPath);
            }

            var taken = SessionLock.TryAcquire(location, request, sessionLogger);

            if (taken.Acquired is not { } held)
            {
                return new ToolOutcome(taken.Message, IsError: true);
            }

            acquired = held;

            // The family comes from the session's own record rather than from a
            // constant: `resume` reads it out of lock.json, and a profile
            // belongs to the browser that made it.
            var config = BrowserConfiguration.ForSession(location, mode, request.Browser, tracing, consoleLevel);
            var configFile = Path.Combine(
                _environment.InstanceDirectory,
                $"playwright-mcp-{location.Hash[..16]}.json");

            var artifacts = new ArtifactRouter(location);

            var options = ChildLaunch.Create(
                _environment.Payload,
                _environment.Paths.BrowsersDirectory,

                // The OUTPUT root rather than the session root, which is the
                // first and cheapest of the artifact-routing levers: upstream
                // resolves a relative `filename` against the child's cwd, so a
                // bare `foo.png` that nothing rewrote still lands inside the
                // instance tree by construction rather than in whatever
                // directory the client happened to be started from. It is also
                // what upstream's own `checkFile` measures against, so the two
                // allowed roots coincide instead of overlapping.
                artifacts.OutputRoot,
                configFile,
                config,
                name: $"playwright-mcp[{location.Hash[..8]}]");

            // CA2000 is disabled for these two statements and nothing else.
            // Ownership moves into the live session and then into the dictionary
            // that DisposeAsync drains, and the finally below covers every path
            // that does not get there -- but both types are IAsyncDisposable
            // rather than IDisposable, and the rule's dataflow does not follow an
            // `await x.DisposeAsync()` in a finally.
#pragma warning disable CA2000
            child = await _environment.ConnectChild(
                options,
                logging.Factory,
                $"browserai-{location.Hash[..8]}-",
                _relay,
                cancellationToken).ConfigureAwait(false);

            session = new LiveSession(location, held, mode, child, logging, config, configFile, createdHere, artifacts, _environment.BrowserIdlePeriod, _environment.Clock);
#pragma warning restore CA2000

            if (!_live.TryAdd(location.Key, session))
            {
                return new ToolOutcome(
                    $"'{location.FullPath}' was opened by another call on this connection while this one was starting. Nothing was changed by this call; use the session that exists.",
                    IsError: true);
            }

            // Everything above is now owned by the dictionary.
            handedOver = true;
            _index.Record(location);
            SessionToolLog.Opened(sessionLogger, location.FullPath, mode.Name, createdHere);

            // One index walk, used twice: the roll-up beside the sessions and
            // the line in this answer that names them are the same question, and
            // walking twice is the shape that made this the slowest call in the
            // suite.
            return new ToolOutcome(Describe(session, notes, RefreshRollUp(location)), IsError: false);
        }
        catch (FirefoxProfileLockedException collision)
        {
            // Row 11 rather than row 7, because it is not a runtime that failed
            // to start: nothing was started, deliberately, and the sentence
            // already names the recovery. Reachable through `resume`, which
            // reads the family out of lock.json -- `init` cannot record Firefox
            // in this build (see SupportedBrowser).
            SessionToolLog.CouldNotOpen(_logger, location.FullPath, collision);

            return new ToolOutcome(collision.Message, IsError: true);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            SessionToolLog.CouldNotOpen(_logger, location.FullPath, failure);

            return new ToolOutcome(
                SessionErrors.BrowserRuntimeDidNotStart(location.FullPath, $"{failure.GetType().Name}: {failure.Message}"),
                IsError: true);
        }
        finally
        {
            if (!handedOver)
            {
                if (session is not null)
                {
                    // Built, but never reached the dictionary. Disposing it
                    // releases the lock, the child and the log in one go.
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    if (child is not null)
                    {
                        await child.DisposeAsync().ConfigureAwait(false);
                    }

                    // A failed open must not leave the directory owned. The
                    // record stays on disk, which is what makes the next resume
                    // able to say who had it.
                    acquired?.Dispose();

                    // Last, so the release above is still recorded in it.
                    logging?.Dispose();
                }
            }
        }
    }

    private string Describe(
        LiveSession session,
        IReadOnlyList<string> notes,
        (List<RollUpEntry> Beneath, bool RolledUp) rollUp)
    {
        var beneath = rollUp.Beneath;

        var record = session.Lock.Record;
        var text = new StringBuilder();

        foreach (var note in notes)
        {
            _ = text.Append("NOTE: ").Append(note).Append('\n');
        }

        if (!session.CreatedHere && !session.NoticeGiven)
        {
            session.NoticeGiven = true;

            _ = text.Append("NOTE: you are driving a session this connection did not create; it was opened ")
                .Append(Stamp(record.Created))
                .Append(". Another agent may be using it.\n");
        }

        if (session.Lock.GateWasAbandoned)
        {
            _ = text.Append("NOTE: the per-directory lock was found abandoned by a process that died holding it. The session was taken, but whatever that process was writing may be incomplete.\n");
        }

        if (text.Length is not 0)
        {
            _ = text.Append('\n');
        }

        _ = text
            .Append("Session ready. Pass session='").Append(session.Location.FullPath).Append("' on every browser tool call.\n")
            .Append("  directory: ").Append(session.Location.FullPath).Append('\n')
            .Append("  mode: ").Append(session.Mode.Name).Append(" — ").Append(session.Mode.Grants).Append('\n')
            .Append("  browser: ").Append(record.Browser).Append('\n')
            .Append("  profile: ").Append(Path.Combine(session.Location.FullPath, SessionLayout.ProfileFolderName)).Append('\n')
            .Append("  output: ").Append(Path.Combine(session.Location.FullPath, SessionLayout.OutputFolderName)).Append('\n')
            .Append("  downloads: ").Append(Path.Combine(session.Location.FullPath, SessionLayout.DownloadsFolderName)).Append('\n')
            .Append("  log: ").Append(session.Logging.Path).Append('\n')
            .Append("  artifact index: ").Append(Path.Combine(session.Location.FullPath, ArtifactRouter.IndexFileName))
            .Append(" — pass a plain 'filename' such as login.png on any tool that takes one; BrowserAI files it by kind under output\\ and tells you the full path.\n")
            .Append("  purpose: ").Append(record.Purpose).Append('\n')
            .Append("  created: ").Append(Stamp(record.Created)).Append("   last used: ").Append(Stamp(record.LastUsed)).Append('\n')
            .Append("  child protocol: ").Append(session.Child.NegotiatedProtocolVersion ?? "<none>").Append('\n')
            .Append("  browserProvisioning: ").Append(Provisioning(record.Browser)).Append('\n');

        if (record.PurposeHistory.Count > 1)
        {
            _ = text.Append("  purpose recorded by previous sessions, oldest first: ")
                .Append(string.Join(" / ", record.PurposeHistory.Take(record.PurposeHistory.Count - 1)))
                .Append('\n');
        }

        // Scoped to the root this session sits under, never machine-wide: an
        // aggregate over everything would pull unrelated projects' sessions into
        // whatever context happens to be open, and the paths were the caller's
        // own choice rather than a boundary.
        if (Path.GetDirectoryName(session.Location.FullPath) is { Length: > 0 } root)
        {
            var siblings = beneath.Where(entry => !string.Equals(entry.Directory, session.Location.FullPath, StringComparison.OrdinalIgnoreCase)).ToList();

            _ = text.Append("  other sessions under ").Append(root).Append(": ")
                .Append(siblings.Count.ToString(CultureInfo.InvariantCulture));

            if (siblings.Count is not 0)
            {
                _ = text.Append(" — ").Append(string.Join(", ", siblings.Take(10).Select(entry => Path.GetFileName(entry.Directory))));
            }

            _ = text.Append(rollUp.RolledUp ? " (rolled up in " : " (⚠️ the roll-up at ")
                .Append(Path.Combine(root, ArtifactRouter.RollUpFileName))
                .Append(rollUp.RolledUp
                    ? ")\n"
                    : " COULD NOT BE WRITTEN on this call, so it is stale or absent; the count above came from BrowserAI's own index and is current)\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Starts provisioning if it has not happened, and answers <b>without
    /// waiting for it</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the line <c>init</c> must not block on.</b> A caller held for
    /// three minutes inside one tool call has had whatever timing it was managing
    /// corrupted, with nothing to read and no basis for deciding whether to keep
    /// waiting. So the download starts here and the answer says so; every upstream
    /// call is refused with
    /// <see cref="SessionErrors.ProvisioningInProgress"/> meanwhile — including
    /// <c>browser_get_config</c>, which resolves the executable before it answers
    /// — BrowserAI's own tools keep working throughout, and the same child
    /// navigates once the install lands.
    /// </para>
    /// <para>
    /// The word in the result is the state itself — <c>installed</c>,
    /// <c>provisioning</c>, <c>failed</c> — because a caller that has to parse
    /// English to find out whether a navigation will work is one upstream wording
    /// change away from getting it wrong.
    /// </para>
    /// <para>
    /// ⚠️ <b>The middle word became <c>provisioning</c> on 2026-08-18 (previously
    /// <c>downloading</c>).</b> One word covers five phases — waiting on another
    /// process's provisioning mutex, deleting an abandoned tree, downloading,
    /// extracting, and pruning superseded revisions — and only one of them is a
    /// download. The cached-run path reaches the mutex-waiter routinely, so the
    /// misleading phase was not the rare one. There is no fourth word for it:
    /// the three buckets a caller acts on are <i>installed</i> / <i>not yet</i> /
    /// <i>failed</i>, and a mutex-loser belongs in the middle exactly as a
    /// downloader does. What separates the five is
    /// <see cref="ProvisioningStatus.Detail"/>, the sentence printed beside the
    /// word — and both of its unfinished branches were given an explicit "wait
    /// and call the same tool again" at the same time, because <c>downloading</c>
    /// implied a recovery that <c>provisioning</c> does not. <c>QUESTIONS.md</c>
    /// §9 records the decision.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family this session was created for.</param>
    /// <returns>The state, lower-cased, and one sentence of detail.</returns>
    private string Provisioning(string browser)
    {
        try
        {
            var status = _environment.Provisioner.Ensure(browser);

            // Spelled out rather than derived from the enum's name: the word is
            // part of the surface a caller reads, and ToLowerInvariant on an
            // enum name would make renaming a C# member a silent change to what
            // the model is told.
            var word = status.State switch
            {
                ProvisioningState.Installed => "installed",
                ProvisioningState.Provisioning => "provisioning",
                _ => "failed",
            };

            return $"{word} — {status.Detail}";
        }
#pragma warning disable CA1031 // A provisioning probe that throws must not fail an init: the session is usable, and the answer says what could not be read.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return $"unknown — the browsers root '{_environment.Paths.BrowsersDirectory}' could not be examined: {failure.Message}";
        }
    }

    private static string? Existing(SessionPath location)
    {
        LockRecord? record;

        try
        {
            record = SessionLock.ReadRecord(location);
        }
        catch (LockFileException failure)
        {
            return $"'{location.FullPath}' already holds a '{SessionLayout.LockFileName}', and it cannot be read: {failure.Message} "
                + $"{SessionToolSurface.Init} will not overwrite a session record it does not understand.";
        }

        return record is null
            ? null
            : SessionErrors.SessionAlreadyExists(
                location.FullPath,
                record.Mode,
                record.Browser,
                record.Created,
                record.LastUsed,
                record.Purpose);
    }

    private string? FreeSpaceRefusal(SessionPath location)
    {
        // A volume whose free space cannot be queried in one call -- a network
        // share, most often -- reports null and is skipped rather than replaced
        // by a directory walk: the check exists to be cheap, and an expensive
        // substitute would cost every session start for a case that fails loudly
        // later anyway.
        var free = _environment.FreeBytesOn(location.FullPath);

        return free is not (>= 0 and < RequiredFreeBytes)
            ? null
            : SessionErrors.InsufficientDisk(location.FullPath, free.Value, RequiredFreeBytes);
    }

    private static bool SamePath(string recorded, SessionPath location)
    {
        try
        {
            return string.Equals(SessionPath.Resolve(recorded).Key, location.Key, StringComparison.Ordinal);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A recorded path this build cannot canonicalise is not this
            // directory, and the move/copy test below settles what to do about
            // it by asking whether it exists.
            return false;
        }
    }

    /// <summary>
    /// The subtree <c>browserai_list</c> was pointed at, as a display path and as
    /// a case-folded prefix.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <see cref="SessionPath.Resolve"/>.</b> That refuses a
    /// volume root, because a volume root is not a session directory — and a
    /// volume root is exactly what a caller passes to <c>list</c> to see
    /// everything. The two are different questions about the same string, and
    /// conflating them would either break <c>list</c> or let <c>init</c> take a
    /// whole drive.
    /// </remarks>
    private static (string Root, string Prefix) Subtree(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new SessionToolException(
                $"'directory' must be an absolute path, and '{directory}' is not. There is no unscoped list: breadth is stated rather than assumed, so pass the tree you mean — a drive root to see everything.");
        }

        string full;

        try
        {
            full = Path.GetFullPath(directory);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SessionToolException($"'directory' = '{directory}' is not a usable directory path: {failure.Message}");
        }

        var key = full.ToUpperInvariant();

        return (full, key.EndsWith(Path.DirectorySeparatorChar) ? key : key + Path.DirectorySeparatorChar);
    }

    private static bool IsUnder(SessionPath candidate, string prefix) =>
        (candidate.Key + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites the roll-up covering the root one session sits under.
    /// </summary>
    /// <remarks>
    /// <b>Scoped by root and never by machine.</b> The machine-wide index is
    /// <see cref="SessionIndex"/>'s and stays available through
    /// <c>browserai_list</c>; this is the aggregate that sits <i>beside</i> the
    /// sessions it covers, so an agent working in one tree meets that tree's
    /// sessions and nobody else's.
    /// </remarks>
    /// <param name="location">The session whose root is rolled up.</param>
    /// <returns>
    /// Every session under that root, and whether the roll-up file was actually
    /// rewritten. <b>The second half is not optional</b>: the answer this feeds
    /// names the file by path, and naming a file that was not written is the
    /// silent failure this product exists to remove. It read
    /// <c>ArtifactRouter.WriteRollUp(root, beneath);</c> — discarding the answer
    /// its own doc comment says the caller must carry — until 2026-08-16.
    /// </returns>
    private (List<RollUpEntry> Beneath, bool RolledUp) RefreshRollUp(SessionPath location)
    {
        var root = Path.GetDirectoryName(location.FullPath);

        if (root is null or { Length: 0 })
        {
            // Unreachable through SessionPath, which refuses a volume root -- but
            // this method is not the place to prove that.
            return ([], true);
        }

        var beneath = Beneath(root);

        return (beneath, ArtifactRouter.WriteRollUp(root, beneath));
    }

    /// <summary>Every session under a root, newest use first.</summary>
    private List<RollUpEntry> Beneath(string root)
    {
        var prefix = root.ToUpperInvariant();
        prefix = prefix.EndsWith(Path.DirectorySeparatorChar) ? prefix : prefix + Path.DirectorySeparatorChar;

        var entries = new List<RollUpEntry>();

        foreach (var entry in _index.Follow())
        {
            if (entry.Session is not { } session || entry.Record is not { } record || !IsUnder(session, prefix))
            {
                continue;
            }

            entries.Add(new RollUpEntry(
                session.FullPath,
                record.Mode,
                record.Purpose,
                record.Created,
                record.LastUsed,
                SessionLayout.SizeOnDisk(session.FullPath)));
        }

        entries.Sort((left, right) => right.LastUsed.CompareTo(left.LastUsed));

        return entries;
    }

    private static string Megabytes(long bytes) =>
        ((double)bytes / (1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    private static SessionPath Resolve(string directory, string argument)
    {
        // Rejected outright, never normalised into something that happens to
        // work. A relative path in particular must not reach GetFullPath, which
        // would resolve it against whatever working directory this process
        // happens to have -- a different directory per process, and never the
        // one the caller meant.
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new SessionToolException(SessionErrors.DirectoryNotAbsolute(argument, directory));
        }

        try
        {
            return SessionPath.Resolve(directory);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SessionToolException(SessionErrors.DirectoryUnusable(argument, directory, failure.Message));
        }
    }

    private static string Required(JsonObject? arguments, string name) =>
        Optional(arguments, name)
        ?? throw new SessionToolException(
            $"'{name}' is required and was not given. BrowserAI has no default for it, because a default is a decision made on the caller's behalf that the caller never notices making.");

    private static string? Optional(JsonObject? arguments, string name)
    {
        if (arguments?[name] is not { } value || value.GetValueKind() is JsonValueKind.Null)
        {
            return null;
        }

        if (value.GetValueKind() is not JsonValueKind.String)
        {
            throw new SessionToolException($"'{name}' must be a string, and it arrived as {value.GetValueKind()}.");
        }

        var text = value.GetValue<string>();

        return string.IsNullOrWhiteSpace(text)
            ? throw new SessionToolException($"'{name}' was given as an empty string, which names nothing. Give it a value or leave it out.")
            : text;
    }

    private static bool? Flag(JsonObject? arguments, string name)
    {
        if (arguments?[name] is not { } value || value.GetValueKind() is JsonValueKind.Null)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new SessionToolException($"'{name}' must be true or false, and it arrived as {value.GetValueKind()}."),
        };
    }

    private static void Refuse(JsonObject? arguments, string name, string why)
    {
        if (arguments?[name] is not null)
        {
            throw new SessionToolException(SessionErrors.ArgumentNotAcceptedOnResume(name, why));
        }
    }

    private static SessionModeDefinition Mode(JsonObject? arguments)
    {
        var name = Required(arguments, "mode");

        return SessionModes.Find(name)
            ?? throw new SessionToolException(
                $"'{name}' is not a BrowserAI session mode. The three are: {SessionModes.Table} There is deliberately no default: a mode chosen by omission is a security posture nobody decided on, and the whole point of 'interactive' is that a human relies on it.");
    }

    private static string Browser(JsonObject? arguments)
    {
        var name = Optional(arguments, "browser") ?? SupportedBrowser;

        return string.Equals(name, SupportedBrowser, StringComparison.OrdinalIgnoreCase)
            ? SupportedBrowser
            : throw new SessionToolException(
                $"'{name}' is not a browser this build of BrowserAI can create a session for. Only '{SupportedBrowser}' is supported. Nothing was changed.");
    }

    private static string ConsoleLevel(JsonObject? arguments)
    {
        var level = Optional(arguments, "consoleLevel") ?? BrowserConfiguration.DefaultConsoleLevel;

        return BrowserConfiguration.ConsoleLevels.Contains(level, StringComparer.Ordinal)
            ? level
            : throw new SessionToolException(
                $"'consoleLevel' must be one of {string.Join(", ", BrowserConfiguration.ConsoleLevels)}, and '{level}' is not. Note that the default, '{BrowserConfiguration.DefaultConsoleLevel}', silently drops debug messages.");
    }
}

/// <summary>What one authored tool produced.</summary>
/// <param name="Text">What to tell the caller.</param>
/// <param name="IsError">Whether this is a refusal rather than an answer.</param>
internal sealed record ToolOutcome(string Text, bool IsError);

/// <summary>
/// An authored tool refusing what it was asked, with a recovery in its message.
/// </summary>
internal sealed class SessionToolException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public SessionToolException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and how to recover.</param>
    public SessionToolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and how to recover.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public SessionToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Source-generated log messages for the session tools.</summary>
/// <remarks>Event ids start at 40, after <see cref="SessionLog"/>'s 1–8 and <see cref="SessionIndexLog"/>'s 20s.</remarks>
internal static partial class SessionToolLog
{
    /// <summary>A session was opened, by init or by resume.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="mode">The mode bound at creation.</param>
    /// <param name="createdHere">Whether this connection created it.</param>
    [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "Session open at {Directory} in mode {Mode}; created by this connection: {CreatedHere}.")]
    public static partial void Opened(ILogger logger, string directory, string mode, bool createdHere);

    /// <summary>A resume found the recorded path gone and repaired the record.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="recorded">The path the record named.</param>
    /// <param name="actual">Where the directory is now.</param>
    [LoggerMessage(
        EventId = 41,
        Level = LogLevel.Warning,
        Message = "Session directory moved: lock.json recorded '{Recorded}', which no longer exists, and the session was opened at '{Actual}'. The record has been repaired.")]
    public static partial void DirectoryMoved(ILogger logger, string recorded, string actual);

    /// <summary>A session directory was deleted.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="couldNotRemove">How many items survived the delete.</param>
    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Session destroyed at {Directory}; {CouldNotRemove} item(s) could not be removed.")]
    public static partial void Destroyed(ILogger logger, string directory, int couldNotRemove);

    /// <summary>A session's purpose was replaced.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="previous">What it was.</param>
    /// <param name="purpose">What it is now.</param>
    [LoggerMessage(EventId = 43, Level = LogLevel.Information, Message = "Purpose of {Directory} changed from '{Previous}' to '{Purpose}'.")]
    public static partial void PurposeChanged(ILogger logger, string directory, string previous, string purpose);

    /// <summary>The directory was locked and the child could not be started.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 44, Level = LogLevel.Error, Message = "Session at {Directory} was locked but its browser runtime could not be started; the lock has been released.")]
    public static partial void CouldNotOpen(ILogger logger, string directory, Exception failure);

    /// <summary>A browser-needing call was refused because the browser is not there yet.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="browser">The family.</param>
    /// <param name="state">Where provisioning stands.</param>
    [LoggerMessage(
        EventId = 45,
        Level = LogLevel.Information,
        Message = "'{Tool}' was refused because {Browser} provisioning is '{State}'. The session stays open and the same call succeeds once it lands.")]
    public static partial void CallRefusedWhileProvisioning(ILogger logger, string tool, string browser, ProvisioningState state);

    /// <summary>The provisioning state could not be read at all.</summary>
    /// <remarks>
    /// Logged rather than turned into a refusal: the browser may well be
    /// installed, and a probe that cannot answer must not stand between a caller
    /// and a working session. If it is genuinely missing, the launch says so with
    /// its own cause.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 46,
        Level = LogLevel.Warning,
        Message = "Whether {Browser} is provisioned could not be determined; the call was allowed through rather than refused on a guess.")]
    public static partial void ProvisioningUnreadable(ILogger logger, string browser, Exception failure);
}

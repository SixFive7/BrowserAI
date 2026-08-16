// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Logging;
using BrowserAI.Protocol;
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
/// explains itself. §H.4's catalogue wording is
/// [step 13](../../plan/build-order.md#13-the-one-table-enforcement-and-the-model-facing-surface)'s;
/// what is fixed here is that a cause and a route out reach the caller at all.
/// </para>
/// </remarks>
internal sealed class SessionManager : IAsyncDisposable
{
    /// <summary>
    /// How much room a volume must have before a session is created there.
    /// </summary>
    /// <remarks>
    /// First-run browser provisioning needs 203.8 MB down and 433 MiB extracted,
    /// so peak usage is ~640 MiB while both the archive and the tree exist. A
    /// refusal here that names the number is recoverable in one turn; a failure
    /// partway through the download is the <c>spawn EFTYPE</c> shape — success
    /// shaped, stderr empty, discovered at first navigation.
    /// </remarks>
    public const long RequiredFreeBytes = 640L * 1024 * 1024;

    /// <summary>The only browser family this build can create a session for.</summary>
    /// <remarks>Firefox is [step 17](../../plan/build-order.md#17-firefox).</remarks>
    public const string SupportedBrowser = "chromium";

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
            return new ToolOutcome(Describe(already, ["This session is already open in this BrowserAI; nothing was changed."]), IsError: false);
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
                    $"'{location.FullPath}' looks like a COPY of the session at '{record.Directory}', which still exists. "
                    + $"Its record says: mode {record.Mode}, purpose: {record.Purpose}. A copy inherits an ownership record naming a process that may still be alive against the original, so resuming it silently would replay another session's history as though it described this one. "
                    + $"Nothing was changed. Pass acknowledgeCopy=true to take this copy over anyway, or resume '{record.Directory}' instead.",
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
            var size = SizeOnDisk(session.FullPath);

            lines.Add(
                $"{session.FullPath}\n"
                + $"  mode: {record.Mode}   browser: {record.Browser}   size on disk: {Megabytes(size)}\n"
                + $"  created: {Stamp(record.Created)}   last used: {Stamp(record.LastUsed)}\n"
                + $"  purpose recorded by a previous session: {record.Purpose}");
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

        // Released before the delete: Windows will not remove a file this
        // process is holding open, and the lock has done its job the moment
        // ownership is proven.
        held.Dispose();

        var size = SizeOnDisk(location.FullPath);
        var failures = new List<string>();
        Remove(location.FullPath, failures);

        _index.Forget(location);
        SessionToolLog.Destroyed(_logger, location.FullPath, failures.Count);

        var summary =
            $"Destroyed the '{record.Mode}' session at '{location.FullPath}' ({Megabytes(size)}). Its purpose was: {record.Purpose}";

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

            var config = BrowserConfiguration.ForSession(location, mode, tracing, consoleLevel);
            var configFile = Path.Combine(
                _environment.InstanceDirectory,
                $"playwright-mcp-{location.Hash[..16]}.json");

            var options = ChildLaunch.Create(
                _environment.Payload,
                _environment.Paths.BrowsersDirectory,
                location.FullPath,
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
            child = await ChildConnection.ConnectAsync(
                new DirectStdioClientTransport(options, logging.Factory),
                logging.Factory,
                $"browserai-{location.Hash[..8]}-",
                _relay,
                cancellationToken).ConfigureAwait(false);

            session = new LiveSession(location, held, mode, child, logging, config, configFile, createdHere);
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

            return new ToolOutcome(Describe(session, notes), IsError: false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            SessionToolLog.CouldNotOpen(_logger, location.FullPath, failure);

            return new ToolOutcome(
                $"'{location.FullPath}' was locked but its browser runtime could not be started: {failure.GetType().Name}: {failure.Message} "
                + $"The directory is left as it is and nothing is running. Fix the cause and call {SessionToolSurface.Resume} on the same directory.",
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

    private string Describe(LiveSession session, IReadOnlyList<string> notes)
    {
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
            .Append("  purpose: ").Append(record.Purpose).Append('\n')
            .Append("  created: ").Append(Stamp(record.Created)).Append("   last used: ").Append(Stamp(record.LastUsed)).Append('\n')
            .Append("  child protocol: ").Append(session.Child.NegotiatedProtocolVersion ?? "<none>").Append('\n')
            .Append("  browsers: ").Append(Provisioning()).Append('\n');

        if (record.PurposeHistory.Count > 1)
        {
            _ = text.Append("  purpose recorded by previous sessions, oldest first: ")
                .Append(string.Join(" / ", record.PurposeHistory.Take(record.PurposeHistory.Count - 1)))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// What the browsers root holds, reported rather than acted on.
    /// </summary>
    /// <remarks>
    /// Provisioning itself — the non-blocking install, the timers, the error text
    /// and the reinstall tool — is
    /// [step 15](../../plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser).
    /// This is one directory probe so that <c>init</c>'s result says whether a
    /// navigation is going to work, which is the fact a caller can act on.
    /// </remarks>
    private string Provisioning()
    {
        var root = _environment.Paths.BrowsersDirectory;

        try
        {
            var installed = Directory.Exists(root)
                && Directory.EnumerateDirectories(root, "chromium-*").Any();

            return installed
                ? $"{root} (a Chromium build is present)"
                : $"{root} (EMPTY — no Chromium build is installed there, so the first navigation will fail until one is)";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"{root} (could not be read: {failure.Message})";
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
            : $"'{location.FullPath}' is already a BrowserAI session and {SessionToolSurface.Init} will not take it over. "
                + $"It is a '{record.Mode}' session on {record.Browser}, created {Stamp(record.Created)}, last used {Stamp(record.LastUsed)}, purpose: {record.Purpose} "
                + $"Call {SessionToolSurface.Resume} with directory='{location.FullPath}' to drive it, {SessionToolSurface.Destroy} to delete it, or {SessionToolSurface.Init} on a directory that is not one. "
                + "There is deliberately no difference between a session that was lost and one that was closed cleanly: both are resumed.";
    }

    private static string? FreeSpaceRefusal(SessionPath location)
    {
        long free;

        try
        {
            // O(1), and only ever O(1). A directory walk here would make the
            // check slower than the failure it prevents, and init is on the hot
            // path of every session.
            free = new DriveInfo(Path.GetPathRoot(location.FullPath) ?? location.FullPath).AvailableFreeSpace;
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A volume whose free space cannot be queried in one call -- a
            // network share, most often. Skipped rather than replaced by a walk:
            // the check exists to be cheap, and an expensive substitute would
            // cost every session start for a case that fails loudly later.
            return null;
        }

        return free >= RequiredFreeBytes
            ? null
            : $"'{location.FullPath}' is on a volume with {Megabytes(free)} free. A session needs about {Megabytes(RequiredFreeBytes)} while a browser is provisioned, "
                + "and a download that runs out of space partway through fails at first navigation rather than here. Nothing was changed. Free some space, or name a directory on another volume.";
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

    private static long SizeOnDisk(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(file => file.Length);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void Remove(string directory, List<string> failures)
    {
        // Deleted item by item rather than with Directory.Delete(recursive), so
        // one file a browser or a virus scanner still holds open costs that file
        // rather than the whole call. What could not go is reported; a destroy
        // that silently left half a tree would be the founding failure shape.
        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(directory)))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                failures.Add($"  {file}: {failure.Message}");
            }
        }

        foreach (var child in SafeEnumerate(() => Directory.EnumerateDirectories(directory)))
        {
            Remove(child, failures);
        }

        try
        {
            Directory.Delete(directory);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            failures.Add($"  {directory}\\: {failure.Message}");
        }
    }

    private static IReadOnlyList<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return [.. enumerate()];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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
            throw new SessionToolException(
                $"'{argument}' must be an absolute path, and '{directory}' is not. BrowserAI has no default session directory and does not resolve a relative one, because that would silently pick a location nobody chose. Pass a full path such as C:\\work\\checkout-flow-bug.");
        }

        try
        {
            return SessionPath.Resolve(directory);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SessionToolException(
                $"'{argument}' = '{directory}' is not a usable directory path: {failure.Message} Nothing was changed.");
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
            throw new SessionToolException(
                $"'{name}' is not an argument of {SessionToolSurface.Resume}, because {why}. Remove it. If this directory is not the session you meant, name another one.");
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
}

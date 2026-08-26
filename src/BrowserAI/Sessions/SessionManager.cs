// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Storage;
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
    /// <para>
    /// <b>One number for both families, sized on the larger.</b> Chromium's
    /// first-run provisioning needs 203.8 MB down and 430.48 MiB extracted —
    /// both re-measured 2026-08-16 — so peak usage is ~640 MiB while both the
    /// archive and the tree exist. Firefox needs 127.2 MB down and 340.15 MiB
    /// extracted (measured 2026-08-19), which is smaller in both halves, so this
    /// bound holds for it without being restated per family. A
    /// refusal here that names the number is recoverable in one turn; a failure
    /// partway through the download is the <c>spawn EFTYPE</c> shape — success
    /// shaped, stderr empty, discovered at first navigation.
    /// </para>
    /// <para>
    /// <b>It is deliberately not the sum of both families.</b> A session names
    /// one browser and provisions one tree; a machine that ends up with two has
    /// paid for them one at a time, and asking for 1.1 GiB free before the first
    /// session on a volume would refuse work that fits.
    /// </para>
    /// </remarks>
    public const long RequiredFreeBytes = 640L * 1024 * 1024;

    /// <summary>The family <c>browserai_init</c> uses when the caller names none.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously <c>SupportedBrowser</c>, "the only
    /// browser family this build can create a session for").</b> Both families
    /// in <see cref="ProvisionedBrowsers.Families"/> are now offered on
    /// <c>init</c>; what survives is a <i>default</i>, which is a different
    /// claim. The two things that were owed before Firefox could be offered are
    /// done: <see cref="BrowserProvisioner.FirstRunDownloadSizes"/> carries a
    /// measured figure per family, and
    /// <c>browserai_reinstall_browser</c> now names the family it reinstalls.
    /// </para>
    /// <para>
    /// <b>A default at all, where <c>browserai_reinstall_browser</c>'s
    /// <c>browser</c> has none, and the asymmetry is the point.</b> A browser
    /// chosen by omission at <c>init</c> is a rendering engine, and every session
    /// in this product's history has been Chromium; a browser chosen by omission
    /// at a <i>reinstall</i> deletes a working tree and reports success.
    /// <i>Corrected 2026-08-20 (previously the comparison was with
    /// <c>mode</c>, which "has none" because "a mode chosen by omission is a
    /// security posture nobody decided on").</i> Session modes are gone, so that
    /// precedent no longer exists to point at.
    /// </para>
    /// <para>
    /// <b>The rest of the product was already family-parameterised.</b>
    /// Provisioning, the config generator, the launch preflight and the stray
    /// sweep all take the family from the session's own <c>browserai.data</c>, so a
    /// record that names Firefox is honoured on <c>resume</c> rather than
    /// silently run as Chromium against a Firefox profile.
    /// </para>
    /// </remarks>
    public const string DefaultBrowser = ProvisionedBrowsers.Chromium;

    /// <summary>
    /// The sentence <c>browserai_destroy</c> says when it removed the session's
    /// record but could not remove everything under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A constant because the promise it carries is the one a test has to
    /// hold destroy to, and prose copied into an assertion drifts silently in
    /// the direction that passes.</b> <c>browserai_destroy</c> does not promise
    /// the tree is gone — Windows will not unlink a file a browser is still
    /// mapping, and the release lags the process by however long the kernel
    /// takes — it promises that <b>what survived is named</b>. A test that
    /// re-typed this heading and then watched it be reworded would stop
    /// recognising the survivor arm, and would go green by never reaching the
    /// assertions that matter.
    /// </para>
    /// <para>
    /// ⚠️ <b>Extracted 2026-08-19, after <c>FirefoxSessionTests</c> asserted the
    /// stronger property instead.</b> That test required the directory to be
    /// gone, which is a guarantee this tool has never made: it passed nine local
    /// runs and failed three consecutive CI runs on a four-core runner, where
    /// Firefox — the family slowest to release its profile — was still holding
    /// mapped files when the answer was composed. The assertion was not merely
    /// strict, it was wrong, and the honest version of it needs this string.
    /// </para>
    /// </remarks>
    public const string SurvivorsHeading = "item(s) could not be removed, because something still has them open:";

    /// <summary>
    /// How many survivors the answer lists by name before it stops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cap on the <i>listing</i> and never on the <i>count</i>: the number in
    /// <see cref="SurvivorsHeading"/>'s sentence is always the whole tally, so a
    /// caller can tell a truncated list from a complete one by comparing the two.
    /// </para>
    /// <para>
    /// ⚠️ <b>And since 2026-08-19 the answer says which of the two it is, rather
    /// than leaving the comparison to be noticed.</b> Previously the tally and
    /// the listing were both there and <b>nothing anywhere said the list had been
    /// cut</b>: at 25 survivors a reader saw the number 25 and twenty lines, and
    /// the only evidence of the other five was arithmetic nobody was asked to do.
    /// A model reading twenty lines under a heading has been given a complete
    /// list unless the text says otherwise, and this one is written for a model.
    /// See <see cref="Listing"/>.
    /// </para>
    /// </remarks>
    public const int SurvivorsNamed = 20;

    /// <summary>
    /// One line per item up to <see cref="SurvivorsNamed"/>, and a sentence
    /// saying so when the cap cut the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One routine for all three capped listings in this file</b> — the
    /// survivors of a destroy, the survivors of a reinstall's delete, and the
    /// sessions a reinstall refusal names. All three used to take
    /// <c>Take(20)</c> inline against a tally printed above them, and all three
    /// were silent about the cut; a fix applied to one of them would have left
    /// the same defect standing twice.
    /// </para>
    /// <para>
    /// <b>The note is deliberately not indented.</b> Every line
    /// <c>TreeDelete</c> names is indented by two spaces, and that indent is what
    /// a reader — and <c>DestroyAnswer</c> in the suite — uses to find where the
    /// listing ends. A note that lined up with the items would be read as one.
    /// </para>
    /// </remarks>
    /// <param name="items">Everything there is to name, in the order it should be named.</param>
    /// <returns>The listing, with a truncation note when there is one to make.</returns>
    internal static string Listing(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var listing = string.Join("\n", items.Take(SurvivorsNamed));

        return items.Count <= SurvivorsNamed ? listing : $"{listing}\n{TruncationNote(items.Count)}";
    }

    /// <summary>
    /// The sentence a cut listing ends with, written once so that a test can
    /// require it rather than re-type it.
    /// </summary>
    /// <remarks>
    /// <b>It restates the tally rather than only the remainder.</b> The number
    /// above the listing and the number of lines under it are what a reader is
    /// being asked to reconcile, so the sentence that explains the gap names both
    /// sides of it and does the subtraction itself.
    /// </remarks>
    /// <param name="total">The whole tally, which is always larger than the cap when this is used.</param>
    /// <returns>The note.</returns>
    internal static string TruncationNote(int total) =>
        $"Only the first {SurvivorsNamed.ToString(CultureInfo.InvariantCulture)} are listed; the "
        + $"{total.ToString(CultureInfo.InvariantCulture)} above is the whole tally, so "
        + $"{(total - SurvivorsNamed).ToString(CultureInfo.InvariantCulture)} more are not named here.";

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
    /// <remarks>
    /// ⚠️ <b>THE HOT PATH, and it reads rather than resolves.</b> Every forwarded
    /// browser call carries a <c>session</c> argument and arrives here. The value
    /// came out of BrowserAI's own answer, so it is canonical already; asking the
    /// volume question about it would add the object-manager call and a directory
    /// open to a <c>browser_snapshot</c> measured at <b>4.2 ms</b>
    /// ([kb](../../../kb/playwright/provisioning-and-timings.md)), for a question
    /// whose answer cannot change what the dictionary lookup below returns. A
    /// spelling that is not canonical misses the dictionary either way, because
    /// the key was stored canonical — and when it does,
    /// <see cref="ExplainUnknownSession"/> runs the whole sequence and is what
    /// names the spelling to use.
    /// </remarks>
    /// <param name="directory">The <c>session</c> argument, as it arrived.</param>
    /// <returns>The live session, or <see langword="null"/>.</returns>
    public LiveSession? Find(string? directory)
    {
        if (CanonicalPath.Of(directory, PathOrigin.Read, SessionToolSurface.SessionParameter).Canonical is not { } canonical)
        {
            return null;
        }

        SessionPath location;

        try
        {
            location = SessionPath.For(canonical);
        }
        catch (ArgumentException)
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
    /// a path with no <c>browserai.data</c> is
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
        catch (SessionRecordException failure)
        {
            // A record that cannot be read is still a session, and saying so is
            // more useful than reporting it as absent: the recovery is to fix or
            // destroy the directory, never to init over it. The old format
            // arrives here too, with its own sentence.
            return $"'{location.FullPath}' holds a record this build cannot read, so '{tool}' was not run and nothing was changed. {failure.Message}";
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
            ? SessionErrors.ProvisioningInProgress(tool, browser, status.Directory, BrowserProvisioner.DownloadSizeFor(browser), status.Progress)
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
                SessionToolSurface.CatchUp => CatchUp(arguments),
                SessionToolSurface.List => List(arguments),
                SessionToolSurface.Destroy => await DestroyAsync(arguments).ConfigureAwait(false),
                SessionToolSurface.SetPurpose => SetPurpose(arguments),
                SessionToolSurface.ReinstallBrowser => await ReinstallBrowserAsync(arguments, cancellationToken).ConfigureAwait(false),
                _ => new ToolOutcome($"'{tool}' is not a BrowserAI session tool. The session tools are: {string.Join(", ", SessionToolSurface.Names)}.", IsError: true),
            };
        }
        catch (SessionToolException failure)
        {
            return new ToolOutcome(failure.Message, IsError: true);
        }
        catch (SessionRecordException failure)
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
        // ⚠️ FIRST, AND HELD FROM HERE TO THE END OF THE SESSION. This is the
        // reader half of the machine-wide reader/writer claim on the browsers
        // root -- see MaintenanceLock. Nothing below may run while a reinstall
        // holds it exclusively, and once this handle exists no reinstall can
        // take it away.
        var claim = MaintenanceLock.TakeShared(_environment.Paths.BrowsersDirectory, out var denial, out var denialDetail);

        // Ownership moves to the session when OpenAsync is reached -- the local
        // is nulled at that moment, which is what makes the finally below cover
        // every path that does NOT get there, including the several that throw
        // SessionToolException out of argument parsing.
        //
        // ⚠️ It is a NULLABLE local rather than a bool flag because CA2000 reads
        // this exact shape and nothing else: declare before the try, transfer by
        // assigning null, dispose unconditionally on the null-conditional in the
        // finally.

        try
        {
            if (claim is null)
            {
                return TheRootCouldNotBeClaimed(SessionToolSurface.Init, denial, denialDetail);
            }

            var named = Required(arguments, "directory");
            var location = Resolve(named, "directory", out var verdict);
            var purpose = Required(arguments, "purpose");
            var browser = Browser(arguments, "browser", DefaultBrowser, ProvisionedBrowsers.Families);
            var headed = Flag(arguments, "headed") ?? false;
            var tracing = Flag(arguments, "tracing") ?? false;
            var run = Run(arguments);
            var debug = Flag(arguments, "debug") ?? false;

            // Being made to say "resume" is the point: it converts an accidental
            // collision into a stated intent. There is deliberately no difference
            // between a lost session, a neatly closed one and one this very process
            // has open -- all three must be resumed, and all three get the same
            // refusal naming the purpose, the browser and the date, because the reason
            // a session ended stops being a thing anyone has to model. An earlier
            // version special-cased "already open in this process" and answered
            // first with a shorter message, which hid the informative one behind an
            // accident of who happened to hold the directory.
            //
            // ⚠️ AND IT IS NOT THE ONLY ASK, since 2026-08-19. This one is UNGATED --
            // it reads the record with no lock held -- so it can land in the instant
            // in which the guard's name is unbound while a peer is acquiring the
            // directory, read null as "free, proceed", and reach a reclaim that rebinds the
            // session's browser family. `RefuseAnExistingRecord` below asks the same
            // question under the per-directory gate, where the record has already
            // been read and a peer replacing one is holding the gate. This look
            // stays because it is what produces the ONE answer described above:
            // moving it inside would let the pre-gate probe answer first for a live
            // session, with a shorter sentence about who holds the file, which is
            // the regression the paragraph above records having already been made
            // once.
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

            // The claim's ownership moves to the session here and nowhere
            // else. Nulling the local is what makes the finally cover every
            // path that did not get this far.
            var held = claim;
            claim = null;

            return await OpenAsync(
                location,
                new SessionLockRequest
                {
                    Browser = browser,
                    Purpose = purpose,

                    // ⚠️ THE PURPOSE IS THE FIRST ROW OF THE LOG, and `init`
                    // has no `why` to put there instead. Two mandatory free-text
                    // fields on one call gets one thoughtful answer and one
                    // restatement, so the purpose -- which IS the reason the
                    // session exists -- is what row one says.
                    Entry = new SessionCall(SessionToolSurface.Init, purpose),

                    // `init` means MAKE a session here. A directory that already
                    // carries a record has to be resumed instead, and this is the
                    // half of that refusal the ungated look above cannot guarantee.
                    RefuseAnExistingRecord = true,
                },
                headed,
                tracing,
                run,
                debug,
                createdHere: true,
                SpellingNote(named, location, verdict) is { } note ? [note] : [],
                held,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            claim?.Dispose();
        }
    }

    private async Task<ToolOutcome> ResumeAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        // ⚠️ FIRST, exactly as `init` does and for the same reason: a resume
        // opens a browser into an existing profile under the same tree, so it is
        // exactly as unsafe while a reinstall is replacing it.
        var claim = MaintenanceLock.TakeShared(_environment.Paths.BrowsersDirectory, out var denial, out var denialDetail);

        // Ownership moves at the same point `init` moves it, by the same
        // mechanism. See there.

        try
        {
            if (claim is null)
            {
                return TheRootCouldNotBeClaimed(SessionToolSurface.Resume, denial, denialDetail);
            }

            var named = Required(arguments, "directory");
            var location = Resolve(named, "directory", out var verdict);
            var why = Why(arguments, SessionToolSurface.Resume);
            var appended = Optional(arguments, "purpose");
            var debug = Flag(arguments, "debug") ?? false;
            var headed = Flag(arguments, "headed") ?? false;
            var tracing = Flag(arguments, "tracing");
            var run = Run(arguments);

            // A profile is browser-specific and a session cannot change what it is,
            // so a caller asking to resume a Firefox directory as Chromium is
            // stating something impossible. Answering "sure" would be the wrong kind
            // of helpful.
            //
            // MODE WAS REFUSED HERE TOO UNTIL 2026-08-20, for the same reason.
            // It is neither refused nor accepted now: there is no such argument
            // and no such property, so a caller that sends one is answered by the
            // schema rather than by a sentence about a thing this build has.
            Refuse(arguments, "browser", "the browser is bound at init and the profile on disk belongs to it");

            if (_live.TryGetValue(location.Key, out var already))
            {
                SessionToolLog.Why(already.Logger, SessionToolSurface.Resume, why);

                // ⚠️ THE ROW IS WRITTEN `in-flight` AND SETTLED, exactly as a
                // forwarded call's is, because one outcome vocabulary across
                // every row is what lets `browserai_catch_up` say "no answer was
                // recorded" without having to know which tool wrote which row.
                var row = already.Lock.Append(SessionToolSurface.Resume, why);

                if (appended is not null)
                {
                    already.Lock.AppendPurpose(RecordText.Sanitise(appended));
                }

                already.Lock.Settle(row, SessionStore.Successful, failure: null);

                return new ToolOutcome(
                    Describe(already, ["This session is already open in this BrowserAI; nothing was changed."], RefreshRollUp(location)),
                    IsError: false);
            }

            var record = SessionLock.ReadRecord(location)
                ?? throw new SessionToolException(
                    $"'{location.FullPath}' has no '{SessionLayout.LockFileName}', so it is not a BrowserAI session and there is nothing to resume. "
                    + $"Call {SessionToolSurface.Init} to create one there, or name the directory of a session that exists — {SessionToolSurface.List} will show what is under a path.");

            var notes = new List<string>();
            string? movedFrom = null;

            if (SpellingNote(named, location, verdict) is { } spelling)
            {
                notes.Add(spelling);
            }

            // The directory is the identity; the path in browserai.data is provenance. A
            // move leaves nothing behind and a copy leaves the original standing, so
            // the recorded path already discriminates and no fingerprint field is
            // needed.
            //
            // ⚠️ A COPY IS NO LONGER REFUSED, and `acknowledgeCopy` is gone with the
            // refusal (2026-08-18). The flag existed because the record was a
            // snapshot: taking the copy over rewrote the only evidence that it WAS a
            // copy, so the caller had to be made to say it knew. Under schema 2 the
            // record is a list of timestamped statements and nothing is overwritten
            // -- resuming appends `location` to a `directory` history that still
            // carries the original -- so the answer below hands the model the
            // provenance instead of demanding a confirmation for it. A confirmation
            // flag whose whole content can be returned as fact is a question that did
            // not need asking. BrowserAI now has none.
            if (!SamePath(record.Directory, location))
            {
                notes.Add(Directory.Exists(record.Directory)
                    ? $"This directory is a COPY of the session at '{record.Directory}', which still exists — the two are now separate sessions, and the process named in the copied record may still be alive against the original. Its recorded purpose and history describe the ORIGINAL, not this copy: read them below before acting on them, and call {SessionToolSurface.SetPurpose} to say what this copy is for."
                    : $"This directory was moved or renamed: its record said '{record.Directory}', which no longer exists. The record has been repaired to '{location.FullPath}'.");

                // Recorded rather than logged here. The interesting record is the
                // SESSION's own browserai.data, which does not exist until OpenAsync
                // has opened it -- so the note is carried there, where whoever is
                // looking into this directory will find it.
                movedFrom = Directory.Exists(record.Directory) ? null : record.Directory;
            }

            // ⚠️ THE CONCATENATION DIED HERE (2026-08-26, previously
            // `$"{record.Purpose} | {appended}"`). Every field of the record is
            // an ordered list of statements, so a resume that says what the
            // session is now for adds a ROW -- and `Compose`'s dedup means a
            // resume that says nothing adds none. The old shape built value N
            // out of the whole of value N-1, which grew quadratically (57.6 KiB
            // at 50 resumes, 860 KiB at 200) and, at the 2,000-character cap,
            // silently truncated the sentence the caller had just written.
            var purpose = appended is null ? record.Purpose : RecordText.Sanitise(appended);

            // Ownership moves here, by the mechanism `init` documents.
            var held = claim;
            claim = null;

            return await OpenAsync(
                location,
                new SessionLockRequest
                {
                    Browser = record.Browser,
                    Purpose = purpose,
                    Entry = new SessionCall(SessionToolSurface.Resume, why),
                },
                headed,
                tracing ?? false,
                run,
                debug,
                createdHere: false,
                notes,
                held,
                cancellationToken,
                movedFrom,
                why).ConfigureAwait(false);
        }
        finally
        {
            claim?.Dispose();
        }
    }

    /// <summary>
    /// The seventh authored tool: what a session was doing, and what is in its
    /// directory now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two sources that routinely disagree, and the answer keeps them
    /// apart.</b> The log says what BrowserAI <i>did</i> — it is written by this
    /// product, one entry per forwarded call, and it knows nothing about what a
    /// page put on disk. The inventory says what is <i>true now</i>. The
    /// load-bearing example is credentials: cookies arrive from navigation
    /// rather than from tools, so a log-only answer would say <i>"no credential
    /// tools were used"</i> about a directory holding a live signed-in profile.
    /// </para>
    /// <para>
    /// <b>Read-only, and it takes no lock it can be refused by.</b> A session
    /// another BrowserAI is driving is exactly the case this exists for, so
    /// anything that could be refused by a live holder would refuse when it was
    /// needed. The record is read the way <see cref="List"/> reads one, and the
    /// directory walk opens no file inside it.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-26 (previously "It now takes one, briefly:
    /// `InUse` goes through `SessionLock.ProbeLivenessUnderTheGate`, which
    /// acquires this directory's own gate at a zero timeout").*** It takes
    /// nothing again, and the sentence above is true without a caveat. The gate
    /// was needed because the record was rewritten on every forwarded call, so a
    /// bare probe could catch a busy session mid-rewrite and read it as free.
    /// The guard is written once now and never rewritten, so
    /// <see cref="SessionLock.ProbeLiveness"/> — one <c>CreateFile</c> — is
    /// sound on its own.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is NOT quite side-effect-free, and that is the one caveat left.</b>
    /// A read against a crashed holder's uncheckpointed write-ahead log recovers
    /// that log, and building the index leaves a <c>-shm</c> beside the store in
    /// a directory this call only asked to look at. Where it may not create that
    /// file the read is refused instead, which is the right way round: answering
    /// with a session's history as of its last checkpoint would be a confident
    /// wrong answer.
    /// </para>
    /// <para>
    /// <b>The log is printed newest-last and truncated from the FRONT.</b> A
    /// caller arriving at a session wants the recent story; an elision is stated
    /// rather than presented as continuity, and the record's own cap says
    /// <i>may</i> because it cannot tell whether a trim has happened.
    /// </para>
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>What to tell the caller.</returns>
    private ToolOutcome CatchUp(JsonObject? arguments)
    {
        var location = Resolve(Required(arguments, SessionToolSurface.SessionParameter), SessionToolSurface.SessionParameter);
        var asked = Number(arguments, SessionToolSurface.PageParameter);

        var record = SessionLock.ReadRecord(location)
            ?? throw new SessionToolException(
                $"'{location.FullPath}' has no '{SessionLayout.DataFileName}', so it is not a BrowserAI session and there is nothing to catch up on. "
                + $"Call {SessionToolSurface.List} with a directory to see the sessions beneath it, or {SessionToolSurface.Init} to create one here.");

        var total = record.LogLength;
        var pages = total is 0 ? 1 : (int)((total + PageSize - 1) / PageSize);
        var page = (int)(asked ?? 1);

        if (page < 1 || page > pages)
        {
            throw new SessionToolException(
                $"'{SessionToolSurface.PageParameter}' = {page.ToString(CultureInfo.InvariantCulture)} is outside this session's log, which is "
                + $"{total.ToString(CultureInfo.InvariantCulture)} entr(ies) over {pages.ToString(CultureInfo.InvariantCulture)} page(s) of {PageSize.ToString(CultureInfo.InvariantCulture)}. "
                + $"Pages are numbered from the OLDEST end, so page 1 is where the session started and page {pages.ToString(CultureInfo.InvariantCulture)} is what happened most recently. Nothing was changed.");
        }

        var skip = (long)(page - 1) * PageSize;

        IReadOnlyList<SessionLogRow> rows;

        try
        {
            using var store = SessionStore.OpenForReading(location.DataFile);

            rows = SessionRecordReader.Log(store, skip, PageSize);
        }
        catch (SqliteException refused)
        {
            throw new SessionToolException(
                $"'{location.DataFile}' could not be read for page {page.ToString(CultureInfo.InvariantCulture)}: {refused.Message}");
        }

        var now = DateTimeOffset.Now;
        var text = new StringBuilder();

        // ⚠️ THE VOLATILE HALF IS ON PAGE 1 AND NOWHERE ELSE, and that is not
        // tidiness. The inventory is a fresh directory walk and the in-use line
        // is a fresh probe, so repeating them would let two pages of one answer
        // disagree about one session in one minute -- about information that has
        // nothing to do with the page being fetched. The log half is stable by
        // construction: rows are only ever appended, and numbering from the
        // OLDEST end means an append can change the last page and no other.
        if (page is 1)
        {
            _ = text
                .Append("Session: ").Append(location.FullPath).Append('\n')
                .Append("  browser: ").Append(record.Browser).Append('\n')
                .Append("  created: ").Append(Stamp(record.Created)).Append("   (").Append(Age(now - record.Created)).Append(" ago)\n")
                .Append("  last touched: ").Append(Stamp(record.LastUsed)).Append("   (").Append(Age(now - record.LastUsed)).Append(" ago)\n")
                .Append("  ").Append(InUse(location)).Append('\n')
                .Append("  ").Append(SessionErrors.Recorded(record.Purpose)).Append('\n');
        }

        _ = text.Append('\n')
            .Append("WHAT WAS DONE HERE — the session's own log, oldest first. This is what BrowserAI did; it says nothing about what a page wrote to disk.\n")
            .Append("  page ").Append(page.ToString(CultureInfo.InvariantCulture))
            .Append(" of ").Append(pages.ToString(CultureInfo.InvariantCulture))
            .Append(", entries ").Append((total is 0 ? 0 : skip + 1).ToString(CultureInfo.InvariantCulture))
            .Append('–').Append((skip + rows.Count).ToString(CultureInfo.InvariantCulture))
            .Append(" of ").Append(total.ToString(CultureInfo.InvariantCulture)).Append('\n');

        if (rows.Count is 0)
        {
            _ = text.Append("  (nothing: this session's record carries no entries at all, which means no browser call was ever forwarded through it)\n");
        }
        else
        {
            var number = skip;

            foreach (var row in rows)
            {
                number++;
                Render(text, number, row);
            }
        }

        if (page < pages)
        {
            _ = text.Append("  → ").Append(pages - page).Append(" more page(s). The next is ")
                .Append(SessionToolSurface.CatchUp).Append("(session='").Append(location.FullPath)
                .Append("', ").Append(SessionToolSurface.PageParameter).Append('=').Append(page + 1)
                .Append("). Page numbers count from the OLDEST entry, so a page you have already read never changes when the session goes on working.\n");
        }
        else if (pages > 1)
        {
            _ = text.Append("  → this is the last page; it is the only one that can change while the session is live.\n");
        }

        if (page is 1)
        {
            AppendWhatIsHereNow(text, location, now);
            _ = text.Append(HowItGotHere(record));
        }

        return new ToolOutcome(text.ToString(), IsError: false);
    }

    /// <summary>
    /// How many log entries one <c>browserai_catch_up</c> page carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A PAGE SIZE, NOT A TRUNCATION — and that is the whole of the
    /// change (2026-08-26, previously <c>LoggedEntriesShown = 40</c>).</b> The
    /// old constant printed the newest forty entries of a record that held up to
    /// 250 and elided the rest with a sentence naming the file, so the tool that
    /// exists to answer <i>what was I doing here</i> showed 13–40 % of what the
    /// record held and the remainder was reachable only by opening a JSON file
    /// by hand. Nothing is elided now; everything is reachable, a page at a
    /// time.
    /// </para>
    /// <para>
    /// <b>Numbered from the OLDEST end, which is what makes a page stable.</b>
    /// The log is append-only and nothing evicts, so entry <i>i</i> names the
    /// same entry forever and an append can only ever change the last page.
    /// Numbering from the newest end — the shape the old truncation used —
    /// shifts every boundary on every call, so page 2 of a live session would be
    /// a different set of entries each time it was fetched.
    /// </para>
    /// </remarks>
    private const int PageSize = 100;

    /// <summary>Renders one log row into the answer.</summary>
    /// <remarks>
    /// <para>
    /// <b>A stale <c>in-flight</c> row says <i>no answer was recorded</i>, which
    /// is the true statement.</b> The row was written before the call was
    /// forwarded; if nothing settled it, the call hung, the child died or the
    /// process was killed — and a reader that saw a bare tool name with no
    /// outcome would have to guess which. A <c>false</c> there would be a lie
    /// and a <c>true</c> would be worse.
    /// </para>
    /// <para>
    /// <b>Only a failure carries a payload, and it is printed.</b> A successful
    /// call's answer went back to the caller byte-identical and is not stored;
    /// a failure's is, because it is the one thing a later reader cannot
    /// reconstruct.
    /// </para>
    /// </remarks>
    /// <param name="text">Where it goes.</param>
    /// <param name="number">Its position in the whole log, counting from the oldest.</param>
    /// <param name="row">The row.</param>
    private static void Render(StringBuilder text, long number, SessionLogRow row)
    {
        _ = text.Append("  ").Append(number.ToString(CultureInfo.InvariantCulture)).Append(". ")
            .Append(Stamp(row.At)).Append("  ").Append(row.Tool);

        _ = row.Outcome switch
        {
            SessionStore.Successful => text.Append("   ✓").Append(Took(row)),
            SessionStore.Failed => text.Append("   ✗ FAILED").Append(Took(row)),
            _ => text.Append("   — no answer was recorded: the row was written before the call was forwarded and nothing settled it, so the call hung, the child died, or the process ended first"),
        };

        _ = text.Append('\n');

        Indented(text, "      why: ", row.Why);

        if (row.Failure is { Length: > 0 } failure)
        {
            Indented(text, "      it failed with: ", failure);
        }
    }

    /// <summary>How long a settled call took, as a clause, or nothing.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The clause.</returns>
    private static string Took(SessionLogRow row) =>
        row.SettledAt is { } settled
            ? $" after {Math.Max(0, (settled - row.At).TotalSeconds).ToString("F2", CultureInfo.InvariantCulture)}s"
            : string.Empty;

    /// <summary>
    /// Writes free text under a label, with every line after the first indented
    /// to match.
    /// </summary>
    /// <remarks>
    /// <b>A <c>why</c> may be multi-line since the caps were removed</b>, and an
    /// unindented second line reads as a new entry to the model this answer is
    /// written for. The sanitiser has already dropped every control character
    /// except <c>\n</c>, so this is the only place line breaks have to be
    /// handled at all.
    /// </remarks>
    /// <param name="text">Where it goes.</param>
    /// <param name="label">The label, which sets the indent.</param>
    /// <param name="value">The text.</param>
    private static void Indented(StringBuilder text, string label, string value)
    {
        var indent = new string(' ', label.Length);
        var first = true;

        foreach (var line in value.Split('\n'))
        {
            _ = text.Append(first ? label : indent).Append(line).Append('\n');
            first = false;
        }
    }

    /// <summary>The directory as it is right now, walked at the moment of asking.</summary>
    /// <param name="text">Where it goes.</param>
    /// <param name="location">The session directory.</param>
    /// <param name="now">The instant the ages are against.</param>
    private static void AppendWhatIsHereNow(StringBuilder text, SessionPath location, DateTimeOffset now)
    {
        var contents = SessionInventory.Of(location);

        _ = text.Append('\n').Append("WHAT IS HERE NOW — the directory, walked just now. This is what is true; it does not know why any of it exists.\n");

        if (contents.Failure is { } failure)
        {
            _ = text.Append("  ⚠️ the directory could not be read (").Append(failure)
                .Append("), so this half of the answer is UNKNOWN rather than empty. Do not read it as 'nothing here'.\n");

            return;
        }

        _ = text
            .Append("  total: ").Append(Sizes.Describe(contents.Bytes)).Append(" in ")
            .Append(contents.Files.ToString(CultureInfo.InvariantCulture)).Append(" file(s)\n")
            .Append("  ").Append(OutputSize(location)).Append('\n');

        if (contents.LastWritten is { } written)
        {
            _ = text.Append("  last file written: ").Append(Stamp(written.ToLocalTime()))
                .Append("   (").Append(Age(now - written)).Append(" ago — a browser writes into the profile continuously while a page is open, so this moves when the record does not)\n");
        }

        foreach (var kind in contents.Kinds)
        {
            _ = text.Append("  ").Append(kind).Append('\n');
        }

        _ = text.Append(contents.CookieStore is { } store
            ? $"  ⚠️ CREDENTIALS: the profile holds a cookie store at '{store}'. This session may be signed in to something, whether or not any cookie tool appears above — cookies arrive from navigation. {SessionToolSurface.Destroy} is what removes it.\n"
            : "  no cookie store in the profile, so nothing has signed in through this session yet.\n");

        foreach (var archive in contents.Archives)
        {
            _ = text.Append("  ⚠️ PLAINTEXT CREDENTIALS: '").Append(archive.RelativePath).Append("' is an HTTP Archive (")
                .Append(Sizes.Describe(archive.Bytes))
                .Append("). A HAR records every request and response including headers, so every bearer token and session cookie that crossed the wire is in it in clear text. Treat the file as a secret and delete it when you are done.\n");
        }
    }

    /// <summary>
    /// What the session has written to <c>output\</c>, and who is responsible
    /// for removing it.
    /// </summary>
    /// <remarks>
    /// <b>Reported and never acted on — the maintainer's decision, 2026-08-25.</b>
    /// Nothing in BrowserAI deletes an artifact, ever: not on a schedule, not at
    /// a size, not when a session closes. What a caller gets instead is the
    /// number, on both of the tools that describe a session, so that retention
    /// is a decision somebody takes rather than one the server takes quietly on
    /// their behalf. <c>browserai_destroy</c> is the whole of the deletion
    /// story.
    /// </remarks>
    /// <param name="location">The session directory.</param>
    /// <returns>The line.</returns>
    private static string OutputSize(SessionPath location)
    {
        var output = Path.Combine(location.FullPath, SessionLayout.OutputFolderName);
        var (bytes, files) = SessionLayout.SizeAndFiles(output);

        return $"output: {Sizes.Describe(bytes)} in {files.ToString(CultureInfo.InvariantCulture)} file(s) — "
            + $"BrowserAI never deletes any of it, on any schedule or at any size; {SessionToolSurface.Destroy} is what removes a session and everything under it.";
    }

    /// <summary>A duration in the largest unit that does not round to zero.</summary>
    /// <param name="span">How long.</param>
    /// <returns>The figure, for a person or a model to act on.</returns>
    private static string Age(TimeSpan span) =>
        span switch
        {
            { TotalDays: >= 1 } => $"{span.TotalDays.ToString("F1", CultureInfo.InvariantCulture)} days",
            { TotalHours: >= 1 } => $"{span.TotalHours.ToString("F1", CultureInfo.InvariantCulture)} hours",
            { TotalMinutes: >= 1 } => $"{span.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} minutes",
            _ => $"{Math.Max(0, span.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture)} seconds",
        };

    private ToolOutcome List(JsonObject? arguments)
    {
        var (root, prefix) = Subtree(Required(arguments, "directory"));
        var lines = new List<string>();
        var found = 0;

        foreach (var entry in _index.FollowUnder(prefix))
        {
            if (entry.Session is not { } session || entry.Record is not { } record)
            {
                continue;
            }

            found++;
            var size = SessionLayout.SizeOnDisk(session.FullPath);

            lines.Add(
                $"{session.FullPath}\n"
                + $"  browser: {record.Browser}   size on disk: {Megabytes(size)}\n"
                + $"  created: {Stamp(record.Created)}   last used: {Stamp(record.LastUsed)}\n"
                + $"  {OutputSize(session)}\n"
                + $"  {InUse(session)}\n"
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

    /// <summary>
    /// The one line <c>browserai_list</c> prints about whether a session is
    /// being driven right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-20. Until then the listing reported browser,
    /// purpose, dates and size and performed no liveness check at all</b>, so a
    /// caller could not tell an abandoned session from one another agent was
    /// inside — which is the distinction that matters most in the turn before
    /// <c>browserai_destroy</c>.
    /// </para>
    /// <para>
    /// <b>Through the lock probe, and never the process-liveness check.</b>
    /// <see cref="SessionLock.ProbeLiveness"/> asks the kernel about
    /// <c>browserai.lock</c>; the process check asks for a handle on a peer,
    /// which a token may not be able to open and which names a pid Windows may
    /// already have reused. <b>It never opens the record</b> — a database open
    /// is orders of magnitude dearer, it can create a file in a directory nobody
    /// asked it to touch, and the newest holder statement cannot answer this
    /// question anyway.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-26 (previously "under this directory's own gate
    /// at zero timeout … the gate is what separates nobody has this from
    /// somebody is mid-rewrite").*** The gate is gone from this path and the
    /// defect it was closing is gone with the file it was about. Between
    /// 2026-08-20 and 2026-08-24 the listing read a bare probe's *not held* as
    /// free and printed <i>in use: no</i> about a session another agent was
    /// driving, because every forwarded call rewrote the record and dropped the
    /// ownership handle to do it. Nothing rewrites the guard: the only window
    /// left is between the rename and the first hold at acquisition, inside the
    /// per-directory gate, and what is seen there is *free* about a directory
    /// somebody is in the middle of taking — a momentary truth that corrects
    /// itself.
    /// </para>
    /// <para>
    /// <b>Three answers, and the third one is the reason this is not a
    /// <see langword="bool"/>.</b> An unreadable <c>browserai.lock</c> is not a free
    /// session; it is an unanswered question, and printing it as free is the one
    /// direction that costs a caller a session it was about to destroy.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE HOLDER IS DELIBERATELY NOT NAMED, AND THAT IS THE TRAP THIS
    /// METHOD IS BUILT AROUND.</b> A sharing violation says the file is held; it
    /// does not say by whom, and the record inside can describe a previous
    /// holder. That exact gap produced a wrong <i>sentence</i> — never a wrong
    /// owner — on 2026-08-19, and it is [a hazard row](../../../HAZARDS.md#hazard-index).
    /// Turning this answer into <i>"held by PID n"</i> would publish that
    /// sentence to a model on every listing rather than once in a refusal, so
    /// what is printed is the fact the probe can support and a note saying which
    /// fact it is not.
    /// </para>
    /// <para>
    /// <b>A session this process is already driving is answered without asking
    /// the kernel.</b> That is more accurate — it says <i>which</i> BrowserAI —
    /// and it removes the whole self-probing half of the cost and of the
    /// handle-collision exposure above.
    /// </para>
    /// <para>
    /// <b>Cost: one <c>CreateFile</c>/<c>CloseHandle</c> per listed entry, plus
    /// the gate's own create/acquire/release/close.</b> No process handle and no
    /// second directory walk. ⚠️ ***Corrected 2026-08-24 (previously "and nothing
    /// else. No process handle, no mutex, no second directory walk") — there IS
    /// now a mutex per entry, and the figures below are the file half only. The
    /// uncontended acquire alone was measured at 0.007–0.009 ms; the create/close
    /// pair is unmeasured and is deliberately not guessed at here.*** Measured
    /// 2026-08-20 at <b>0.035 ms</b> free and <b>0.049 ms</b>
    /// held
    /// ([kb](../../../kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20)),
    /// against <b>0.6–2.3 ms</b> for the <see cref="SessionLayout.SizeOnDisk"/>
    /// recursive enumeration this same loop already performs for every entry —
    /// measured on the same day over a 310-file tree, and a session whose
    /// profile has been used is far larger than that. <b>It is a seventeenth of
    /// a cost the listing already pays, on the smallest tree available to
    /// measure.</b>
    /// </para>
    /// <para>
    /// <b>A drive-root listing cannot become pathological on this account</b>:
    /// the loop is over the session <i>index</i> — one file per session
    /// BrowserAI has ever been told about — and the <c>directory</c> argument
    /// <i>filters</i> that list rather than causing a walk of it. Pointing this
    /// tool at <c>C:\</c> therefore adds one file open per known session and
    /// never one per file on the volume, and the entries it adds them for are
    /// exactly the entries it was already going to weigh.
    /// </para>
    /// <para>
    /// ⚠️ <b>That was true of this probe and read as though it were true of the
    /// listing, and until 2026-08-24 the listing was the expensive half.</b> The
    /// subtree filter ran <i>after</i> the record was opened, so every session on
    /// the machine had its <c>browserai.json</c> strictly parsed — and each of
    /// those opens carried <c>RenameWindow</c>'s budget — to print the few under
    /// the prefix. <see cref="SessionIndex.FollowUnder"/> is what moved the
    /// filter above the open.
    /// </para>
    /// </remarks>
    /// <param name="session">The canonicalised session directory.</param>
    /// <returns>The line, framed so a model can act on it.</returns>
    private string InUse(SessionPath session)
    {
        if (_live.ContainsKey(session.Key))
        {
            return "in use: YES — this BrowserAI process is driving it right now.";
        }

        var answer = SessionLock.ProbeLiveness(session);

        return answer.State switch
        {
            SessionLiveness.Held =>
                $"in use: YES — something holds '{session.LockFile}' right now. That is the kernel's answer about the file, not about who: the guard names whoever took the directory, so this does not say which process.",

            SessionLiveness.NotHeld =>
                $"in use: no — nothing held '{session.LockFile}'. It is a snapshot rather than a reservation: another agent can open the session immediately afterwards.",

            _ =>
                $"in use: UNKNOWN — {answer.Why} Treat it as possibly in use; this is not the same answer as 'no'.",
        };
    }

    private async Task<ToolOutcome> DestroyAsync(JsonObject? arguments)
    {
        var location = Resolve(Required(arguments, "directory"), "directory");

        // ⚠️ READ BEFORE ANYTHING IS TORN DOWN, and it is the ordering that
        // matters rather than the read. A missing `why` has to be a refusal that
        // changed nothing, and by the time the session's browser is closed and
        // its tree is walked there is nothing left to refuse into.
        var why = Why(arguments, SessionToolSurface.Destroy);

        if (_live.TryRemove(location.Key, out var live))
        {
            // Torn down first: the browser has to go before the tree it is
            // writing into, and this process's own handles on browserai.lock and
            // browserai.data have to be closed before any of it can be deleted.
            await live.DisposeAsync().ConfigureAwait(false);
        }

        // The single check that makes it safe to hand a model a tool that
        // deletes trees: it cannot be aimed at Documents\.
        // ⚠️ BEFORE THE READ, BECAUSE THE READ CANNOT ANSWER IT. A directory
        // holding the old record is intact and is a session -- just not one this
        // build can open -- so the honest answer is neither "not a session" nor
        // "damaged", and this tool must not delete a tree it cannot recognise
        // the contents of. The sentence is the maintainer's, verbatim.
        if (SessionLayout.OldFormatRefusal(location) is { } notThisFormat)
        {
            return new ToolOutcome(
                $"{notThisFormat}\n\nI cannot clean this up — remove the entire directory yourself.",
                IsError: true);
        }

        var record = SessionLock.ReadRecord(location)
            ?? throw new SessionToolException(
                $"'{location.FullPath}' has no '{SessionLayout.DataFileName}', so it is not a BrowserAI session and {SessionToolSurface.Destroy} will not touch it. "
                + "This refusal is what makes the tool safe: it deletes session directories and nothing else. Nothing was changed.");

        var taken = SessionLock.TryAcquire(
            location,
            // No `Entry`: this call takes the record in order to delete it,
            // and an entry appended to a file that is about to be unlinked is a
            // write nobody reads. The `why` is in the answer instead.
            new SessionLockRequest { Browser = record.Browser, Purpose = record.Purpose },
            _logger);

        if (taken.Acquired is not { } held)
        {
            return new ToolOutcome($"'{location.FullPath}' was not destroyed. {taken.Message}", IsError: true);
        }

        // ⚠️ Corrected 2026-08-18 (previously `held.Dispose()` on this line,
        // defended as "Windows will not remove a file this process is holding
        // open, and the lock has done its job the moment ownership is proven").
        // The first half is true of browserai.json and the second half is the defect:
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
        // This pass runs with browserai.lock and browserai.data STILL OPEN, so it
        // removes everything except those two and the directory above them. Its failure list is
        // discarded because those two are in it by construction and neither is
        // news; the pass below is the one whose survivors the caller is told
        // about, and it re-tries anything this one could not take.
        TreeDelete.Remove(location.FullPath, []);

        // Release and finish inside one hold of the per-directory gate, so the
        // instant in which browserai.lock is unheld and still on disk is an
        // instant no peer's create-or-take can be inside.
        held.ReleaseAndDelete(() => TreeDelete.Remove(location.FullPath, failures));

        _index.Forget(location);
        var rollUp = RefreshRollUp(location);
        SessionToolLog.Destroyed(_logger, location.FullPath, failures.Count);

        var summary =
            $"Destroyed the session at '{location.FullPath}' ({Megabytes(size)}). Its purpose was: {record.Purpose}\nYou said it was being destroyed because: {why}";

        if (!rollUp.RolledUp && Path.GetDirectoryName(location.FullPath) is { Length: > 0 } parent)
        {
            // The destroyed session is still listed in the file until this
            // succeeds, and a roll-up naming a directory that is gone is worse
            // than one that is merely behind.
            summary +=
                $"\n\n⚠️ The roll-up at '{Path.Combine(parent, SessionRollUp.FileName)}' could not be rewritten, so it still lists this session. Nothing else depends on it: browserai_list reads BrowserAI's own index.";
        }

        // ⚠️ THE SURVIVOR ARM IS `IsError: true`, CHANGED 2026-08-19 (previously
        // `IsError: false`, defended as "a destroy that removed a
        // nine-thousand-file profile and could not remove eleven locked files
        // has done what it was asked"). That defence still stands and is not
        // what moved: the maintainer's call is that a call which did not
        // entirely do the thing it is named for must not be indistinguishable,
        // to a model scanning result shapes, from one that did.
        //
        // The objection this was taken over is recorded in QUESTIONS.md §11 —
        // an error invites a retry, and a retry finds no session and refuses,
        // which is a worse message than the truthful one. The refinement that
        // answers it is the text below: it says the session IS destroyed, says
        // in as many words not to call this tool again, and says what to do
        // instead. A model that reads it cannot reach the retry the objection
        // predicted.
        //
        // INLINE RATHER THAN IN `SessionErrors`, deliberately, and the
        // directory's own CLAUDE.md is why the question comes up: refusals live
        // in the catalogue. This is not a refusal. Nothing was declined, the
        // work was done, and what is returned is a report composed out of the
        // summary, the tally and the listing -- three things a catalogue row
        // cannot hold.
        return failures.Count is 0
            ? new ToolOutcome(summary, IsError: false)
            : new ToolOutcome(
                $"{summary}\n\nBUT {failures.Count.ToString(CultureInfo.InvariantCulture)} {SurvivorsHeading}\n"
                + Listing(failures)
                + "\n\nThe session itself IS destroyed: its record is gone and BrowserAI's index has forgotten it, so what is listed above is residue on disk rather than a session."
                + $"\nDo NOT call {SessionToolSurface.Destroy} on '{location.FullPath}' again — there is no session there for it to destroy, and it will refuse."
                + "\nWhat is left is outside BrowserAI: wait for whatever still holds those files to exit and then delete them yourself, or leave them. Nothing in BrowserAI reads them again.",
                IsError: true);
    }

    private ToolOutcome SetPurpose(JsonObject? arguments)
    {
        var location = Resolve(Required(arguments, "session"), "session");
        var purpose = RecordText.Sanitise(Required(arguments, "purpose"));
        var why = Why(arguments, SessionToolSurface.SetPurpose);

        if (_live.TryGetValue(location.Key, out var live))
        {
            var previous = live.Lock.Record.Purpose;

            // ⚠️ THE ROW FIRST AND THE STATEMENT SECOND, and the ordering is the
            // one property this used to buy with a single rewrite. Both are
            // appends now, so there is no "one write" to make them atomic in --
            // and a reader that lands between them finds a purpose change whose
            // explanation has ALREADY arrived, which is the harmless order. The
            // reverse would show a purpose nothing accounts for.
            var row = live.Lock.Append(SessionToolSurface.SetPurpose, why);

            live.Lock.AppendPurpose(purpose);
            live.Lock.Settle(row, SessionStore.Successful, failure: null);

            SessionToolLog.Why(live.Logger, SessionToolSurface.SetPurpose, why);
            SessionToolLog.PurposeChanged(_logger, location.FullPath, previous, purpose);

            return new ToolOutcome($"Purpose of '{location.FullPath}' is now: {purpose}\nIt was: {previous}", IsError: false);
        }

        var recorded = SessionLock.ReadRecord(location)
            ?? throw new SessionToolException(
                $"'{location.FullPath}' has no '{SessionLayout.DataFileName}', so it is not a BrowserAI session and has no purpose to set. Nothing was changed.");

        var taken = SessionLock.TryAcquire(
            location,
            new SessionLockRequest
            {
                Browser = recorded.Browser,
                Purpose = purpose,
                Entry = new SessionCall(SessionToolSurface.SetPurpose, why),
            },
            _logger);

        if (taken.Acquired is not { } held)
        {
            return new ToolOutcome($"The purpose of '{location.FullPath}' was not changed. {taken.Message}", IsError: true);
        }

        held.SettleOpening(SessionStore.Successful, failure: null);
        held.Dispose();

        // THE PROCESS LOG, because a closed session has no logging stack of its
        // own -- SessionLogging is created by OpenAsync and disposed when the
        // session is torn down. The line carries the directory for that reason:
        // it lands in a machine-wide file beside every other session's, where a
        // scoped logger would not have needed saying.
        SessionToolLog.WhyForClosedSession(_logger, SessionToolSurface.SetPurpose, location.FullPath, why);
        SessionToolLog.PurposeChanged(_logger, location.FullPath, recorded.Purpose, purpose);

        return new ToolOutcome(
            $"Purpose of '{location.FullPath}' is now: {purpose}\nIt was: {recorded.Purpose}\nThe session was not running, so it was updated in place and left closed.",
            IsError: false);
    }

    /// <summary>
    /// The sixth authored tool: delete one shared browser tree and download it
    /// again, or refuse and say what is using it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-19 at Firefox support (previously no arguments at
    /// all — "there is nothing to name: the install is shared by every session
    /// on this machine").</b> <b>The stated reason expired rather than being
    /// overruled.</b> With one family provisioned there was genuinely nothing to
    /// name; with <see cref="ProvisionedBrowsers.Families"/> holding two there
    /// are two trees, two revisions and two mutexes, and the caller's broken
    /// browser is exactly one of them. So the tool now takes one
    /// <b>required</b> argument. The other two candidates were weighed and
    /// rejected:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>Reinstall both trees.</b> Keeps the no-arguments property, and makes
    /// the blast radius worse in the one situation the tool exists for: a
    /// caller with a broken Firefox pays 331 MB, loses a working Chromium for
    /// the length of its own re-download, and if the network fails mid-way ends
    /// the call with two broken browsers instead of one. A repair tool must not
    /// be able to break something that was working.
    /// </item>
    /// <item>
    /// <b>Optional, defaulting to Chromium.</b> The worst of the three: a caller
    /// whose Firefox is broken calls it, Chromium is deleted and re-downloaded,
    /// and the answer says a reinstall succeeded. Success-shaped, nothing fixed
    /// — the failure class the whole product is written against.
    /// </item>
    /// </list>
    /// <para>
    /// <b>Required rather than optional-with-no-default.</b> <i>Corrected
    /// 2026-08-20 (previously "following <c>mode</c> on <c>init</c> … the
    /// precedent already settled in this file").</i> That precedent went with
    /// session modes; the rule it stated survives on its own, which is that an
    /// argument whose omission cannot be answered honestly is required. The
    /// caller always knows which family: the refusal that sent them here names
    /// it, <c>browserai.data</c> records it, and <c>browserai_list</c> prints it per
    /// session.
    /// </para>
    /// <para>
    /// <b>Naming the family narrows the refusal as well as the delete.</b> The
    /// running-process check was always per-directory; with the family named it
    /// is per-family in effect too, so an open Chromium session no longer blocks
    /// a Firefox reinstall. Nothing was relaxed to achieve that — the check asks
    /// the same question of a smaller tree.
    /// </para>
    /// <para>
    /// <b>It refuses rather than coordinates, and that is the design rather than
    /// a limitation.</b> A browser install is shared by every session on the
    /// machine that uses that family, so "make this safe" would mean terminating
    /// browsers other agents are driving. There is deliberately no force
    /// argument: force here has exactly one meaning and it is the wrong one.
    /// </para>
    /// <para>
    /// <b><c>ffmpeg</c> and <c>winldd</c> are shared by both families and are
    /// deliberately not touched.</b> Both installs fetch them into the same
    /// browsers root under their own revision directories, each with its own
    /// completion marker, so a family's reinstall deletes
    /// <c>chromium-&lt;rev&gt;</c> or <c>firefox-&lt;rev&gt;</c> and nothing
    /// else. A corrupt <c>ffmpeg</c> is therefore <i>not</i> repairable through
    /// this tool by either family — recorded as a limitation rather than
    /// discovered as one.
    /// </para>
    /// <para>
    /// <b>Download-beside-and-swap does not work on Windows</b>, which is what
    /// makes the refusal load-bearing rather than merely polite: a directory
    /// holding open executables cannot be renamed, so there is no arrangement in
    /// which the old tree keeps serving while a new one is fetched. The window
    /// with no browser installed is unavoidable and is stated.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously "The check here answers 'is
    /// anything RUNNING FROM the tree', and that is half the question … a session
    /// that opened a browser between the check and the delete makes the delete
    /// fail on an open executable, so THAT race produces a refusal with evidence
    /// rather than a corrupted tree").</b> The second half was too generous. A
    /// browser opened in that window fails the delete <i>on Windows</i>, which is
    /// true and is not the whole race: the peer's session is <b>created</b> in
    /// that window too, and a session whose tree was deleted from under it is not
    /// a failed delete, it is a live session pointing at nothing. The answer is
    /// the machine-wide claim this method now takes first — see
    /// <see cref="MaintenanceLock"/> — which stops the peer's <c>init</c> rather
    /// than losing a race with it.
    /// </para>
    /// <para>
    /// <b>And the session gate is now unconditional.</b> It used to be asked only
    /// when a process was already running out of the tree, so a session that was
    /// open with its browser not currently launched let the delete through. The
    /// maintainer's decision of 2026-08-19: <i>"No reinstall if there is any
    /// session running system wide. Including any reinstall sessions."</i> See
    /// <see cref="TheRootIsBusy"/>.
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
    private async Task<ToolOutcome> ReinstallBrowserAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var target = Browser(arguments, "browser", fallback: null, ProvisionedBrowsers.ReinstallTargets);

        // ⚠️ FIRST, AND HELD FOR THE WHOLE CALL. This is the WRITER half of the
        // machine-wide reader/writer claim on the browsers root, and it is the
        // whole gate: every open session holds the same file shared, so this
        // open is refused by the kernel while any of them lives -- of any
        // family, in any process, on this machine. Everything below -- the
        // process census, the recursive delete and the download -- happens
        // inside it, so a peer's `browserai_init` cannot launch a browser into a
        // tree this call is part way through deleting.
        //
        // The ordering is the deadlock argument: this is the OUTERMOST lock and
        // the provisioning mutexes are taken under it, never the other way
        // round. See MaintenanceLock's remarks.
        using var maintenance = MaintenanceLock.TryTakeExclusive(
            _environment.Paths.BrowsersDirectory,
            target,
            out var denial,
            out var denialDetail);

        if (maintenance is null)
        {
            return new ToolOutcome(TheRootIsBusy(denial, denialDetail), IsError: true);
        }

        return ProvisionedBrowsers.IsShared(target)
            ? await ReinstallSharedAsync(cancellationToken).ConfigureAwait(false)
            : await ReinstallFamilyAsync(target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The refusal <c>init</c> and <c>resume</c> earn when the shared claim
    /// could not be taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The take IS the check since 2026-08-20 (previously a
    /// <c>MaintenanceLock.Probe</c> that acquired nothing).</b> The probe existed
    /// because an <c>init</c> that took the claim for a microsecond would make a
    /// racing reinstall report that another reinstall was running — and under the
    /// reader/writer design there is nothing to probe with: an open that a
    /// reinstall's exclusive handle refuses is exactly the shared open a session
    /// needs anyway, and it is held rather than released, so there is no
    /// microsecond and no second question.
    /// </para>
    /// <para>
    /// <b>The refusal names the reinstall by quoting the record the writer
    /// wrote</b>, and carries how far in it is — see
    /// <see cref="SessionErrors.BrowsersAreBeingReinstalled"/>. A caller blocked
    /// by a 203.8 MB download should learn what it is waiting on and how far
    /// through it is, which is the same treatment first-run provisioning already
    /// gives.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool being refused.</param>
    /// <returns>The refusal.</returns>
    private ToolOutcome ReinstallHoldsTheRoot(string tool) =>
        new(
            SessionErrors.BrowsersAreBeingReinstalled(
                tool,
                _environment.Paths.BrowsersDirectory,
                MaintenanceLock.Describe(_environment.Paths.BrowsersDirectory),
                MaintenanceLock.ProgressOf(_environment.Paths.BrowsersDirectory)),
            IsError: true);

    /// <summary>
    /// Picks between the two refusals a failed shared claim can earn, on the
    /// kernel's own answer.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Added 2026-08-24. Until then every cause wore the reinstall's
    /// sentence</b>, so an ACL denial, a full volume and a path that is too long
    /// all told the caller to wait minutes for a download that was not running —
    /// and the recovery for those is the opposite of waiting. A sharing violation
    /// on this open really is contention, by the exclusion arithmetic in
    /// <see cref="MaintenanceLock"/>'s remarks, so <b>that half is deliberately
    /// left unhedged</b>: hedging a sentence that is right in every case the
    /// kernel can distinguish, to cover one it cannot, is a loss.
    /// </remarks>
    /// <param name="tool">The tool being refused.</param>
    /// <param name="denial">What the kernel said.</param>
    /// <param name="detail">Windows' own message, for the unreachable arm.</param>
    /// <returns>The refusal.</returns>
    private ToolOutcome TheRootCouldNotBeClaimed(string tool, MaintenanceDenial denial, string detail) =>
        denial is MaintenanceDenial.Unreachable
            ? new ToolOutcome(
                SessionErrors.TheBrowsersRootCouldNotBeClaimed(tool, _environment.Paths.BrowsersDirectory, detail),
                IsError: true)
            : ReinstallHoldsTheRoot(tool);

    /// <summary>
    /// The refusal a reinstall earns when the exclusive claim could not be
    /// taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The kernel decides, and this only says which of the two causes it
    /// was — a correction of 2026-08-20 (previously a census that decided).</b>
    /// The gate used to be a census that counted sessions and
    /// refused on the count; it is now the exclusive open itself, which is
    /// refused while any session holds the same file shared. That is strictly
    /// stronger: a session whose index entry is missing, or whose process this
    /// one cannot see, still holds a handle.
    /// </para>
    /// <para>
    /// <b>The two causes are mutually exclusive by construction, which is why no
    /// parsing is needed to tell them apart.</b> A reinstall can only hold the
    /// claim exclusively when no session holds it shared — so if the census finds
    /// sessions, sessions are the cause and they are what the caller must close;
    /// if it finds none <i>and the kernel said the file was held</i>, another
    /// reinstall has it and the record says which.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24 (previously "if it finds none, another
    /// reinstall has it and the record says which").*** There is a third case the
    /// census cannot see and it was being reported as the second: <b>the claim
    /// could not be opened at all.</b> The mutual exclusion holds for the kernel's
    /// refusal and not for the count, so a census of zero over a file nothing
    /// could open concluded <i>another reinstall has it</i> about a machine on
    /// which no reinstall was running.
    /// </para>
    /// <para>
    /// ⚠️ <b>The family filter is gone, and the maintainer removed it on purpose:
    /// <i>"No matter the browser type."</i></b> <see cref="LiveSessions"/> used to
    /// take a family and list only sessions of it, because only a Chromium
    /// session can hold an executable out of the Chromium tree. That reasoning is
    /// still true and no longer relevant: the claim is one file at the root of the
    /// browsers directory and knows nothing about families, so a live Firefox
    /// session refuses a Chromium reinstall. Listing only the matching family
    /// would now name none of the sessions the caller has to close.
    /// </para>
    /// <para>
    /// <b>There is no drain and no intent marker, and writer starvation is
    /// accepted</b> — the maintainer's words: <i>"it should not start a
    /// drain/preventstart process of sorts. Keep it simple. Let the user solve the
    /// open sessions block."</i>
    /// </para>
    /// </remarks>
    /// <returns>The whole refusal.</returns>
    private string TheRootIsBusy(MaintenanceDenial denial, string detail)
    {
        var root = _environment.Paths.BrowsersDirectory;

        // ⚠️ BEFORE THE CENSUS, and the order is the fix. A census of zero over
        // a file nothing could open is not evidence of a second reinstall; it is
        // evidence that the question was never asked.
        if (denial is MaintenanceDenial.Unreachable)
        {
            return SessionErrors.TheBrowsersRootCouldNotBeClaimed(SessionToolSurface.ReinstallBrowser, root, detail);
        }

        var claimants = LiveSessions();

        if (claimants.Count is 0)
        {
            // Nothing holds it shared, so what holds it is another writer --
            // mutual against itself, and the maintainer said so explicitly:
            // "Including any reinstall sessions."
            return SessionErrors.BrowsersAreBeingReinstalled(
                SessionToolSurface.ReinstallBrowser,
                root,
                MaintenanceLock.Describe(root),
                MaintenanceLock.ProgressOf(root));
        }

        return $"{SessionToolSurface.ReinstallBrowser} was not run: it needs this machine's browsers root to itself, and {claimants.Count.ToString(CultureInfo.InvariantCulture)} session(s) are holding it:\n"
            + Listing(claimants)
            + $"\nEvery open session holds '{Path.Combine(root, MaintenanceLock.FileName)}' shared for its whole life, whatever browser family it uses, so a live {ProvisionedBrowsers.Firefox} session blocks a {ProvisionedBrowsers.Chromium} reinstall as surely as a {ProvisionedBrowsers.Chromium} one does — nothing runs out of this root while it is being replaced. "
            + "Nothing was changed and nothing was terminated. This call did not wait for those sessions and never will, because waiting on a browser a human may not close is not a thing a tool call may do, and it published no intent that would stop new sessions starting meanwhile. "
            + $"Close them, or wait for them to end, and call {SessionToolSurface.ReinstallBrowser} again. There is deliberately no force option — forcing here means killing browsers other agents are driving.";
    }

    /// <summary>
    /// <c>browserai_reinstall_browser</c> against <c>ffmpeg</c> and
    /// <c>winldd</c>, which both families use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE REFUSAL IS DELIBERATELY WIDER THAN THE FAMILY PATH'S, AND THIS
    /// IS THE DECISION.</b> A family reinstall is gated on <i>a process running
    /// out of that tree</i>, and for a family that question and <i>a session is
    /// driving this browser</i> are the same question: <c>chrome.exe</c> lives
    /// for the session's life and holds its own image open, so the gate reads as
    /// "somebody is using this" and Windows refuses to unlink an open image as a
    /// second line of defence.
    /// </para>
    /// <para>
    /// <b>For the shared components the two questions come apart.</b>
    /// <c>ffmpeg-win64.exe</c> exists only while a recording is running and
    /// <c>PrintDeps.exe</c> only during dependency validation at launch — so a
    /// process-only gate answers <i>nothing is using it</i> on a machine with ten
    /// live sessions, any of which starts the codec the instant a
    /// <c>video</c> artifact is asked for, and the tree is then being deleted
    /// underneath it. The consequence is not symmetric either: deleting a family
    /// tree under a live browser is refused by the filesystem, and deleting
    /// <c>ffmpeg-&lt;rev&gt;</c> while nothing happens to be running out of it
    /// succeeds.
    /// </para>
    /// <para>
    /// <b>So this refuses while ANY session is open, of EITHER family</b>, and
    /// still reports a process running out of either tree that no session
    /// accounts for. The direction of the trade is chosen on what each mistake
    /// costs: a refusal that says <i>close your sessions</i> is recoverable in a
    /// turn, and a shared tree corrupted <i>by the operation that exists to
    /// repair it</i> is the failure this whole value was added to fix.
    /// </para>
    /// <para>
    /// ⚠️ <b>The family path caught up on 2026-08-19 and the gap between them
    /// narrowed to one thing: WHICH sessions count.</b> Both now refuse on an
    /// open session unconditionally; this one counts every family and a family
    /// reinstall counts its own. What was described above as "stricter" is now
    /// only <i>wider</i>.
    /// </para>
    /// <para>
    /// <b>Still no force flag</b>, for the same reason the family path has none.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>What to tell the caller.</returns>
    private async Task<ToolOutcome> ReinstallSharedAsync(CancellationToken cancellationToken)
    {
        var directories = _environment.Provisioner.SharedComponentDirectories();
        var named = string.Join("' and '", directories);
        var running = new List<RunningImage>();

        // ⚠️ NO SESSION GATE HERE ANY MORE, and its absence is the design rather
        // than a deletion. The caller could not have reached this method with a
        // session open anywhere on the machine: `ReinstallBrowserAsync` holds the
        // browsers root EXCLUSIVELY, and every live session holds the same file
        // shared. What used to be a census this method ran is now the kernel's
        // answer to one open, taken before this method was called.
        foreach (var directory in directories)
        {
            try
            {
                running.AddRange(BrowserProcesses.RunningFrom(directory));
            }
            catch (Win32Exception failure)
            {
                return new ToolOutcome(
                    $"{SessionToolSurface.ReinstallBrowser} was not run: BrowserAI could not enumerate processes to check whether anything is still using '{directory}' ({failure.Message}). Nothing was changed. "
                    + "It refuses rather than guessing, because deleting a tree that something is running from leaves a directory that is neither the old install nor the new one.",
                    IsError: true);
            }
        }

        if (running.Count is not 0)
        {
            // Live processes out of a shared tree that no session accounts for.
            // Reported by full path, never terminated.
            return new ToolOutcome(
                SessionErrors.UnattributableBrowserRunning(
                    SessionToolSurface.ReinstallBrowser,
                    named,
                    [.. running.Select(entry => (entry.ProcessId, entry.ImagePath))]),
                IsError: true);
        }

        var outcome = await _environment.Provisioner.ReinstallSharedAsync(cancellationToken).ConfigureAwait(false);

        return AnswerFor(ProvisionedBrowsers.Shared, outcome);
    }

    private async Task<ToolOutcome> ReinstallFamilyAsync(string browser, CancellationToken cancellationToken)
    {
        var directory = _environment.Provisioner.DirectoryFor(browser);

        // ⚠️ NO SESSION GATE HERE ANY MORE -- see ReinstallSharedAsync for the
        // same note. It is the exclusive claim on the browsers root, taken
        // before this method was called, and it does not distinguish families:
        // a live Firefox session refuses a Chromium reinstall.
        //
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
            // Live browsers, and no session anywhere accounts for them -- the
            // gate above already answered the case where one does. §H.4 row 13:
            // reported, never terminated.
            return new ToolOutcome(
                SessionErrors.UnattributableBrowserRunning(
                    SessionToolSurface.ReinstallBrowser,
                    directory,
                    [.. running.Select(entry => (entry.ProcessId, entry.ImagePath))]),
                IsError: true);
        }

        var outcome = await _environment.Provisioner.ReinstallAsync(browser, cancellationToken).ConfigureAwait(false);

        return AnswerFor(browser, outcome);
    }

    /// <summary>
    /// The answer both reinstall paths compose, which is the same sentence set
    /// over one tree or two.
    /// </summary>
    /// <param name="target">What the caller named.</param>
    /// <param name="outcome">What happened.</param>
    /// <returns>What to tell the caller.</returns>
    private static ToolOutcome AnswerFor(string target, ReinstallOutcome outcome)
    {
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
                $"'{outcome.Named}' was only partly removed, so nothing was downloaded on top of it and the {target} install is now incomplete. {outcome.Failures.Count.ToString(CultureInfo.InvariantCulture)} item(s) survived:\n"
                + Listing(outcome.Failures)
                + $"\nSomething still has those files open. Once it has exited, call {SessionToolSurface.ReinstallBrowser} again — it will delete what is left and download a complete tree.",
                IsError: true);
        }

        return outcome.Status.State is ProvisioningState.Installed
            ? new ToolOutcome(
                $"Re-provisioned {target}. '{outcome.Named}' was deleted ({removed}) and downloaded again. {outcome.Status.Detail}",
                IsError: false)
            : new ToolOutcome(
                $"'{outcome.Named}' was deleted ({removed}) and the download that should have replaced it did not complete, so there is no {target} installed now. {outcome.Status.Detail} "
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
    /// <c>browserai.lock</c> whose holder is checked with
    /// <see cref="ProcessLiveness.IsAlive"/> — pid and creation time together,
    /// never a pid alone, because Windows reuses pids and a reclaim keyed on one
    /// eventually reads a stranger as the holder.
    /// <para>
    /// ⚠️ <b>The family filter is gone, removed 2026-08-20 at the maintainer's
    /// decision</b> *(previously "Filtered by family since 2026-08-19, and the
    /// filter is what makes the refusal actionable … listing a live Chromium
    /// session beside a blocked Firefox reinstall would tell the caller to close
    /// the wrong browser")*. His words were <i>"any init or resume should take a
    /// system level lock. No matter the browser type"</i>, and the lock is one
    /// file at the root of the browsers directory that knows nothing about
    /// families — so a live Firefox session really does refuse a Chromium
    /// reinstall, and listing only the matching family would name none of the
    /// sessions the caller has to close. The old reasoning was not wrong and is
    /// no longer the question: it was about which sessions can hold an
    /// <i>executable</i> out of a tree, and the gate is no longer about
    /// executables.
    /// </para>
    /// <para>
    /// <b>This list explains a refusal; it never decides one.</b> The kernel
    /// decides, on the exclusive open. So a session this walk cannot see — an
    /// index entry that was swept, a record that will not parse — costs the
    /// refusal a line rather than costing the machine a guarantee.
    /// </para>
    /// </remarks>
    /// <returns>One line per live session, of any family.</returns>
    private List<string> LiveSessions()
    {
        var lines = new List<string>();

        foreach (var session in _live.Values)
        {
            lines.Add($"  {session.Location.FullPath} — open in this BrowserAI (browser '{session.Lock.Record.Browser}')");
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

            // ⚠️ THE GUARD, NOT THE RECORD'S PID. The newest holder statement
            // says who took the directory, which is a different question from
            // whether anybody still has it -- and the kernel answers the second
            // one without opening a process handle a token may not be allowed to
            // open, and without naming a pid Windows may already have reused.
            if (SessionLock.ProbeLiveness(session).State is SessionLiveness.Held)
            {
                lines.Add($"  {session.FullPath} — held by another process since {Stamp(record.LastUsed)}");
            }
        }

        return lines;
    }

    private async Task<ToolOutcome> OpenAsync(
        SessionPath location,
        SessionLockRequest request,
        bool headed,
        bool tracing,
        RunOptions run,
        bool debug,
        bool createdHere,
        IReadOnlyList<string> notes,
        MaintenanceLock claim,
        CancellationToken cancellationToken,
        string? movedFrom = null,
        string? why = null)
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
            // The session's logging stack is built FIRST, before the lock, so
            // that taking the lock is one of the things it records at the level
            // this call asked for. An earlier version acquired first and logged
            // the acquisition at the process-wide level, which left a `debug`
            // session's records starting mid-story.
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
            // constant: `resume` reads it out of the record, and a profile
            // belongs to the browser that made it.
            var config = BrowserConfiguration.ForSession(location, headed, request.Browser, tracing, run);
            var configFile = Path.Combine(
                _environment.InstanceDirectory,
                $"playwright-mcp-{location.Hash[..16]}.json");

            var options = ChildLaunch.Create(
                _environment.Payload,
                _environment.Paths.BrowsersDirectory,

                // ⚠️ THE OUTPUT ROOT RATHER THAN THE SESSION ROOT, AND SINCE
                // 2026-08-26 IT IS THE WHOLE OF THE CONTAINMENT RATHER THAN THE
                // cheapest of several levers. Upstream resolves a relative
                // `filename` against the child's cwd and refuses anything that
                // resolves outside `outputDir` or that cwd -- BrowserAI writes
                // both as this one directory and writes
                // `allowUnrestrictedFileAccess: false` so the check runs -- so
                // the two allowed roots coincide rather than overlapping, and a
                // caller's own string never reaches the filesystem through
                // anything of ours.
                Path.Combine(location.FullPath, SessionLayout.OutputFolderName),
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

            session = new LiveSession(location, held, claim, child, logging, config, configFile, createdHere, _environment.BrowserIdlePeriod, _environment.Clock);
#pragma warning restore CA2000

            if (!_live.TryAdd(location.Key, session))
            {
                return new ToolOutcome(
                    $"'{location.FullPath}' was opened by another call on this connection while this one was starting. Nothing was changed by this call; use the session that exists.",
                    IsError: true);
            }

            // Everything above is now owned by the dictionary.
            handedOver = true;

            // ⚠️ THE ROW THE ACQUISITION WROTE IS SETTLED HERE AND NOWHERE
            // EARLIER. It was written `in-flight` before the child was launched,
            // which is the same ordering every forwarded call uses and for the
            // same reason: a launch that hangs still leaves a record of what the
            // directory was for.
            held.SettleOpening(SessionStore.Successful, failure: null);
            _index.Record(location);
            SessionToolLog.Opened(sessionLogger, location.FullPath, headed, createdHere);

            if (why is not null)
            {
                SessionToolLog.Why(sessionLogger, SessionToolSurface.Resume, why);
            }

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
            // already names the recovery. Reachable from `init` as well as
            // `resume` since 2026-08-19 -- previously only from `resume`, which
            // reads the family out of browserai.data, because `init` could not record
            // Firefox at all.
            SessionToolLog.CouldNotOpen(_logger, location.FullPath, collision);
            Failed(acquired, collision);

            return new ToolOutcome(collision.Message, IsError: true);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            SessionToolLog.CouldNotOpen(_logger, location.FullPath, failure);
            Failed(acquired, failure);

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

                    // And nor may it leave the browsers root claimed. A shared
                    // claim nobody releases is a reinstall that can never run
                    // again on this machine, and the caller would have no way to
                    // find out why. `LiveSession` owns it on the path that
                    // succeeds; this is every path that does not.
                    claim.Dispose();

                    // Last, so the release above is still recorded in it.
                    logging?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Records that the call which took the directory did not end well, on the
    /// row it wrote before it tried.
    /// </summary>
    /// <remarks>
    /// <b>It runs before the lock is disposed and the failure payload is the
    /// exception with its stack.</b> The row is the only durable evidence that
    /// an <c>init</c> was attempted here at all — the session never opened, so
    /// there is no live session to ask — and a caller resuming the directory
    /// tomorrow meets *this browser would not start, and here is what it said*
    /// rather than a purpose with nothing behind it.
    /// </remarks>
    /// <param name="acquired">The lock, if one was taken.</param>
    /// <param name="failure">What went wrong.</param>
    private static void Failed(SessionLock? acquired, Exception failure) =>
        acquired?.SettleOpening(SessionStore.Failed, Encoding.UTF8.GetBytes(failure.ToString()));

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
            .Append("  browser: ").Append(record.Browser).Append('\n')
            .Append("  profile: ").Append(Path.Combine(session.Location.FullPath, SessionLayout.ProfileFolderName)).Append('\n')
            .Append("  output: ").Append(Path.Combine(session.Location.FullPath, SessionLayout.OutputFolderName))
            .Append(" — every file a tool writes lands here, flat, under whatever name the tool was given. Pass a plain 'filename' such as login.png; an absolute one, or one that climbs out of this directory, is refused by the browser server itself. A name that already exists is OVERWRITTEN.\n")
            .Append("  downloads: ").Append(Path.Combine(session.Location.FullPath, SessionLayout.DownloadsFolderName)).Append('\n')
            .Append("  purpose: ").Append(record.Purpose).Append('\n')
            .Append("  created: ").Append(Stamp(record.Created)).Append("   last used: ").Append(Stamp(record.LastUsed)).Append('\n')
            .Append("  viewport: ").Append(session.Config.Opinions.FirstOrDefault(opinion => opinion.Path == "browser.contextOptions.viewport.width")?.Value.ToString() ?? "?")
            .Append('x').Append(session.Config.Opinions.FirstOrDefault(opinion => opinion.Path == "browser.contextOptions.viewport.height")?.Value.ToString() ?? "?")
            .Append(" — a screenshot arrives at this size unscaled, so it is what one costs you\n")
            .Append("  child protocol: ").Append(session.Child.NegotiatedProtocolVersion ?? "<none>").Append('\n')
            .Append("  browserProvisioning: ").Append(Provisioning(record.Browser)).Append('\n');

        if (session.Config.HarPath is { } har)
        {
            _ = text
                .Append("  ⚠️ NETWORK CAPTURE IS ON for this run: '").Append(har)
                .Append("'. Every request and response, headers included, is being written there in clear text — every bearer token and session cookie that crosses the wire. Service workers are BLOCKED while it is on, so the site may behave differently from an ordinary run. Delete the file when you are done with it.\n");
        }

        _ = text.Append(HowItGotHere(record));

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
                .Append(Path.Combine(root, SessionRollUp.FileName))
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
        SessionRecord? record;

        try
        {
            record = SessionLock.ReadRecord(location);
        }
        catch (SessionRecordException failure)
        {
            return $"'{location.FullPath}' already holds a BrowserAI record, and it cannot be read: {failure.Message} "
                + $"{SessionToolSurface.Init} will not overwrite a session record it does not understand.";
        }

        return record is null
            ? null
            : SessionErrors.SessionAlreadyExists(
                location.FullPath,
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

    /// <summary>
    /// Whether a path a record carries names the directory the record was found
    /// in.
    /// </summary>
    /// <remarks>
    /// <b>Read and not Named, because the record may have been written on
    /// another machine.</b> A recorded path this build would not have written is
    /// not this directory, and the move/copy test downstream settles what to do
    /// about it by asking whether it exists — which is a filesystem call this one
    /// is not entitled to make on a stranger's string.
    /// </remarks>
    private static bool SamePath(string recorded, SessionPath location)
    {
        if (CanonicalPath.Of(recorded, PathOrigin.Read, RecordFields.Directory).Canonical is not { } canonical)
        {
            return false;
        }

        try
        {
            return string.Equals(SessionPath.For(canonical).Key, location.Key, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The subtree <c>browserai_list</c> was pointed at, as a display path and as
    /// a case-folded prefix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It goes through the same function every other door does — corrected
    /// 2026-08-26, previously a bare <c>Path.GetFullPath</c> plus an upper-cased
    /// prefix, with no alias resolution at all.</b> That second chain existed
    /// because the shared one refused a volume root, and a volume root is exactly
    /// what a caller passes to <c>list</c> to see everything. The cost of the
    /// exception was the worst answer this product could give: a caller who
    /// listed <c>D:\link\work</c> where the sessions live under
    /// <c>C:\real\work</c> was told <i>"No BrowserAI sessions under '…'. That is
    /// an answer rather than an error"</i> — confidently, wrongly, and with
    /// nothing to correct because it was not a refusal.
    /// </para>
    /// <para>
    /// <b>What made it fixable was splitting the two questions.</b>
    /// <see cref="CanonicalPath"/> answers <i>what does the filesystem call
    /// this</i> and has no opinion about volume roots;
    /// <see cref="SessionPath.For"/> answers <i>may this be a session
    /// directory</i>, which is the question <c>list</c> is not asking.
    /// </para>
    /// </remarks>
    private static (string Root, string Prefix) Subtree(string directory)
    {
        var verdict = CanonicalPath.Of(directory, PathOrigin.Named, "directory");

        if (verdict.Refusal is { } refusal)
        {
            throw new SessionToolException(refusal);
        }

        return (verdict.Canonical!, CanonicalPath.PrefixOf(verdict.Canonical!));
    }

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
    /// <c>SessionRollUp.Write(root, beneath);</c> — discarding the answer
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

        return (beneath, SessionRollUp.Write(root, beneath));
    }

    /// <summary>Every session under a root, newest use first.</summary>
    /// <remarks>
    /// ⚠️ <b>The prefix comes from the one derivation — W8, closed 2026-08-26.</b>
    /// This re-derived it: <c>ToUpperInvariant</c>, then append a separator, three
    /// lines from a call into <see cref="SessionIndex"/>, whose own remark
    /// forbade re-deriving the predicate in as many words. It was benign because
    /// <paramref name="root"/> is a directory name off an already-canonical
    /// <see cref="SessionPath"/> — and nothing said so, and nothing would have
    /// noticed when it stopped being true.
    /// </remarks>
    /// <param name="root">The canonical directory the sessions sit under.</param>
    /// <returns>The roll-up entries.</returns>
    private List<RollUpEntry> Beneath(string root)
    {
        var prefix = CanonicalPath.PrefixOf(root);
        var entries = new List<RollUpEntry>();

        foreach (var entry in _index.FollowUnder(prefix))
        {
            if (entry.Session is not { } session || entry.Record is not { } record)
            {
                continue;
            }

            entries.Add(new RollUpEntry(
                session.FullPath,
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

    /// <summary>
    /// How this session got here: every field that has been more than one thing,
    /// oldest statement first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This block is what replaced <c>acknowledgeCopy</c>.</b> A resumed copy
    /// used to be refused until the caller passed a flag, because taking it over
    /// overwrote the only evidence that it was a copy. Nothing is overwritten:
    /// the <c>directory</c> field still carries the original path beside the new
    /// one, with the instant each was recorded, so the resume can simply
    /// <i>tell</i> the model where the directory has been and let it act on
    /// that. A confirmation flag whose entire content can be returned as fact is
    /// a question that did not need asking.
    /// </para>
    /// <para>
    /// <b>Only fields that moved are printed.</b> An ordinary session has one
    /// statement per field and produces nothing here, so this costs a model
    /// nothing to read until there is something to say.
    /// </para>
    /// <para>
    /// ⚠️ <b>The "at the cap" hedge is gone with the cap (2026-08-26).</b> The
    /// old record kept 32 statements per field and trimmed out of the middle, so
    /// every list of that length had to be printed with <i>statements between
    /// the first and these may have been dropped</i> — a sentence that could not
    /// say whether anything actually had been. Nothing evicts now, so what is
    /// printed is the whole history and there is nothing to hedge.
    /// </para>
    /// </remarks>
    /// <param name="record">The record read from <c>browserai.data</c>.</param>
    /// <returns>The block, or an empty string when nothing has changed.</returns>
    private static string HowItGotHere(SessionRecord record)
    {
        var fields = new List<string>();

        line(fields, RecordFields.Directory, record.DirectoryHistory, static value => $"'{value}'");
        line(fields, RecordFields.Browser, record.BrowserHistory, static value => value);
        line(fields, RecordFields.Purpose, record.PurposeHistory, static value => value);
        line(fields, RecordFields.BrowserAiVersion, record.BrowserAiVersionHistory, static value => value);
        line(
            fields,
            RecordFields.Holder,
            record.HolderHistory,
            static holder => $"PID {holder.ProcessId.ToString(CultureInfo.InvariantCulture)}{(holder.ClientProcessName is { } client ? $" started by {client}" : string.Empty)}");

        return fields.Count is 0
            ? string.Empty
            : "  how this session got here — every field of browserai.data is an ordered list of timestamped statements, and these are the ones that have been more than one thing:\n"
                + string.Join(string.Empty, fields);

        static void line<T>(List<string> into, string name, IReadOnlyList<Statement<T>> statements, Func<T, string> render)
        {
            if (statements.Count < 2)
            {
                return;
            }

            into.Add($"    {name}: "
                + string.Join(" → ", statements.Select(statement => $"{Stamp(statement.At)} {render(statement.Value)}"))
                + "\n");
        }
    }

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// The throwing adapter over <see cref="CanonicalPath"/>, for the tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One adapter at the tool seam, and the function itself never throws.</b>
    /// <see cref="CanonicalPath.Of"/> returns a verdict because it is also the
    /// read path, where a refusal is an entry's <c>Problem</c> rather than an
    /// answer and an exception per malformed index entry would be a construction
    /// cost per session on the machine. Here it is a <see cref="SessionToolException"/>,
    /// which is what every authored tool already answers with.
    /// </para>
    /// <para>
    /// ⚠️ <b>Every door takes this route since 2026-08-26 — previously
    /// <c>init</c> and <c>resume</c> went through a second entry point
    /// (<c>ResolveToOpen</c>) that ran the boundary refusals, and
    /// <c>destroy</c>, <c>set_purpose</c>, <c>catch_up</c> and <c>list</c> did
    /// not.</b> The split existed so that a session created on a share by a
    /// build older than the refusals stayed removable. Nothing was ever
    /// distributed, so that population is empty — and the cost of the split was
    /// real: <c>destroy</c> on a UNC path reached <c>Directory.Exists</c> and
    /// took <b>21 seconds</b> to answer <i>that is not a session</i>, measured
    /// through the wire on 2026-08-26.
    /// </para>
    /// </remarks>
    /// <param name="directory">The path the caller named.</param>
    /// <param name="argument">Which argument it arrived in.</param>
    /// <returns>The canonical session path.</returns>
    /// <exception cref="SessionToolException">It was refused, or it is a volume root.</exception>
    private static SessionPath Resolve(string directory, string argument) => Resolve(directory, argument, out _);

    /// <inheritdoc cref="Resolve(string, string)"/>
    /// <param name="directory">The path the caller named.</param>
    /// <param name="argument">Which argument it arrived in.</param>
    /// <param name="verdict">
    /// What the function made of it, so <c>init</c> and <c>resume</c> can say in
    /// their answer that the spelling moved.
    /// </param>
    private static SessionPath Resolve(string directory, string argument, out PathVerdict verdict)
    {
        verdict = CanonicalPath.Of(directory, PathOrigin.Named, argument);

        if (verdict.Refusal is { } refusal)
        {
            throw new SessionToolException(refusal);
        }

        try
        {
            return SessionPath.For(verdict.Canonical!);
        }
        catch (ArgumentException failure)
        {
            throw new SessionToolException(SessionErrors.DirectoryUnusable(argument, directory, failure.Message));
        }
    }

    /// <summary>
    /// The one line an answer carries when the directory a caller named is not
    /// the one the filesystem calls it, or when that could not be established.
    /// </summary>
    /// <remarks>
    /// <b>Silent normalisation with one note, rather than a refusal or nothing at
    /// all.</b> The refusal this replaced taught something true — <i>BrowserAI
    /// takes only the filesystem's own spelling</i> — and charged a turn for it,
    /// every turn, to a caller whose alias was not its own choice: a
    /// <c>subst</c> in the user's shell, a redirected profile, a junctioned
    /// <c>AppData</c>. The lesson survives here at no turn's cost, and on a
    /// surface that is not scarce: a result is outside the client's truncation
    /// budget entirely, where a tool description is not.
    /// </remarks>
    /// <param name="named">What the caller wrote.</param>
    /// <param name="location">What it resolved to.</param>
    /// <param name="verdict">The verdict, for the unverified case.</param>
    /// <returns>The note, or <see langword="null"/> when there is nothing to say.</returns>
    private static string? SpellingNote(string named, SessionPath location, PathVerdict verdict)
    {
        if (verdict.Unestablished is { } unestablished)
        {
            return $"BrowserAI could not confirm this directory's spelling: {unestablished}.";
        }

        // Case-insensitively, because a case difference is not an alias -- it is
        // one session by design, and the drive letter alone re-spells on every
        // path that came from a shell spelling it lower-case. A note on every
        // session opened from Git Bash would be noise standing exactly where the
        // signal goes.
        return string.Equals(named.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), location.FullPath, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"The directory you named is a second spelling: the session is at '{location.FullPath}', which is what the filesystem calls it. That is the path BrowserAI records and the one to pass back.";
    }

    /// <summary>
    /// The <c>why</c> an authored session-scoped call must carry.
    /// </summary>
    /// <remarks>
    /// <b>Through the catalogue rather than through <see cref="Required"/>.</b>
    /// The generic sentence says a value is missing and that BrowserAI has no
    /// default; that is right for a directory and useless for this one, because
    /// a model that supplies <i>something</i> has satisfied the schema and
    /// recorded nothing. <c>SessionErrors.WhyMissing</c> is the same sentence the
    /// upstream tools' refusal uses, so a caller meets one wording rather than
    /// two.
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="tool">The tool being called, for the refusal.</param>
    /// <returns>What the caller said it was for.</returns>
    private static string Why(JsonObject? arguments, string tool) =>
        Optional(arguments, SessionToolSurface.WhyParameter)
        ?? throw new SessionToolException(SessionErrors.WhyMissing(tool));

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

    /// <summary>
    /// A whole number an argument carried, or <see langword="null"/> when it
    /// carried none.
    /// </summary>
    /// <remarks>
    /// <b>A named refusal for every wrong shape, never a parse that happens to
    /// work.</b> <c>"3"</c> is a string and is refused rather than coerced:
    /// a schema says <c>integer</c>, and a server that quietly accepts the
    /// string form teaches a model that the schema is advisory. A fraction is
    /// refused for the same reason — page 1.5 is not a page.
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="name">The argument.</param>
    /// <returns>The number.</returns>
    private static long? Number(JsonObject? arguments, string name)
    {
        if (arguments?[name] is not { } value || value.GetValueKind() is JsonValueKind.Null)
        {
            return null;
        }

        if (value.GetValueKind() is not JsonValueKind.Number)
        {
            throw new SessionToolException(
                $"'{name}' must be a whole number, and it arrived as {value.GetValueKind()}. Nothing was changed.");
        }

        return value.GetValue<JsonElement>().TryGetInt64(out var number)
            ? number
            : throw new SessionToolException(
                $"'{name}' must be a whole number, and it arrived as '{value.GetValue<JsonElement>().GetRawText()}'. Nothing was changed.");
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

    /// <summary>
    /// The family a call named, normalised to the spelling upstream uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Normalised rather than echoed, because the answer is written to
    /// <c>browserai.data</c> and read back forever.</b> The comparison is
    /// case-insensitive so <c>"Firefox"</c> is accepted, and what is stored is
    /// the canonical member of <see cref="ProvisionedBrowsers.Families"/> — the
    /// same string <c>browsers.json</c> keys on, the config generator writes as
    /// <c>browserName</c>, and the provisioner mutex hashes.
    /// </para>
    /// <para>
    /// ⚠️ <b>The accepted set is a parameter since 2026-08-19, and the two
    /// callers pass different sets on purpose.</b> <c>browserai_init</c> passes
    /// <see cref="ProvisionedBrowsers.Families"/>, because a session's browser is
    /// a thing that renders web pages; <c>browserai_reinstall_browser</c> passes
    /// <see cref="ProvisionedBrowsers.ReinstallTargets"/>, which adds
    /// <c>shared</c>. Before that this method read <c>Families</c> directly, so
    /// widening the reinstall would have widened <c>init</c> in the same edit and
    /// nothing would have failed — a session could then have been opened against
    /// a codec.
    /// </para>
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="name">The parameter to read, which differs between the two callers.</param>
    /// <param name="fallback">What an absent argument means, or <see langword="null"/> to require one.</param>
    /// <param name="accepted">What this caller accepts, which is also what the refusal lists.</param>
    /// <returns>The canonical name, spelled the way this build spells it.</returns>
    private static string Browser(JsonObject? arguments, string name, string? fallback, IReadOnlyList<string> accepted)
    {
        var asked = (fallback is null ? Required(arguments, name) : Optional(arguments, name)) ?? fallback;

        foreach (var value in accepted)
        {
            if (string.Equals(asked, value, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new SessionToolException(
            $"'{asked}' is not something this build provisions. The {accepted.Count.ToString(CultureInfo.InvariantCulture)} accepted values are: "
            + $"{string.Join(", ", accepted)}. Nothing was changed.");
    }

    /// <summary>
    /// The per-run arguments one <c>init</c> or <c>resume</c> call gave.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read the same way on both calls, deliberately.</b> None of these is
    /// written to the record, so there is nothing to read back and nothing a
    /// resume could contradict: a session created at one viewport is resumed at
    /// another without being destroyed first.
    /// </para>
    /// <para>
    /// ⚠️ <b><c>consoleLevel</c> was here until 2026-08-20 and is gone.</b> The
    /// console level is <c>debug</c> always: measured, <c>error</c> to
    /// <c>debug</c> costs +1 character on a navigation response and +5
    /// otherwise, because the events line is a pointer rather than the message
    /// text — and <c>browser_console_messages</c> already takes its own read
    /// level, which can be lowered at the moment of asking where a capture level
    /// chosen at <c>init</c> cannot be raised retroactively.
    /// </para>
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>What this launch was asked for.</returns>
    private static RunOptions Run(JsonObject? arguments) => new()
    {
        Viewport = Viewport(arguments),
        Locale = Optional(arguments, "locale") ?? BrowserConfiguration.HostLocale,
        TimeZone = Optional(arguments, "timezone") ?? BrowserConfiguration.HostTimeZone,
        IgnoreHttpsErrors = Flag(arguments, "ignoreHTTPSErrors") ?? false,
        CaptureNetwork = Flag(arguments, "captureNetwork") ?? false,
    };

    /// <summary>The viewport a call named, or the default.</summary>
    /// <remarks>
    /// <b>Refused rather than clamped.</b> A caller that wrote <c>1920</c> meant
    /// something, and a server that silently substituted 1920×1080 for it would
    /// answer every later question about the page at a size the caller never
    /// chose. The refusal names the form and the bounds.
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The size.</returns>
    private static ViewportSize Viewport(JsonObject? arguments)
    {
        if (Optional(arguments, "viewport") is not { } asked)
        {
            return BrowserConfiguration.DefaultViewport;
        }

        return ViewportSize.TryParse(asked, out var size)
            ? size
            : throw new SessionToolException(
                $"'viewport' = '{asked}' is not a size BrowserAI accepts. Write it as WIDTHxHEIGHT in CSS pixels — '{BrowserConfiguration.DefaultViewport}' is the default — with each side between "
                + $"{ViewportSize.Smallest.ToString(CultureInfo.InvariantCulture)} and {ViewportSize.Largest.ToString(CultureInfo.InvariantCulture)}. "
                + "Nothing was created and nothing was changed. It is refused rather than rounded to the nearest thing that works, because a size you did not choose is one every later screenshot is silently taken at.");
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
    /// <param name="headed">Whether this run of the session opened a window.</param>
    /// <param name="createdHere">Whether this connection created it.</param>
    [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "Session open at {Directory}, headed: {Headed}; created by this connection: {CreatedHere}.")]
    public static partial void Opened(ILogger logger, string directory, bool headed, bool createdHere);

    /// <summary>
    /// The <c>why</c> a caller gave for one session-scoped call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Information rather than Debug, and it goes to the SESSION's own log
    /// rather than to the process log.</b> The audience is whoever opens this
    /// directory next, and a level a caller has to turn on is a level that was
    /// off when the interesting call happened.
    /// </para>
    /// <para>
    /// <b>The text is the caller's, unmodified.</b> It is free text from a model
    /// and it is written into a file another model may read, which is the same
    /// channel <c>purpose</c> is — but it is not replayed into a tool answer, so
    /// the framing that guards <c>purpose</c> is not needed here and would be
    /// noise in a log line.
    /// </para>
    /// </remarks>
    /// <param name="logger">The session's own logger.</param>
    /// <param name="tool">The tool the caller named.</param>
    /// <param name="why">What the caller said it was for.</param>
    [LoggerMessage(EventId = 47, Level = LogLevel.Information, Message = "{Tool}: {Why}")]
    public static partial void Why(ILogger logger, string tool, string why);

    /// <summary>
    /// The <c>why</c> a caller gave for a call against a session that is not
    /// open.
    /// </summary>
    /// <remarks>
    /// <b>It names the directory because it has to.</b> A closed session has no
    /// log of its own open, so this goes to the machine-wide process log, where
    /// a line saying only <i>"browserai_set_purpose: because X"</i> could be
    /// about any session on the machine.
    /// </remarks>
    /// <param name="logger">The process log.</param>
    /// <param name="tool">The tool the caller named.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="why">What the caller said it was for.</param>
    [LoggerMessage(EventId = 48, Level = LogLevel.Information, Message = "{Tool} on the closed session at {Directory}: {Why}")]
    public static partial void WhyForClosedSession(ILogger logger, string tool, string directory, string why);

    /// <summary>A resume found the recorded path gone and repaired the record.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="recorded">The path the record named.</param>
    /// <param name="actual">Where the directory is now.</param>
    [LoggerMessage(
        EventId = 41,
        Level = LogLevel.Warning,
        Message = "Session directory moved: browserai.data recorded '{Recorded}', which no longer exists, and the session was opened at '{Actual}'. The record has been repaired.")]
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

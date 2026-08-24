// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;

namespace BrowserAI.Artifacts;

/// <summary>What one call's <c>filename</c> argument was turned into.</summary>
/// <param name="Tool">The tool that was called.</param>
/// <param name="Prefix">The generator prefix that decided the folder.</param>
/// <param name="AbsolutePath">Where the file goes.</param>
/// <param name="SessionRelativePath">The same place, relative to the session directory.</param>
/// <param name="Asked">The name the caller asked for, or <see langword="null"/> when it named none.</param>
/// <param name="RenamedFrom">The name that was already taken, when one was.</param>
/// <param name="Writes">Whether the child is about to write this file rather than read it.</param>
/// <param name="InlineImageMediaType">
/// The media type of the image block this answer has to regain, or
/// <see langword="null"/> when it has none. Set only where BrowserAI supplied
/// the name — which is the caller-visible condition upstream's own
/// <c>if (!params.filename)</c> tests.
/// </param>
internal sealed record ArtifactPlan(
    string Tool,
    string Prefix,
    string AbsolutePath,
    string SessionRelativePath,
    string? Asked,
    string? RenamedFrom,
    bool Writes,
    string? InlineImageMediaType);

/// <summary>The image block an answer regained, read back off disk.</summary>
/// <param name="Data">The bytes, exactly as the child wrote them.</param>
/// <param name="MediaType">What upstream would have called them.</param>
internal sealed record ArtifactImage(byte[] Data, string MediaType);

/// <summary>What the caller is told about one call's artifacts.</summary>
/// <param name="Note">The text block, naming every path and what happened to it.</param>
/// <param name="Image">
/// The image block to append after it, or <see langword="null"/> — which is
/// every call but a screenshot BrowserAI named.
/// </param>
internal sealed record ArtifactAnswer(string Note, ArtifactImage? Image);

/// <summary>One routed artifact, as <c>session.json</c> records it.</summary>
/// <param name="Tool">What produced it.</param>
/// <param name="At">When.</param>
/// <param name="Url">The page the session was last pointed at, if any.</param>
/// <param name="Bytes">How big it turned out to be.</param>
/// <param name="AbsolutePath">Where it is.</param>
/// <param name="SessionRelativePath">Where it is, relative to the session directory.</param>
/// <param name="RenamedFrom">The name that was taken, when the file had to be suffixed.</param>
/// <param name="SortedAfterTheFact">Whether it was classified after the fact rather than routed inbound.</param>
/// <param name="LeftWhereTheChildPutIt">
/// Whether it was recorded where the child wrote it rather than moved, because
/// the child's own answer had already published its name.
/// <para>
/// ⚠️ <b>The third state, added 2026-08-20, and it exists because the second one
/// was breaking links.</b> Sorting a file the child has told the caller about
/// moves it out from under a pointer the caller is about to follow — see
/// <c>ArtifactRouter.NoteWhatTheAnswerPublished</c>.
/// </para>
/// </param>
internal sealed record ArtifactRecord(
    string Tool,
    DateTimeOffset At,
    string? Url,
    long Bytes,
    string AbsolutePath,
    string SessionRelativePath,
    string? RenamedFrom,
    bool SortedAfterTheFact,
    bool LeftWhereTheChildPutIt = false);

/// <summary>
/// One session's artifacts: where each goes, what happened to it, and what the
/// caller is told about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Route on the way in; do not sort on the way out.</b> BrowserAI sees every
/// <c>tools/call</c> before the child does, so a <c>filename</c> is rewritten
/// into the folder its generator prefix implies and the file is <i>born</i> in
/// the right place. The child's working directory is the output root as well, so
/// a name nothing rewrote still lands inside the session tree by construction
/// rather than by a rule somebody has to keep.
/// </para>
/// <para>
/// <b>What cannot be routed inbound is handled after the fact.</b> A
/// browser-initiated download is named by the site rather than by an argument,
/// so it lands directly in the output root; anything found there is classified
/// by its generator prefix, and a name carrying no prefix is a download, because
/// a download is the one artifact whose name upstream did not choose. That sweep
/// is scoped to the output root and never to the machine.
/// </para>
/// <para>
/// <i>Corrected 2026-08-18 (previously "Two things cannot be routed inbound …
/// and <c>browser_annotate</c> generates its own name with no argument to
/// rewrite").</i> That tool is withheld from the surface and refused if named
/// anyway, so this build cannot produce an <c>annotations-*</c> file at all. The
/// sweep still runs, for downloads and for anything upstream writes loose in a
/// future version, and the <c>annotations</c> folder stays declared because the
/// prefix set is derived from the resolved bundle rather than from what this
/// build calls — see <see cref="ArtifactRouting"/>.
/// </para>
/// <para>
/// <b>Levers two and three ship together.</b> Relocating a file while telling
/// the model it went somewhere else is a new silent failure introduced by the
/// fix for an old one, so every call this class rewrites is a call whose answer
/// gains <see cref="Note"/> — the absolute path, the session-relative path, any
/// rename, and the session's cumulative size.
/// </para>
/// </remarks>
internal sealed class ArtifactRouter
{
    /// <summary>The artifact record, in the session's own directory.</summary>
    /// <summary>
    /// The schema version stamped into <c>session.json</c> and into the roll-up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>§C names these files beside <c>browserai.json</c> under "our own files
    /// reject what they do not recognise", and until 2026-08-17 they carried no
    /// version to reject anything against.</b> <c>browserai.json</c> has had one from
    /// the start because it is <i>read</i> — by the process that takes the
    /// session, by the sweeper deciding ownership, by <c>destroy</c> refusing a
    /// record it does not understand. These two are written and read by nobody
    /// in this build.
    /// </para>
    /// <para>
    /// <b>Which is why the version ships and a parser does not.</b> A strict
    /// reader with no caller is the exact shape this project has already deleted
    /// once — <c>BrowserProvisioner.PruneSupersededRevisions()</c> was public,
    /// had zero callers, and its own documentation claimed one. What a version
    /// buys with no reader is the thing that cannot be added afterwards: a
    /// later BrowserAI meeting a file written by this one can tell what it is
    /// looking at, and a file written by a later build announces itself rather
    /// than being guessed at. Adding the field later would leave every file
    /// written before the change indistinguishable from version 1, forever.
    /// </para>
    /// <para>
    /// ⚠️ <b>2 since 2026-08-20 (previously 1).</b> The roll-up's <c>beneath</c>
    /// entries carried a <c>mode</c> for every session and session modes were
    /// deleted that day. The field is gone rather than written empty, and the
    /// version moved with it — which is the whole of what a version with no
    /// reader is for: a file that has lost a field announces the fact instead of
    /// being read as a version-1 file somebody truncated.
    /// </para>
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    public const string IndexFileName = "session.json";

    /// <summary>The per-root roll-up, beside the sessions it covers.</summary>
    public const string RollUpFileName = "browserai-sessions.json";

    /// <summary>
    /// How many suffixed names are tried before a random one is used.
    /// </summary>
    /// <remarks>
    /// A cap rather than an unbounded search: a directory holding a thousand
    /// <c>login-N.png</c> is a caller doing something the counter is not going to
    /// fix, and walking it on every call would make the routing cost grow with
    /// the session's history.
    /// </remarks>
    public const int SuffixAttempts = 1000;

    private static readonly JsonWriterOptions IndexWriterOptions = new()
    {
        Indented = true,

        // A Windows path is mostly backslashes and this file is read by people
        // and by models. The default encoder additionally escapes characters a
        // path may legitimately carry, which round-trips perfectly and is
        // unreadable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SessionPath _location;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every file name the child has published in an answer of its own, and
    /// therefore every file this router must never move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Monotone across calls, on purpose.</b> A name is added the first time
    /// an answer mentions it: the console log is named in the answer that
    /// <i>creates</i> entries and not in the ones that follow, so a set scoped to
    /// one call would leave the file movable on the very next call — which is the
    /// second half of the defect this exists to close.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24 (previously "and is never removed").*** It was
    /// monotone across <b>files</b> too, and that half was a defect rather than a
    /// property: a name pinned once — correctly, or by the undelimited match this
    /// set used to be fed — pinned every later file that happened to carry the
    /// same name, for the session's life, with no answer naming it. A name is now
    /// dropped at the end of a sweep once nothing loose in the output root
    /// carries it, so nothing is inherited. The cost is stated rather than
    /// hidden: a pinned file deleted between calls and recreated under the same
    /// name before any answer names it again is swept once.
    /// </para>
    /// </remarks>
    private readonly HashSet<string> _published = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which published files have already been written into the index.</summary>
    /// <remarks>
    /// Without it a session that made forty calls would record the same console
    /// log forty times, and the note under every answer would repeat it.
    /// </remarks>
    private readonly HashSet<string> _recorded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ArtifactRecord> _artifacts = [];
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _created = DateTimeOffset.Now;

    private string? _lastUrl;

    /// <summary>Creates the router for one session.</summary>
    /// <param name="location">The canonicalised session directory.</param>
    public ArtifactRouter(SessionPath location)
    {
        ArgumentNullException.ThrowIfNull(location);

        _location = location;
        OutputRoot = Path.Combine(location.FullPath, SessionLayout.OutputFolderName);
    }

    /// <summary>
    /// The child's working directory, and the directory upstream resolves an
    /// un-rewritten relative name against.
    /// </summary>
    public string OutputRoot { get; }

    /// <summary>How many artifacts this session has routed.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _artifacts.Count;
            }
        }
    }

    /// <summary>
    /// Notes anything in a call's arguments that makes a later artifact's name
    /// legible.
    /// </summary>
    /// <remarks>
    /// The <c>url</c> a caller navigated to, and nothing else. Read from the
    /// request rather than from the answer: it costs one dictionary lookup on the
    /// way past, where recovering it from a result means scanning every text
    /// block of every response for a sentence upstream may reword.
    /// </remarks>
    /// <param name="arguments">The call's arguments, as they arrived.</param>
    public void Observe(JsonObject? arguments)
    {
        if ((arguments?["url"] as JsonValue)?.TryGetValue(out string? url) is true && !string.IsNullOrWhiteSpace(url))
        {
            lock (_gate)
            {
                _lastUrl = url;
            }
        }
    }

    /// <summary>Decides where one call's file goes, before the child sees it.</summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="arguments">Its arguments, as they arrived.</param>
    /// <returns>The plan, or <see langword="null"/> when this call names no file.</returns>
    /// <exception cref="SessionToolException">The <c>filename</c> names a place a session may not reach.</exception>
    public ArtifactPlan? Plan(string tool, JsonObject? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (ArtifactTools.For(tool) is not { } rule)
        {
            return null;
        }

        var asked = Asked(tool, arguments);
        var writes = rule.Kind is ArtifactArgument.Written;

        if (asked is null)
        {
            // A tool whose `filename` decides whether the answer is a file or the
            // response body must never be given one: supplying it would change
            // what the call does, which is a louder failure than a timestamped
            // name.
            if (!writes || rule.GeneratedExtension is null)
            {
                return null;
            }
        }

        var prefix = rule.Prefix(arguments);

        var folder = rule.Kind is ArtifactArgument.Opaque
            ? SessionLayout.OutputFolderName
            : ArtifactRouting.FolderFor(prefix);

        string name;
        string? inlineImageMediaType = null;

        if (asked is null)
        {
            // The one expression upstream derives BOTH from: the file type
            // decides the extension it writes and the `image/<type>` it sends,
            // so reading it once here is what keeps the two from disagreeing.
            var extension = rule.GeneratedExtension!(arguments);

            name = Generated(prefix, extension);

            // ⚠️ Only on this branch. `asked is null` IS upstream's
            // `!params.filename`: a caller that named the file was never sent an
            // image, and appending one here would be adding a block to an answer
            // upstream did not put one in.
            inlineImageMediaType = rule.RestoresUpstreamsInlineImage ? "image/" + extension : null;
        }
        else
        {
            name = ArtifactFilename.Relative(tool, asked);
        }

        var target = Path.GetFullPath(Path.Combine(_location.FullPath, folder, name));

        if (!ArtifactFilename.IsInside(_location.FullPath, target))
        {
            throw new SessionToolException(SessionErrors.FilenameEscapesTheSession(tool, asked ?? name));
        }

        string? renamedFrom = null;

        lock (_gate)
        {
            if (writes)
            {
                var unique = Unique(target);

                if (!string.Equals(unique, target, StringComparison.OrdinalIgnoreCase))
                {
                    renamedFrom = Path.GetFileName(target);
                    target = unique;
                }

                _ = _reserved.Add(target);
            }
        }

        // The child writes the file; it does not create the folder above it when
        // the name it was handed is absolute.
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        return new ArtifactPlan(
            tool,
            prefix,
            target,
            Path.GetRelativePath(_location.FullPath, target),
            asked,
            renamedFrom,
            writes,
            inlineImageMediaType);
    }

    /// <summary>Rewrites the arguments the child will actually receive.</summary>
    /// <param name="plan">What <see cref="Plan"/> decided.</param>
    /// <param name="forwarded">The cloned <c>params</c> that is about to be sent.</param>
    public static void Apply(ArtifactPlan plan, JsonObject? forwarded)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (forwarded?["arguments"] is not JsonObject arguments)
        {
            return;
        }

        // Absolute rather than relative, deliberately. Upstream resolves a
        // relative name against the child's cwd, so a relative rewrite would be
        // correct only for as long as the cwd is what this process believes --
        // and the path reported back to the caller has to be the one the file is
        // actually at.
        arguments[ArtifactTools.FilenameArgument] = plan.AbsolutePath;
    }

    /// <summary>
    /// Records what the call produced and returns what the caller is told about
    /// it.
    /// </summary>
    /// <remarks>
    /// <b>The image is read back off disk rather than kept from anywhere.</b>
    /// BrowserAI never holds the bytes: the child took the screenshot and wrote
    /// the file, and what goes back inline is what a reader following the path
    /// in the note would find. That is the only version of "the same image" a
    /// proxy can honestly claim, and it makes the block impossible to produce
    /// for a file that is not there.
    /// </remarks>
    /// <param name="plan">What <see cref="Plan"/> decided, or <see langword="null"/>.</param>
    /// <param name="answer">
    /// The child's own result, serialised, or <see langword="null"/> when there
    /// is none.
    /// <para>
    /// ⚠️ <b>Read BEFORE the sweep and for one reason: a file the child has
    /// named in its own answer must not move.</b> See
    /// <see cref="NoteWhatTheAnswerPublished"/>.
    /// </para>
    /// </param>
    /// <returns>What to append to the answer, or <see langword="null"/> when there is nothing to say.</returns>
    public ArtifactAnswer? Complete(ArtifactPlan? plan, string? answer = null)
    {
        // A call that was refused inside a success envelope -- upstream's
        // `isError: true` -- wrote nothing, and reporting a file that is not
        // there would be the same lie as reporting the wrong path. The name goes
        // back so the caller's retry gets the name it asked for rather than a
        // suffix.
        if (plan is { Writes: true } && !File.Exists(plan.AbsolutePath))
        {
            Release(plan);
            plan = null;
        }

        // ⚠️ BEFORE THE SWEEP, ALWAYS. The names this call published are what
        // the sweep below is forbidden to move, and reading them afterwards
        // would be reading them one call too late -- which is exactly the defect
        // this closes.
        NoteWhatTheAnswerPublished(answer);

        var moved = SweepOutputRoot();

        if (plan is null && moved.Count is 0)
        {
            return null;
        }

        var written = new List<ArtifactRecord>();

        if (plan is { Writes: true })
        {
            written.Add(Record(plan.Tool, plan.AbsolutePath, plan.SessionRelativePath, plan.RenamedFrom, sortedAfterTheFact: false));
        }

        written.AddRange(moved);

        // Read while the file is known to exist and before the note is built, so
        // a read that failed can say so in the same answer.
        var image = ReadInlineImage(plan);

        // ⚠️ The answer, not a discard. Until 2026-08-16 this call site read
        // `WriteIndex();` and the one below it `_ = TryWrite(...)`, so an index
        // that could not be written was silent -- while the note underneath
        // still ended with `index: <path>`, naming a file that was stale or
        // absent. That is this project's founding failure shape produced by the
        // fix for it.
        var indexed = true;

        if (written.Count is not 0)
        {
            lock (_gate)
            {
                _artifacts.AddRange(written);
            }

            indexed = WriteIndex();
        }

        return new ArtifactAnswer(Note(plan, written, indexed, image), image.Bytes);
    }

    /// <summary>
    /// The image an answer regained, and why it did not when it did not.
    /// </summary>
    /// <param name="Bytes">The block to append, or <see langword="null"/>.</param>
    /// <param name="Expected">Whether this call was one that should have carried an image.</param>
    /// <param name="Failure">Why the file could not be read back, when it could not.</param>
    private readonly record struct InlineImage(ArtifactImage? Bytes, bool Expected, string? Failure);

    /// <summary>Reads back the image this call is to answer with.</summary>
    /// <remarks>
    /// <b>A read that fails must not fail the call.</b> The screenshot is on
    /// disk, the note names it, and turning a taken screenshot into a failed one
    /// because a scanner held the file for 40 ms would be worse than the missing
    /// block. It is not silent either: the note says the image is missing and
    /// why, so a model that expected one is told rather than left to conclude
    /// the page was blank.
    /// </remarks>
    private static InlineImage ReadInlineImage(ArtifactPlan? plan)
    {
        if (plan is not { Writes: true, InlineImageMediaType: { } mediaType })
        {
            return new InlineImage(null, Expected: false, Failure: null);
        }

        try
        {
            return new InlineImage(new ArtifactImage(File.ReadAllBytes(plan.AbsolutePath), mediaType), Expected: true, Failure: null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new InlineImage(null, Expected: true, failure.Message);
        }
    }

    /// <summary>
    /// Gives a reserved name back, for a call that has ended.
    /// </summary>
    /// <remarks>
    /// ⚠️ ***Corrected 2026-08-24 (previously "for a call that never reached the
    /// child").*** It is now called for every call that took a plan, from a
    /// <c>finally</c> covering the whole of <c>BrowserProxy.AnswerToolsCallAsync</c>,
    /// because until then the release sites were all on paths that <b>return</b>
    /// — so a cancelled <c>tools/call</c> left its name reserved for the
    /// session's life. <b>Releasing after a successful write is safe</b>:
    /// <c>Taken</c> is <c>_reserved.Contains(candidate) || File.Exists(candidate)</c>,
    /// so a file that is on disk holds its own name, and a file the caller later
    /// deletes stops holding a name it no longer occupies — which is behaviour the
    /// reservation set on its own was never able to give.
    /// </remarks>
    /// <param name="plan">What <see cref="Plan"/> decided, or <see langword="null"/>.</param>
    public void Release(ArtifactPlan? plan)
    {
        if (plan is not { Writes: true })
        {
            return;
        }

        lock (_gate)
        {
            _ = _reserved.Remove(plan.AbsolutePath);
        }
    }

    /// <summary>
    /// Rewrites the roll-up covering the root this session sits under.
    /// </summary>
    /// <remarks>
    /// <b>Scoped by root, never by machine.</b> BrowserAI is registered once and
    /// serves every repository on the host, so an aggregate over everything would
    /// pull unrelated projects' sessions into whatever context happens to be
    /// open. That is a noise problem rather than a security boundary — the paths
    /// were the caller's own choice — but noise in an agent's context is a real
    /// cost, and the cheap fix is to default the aggregate to the root in play.
    /// </remarks>
    /// <param name="root">The directory the sessions sit under.</param>
    /// <param name="sessions">Every session beneath it, already filtered.</param>
    /// <returns>Whether it was written. A caller that names the file must say so when it was not.</returns>
    public static bool WriteRollUp(string root, IReadOnlyList<RollUpEntry> sessions)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sessions);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, IndexWriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteString(
                "_what_this_is",
                "Every BrowserAI session beneath this directory, and nothing outside it. Written by BrowserAI when a session under this root is opened or destroyed. A machine-wide view is available by asking browserai_list about a wider directory.");

            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("root", root);
            writer.WriteString("updated", Stamp(DateTimeOffset.Now));
            writer.WriteNumber("sessions", sessions.Count);

            writer.WriteStartArray("beneath");

            foreach (var session in sessions)
            {
                writer.WriteStartObject();
                writer.WriteString("directory", session.Directory);
                writer.WriteString("purpose", session.Purpose);
                writer.WriteString("created", Stamp(session.Created));
                writer.WriteString("lastUsed", Stamp(session.LastUsed));
                writer.WriteNumber("bytes", session.Bytes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return TryWrite(Path.Combine(root, RollUpFileName), buffer.ToArray());
    }

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    private static string Megabytes(long bytes) =>
        ((double)bytes / (1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

    /// <summary>
    /// Writes a file BrowserAI owns, and never lets failing to do so fail the
    /// call that provoked it.
    /// </summary>
    /// <remarks>
    /// The index is a record of work that already happened. A read-only volume or
    /// a virus scanner holding the file open must not turn a screenshot that was
    /// taken into a screenshot that failed — but it must not be silent either, so
    /// the answer carries what could not be written.
    /// </remarks>
    private static bool TryWrite(string path, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Asked(string tool, JsonObject? arguments)
    {
        if (arguments?[ArtifactTools.FilenameArgument] is not { } value || value.GetValueKind() is JsonValueKind.Null)
        {
            return null;
        }

        if (value.GetValueKind() is not JsonValueKind.String)
        {
            throw new SessionToolException(SessionErrors.FilenameNotUsable(
                tool,
                value.ToJsonString(),
                $"it arrived as {value.GetValueKind()} rather than as a string."));
        }

        return value.GetValue<string>();
    }

    /// <summary>A legible name for a file upstream would have timestamped.</summary>
    /// <remarks>
    /// <c>checkout-step-3.png</c> survives a month and
    /// <c>page-2026-08-14T04-11-50-882Z.png</c> does not, and 346 unnavigable
    /// session directories are what that difference cost. The slug comes from the
    /// last URL the caller navigated to, which is the one fact about the page
    /// BrowserAI can know without asking the child.
    /// </remarks>
    private string Generated(string prefix, string extension)
    {
        string stem;
        int next;

        lock (_gate)
        {
            stem = Slug(_lastUrl) ?? prefix;
            next = _counters.TryGetValue(stem, out var used) ? used + 1 : 1;
            _counters[stem] = next;
        }

        return $"{stem}-{next.ToString(CultureInfo.InvariantCulture)}.{extension}";
    }

    private static string? Slug(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var source = parsed.Host.Length is not 0
            ? parsed.Host + parsed.AbsolutePath
            : parsed.Scheme;

        var slug = new StringBuilder(source.Length);

        foreach (var character in source)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                _ = slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length is not 0 && slug[^1] is not '-')
            {
                _ = slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');

        return text.Length is 0 ? null : text[..Math.Min(text.Length, 48)].TrimEnd('-');
    }

    /// <summary>The first name in that folder nothing has taken.</summary>
    /// <remarks>
    /// <b>Suffix rather than overwrite.</b> Two screenshots called
    /// <c>login.png</c> in one session is data loss wearing a success, and the
    /// reservation set is what makes that true of two calls in flight at once
    /// rather than only of two calls in sequence.
    /// </remarks>
    private string Unique(string target)
    {
        if (!Taken(target))
        {
            return target;
        }

        var folder = Path.GetDirectoryName(target)!;
        var stem = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);

        for (var attempt = 2; attempt <= SuffixAttempts; attempt++)
        {
            var candidate = Path.Combine(folder, $"{stem}-{attempt.ToString(CultureInfo.InvariantCulture)}{extension}");

            if (!Taken(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{stem}-{Guid.NewGuid():N}{extension}");
    }

    private bool Taken(string candidate) => _reserved.Contains(candidate) || File.Exists(candidate);

    /// <summary>
    /// Everything sitting loose in the output root, moved into the folder its
    /// prefix names.
    /// </summary>
    /// <remarks>
    /// <b>This is the old sort, kept for the two cases routing cannot reach</b> —
    /// a download the site named and an annotation upstream named — and applied
    /// to a directory that should be empty, so it costs one non-recursive
    /// enumeration. Classification is by generator prefix and never by date: a
    /// name with no prefix is a download, because a download is the one artifact
    /// whose name upstream did not choose.
    /// </remarks>
    /// <summary>
    /// Marks every loose file whose name the child's own answer mentions, so
    /// that the sweep below leaves it alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS THE FIX FOR TWO REPRODUCED DEFECTS, and they were the same
    /// defect twice.</b> Upstream writes two kinds of file that BrowserAI's
    /// inbound routing cannot reach, because neither comes from a
    /// <c>filename</c> argument: the <b>console log</b> and the <b>snapshot
    /// <c>.yml</c></b>. It then publishes a pointer to each <i>in the answer
    /// itself</i> — a Markdown link to <c>./page-….yml</c>, and
    /// <c>- New console entries: console-….log#L1-L24</c> — and both are
    /// relative to the child's working directory, which is the output root. The
    /// sweep moved both into <c>output\console\</c> and <c>output\page\</c>, so
    /// <b>every one of those pointers named a file that was no longer there</b>.
    /// </para>
    /// <para>
    /// <b>The console half was worse, because the file is still open.</b>
    /// Measured 2026-08-20 against a real Chromium through the published binary:
    /// after the first sweep the child appended again, recreated the log at the
    /// output root, and the next sweep collided with the moved copy and landed it
    /// as <c>-2</c>. The answer then said
    /// <c>console-….log#L25-L28</c> — a file with 24 lines in it — while those
    /// four entries sat in <c>console-….log-2</c> at <i>its</i> lines 1 to 4.
    /// A third call produced <c>-3</c>. <b>Bare upstream does not have this</b>:
    /// nothing there moves the file.
    /// </para>
    /// <para>
    /// <b>The rule is mechanical rather than a list.</b> A list of the two
    /// prefixes would be right today and silently wrong the first time upstream
    /// published a pointer to a third; what is actually true is <i>the child
    /// named this file in an answer</i>, and that is what is tested. The set is
    /// monotone — see <c>_published</c> — because the console log is named only
    /// in the answer that creates entries, and a per-call set would leave it
    /// movable on the very next call.
    /// </para>
    /// <para>
    /// <b>Substring rather than a parse, deliberately.</b> The pointer's shape is
    /// upstream's and changes without notice — a Markdown link today, a bare name
    /// with an <c>#L</c> fragment beside it — and what matters is only whether
    /// the answer names the file.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24 (previously "what matters is only whether the
    /// name appears at all. A generated name carries a millisecond timestamp, so
    /// a false positive would need the answer to contain that exact string for
    /// some other reason").*** The timestamp defence covers <i>generated</i>
    /// names and says nothing about the one artifact class this same file records
    /// upstream as not naming — a browser-initiated download, which carries the
    /// site's own suggested name. A bare <c>Contains</c> pinned
    /// <c>report.pdf</c> on an answer that only ever said
    /// <c>quarterly-report.pdf</c>, and pinned a one-character name on every
    /// answer there is. The match is now delimited; see
    /// <see cref="NamesTheFile"/>.
    /// </para>
    /// </remarks>
    /// <param name="answer">The child's result, serialised, or <see langword="null"/>.</param>
    private void NoteWhatTheAnswerPublished(string? answer)
    {
        if (string.IsNullOrEmpty(answer))
        {
            return;
        }

        IReadOnlyList<string> loose;

        try
        {
            loose = [.. Directory.EnumerateFiles(OutputRoot)];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var file in loose)
            {
                var name = Path.GetFileName(file);

                if (NamesTheFile(answer, name))
                {
                    _ = _published.Add(name);
                }
            }
        }
    }

    /// <summary>
    /// Whether the answer names this file, as opposed to merely spelling it
    /// inside a longer word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Still a substring rather than a parse</b> — the pointer's shape is
    /// upstream's and changes without notice — but a bare <c>Contains</c> pinned
    /// <c>report.pdf</c> on an answer that only ever said
    /// <c>quarterly-report.pdf</c>. What is added is a boundary, and it is the
    /// weakest thing that separates <i>the answer points at this file</i> from
    /// <i>these characters occur</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>A generator prefix is deliberately NOT required.</b> Upstream
    /// publishes a pointer to a browser-initiated download too —
    /// <c>- Downloaded file &lt;name&gt; to "./&lt;name&gt;"</c>, read out of the
    /// resolved bundle 2026-08-24 — and that name is the site's, with no prefix
    /// on it. Requiring one would move the file out from under upstream's own
    /// pointer, which is the defect this whole mechanism exists to close.
    /// </para>
    /// <para>
    /// <b>What this does not fix, stated rather than glossed:</b> a short,
    /// word-shaped name — <c>a</c>, <c>data</c>, <c>index</c> — that a page
    /// renders as ordinary prose is still delimited and still pins. The residue
    /// is bounded to <i>the file stays in <c>output\</c> and is recorded
    /// <c>LeftWhereTheChildPutIt</c> rather than sorted</i>, inside the same
    /// session tree, with the absolute path in the note either way.
    /// </para>
    /// </remarks>
    /// <param name="answer">The child's result, serialised.</param>
    /// <param name="name">A file name, with no directory part.</param>
    /// <returns>Whether the answer names it.</returns>
    private static bool NamesTheFile(string answer, string name)
    {
        for (var at = answer.IndexOf(name, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = answer.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsNameCharacter(answer, at - 1) && !IsNameCharacter(answer, at + name.Length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the character at that index could be part of a file name.</summary>
    /// <remarks>
    /// ⚠️ <b><c>/</c>, <c>\</c>, <c>#</c>, <c>(</c>, <c>)</c> and <c>"</c> are
    /// deliberately absent</b>, because every pointer upstream writes puts one of
    /// them on at least one side: a Markdown link whose target is
    /// <c>./page-….yml</c>, so a <c>/</c> before and a <c>)</c> after;
    /// <c>- New console entries: console-….log#L1-L24</c>;
    /// <c>- Downloaded file x.pdf to "./x.pdf"</c> — and the answer this reads is
    /// the SERIALISED result, so a quote arrives as <c>\"</c> and a separator as
    /// <c>\\</c>.
    /// </remarks>
    /// <param name="answer">The child's result, serialised.</param>
    /// <param name="index">Where to look; out of range is not a name character.</param>
    /// <returns>Whether that character could be part of a file name.</returns>
    private static bool IsNameCharacter(string answer, int index) =>
        index >= 0
        && index < answer.Length
        && (char.IsLetterOrDigit(answer[index]) || answer[index] is '-' or '_' or '.');

    private List<ArtifactRecord> SweepOutputRoot()
    {
        var moved = new List<ArtifactRecord>();

        IReadOnlyList<string> loose;

        try
        {
            loose = [.. Directory.EnumerateFiles(OutputRoot)];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return moved;
        }

        foreach (var file in loose)
        {
            var name = Path.GetFileName(file);

            bool published;
            bool alreadyRecorded;

            lock (_gate)
            {
                published = _published.Contains(name);
                alreadyRecorded = published && !_recorded.Add(name);
            }

            if (published)
            {
                // ⚠️ RECORDED WHERE IT IS, NOT MOVED. The child's own answer
                // points at this path; moving the file would leave that pointer
                // naming nothing, which is worse than no pointer at all. It is
                // still recorded, because "not moved" must not mean "not
                // mentioned" -- the caller gets the absolute path and the index
                // gets an entry, once.
                if (!alreadyRecorded)
                {
                    moved.Add(Record(
                        $"named by the child in its own answer: {name}",
                        file,
                        Path.GetRelativePath(_location.FullPath, file),
                        renamedFrom: null,
                        sortedAfterTheFact: false,
                        leftWhereTheChildPutIt: true));
                }

                continue;
            }

            var prefix = ArtifactRouting.PrefixOf(name) ?? ArtifactRouting.DownloadPrefix;
            var folder = Path.Combine(_location.FullPath, ArtifactRouting.FolderFor(prefix));

            string target;

            lock (_gate)
            {
                target = Unique(Path.Combine(folder, name));
                _ = _reserved.Add(target);
            }

            try
            {
                _ = Directory.CreateDirectory(folder);
                File.Move(file, target);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Still being written, most likely. It stays where it is and the
                // next call sweeps it.
                continue;
            }

            moved.Add(Record(
                $"sorted from {SessionLayout.OutputFolderName}\\{name}",
                target,
                Path.GetRelativePath(_location.FullPath, target),
                string.Equals(Path.GetFileName(target), name, StringComparison.OrdinalIgnoreCase) ? null : name,
                sortedAfterTheFact: true));
        }

        // ⚠️ THE SET IS STILL MONOTONE ACROSS CALLS, WHICH IS THE PROPERTY THE
        // POINTER FIX NEEDED -- the console log is named only in the answer that
        // creates entries, so a per-call set would leave it movable on the very
        // next call. What it is no longer is monotone across FILES: a name is
        // dropped once nothing loose in the output root carries it, so a later,
        // unrelated file that happens to be called the same thing is not pinned
        // by inheritance from an answer that named something else.
        //
        // `loose` is the enumeration taken BEFORE anything moved, so a file this
        // very sweep sorted is dropped from `_recorded` -- which is correct: it
        // is no longer in the output root and its index entry is already written.
        lock (_gate)
        {
            var present = loose.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            _ = _published.RemoveWhere(name => !present.Contains(name));
            _ = _recorded.RemoveWhere(name => !present.Contains(name));
        }

        return moved;
    }

    private ArtifactRecord Record(
        string tool,
        string absolute,
        string relative,
        string? renamedFrom,
        bool sortedAfterTheFact,
        bool leftWhereTheChildPutIt = false)
    {
        long bytes;

        try
        {
            bytes = File.Exists(absolute) ? new FileInfo(absolute).Length : 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            bytes = 0;
        }

        string? url;

        lock (_gate)
        {
            url = _lastUrl;
        }

        return new ArtifactRecord(tool, DateTimeOffset.Now, url, bytes, absolute, relative, renamedFrom, sortedAfterTheFact, leftWhereTheChildPutIt);
    }

    private string Note(ArtifactPlan? plan, IReadOnlyList<ArtifactRecord> written, bool indexed, InlineImage image)
    {
        var note = new StringBuilder();

        if (plan is { Writes: false })
        {
            _ = note.Append("BrowserAI resolved '").Append(plan.Asked).Append("' to ").Append(plan.AbsolutePath)
                .Append(" (session-relative ").Append(plan.SessionRelativePath).Append(").\n");
        }

        foreach (var artifact in written)
        {
            _ = note
                .Append(artifact switch
                {
                    { LeftWhereTheChildPutIt: true } =>
                        "BrowserAI LEFT this artifact where the browser wrote it, because the answer above links to it there. Do not expect it under a typed folder.\n",
                    { SortedAfterTheFact: true } => "BrowserAI sorted an artifact it could not route on the way in.\n",
                    _ => "BrowserAI routed this artifact.\n",
                })
                .Append("  file: ").Append(artifact.AbsolutePath).Append('\n')
                .Append("  session-relative: ").Append(artifact.SessionRelativePath).Append('\n')
                .Append("  size: ").Append(Megabytes(artifact.Bytes)).Append('\n');

            if (artifact.RenamedFrom is { } taken)
            {
                _ = note.Append("  renamed: '").Append(taken)
                    .Append("' already existed in that folder and was NOT overwritten; this one is '")
                    .Append(Path.GetFileName(artifact.AbsolutePath)).Append("'.\n");
            }
        }

        var total = SessionLayout.SizeOnDisk(_location.FullPath);

        lock (_gate)
        {
            _ = note.Append("  session total: ").Append(Megabytes(total))
                .Append(" across ").Append(_artifacts.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" routed artifact(s) in ").Append(_location.FullPath).Append('\n');
        }

        _ = note.Append("  index: ").Append(Path.Combine(_location.FullPath, IndexFileName));

        // The one thing a caller must not be told is that a file it is being
        // handed the path of was written when it was not. The artifact itself is
        // on disk either way -- that is why this is a line in the note and not
        // an error -- but a reader following the index path would otherwise meet
        // a stale file or none, with nothing having said so.
        _ = note.Append(indexed
            ? "\n"
            : " -- ⚠️ COULD NOT BE WRITTEN on this call (the volume is read-only, or something holds it open), so it does NOT list the artifact(s) above. Everything named above is on disk at the path given; only the index is behind.\n");

        // Said out loud, because the alternative is a model concluding something
        // about the page from a block that is missing for a reason that has
        // nothing to do with it.
        if (image is { Expected: true, Bytes: null })
        {
            _ = note.Append("  ⚠️ the image block that normally follows this note is MISSING on this call: the file above could not be read back (")
                .Append(image.Failure)
                .Append("). The screenshot itself is on disk at the path given -- read it there, and do not treat the absent image as anything about the page.\n");
        }

        return note.ToString();
    }

    /// <summary>Rewrites <c>session.json</c> from what this router knows.</summary>
    /// <remarks>
    /// Browser and <c>purpose</c> stay <c>browserai.json</c>'s to own. A second
    /// copy of the session's identity is a second thing to disagree with the
    /// first; this file is the artifact record and nothing else.
    /// </remarks>
    /// <returns>Whether it was written.</returns>
    private bool WriteIndex()
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, IndexWriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteString(
                "_what_this_is",
                "What is inside this session: one entry per artifact BrowserAI routed, with the tool that produced it and both path forms. The session's identity -- mode, browser, purpose -- is browserai.json's and is deliberately not duplicated here.");

            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("session", _location.FullPath);
            writer.WriteString("created", Stamp(_created));
            writer.WriteString("lastTouched", Stamp(DateTimeOffset.Now));

            writer.WriteStartObject("folders");

            foreach (var folder in ArtifactRouting.Folders)
            {
                writer.WriteString(folder.Replace('\\', '/'), Path.Combine(_location.FullPath, folder));
            }

            writer.WriteEndObject();

            writer.WriteStartArray("artifacts");

            lock (_gate)
            {
                foreach (var artifact in _artifacts)
                {
                    writer.WriteStartObject();
                    writer.WriteString("tool", artifact.Tool);
                    writer.WriteString("at", Stamp(artifact.At));

                    if (artifact.Url is { } url)
                    {
                        writer.WriteString("url", url);
                    }

                    writer.WriteNumber("bytes", artifact.Bytes);
                    writer.WriteString("path", artifact.AbsolutePath);
                    writer.WriteString("sessionRelative", artifact.SessionRelativePath);

                    if (artifact.RenamedFrom is { } renamedFrom)
                    {
                        writer.WriteString("renamedFrom", renamedFrom);
                    }

                    if (artifact.SortedAfterTheFact)
                    {
                        writer.WriteBoolean("sortedAfterTheFact", value: true);
                    }

                    if (artifact.LeftWhereTheChildPutIt)
                    {
                        writer.WriteBoolean("leftWhereTheChildPutIt", value: true);
                    }

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return TryWrite(Path.Combine(_location.FullPath, IndexFileName), buffer.ToArray());
    }
}

/// <summary>One session, as the per-root roll-up lists it.</summary>
/// <param name="Directory">The session directory.</param>
/// <param name="Purpose">What it says it is for.</param>
/// <param name="Created">When it was created.</param>
/// <param name="LastUsed">When it was last used.</param>
/// <param name="Bytes">Its size on disk.</param>
internal sealed record RollUpEntry(
    string Directory,
    string Purpose,
    DateTimeOffset Created,
    DateTimeOffset LastUsed,
    long Bytes);

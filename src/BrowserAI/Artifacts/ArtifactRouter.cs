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
internal sealed record ArtifactPlan(
    string Tool,
    string Prefix,
    string AbsolutePath,
    string SessionRelativePath,
    string? Asked,
    string? RenamedFrom,
    bool Writes);

/// <summary>One routed artifact, as <c>session.json</c> records it.</summary>
/// <param name="Tool">What produced it.</param>
/// <param name="At">When.</param>
/// <param name="Url">The page the session was last pointed at, if any.</param>
/// <param name="Bytes">How big it turned out to be.</param>
/// <param name="AbsolutePath">Where it is.</param>
/// <param name="SessionRelativePath">Where it is, relative to the session directory.</param>
/// <param name="RenamedFrom">The name that was taken, when the file had to be suffixed.</param>
/// <param name="SortedAfterTheFact">Whether it was classified after the fact rather than routed inbound.</param>
internal sealed record ArtifactRecord(
    string Tool,
    DateTimeOffset At,
    string? Url,
    long Bytes,
    string AbsolutePath,
    string SessionRelativePath,
    string? RenamedFrom,
    bool SortedAfterTheFact);

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
/// <b>Two things cannot be routed inbound, and both are handled after the
/// fact.</b> A browser-initiated download is named by the site rather than by an
/// argument, and <c>browser_annotate</c> generates its own name with no argument
/// to rewrite. Both land directly in the output root, so anything found there is
/// classified by its generator prefix — and a name carrying no prefix is a
/// download, because a download is the one artifact whose name upstream did not
/// choose. That sweep is scoped to the output root and never to the machine.
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

        var name = asked is null
            ? Generated(prefix, rule.GeneratedExtension!(arguments))
            : ArtifactFilename.Relative(tool, asked);

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
            writes);
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
    /// <param name="plan">What <see cref="Plan"/> decided, or <see langword="null"/>.</param>
    /// <returns>The note to append to the answer, or <see langword="null"/> when there is nothing to say.</returns>
    public string? Complete(ArtifactPlan? plan)
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

        if (written.Count is not 0)
        {
            lock (_gate)
            {
                _artifacts.AddRange(written);
            }

            WriteIndex();
        }

        return Note(plan, written);
    }

    /// <summary>
    /// Gives a reserved name back, for a call that never reached the child.
    /// </summary>
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
    public static void WriteRollUp(string root, IReadOnlyList<RollUpEntry> sessions)
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

            writer.WriteString("root", root);
            writer.WriteString("updated", Stamp(DateTimeOffset.Now));
            writer.WriteNumber("sessions", sessions.Count);

            writer.WriteStartArray("beneath");

            foreach (var session in sessions)
            {
                writer.WriteStartObject();
                writer.WriteString("directory", session.Directory);
                writer.WriteString("mode", session.Mode);
                writer.WriteString("purpose", session.Purpose);
                writer.WriteString("created", Stamp(session.Created));
                writer.WriteString("lastUsed", Stamp(session.LastUsed));
                writer.WriteNumber("bytes", session.Bytes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        TryWrite(Path.Combine(root, RollUpFileName), buffer.ToArray());
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

        return moved;
    }

    private ArtifactRecord Record(string tool, string absolute, string relative, string? renamedFrom, bool sortedAfterTheFact)
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

        return new ArtifactRecord(tool, DateTimeOffset.Now, url, bytes, absolute, relative, renamedFrom, sortedAfterTheFact);
    }

    private string Note(ArtifactPlan? plan, IReadOnlyList<ArtifactRecord> written)
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
                .Append(artifact.SortedAfterTheFact
                    ? "BrowserAI sorted an artifact it could not route on the way in.\n"
                    : "BrowserAI routed this artifact.\n")
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

        _ = note.Append("  index: ").Append(Path.Combine(_location.FullPath, IndexFileName)).Append('\n');

        return note.ToString();
    }

    /// <summary>Rewrites <c>session.json</c> from what this router knows.</summary>
    /// <remarks>
    /// Mode, browser and <c>purpose</c> stay <c>lock.json</c>'s to own. A second
    /// copy of the session's identity is a second thing to disagree with the
    /// first; this file is the artifact record and nothing else.
    /// </remarks>
    private void WriteIndex()
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, IndexWriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteString(
                "_what_this_is",
                "What is inside this session: one entry per artifact BrowserAI routed, with the tool that produced it and both path forms. The session's identity -- mode, browser, purpose -- is lock.json's and is deliberately not duplicated here.");

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

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _ = TryWrite(Path.Combine(_location.FullPath, IndexFileName), buffer.ToArray());
    }
}

/// <summary>One session, as the per-root roll-up lists it.</summary>
/// <param name="Directory">The session directory.</param>
/// <param name="Mode">The mode bound at <c>init</c>.</param>
/// <param name="Purpose">What it says it is for.</param>
/// <param name="Created">When it was created.</param>
/// <param name="LastUsed">When it was last used.</param>
/// <param name="Bytes">Its size on disk.</param>
internal sealed record RollUpEntry(
    string Directory,
    string Mode,
    string Purpose,
    DateTimeOffset Created,
    DateTimeOffset LastUsed,
    long Bytes);

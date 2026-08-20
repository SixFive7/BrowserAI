// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BrowserAI.Sessions;

/// <summary>
/// One timestamped statement about one field of a session's record: what it was
/// said to be, and when it was said.
/// </summary>
/// <typeparam name="T">What the statement is about.</typeparam>
/// <param name="At">When the statement was made.</param>
/// <param name="Value">What was stated.</param>
internal sealed record Statement<T>(DateTimeOffset At, T Value);

/// <summary>
/// The contents of <c>browserai.json</c>: who owns this session directory, what it is
/// for, what wrote the file — and, since schema 2, <b>how it got that way</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every field is an ordered list of timestamped statements, oldest first.</b>
/// The record is append-only rather than a snapshot, so a session can say how it
/// got here and not only where it is. A directory that reads
/// <c>[{at: t0, value: "C:\work\gamma"}, {at: t1, value: "C:\work\gamma-copy"}]</c>
/// has told the reader, without being asked and without a flag, that it is a copy
/// and what it was copied from — which is the whole reason
/// <c>browserai_resume</c> no longer has to refuse one.
/// </para>
/// <para>
/// <b><see cref="Created"/> and <see cref="LastUsed"/> are derived and no longer
/// stored.</b> With every statement carrying its own timestamp they are exactly
/// the earliest and the latest of them, and a stored copy could only disagree
/// with the statements it summarises. <see cref="LastUsed"/> therefore means
/// <i>when anything about this session last changed</i>, which in practice is
/// when it was last opened: an acquisition by a different process always appends
/// a holder statement, because <c>(pid, creationFileTime)</c> is never the same
/// twice.
/// </para>
/// <para>
/// <b>A statement is appended only when the value changes</b>, so the four fields
/// that describe what the session <i>is</i> — mode, browser, directory, build —
/// stay one statement long for the life of a session that is not moved, copied or
/// run under a new build. Growth comes from <see cref="PurposeHistory"/> and
/// <see cref="HolderHistory"/>, and is capped: see
/// <see cref="MaximumStatementsPerField"/>.
/// </para>
/// <para>
/// <b>Schema 2 does not read schema 1, and there is no converter.</b> A version-1
/// file is refused with a message naming the fix. That is a deliberate choice
/// rather than an omission — nothing but this machine's own alpha has ever
/// written one, and a converter for a format with no installed base is code that
/// can only ever be wrong in private.
/// </para>
/// <para>
/// <b>The holder record persists after death on purpose.</b> It is what lets a
/// second BrowserAI say <i>"held by PID 1234 since 14:02, no longer running —
/// reclaiming"</i> instead of a bare refusal, and <c>(pid, creationFileTime)</c>
/// together are what stop that sentence naming a stranger after a pid is reused.
/// </para>
/// <para>
/// <b>Every timestamp is ISO 8601, invariant, with an explicit offset</b> — a
/// <see cref="DateTimeOffset"/> round-tripped through the <c>"O"</c> specifier,
/// never <c>DateTime.ToString()</c> against the current culture and never a bare
/// local time. A file written on a machine with a non-invariant culture and read
/// on another otherwise produces either a parse error or, worse, a date that
/// parses to the wrong day.
/// </para>
/// </remarks>
internal sealed record LockRecord
{
    /// <summary>The schema this build writes and the only one it reads.</summary>
    /// <remarks>
    /// ⚠️ <b>2 since 2026-08-18 (previously 1, one value per field with a
    /// history on <c>purpose</c> alone).</b> Bumping it is what makes an old file
    /// a refusal with a recovery rather than a record read as something it is
    /// not.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// The longest <c>purpose</c> that is stored. Free text written by one agent
    /// and replayed into another's context is a channel between agents; the cap
    /// and the control-character strip are what keep it data.
    /// </summary>
    public const int PurposeMaximumLength = 2000;

    /// <summary>
    /// How many statements one field keeps. <b>This is the answer to "does an
    /// append-only file grow without bound".</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It does not, and the bound is stated in bytes rather than in
    /// statements.</b> <c>purpose</c> is the only field whose values are large,
    /// capped at <see cref="PurposeMaximumLength"/> characters each, so the
    /// worst case is 32 × 2,000 ≈ 64 KB of purpose plus a few KB of everything
    /// else. The file is written <c>WriteThrough</c> and flushed on every
    /// acquisition; ~70 KB of that is a handful of sectors and is dwarfed by the
    /// rename that follows it. <b>Schema 1's <c>purposeHistory</c> had no cap at
    /// all</b>, so any bound here is strictly an improvement on what shipped.
    /// </para>
    /// <para>
    /// <b>The oldest statement is never dropped; the second-oldest is.</b> A
    /// history exists to say where a session started as much as where it is now,
    /// and <see cref="Created"/> is read from that first statement — a policy
    /// that trimmed the front would silently move a session's creation date,
    /// which is the class of wrong answer this repository exists to remove. So
    /// the elision is in the <b>middle</b>, and it is visible rather than
    /// assumed: <c>browserai_init</c> and <c>browserai_resume</c> say that a
    /// field at the cap has had statements between its first and its most recent
    /// dropped.
    /// </para>
    /// <para>
    /// <b>Thirty-two, and it must never be below two</b> — a cap of one would
    /// make the trim keep the oldest statement and discard the one just written.
    /// <c>LockRecordTests</c> asserts the floor.
    /// </para>
    /// </remarks>
    public const int MaximumStatementsPerField = 32;

    /// <summary>Which version of this file's schema wrote it.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Every absolute path this record has been written at, oldest first.
    /// </summary>
    /// <remarks>
    /// Provenance rather than identity. Identity is where the caller is pointing
    /// right now; this answers the narrower question of where the session has
    /// been, and comparing its last entry with the caller's path is what tells a
    /// resume whether the directory was moved or copied. <b>Since schema 2 the
    /// whole list is handed back to the model</b>, which is what makes a copy
    /// self-describing instead of a thing to be acknowledged.
    /// </remarks>
    public required IReadOnlyList<Statement<string>> DirectoryHistory { get; init; }

    /// <summary>Every mode this session has been. A session cannot change what it is, so in practice: one.</summary>
    public required IReadOnlyList<Statement<string>> ModeHistory { get; init; }

    /// <summary>Every browser family this profile has belonged to. In practice: one.</summary>
    public required IReadOnlyList<Statement<string>> BrowserHistory { get; init; }

    /// <summary>Everything the session has been for, oldest first.</summary>
    public required IReadOnlyList<Statement<string>> PurposeHistory { get; init; }

    /// <summary>Every BrowserAI build that has written this record.</summary>
    public required IReadOnlyList<Statement<string>> BrowserAiVersionHistory { get; init; }

    /// <summary>Everyone who has held, or last held, the lock.</summary>
    public required IReadOnlyList<Statement<LockHolder>> HolderHistory { get; init; }

    /// <summary>The resolved absolute path as of the newest statement.</summary>
    public string Directory => DirectoryHistory[^1].Value;

    /// <summary>The session's mode.</summary>
    public string Mode => ModeHistory[^1].Value;

    /// <summary>The browser family this profile belongs to.</summary>
    public string Browser => BrowserHistory[^1].Value;

    /// <summary>What the session is for, as the agent that last set it said.</summary>
    public string Purpose => PurposeHistory[^1].Value;

    /// <summary>The BrowserAI build that last wrote this record.</summary>
    public string BrowserAiVersion => BrowserAiVersionHistory[^1].Value;

    /// <summary>Who holds, or last held, the lock.</summary>
    public LockHolder Holder => HolderHistory[^1].Value;

    /// <summary>When the current holder took it.</summary>
    /// <remarks>
    /// Distinct from <see cref="LastUsed"/>, which a later <c>set_purpose</c>
    /// moves past this. A refusal that says <i>"took the lock at"</i> must quote
    /// this one.
    /// </remarks>
    public DateTimeOffset TakenAt => HolderHistory[^1].At;

    /// <summary>
    /// When the session directory was first locked: the earliest statement in the
    /// record.
    /// </summary>
    /// <remarks>
    /// Read from the <i>first</i> statement of every list rather than from one of
    /// them, so that adding a field later cannot quietly change what this means.
    /// The trim never removes a first statement, so this value is stable for the
    /// life of the directory.
    /// </remarks>
    public DateTimeOffset Created => Bound(first: true);

    /// <summary>When anything about this session last changed.</summary>
    public DateTimeOffset LastUsed => Bound(first: false);

    /// <summary>
    /// Value equality, <b>including</b> every statement list element by element.
    /// </summary>
    /// <param name="other">The record to compare against.</param>
    /// <returns>Whether the two describe the same session state.</returns>
    /// <remarks>
    /// The compiler-generated version compares the lists by reference, so two
    /// records parsed from identical bytes reported themselves unequal. That is
    /// the wrong answer in the direction that matters: a purpose-change check
    /// built on it would rewrite the file on every call and never say why.
    /// </remarks>
    public bool Equals(LockRecord? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && DirectoryHistory.SequenceEqual(other.DirectoryHistory)
        && ModeHistory.SequenceEqual(other.ModeHistory)
        && BrowserHistory.SequenceEqual(other.BrowserHistory)
        && PurposeHistory.SequenceEqual(other.PurposeHistory)
        && BrowserAiVersionHistory.SequenceEqual(other.BrowserAiVersionHistory)
        && HolderHistory.SequenceEqual(other.HolderHistory);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(SchemaVersion, Directory, Mode, Browser, Purpose, Created, LastUsed, Holder);

    /// <summary>
    /// Appends a statement, <b>only if it says something new</b>, and keeps the
    /// list inside <see cref="MaximumStatementsPerField"/>.
    /// </summary>
    /// <typeparam name="T">What the statement is about.</typeparam>
    /// <param name="previous">The statements already recorded, or <see langword="null"/> for a new record.</param>
    /// <param name="value">What is being stated now.</param>
    /// <param name="at">When.</param>
    /// <returns>The new list, or <paramref name="previous"/> unchanged when the value has not moved.</returns>
    /// <remarks>
    /// <b>Returning <paramref name="previous"/> by reference on a no-op is the
    /// point, not an optimisation.</b> A history that gained an entry every time
    /// the record was rewritten would record that a file was written, which
    /// nobody asked and which would push the interesting statements out under the
    /// cap. What is worth keeping is a <i>change</i>.
    /// </remarks>
    public static IReadOnlyList<Statement<T>> Append<T>(IReadOnlyList<Statement<T>>? previous, T value, DateTimeOffset at)
    {
        if (previous is { Count: > 0 } && EqualityComparer<T>.Default.Equals(previous[^1].Value, value))
        {
            return previous;
        }

        var next = new List<Statement<T>>(previous ?? []) { new(at, value) };

        // From index 1: the first statement is where the session started and is
        // never dropped, because Created is read from it.
        while (next.Count > MaximumStatementsPerField)
        {
            next.RemoveAt(1);
        }

        return next;
    }

    /// <summary>Whether a field has dropped statements out of its middle.</summary>
    /// <param name="statements">The field.</param>
    /// <returns>Whether the list is at its cap.</returns>
    /// <remarks>
    /// At the cap is the only state in which a trim can have happened, and the
    /// record cannot tell whether one <i>did</i> — so the sentence this feeds
    /// says "may have", which is the true statement.
    /// </remarks>
    public static bool IsAtTheCap<T>(IReadOnlyList<Statement<T>> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        return statements.Count >= MaximumStatementsPerField;
    }

    /// <summary>
    /// Caps and de-controls a purpose before it is stored.
    /// </summary>
    /// <param name="purpose">Free text from the calling model.</param>
    /// <returns>The text as it will be stored and replayed.</returns>
    public static string SanitisePurpose(string purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);

        // Control characters become spaces rather than being dropped: dropping
        // them silently joins two lines into one word, which changes what the
        // text says. JSON would escape them safely -- this is about what a
        // future agent reads, not about the encoding.
        var flattened = string.Create(
            Math.Min(purpose.Length, PurposeMaximumLength),
            purpose,
            static (destination, source) =>
            {
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = char.IsControl(source[i]) ? ' ' : source[i];
                }
            });

        return flattened.Trim();
    }

    /// <summary>Serialises the record exactly as it is written to disk.</summary>
    /// <returns>UTF-8 bytes, LF-separated, no BOM.</returns>
    /// <remarks>
    /// <b>The relaxed encoder is not decoration.</b> <see cref="Utf8JsonWriter"/>
    /// defaults to an encoder that escapes <c>+</c>, so every ISO 8601 timestamp
    /// with a positive UTC offset was being written with its offset sign as a
    /// <c>+</c> escape. That round-trips perfectly and is unreadable by the
    /// person this file exists for, and it would have shipped unnoticed — it was
    /// caught by an assertion on the literal bytes rather than by one on the
    /// parsed value, which is the only kind of assertion that could catch it.
    /// </remarks>
    public byte[] ToUtf8()
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(LockJson.SchemaVersion, SchemaVersion);

            WriteStatements(writer, LockJson.Directory, DirectoryHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.Mode, ModeHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.Browser, BrowserHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.Purpose, PurposeHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.BrowserAiVersion, BrowserAiVersionHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.Holder, HolderHistory, static (w, holder) => WriteHolder(w, holder));

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Parses a record, refusing anything it does not recognise.
    /// </summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="path">The file, named in any failure.</param>
    /// <returns>The parsed record.</returns>
    /// <exception cref="LockFileException">
    /// The file carries an unknown key, is missing a required one, holds an empty
    /// statement list, carries a timestamp that is not round-trippable ISO 8601,
    /// was written by a different schema version, or is not JSON at all.
    /// </exception>
    public static LockRecord Read(ReadOnlySpan<byte> utf8, string path)
    {
        int? schemaVersion = null;
        List<Statement<string>>? directory = null;
        List<Statement<string>>? mode = null;
        List<Statement<string>>? browser = null;
        List<Statement<string>>? purpose = null;
        List<Statement<string>>? version = null;
        List<Statement<LockHolder>>? holder = null;

        // ⚠️ THE VERSION IS CHECKED BEFORE ANYTHING ELSE IS PARSED, in a pass of
        // its own. A schema-1 file is well-formed JSON whose top-level keys this
        // build still recognises by name -- `directory` is there, it is just a
        // string where an array of statements now goes. Checked afterwards, as it
        // was, the reader would refuse it with "'directory' where an array of
        // timestamped statements was expected", which reads as damage and sends
        // the caller to repair a file that is not broken. The version is the
        // reason, so the version is the message.
        if (SchemaVersionOf(utf8) is { } declared && declared != CurrentSchemaVersion)
        {
            throw new LockFileException(WrongSchema(declared, path));
        }

        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });

        try
        {
            ExpectStartObject(ref reader, path);

            while (ReadPropertyName(ref reader, path) is { } name)
            {
                switch (name)
                {
                    case LockJson.SchemaVersion:
                        schemaVersion = ReadInt32(ref reader, name, path);
                        break;
                    case LockJson.Directory:
                        directory = ReadStatements<string>(ref reader, name, path, ReadString);
                        break;
                    case LockJson.Mode:
                        mode = ReadStatements<string>(ref reader, name, path, ReadString);
                        break;
                    case LockJson.Browser:
                        browser = ReadStatements<string>(ref reader, name, path, ReadString);
                        break;
                    case LockJson.Purpose:
                        purpose = ReadStatements<string>(ref reader, name, path, ReadString);
                        break;
                    case LockJson.BrowserAiVersion:
                        version = ReadStatements<string>(ref reader, name, path, ReadString);
                        break;
                    case LockJson.Holder:
                        holder = ReadStatements<LockHolder>(ref reader, name, path, ReadHolder);
                        break;
                    default:
                        throw Unrecognised(name, path);
                }
            }
        }
        catch (JsonException failure)
        {
            throw new LockFileException(
                $"'{path}' is not valid JSON ({failure.Message}) BrowserAI will not guess at a session record it cannot read. " +
                RecoveryAdvice(path),
                failure);
        }

        if (schemaVersion is not CurrentSchemaVersion)
        {
            throw new LockFileException(WrongSchema(schemaVersion, path));
        }

        return new LockRecord
        {
            SchemaVersion = schemaVersion.Value,
            DirectoryHistory = directory ?? throw Missing(LockJson.Directory, path),
            ModeHistory = mode ?? throw Missing(LockJson.Mode, path),
            BrowserHistory = browser ?? throw Missing(LockJson.Browser, path),
            PurposeHistory = purpose ?? throw Missing(LockJson.Purpose, path),
            BrowserAiVersionHistory = version ?? throw Missing(LockJson.BrowserAiVersion, path),
            HolderHistory = holder ?? throw Missing(LockJson.Holder, path),
        };
    }

    /// <summary>
    /// The sentence every refusal ends with. Naming a recovery that <i>is</i> the
    /// call that just failed is the same as naming none.
    /// </summary>
    private const string RepeatingFails = "Repeating the call that just failed will fail identically.";

    /// <summary>Reads one statement's value out of the reader.</summary>
    /// <typeparam name="T">What the statement is about.</typeparam>
    /// <param name="reader">The reader, positioned on the value's key.</param>
    /// <param name="name">The field, for a failure message.</param>
    /// <param name="path">The file, for a failure message.</param>
    /// <returns>The value.</returns>
    private delegate T StatementValueReader<out T>(ref Utf8JsonReader reader, string name, string path);

    /// <summary>
    /// The declared schema version, read on its own before the strict parse.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately lenient, and it decides nothing on its own.</b> It answers
    /// <see langword="null"/> for anything it cannot read — malformed JSON, a
    /// missing key, a version that is not a number — and every one of those is
    /// then refused with its proper message by the strict parse below. The only
    /// thing this pass may do is turn <i>a version this build does not read</i>
    /// into that sentence rather than into a complaint about the first field
    /// whose shape changed with it.
    /// </remarks>
    /// <param name="utf8">The file's bytes.</param>
    /// <returns>The declared version, or <see langword="null"/>.</returns>
    private static int? SchemaVersionOf(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }

            var depth = reader.CurrentDepth;

            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.PropertyName
                    && reader.CurrentDepth == depth + 1
                    && reader.ValueTextEquals(LockJson.SchemaVersion))
                {
                    return reader.Read() && reader.TokenType is JsonTokenType.Number && reader.TryGetInt32(out var version)
                        ? version
                        : null;
                }
            }
        }
        catch (JsonException)
        {
            // Not readable as JSON at all, which the strict parse reports far
            // better than this pass could.
        }

        return null;
    }

    private static DateTimeOffset Bound(bool first, params ReadOnlySpan<DateTimeOffset> candidates)
    {
        var chosen = candidates[0];

        foreach (var candidate in candidates[1..])
        {
            if (first ? candidate < chosen : candidate > chosen)
            {
                chosen = candidate;
            }
        }

        return chosen;
    }

    private DateTimeOffset Bound(bool first) =>
        Bound(
            first,
            first ? DirectoryHistory[0].At : DirectoryHistory[^1].At,
            first ? ModeHistory[0].At : ModeHistory[^1].At,
            first ? BrowserHistory[0].At : BrowserHistory[^1].At,
            first ? PurposeHistory[0].At : PurposeHistory[^1].At,
            first ? BrowserAiVersionHistory[0].At : BrowserAiVersionHistory[^1].At,
            first ? HolderHistory[0].At : HolderHistory[^1].At);

    private static string WrongSchema(int? schemaVersion, string path) =>
        schemaVersion switch
        {
            null =>
                $"'{path}' has no '{LockJson.SchemaVersion}', so nothing in it can be trusted to mean what this build thinks it means. {RecoveryAdvice(path)}",

            // The one version that ever existed before this build, named
            // specifically because its recovery is specific: there is no
            // converter and there deliberately never will be, so the fix has to
            // be a sentence rather than an upgrade path.
            < CurrentSchemaVersion =>
                $"'{path}' was written with schema version {schemaVersion.Value.ToString(CultureInfo.InvariantCulture)}; this build reads version {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)} only, "
                + "in which every field is an ordered list of timestamped statements rather than a single value. "
                + $"There is no converter and there will not be one. Delete '{path}' and call {SessionToolSurface.Init} on this directory again: the profile, output and downloads beside it are untouched and the new session goes on using them, "
                + $"and what is lost is the recorded purpose and the history, which no longer describe anything this build can act on. {RepeatingFails}",

            _ =>
                $"'{path}' was written with schema version {schemaVersion.Value.ToString(CultureInfo.InvariantCulture)}; this build of BrowserAI reads version {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)} only. A newer BrowserAI wrote this session. Use that build, or delete the session directory and create a new one. {RepeatingFails}",
        };

    private static string RecoveryAdvice(string path) =>
        $"Recovery: open '{path}' and remove what does not belong, or delete that file and create the session again — a session directory whose record is gone is a new session, not a broken one. {RepeatingFails}";

    private static LockFileException Unrecognised(string name, string path) =>
        new($"'{path}' carries a key BrowserAI does not recognise: '{name}'. Our own files are parsed strictly, because an unrecognised key is indistinguishable from a missing one under lenient parsing — the file would be reported as understood while a field nobody honoured decided nothing. {RecoveryAdvice(path)}");

    private static LockFileException Missing(string name, string path) =>
        new($"'{path}' has no '{name}', so it does not describe a session this build can act on. {RecoveryAdvice(path)}");

    private static LockFileException Empty(string name, string path) =>
        new($"'{path}' has an empty '{name}'. Every field is a list of timestamped statements and the newest one is the current value, so a list with nothing in it has no current value and cannot be acted on. {RecoveryAdvice(path)}");

    private static void ExpectStartObject(ref Utf8JsonReader reader, string path)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new LockFileException($"'{path}' does not start with a JSON object. {RecoveryAdvice(path)}");
        }
    }

    private static string? ReadPropertyName(ref Utf8JsonReader reader, string path)
    {
        if (!reader.Read())
        {
            throw new LockFileException($"'{path}' ends in the middle of the record. {RecoveryAdvice(path)}");
        }

        return reader.TokenType switch
        {
            JsonTokenType.PropertyName => reader.GetString(),
            JsonTokenType.EndObject => null,
            _ => throw new LockFileException($"'{path}' has a {reader.TokenType} where a key was expected. {RecoveryAdvice(path)}"),
        };
    }

    private static string ReadString(ref Utf8JsonReader reader, string name, string path) =>
        reader.Read() && reader.TokenType is JsonTokenType.String
            ? reader.GetString()!
            : throw Wrong(name, "a string", path);

    private static int ReadInt32(ref Utf8JsonReader reader, string name, string path) =>
        reader.Read() && reader.TokenType is JsonTokenType.Number
            ? reader.GetInt32()
            : throw Wrong(name, "a number", path);

    private static long ReadInt64(ref Utf8JsonReader reader, string name, string path) =>
        reader.Read() && reader.TokenType is JsonTokenType.Number
            ? reader.GetInt64()
            : throw Wrong(name, "a number", path);

    private static string? ReadNullableString(ref Utf8JsonReader reader, string name, string path)
    {
        if (!reader.Read())
        {
            throw Wrong(name, "a string or null", path);
        }

        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw Wrong(name, "a string or null", path),
        };
    }

    /// <summary>
    /// Reads one field: an array of <c>{ at, value }</c> objects, strictly.
    /// </summary>
    /// <remarks>
    /// <b>Written once and shared by both value shapes on purpose.</b> Every
    /// refusal schema 1 made — an unknown key, a missing key, a timestamp that
    /// is not round-trippable — has to hold at this new nesting level too, and
    /// two hand-written copies of the same walk are two chances for one of them
    /// to stop refusing.
    /// </remarks>
    private static List<Statement<T>> ReadStatements<T>(
        ref Utf8JsonReader reader,
        string name,
        string path,
        StatementValueReader<T> readValue)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartArray)
        {
            throw Wrong(name, "an array of timestamped statements", path);
        }

        var statements = new List<Statement<T>>();

        while (true)
        {
            if (!reader.Read())
            {
                throw new LockFileException($"'{path}' ends in the middle of '{name}'. {RecoveryAdvice(path)}");
            }

            if (reader.TokenType is JsonTokenType.EndArray)
            {
                break;
            }

            if (reader.TokenType is not JsonTokenType.StartObject)
            {
                throw Wrong(name, "an array of timestamped statements", path);
            }

            DateTimeOffset? at = null;
            var value = default(T);
            var sawValue = false;

            while (ReadPropertyName(ref reader, path) is { } key)
            {
                switch (key)
                {
                    case LockJson.At:
                        at = ReadTimestamp(ref reader, $"{name}.{LockJson.At}", path);
                        break;
                    case LockJson.Value:
                        value = readValue(ref reader, $"{name}.{LockJson.Value}", path);
                        sawValue = true;
                        break;
                    default:
                        throw Unrecognised($"{name}.{key}", path);
                }
            }

            statements.Add(new Statement<T>(
                at ?? throw Missing($"{name}.{LockJson.At}", path),
                sawValue ? value! : throw Missing($"{name}.{LockJson.Value}", path)));
        }

        return statements.Count is 0 ? throw Empty(name, path) : statements;
    }

    private static void WriteStatements<T>(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<Statement<T>> statements,
        Action<Utf8JsonWriter, T> writeValue)
    {
        writer.WriteStartArray(name);

        foreach (var statement in statements)
        {
            writer.WriteStartObject();
            writer.WriteString(LockJson.At, statement.At.ToString("O", CultureInfo.InvariantCulture));
            writer.WritePropertyName(LockJson.Value);
            writeValue(writer, statement.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteHolder(Utf8JsonWriter writer, LockHolder holder)
    {
        writer.WriteStartObject();
        writer.WriteNumber(LockJson.ProcessId, holder.ProcessId);
        writer.WriteNumber(LockJson.ProcessCreatedFileTime, holder.ProcessCreatedFileTime);

        if (holder.ClientProcessName is { } client)
        {
            writer.WriteString(LockJson.ClientProcessName, client);
        }
        else
        {
            writer.WriteNull(LockJson.ClientProcessName);
        }

        writer.WriteEndObject();
    }

    private static DateTimeOffset ReadTimestamp(ref Utf8JsonReader reader, string name, string path)
    {
        var text = ReadString(ref reader, name, path);

        // ParseExact against "O" and the invariant culture, so a file written
        // under any locale reads identically -- and so a value that is not
        // round-trippable ISO 8601 is refused rather than being coerced into
        // some plausible-looking date.
        return DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw new LockFileException(
                $"'{path}' has '{name}' = '{text}', which is not an ISO 8601 timestamp with an offset. {RecoveryAdvice(path)}");
    }

    private static LockHolder ReadHolder(ref Utf8JsonReader reader, string name, string path)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            throw Wrong(name, "an object", path);
        }

        int? processId = null;
        long? createdFileTime = null;
        var client = default(string);
        var sawClient = false;

        while (ReadPropertyName(ref reader, path) is { } key)
        {
            switch (key)
            {
                case LockJson.ProcessId:
                    processId = ReadInt32(ref reader, key, path);
                    break;
                case LockJson.ProcessCreatedFileTime:
                    createdFileTime = ReadInt64(ref reader, key, path);
                    break;
                case LockJson.ClientProcessName:
                    client = ReadNullableString(ref reader, key, path);
                    sawClient = true;
                    break;
                default:
                    throw Unrecognised($"{name}.{key}", path);
            }
        }

        return new LockHolder
        {
            ProcessId = processId ?? throw Missing($"{name}.{LockJson.ProcessId}", path),
            ProcessCreatedFileTime = createdFileTime ?? throw Missing($"{name}.{LockJson.ProcessCreatedFileTime}", path),
            ClientProcessName = sawClient ? client : throw Missing($"{name}.{LockJson.ClientProcessName}", path),
        };
    }

    private static LockFileException Wrong(string name, string expected, string path) =>
        new($"'{path}' has '{name}' where {expected} was expected. {RecoveryAdvice(path)}");
}

/// <summary>Who holds, or last held, a session directory.</summary>
internal sealed record LockHolder
{
    /// <summary>The holder's process id.</summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// Its creation time as a Windows FILETIME. Together with
    /// <see cref="ProcessId"/> this is the identity; the pid alone is not,
    /// because Windows reuses pids.
    /// </summary>
    public required long ProcessCreatedFileTime { get; init; }

    /// <summary>
    /// The MCP client that started the holder, for a person reading the file.
    /// <see langword="null"/> when it could not be read.
    /// </summary>
    /// <remarks>
    /// Display data. Nothing in BrowserAI ever chooses, counts or terminates a
    /// process by this value.
    /// </remarks>
    public required string? ClientProcessName { get; init; }
}

/// <summary>The key names in <c>browserai.json</c>, spelled once.</summary>
/// <remarks>
/// Strict parsing means a typo in a key name here becomes a refusal at runtime
/// rather than a field that silently reads as absent, so the writer and the
/// reader take their names from the same constants.
/// </remarks>
internal static class LockJson
{
    /// <summary>Which version of the schema wrote the file.</summary>
    public const string SchemaVersion = "schemaVersion";

    /// <summary>When one statement was made.</summary>
    public const string At = "at";

    /// <summary>What one statement said.</summary>
    public const string Value = "value";

    /// <summary>Every absolute path the record has been written at.</summary>
    public const string Directory = "directory";

    /// <summary>Every mode the session has been.</summary>
    public const string Mode = "mode";

    /// <summary>Every browser family the profile has belonged to.</summary>
    public const string Browser = "browser";

    /// <summary>Everything the session has been for.</summary>
    public const string Purpose = "purpose";

    /// <summary>Every build that has written the record.</summary>
    public const string BrowserAiVersion = "browserAiVersion";

    /// <summary>Everyone who has held the lock.</summary>
    public const string Holder = "holder";

    /// <summary>The holder's pid.</summary>
    public const string ProcessId = "processId";

    /// <summary>The holder's creation time, as a FILETIME.</summary>
    public const string ProcessCreatedFileTime = "processCreatedFileTime";

    /// <summary>The client that started the holder.</summary>
    public const string ClientProcessName = "clientProcessName";
}

/// <summary>
/// A <c>browserai.json</c> that cannot be acted on, with a recovery in its message.
/// </summary>
/// <remarks>
/// The message always names a recovery that is <b>not</b> the call that just
/// failed. A retry that repeats the failed call is not a recovery, and offering
/// one to a model is how a caller ends up in a loop that never terminates and
/// never explains itself.
/// </remarks>
internal sealed class LockFileException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public LockFileException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and how to recover.</param>
    public LockFileException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and how to recover.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public LockFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

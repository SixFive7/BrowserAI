// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

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
/// <b>A statement is appended only when the value changes</b>, so the three
/// fields that describe what the session <i>is</i> — browser, directory, build —
/// stay one statement long for the life of a session that is not moved, copied or
/// run under a new build. <i>Corrected 2026-08-20 (previously "the four fields …
/// mode, browser, directory, build"); <c>mode</c> went at schema 3.</i> Growth comes from <see cref="PurposeHistory"/> and
/// <see cref="HolderHistory"/>, and is capped: see
/// <see cref="MaximumStatementsPerField"/>.
/// </para>
/// <para>
/// <b>A schema reads only its own version, and there is no converter.</b> A
/// version-1 or version-2 file is refused with a message naming the fix. That is
/// a deliberate choice rather than an omission — a converter for a format with no
/// installed base is code that can only ever be wrong in private, and the recovery
/// costs a caller nothing that matters: the profile, output and downloads survive
/// the delete, so what is lost is the recorded purpose and the history.
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
    /// ⚠️ <b>4 since 2026-08-20 (previously 3, earlier the same day).</b> What
    /// moved at 4 is the <see cref="Log"/>: one time-ordered stream carrying
    /// <c>init</c>'s purpose, every purpose change, and every browser call this
    /// session forwarded, so that a reader sees <i>the human changed the purpose
    /// here</i> sitting between the calls it explains. Two bumps in one day is
    /// not a mistake and is not consolidated: 3 and 4 are separate changes with
    /// separate reasons, and a reader who meets a version-3 file on disk needs
    /// the sentence that describes it rather than one that describes both.
    /// <br /><br />
    /// ⚠️ <b>3 since 2026-08-20 (previously 2 since 2026-08-18, and 1 before
    /// that — one value per field with a history on <c>purpose</c> alone).</b>
    /// Bumping it is what makes an old file a refusal with a recovery rather than
    /// a record read as something it is not. <b>What moved at 3 is that
    /// <c>mode</c> is gone.</b> Session modes were deleted that day, every
    /// capability is granted to every session, and headedness became a per-run
    /// argument — so a <c>mode</c> in a record described nothing, and the strict
    /// parser below would have gone on requiring a field no code reads. A
    /// version-2 file is refused with the recovery it has always carried: delete
    /// the record and call <c>browserai_init</c> on the directory again; the
    /// profile, output and downloads beside it are untouched and the new session
    /// goes on using them.
    /// </remarks>
    public const int CurrentSchemaVersion = 4;

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

    /// <summary>
    /// How many entries the <see cref="Log"/> keeps, and therefore the answer to
    /// "does a busy session grow <c>browserai.json</c> without bound".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It does not, and the bound is stated in bytes rather than in
    /// entries.</b> An entry is a timestamp, a tool name, a <c>why</c> capped at
    /// <see cref="WhyMaximumLength"/>, and a list of arguments each capped at
    /// <see cref="ArgumentValueMaximumLength"/> — so the worst case is roughly
    /// 250 × (400 + a handful of 200-character values) ≈ 400 KB. That is a large
    /// file for a session that made two hundred and fifty calls and it is
    /// written <c>WriteThrough</c> on every one of them; the cost is real and is
    /// [recorded as a decision](../../../QUESTIONS.md) rather than hidden.
    /// </para>
    /// <para>
    /// <b>The oldest entry is never dropped; the second-oldest is.</b> Entry
    /// zero is <c>browserai_init</c> — what the session was created for — and a
    /// policy that trimmed the front would lose the only statement of why the
    /// directory exists. So the elision is in the <b>middle</b>, exactly as
    /// <see cref="MaximumStatementsPerField"/>'s is, and it is <b>visible</b>:
    /// <c>browserai_catch_up</c> and every answer that reads the log say how
    /// many entries were dropped rather than presenting a gap as continuity.
    /// </para>
    /// </remarks>
    public const int MaximumLogEntries = 250;

    /// <summary>
    /// The longest <c>why</c> that is stored, per entry.
    /// </summary>
    /// <remarks>
    /// Shorter than <see cref="PurposeMaximumLength"/> on purpose: a purpose is
    /// one sentence describing a whole session and is read once, while a
    /// <c>why</c> is one clause describing one call and there are up to
    /// <see cref="MaximumLogEntries"/> of them. The cap is what keeps a model
    /// that writes an essay per call from turning the record into its own
    /// transcript.
    /// </remarks>
    public const int WhyMaximumLength = 400;

    /// <summary>
    /// The longest argument value that is stored verbatim before it is cut.
    /// </summary>
    /// <remarks>
    /// See <see cref="LoggedArgument"/> for what is stored instead of a long
    /// one, and for the two parameter names whose value is never stored at all.
    /// </remarks>
    public const int ArgumentValueMaximumLength = 200;

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

    /// <summary>Every browser family this profile has belonged to. In practice: one.</summary>
    public required IReadOnlyList<Statement<string>> BrowserHistory { get; init; }

    /// <summary>Everything the session has been for, oldest first.</summary>
    public required IReadOnlyList<Statement<string>> PurposeHistory { get; init; }

    /// <summary>Every BrowserAI build that has written this record.</summary>
    public required IReadOnlyList<Statement<string>> BrowserAiVersionHistory { get; init; }

    /// <summary>Everyone who has held, or last held, the lock.</summary>
    public required IReadOnlyList<Statement<LockHolder>> HolderHistory { get; init; }

    /// <summary>
    /// Everything this session has done, oldest first: one stream, not two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One stream is the whole design.</b> <c>init</c>'s purpose, every
    /// purpose change on <c>resume</c>, every explicit <c>browserai_set_purpose</c>
    /// and every browser call this session forwarded are entries in the same
    /// ordered list — so a reader sees <i>the human changed the purpose here</i>
    /// sitting between the calls it explains. Two files, or two arrays, would
    /// have to be merged by timestamp by whoever read them, and a merge nobody
    /// performs is a story nobody reads.
    /// </para>
    /// <para>
    /// <b>Refused calls are not here.</b> This records what the session
    /// <i>did</i>: an entry is written immediately before a call is forwarded to
    /// the browser, so a call that never returns still left one, and a call that
    /// BrowserAI declined never reaches this list. The refusals are in the
    /// session's own <c>browserai.log</c> beside it, which is where a reader
    /// looking for <i>what was attempted and refused</i> is sent.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is inside <c>browserai.json</c> rather than in a sibling
    /// append-only file, and that was the maintainer's decision over a
    /// recommendation of the sibling.</b> What it buys: one file, one lock, one
    /// atomic rename, and a session whose whole story cannot be half-copied when
    /// the directory is moved. What it costs: a durable
    /// <c>WriteThrough</c> write and a rename of the entire record on **every
    /// forwarded browser call**, where an append-only sibling would have been an
    /// <c>O(entry)</c> append. [QUESTIONS.md](../../../QUESTIONS.md) records both
    /// sides.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<LogEntry> Log { get; init; }

    /// <summary>The resolved absolute path as of the newest statement.</summary>
    public string Directory => DirectoryHistory[^1].Value;

    /// <summary>The browser family this profile belongs to.</summary>
    public string Browser => BrowserHistory[^1].Value;

    /// <summary>What the session is for, as the agent that last set it said.</summary>
    public string Purpose => PurposeHistory[^1].Value;

    /// <summary>The BrowserAI build that last wrote this record.</summary>
    public string BrowserAiVersion => BrowserAiVersionHistory[^1].Value;

    /// <summary>Who holds, or last held, the lock.</summary>
    public LockHolder Holder => HolderHistory[^1].Value;

    /// <summary>Whether the log has had entries dropped out of its middle.</summary>
    /// <remarks>
    /// <b>It says <i>may</i> rather than <i>has</i>, and the difference is the
    /// point.</b> The record does not carry a count of what the trim removed —
    /// adding one would be a second number to keep in step with a list — so a
    /// full log is one that either has been trimmed or is exactly at the cap.
    /// Every answer that reads the log says <i>may</i> for that reason.
    /// </remarks>
    public bool LogIsAtTheCap => Log.Count >= MaximumLogEntries;

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

    /// <summary>When anything about this session last happened.</summary>
    /// <remarks>
    /// ⚠️ <b>The log is read here and deliberately NOT in
    /// <see cref="Created"/>.</b> Before the log existed, a session driven for an
    /// hour without its purpose or its holder changing appended nothing to any
    /// statement list, so this answered <i>when the session was opened</i> rather
    /// than when it was last used — and <c>browserai_list</c> printed that as
    /// "last used". The log's newest entry is the honest answer.
    /// <br /><br />
    /// <c>Created</c> is left alone because it already had a correct answer and
    /// a stronger property: it is the first statement of every field, and the
    /// trim never removes a first statement. The log's first entry is the
    /// <c>init</c> call, written microseconds before the record it lands in, so
    /// including it would move <c>Created</c> earlier by an interval that means
    /// nothing and break the invariant that <c>Created</c> is a statement's own
    /// timestamp.
    /// </remarks>
    public DateTimeOffset LastUsed =>
        Log.Count is 0 || Log[^1].At < Bound(first: false)
            ? Bound(first: false)
            : Log[^1].At;

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
        && BrowserHistory.SequenceEqual(other.BrowserHistory)
        && PurposeHistory.SequenceEqual(other.PurposeHistory)
        && BrowserAiVersionHistory.SequenceEqual(other.BrowserAiVersionHistory)
        && HolderHistory.SequenceEqual(other.HolderHistory)
        && Log.SequenceEqual(other.Log);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(SchemaVersion, Directory, Browser, Purpose, Created, LastUsed, Holder);

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

    /// <summary>Appends one entry to the log, trimming out of the middle.</summary>
    /// <remarks>
    /// <b>Unconditional, where <see cref="Append{T}"/> deduplicates.</b> A field
    /// that says the same thing twice has not changed and gains nothing from a
    /// second statement; a session that made the same call twice <i>did</i> make
    /// it twice, and collapsing those would turn a retry loop into a single
    /// entry — which is exactly the shape a reader is looking for.
    /// </remarks>
    /// <param name="previous">The log so far, or <see langword="null"/> for a new record.</param>
    /// <param name="entry">What to append.</param>
    /// <returns>The next log.</returns>
    public static IReadOnlyList<LogEntry> AppendLog(IReadOnlyList<LogEntry>? previous, LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var next = new List<LogEntry>(previous ?? []) { entry };

        // Out of the MIDDLE, keeping entry zero -- what the session was created
        // for -- exactly as the statement trim keeps the first statement that
        // `Created` is read from.
        while (next.Count > MaximumLogEntries)
        {
            next.RemoveAt(1);
        }

        return next;
    }

    /// <summary>
    /// Flattens and caps a <c>why</c>, the same way a purpose is flattened and
    /// capped.
    /// </summary>
    /// <remarks>
    /// <b>The same treatment for the same reason.</b> A <c>why</c> is free text
    /// written by one model into a file another model reads, so a control
    /// character in it is a control character in somebody else's context, and an
    /// uncapped one is an unbounded span of another agent's text.
    /// </remarks>
    /// <param name="why">What the caller said the call was for.</param>
    /// <returns>The text as it is stored.</returns>
    public static string SanitiseWhy(string why) => Flatten(why, WhyMaximumLength);

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
        return Flatten(purpose, PurposeMaximumLength);
    }

    /// <summary>Caps a string and replaces every control character with a space.</summary>
    /// <param name="text">The text as it arrived.</param>
    /// <param name="maximum">The longest result.</param>
    /// <returns>The flattened, capped, trimmed text.</returns>
    private static string Flatten(string text, int maximum)
    {
        ArgumentNullException.ThrowIfNull(text);

        var flattened = string.Create(
            Math.Min(text.Length, maximum),
            text,
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
            WriteStatements(writer, LockJson.Browser, BrowserHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.Purpose, PurposeHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.BrowserAiVersion, BrowserAiVersionHistory, static (w, value) => w.WriteStringValue(value));
            WriteStatements(writer, LockJson.Holder, HolderHistory, static (w, holder) => WriteHolder(w, holder));

            writer.WriteStartArray(LockJson.Log);

            foreach (var entry in Log)
            {
                writer.WriteStartObject();
                writer.WriteString(LockJson.At, entry.At.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString(LockJson.Tool, entry.Tool);
                writer.WriteString(LockJson.Why, entry.Why);

                writer.WriteStartArray(LockJson.Arguments);

                foreach (var argument in entry.Arguments)
                {
                    writer.WriteStartObject();
                    writer.WriteString(LockJson.Name, argument.Name);
                    writer.WriteString(LockJson.Value, argument.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

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
        List<Statement<string>>? browser = null;
        List<Statement<string>>? purpose = null;
        List<Statement<string>>? version = null;
        List<Statement<LockHolder>>? holder = null;
        List<LogEntry>? log = null;

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
                    case LockJson.Log:
                        log = ReadLog(ref reader, name, path);
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
            BrowserHistory = browser ?? throw Missing(LockJson.Browser, path),
            PurposeHistory = purpose ?? throw Missing(LockJson.Purpose, path),
            BrowserAiVersionHistory = version ?? throw Missing(LockJson.BrowserAiVersion, path),
            HolderHistory = holder ?? throw Missing(LockJson.Holder, path),

            // ⚠️ EMPTY IS LEGAL HERE AND NOWHERE ELSE IN THIS RECORD. Every
            // other field is a list of statements whose newest element IS the
            // current value, so an empty one has no value and cannot be acted
            // on. The log is a history rather than a value: `browserai_destroy`
            // takes the directory without appending, so a record written by that
            // path has whatever the session left behind -- and a session that
            // was created and immediately destroyed leaves nothing.
            Log = log ?? throw Missing(LockJson.Log, path),
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

    /// <summary>Reads the whole <c>log</c> array.</summary>
    /// <remarks>
    /// Hand-written for the same reason every other reader here is: our own
    /// files are parsed <b>strictly</b>, so an unrecognised key inside an entry
    /// is a refusal rather than something dropped in silence.
    /// </remarks>
    /// <param name="reader">The reader, positioned before the array.</param>
    /// <param name="name">The member's name, for a message.</param>
    /// <param name="path">The file, for a message.</param>
    /// <returns>The entries, oldest first.</returns>
    private static List<LogEntry> ReadLog(ref Utf8JsonReader reader, string name, string path)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartArray)
        {
            throw Wrong(name, "an array of log entries", path);
        }

        var entries = new List<LogEntry>();

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
                throw Wrong(name, "an array of log entries", path);
            }

            DateTimeOffset? at = null;
            string? tool = null;
            string? why = null;
            List<LoggedArgument>? arguments = null;

            while (ReadPropertyName(ref reader, path) is { } key)
            {
                switch (key)
                {
                    case LockJson.At:
                        at = ReadTimestamp(ref reader, $"{name}.{LockJson.At}", path);
                        break;
                    case LockJson.Tool:
                        tool = ReadString(ref reader, $"{name}.{LockJson.Tool}", path);
                        break;
                    case LockJson.Why:
                        why = ReadString(ref reader, $"{name}.{LockJson.Why}", path);
                        break;
                    case LockJson.Arguments:
                        arguments = ReadArguments(ref reader, $"{name}.{LockJson.Arguments}", path);
                        break;
                    default:
                        throw Unrecognised($"{name}.{key}", path);
                }
            }

            entries.Add(new LogEntry(
                at ?? throw Missing($"{name}.{LockJson.At}", path),
                tool ?? throw Missing($"{name}.{LockJson.Tool}", path),
                why ?? throw Missing($"{name}.{LockJson.Why}", path),
                arguments ?? throw Missing($"{name}.{LockJson.Arguments}", path)));
        }

        return entries;
    }

    /// <summary>Reads one entry's <c>arguments</c> array.</summary>
    /// <param name="reader">The reader, positioned before the array.</param>
    /// <param name="name">The member's name, for a message.</param>
    /// <param name="path">The file, for a message.</param>
    /// <returns>The arguments, in the order they were recorded.</returns>
    private static List<LoggedArgument> ReadArguments(ref Utf8JsonReader reader, string name, string path)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartArray)
        {
            throw Wrong(name, "an array of arguments", path);
        }

        var arguments = new List<LoggedArgument>();

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
                throw Wrong(name, "an array of arguments", path);
            }

            string? argument = null;
            string? value = null;

            while (ReadPropertyName(ref reader, path) is { } key)
            {
                switch (key)
                {
                    case LockJson.Name:
                        argument = ReadString(ref reader, $"{name}.{LockJson.Name}", path);
                        break;
                    case LockJson.Value:
                        value = ReadString(ref reader, $"{name}.{LockJson.Value}", path);
                        break;
                    default:
                        throw Unrecognised($"{name}.{key}", path);
                }
            }

            arguments.Add(new LoggedArgument(
                argument ?? throw Missing($"{name}.{LockJson.Name}", path),
                value ?? throw Missing($"{name}.{LockJson.Value}", path)));
        }

        return arguments;
    }

    private static LockFileException Wrong(string name, string expected, string path) =>
        new($"'{path}' has '{name}' where {expected} was expected. {RecoveryAdvice(path)}");
}

/// <summary>
/// One thing this session did: when, which tool, what the caller said it was
/// for, and the arguments it carried.
/// </summary>
/// <param name="At">When the call was made — before it was forwarded, not after it returned.</param>
/// <param name="Tool">
/// The tool, as it was named on the wire. An authored one is
/// <c>browserai_</c>-prefixed; everything else is upstream's own byte-for-byte
/// name.
/// </param>
/// <param name="Why">
/// What the caller said the call was for, capped and de-controlled.
/// <para>
/// ⚠️ <b>For <c>browserai_init</c> this carries the session's <c>purpose</c>.</b>
/// <c>init</c> has no <c>why</c> argument — two mandatory free-text fields on one
/// call gets one thoughtful answer and one restatement — and the purpose <i>is</i>
/// the reason the session exists, so it is the honest first entry of the stream
/// rather than a blank one.
/// </para>
/// </param>
/// <param name="Arguments">
/// What the call carried, after <see cref="LoggedArgument"/>'s policy has been
/// applied to every value. Every argument's <b>name</b> is here whatever
/// happened to its value.
/// </param>
internal sealed record LogEntry(
    DateTimeOffset At,
    string Tool,
    string Why,
    IReadOnlyList<LoggedArgument> Arguments)
{
    /// <inheritdoc />
    public bool Equals(LogEntry? other) =>
        other is not null
        && At == other.At
        && string.Equals(Tool, other.Tool, StringComparison.Ordinal)
        && string.Equals(Why, other.Why, StringComparison.Ordinal)
        && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(At, Tool, Why, Arguments.Count);
}

/// <summary>
/// One argument as it is stored: the parameter name, always, and a value that
/// has been through the policy below.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is always recorded and the value sometimes is not, and that
/// asymmetry is the decision.</b> A reader has to be able to see that a password
/// field was filled even when what it was filled with is not here; a log that
/// dropped the whole argument would present the call as though it had taken none.
/// </para>
/// <para>
/// <b>Three rules, in this order</b> — see <see cref="Of"/>:
/// </para>
/// <list type="number">
/// <item>
/// <b>A withheld name is never stored, at any length.</b>
/// <see cref="WithheldNames"/> is <c>value</c> and <c>text</c> — the two scalar
/// string parameters upstream uses for something a person typed or a server set:
/// <c>browser_cookie_set</c>, <c>browser_localstorage_set</c>,
/// <c>browser_sessionstorage_set</c> and <c>browser_type</c>. What is stored is
/// <c>&lt;withheld, N characters&gt;</c>, so the length survives and the content
/// does not.
/// </item>
/// <item>
/// <b>A non-scalar is stored as a shape</b> — <c>&lt;object, N keys&gt;</c> or
/// <c>&lt;array, N items&gt;</c>. <c>browser_fill_form</c>'s <c>fields</c> and
/// <c>browser_route</c>'s <c>headers</c> are the two that matter, and both are
/// exactly the payloads that carry many values at once. A recursive walk would
/// have to take the same withhold-or-store decision again at every depth with
/// strictly less information about what the leaf means.
/// </item>
/// <item>
/// <b>Everything else is stored verbatim up to
/// <see cref="LockRecord.ArgumentValueMaximumLength"/> characters</b>, then cut
/// with <c>… (+N more characters)</c>. That is what turns
/// <c>browser_evaluate</c>'s function body from a transcript into a summary: the
/// first two hundred characters of a script say what it was reaching for, which
/// is the question a reader has.
/// </item>
/// </list>
/// <para>
/// ⚠️ <b>What this is NOT.</b> It is not a redaction boundary. The log lives
/// inside the session directory, and so does the browser profile whose cookie
/// database holds the same credentials — [measured
/// 2026-08-18](../../../kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18),
/// recoverable by any process running as the same user. Withholding <c>value</c>
/// and <c>text</c> stops a password being written into a file a model is
/// <i>invited to read back</i>; it does not stop anything that could read the
/// directory at all. The distinction is the same one the session-mode removal
/// rested on, and it points the same way.
/// </para>
/// </remarks>
/// <param name="Name">The parameter's name, exactly as the caller wrote it.</param>
/// <param name="Value">The value as it is stored, which may be a summary or a withholding marker.</param>
internal sealed record LoggedArgument(string Name, string Value)
{
    /// <summary>
    /// The parameter names whose value is never stored.
    /// </summary>
    /// <remarks>
    /// <b>Two, and they are grounded in the golden snapshot rather than
    /// guessed</b> — <c>LogEntryTests</c> asserts that each is a real parameter
    /// name on a real upstream tool, so a rename upstream turns the build red
    /// instead of quietly widening what is written down.
    /// </remarks>
    public static IReadOnlyList<string> WithheldNames { get; } = ["value", "text"];

    /// <summary>Applies the policy to one argument.</summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">The value, as it arrived on the wire.</param>
    /// <returns>The argument as it is stored.</returns>
    public static LoggedArgument Of(string name, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (WithheldNames.Contains(name, StringComparer.Ordinal))
        {
            var length = (value as JsonValue)?.TryGetValue(out string? text) is true ? text!.Length : 0;

            return new LoggedArgument(name, $"<withheld, {length.ToString(CultureInfo.InvariantCulture)} characters>");
        }

        return new LoggedArgument(name, Summarise(value));
    }

    /// <summary>Turns one value into the string that is stored.</summary>
    /// <param name="value">The value, as it arrived.</param>
    /// <returns>The stored form.</returns>
    private static string Summarise(JsonNode? value) =>
        value switch
        {
            null => "<null>",
            JsonObject nested => $"<object, {nested.Count.ToString(CultureInfo.InvariantCulture)} keys>",
            JsonArray array => $"<array, {array.Count.ToString(CultureInfo.InvariantCulture)} items>",
            _ => Cut(value.ToString()),
        };

    /// <summary>Caps one scalar, saying how much was left out.</summary>
    /// <param name="text">The value as text.</param>
    /// <returns>The stored form.</returns>
    private static string Cut(string text)
    {
        var flat = LockRecord.SanitiseWhy(text);

        if (text.Length <= LockRecord.ArgumentValueMaximumLength)
        {
            return flat;
        }

        var kept = flat.Length > LockRecord.ArgumentValueMaximumLength
            ? flat[..LockRecord.ArgumentValueMaximumLength]
            : flat;

        var dropped = text.Length - LockRecord.ArgumentValueMaximumLength;

        return $"{kept}… (+{dropped.ToString(CultureInfo.InvariantCulture)} more characters)";
    }
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

    /// <summary>Everything the session has done, oldest first.</summary>
    public const string Log = "log";

    /// <summary>The tool one log entry is about.</summary>
    public const string Tool = "tool";

    /// <summary>What the caller said one call was for.</summary>
    public const string Why = "why";

    /// <summary>The arguments one call carried, as they are stored.</summary>
    public const string Arguments = "arguments";

    /// <summary>One argument's parameter name.</summary>
    public const string Name = "name";
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

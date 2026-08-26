// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.Storage;

namespace BrowserAI.Sessions;

/// <summary>
/// Which field one row of <c>statements</c> is a statement about.
/// </summary>
/// <remarks>
/// <b>The set is closed and it is spelled once.</b> A reader groups by these
/// strings and a writer emits them, so a spelling that existed in one place and
/// not the other would produce a history that silently stopped growing rather
/// than a failure anybody could see.
/// </remarks>
internal static class RecordFields
{
    /// <summary>Every absolute path this session has been written at.</summary>
    public const string Directory = "directory";

    /// <summary>Every browser family this profile has belonged to. In practice: one.</summary>
    public const string Browser = "browser";

    /// <summary>Everything the session has been for, oldest first.</summary>
    public const string Purpose = "purpose";

    /// <summary>Every BrowserAI build that has written this record.</summary>
    public const string BrowserAiVersion = "browserAiVersion";

    /// <summary>Who has held the directory, one row per acquisition.</summary>
    public const string Holder = "holder";
}

/// <summary>One timestamped thing a session said about itself.</summary>
/// <typeparam name="T">What the statement carries.</typeparam>
/// <param name="At">When it was made.</param>
/// <param name="Value">What it says.</param>
internal sealed record Statement<T>(DateTimeOffset At, T Value);

/// <summary>
/// A session's record cannot be acted on: the store, the guard, or the format.
/// </summary>
/// <remarks>
/// <b>One exception for three sources, deliberately.</b> A caller asking *what
/// is this directory* meets a SQLite refusal, an unparseable <c>browserai.lock</c>
/// and a directory carrying the old <c>browserai.json</c> as the same kind of
/// event — the record could not be read and nothing was changed — and every one
/// of them arrives with a sentence that names the recovery. Three exception
/// types would put three catch arms at every reader, and the reader that
/// forgets one is the one that turns a readable failure into a stack trace.
/// </remarks>
internal sealed class SessionRecordException : Exception
{
    /// <inheritdoc />
    public SessionRecordException()
    {
    }

    /// <inheritdoc />
    /// <param name="message">What is wrong, and what to do about it.</param>
    public SessionRecordException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    /// <param name="message">What is wrong, and what to do about it.</param>
    /// <param name="innerException">What produced it.</param>
    public SessionRecordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One session as a reader sees it: every statement it has made about itself,
/// and how much of a log there is behind them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Assembled from two files, and it says which is which.</b> The statements
/// come from <c>browserai.data</c>; whether anybody is <i>holding</i> the
/// directory does not come from here at all — that is
/// <see cref="SessionLock.ProbeLiveness"/>, one <c>CreateFile</c> on
/// <c>browserai.lock</c>, and no reader of this type may substitute the newest
/// <see cref="HolderHistory"/> row for it. A holder row says who took the
/// directory; it cannot say whether they still have it.
/// </para>
/// <para>
/// <b>Newest statement wins, and nothing is ever overwritten.</b> A session
/// that moves gains a <c>directory</c> row; a session that is re-purposed gains
/// a <c>purpose</c> row. ⚠️ <b>That is what killed the concatenation</b> —
/// <c>resume</c> used to build the next purpose out of the whole of the
/// previous one, which grew quadratically and lost the tail of it silently at
/// the 2,000-character cap. A row is a row.
/// </para>
/// <para>
/// <b>The log is not in here.</b> It is read a page at a time, because a
/// session with ten thousand calls in it has a record this type would otherwise
/// have to hold in memory to answer *what browser is this*.
/// <see cref="LogLength"/> is the count and nothing else.
/// </para>
/// </remarks>
internal sealed class SessionRecord
{
    /// <summary>Every path this record has been written at, oldest first.</summary>
    public required IReadOnlyList<Statement<string>> DirectoryHistory { get; init; }

    /// <summary>Every browser family this profile has belonged to, oldest first.</summary>
    public required IReadOnlyList<Statement<string>> BrowserHistory { get; init; }

    /// <summary>Everything this session has been for, oldest first.</summary>
    public required IReadOnlyList<Statement<string>> PurposeHistory { get; init; }

    /// <summary>Every build that has written this record, oldest first.</summary>
    public required IReadOnlyList<Statement<string>> BrowserAiVersionHistory { get; init; }

    /// <summary>Everyone who has taken this directory, oldest first.</summary>
    public required IReadOnlyList<Statement<LockFileHolder>> HolderHistory { get; init; }

    /// <summary>How many rows the log holds.</summary>
    public required long LogLength { get; init; }

    /// <summary>
    /// When this session was created: the oldest statement in the record.
    /// </summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>
    /// When anything last happened here: the newest of every statement and the
    /// newest log row.
    /// </summary>
    /// <remarks>
    /// <b>The log half is what makes this move during a session.</b> Before the
    /// log existed, an hour of driving a browser moved no timestamp at all,
    /// because nothing but an acquisition wrote a statement.
    /// </remarks>
    public required DateTimeOffset LastUsed { get; init; }

    /// <summary>The path the newest <c>directory</c> statement names.</summary>
    public string Directory => Newest(DirectoryHistory) ?? string.Empty;

    /// <summary>The family the newest <c>browser</c> statement names.</summary>
    public string Browser => Newest(BrowserHistory) ?? string.Empty;

    /// <summary>What the newest <c>purpose</c> statement says.</summary>
    public string Purpose => Newest(PurposeHistory) ?? string.Empty;

    /// <summary>The build the newest <c>browserAiVersion</c> statement names.</summary>
    public string BrowserAiVersion => Newest(BrowserAiVersionHistory) ?? string.Empty;

    /// <summary>Who took the directory most recently, whether or not they still have it.</summary>
    public LockFileHolder? Holder => HolderHistory.Count is 0 ? null : HolderHistory[^1].Value;

    /// <summary>When the newest holder took it.</summary>
    public DateTimeOffset TakenAt => HolderHistory.Count is 0 ? Created : HolderHistory[^1].At;

    /// <summary>The newest value of a field, or <see langword="null"/> when it has none.</summary>
    /// <param name="statements">The field's history.</param>
    /// <returns>The value.</returns>
    private static string? Newest(IReadOnlyList<Statement<string>> statements) =>
        statements.Count is 0 ? null : statements[^1].Value;
}

/// <summary>
/// One row of the log, rendered rather than stored: the store's own shape with
/// its timestamps parsed and its failure payload turned back into text.
/// </summary>
/// <param name="Id">The row's id, which is also its order and its page number's basis.</param>
/// <param name="At">When the call was made, <b>before</b> it was forwarded.</param>
/// <param name="Tool">The tool name, verbatim, whatever the caller said.</param>
/// <param name="Why">What the caller said it was for.</param>
/// <param name="Outcome">One of the three <see cref="SessionStore"/> spells.</param>
/// <param name="SettledAt">When the answer arrived, or <see langword="null"/> while it has not.</param>
/// <param name="Failure">Why it failed, or <see langword="null"/>.</param>
internal sealed record SessionLogRow(
    long Id,
    DateTimeOffset At,
    string Tool,
    string Why,
    string Outcome,
    DateTimeOffset? SettledAt,
    string? Failure);

/// <summary>
/// Reads and writes the shapes above against a <see cref="SessionStore"/>.
/// </summary>
/// <remarks>
/// <b>Every timestamp in the store is round-trippable ISO 8601 and this is the
/// only place that is true by construction.</b> The store's columns are text,
/// so a stamp written one way and parsed another would produce a record that
/// reads as valid and sorts wrongly.
/// </remarks>
internal static class SessionRecordReader
{
    /// <summary>How the store spells an instant.</summary>
    private const string StampFormat = "O";

    /// <summary>An instant as the store holds it.</summary>
    /// <param name="moment">The instant.</param>
    /// <returns>The text.</returns>
    public static string Stamp(DateTimeOffset moment) => moment.ToString(StampFormat, CultureInfo.InvariantCulture);

    /// <summary>An instant as the store holds it, back again.</summary>
    /// <remarks>
    /// <b>A stamp this build cannot parse is the epoch rather than a
    /// refusal.</b> The record's job is to say what happened; a timestamp that
    /// somebody hand-edited into nonsense costs an ordering, not the whole
    /// history behind it.
    /// </remarks>
    /// <param name="text">The stored text.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset Moment(string? text) =>
        DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var moment)
            ? moment
            : DateTimeOffset.MinValue;

    /// <summary>Assembles the record from everything the store holds.</summary>
    /// <param name="store">An open store.</param>
    /// <returns>The record.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public static SessionRecord Read(SessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var directory = new List<Statement<string>>();
        var browser = new List<Statement<string>>();
        var purpose = new List<Statement<string>>();
        var version = new List<Statement<string>>();
        var holder = new List<Statement<LockFileHolder>>();

        var oldest = DateTimeOffset.MaxValue;
        var newest = DateTimeOffset.MinValue;

        foreach (var row in store.Statements())
        {
            var at = Moment(row.At);

            if (at < oldest)
            {
                oldest = at;
            }

            if (at > newest)
            {
                newest = at;
            }

            switch (row.Field)
            {
                case RecordFields.Directory:
                    directory.Add(new Statement<string>(at, row.Value));
                    break;

                case RecordFields.Browser:
                    browser.Add(new Statement<string>(at, row.Value));
                    break;

                case RecordFields.Purpose:
                    purpose.Add(new Statement<string>(at, row.Value));
                    break;

                case RecordFields.BrowserAiVersion:
                    version.Add(new Statement<string>(at, row.Value));
                    break;

                case RecordFields.Holder:
                    holder.Add(new Statement<LockFileHolder>(at, ReadHolder(row.Value)));
                    break;

                default:
                    // Deliberately kept rather than refused. A field this build
                    // does not know is a field a LATER build wrote, and the
                    // schema version is what refuses a record this one cannot
                    // act on -- a strict reader here would turn "one unknown
                    // row" into "this directory is not a session".
                    break;
            }
        }

        var newestCall = Moment(store.NewestLogAt());

        if (newestCall > newest)
        {
            newest = newestCall;
        }

        return new SessionRecord
        {
            DirectoryHistory = directory,
            BrowserHistory = browser,
            PurposeHistory = purpose,
            BrowserAiVersionHistory = version,
            HolderHistory = holder,
            LogLength = store.LogLength(),
            Created = oldest == DateTimeOffset.MaxValue ? DateTimeOffset.MinValue : oldest,
            LastUsed = newest,
        };
    }

    /// <summary>One page of the log, oldest first.</summary>
    /// <param name="store">An open store.</param>
    /// <param name="skip">How many rows to pass over.</param>
    /// <param name="take">How many to take; negative means all of them.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public static IReadOnlyList<SessionLogRow> Log(SessionStore store, long skip, long take)
    {
        ArgumentNullException.ThrowIfNull(store);

        var rows = new List<SessionLogRow>();

        foreach (var row in store.Log(skip, take))
        {
            rows.Add(new SessionLogRow(
                row.Id,
                Moment(row.At),
                row.Tool,
                row.Why,
                row.Outcome,
                row.SettledAt is null ? null : Moment(row.SettledAt),
                row.Failure is null ? null : Encoding.UTF8.GetString(row.Failure)));
        }

        return rows;
    }

    /// <summary>The holder record as one <c>statements</c> row's value.</summary>
    /// <remarks>
    /// <b>The same JSON <c>browserai.lock</c> carries, on purpose.</b> The two
    /// say the same thing about the same acquisition — one for a prober that
    /// must not open a database, one for a history that outlives every holder —
    /// and a second spelling is how the two come to disagree about who had a
    /// directory.
    /// </remarks>
    /// <param name="holder">Who took it.</param>
    /// <returns>The value.</returns>
    public static string WriteHolder(LockFileHolder holder) => Encoding.UTF8.GetString(LockFile.Serialise(holder)).Trim();

    /// <summary>The holder record back out of one row's value.</summary>
    /// <remarks>
    /// <b>An unreadable holder row is a holder this build cannot name, never a
    /// record it refuses.</b> The identity is used to say *PID 1234 started by
    /// claude* in a narration; losing it costs a sentence, and refusing the
    /// whole record over it would cost the session.
    /// </remarks>
    /// <param name="value">The stored text.</param>
    /// <returns>The holder.</returns>
    private static LockFileHolder ReadHolder(string value)
    {
        try
        {
            return LockFile.Parse(Encoding.UTF8.GetBytes(value), RecordFields.Holder);
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException)
        {
            return new LockFileHolder(0, 0, null);
        }
    }
}

/// <summary>
/// What free text is allowed to carry once it is inside somebody else's
/// context.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <c>purpose</c> and a <c>why</c> are written by one model and replayed
/// into another's context, so they are a channel between agents.</b> What keeps
/// them data rather than instructions is not a length — the maintainer removed
/// every cap — it is that they cannot carry the characters a terminal, a
/// renderer or a prompt assembler acts on.
/// </para>
/// <para>
/// <b>Five rules, and the first one is the change.</b> <c>\n</c> survives,
/// because multi-line text is now allowed and a paragraph flattened into one
/// line is a different paragraph. <c>\r</c> is dropped, so a record written on
/// Windows and read anywhere does not carry a stray carriage return into a
/// renderer. Every other <c>Cc</c> becomes a space, and so do U+2028 and U+2029
/// — which <c>char.IsControl</c> cannot see, because it tests category
/// <c>Cc</c> alone and those two are <c>Zl</c> and <c>Zp</c>. Every <c>Cf</c>
/// is dropped outright: U+200B, U+202E and U+FEFF are invisible by
/// construction, so neutralising them to a space would leave a space nobody
/// typed where the honest answer is nothing. An unpaired surrogate is dropped
/// too, for the same reason: half a character is not text.
/// </para>
/// <para>
/// ⚠️ <b>It enumerates RUNES, and it iterated <c>char</c> until 2026-08-26 —
/// which meant the <c>Cf</c> rule above covered the basic plane alone.</b>
/// <c>char.GetUnicodeCategory</c> answers <c>UnicodeCategory.Surrogate</c> for
/// either half of a supplementary-plane character and never <c>Format</c>, so
/// no supplementary-plane character was ever tested. Measured that day through
/// the published binary, on one <c>browserai_init</c> purpose read back through
/// <c>browserai_catch_up</c>: U+200B, U+202E and U+FEFF were dropped as
/// documented, and <b>U+E0001, U+E0048, U+E0049 and U+1D173 came through
/// whole</b> — the second and third being the invisible text "HI" in the TAG
/// block, which is the canonical smuggling range for this class.
/// </para>
/// <para>
/// <b>It applies to <c>tool</c> as well as to <c>why</c> and <c>purpose</c>.</b>
/// A refused call records the caller's own string, and a newline in a recorded
/// tool name puts a forged line into the replay that names it.
/// </para>
/// </remarks>
internal static class RecordText
{
    /// <summary>Line separator, U+2028 — <c>Zl</c>, which <c>char.IsControl</c> does not see.</summary>
    private const char LineSeparator = '\u2028';

    /// <summary>Paragraph separator, U+2029 — <c>Zp</c>, likewise.</summary>
    private const char ParagraphSeparator = '\u2029';

    /// <summary>Cleans free text for storage and for replay.</summary>
    /// <remarks>
    /// <b><see cref="Rune.DecodeFromUtf16"/> rather than
    /// <c>MemoryExtensions.EnumerateRunes</c>.</b> The enumerator answers U+FFFD
    /// for an unpaired surrogate and also for a genuine U+FFFD somebody typed,
    /// and only one of those is text; decoding by hand is what tells them apart.
    /// </remarks>
    /// <param name="text">The text as it arrived.</param>
    /// <returns>The text as it is stored.</returns>
    public static string Sanitise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var cleaned = new StringBuilder(text.Length);
        var remaining = text.AsSpan();

        // Declared outside the loop: a stackalloc inside one is not released
        // until the method returns.
        Span<char> encoded = stackalloc char[2];

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);

            remaining = remaining[consumed..];

            // Half of a surrogate pair, which is what a truncated UTF-16 payload
            // arrives as. Dropped rather than neutralised, like a Cf.
            if (status is not OperationStatus.Done)
            {
                continue;
            }

            switch (rune.Value)
            {
                case '\n':
                    _ = cleaned.Append('\n');
                    break;

                case '\r':
                    break;

                case LineSeparator or ParagraphSeparator:
                    _ = cleaned.Append(' ');
                    break;

                default:
                    if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format)
                    {
                        break;
                    }

                    if (Rune.IsControl(rune))
                    {
                        _ = cleaned.Append(' ');
                        break;
                    }

                    _ = cleaned.Append(encoded[..rune.EncodeToUtf16(encoded)]);
                    break;
            }
        }

        return cleaned.ToString().Trim();
    }

    /// <summary>
    /// The same text with every character a renderer acts on shown as its code
    /// point instead of carried.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>For the half of this channel nothing sanitises: a refusal.</b>
    /// <see cref="Sanitise"/> guards what goes into the record; a refusal goes
    /// straight into the calling model's context — and, for a refusal at the
    /// verdict door, into the record's failure payload — and it quotes the
    /// caller's own path back. Measured 2026-08-26 through the published binary:
    /// a <c>browserai_init</c> on a path carrying U+0007 answered with a message
    /// that correctly named <c>U+0007</c> in words and then embedded the byte
    /// itself twice.
    /// </para>
    /// <para>
    /// <b>It shows rather than strips, and that is the difference from
    /// <see cref="Sanitise"/>.</b> A caller has to be able to see which
    /// character it typed was the problem, so nothing is dropped — it is
    /// rendered. <c>\n</c> is escaped here and survives there, because a refusal
    /// is one quoted sentence and a newline inside the quotes is what would let
    /// a caller's path read as the server's own lines.
    /// </para>
    /// </remarks>
    /// <param name="text">The text as it arrived.</param>
    /// <returns>The text, safe to quote into an answer.</returns>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var shown = new StringBuilder(text.Length);
        var remaining = text.AsSpan();

        Span<char> encoded = stackalloc char[2];

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);

            // An unpaired surrogate has a code point worth naming: it is what a
            // caller sees when something truncated its string.
            if (status is not OperationStatus.Done)
            {
                _ = shown.Append(CultureInfo.InvariantCulture, $"<U+{(int)remaining[0]:X4}>");
                remaining = remaining[consumed..];
                continue;
            }

            remaining = remaining[consumed..];

            if (Rune.IsControl(rune)
                || Rune.GetUnicodeCategory(rune)
                    is UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator)
            {
                // The same U+XXXX spelling `CanonicalPath`'s own clauses use, in
                // angle brackets so it reads as a substitution rather than as
                // part of the name.
                _ = shown.Append(CultureInfo.InvariantCulture, $"<U+{rune.Value:X4}>");
                continue;
            }

            _ = shown.Append(encoded[..rune.EncodeToUtf16(encoded)]);
        }

        return shown.ToString();
    }
}

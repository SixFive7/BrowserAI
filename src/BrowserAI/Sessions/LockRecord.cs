// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BrowserAI.Sessions;

/// <summary>
/// The contents of <c>lock.json</c>: who owns this session directory, what it is
/// for, and what wrote the file.
/// </summary>
/// <remarks>
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
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The longest <c>purpose</c> that is stored. Free text written by one agent
    /// and replayed into another's context is a channel between agents; the cap
    /// and the control-character strip are what keep it data.
    /// </summary>
    public const int PurposeMaximumLength = 2000;

    /// <summary>Which version of this file's schema wrote it.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// The resolved absolute path of the directory itself, as it was when this
    /// record was written.
    /// </summary>
    /// <remarks>
    /// Provenance rather than identity. Identity is where the caller is pointing
    /// right now; this answers the narrower question of where the session was
    /// when its record was last written, and comparing the two is what tells a
    /// resume whether the directory was moved or copied. That comparison is the
    /// session tools' business, not this file's.
    /// </remarks>
    public required string Directory { get; init; }

    /// <summary>The session's mode. A session cannot change what it is.</summary>
    public required string Mode { get; init; }

    /// <summary>The browser family this profile belongs to.</summary>
    public required string Browser { get; init; }

    /// <summary>What the session is for, as the agent that created it said.</summary>
    public required string Purpose { get; init; }

    /// <summary>Everything <see cref="Purpose"/> has been, oldest first.</summary>
    public required IReadOnlyList<string> PurposeHistory { get; init; }

    /// <summary>When the session directory was first locked.</summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>When this record was last written.</summary>
    public required DateTimeOffset LastUsed { get; init; }

    /// <summary>The BrowserAI build that wrote this record.</summary>
    public required string BrowserAiVersion { get; init; }

    /// <summary>Who holds, or last held, the lock.</summary>
    public required LockHolder Holder { get; init; }

    /// <summary>
    /// Value equality, <b>including</b> <see cref="PurposeHistory"/> element by
    /// element.
    /// </summary>
    /// <param name="other">The record to compare against.</param>
    /// <returns>Whether the two describe the same session state.</returns>
    /// <remarks>
    /// The compiler-generated version compares the history by reference, so two
    /// records parsed from identical bytes reported themselves unequal. That is
    /// the wrong answer in the direction that matters: a purpose-change check
    /// built on it would rewrite the file on every call and never say why.
    /// </remarks>
    public bool Equals(LockRecord? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && string.Equals(Directory, other.Directory, StringComparison.Ordinal)
        && string.Equals(Mode, other.Mode, StringComparison.Ordinal)
        && string.Equals(Browser, other.Browser, StringComparison.Ordinal)
        && string.Equals(Purpose, other.Purpose, StringComparison.Ordinal)
        && Created == other.Created
        && LastUsed == other.LastUsed
        && string.Equals(BrowserAiVersion, other.BrowserAiVersion, StringComparison.Ordinal)
        && Holder == other.Holder
        && PurposeHistory.SequenceEqual(other.PurposeHistory, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(SchemaVersion, Directory, Mode, Browser, Purpose, Created, LastUsed, Holder);

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
            writer.WriteString(LockJson.Directory, Directory);
            writer.WriteString(LockJson.Mode, Mode);
            writer.WriteString(LockJson.Browser, Browser);
            writer.WriteString(LockJson.Purpose, Purpose);

            writer.WriteStartArray(LockJson.PurposeHistory);

            foreach (var entry in PurposeHistory)
            {
                writer.WriteStringValue(entry);
            }

            writer.WriteEndArray();

            writer.WriteString(LockJson.Created, Created.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString(LockJson.LastUsed, LastUsed.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString(LockJson.BrowserAiVersion, BrowserAiVersion);

            writer.WriteStartObject(LockJson.Holder);
            writer.WriteNumber(LockJson.ProcessId, Holder.ProcessId);
            writer.WriteNumber(LockJson.ProcessCreatedFileTime, Holder.ProcessCreatedFileTime);

            if (Holder.ClientProcessName is { } client)
            {
                writer.WriteString(LockJson.ClientProcessName, client);
            }
            else
            {
                writer.WriteNull(LockJson.ClientProcessName);
            }

            writer.WriteEndObject();
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
    /// The file carries an unknown key, is missing a required one, was written
    /// by a different schema version, or is not JSON at all.
    /// </exception>
    public static LockRecord Read(ReadOnlySpan<byte> utf8, string path)
    {
        int? schemaVersion = null;
        string? directory = null;
        string? mode = null;
        string? browser = null;
        string? purpose = null;
        List<string>? purposeHistory = null;
        DateTimeOffset? created = null;
        DateTimeOffset? lastUsed = null;
        string? version = null;
        LockHolder? holder = null;

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
                        directory = ReadString(ref reader, name, path);
                        break;
                    case LockJson.Mode:
                        mode = ReadString(ref reader, name, path);
                        break;
                    case LockJson.Browser:
                        browser = ReadString(ref reader, name, path);
                        break;
                    case LockJson.Purpose:
                        purpose = ReadString(ref reader, name, path);
                        break;
                    case LockJson.PurposeHistory:
                        purposeHistory = ReadStringArray(ref reader, name, path);
                        break;
                    case LockJson.Created:
                        created = ReadTimestamp(ref reader, name, path);
                        break;
                    case LockJson.LastUsed:
                        lastUsed = ReadTimestamp(ref reader, name, path);
                        break;
                    case LockJson.BrowserAiVersion:
                        version = ReadString(ref reader, name, path);
                        break;
                    case LockJson.Holder:
                        holder = ReadHolder(ref reader, path);
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
            throw new LockFileException(
                schemaVersion is null
                    ? $"'{path}' has no '{LockJson.SchemaVersion}', so nothing in it can be trusted to mean what this build thinks it means. {RecoveryAdvice(path)}"
                    : $"'{path}' was written with schema version {schemaVersion.Value.ToString(CultureInfo.InvariantCulture)}; this build of BrowserAI reads version {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)} only. A newer BrowserAI wrote this session. Use that build, or delete the session directory and create a new one. {RepeatingFails}");
        }

        return new LockRecord
        {
            SchemaVersion = schemaVersion.Value,
            Directory = directory ?? throw Missing(LockJson.Directory, path),
            Mode = mode ?? throw Missing(LockJson.Mode, path),
            Browser = browser ?? throw Missing(LockJson.Browser, path),
            Purpose = purpose ?? throw Missing(LockJson.Purpose, path),
            PurposeHistory = purposeHistory ?? throw Missing(LockJson.PurposeHistory, path),
            Created = created ?? throw Missing(LockJson.Created, path),
            LastUsed = lastUsed ?? throw Missing(LockJson.LastUsed, path),
            BrowserAiVersion = version ?? throw Missing(LockJson.BrowserAiVersion, path),
            Holder = holder ?? throw Missing(LockJson.Holder, path),
        };
    }

    /// <summary>
    /// The sentence every refusal ends with. Naming a recovery that <i>is</i> the
    /// call that just failed is the same as naming none.
    /// </summary>
    private const string RepeatingFails = "Repeating the call that just failed will fail identically.";

    private static string RecoveryAdvice(string path) =>
        $"Recovery: open '{path}' and remove what does not belong, or delete that file and create the session again — a session directory whose lock file is gone is a new session, not a broken one. {RepeatingFails}";

    private static LockFileException Unrecognised(string name, string path) =>
        new($"'{path}' carries a key BrowserAI does not recognise: '{name}'. Our own files are parsed strictly, because an unrecognised key is indistinguishable from a missing one under lenient parsing — the file would be reported as understood while a field nobody honoured decided nothing. {RecoveryAdvice(path)}");

    private static LockFileException Missing(string name, string path) =>
        new($"'{path}' has no '{name}', so it does not describe a session this build can act on. {RecoveryAdvice(path)}");

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

    private static List<string> ReadStringArray(ref Utf8JsonReader reader, string name, string path)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartArray)
        {
            throw Wrong(name, "an array of strings", path);
        }

        var values = new List<string>();

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
        {
            values.Add(reader.TokenType is JsonTokenType.String
                ? reader.GetString()!
                : throw Wrong(name, "an array of strings", path));
        }

        return values;
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

    private static LockHolder ReadHolder(ref Utf8JsonReader reader, string path)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            throw Wrong(LockJson.Holder, "an object", path);
        }

        int? processId = null;
        long? createdFileTime = null;
        var client = default(string);
        var sawClient = false;

        while (ReadPropertyName(ref reader, path) is { } name)
        {
            switch (name)
            {
                case LockJson.ProcessId:
                    processId = ReadInt32(ref reader, name, path);
                    break;
                case LockJson.ProcessCreatedFileTime:
                    createdFileTime = ReadInt64(ref reader, name, path);
                    break;
                case LockJson.ClientProcessName:
                    client = ReadNullableString(ref reader, name, path);
                    sawClient = true;
                    break;
                default:
                    throw Unrecognised($"{LockJson.Holder}.{name}", path);
            }
        }

        return new LockHolder
        {
            ProcessId = processId ?? throw Missing($"{LockJson.Holder}.{LockJson.ProcessId}", path),
            ProcessCreatedFileTime = createdFileTime ?? throw Missing($"{LockJson.Holder}.{LockJson.ProcessCreatedFileTime}", path),
            ClientProcessName = sawClient ? client : throw Missing($"{LockJson.Holder}.{LockJson.ClientProcessName}", path),
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

/// <summary>The key names in <c>lock.json</c>, spelled once.</summary>
/// <remarks>
/// Strict parsing means a typo in a key name here becomes a refusal at runtime
/// rather than a field that silently reads as absent, so the writer and the
/// reader take their names from the same constants.
/// </remarks>
internal static class LockJson
{
    /// <summary>Which version of the schema wrote the file.</summary>
    public const string SchemaVersion = "schemaVersion";

    /// <summary>The resolved absolute path recorded at write time.</summary>
    public const string Directory = "directory";

    /// <summary>The session's mode.</summary>
    public const string Mode = "mode";

    /// <summary>The browser family.</summary>
    public const string Browser = "browser";

    /// <summary>What the session is for.</summary>
    public const string Purpose = "purpose";

    /// <summary>Everything the purpose has been.</summary>
    public const string PurposeHistory = "purposeHistory";

    /// <summary>When the directory was first locked.</summary>
    public const string Created = "created";

    /// <summary>When the record was last written.</summary>
    public const string LastUsed = "lastUsed";

    /// <summary>The build that wrote the record.</summary>
    public const string BrowserAiVersion = "browserAiVersion";

    /// <summary>The holder object.</summary>
    public const string Holder = "holder";

    /// <summary>The holder's pid.</summary>
    public const string ProcessId = "processId";

    /// <summary>The holder's creation time, as a FILETIME.</summary>
    public const string ProcessCreatedFileTime = "processCreatedFileTime";

    /// <summary>The client that started the holder.</summary>
    public const string ClientProcessName = "clientProcessName";
}

/// <summary>
/// A <c>lock.json</c> that cannot be acted on, with a recovery in its message.
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

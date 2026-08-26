// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Text.Json;
using BrowserAI.Interop;
using BrowserAI.Sessions;

namespace BrowserAI.Storage;

/// <summary>
/// <c>browserai.lock</c> — the whole of who owns a session directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>The handle is the lock, and this file carries nothing else.</b> It is
/// held <c>FileAccess.ReadWrite, FileShare.Read</c> for the session's life: a
/// second BrowserAI asking for write access is refused by the kernel, any
/// reader is admitted, and the OS releases it when the holder dies, however it
/// dies. No registry, no token, no expiry, no heartbeat — *stale* and *alive*
/// are distinguishable without guessing because the kernel already knows.
/// </para>
/// <para>
/// <b>Six properties, and this type plus <see cref="SessionStore"/> is how each
/// one survives the move to SQLite.</b> One writer per directory, from the
/// share mode. Readers proceed <i>and see live data</i> — they read the store,
/// which is a different file, so the guard never has to admit them to itself.
/// Held for the whole session. Released by the OS on death. Observable in one
/// <c>CreateFile</c>. And it names the holder, because that is what is written
/// inside it.
/// </para>
/// <para>
/// ⚠️ <b>Written ONCE, at acquisition, and never again — which is what makes
/// an ABSENT lock file mean *free* here where it could not before.</b> The
/// record this replaces was rewritten on every forwarded call, so its name was
/// unbound for a few milliseconds each time and a probe that landed there had
/// to answer *undetermined*: an absence was a record being replaced as often as
/// it was a session that had gone. Nothing rewrites this file, so the only
/// window left is between the rename and the first hold at acquisition, and
/// that one is inside the per-directory gate every acquirer takes. What a
/// reporting caller can still see in it is *free* about a directory somebody
/// is in the middle of taking — a momentary truth that corrects itself, rather
/// than a stale one that does not.
/// </para>
/// <para>
/// <b>The writer is the lock holder, and nothing here enforces that.</b> It is
/// an application-level invariant kept by the code path that reaches this type.
/// Adversarial and hostile-caller defence is an explicit non-goal of this
/// product: the premise is that the model tries to behave, and what is being
/// steered is honest mistakes.
/// </para>
/// </remarks>
internal static class LockFile
{
    /// <summary>The lock file's name inside a session directory.</summary>
    public const string FileName = "browserai.lock";

    /// <summary>
    /// What a temporary lock file being renamed into place is called.
    /// </summary>
    /// <remarks>
    /// A pattern rather than a name, so that a sweep looking for the residue of
    /// an interrupted acquisition has something to match. It shares the prefix
    /// deliberately: a stray beside <see cref="FileName"/> reads as what it is.
    /// </remarks>
    public const string TemporaryFilePattern = $"{FileName}.new-*";

    /// <summary>The <c>processId</c> property of the holder record.</summary>
    private const string ProcessIdProperty = "processId";

    /// <summary>The <c>processCreatedFileTime</c> property of the holder record.</summary>
    private const string ProcessCreatedFileTimeProperty = "processCreatedFileTime";

    /// <summary>The <c>clientProcessName</c> property of the holder record.</summary>
    private const string ClientProcessNameProperty = "clientProcessName";

    /// <summary>
    /// Writes the holder record and takes the directory, in that order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Temporary file, durable write, rename, then hold.</b> The temporary
    /// file is in the target's own directory because a rename is atomic only
    /// within one volume and cheap only within one directory. The write is
    /// <c>WriteThrough</c> <i>and</i> flushed to disk, because a plain write
    /// returns once the bytes are in the filesystem cache and this is the one
    /// file whose loss cannot be reconstructed from anything else in the
    /// directory.
    /// </para>
    /// <para>
    /// <b>The rename is the only one this file will ever see.</b> Everything
    /// the session goes on to say about itself goes into
    /// <see cref="SessionStore"/>, so the window a rename opens is paid once per
    /// acquisition rather than once per call.
    /// </para>
    /// <para>
    /// ⚠️ <b>The two halves are also callable separately, and one caller does
    /// that on purpose.</b> Acquisition has to be able to say <i>the guard WAS
    /// written and could not then be held</i>, which is a different sentence
    /// from <i>nothing was changed</i> — and one of them is false at the moment
    /// it is said if the two failures share a <c>catch</c>. That exact
    /// conflation shipped once, in the record this file replaces.
    /// </para>
    /// </remarks>
    /// <param name="path">The lock file.</param>
    /// <param name="holder">Who is taking it.</param>
    /// <returns>The hold, which the caller keeps for the session's life.</returns>
    /// <exception cref="IOException">The write, the rename or the hold failed.</exception>
    public static LockFileHold TakeAndWrite(string path, LockFileHolder holder)
    {
        Write(path, holder);

        // ⚠️ WAITED OUT RATHER THAN BELIEVED, AND ONLY HERE. The caller holds
        // the per-directory gate and the file on disk names this process, so no
        // second owner can exist -- becoming one means passing through the gate.
        // A sharing violation on this line is therefore somebody's transient
        // probe handle, which is what `browserai_list` opens for the length of
        // one `FileStream` construction and cannot stop opening: detecting an
        // owner and blocking one are the same capability. Believing it once cost
        // a lock (CI run 32203064556 attempt 1, against the record this file
        // replaces).
        return RenameWindow.WaitOutWhereNoOwnerIsPossible(() => Hold(path));
    }

    /// <summary>
    /// Writes the holder record into place, durably, and takes nothing.
    /// </summary>
    /// <param name="path">The lock file.</param>
    /// <param name="holder">Who is taking it.</param>
    /// <exception cref="IOException">The write or the rename failed; nothing was changed.</exception>
    public static void Write(string path, LockFileHolder holder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(holder);

        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException($"'{path}' has no directory to write into.");

        var temporary = Path.Combine(directory, $"{FileName}.new-{Guid.NewGuid():N}");

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                stream.Write(Serialise(holder));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    /// The holder's open itself, written once so that no second caller can come
    /// to hold the file in a different mode.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>FileShare.Read</c> is the entire mechanism and is not
    /// negotiable.</b> It admits every reader and refuses every writer, which is
    /// exactly the predicate this design needs and exactly the one no database
    /// engine offers: SQLite's own <c>xOpen</c> asks for
    /// <c>FILE_SHARE_READ | FILE_SHARE_WRITE</c> or for nothing at all, so a
    /// probe would read a driven session as free, or a reader would be locked
    /// out of the record it came for.
    /// </remarks>
    /// <param name="path">The lock file.</param>
    /// <returns>The hold.</returns>
    /// <exception cref="IOException">Somebody else holds it, or it is not there.</exception>
    public static LockFileHold Hold(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1), path);

    /// <summary>
    /// Whether anything holds a session directory at the instant of the look,
    /// without taking anything and without opening a process handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three answers, and none of them is a guess.</b>
    /// <see cref="LockFileState.Held"/> is the kernel's own sharing violation.
    /// <see cref="LockFileState.Released"/> is a lock file that opens, which
    /// means it names a holder that is no longer holding — the shape a killed
    /// session leaves. <see cref="LockFileState.Free"/> is no lock file at all,
    /// which means the directory has never been taken or was destroyed.
    /// </para>
    /// <para>
    /// ⚠️ <b>The access must be <c>ReadWrite</c> or this cannot detect an owner
    /// at all</b>, and that is also why it cannot be made harmless: a handle
    /// whose granted access is outside <c>Read</c> is precisely what an open
    /// sharing only <c>Read</c> is refused by, so for the instant this handle
    /// lives it would refuse a holder's own re-open. Detecting an owner and
    /// blocking one are the same capability. The share mode is wider than a
    /// holder's on purpose — this handle lets go immediately, and one without
    /// <c>Delete</c> would refuse a concurrent destroy for as long as it lived.
    /// </para>
    /// <para>
    /// <b>Anything else is <see cref="LockFileState.Undetermined"/> and carries
    /// a reason.</b> A denied open, a device error, a path that is not a file:
    /// collapsing any of them into *free* would hand a caller a confident wrong
    /// answer in the direction that costs somebody else's session.
    /// </para>
    /// <para>
    /// <b>Cost: one <c>CreateFile</c> and one <c>CloseHandle</c></b>, with no
    /// directory walk, no process open, no mutex and no database. That is what
    /// makes it affordable over a whole tree of sessions, which is what
    /// <c>browserai_list</c> does.
    /// </para>
    /// </remarks>
    /// <param name="path">The lock file.</param>
    /// <returns>The state, and a reason whenever it is not settled.</returns>
    public static LockFileAnswer Probe(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            using var probe = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1);

            return new LockFileAnswer(LockFileState.Released, null);
        }
        catch (IOException failure) when (RenameWindow.IsSharingViolation(failure))
        {
            return new LockFileAnswer(LockFileState.Held, null);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // Free, and this is the half that could not be said before. The
            // file is written once and never renamed again, so an absence is an
            // absence rather than a record mid-replacement.
            return new LockFileAnswer(LockFileState.Free, null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new LockFileAnswer(
                LockFileState.Undetermined,
                $"'{path}' could not be opened, and the failure was not a sharing violation ({failure.Message}).");
        }
    }

    /// <summary>
    /// Reads the holder record, from outside, while a holder may have it open.
    /// </summary>
    /// <remarks>
    /// <b>The share mode is what makes this work against a live holder.</b> A
    /// holder's granted access is <c>ReadWrite</c>, so a reader that did not
    /// share write would be refused outright — which would turn *somebody owns
    /// this* into *this file is unreadable*, at exactly the moment somebody
    /// wants to know who.
    /// </remarks>
    /// <param name="path">The lock file.</param>
    /// <returns>The holder, or <see langword="null"/> when there is no lock file.</returns>
    /// <exception cref="InvalidDataException">There is a lock file and it is not one of ours.</exception>
    public static LockFileHolder? Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
            using var buffer = new MemoryStream();

            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        return bytes.Length is 0 ? null : Parse(bytes, path);
    }

    /// <summary>The holder record's bytes: UTF-8, LF, no BOM.</summary>
    /// <remarks>
    /// <b>Indented and newline-terminated, because a person opens this file.</b>
    /// It is roughly a hundred bytes and it is the one thing in the directory
    /// that answers *who has this* without a tool. The newline is spelled
    /// explicitly rather than left to <c>Environment.NewLine</c>, which is what
    /// silently made the record this replaces CRLF.
    /// </remarks>
    /// <param name="holder">Who is taking the directory.</param>
    /// <returns>The bytes.</returns>
    /// <remarks>
    /// ⚠️ <b>Internal because the record's own <c>holder</c> history uses the
    /// same bytes.</b> The lock file says who has the directory now and the
    /// history says who has ever had it; one spelling for both is what stops
    /// the two disagreeing about an acquisition they both describe.
    /// </remarks>
    internal static byte[] Serialise(LockFileHolder holder)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(ProcessIdProperty, holder.ProcessId);
            writer.WriteNumber(ProcessCreatedFileTimeProperty, holder.ProcessCreatedFileTime);

            if (holder.ClientProcessName is { } client)
            {
                writer.WriteString(ClientProcessNameProperty, client);
            }
            else
            {
                writer.WriteNull(ClientProcessNameProperty);
            }

            writer.WriteEndObject();
        }

        return [.. buffer.WrittenSpan, (byte)'\n'];
    }

    /// <summary>Reads the holder record strictly.</summary>
    /// <remarks>
    /// <b>An unknown key is a refusal rather than a field dropped in
    /// silence.</b> The set of things a lock file may say is closed, and a file
    /// carrying something else is somebody else's file — which is a different
    /// answer from *this directory is free* and has to stay one.
    /// </remarks>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="path">Where they came from, for the message.</param>
    /// <returns>The holder.</returns>
    /// <exception cref="InvalidDataException">It is not one of ours.</exception>
    internal static LockFileHolder Parse(byte[] bytes, string path)
    {
        int? processId = null;
        long? createdFileTime = null;
        string? client = null;
        var sawClient = false;

        try
        {
            var reader = new Utf8JsonReader(bytes);

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                throw Damaged(path, "it does not begin with a JSON object");
            }

            while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
            {
                var name = reader.GetString();

                _ = reader.Read();

                switch (name)
                {
                    case ProcessIdProperty:
                        processId = reader.GetInt32();
                        break;

                    case ProcessCreatedFileTimeProperty:
                        createdFileTime = reader.GetInt64();
                        break;

                    case ClientProcessNameProperty:
                        client = reader.TokenType is JsonTokenType.Null ? null : reader.GetString();
                        sawClient = true;
                        break;

                    default:
                        throw Damaged(path, $"it carries a property BrowserAI does not write, '{name}'");
                }
            }
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or FormatException)
        {
            throw Damaged(path, $"it is not the JSON BrowserAI writes ({failure.Message})");
        }

        if (processId is not { } pid)
        {
            throw Damaged(path, $"it does not say '{ProcessIdProperty}'");
        }

        if (createdFileTime is not { } created)
        {
            // ⚠️ Refused rather than defaulted, and the reason is the rule this
            // whole record exists for: a pid alone is not an identity, because
            // Windows reuses pids within seconds. A lock file naming only a pid
            // would let a reclaim take a live stranger's directory.
            throw Damaged(path, $"it does not say '{ProcessCreatedFileTimeProperty}', and a pid on its own is not an identity");
        }

        if (!sawClient)
        {
            throw Damaged(path, $"it does not say '{ClientProcessNameProperty}'");
        }

        return new LockFileHolder(pid, created, client);
    }

    /// <summary>The refusal for a lock file this build did not write.</summary>
    /// <param name="path">The file.</param>
    /// <param name="because">What is wrong with it, as a clause.</param>
    /// <returns>The exception, for the caller to throw.</returns>
    private static InvalidDataException Damaged(string path, string because) =>
        new($"'{path}' is not a BrowserAI lock file: {because}. BrowserAI will not guess at ownership of a directory it cannot read the guard of.");

    /// <summary>Removes a temporary file, and does not care whether it was there.</summary>
    /// <param name="path">The file.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The rename already took it, or something else holds it. Neither
            // is a reason to fail an acquisition that succeeded.
        }
    }
}

/// <summary>What a <see cref="LockFile.Probe"/> found.</summary>
internal enum LockFileState
{
    /// <summary>
    /// There is no lock file, so nothing has this directory and nothing ever
    /// released it.
    /// </summary>
    Free,

    /// <summary>
    /// A live holder has it: the open was refused with a sharing violation,
    /// which is the kernel's own answer and the only one read positively.
    /// </summary>
    Held,

    /// <summary>
    /// There is a lock file and it opened, so it names a holder that is no
    /// longer holding — the shape a killed or finished session leaves behind.
    /// </summary>
    Released,

    /// <summary>
    /// The question could not be answered, and
    /// <see cref="LockFileAnswer.Why"/> says what stopped it. Never read as
    /// free.
    /// </summary>
    Undetermined,
}

/// <summary>
/// What <see cref="LockFile.Probe"/> found, and why when it found nothing.
/// </summary>
/// <param name="State">The state.</param>
/// <param name="Why">
/// The reason, for <see cref="LockFileState.Undetermined"/> and never
/// otherwise.
/// </param>
internal readonly record struct LockFileAnswer(LockFileState State, string? Why);

/// <summary>
/// Who holds a session directory.
/// </summary>
/// <param name="ProcessId">The holder's process.</param>
/// <param name="ProcessCreatedFileTime">
/// Its creation time as a Windows FILETIME. Together with
/// <paramref name="ProcessId"/> this is the identity; the pid alone is not,
/// because Windows reuses pids.
/// </param>
/// <param name="ClientProcessName">
/// Which MCP client started the holder, or <see langword="null"/> when it could
/// not be read. <b>Display only</b> — nothing in BrowserAI ever chooses, counts
/// or terminates a process by this value.
/// </param>
internal sealed record LockFileHolder(int ProcessId, long ProcessCreatedFileTime, string? ClientProcessName)
{
    /// <summary>This process, as a holder record.</summary>
    /// <returns>The holder.</returns>
    public static LockFileHolder ForThisProcess() =>
        new(Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess(), ProcessLiveness.ClientProcessName());

    /// <summary>
    /// Whether the process this record names is still the process it named.
    /// </summary>
    /// <remarks>
    /// <b>A second opinion, and never the first one.</b> The kernel's share
    /// mode is what says whether the directory is held; this says whether the
    /// pid in the file is still the same process. They can disagree — a holder
    /// that has just exited, a record left by a killed session — and when they
    /// do, the share mode is the answer and this is the explanation.
    /// </remarks>
    /// <returns>Whether it is alive.</returns>
    public bool IsAlive() => ProcessLiveness.IsAlive(ProcessId, ProcessCreatedFileTime);
}

/// <summary>
/// A held <c>browserai.lock</c>: the session's ownership of its directory, for
/// as long as this object lives.
/// </summary>
/// <remarks>
/// <b>Nothing writes through this handle and nothing ever will.</b> It is
/// opened <c>ReadWrite</c> because <c>FileShare.Read</c> only refuses a peer
/// whose <i>granted</i> access is outside <c>Read</c>, and the granted access
/// is what a peer's own open is measured against. The write capability is the
/// mechanism; using it is not.
/// </remarks>
internal sealed class LockFileHold : IDisposable
{
    private readonly FileStream _stream;

    /// <summary>Takes ownership of an opened lock file.</summary>
    /// <param name="stream">The held handle.</param>
    /// <param name="path">Where it is.</param>
    internal LockFileHold(FileStream stream, string path)
    {
        _stream = stream;
        Path = path;
    }

    /// <summary>The lock file this hold is on.</summary>
    public string Path { get; }

    /// <summary>Whether the handle is still open.</summary>
    /// <remarks>
    /// Read off the stream rather than tracked in a field of its own, so that
    /// *is this session still holding its directory* has one answer and not two
    /// that can drift.
    /// </remarks>
    public bool IsHeld => _stream.SafeFileHandle is { IsClosed: false, IsInvalid: false };

    /// <summary>What the lock file says, read through the handle already open.</summary>
    /// <remarks>
    /// <b>Through this handle rather than through a second open</b>, so that a
    /// holder asking who it is cannot be answered by a file somebody replaced
    /// underneath it, and so that the read is not subject to the sharing rules
    /// a peer's read is subject to. The position is reset first because the
    /// handle is the session's and its position belongs to nobody in
    /// particular.
    /// </remarks>
    /// <returns>The holder, or <see langword="null"/> for an empty file.</returns>
    /// <exception cref="InvalidDataException">The file is not one of ours.</exception>
    public LockFileHolder? ReadHolder()
    {
        _stream.Position = 0;

        using var buffer = new MemoryStream();

        _stream.CopyTo(buffer);

        return buffer.Length is 0 ? null : LockFile.Parse(buffer.ToArray(), Path);
    }

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Logging;

/// <summary>
/// The process log's sink: one file per day shared by every BrowserAI on the
/// machine, append-only, written under a cross-process gate, flushed per record,
/// and incapable of becoming the outage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Append, never truncate.</b> With ~100 concurrent BrowserAI processes a
/// start is the most common event there is, so a sink that truncates on start
/// has deleted the previous crash before anyone looks at it.
/// </para>
/// <para>
/// <b>One shared file under a lock, which is the maintainer's decision taken
/// 2026-08-24 over a recommendation of one file per process:</b> <i>"Simple to
/// read, simple to write."</i> Per-process files are not to be revived. What the
/// gate buys is stated where it is taken, in <see cref="NativeFile.TakeGate"/>:
/// the length is read, the instant is stamped and the bytes go down inside one
/// claim, so nothing interleaves and the file is sorted by construction.
/// </para>
/// <para>
/// <b>Flushed per record, synchronously.</b> An asynchronous sink plus an
/// abnormal exit drops precisely the line that explains the exit, because the
/// queued record dies with the queue. Owning the sink is also why the
/// framework's console provider is taken for one setting and not for its
/// durability: whether it drains at process exit is unverified, and an
/// unverified flush is the same class of assumption as an unread exit code.
/// </para>
/// <para>
/// <b>Every failure is swallowed.</b> Local disk only, no network path, no
/// dialog, no retry loop. A log write that fails is dropped, because logging
/// must never be able to become the outage — a <c>\\host\share</c> that is not
/// answering blocks a file call for 21 measured seconds, and a log write is not
/// where anyone should discover that.
/// </para>
/// <para>
/// ⚠️ <b>The file cannot be deleted while any BrowserAI is running, and that is
/// the machine's log rather than one process's own.</b>
/// <see cref="NativeFile.OpenForLockedAppend"/> is asked for no delete sharing
/// here, which closes
/// [finding 10](../../../docs/reviews/2026-08-18-adversarial-processes.md) — with
/// it granted, anything could unlink the live log and every subsequent write
/// succeeded into an unlinked file object while <see cref="CurrentFile"/> went
/// on naming a path that no longer existed, and nothing failed. The cost is
/// accepted knowingly: while one BrowserAI holds today's file, <b>nobody</b> can
/// remove it — not the user, not an installer, and not this type's own
/// <see cref="SweepExpired"/>, which is why that pass tolerates a refusal
/// instead of reporting one.
/// </para>
/// </remarks>
internal sealed class RollingFileWriter : ILogSink, IDisposable
{
    /// <summary>
    /// Roll to the next indexed file before a record would take it past this
    /// size. Small enough that a reader can open one in an editor, large enough
    /// that a busy day is a handful of files rather than hundreds.
    /// </summary>
    private const long MaxBytesPerFile = 8L * 1024 * 1024;

    /// <summary>
    /// How long a rolled file is kept. The number is ours rather than measured;
    /// what matters is that it is enforced somewhere that outlives an update,
    /// which is the half a shipped product's identical policy never had — its
    /// logs sat inside the directory each update replaced wholesale, so the
    /// retention window could never once have been reached.
    /// </summary>
    private const int RetentionDays = 30;

    private const string FilePrefix = "browserai-";
    private const string FileSuffix = ".log";

    private readonly Lock _gate = new();
    private readonly string _directory;

    private SafeFileHandle? _handle;
    private string _currentDay = string.Empty;
    private int _currentIndex;
    private bool _disabled;

    /// <summary>Creates the writer and sweeps expired files, both best-effort.</summary>
    /// <param name="directory">Where the log files live.</param>
    public RollingFileWriter(string directory)
    {
        _directory = directory;

        // The UNC refusal, and it has to happen HERE rather than at the first
        // write. `SweepExpired()` on the next line enumerates the directory, so
        // a `\\host\share` that is not answering would block the constructor --
        // which runs on the startup path, before anything is serving -- for the
        // 21 seconds a dead share costs a single file call. That is logging
        // becoming the outage, on the one path where nothing is yet running to
        // report it.
        if (VolumeIdentity.IsUncOrDeviceSpelling(directory))
        {
            RefusedNetworkDirectory = true;
            _disabled = true;
            return;
        }

        SweepExpired();
    }

    /// <summary>The file currently being appended to, or null if none is open.</summary>
    public string? CurrentFile { get; private set; }

    /// <summary>
    /// Whether this writer refused its directory outright for being a network
    /// path, as opposed to failing a write against a local one.
    /// </summary>
    /// <remarks>
    /// <b>A distinct fact rather than an absent <see cref="CurrentFile"/>.</b>
    /// "Nothing is open" is also true of a writer that has not been written to
    /// yet and of one whose last write failed, and the three want different
    /// answers from anyone looking: the first is normal, the second is a disk
    /// problem, and this one is a configuration nobody should be in. Records
    /// still reach stderr through the console provider, so this degrades the
    /// process log rather than silencing the process.
    /// </remarks>
    public bool RefusedNetworkDirectory { get; }

    /// <inheritdoc />
    /// <remarks>There is no buffer, so a record that has been written is already durable.</remarks>
    public void Write(string record)
    {
        lock (_gate)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                // The loop turns over at most once per roll: the only `continue`
                // is the one that closes a full file and opens the next index.
                while (true)
                {
                    var handle = EnsureOpen();

                    if (TryAppend(handle, record))
                    {
                        return;
                    }

                    Close();
                    _currentIndex++;
                }
            }
#pragma warning disable CA1031 // Logging never propagates a failure -- see the type's remarks.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Drop the record and keep the process alive. A sink that
                // throws upward turns a diagnostic into the failure it was
                // meant to describe. The handle is closed so the next write
                // re-opens rather than reusing one that has just failed: a
                // retry that repeats the failed call is not a recovery. Closing
                // is also what releases the write gate if the failure was the
                // release itself, which is the one failure that would otherwise
                // stall every BrowserAI on the machine.
                Close();
            }
        }
    }

    // There is deliberately no Flush(). Nothing is buffered anywhere: every
    // record is one unbuffered write against a synchronous handle. That is
    // what makes "a deliberately unhandled exception still leaves its last log
    // line on disk" a property of the design rather than of the timing, and a
    // no-op method named Flush would be a mechanism that only looks like one.

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disabled = true;
            Close();
        }
    }

    // WHY THE TEST ABOVE IS THE SPELLING TEST AND NOT THE WHOLE ANSWER.
    //
    // Corrected 2026-08-19. This file used to carry its own IsNetworkPath and a
    // paragraph explaining that a mapped drive letter was NOT caught, because
    // "telling the difference needs GetDriveType -- a filesystem call, which on
    // a disconnected mapping can block for exactly as long as the thing being
    // avoided." That last clause was reasoning, not a measurement, and it was
    // wrong: GetDriveTypeW answered DRIVE_REMOTE in 0.9 ms against a letter
    // mapped to a dead hostname, measured immediately after a File.Exists on
    // that same letter had taken 22 s (kb/windows/detection.md). The whole
    // question now lives in Interop/VolumeIdentity, where the two halves --
    // characters, then the object manager -- sit together.
    //
    // THIS SITE STILL USES ONLY THE SPELLING HALF, deliberately. The directory
    // here is the PROCESS log's, derived from the install location rather than
    // supplied by a caller, and this constructor runs on the startup path before
    // anything is serving. The spelling test is what a value of unknown
    // provenance needs; VolumeIdentity.Of would add a syscall on every start to
    // answer a question nobody has ever been able to pose here. The
    // caller-supplied path is the SESSION directory, and that one goes through
    // Sessions/SessionDirectoryGuard, which asks both halves.

    /// <summary>
    /// Writes one record into the open file, or answers <see langword="false"/>
    /// when that file is full and the caller must roll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-24 (previously "The starting size is read once.
    /// It drifts under concurrency, which only means the roll happens at
    /// approximately the cap rather than exactly at it — and paying a metadata
    /// query per record to fix that would be a worse trade").</b> The roll now
    /// happens <b>exactly</b> at the cap: the length is the file's own, read
    /// through the open handle <i>inside the write gate</i>, so no other
    /// process can have appended between the read and the decision and there is
    /// no per-process counter left to drift. It is not a metadata query — the
    /// handle is already open — and no file in the directory ever exceeds
    /// <see cref="MaxBytesPerFile"/>.
    /// </para>
    /// <para>
    /// <b>Except for a record that is bigger than the cap on its own</b>, which
    /// is written rather than dropped and lands alone in its own file: the
    /// <c>length is 0</c> arm is what stops that record rolling forever without
    /// ever being written. A log may not lose a record for being long.
    /// </para>
    /// <para>
    /// <b>The instant is stamped here, in the gate, one statement before the
    /// bytes go down</b> — see <see cref="FileLoggerProvider.WriteStamp"/> for
    /// why that is the whole answer to keeping the file sorted.
    /// </para>
    /// </remarks>
    /// <param name="handle">The open log file.</param>
    /// <param name="record">The formatted record, without its line terminator.</param>
    /// <returns>Whether the record was written.</returns>
    private static bool TryAppend(SafeFileHandle handle, string record)
    {
        using var claim = NativeFile.TakeGate(handle);

        var length = RandomAccess.GetLength(handle);

        // LF rather than CRLF, to match every other file this repository
        // writes. The protocol channel's line ending is StdioChannel's
        // business; this one is only about not surprising a reader.
        var bytes = Encoding.UTF8.GetBytes(FileLoggerProvider.WriteStamp(DateTime.UtcNow) + record + "\n");

        if (length > 0 && length + bytes.Length > MaxBytesPerFile)
        {
            return false;
        }

        RandomAccess.Write(handle, bytes, length);
        return true;
    }

    private SafeFileHandle EnsureOpen()
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        if (_handle is not null && _currentDay == today)
        {
            return _handle;
        }

        if (_currentDay != today)
        {
            Close();
            _currentDay = today;
            _currentIndex = 0;
        }

        _ = Directory.CreateDirectory(_directory);

        // Skip past any file another process has already filled. This is the
        // cheap starting point and not the decision: TryAppend re-reads the
        // length under the gate, so a file that fills between this scan and the
        // first write still rolls exactly.
        while (true)
        {
            var candidate = Path.Combine(_directory, $"{FilePrefix}{_currentDay}-{_currentIndex:D3}{FileSuffix}");
            var existing = new FileInfo(candidate);

            if (existing.Exists && existing.Length >= MaxBytesPerFile)
            {
                _currentIndex++;
                continue;
            }

            // Shared with every other BrowserAI process on the machine for
            // reading and writing, and with nothing at all for deleting. See
            // NativeFile: FileMode.Append loses records here, and it loses them
            // silently.
            _handle = NativeFile.OpenForLockedAppend(candidate, shareDelete: false);
            CurrentFile = candidate;
            return _handle;
        }
    }

    private void SweepExpired()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

            foreach (var file in Directory.EnumerateFiles(_directory, $"{FilePrefix}*{FileSuffix}"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    // A file another BrowserAI still holds open cannot be
                    // deleted at all now that delete sharing is withheld, and
                    // that refusal lands in the catch below rather than
                    // anywhere a reader sees it. It is the accepted half of
                    // finding 10 and not a fault: a file old enough to sweep
                    // and still open belongs to a process whose clock or
                    // backup put it there, and the next start sweeps it.
                    File.Delete(file);
                }
            }
        }
#pragma warning disable CA1031 // Retention is best-effort; a file another process holds is skipped, never retried.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Nothing to do and nowhere to say it: this runs before the logger
            // the caller is trying to build.
        }
    }

    private void Close()
    {
        try
        {
            _handle?.Dispose();
        }
#pragma warning disable CA1031 // See Write.
        catch (Exception)
#pragma warning restore CA1031
        {
            // A handle that will not close is abandoned rather than retried.
        }
        finally
        {
            _handle = null;
            CurrentFile = null;
        }
    }
}

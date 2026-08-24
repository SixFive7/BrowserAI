// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Interop;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Logging;

/// <summary>
/// One session's own log: <c>&lt;session-dir&gt;\browserai.log</c>, beside its
/// <c>browserai.json</c>, and <b>the only file that session's records go to</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not roll, and that is the difference from the process log.</b> The
/// process log is written by every BrowserAI on the machine and has to be bounded
/// by day and by size; a session's log belongs to the directory the caller named,
/// is deleted with it by <c>browserai_destroy</c>, and is the file a person opens
/// when <i>that</i> session misbehaved. Splitting it into indexed parts would
/// scatter the one conversation it exists to hold.
/// </para>
/// <para>
/// <b>Written under the same cross-process gate the process log uses</b>, for the
/// same reason and at the same two-syscall cost: a session is resumable forever
/// and may be driven by a succession of BrowserAI processes, so two of them can
/// hold this file open across a reclaim. The length read, the write stamp and the
/// bytes all happen inside one claim, so this file is sorted by construction
/// exactly as the shared one is.
/// </para>
/// <para>
/// ⚠️ <b>Delete sharing is granted here and withheld on the process log, and the
/// asymmetry is the point.</b> The process log must not be unlinkable out from
/// under a hundred writers; this file <i>must</i> be, because
/// <c>browserai_destroy</c> deletes the session directory while the session that
/// owns it is still live — that is the tool working, not a failure — and a log
/// that could veto its own directory's removal would turn a supported call into a
/// refusal.
/// </para>
/// <para>
/// <b>Every failure is swallowed</b>, as everywhere else in this namespace. A log
/// write must never be able to become the outage it was meant to describe.
/// </para>
/// </remarks>
internal sealed class SessionLogFile : ILogSink, IDisposable
{
    /// <summary>
    /// The file name, at the session root beside <c>browserai.json</c>.
    /// </summary>
    /// <remarks>
    /// It lives with the session rather than under the app data root because the
    /// session directory is the identity: a log that travels with the directory
    /// is one an agent can read with the path it already has, and one
    /// <c>browserai_destroy</c> removes with everything else. Everything that
    /// happens <i>outside</i> a session has no directory to attach to and goes to
    /// <see cref="ProcessLog"/> instead.
    /// </remarks>
    public const string FileName = "browserai.log";

    private readonly Lock _gate = new();

    private SafeFileHandle? _handle;
    private bool _disabled;

    /// <summary>Opens, or prepares to open, a session's log.</summary>
    /// <param name="sessionDirectory">The session directory. It must already exist.</param>
    public SessionLogFile(string sessionDirectory)
    {
        ArgumentNullException.ThrowIfNull(sessionDirectory);
        Path = System.IO.Path.Combine(sessionDirectory, FileName);
    }

    /// <summary>The file records are appended to.</summary>
    public string Path { get; }

    /// <inheritdoc />
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
                var handle = _handle ??= NativeFile.OpenForLockedAppend(Path, shareDelete: true);

                using var claim = NativeFile.TakeGate(handle);

                var length = RandomAccess.GetLength(handle);
                var bytes = Encoding.UTF8.GetBytes(FileLoggerProvider.WriteStamp(DateTime.UtcNow) + record + "\n");

                RandomAccess.Write(handle, bytes, length);
            }
#pragma warning disable CA1031 // See the type's remarks: logging never propagates a failure.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Dropped, and the handle closed so the next write re-opens
                // rather than reusing one that has just failed. A session
                // directory can be deleted underneath a live session; that is
                // browserai_destroy working, not a reason to fail a call.
                // Closing is also what releases the gate when the failure was
                // the release itself.
                Close();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disabled = true;
            Close();
        }
    }

    private void Close()
    {
        try
        {
            _handle?.Dispose();
        }
#pragma warning disable CA1031 // A handle that will not close is abandoned rather than retried.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            _handle = null;
        }
    }
}

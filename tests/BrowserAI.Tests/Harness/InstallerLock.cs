// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>What became of the installer lock for this run.</summary>
internal enum InstallerLockState
{
    /// <summary>Nothing has asked yet.</summary>
    Unread,

    /// <summary>This process took it, and lets it go when the session ends.</summary>
    Taken,

    /// <summary>
    /// A live holder that declared itself in this process's environment has it:
    /// a gate driver, or the test host that started this one.
    /// </summary>
    Declared,

    /// <summary>Somebody else held it past the wait, so the installer arms do not run.</summary>
    NotTaken,
}

/// <summary>What one reading of the lock file says to do next.</summary>
internal enum InstallerLockStep
{
    /// <summary>There is no lock: create it.</summary>
    Create,

    /// <summary>The holder this process was told about has it, and is alive.</summary>
    Covered,

    /// <summary>A holder that is gone left it behind: remove it and look again.</summary>
    Stale,

    /// <summary>Somebody else alive has it: wait.</summary>
    Wait,
}

/// <summary>Who a lock file names.</summary>
/// <param name="ProcessId">The holder's pid.</param>
/// <param name="CreatedFileTime">Its creation time, when the holder wrote one.</param>
internal readonly record struct InstallerLockHolder(int ProcessId, long? CreatedFileTime);

/// <summary>
/// <c>.work\installer.lock</c>, taken by the suite itself for the whole session.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Q291, decided 2026-09-24 by the maintainer, verbatim: <i>"Q291 a"</i>.</b>
/// <i>Previously a convention nothing in the tree read</i>: every run that included
/// <c>RealInstallerTests</c> was to be started by somebody who had taken the file by
/// hand, and a run started without it installed the test pack beside whatever else
/// was installing it. Those arms share one Add/Remove key, one Start Menu title and,
/// since Q294 b, one user PATH; two of them at once is the collision the test id was
/// split to prevent.
/// </para>
/// <para>
/// <b>The protocol is the file and the holder it names.</b> A holder creates the file
/// with <c>FileMode.CreateNew</c> and writes one line naming its pid and, where it
/// can, its creation time as a FILETIME; it deletes the file when it is done. A file
/// whose holder is gone is stale and is taken over. A live holder is waited for, up to
/// <see cref="TestDefaults.InstallerLockWait"/>, and past that the run does not run
/// the installer arms: the <c>release installer</c> capability reads absent and names
/// the holder. <b>A gate driver takes the file before its first clearance snapshot and
/// declares itself in <see cref="DeclarationVariable"/></b>, so the test host it starts
/// finds a holder it was told about and neither waits for it nor lets it go. The host
/// declares itself the same way when it takes the lock, so the child test hosts some
/// arms start are covered by their parent.
/// </para>
/// <para>
/// <b>One file for every checkout of this repository.</b> A linked worktree's own
/// <c>.work</c> would be a second lock over the same machine-wide state, so a worktree
/// resolves the main checkout through its <c>.git</c> file and uses that one's.
/// </para>
/// </remarks>
internal static partial class InstallerLock
{
    /// <summary>The variable a holder names itself in for the processes it starts.</summary>
    public const string DeclarationVariable = "BROWSERAI_INSTALLER_LOCK_HELD";

    /// <summary>The label this row carries in the coverage block.</summary>
    public const string Title = "installer lock";

    /// <summary>How long a file with no holder in it is given to be a holder mid-write.</summary>
    private static readonly TimeSpan UnreadableGrace = TimeSpan.FromSeconds(10);

    private static readonly Lock Gate = new();
    private static InstallerLockState _state = InstallerLockState.Unread;
    private static string _detail = "not asked";
    private static InstallerLockHolder? _mine;

    /// <summary>The lock file.</summary>
    public static string FilePath { get; } = Path.Combine(WorkDirectory(), "installer.lock");

    /// <summary>What became of the lock for this run.</summary>
    public static InstallerLockState State
    {
        get
        {
            lock (Gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Whether this run may run the installer arms.</summary>
    public static bool IsHeldForThisRun => State is InstallerLockState.Taken or InstallerLockState.Declared;

    /// <summary>One clause for the capability's witness.</summary>
    public static string Detail
    {
        get
        {
            lock (Gate)
            {
                return _detail;
            }
        }
    }

    /// <summary>This run's row in the coverage block.</summary>
    public static string CoverageRow
    {
        get
        {
            lock (Gate)
            {
                var word = _state switch
                {
                    InstallerLockState.Taken => "TAKEN  ",
                    InstallerLockState.Declared => "HELD   ",
                    InstallerLockState.NotTaken => "WAITED ",
                    _ => "UNREAD ",
                };

                return "  " + Title.PadRight(20) + word + "  " + _detail;
            }
        }
    }

    /// <summary>The token a holder declares: its pid and its creation time.</summary>
    /// <param name="holder">The holder.</param>
    /// <returns>The token.</returns>
    public static string TokenOf(InstallerLockHolder holder) =>
        holder.CreatedFileTime is { } created
            ? string.Create(CultureInfo.InvariantCulture, $"pid={holder.ProcessId} created={created}")
            : string.Create(CultureInfo.InvariantCulture, $"pid={holder.ProcessId}");

    /// <summary>Who a lock file's text or a declaration names, or null when neither parses.</summary>
    /// <remarks>
    /// <b>Two spellings of the pid</b>: <c>pid=123</c>, which this protocol writes, and
    /// <c>pid 123</c>, which the rigs that took the file by hand before it existed
    /// wrote. A holder that wrote no creation time is judged alive by its pid alone.
    /// </remarks>
    /// <param name="text">The file's text or the declared token.</param>
    /// <returns>The holder.</returns>
    public static InstallerLockHolder? Parse(string? text)
    {
        if (text is null || PidPattern().Match(text) is not { Success: true } pid
            || !int.TryParse(pid.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            return null;
        }

        long? created = CreatedPattern().Match(text) is { Success: true } time
            && long.TryParse(time.Groups["created"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

        return new InstallerLockHolder(processId, created);
    }

    /// <summary>
    /// What one reading of the lock says to do, as a pure function of what was read.
    /// </summary>
    /// <param name="fileText">The lock file's text, or null when there is no file.</param>
    /// <param name="declared">The declaration in this process's environment, or null.</param>
    /// <param name="isAlive">Whether a holder is a live process.</param>
    /// <param name="age">How long ago the file was last written.</param>
    /// <returns>The step.</returns>
    public static InstallerLockStep Decide(string? fileText, string? declared, Func<InstallerLockHolder, bool> isAlive, TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(isAlive);

        if (fileText is null)
        {
            return InstallerLockStep.Create;
        }

        if (Parse(fileText) is not { } holder)
        {
            // ⚠️ A file with no holder in it is a holder mid-write for a moment and
            // debris after that: it is created, then written, and a reader can land
            // between the two. Removing it inside that moment would take a lock away
            // from a holder that believes it has it.
            return age < UnreadableGrace ? InstallerLockStep.Wait : InstallerLockStep.Stale;
        }

        if (!isAlive(holder))
        {
            return InstallerLockStep.Stale;
        }

        return Parse(declared) is { } told && told == holder
            ? InstallerLockStep.Covered
            : InstallerLockStep.Wait;
    }

    /// <summary>
    /// Takes the lock for this session, waits for a live holder, or finds that it
    /// is covered. Called once, from the session hook.
    /// </summary>
    public static void Take()
    {
        lock (Gate)
        {
            if (_state is not InstallerLockState.Unread)
            {
                return;
            }
        }

        var me = new InstallerLockHolder(Environment.ProcessId, ProcessIdentity.CreationTimeOf(Environment.ProcessId));
        var declared = Environment.GetEnvironmentVariable(DeclarationVariable);
        var deadline = DateTime.UtcNow + TestDefaults.InstallerLockWait;
        var waited = false;

        while (true)
        {
            var (text, busy, age) = TryRead();

            if (busy)
            {
                // Open for writing by its holder this instant: read it again.
                Thread.Sleep(TimeSpan.FromMilliseconds(200));
                continue;
            }

            switch (Decide(text, declared, IsAlive, age))
            {
                case InstallerLockStep.Covered:
                    Settle(InstallerLockState.Declared, null, $"held by {declared}, alive, the holder named in {DeclarationVariable}: this run neither waited for it nor lets it go");
                    return;

                case InstallerLockStep.Create:
                    if (TryCreate(me))
                    {
                        // Declared for the child test hosts some arms start, which
                        // inherit this block and would otherwise wait for their parent.
                        Environment.SetEnvironmentVariable(DeclarationVariable, TokenOf(me));
                        Settle(
                            InstallerLockState.Taken,
                            me,
                            $"taken by this run ({TokenOf(me)}){(waited ? " after waiting for another holder" : string.Empty)}, and let go when the session ends: {FilePath}");
                        return;
                    }

                    continue;

                case InstallerLockStep.Stale:
                    TryRemoveIfStill(text!);
                    continue;

                default:
                    if (DateTime.UtcNow >= deadline)
                    {
                        Settle(
                            InstallerLockState.NotTaken,
                            null,
                            $"'{text!.Trim()}' held {FilePath} for the whole of the suite's wait, so the installer arms do not run in this session");
                        return;
                    }

                    waited = true;
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                    continue;
            }
        }
    }

    /// <summary>Lets the lock go if this process took it. Called once, from the session hook.</summary>
    public static void Release()
    {
        InstallerLockHolder? mine;

        lock (Gate)
        {
            mine = _state is InstallerLockState.Taken ? _mine : null;
        }

        if (mine is null)
        {
            return;
        }

        // Only a file that still names this process: a holder that took over a lock
        // this one had somehow lost must not have it deleted from under it.
        if (TryRead() is ({ } text, false, _) && Parse(text) == mine)
        {
            TryRemoveIfStill(text);
        }
    }

    /// <summary>Whether a holder is a live process.</summary>
    /// <param name="holder">The holder.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsAlive(InstallerLockHolder holder)
    {
        if (holder.CreatedFileTime is { } created)
        {
            return ProcessIdentity.IsAlive(holder.ProcessId, created);
        }

        try
        {
            _ = ProcessIdentity.CreationTimeOf(holder.ProcessId);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The <c>.work</c> directory of the main checkout, whichever checkout this is.</summary>
    /// <returns>The directory.</returns>
    private static string WorkDirectory()
    {
        var root = RepositoryLayout.Root.FullName;
        var dotGit = Path.Combine(root, ".git");

        try
        {
            // A linked worktree's .git is a FILE naming its own git directory, and
            // that directory's `commondir` names the main one.
            if (File.Exists(dotGit)
                && File.ReadAllText(dotGit).Trim() is { } pointer
                && pointer.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            {
                var gitDirectory = Path.GetFullPath(pointer["gitdir:".Length..].Trim(), root);
                var commonFile = Path.Combine(gitDirectory, "commondir");

                if (File.Exists(commonFile)
                    && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(File.ReadAllText(commonFile).Trim(), gitDirectory))) is { Length: > 0 } main)
                {
                    root = main;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Path.Combine(root, ".work");
    }

    private static void Settle(InstallerLockState state, InstallerLockHolder? mine, string detail)
    {
        lock (Gate)
        {
            _state = state;
            _mine = mine;
            _detail = detail;
        }
    }

    /// <summary>
    /// The lock file's text and age, or no text when there is no file, or busy when
    /// it could not be opened this instant.
    /// </summary>
    private static (string? Text, bool Busy, TimeSpan Age) TryRead()
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var text = reader.ReadToEnd();

            return (text, false, DateTime.UtcNow - File.GetLastWriteTimeUtc(FilePath));
        }
        catch (FileNotFoundException)
        {
            return (null, false, TimeSpan.Zero);
        }
        catch (DirectoryNotFoundException)
        {
            return (null, false, TimeSpan.Zero);
        }
        catch (IOException)
        {
            return (null, true, TimeSpan.Zero);
        }
    }

    private static bool TryCreate(InstallerLockHolder me)
    {
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            using var stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"holder=suite {TokenOf(me)} at={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");

            stream.Write(Encoding.UTF8.GetBytes(line));
            return true;
        }
        catch (IOException)
        {
            // Somebody created it between the read and the create.
            return false;
        }
    }

    private static void TryRemoveIfStill(string text)
    {
        try
        {
            if (TryRead() is ({ } now, false, _) && now == text)
            {
                File.Delete(FilePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"\bpid(?:=|\s)(?<pid>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PidPattern();

    [GeneratedRegex(@"\bcreated=(?<created>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex CreatedPattern();
}

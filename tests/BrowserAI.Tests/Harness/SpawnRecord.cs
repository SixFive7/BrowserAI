// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Logging;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What this run started, written where the <b>next</b> run can read it — so a
/// run that is killed leaves behind a list of identities rather than a set of
/// processes nobody can name.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes was the last one in the reclaim pass.</b>
/// [Testing](../../../TESTING.md#testing-a-hard-requirement-and-the-release-gate) asks that <i>anything the
/// previous run recorded is terminated by <c>(pid, creationFileTime)</c> from its
/// own spawn record</i>. The other three bullets — the abandoned mutexes, the
/// scratch tree, the stray index entries — were built; nothing wrote a record, so
/// the one bullet that names processes had no input and quietly did nothing. A
/// run killed mid-test therefore left a process the next run could not identify,
/// only a directory it could not delete: the leftover surfaced as a
/// <see cref="ScratchRoot.LastPassSurvivors"/> entry blaming a file.
/// </para>
/// <para>
/// <b>An identity, never a name.</b> Each row names a pid and the creation time
/// read at the instant it was started, and reclaiming re-reads that time before
/// acting — so a pid Windows has recycled is skipped rather than killed. There is
/// no image name anywhere in the file and there must never be one; the rule that
/// nothing is found by image name has no exception for test code, and a record
/// carrying names is one refactor from being matched on.
/// </para>
/// <para>
/// ⚠️ <b>Every row also names its OWNER, since 2026-08-29, and that is what stops
/// the pass being a machine-wide kill.</b> Until then a row named only its
/// subject, so a <i>second</i> harness process reading a <i>live</i> run's record
/// terminated that run's browsers, probes and slices — with exit code 1, and then
/// deleted the scratch tree they were using. Measured 2026-08-29 at
/// <b>18 of 18</b> launches, and it is the mechanism that finally reproduced the
/// silent Chromium death of 2026-08-26 point for point: silent pipes, five log
/// lines, no message window, exit 1. The whole trail is
/// [QUESTIONS §8a](../../../QUESTIONS.md), whose direction (b) this is.
/// <b>The pass now terminates a subject only when its owner is gone</b>, so a
/// live run's rows are invisible to anybody else and a killed run's rows are
/// still recovered.
/// </para>
/// <para>
/// <b>What "owner" means here, stated because the obvious reading is wrong.</b>
/// A <i>run</i> is not a process: <c>dotnet test</c> starts a test host, and this
/// suite starts a second <c>BrowserAI.Tests.exe</c> inside itself, so there is no
/// one pid whose death means "the run is over". The owner of a row is therefore
/// <b>the process that started the recorded process and holds the job object
/// containing it</b> — <see cref="JobObjectScope"/>'s job lives in that process
/// and closes when it exits. That definition keeps both properties for the same
/// reason: while the owner lives, the subject is somebody else's business and its
/// containment is intact; once the owner dies, its job closed, so anything still
/// running is an orphan and reclaiming it is exactly the job this file exists
/// for. A run that spans three processes writes three owners and each is judged
/// on its own, which is <i>stronger</i> than a per-run token — a token would keep
/// a row alive because some unrelated process of the same run was.
/// </para>
/// <para>
/// <b>Why an identity and not a run id at all: a run id cannot be asked whether
/// it is alive.</b> The pass has exactly one question to answer and only the
/// operating system can answer it. A <c>(pid, creationFileTime)</c> pair is
/// answerable — it is this repository's standing identity for a process, the one
/// <c>browserai.lock</c> and every process-log record already spell — and it
/// cannot be impersonated by a recycled pid. A GUID in the file would have needed
/// a second mechanism, a live marker, to become a liveness question again, and
/// that marker would have had the same staleness problem one level down.
/// </para>
/// <para>
/// <b>What it does not cover, stated rather than discovered.</b> It records the
/// processes the harness itself starts. A grandchild — a browser a probe
/// launched, a node the product spawned — is not in the file, and the mechanism
/// that contains those is the job object, which takes the whole tree down when
/// its handle closes. So this is the belt to the job object's brace: it exists
/// for the case where the job died with the host and something outlived it
/// anyway, and it terminates the process it named rather than a tree, because
/// <c>Process.Kill(entireProcessTree: true)</c> is banned repository-wide for
/// walking re-parentable links.
/// </para>
/// <para>
/// <b>Appended under a lock, because the suite runs at
/// <see cref="SuiteParallelism.Unbounded"/></b> and two tests starting probes at
/// once is the ordinary case rather than the exception. The file is opened for
/// append and closed immediately: a handle held for the length of a run would be
/// a handle the next run's reclaim cannot read.
/// </para>
/// <para>
/// ⚠️ <b>The residual, named rather than left to be found.</b> A pass rewrites
/// the file with the rows it left alone, and that rewrite is not atomic against
/// another process appending in the same instant — so a live run can lose the one
/// row it wrote inside that window. It is a strictly smaller loss than the whole
/// file, which is what the pass used to take, and closing it needs the
/// machine-wide interlock that
/// [QUESTIONS §8a](../../../QUESTIONS.md) carries as
/// direction (c). That direction was not taken and this sentence is why it is
/// still worth taking.
/// </para>
/// </remarks>
internal static class SpawnRecord
{
    /// <summary>
    /// The category every announcement is written under, so the grep this
    /// mechanism exists for is one string.
    /// </summary>
    private const string AnnouncementCategory = "BrowserAI.Tests.SpawnRecordReclaim";

    private static readonly Lock Gate = new();

    /// <summary>
    /// This process, as the owner column of every row it writes.
    /// </summary>
    /// <remarks>
    /// Read once. A creation time of <c>0</c> means it could not be read at all,
    /// which <see cref="Add"/> treats as a reason to record nothing — see there.
    /// </remarks>
    private static readonly Identity Self = new(Environment.ProcessId, OwnCreationTimeOrZero());

    /// <summary>
    /// <c>&lt;repo&gt;\.work\spawn-record.txt</c> — <b>outside</b> the scratch
    /// root on purpose, because the reclaim deletes that tree and would take its
    /// own input with it.
    /// </summary>
    public static string Path { get; } =
        System.IO.Path.Combine(RepositoryLayout.Root.FullName, ".work", "spawn-record.txt");

    /// <summary>Records one process this run started, and who started it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Never fatal.</b> A record that could not be written costs the next run
    /// its reclaim of this process and nothing else, and failing a test because
    /// bookkeeping failed would be the harness inventing a defect.
    /// </para>
    /// <para>
    /// <b>A process that cannot name itself records nothing at all</b>, which is
    /// the same trade one step earlier. An owner column another process cannot
    /// check is worse than an absent row: it would read as an owner that is not
    /// running, and the row would be terminated by the next pass that met it —
    /// which is the exact failure the column was added to remove.
    /// </para>
    /// </remarks>
    /// <param name="processId">The pid, as the launcher returned it.</param>
    public static void Add(int processId)
    {
        if (Self.Created is 0)
        {
            return;
        }

        try
        {
            var subject = new Identity(processId, ProcessIdentity.CreationTimeOf(processId));

            lock (Gate)
            {
                _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, Row(subject) + Environment.NewLine);
            }
        }
#pragma warning disable CA1031 // See the remarks: bookkeeping is never a reason to fail a test.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>
    /// Terminates everything an <b>ended</b> run recorded that is still that
    /// process, leaves a live run's rows alone, and rewrites the record with what
    /// it left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two questions per row, in this order, and the order is the point.</b>
    /// First: is the owner still here? A row whose owner is this process, or any
    /// process still running, describes containment that has not failed — nothing
    /// may be done to it, and it is written back so the owner's own next run can
    /// still honour it. Only then: is the subject still that process? A row whose
    /// creation time no longer matches names a process that has already exited,
    /// and the number now belongs to something else — possibly the developer's
    /// editor. Those are reported as skipped, which is the state the pass expects
    /// to be in on a healthy machine.
    /// </para>
    /// <para>
    /// <b>The self check is first and is not merely the liveness check restated.</b>
    /// This process is alive by construction, so asking the operating system
    /// about it could only ever confirm what is already known — but the pass runs
    /// before anything else and a mechanism that can terminate its own run's
    /// processes if one syscall misbehaves is not one to leave resting on that
    /// syscall.
    /// </para>
    /// <para>
    /// <b>A row it cannot read is reported and dropped, never guessed at.</b> The
    /// only rows in that class are ones written before the owner column existed,
    /// and they name a subject with nobody accountable for it; terminating on a
    /// row this build did not write would be the machine-wide kill again with an
    /// extra step. The cost is a single leftover from a record file that predates
    /// 2026-08-29, in a gitignored scratch directory.
    /// </para>
    /// </remarks>
    /// <param name="path">The record to read, so this is testable as itself.</param>
    /// <returns>One line per recorded process, saying what was done about it.</returns>
    public static List<string> Reclaim(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var report = new List<string>();
        var kept = new List<string>();
        var ended = new List<(Identity Subject, Identity Owner)>();

        lock (Gate)
        {
            if (!File.Exists(path))
            {
                return report;
            }

            foreach (var line in ReadOrNothing(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (Parse(line) is not var (subject, owner))
                {
                    report.Add($"ignored {line.Trim()}: not a row this pass can read");
                    continue;
                }

                if (OwnerIsStillHere(owner))
                {
                    kept.Add(line);
                    report.Add($"left alone {subject}: its owner {owner} is still running");
                    continue;
                }

                if (!ProcessIdentity.IsAlive(subject.ProcessId, subject.Created))
                {
                    report.Add($"skipped {subject}: not that process any more");
                    continue;
                }

                try
                {
                    ProcessIdentity.Terminate(subject.ProcessId, subject.Created);

                    // Recorded HERE, on the line after the call that delivered
                    // the exit code, and not from the report below: what the
                    // announcement claims is that this pass handed that process
                    // a 1, and the only place that is true is after a Terminate
                    // that did not throw. A process that was asked and has not
                    // gone still carries the kill and still belongs in the log.
                    ended.Add((subject, owner));

                    // Waited out, because the next thing the pass does is delete
                    // the tree this process was writing into. TerminateProcess
                    // only asks; a delete that raced it would report a locked
                    // file and name the wrong cause -- which is the exact
                    // misdiagnosis this whole pass exists to prevent.
                    report.Add(ProcessIdentity.WaitUntilGone(subject.ProcessId, subject.Created, TestDefaults.ProcessHang)
                        ? $"terminated {subject}: left over from a previous run owned by {owner}"
                        : $"could not terminate {subject}: it was asked to exit and has not");
                }
#pragma warning disable CA1031 // A process that cannot be terminated is reported, never thrown about: the run has not started yet.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    report.Add($"could not terminate {subject}: {failure.Message}");
                }
            }

            TryRewrite(path, kept);
        }

        // Outside the gate: the announcement goes to a different file, and a
        // pass holding the record's lock across a machine-wide log write would
        // be queueing every other harness process behind a diagnostic.
        Announce(path, ended);

        return report;
    }

    /// <summary>
    /// Writes what the pass ended to the machine's process log, and writes
    /// nothing when it ended nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is direction (d) of
    /// [QUESTIONS §8a](../../../QUESTIONS.md), taken
    /// 2026-08-29 with (b).</b> Until now the pass's whole account of itself was
    /// <see cref="ScratchRoot.LastPassReport"/>, an in-memory list, of which only
    /// a <i>survivor</i> ever reached the coverage block — so a reclaim that
    /// succeeded left <b>no trace at all</b>. That is why nothing could say which
    /// terminator fired on 2026-08-26, and why an exit code of 1 cost two rigs
    /// and eighty launches to not explain.
    /// </para>
    /// <para>
    /// <b>The process log, and not a file of the harness's own.</b> It is the
    /// machine-wide, durable, thirty-day record of what processes on this box
    /// did; it sits outside the scratch tree the same pass is about to delete;
    /// and it is already where the suite's own published slices write, which is
    /// why <see cref="ProcessLogRecords"/> exists to read it back. A harness-only
    /// log would be a second place to look, known to nobody who was not already
    /// reading this file.
    /// </para>
    /// <para>
    /// ⚠️ <b>So the suite now writes outside the repository in two places rather
    /// than one</b>, and the claims that said otherwise —
    /// <see cref="ScratchRoot.ProfileScratch"/> and
    /// <see cref="ScratchDirectory"/> — carry the correction.
    /// </para>
    /// <para>
    /// <b>Silence is the signal when nothing was ended.</b> Every run of this
    /// suite runs this pass, so a pass that announced its no-ops would put a line
    /// in the machine's log on every start and the one grep this buys would stop
    /// being one.
    /// </para>
    /// <para>
    /// <b>Never fatal, for the reason <see cref="Add"/> is not.</b> A reclaim that
    /// could not describe itself has still reclaimed, and a diagnostic that can
    /// fail a run is a diagnostic that becomes the outage — which is the process
    /// log's own founding rule, applied to a writer that is not the product.
    /// </para>
    /// </remarks>
    /// <param name="path">The record being honoured, named in every line.</param>
    /// <param name="ended">The subjects this pass handed an exit code to, and their owners.</param>
    private static void Announce(string path, List<(Identity Subject, Identity Owner)> ended)
    {
        if (ended.Count is 0)
        {
            return;
        }

        try
        {
            using var log = ProcessLog.Create(BrowserAiPaths.Real, LogLevel.Information);
            var logger = log.Factory.CreateLogger(AnnouncementCategory);

            foreach (var (subject, owner) in ended)
            {
                ReclaimAnnouncement.Ended(
                    logger,
                    subject.ToString(),
                    ProcessIdentity.TerminationExitCode,
                    owner.ToString(),
                    path);
            }
        }
#pragma warning disable CA1031 // See the remarks: an announcement never becomes the outage.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static bool OwnerIsStillHere(Identity owner) =>
        owner == Self || ProcessIdentity.IsAlive(owner.ProcessId, owner.Created);

    private static long OwnCreationTimeOrZero()
    {
        try
        {
            return ProcessIdentity.CreationTimeOf(Environment.ProcessId);
        }
#pragma warning disable CA1031 // A harness that cannot name itself records nothing; it does not fail to load.
        catch (Exception)
#pragma warning restore CA1031
        {
            return 0;
        }
    }

    private static string Row(Identity subject) => $"{subject} {Self}";

    private static (Identity Subject, Identity Owner)? Parse(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length is 2 && Identity.Parse(parts[0]) is { } subject && Identity.Parse(parts[1]) is { } owner
            ? (subject, owner)
            : null;
    }

    private static string[] ReadOrNothing(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Puts back what the pass left alone, and empties the file when it left
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <b>Rewriting rather than emptying is half of the fix, not tidiness.</b> A
    /// pass that blanked the file would take a live run's rows with it, and the
    /// day that run really was killed nothing would name what it left behind —
    /// which is the recovery this whole file exists for, removed by the thing
    /// that was supposed to protect it.
    /// </remarks>
    /// <param name="path">The record.</param>
    /// <param name="kept">The rows, verbatim, that the pass did not act on.</param>
    private static void TryRewrite(string path, List<string> kept)
    {
        try
        {
            File.WriteAllText(
                path,
                kept.Count is 0 ? string.Empty : string.Join(Environment.NewLine, kept) + Environment.NewLine);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A process, as this file names one: the pid and the creation time that
    /// tells it from the next process to wear that number.
    /// </summary>
    /// <param name="ProcessId">The pid.</param>
    /// <param name="Created">Its creation time as a Windows FILETIME.</param>
    /// <remarks>
    /// <b>Spelled <c>pid@createdFileTime</c>, which is not a new convention.</b>
    /// It is the same pair, in the same characters, that every process-log record
    /// carries in its <c>pid=</c> header and that <c>browserai.lock</c> writes as
    /// <c>processCreatedFileTime</c> — so a row in this file, a line in the log
    /// and a lock record all name the same process the same way, and the
    /// announcement can quote one into the other without a second format.
    /// </remarks>
    private readonly record struct Identity(int ProcessId, long Created)
    {
        /// <summary>Reads one identity back out of a record row.</summary>
        /// <param name="text">The <c>pid@createdFileTime</c> field.</param>
        /// <returns>The identity, or null when the field is not one.</returns>
        public static Identity? Parse(string text)
        {
            var at = text.IndexOf('@', StringComparison.Ordinal);

            return at > 0
                && int.TryParse(text.AsSpan(..at), NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId)
                && long.TryParse(text.AsSpan((at + 1)..), NumberStyles.Integer, CultureInfo.InvariantCulture, out var created)
                && processId > 0
                    ? new Identity(processId, created)
                    : null;
        }

        /// <inheritdoc />
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{ProcessId}@{Created}");
    }
}

/// <summary>
/// The one record the reclaim pass writes about itself.
/// </summary>
/// <remarks>
/// Source-generated so the message is a single literal that cannot drift from
/// the arguments beside it, and written at <see cref="LogLevel.Warning"/>
/// because a machine-wide termination is not routine — it means a run on this
/// box was killed and something outlived it.
/// </remarks>
internal static partial class ReclaimAnnouncement
{
    /// <summary>Records one process the pass handed an exit code to.</summary>
    /// <param name="logger">The process log's logger.</param>
    /// <param name="subject">The terminated process, as <c>pid@createdFileTime</c>.</param>
    /// <param name="exitCode">The code it was handed, read from the call that handed it.</param>
    /// <param name="owner">The process that had started it, as <c>pid@createdFileTime</c>.</param>
    /// <param name="record">The spawn record the pass was honouring.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The test harness's spawn-record reclaim terminated {Subject} with exit code {ExitCode}, because its owner {Owner} is no longer running. The row was in {Record}.")]
    public static partial void Ended(ILogger logger, string subject, int exitCode, string owner, string record);
}

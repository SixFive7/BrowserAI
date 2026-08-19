// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

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
/// <b>An identity, never a name.</b> Each line is a pid and the creation time
/// read at the instant it was started, and reclaiming re-reads that time before
/// acting — so a pid Windows has recycled is skipped rather than killed. There is
/// no image name anywhere in the file and there must never be one; the rule that
/// nothing is found by image name has no exception for test code, and a record
/// carrying names is one refactor from being matched on.
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
/// </remarks>
internal static class SpawnRecord
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// <c>&lt;repo&gt;\.work\spawn-record.txt</c> — <b>outside</b> the scratch
    /// root on purpose, because the reclaim deletes that tree and would take its
    /// own input with it.
    /// </summary>
    public static string Path { get; } =
        System.IO.Path.Combine(RepositoryLayout.Root.FullName, ".work", "spawn-record.txt");

    /// <summary>Records one process this run started.</summary>
    /// <remarks>
    /// <b>Never fatal.</b> A record that could not be written costs the next run
    /// its reclaim of this process and nothing else, and failing a test because
    /// bookkeeping failed would be the harness inventing a defect.
    /// </remarks>
    /// <param name="processId">The pid, as the launcher returned it.</param>
    public static void Add(int processId)
    {
        try
        {
            var created = ProcessIdentity.CreationTimeOf(processId);

            lock (Gate)
            {
                _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, Line(processId, created) + Environment.NewLine);
            }
        }
#pragma warning disable CA1031 // See the remarks: bookkeeping is never a reason to fail a test.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>
    /// Terminates everything a previous run recorded that is <b>still that
    /// process</b>, and empties the record.
    /// </summary>
    /// <remarks>
    /// <b>Every line is an assertion about a pid that is checked before it is
    /// acted on.</b> A line whose creation time no longer matches names a process
    /// that has already exited, and the number now belongs to something else —
    /// possibly the developer's editor. Those are reported as skipped, which is
    /// the state the pass expects to be in on a healthy machine.
    /// </remarks>
    /// <param name="path">The record to read, so this is testable as itself.</param>
    /// <returns>One line per recorded process, saying what was done about it.</returns>
    public static List<string> Reclaim(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var report = new List<string>();

        lock (Gate)
        {
            if (!File.Exists(path))
            {
                return report;
            }

            foreach (var line in ReadOrNothing(path))
            {
                if (Parse(line) is not var (processId, created))
                {
                    continue;
                }

                if (!ProcessIdentity.IsAlive(processId, created))
                {
                    report.Add($"skipped {Line(processId, created)}: not that process any more");
                    continue;
                }

                try
                {
                    ProcessIdentity.Terminate(processId, created);

                    // Waited out, because the next thing the pass does is delete
                    // the tree this process was writing into. TerminateProcess
                    // only asks; a delete that raced it would report a locked
                    // file and name the wrong cause -- which is the exact
                    // misdiagnosis this whole pass exists to prevent.
                    report.Add(ProcessIdentity.WaitUntilGone(processId, created, TestDefaults.ProcessHang)
                        ? $"terminated {Line(processId, created)}: left over from a previous run"
                        : $"could not terminate {Line(processId, created)}: it was asked to exit and has not");
                }
#pragma warning disable CA1031 // A process that cannot be terminated is reported, never thrown about: the run has not started yet.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    report.Add($"could not terminate {Line(processId, created)}: {failure.Message}");
                }
            }

            TryEmpty(path);
        }

        return report;
    }

    private static string Line(int processId, long created) =>
        $"{processId.ToString(CultureInfo.InvariantCulture)} {created.ToString(CultureInfo.InvariantCulture)}";

    private static (int ProcessId, long Created)? Parse(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length is 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var created)
            && processId > 0
                ? (processId, created)
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

    private static void TryEmpty(string path)
    {
        try
        {
            File.WriteAllText(path, string.Empty);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}

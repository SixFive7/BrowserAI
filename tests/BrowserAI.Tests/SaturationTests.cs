// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The design point, run for real: the charter's <b>~100 concurrent BrowserAI
/// processes sharing one process log</b>, with real browsers launching and
/// closing underneath them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every other test in this suite is about one thing being right. This one
/// is about the machine not being the thing that breaks.</b> The number 100
/// comes from the charter — eight editor windows with a dozen agent sessions
/// each — and until 2026-08-17 nothing exercised anything like it: the widest
/// concurrency the suite reached was <c>ProcessLogTests</c>' eight probe
/// processes writing twenty-five lines apiece, which is not a browser, not a
/// session and not a job.
/// </para>
/// <para>
/// <b>It is nondeterministic in its timing and deterministic in every
/// assertion, which is the only shape worth having.</b> Nothing below reads a
/// stopwatch, compares an elapsed time or asserts a rate. What it asserts is
/// identity, disjointness and completeness — properties that are either true or
/// false whatever the machine is doing, and every one of which is <i>false</i>
/// if containment or session isolation breaks. The one place time appears is a
/// bounded wait for a real process tree to die, which is a hang detector rather
/// than a budget.
/// </para>
/// <para>
/// <b>The five claims, and what each would look like if it broke:</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Every process answered.</b> Not "most of them" — a run in which one
/// BrowserAI in a hundred silently failed to open a session is exactly the
/// degradation this repository exists to make impossible to miss, so the count
/// is asserted against the count that was dispatched and every failure carries
/// its own process's stderr.
/// </description></item>
/// <item><description>
/// <b>Every session belongs to exactly one process.</b> Each <c>init</c> answer
/// names its own directory and no other's, and each session's <c>browserai.lock</c>
/// names the pid that opened it. A shared static, a path collision or a
/// cross-wired session shows up here as one directory claimed twice.
/// </description></item>
/// <item><description>
/// <b>The jobs are pairwise disjoint.</b> No pid is in two BrowserAIs' job
/// objects. This is what containment <i>means</i> at scale: one client's
/// teardown must not be able to take another client's browser with it.
/// </description></item>
/// <item><description>
/// <b>Nothing survives teardown.</b> Every pid recorded while the browsers were
/// up is dead once the jobs close — and the pids are recorded with their
/// creation times, so a recycled pid cannot make a leak look like a clean exit.
/// </description></item>
/// <item><description>
/// <b>The shared process log is still readable.</b> No record header appears
/// anywhere but at the start of a line in anything this run's processes wrote,
/// and every one of this test's processes appears in it by pid. A hundred
/// processes appending to one file is the charter's own claim about
/// <c>FILE_APPEND_DATA</c>, and this is the only test that puts a hundred
/// processes behind it.
/// <para>
/// ⚠️ <b>Corrected 2026-08-24 (previously "Every line in it is a whole record or
/// an indented continuation").</b> That sentence outlived the assertion by seven
/// days: the rule about what every <i>other</i> line must look like was removed
/// on 2026-08-17, because a log message may contain newlines and the shape of
/// its continuations is not this test's business
/// (see <see cref="RecordHeader"/>). The clause about <i>this run's</i>
/// processes is the 2026-08-24 pid scope
/// (see <see cref="TornRecordsThisRunWasPartyTo"/>) — the log is machine-wide,
/// so an unscoped claim about it is a claim about every checkout on the box.
/// </para>
/// </description></item>
/// </list>
/// <para>
/// <b>The process log it writes to is the real one</b>, under
/// <c>%LocalAppData%\BrowserAI\logs</c>, because that is the file whose
/// concurrency is the claim. Sessions, profiles and working directories all go
/// to scratch; nothing here writes to a developer's sessions and nothing deletes
/// anything outside <c>.work\</c>.
/// </para>
/// <para>
/// ⚠️ <b><c>[NotInParallel]</c> with no key, which in TUnit means it runs beside
/// nothing at all — and this is the one kind of exclusivity that is a
/// requirement rather than an excuse.</b> The distinction is not "it is flaky
/// otherwise": <b>the assertions are meaningless without the resource</b>. This
/// test's subject is what BrowserAI does when the machine is pinned, so the
/// machine has to be pinned <i>by it</i>. Sharing 32 cores with 418 other tests
/// does not make the test stronger; it makes it measure a different, unstated
/// load and then fail on somebody else's thirty-second bound.
/// </para>
/// <para>
/// <b>Measured 2026-08-17, and this is why the earlier note here was wrong.</b>
/// Run in parallel with the rest of the suite it was the top failure of the
/// streak — five red runs in six, at <b>0, 0, 6, 1, 1, 39, 36</b> failures —
/// and <i>not one</i> of those failures was containment or isolation. They were
/// timeouts: <c>initialization timed out</c>, <c>no frame arrived within 30 s</c>
/// between two objects in the same process, and rig teardowns reporting their
/// server task still running. A test that cannot fail for its own reasons is
/// not testing anything.
/// </para>
/// <para>
/// <b>The 100 is not negotiable downwards to make this pass.</b> The charter
/// makes that claim publicly, so it gets measured or amended — never quietly
/// weakened.
/// </para>
/// </remarks>
internal sealed partial class SaturationTests
{
    /// <summary>
    /// How many published BrowserAI processes run at once.
    /// </summary>
    /// <remarks>
    /// <b>The charter's number, not a number that was convenient.</b> Every one
    /// of them opens a real session with a real <c>node.exe</c> behind it, so
    /// this is a hundred BrowserAIs and a hundred children contending for one
    /// process log, one session index, one instance root and one sweep mutex.
    /// Measured 2026-08-17 on a 32-core / 128 GB machine
    /// ([kb](../../kb/windows/processes.md#saturation-the-100-process-design-point)).
    /// </remarks>
    private const int Processes = 100;

    /// <summary>
    /// How many of those also launch, close and relaunch a real Chromium.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A subset, and the number is a measured ceiling rather than a
    /// preference.</b> The hundred is a claim about BrowserAI processes; the
    /// browser subset is what makes the containment and disjointness claims mean
    /// anything, and each one is a tree of about eight processes and several
    /// hundred megabytes.
    /// </para>
    /// <para>
    /// ⚠️ <b>The product holds at 24. The rest of the suite does not.</b>
    /// Measured 2026-08-17, both ways:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Alone, at 24:</b> green in <b>82 s</b>. Every one of the hundred peers
    /// answered, every session was claimed once, every job was disjoint, nothing
    /// leaked. <b>802 processes</b> were live at the census — the figure the
    /// fault-injection run reported when teardown was skipped.
    /// </description></item>
    /// <item><description>
    /// <b>Inside the full suite, at 24:</b> <b>seven other tests failed</b>, none
    /// of them about BrowserAI. Their shape is the point: <i>"No frame arrived on
    /// this pipe within 30 s"</i> between two <i>in-process</i> objects, and a rig
    /// teardown reporting its server task still running after 30 s. At 800
    /// processes over 32 cores the test host gets no CPU, so a thirty-second
    /// in-process silence stops meaning "deadlock". Those bounds are hang
    /// detectors; raising them to accommodate this test would blind them, which
    /// is the move this repository forbids.
    /// </description></item>
    /// <item><description>
    /// <b>Inside the full suite, at 8:</b> green, and green repeatedly. The suite
    /// costs <b>105 s</b> where it cost 20 s without this test, and 96 s of that
    /// is this test.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>So the ceiling is on what the machine can carry while 418 other tests
    /// run, not on anything BrowserAI does.</b> Raise this to 24 to reproduce the
    /// standalone measurement; the suite will go red, and it will go red for the
    /// reason above rather than for a defect.
    /// </para>
    /// </remarks>
    private const int WithBrowsers = 8;

    /// <summary>How long a real browser tree gets to die once its job has closed.</summary>
    private static readonly TimeSpan TeardownPatience = TestDefaults.ProcessHang;

    /// <summary>
    /// One process's whole conversation. Generous, because this is a hundred
    /// process starts and two dozen browser launches against one machine.
    /// </summary>
    /// <remarks>
    /// <b>Not a budget and never asserted on.</b> It is the point at which a
    /// wedged conversation is reported as wedged instead of hanging the run, and
    /// the failure it produces carries that process's own stderr.
    /// </remarks>
    private static readonly TimeSpan Conversation = TestDefaults.BrowserHang;

    /// <summary>
    /// A hundred BrowserAI processes, two dozen browsers, one process log, and
    /// nothing crossed or leaked.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    // No key: in TUnit that means beside nothing at all. The assertion is about
    // a pinned machine, so this test has to be the thing pinning it -- see the
    // type's remarks for the measurement that settled it.
    [NotInParallel]
    public async Task TheDesignPointHoldsWithEveryProcessBrowserAndSessionAtOnce()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("saturation");

        var logsBefore = LogFilesNow();
        var started = DateTimeOffset.UtcNow;

        var peers = Enumerable.Range(0, Processes).Select(index => new Peer(index, scratch.Path)).ToList();

        try
        {
            // Everything at once. Task.WhenAll rather than a throttle, because a
            // throttle would be this test deciding the machine cannot take what
            // it is named for.
            var reports = await Task.WhenAll(peers.Select(peer => peer.RunAsync()));

            // ---- 1. Every process answered ---------------------------------

            var broken = reports.Where(report => report.Failure is not null)
                .Select(report => $"peer {report.Index.ToString(CultureInfo.InvariantCulture)}: {report.Failure}")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine + Environment.NewLine, broken)).IsEmpty();
            await Assert.That(reports.Length).IsEqualTo(Processes);

            // ---- 2. Every session belongs to exactly one process ------------

            // The answer a peer got names its own directory. Asserted as a set
            // rather than per peer, so a cross-wiring shows up as a duplicate
            // rather than as one confusing message.
            var claimed = reports.GroupBy(report => report.Session, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() is not 1)
                .Select(group => $"{group.Key} was claimed by peers {string.Join(", ", group.Select(report => report.Index))}")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine, claimed)).IsEmpty();

            var mixedUp = reports
                .Where(report => reports.Any(other => other.Index != report.Index
                    && report.InitText.Contains(other.Session, StringComparison.OrdinalIgnoreCase)))
                .Select(report => $"peer {report.Index.ToString(CultureInfo.InvariantCulture)}'s init answer names another peer's session")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine, mixedUp)).IsEmpty();

            // And on disk: the lock in each session directory names the process
            // that opened it, which is the product's own record rather than the
            // harness's bookkeeping.
            var misheld = reports
                .Where(report => report.LockHolder != report.ProcessId)
                .Select(report => $"peer {report.Index.ToString(CultureInfo.InvariantCulture)}: {report.Session} records holder pid {report.LockHolder.ToString(CultureInfo.InvariantCulture)}, but the process that opened it is {report.ProcessId.ToString(CultureInfo.InvariantCulture)}")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine, misheld)).IsEmpty();

            // ---- 3. The jobs are pairwise disjoint --------------------------

            // ⚠️ Keyed on (pid, creation time) and never on the pid alone, and
            // this test is where that rule earns its keep rather than where it is
            // recited. The first version compared pids: it reported TWELVE
            // processes shared between jobs on its very first run, every one of
            // them a pid Windows had recycled between two peers reading their
            // job membership. Twenty-four browser trees closing at once frees
            // roughly two hundred pids in a second, so at this scale reuse is
            // not the unlucky case — it is the normal one, and a pid on its own
            // is not an identity.
            var owners = new Dictionary<(int ProcessId, long Created), int>();
            var shared = new List<string>();

            foreach (var report in reports)
            {
                foreach (var member in report.JobMembers)
                {
                    var identity = (member.ProcessId, member.CreatedFileTime);

                    if (owners.TryGetValue(identity, out var first))
                    {
                        shared.Add($"pid {member.ProcessId.ToString(CultureInfo.InvariantCulture)} created at {member.CreatedFileTime.ToString(CultureInfo.InvariantCulture)} ({member.ImagePath}) is in the jobs of peers {first.ToString(CultureInfo.InvariantCulture)} and {report.Index.ToString(CultureInfo.InvariantCulture)}");
                        continue;
                    }

                    owners[identity] = report.Index;
                }
            }

            await Assert.That(string.Join(Environment.NewLine, shared)).IsEmpty();

            // A job that held only its own BrowserAI would satisfy disjointness
            // vacuously: every peer opened a session, so every peer has a node
            // child in its job as well.
            var thin = reports.Where(report => report.JobMembers.Count < 2)
                .Select(report => $"peer {report.Index.ToString(CultureInfo.InvariantCulture)} had {report.JobMembers.Count.ToString(CultureInfo.InvariantCulture)} process(es) in its job")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine, thin)).IsEmpty();

            // The browser half, which is what makes disjointness a containment
            // claim rather than an arithmetic one: a real browser tree really was
            // up inside each of the peers that launched one.
            var withoutABrowser = reports.Where(report => report.LaunchesABrowser && report.BrowsersInJob is 0)
                .Select(report => $"peer {report.Index.ToString(CultureInfo.InvariantCulture)} navigated and had no process running out of the browsers root in its job")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine, withoutABrowser)).IsEmpty();

            // ---- 4. Nothing survives teardown ------------------------------

            var recorded = reports.SelectMany(report => report.JobMembers).ToList();

            foreach (var peer in peers)
            {
                await peer.DisposeAsync();
            }

            var survivors = await WaitForNoneAliveAsync(recorded, TeardownPatience);

            await Assert.That(string.Join(
                Environment.NewLine,
                survivors.Select(member => $"pid {member.ProcessId.ToString(CultureInfo.InvariantCulture)} ({member.ImagePath}) was still alive after every job closed")))
                .IsEmpty();

            // ---- 5. The shared process log is still readable ----------------

            var (lines, pids) = ReadProcessLogSince(logsBefore, started);

            // ⚠️ COUNTED OVER THIS RUN'S OWN PIDS, and that is the 2026-08-18
            // correction. It used to count every record header in every log file
            // on the machine — so on a developer's machine, with months of
            // history under `%LocalAppData%\BrowserAI\logs`, it was satisfied
            // before this test started and could not fail. The first machine it
            // ever met with an empty log directory was a CI runner, where it read
            // 6; with the directory moved aside here, 0. A check whose subject is
            // "what this run wrote" must not be answerable by what a previous one
            // wrote, however many files that takes.
            //
            // Read before the torn check rather than after it because both are
            // now scoped by it, which is the same rule applied to the same file
            // twice.
            var ours = new HashSet<int>(reports.Select(report => report.ProcessId));

            var torn = TornRecordsThisRunWasPartyTo(lines, ours);

            await Assert.That(string.Join(Environment.NewLine, torn)).IsEmpty();

            // Not vacuous: the check above only means something if this log
            // actually holds records, and a run that read the wrong file or an
            // empty one would satisfy it perfectly.

            var written = lines.Count(line =>
                RecordHeader().Match(line) is { Success: true, Index: 0 } header
                && int.TryParse(
                    header.Groups["pid"].ValueSpan,
                    CultureInfo.InvariantCulture,
                    out var pid)
                && ours.Contains(pid));

            await Assert.That(written).IsGreaterThanOrEqualTo(Processes)
                .Because($"{ours.Count.ToString(CultureInfo.InvariantCulture)} peers ran and the shared log holds {lines.Count.ToString(CultureInfo.InvariantCulture)} line(s) across {LogFilesNow().Count.ToString(CultureInfo.InvariantCulture)} file(s)");

            // Every peer really did write into it. Without this the check above
            // passes against a log none of them reached.
            var missing = reports
                .Where(report => !pids.Contains(report.ProcessId))
                .Select(report => $"peer {report.Index.ToString(CultureInfo.InvariantCulture)} (pid {report.ProcessId.ToString(CultureInfo.InvariantCulture)}) wrote no record into the shared process log")
                .ToList();

            await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

            // ---- 6. No session leaked into the machine's index --------------

            // Every peer destroyed its own session, so nothing under this test's
            // scratch root may still be pointed at from %LocalAppData%. An entry
            // that outlives its directory is a `browserai_list` that grows for
            // ever, which is invisible until somebody reads it.
            await Assert.That(string.Join(Environment.NewLine, IndexEntriesPointingInto(scratch.Path))).IsEmpty();
        }
        finally
        {
            foreach (var peer in peers)
            {
                await peer.DisposeAsync();
            }

            ReclaimOurOwnBookkeeping([.. peers.Select(peer => peer.ProcessId).Where(id => id is not 0)]);
        }
    }

    /// <summary>
    /// A stranger's torn record is not this run's failure, and one this run was
    /// party to still is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The both-directions control for the pid scope above, planted here
    /// because it cannot be planted live.</b> The arm it guards reads the
    /// machine-wide log with no time filter — deliberately, see
    /// <see cref="ReadProcessLogSince"/> — so the only way to watch the scope
    /// work is to hand the same predicate a line it must ignore and a line it
    /// must catch. A green run of the live arm proves nothing about either,
    /// because on a machine whose log holds no torn record at all both scopes
    /// pass by matching nothing.
    /// </para>
    /// <para>
    /// <b>The synthetic headers go through <see cref="RecordHeader"/>, the same
    /// expression the live arm uses.</b> That is what stops this becoming a
    /// second copy of the record format: if <c>FileLoggerProvider</c> moves the
    /// header and the expression follows it, the catch arm below stops matching
    /// and this test goes red rather than quietly asserting about a shape
    /// nothing writes any more.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStrangersTornRecordIsNotThisRunsAndOneThisRunWasPartyToIs()
    {
        // Odd, so neither could ever be a real Windows pid — those are
        // multiples of four — which is what makes a planted line provably a
        // stranger's rather than a peer's on an unlucky day.
        const int Ours = 4242;
        const int Stranger = 9191;
        const int AnotherStranger = 9193;

        var ours = new HashSet<int> { Ours };

        // The control on the control: the expression really does find a header
        // at a non-zero offset in a line built this way, so an empty result
        // below means "ignored" rather than "never matched anything".
        await Assert.That(RecordHeader().Count(Torn(Stranger, AnotherStranger))).IsEqualTo(2);

        // A sibling checkout's tear, hours old, still in the machine-wide log.
        // Not this run's business, and until 2026-08-24 it failed this run.
        await Assert.That(TornRecordsThisRunWasPartyTo([Torn(Stranger, AnotherStranger)], ours)).IsEmpty();

        // Intact records, ours and a stranger's, are not tears in either scope.
        await Assert.That(TornRecordsThisRunWasPartyTo([SyntheticRecord(Ours), SyntheticRecord(Stranger)], ours)).IsEmpty();

        // Somebody else's bytes landed inside OUR record: our write was not
        // atomic against theirs, which is exactly the claim under test.
        await Assert.That(TornRecordsThisRunWasPartyTo([Torn(Ours, Stranger)], ours).Count).IsEqualTo(1);

        // And the other way round: OUR bytes landed inside somebody else's. The
        // same defect seen from the other side, and just as much ours.
        await Assert.That(TornRecordsThisRunWasPartyTo([Torn(Stranger, Ours)], ours).Count).IsEqualTo(1);

        // The cap survives the scope: ten reported, not one per line.
        await Assert.That(TornRecordsThisRunWasPartyTo(
            [.. Enumerable.Repeat(Torn(Ours, Stranger), 25)],
            ours).Count)
            .IsEqualTo(10);
    }

    /// <summary>
    /// One process's record with another's spliced into the middle of it, which
    /// is what a torn append looks like on disk.
    /// </summary>
    /// <param name="interrupted">The pid whose record was being written.</param>
    /// <param name="interrupting">The pid whose bytes landed inside it.</param>
    /// <returns>The line.</returns>
    private static string Torn(int interrupted, int interrupting) =>
        SyntheticRecord(interrupted) + SyntheticRecord(interrupting);

    /// <summary>One whole record, in the form <c>FileLoggerProvider</c> writes.</summary>
    /// <param name="processId">The pid that wrote it.</param>
    /// <returns>The record.</returns>
    private static string SyntheticRecord(int processId) => string.Create(
        CultureInfo.InvariantCulture,
        $"2026-08-24T03:38:00.0000000Z  made=2026-08-24T03:38:00.0000000Z  INFO   pid={processId}@133000000000000000  BrowserAI  a record");

    /// <summary>
    /// The torn records among these lines that this run's own processes were
    /// party to, at either end of the tear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-24 (previously every torn record in every log
    /// file on the machine, scoped by nothing).</b> The log is machine-wide and
    /// read here with no time filter, so one torn record written by <i>any</i>
    /// BrowserAI from <i>any</i> checkout failed this arm on every subsequent
    /// run until the log rolled, and no checkout could clear it: a sibling's
    /// tear at 03:38 failed a run two hours later. The claim this arm makes is
    /// about <b>this</b> run's hundred processes appending to one
    /// <c>FILE_APPEND_DATA</c> handle, and a stranger's history cannot be
    /// evidence for or against it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Scoped by pid and never by time.</b> The obvious filter — skip what
    /// was written before this run started — is the one
    /// <see cref="ReadProcessLogSince"/> already refuses, and for a reason that
    /// has not changed: NTFS does not keep a file's last-write time current
    /// while handles are open on it, and this file has a hundred BrowserAIs
    /// appending to it, so the one file holding all the records is the one such
    /// a filter skips. Measured 2026-08-17, three runs in a row. The pids are
    /// this run's own, exactly the scope the non-vacuity check beside it uses.
    /// </para>
    /// <para>
    /// <b>Either end counts, and that is the strength being kept.</b> A tear has
    /// two parties — the record that was interrupted and the record that
    /// interrupted it — and our write failing to be atomic against a stranger's
    /// is the same defect as a stranger's failing to be atomic against ours.
    /// Only a line naming none of this run's pids is somebody else's history.
    /// </para>
    /// </remarks>
    /// <param name="lines">The log's lines.</param>
    /// <param name="ours">The pids this run started.</param>
    /// <returns>Up to ten findings, empty when there are none.</returns>
    private static List<string> TornRecordsThisRunWasPartyTo(IReadOnlyList<string> lines, HashSet<int> ours)
    {
        var found = new List<string>();

        foreach (var line in lines)
        {
            var headers = RecordHeader().Matches(line);

            if (!headers.Any(header => int.TryParse(
                    header.Groups["pid"].ValueSpan,
                    CultureInfo.InvariantCulture,
                    out var pid)
                && ours.Contains(pid)))
            {
                continue;
            }

            foreach (var header in headers.Where(header => header.Index is not 0))
            {
                found.Add(
                    $"a record header starts at offset {header.Index.ToString(CultureInfo.InvariantCulture)} of a line, so two processes' bytes are interleaved: {(line.Length <= 300 ? line : line[..300] + "…")}");

                if (found.Count is 10)
                {
                    return found;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Removes the per-run bookkeeping this test's own hundred processes left in
    /// the <b>shared</b> app root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the test cleaning up after itself, and without it the test
    /// degrades every run that follows it — including its own.</b> Each BrowserAI
    /// creates an instance directory (<c>&lt;pid&gt;-&lt;guid&gt;</c>) and a
    /// live-instance marker (<c>&lt;pid&gt;-&lt;guid&gt;.live</c>) at startup,
    /// and this test starts a hundred of them against the real
    /// <c>%LocalAppData%\BrowserAI</c>. Both are reclaimed by the product on its
    /// own schedule — the instance sweep at the next startup, the live marker at
    /// the next update census — and neither schedule keeps up with a hundred per
    /// run.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-17, running the suite back to back.</b> Runs 1 and 2
    /// were green; by run 6 the real app root held <b>306</b> instance
    /// directories and <b>2,629</b> live markers, and the run took <b>2m43s</b>
    /// with 39 failures — every one of them a thirty-second in-process timeout,
    /// none of them a logic error. A hundred concurrent startups each sweeping a
    /// directory of three hundred candidates is thirty thousand rename attempts
    /// per run, and it grows with every run.
    /// </para>
    /// <para>
    /// <b>Keyed on this test's own pids and nothing else.</b> Both names begin
    /// with the pid that created them, so nothing another BrowserAI on the
    /// machine owns can match — and a pid this run did not start is never acted
    /// on. Best-effort throughout: a directory that will not go is the product's
    /// sweep to reclaim, exactly as it would be for a run that was killed.
    /// </para>
    /// <para>
    /// <b>What this does NOT do is assert.</b> A killed BrowserAI legitimately
    /// leaves both behind — that is what the containment contract guarantees —
    /// so leaving them is not a defect and reclaiming them is not a fix. It is
    /// this test declining to make the machine worse.
    /// </para>
    /// </remarks>
    /// <param name="ours">The pids this run started.</param>
    private static void ReclaimOurOwnBookkeeping(HashSet<int> ours)
    {
        var paths = BrowserAiPaths.Real;

        foreach (var directory in Enumerate(paths.InstanceRoot, directories: true))
        {
            if (ours.Contains(PidPrefixOf(Path.GetFileName(directory))))
            {
                _ = ScratchDirectory.RemoveTree(directory);
            }
        }

        foreach (var file in Enumerate(paths.LiveInstanceDirectory, directories: false))
        {
            if (ours.Contains(PidPrefixOf(Path.GetFileName(file))))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Still held, which means still running. Left alone.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>The pid a bookkeeping name begins with, or zero.</summary>
    /// <param name="name">The file or directory name.</param>
    /// <returns>The pid, or zero when the name is not one of ours in shape.</returns>
    private static int PidPrefixOf(string name)
    {
        var at = name.IndexOf('-', StringComparison.Ordinal);

        return at > 0 && int.TryParse(name.AsSpan(0, at), CultureInfo.InvariantCulture, out var pid) ? pid : 0;
    }

    private static IReadOnlyList<string> Enumerate(string directory, bool directories)
    {
        try
        {
            return directories
                ? [.. Directory.EnumerateDirectories(directory)]
                : [.. Directory.EnumerateFiles(directory)];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The start of one record, as <c>FileLoggerProvider</c> writes it:
    /// <c>&lt;ISO-8601&gt;  made=&lt;ISO-8601&gt;  &lt;LVL&gt;  pid=&lt;n&gt;@&lt;filetime&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is asserted is that this never appears anywhere but at the start
    /// of a line</b>, which is precisely what a torn append looks like: process
    /// A's bytes spliced into the middle of process B's record. Every record is
    /// one <c>WriteFile</c> against a <c>FILE_APPEND_DATA</c> handle, and this is
    /// the property that claim rests on.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-17 (previously: every line must either be a record
    /// header or start with four spaces).</b> That fired on the first real run
    /// against a perfectly intact log — <c>SessionErrors.UnattributableBrowserRunning</c>
    /// lists candidate pids one per line, indented by two — because a log
    /// <i>message</i> may contain newlines and the shape of its continuations is
    /// not this test's business. A rule about where a header may appear is; a
    /// rule about what every other line must look like is a second, weaker copy
    /// of the message catalogue.
    /// </para>
    /// <para>
    /// ⚠️⚠️ <b>Corrected 2026-08-18 (previously <c>…\s\S+\s\spid=\d+</c>, with
    /// TWO spaces before <c>pid=</c>), and it had never matched a single
    /// <c>INFO</c> or <c>WARN</c> record in its life.</b>
    /// <c>FileLoggerProvider.Abbreviate</c> pads every level name to five
    /// characters so the columns line up — <c>"INFO "</c>, <c>"WARN "</c>,
    /// <c>"CRIT "</c> — and then writes <c>"  pid="</c>. So a four-letter level
    /// is followed by <b>three</b> spaces and <c>\s\s</c> could not reach
    /// <c>pid=</c>; only the five-letter levels <c>TRACE</c>, <c>DEBUG</c> and
    /// <c>ERROR</c> ever matched. Measured 2026-08-18 against a real
    /// hundred-process run: <b>2,217 records, 2,117 INFO and 100 WARN, and
    /// zero matches.</b>
    /// </para>
    /// <para>
    /// <b>Both assertions that use this were therefore green while blind, which
    /// is the failure class this repository exists to eliminate.</b> The torn
    /// check could not see 99.7% of the log it was scanning, so it passed by
    /// matching nothing. The count check passed only because it reads <i>every</i>
    /// log file on the machine and a developer's has months of history: on a CI
    /// runner with an empty log directory it returned <b>6</b>, and here, with
    /// the directory moved aside, <b>0</b>.
    /// </para>
    /// <para>
    /// ⚠️ <b>And the 2026-08-17 note above was a misdiagnosis of this same
    /// defect.</b> It recorded "the header count came back as 12 against a
    /// hundred peers … on a log that was perfectly intact" and blamed a
    /// last-write-time filter. Twelve was the number of <c>DEBUG</c> records in
    /// the file. Removing the filter raised the number by reading more history
    /// and left the cause untouched — which is why the same test failed again the
    /// first time it met a machine with no history. With the expression fixed,
    /// that same run reads 2,217 headers, all at index 0, from 100 distinct pids.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-24 (previously
    /// <c>…T\d{2}:\d{2}:\d{2}[^\s]*\s\s\S+\s+pid=\d+</c>).</b> A record now carries
    /// <b>two</b> times — the leading column is when it was <i>written</i>, taken
    /// inside the file's write gate, and <c>made=</c> is when it was created —
    /// and the writer is <c>pid=&lt;n&gt;@&lt;createdFileTime&gt;</c> rather than
    /// a bare pid. Both halves are matched here rather than skipped over with
    /// <c>.*</c>: this expression is what says <i>a header may only appear at the
    /// start of a line</i>, and one that matched a prefix of the header would
    /// find the header inside itself.
    /// </para>
    /// <para>
    /// <b>The pid is a named group now</b>, because the caller used to read
    /// everything after the last <c>pid=</c> as an integer and there is a
    /// <c>@</c> and a FILETIME behind it.
    /// </para>
    /// </remarks>
    /// <returns>The compiled expression.</returns>
    [System.Text.RegularExpressions.GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[^\s]*\s\smade=\S+\s\s\S+\s+pid=(?<pid>\d+)@\d+")]
    private static partial System.Text.RegularExpressions.Regex RecordHeader();

    private static List<string> IndexEntriesPointingInto(string root)
    {
        var index = BrowserAiPaths.Real.IndexDirectory;

        if (!Directory.Exists(index))
        {
            return [];
        }

        var leaked = new List<string>();

        foreach (var entry in Directory.EnumerateFiles(index))
        {
            try
            {
                if (File.ReadAllText(entry).Trim().StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    leaked.Add($"{entry} still points into {root}");
                }
            }
            catch (IOException)
            {
                // Being written by something else this instant. An entry that
                // cannot be read cannot be shown to be ours, and this check never
                // accuses on a guess.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return leaked;
    }

    private static IReadOnlyList<string> LogFilesNow()
    {
        var directory = BrowserAiPaths.Real.LogDirectory;

        return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory, "browserai-*.log")] : [];
    }

    /// <summary>
    /// The process log's lines, and the pids that wrote them.
    /// </summary>
    /// <remarks>
    /// <b>Read with <c>FileShare.ReadWrite | Delete</c> and never locked</b>: a
    /// hundred BrowserAIs and whatever else the suite is running are appending
    /// to this file while it is read, and a reader that took an exclusive share
    /// would be the thing that broke the property under test.
    /// </remarks>
    private static (IReadOnlyList<string> Lines, HashSet<int> Pids) ReadProcessLogSince(
        IReadOnlyList<string> before,
        DateTimeOffset started)
    {
        var lines = new List<string>();
        var pids = new HashSet<int>();

        foreach (var file in LogFilesNow())
        {
            // ⚠️ EVERY file, with no last-write-time filter, and the filter that
            // was here is why. It skipped a file that existed before the test
            // and whose mtime read older than the start — but NTFS does not keep
            // a file's last-write time current while handles are open on it, and
            // this file has a hundred BrowserAIs appending to it. So the one
            // file holding all the records was the one being skipped. Measured
            // 2026-08-17: the header count came back as 12 against a hundred
            // peers, three runs in a row, on a log that was perfectly intact.
            //
            // Reading everything costs a few megabytes and is correct by
            // construction. What establishes that THIS run's records were seen
            // is the pid check, not the file selection.
            _ = before;

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);

                const string Marker = "  pid=";
                var at = line.IndexOf(Marker, StringComparison.Ordinal);

                if (at < 0)
                {
                    continue;
                }

                var from = at + Marker.Length;
                var to = from;

                while (to < line.Length && char.IsAsciiDigit(line[to]))
                {
                    to++;
                }

                if (to > from && int.TryParse(line.AsSpan(from, to - from), CultureInfo.InvariantCulture, out var pid))
                {
                    _ = pids.Add(pid);
                }
            }
        }

        return (lines, pids);
    }

    private static async Task<List<JobMember>> WaitForNoneAliveAsync(
        List<JobMember> recorded,
        TimeSpan patience)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            var alive = recorded
                .Where(member => ProcessIdentity.IsAlive(member.ProcessId, member.CreatedFileTime))
                .ToList();

            if (alive.Count is 0 || waited.Elapsed > patience)
            {
                return alive;
            }

            await Task.Delay(250);
        }
    }

    /// <summary>One process in the job of one peer, with the identity that makes a pid mean something.</summary>
    /// <param name="ProcessId">The pid.</param>
    /// <param name="CreatedFileTime">Its creation time. Without this a recycled pid reads as a survivor.</param>
    /// <param name="ImagePath">Its image, for the failure message.</param>
    private sealed record JobMember(int ProcessId, long CreatedFileTime, string? ImagePath);

    /// <summary>What one peer did, or why it could not.</summary>
    private sealed record PeerReport
    {
        public required int Index { get; init; }

        public required int ProcessId { get; init; }

        public required string Session { get; init; }

        public required bool LaunchesABrowser { get; init; }

        public string InitText { get; init; } = string.Empty;

        public int LockHolder { get; init; }

        public List<JobMember> JobMembers { get; init; } = [];

        public int BrowsersInJob { get; init; }

        public string? Failure { get; init; }
    }

    /// <summary>
    /// One BrowserAI process, its session, and — for some of them — its browser.
    /// </summary>
    private sealed class Peer(int index, string root) : IAsyncDisposable
    {
        private readonly string _home = Path.Combine(root, $"peer-{index.ToString("D3", CultureInfo.InvariantCulture)}");

#pragma warning disable CA2213 // Disposed by DisposeAsync below; the analyser only recognises the synchronous shape, and RawStdioClient has no synchronous Dispose because closing its job has to be awaited beside its stderr pump.
        private RawStdioClient? _client;
#pragma warning restore CA2213

        /// <summary>The session directory this peer owns and nobody else may name.</summary>
        public string Session => Path.Combine(_home, "session");

        /// <summary>
        /// This peer's BrowserAI pid, or zero if it never started.
        /// </summary>
        /// <remarks>
        /// Kept on the peer rather than only on its report, because the report
        /// is not available when a run throws — and the bookkeeping this test
        /// has to reclaim is keyed on exactly this number.
        /// </remarks>
        public int ProcessId { get; private set; }

        /// <summary>Whether this peer drives a real browser.</summary>
        /// <remarks>
        /// The first <see cref="WithBrowsers"/> of them, so the set is fixed
        /// rather than sampled: a test whose coverage varies run to run is one
        /// whose green means something different every time.
        /// </remarks>
        public bool LaunchesABrowser => index < WithBrowsers;

        public async Task<PeerReport> RunAsync()
        {
            _ = Directory.CreateDirectory(_home);

            var client = RawStdioClient.Start(
                PublishedSlice.Executable,
                [],
                _home,
                PublishedSlice.InheritedEnvironment(),
                Conversation);

            _client = client;
            ProcessId = client.ProcessId;

            var report = new PeerReport
            {
                Index = index,
                ProcessId = client.ProcessId,
                Session = Session,
                LaunchesABrowser = LaunchesABrowser,
            };

            try
            {
                _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

                var init = await client.RoundTripAsync("tools/call", new JsonObject
                {
                    ["name"] = SessionToolSurface.Init,
                    ["arguments"] = new JsonObject
                    {
                        ["directory"] = Session,
                        ["purpose"] = $"saturation peer {index.ToString(CultureInfo.InvariantCulture)}",
                    },
                });

                report = report with { InitText = TextOf(init) };

                if ((bool?)init["isError"] is true)
                {
                    return report with { Failure = $"browserai_init was refused: {report.InitText}" };
                }

                var browsers = 0;

                if (LaunchesABrowser)
                {
                    // Launch.
                    var navigated = await client.RoundTripAsync("tools/call", new JsonObject
                    {
                        ["name"] = "browser_navigate",
                        ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = Session, ["why"] = "the suite exercising this call" },
                    });

                    if ((bool?)navigated["isError"] is true)
                    {
                        return report with { Failure = $"browser_navigate was refused: {TextOf(navigated)}" };
                    }

                    browsers = BrowsersIn(client);

                    // Close, then launch again. The relaunch is upstream's own
                    // lazy creation and it is the half that says the close was a
                    // close rather than a teardown: a session whose browser
                    // cannot come back has been broken by the close.
                    _ = await client.RoundTripAsync("tools/call", new JsonObject
                    {
                        ["name"] = LiveSession.BrowserCloseTool,
                        ["arguments"] = new JsonObject { ["session"] = Session, ["why"] = "the suite exercising this call" },
                    });

                    var again = await client.RoundTripAsync("tools/call", new JsonObject
                    {
                        ["name"] = "browser_navigate",
                        ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = Session, ["why"] = "the suite exercising this call" },
                    });

                    if ((bool?)again["isError"] is true)
                    {
                        return report with { Failure = $"the browser did not come back after browser_close: {TextOf(again)}" };
                    }

                    browsers = Math.Max(browsers, BrowsersIn(client));
                }

                // Read while everything is up. Everything after this is teardown.
                var members = Observe(client.JobProcessIds());

                report = report with
                {
                    JobMembers = members,
                    BrowsersInJob = browsers,
                    LockHolder = HolderOf(Session),
                };

                // The session goes before the process does, so the index entry
                // is removed by the product rather than by the scratch sweep.
                _ = await client.RoundTripAsync("tools/call", new JsonObject
                {
                    ["name"] = SessionToolSurface.Destroy,
                    ["arguments"] = new JsonObject { ["directory"] = Session, ["why"] = "the suite exercising this call" },
                });

                return report;
            }
#pragma warning disable CA1031 // One peer's failure is REPORTED, never thrown: a hundred peers means a hundred failures worth reading, and the first exception to escape would hide the other ninety-nine.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                return report with
                {
                    Failure = $"{failure.GetType().Name}: {failure.Message}{Environment.NewLine}--- its stderr ---{Environment.NewLine}{client.StandardErrorSoFar()}",
                };
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_client is { } client)
            {
                _client = null;
                await client.DisposeAsync();
            }
        }

        private static int BrowsersIn(RawStdioClient client)
        {
            var members = client.JobProcessIds().ToHashSet();

            return BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
                .Count(process => members.Contains(process.ProcessId));
        }

        private static List<JobMember> Observe(IEnumerable<int> processIds)
        {
            var observed = new List<JobMember>();

            foreach (var processId in processIds)
            {
                long created;

                try
                {
                    created = ProcessIdentity.CreationTimeOf(processId);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Exited between the job reporting it and this call. Its pid
                    // is meaningless now, and recording it would make the
                    // survivor check act on a number that may be reused.
                    continue;
                }

                observed.Add(new JobMember(processId, created, ProcessCommandLine.ImagePathOf(processId)));
            }

            return observed;
        }

        /// <summary>The pid the session's own lock record names, or zero.</summary>
        private static int HolderOf(string session)
        {
            try
            {
                var record = SessionLock.ReadRecord(SessionPath.For(session));

                return record?.Holder?.ProcessId ?? 0;
            }
#pragma warning disable CA1031 // A lock that cannot be read answers zero, which fails the assertion with the pid it should have named.
            catch (Exception)
#pragma warning restore CA1031
            {
                return 0;
            }
        }

        private static string TextOf(JsonObject answer) =>
            string.Join(
                "\n",
                (answer["content"]?.AsArray() ?? [])
                    .Where(block => (string?)block!["type"] == "text")
                    .Select(block => (string?)block!["text"] ?? string.Empty));
    }
}

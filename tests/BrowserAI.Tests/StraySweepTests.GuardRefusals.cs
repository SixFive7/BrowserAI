// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The second half of <see cref="ASweepLeavesABrowserAloneWhileALiveSessionHoldsItsDirectory"/>:
/// what it waits out, what it never waits out, and the account it gives of a pass that
/// did not end the probe.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's 207, 2026-11-18, in his words verbatim: <i>"207 rec"</i></b>,
/// the recommendation of his question 198. That arm went red once, on 2026-11-17 at
/// 19:19:57Z, where its second pass left the probe running, and what was kept could not
/// say which branch of the pass had done it. The pass decides with one open of
/// <c>browserai.lock</c>, and the kernel refuses that open for any other handle that holds
/// write or delete access, or that does not share read and write, whichever process holds
/// it (kb/windows/processes.md, measured 2026-08-30).
/// </para>
/// <para>
/// <b>Measured 2026-11-18 by plants of this file's arms, never committed in that form</b>
/// (kb/windows/detection.md): a reader sharing only read in this process, and a second
/// process sharing nothing, each opened on the guard after the session let it go, turned
/// the second pass into the guard's refusal and the arm's last assertion red with the
/// message of 19:19:57Z; a reader sharing read, write and delete did not. A probe ended
/// before the pass failed the same assertion with the same message, which is why every
/// failure here carries <see cref="WhatThePassDidAbout"/>.
/// </para>
/// </remarks>
internal sealed partial class StraySweepTests
{
    /// <summary>The category of the process log's record of each refusal the second half waited out.</summary>
    private const string GuardRefusalCategory = "BrowserAI.Tests.SweepGuardRefusal";

    /// <summary>
    /// A reader that shares only read, put on the guard after the session lets it go and let
    /// go itself after two refusals, is waited out, and each refusal is in the process log
    /// with this test host named as the holder.
    /// </summary>
    /// <remarks>
    /// <b>The order is forced, not timed</b>: the reader is let go from the second half's own
    /// call after its second refusal, so two passes meet it and the third does not.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task TheSecondHalfWaitsOutAReaderOnTheLetGoGuardAndRecordsWhoHeldIt()
    {
        using var scratch = await ScratchDirectory.CreateAsync("sweep-guard-reader");
        using var scope = new JobObjectScope();

        var (session, pid, created) = await HeldThenLetGoAsync(scratch, scope);
        var guard = SessionPath.For(session).LockFile;

        await using var reader = new FileStream(guard, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);

        var second = await TheSecondHalfAsync(
            pid,
            created,
            session,
            async refusals =>
            {
                if (refusals is 2)
                {
                    await reader.DisposeAsync();
                }
            });

        await Assert.That(second.LastPass.Terminated.Select(entry => entry.ProcessId)).Contains(pid).Because(second.Account);
        await Assert.That(second.Refusals.Count).IsGreaterThanOrEqualTo(2).Because(second.Account);
        await Assert.That(second.Refusals[0]).Contains("(this test host)");
        await WaitUntilGoneAsync(pid, created);

        var records = await ProcessLogRecords.SinceItsCreationAsync(Environment.ProcessId, ProcessIdentity.CreationTimeOf(Environment.ProcessId));

        await Assert.That(records).Contains($"waited out the stray sweep's refusal of {guard} in pass 1");
        await Assert.That(records).Contains($"waited out the stray sweep's refusal of {guard} in pass 2");
    }

    /// <summary>
    /// Another process holding the let-go guard is waited out once it has gone, and the
    /// refusal names that process.
    /// </summary>
    /// <remarks>
    /// The probe's <c>hold-file</c> opens the guard for read and write sharing nothing. It runs
    /// the planted image, so a pass counts it as a candidate no window claims and leaves it
    /// running; the second half ends it after its first refusal.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task TheSecondHalfWaitsOutAnotherProgramOnTheLetGoGuardAndNamesThatProgram()
    {
        using var scratch = await ScratchDirectory.CreateAsync("sweep-guard-process");
        using var scope = new JobObjectScope();

        var (session, pid, created) = await HeldThenLetGoAsync(scratch, scope);

        var ready = Path.Combine(scratch.Path, "holder.json");
        var holder = await scope.LaunchAsync(
            await PlantedProbe.ExecutablePathAsync(),
            scratch.Path,
            "hold-file",
            SessionPath.For(session).LockFile,
            ready);
        var holderCreated = ProcessIdentity.CreationTimeOf(holder.Id);
        _ = await ProbeReport.ReadAsync(ready, Patience);

        var second = await TheSecondHalfAsync(
            pid,
            created,
            session,
            async refusals =>
            {
                if (refusals is 1)
                {
                    ProcessIdentity.Terminate(holder.Id, holderCreated);
                    await WaitUntilGoneAsync(holder.Id, holderCreated);
                }
            });

        await Assert.That(second.LastPass.Terminated.Select(entry => entry.ProcessId)).Contains(pid).Because(second.Account);
        await Assert.That(second.Refusals.Count).IsGreaterThanOrEqualTo(1).Because(second.Account);
        await Assert.That(second.Refusals[0]).Contains($"pid {holder.Id.ToString(CultureInfo.InvariantCulture)} ");
        await Assert.That(second.Refusals[0]).DoesNotContain("(this test host)");
        await WaitUntilGoneAsync(pid, created);
    }

    /// <summary>
    /// A reader that shares read, write and delete does not hold up the second half: its first
    /// pass ends the probe, and nothing is waited out.
    /// </summary>
    /// <remarks>
    /// <b>The bound of the cause, kept as an arm.</b> Such a handle holds no write access and
    /// shares what the sweep's open asks for, so the kernel passes the open both ways, and a
    /// third party opening the guard like this can never be what refuses a pass.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task AReaderSharingReadWriteAndDeleteOnTheLetGoGuardDoesNotHoldUpTheSecondHalf()
    {
        using var scratch = await ScratchDirectory.CreateAsync("sweep-guard-sharing");
        using var scope = new JobObjectScope();

        var (session, pid, created) = await HeldThenLetGoAsync(scratch, scope);

        await using var reader = new FileStream(
            SessionPath.For(session).LockFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.Asynchronous);

        var second = await TheSecondHalfAsync(pid, created, session);

        await Assert.That(second.LastPass.Terminated.Select(entry => entry.ProcessId)).Contains(pid).Because(second.Account);
        await Assert.That(second.Passes).IsEqualTo(1).Because(second.Account);
        await Assert.That(second.Refusals).IsEmpty();
        await WaitUntilGoneAsync(pid, created);
    }

    /// <summary>
    /// A second half whose probe is gone stops at its first pass, waits out nothing, and says
    /// the probe was neither spared nor alive.
    /// </summary>
    /// <remarks>
    /// <b>Any outcome but the guard's refusal ends the wait at once.</b> The arm's assertion
    /// fails on this outcome with the message of 19:19:57Z, word for word, and the account
    /// beside it is what tells it from the guard's refusal.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task ASecondHalfWhoseProbeIsGoneStopsAtItsFirstPassAndSaysSo()
    {
        using var scratch = await ScratchDirectory.CreateAsync("sweep-guard-gone");
        using var scope = new JobObjectScope();

        var (session, pid, created) = await HeldThenLetGoAsync(scratch, scope);

        ProcessIdentity.Terminate(pid, created);
        await WaitUntilGoneAsync(pid, created);

        var second = await TheSecondHalfAsync(pid, created, session);
        var id = pid.ToString(CultureInfo.InvariantCulture);

        await Assert.That(second.LastPass.Terminated.Select(entry => entry.ProcessId)).DoesNotContain(pid);
        await Assert.That(second.Passes).IsEqualTo(1).Because(second.Account);
        await Assert.That(second.Refusals).IsEmpty();
        await Assert.That(second.Account).Contains($"pid {id} spared: no");
        await Assert.That(second.Account).Contains($"pid {id} alive after the pass: no");
    }

    /// <summary>The arm's own order up to its second half: held, swept and spared, let go.</summary>
    /// <param name="scratch">The arm's scratch directory.</param>
    /// <param name="scope">The job the probe runs in.</param>
    /// <returns>The session directory and the probe's identity.</returns>
    private static async Task<(string Session, int ProcessId, long Created)> HeldThenLetGoAsync(ScratchDirectory scratch, JobObjectScope scope)
    {
        var session = await NewSessionDirectoryAsync(scratch, "owned");
        var profile = Path.Combine(session, SessionLayout.ProfileFolderName);
        var (pid, created) = await PublishAsync(scope, scratch, profile);

        var held = await SessionLock.TryAcquireAsync(
            SessionPath.For(session),
            new SessionLockRequest { Browser = "chromium", Purpose = "driving a checkout flow" },
            NullLogger.Instance);

        await Assert.That(held.Taken).IsTrue();

        await using (held.Acquired)
        {
            var refused = await SweepAsync();

            await Assert.That(refused.Terminated).IsEmpty();
            await Assert.That(refused.Spared.Single(entry => entry.ProcessId == pid).Why)
                .IsEqualTo(SessionLock.GuardOpenInAModeTheSweepCannotShare(SessionPath.For(session)));
            await Assert.That(ProcessIdentity.IsAlive(pid, created)).IsTrue();
        }

        return (session, pid, created);
    }

    /// <summary>
    /// Passes over the probe until one ends it, made again only while the one thing that spared
    /// it is the guard's sharing violation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The guard's refusal is waited out, and nothing else is.</b> Once the session has let
    /// the directory go, a pass refused the guard has met some other handle on it, which the
    /// product refuses on as its design requires, so the next pass is made after the rename
    /// window's own pause. A pass that spared the probe for any other reason, left it
    /// unattributed, or did not find it, ends the second half at once.
    /// </para>
    /// <para>
    /// <b>Bounded by <see cref="RenameWindow.Budget"/></b>, the product's own limit for a handle
    /// passing through this same file: a handle that outlasts it is not one passing through.
    /// <b>The bound counts the passes and the pauses between them, and not this method's own
    /// account of each refusal</b>: the Restart Manager walks every handle on the machine, and
    /// on 2026-11-18 at 91.7 to 93.7 % of the commit limit one refusal's account and the pass
    /// after it took between 5 and 19 s, so a bound that counted them would end an arm that
    /// lets its plant go after two refusals before the plant had gone.
    /// </para>
    /// <para>
    /// <b>The first pause is the rename window's, <c>SessionTimes.RenameRetryFirstDelayMilliseconds</c>,
    /// doubled after every refusal with no ceiling</b>, so a handle that never lets go meets at
    /// most fourteen passes before the bound, and not hundreds, each of which would put a
    /// warning in the shared process log and ask the Restart Manager again: at the values of
    /// 2026-11-18, 5 ms and 30 s, thirteen pauses sum past the budget.
    /// </para>
    /// <para>
    /// <b>Every refusal waited out is a warning in the machine's process log</b>, with the
    /// holders the Restart Manager names at that moment, so a run that stays green still says
    /// who held the guard.
    /// </para>
    /// </remarks>
    /// <param name="pid">The probe.</param>
    /// <param name="created">The probe's creation time.</param>
    /// <param name="session">The session directory the probe's window names.</param>
    /// <param name="afterARefusal">Called with the count of refusals so far, after each one is recorded.</param>
    /// <param name="arm">The calling arm, for the process log.</param>
    /// <returns>The last pass, how many were made, every refusal's account, and the account of the last pass.</returns>
    private static async Task<SecondHalf> TheSecondHalfAsync(
        int pid,
        long created,
        string session,
        Func<int, Task>? afterARefusal = null,
        [CallerMemberName] string arm = "")
    {
        var location = SessionPath.For(session);
        var refusedTheGuard = SessionLock.GuardOpenInAModeTheSweepCannotShare(location);
        var refusals = new List<string>();
        var looking = Stopwatch.StartNew();
        var pause = SessionTimes.RenameRetryFirstDelayMilliseconds;
        var passes = 0;

        while (true)
        {
            var pass = await SweepAsync();
            passes++;

            if (pass.Terminated.Any(entry => entry.ProcessId == pid))
            {
                return new SecondHalf(pass, passes, refusals, "the pass ended it");
            }

            // The account of the pass is not part of the wait: see the remarks.
            looking.Stop();

            var (account, holders) = WhatThePassDidAbout(pass, pid, created, session);

            if (pass.Spared.Where(entry => entry.ProcessId == pid).Select(entry => entry.Why).ToList() is not [var why]
                || !string.Equals(why, refusedTheGuard, StringComparison.Ordinal))
            {
                return new SecondHalf(pass, passes, refusals, account);
            }

            refusals.Add(account);
            AnnounceTheRefusal(location.LockFile, passes, arm, pid, why, holders);

            if (afterARefusal is not null)
            {
                await afterARefusal(refusals.Count);
            }

            if (looking.Elapsed >= RenameWindow.Budget)
            {
                return new SecondHalf(
                    pass,
                    passes,
                    refusals,
                    $"{account}{Environment.NewLine}the guard's refusal outlasted RenameWindow.Budget, {RenameWindow.Budget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s of passes and pauses, over {passes.ToString(CultureInfo.InvariantCulture)} passes");
            }

            looking.Start();
            await Task.Delay(pause);
            pause *= 2;
        }
    }

    /// <summary>
    /// What a pass did about one probe, read when it did not end it: its census, every line it
    /// holds about the probe, and the guard as it is now.
    /// </summary>
    /// <param name="pass">The pass.</param>
    /// <param name="pid">The probe.</param>
    /// <param name="created">The probe's creation time.</param>
    /// <param name="session">The session directory.</param>
    /// <returns>The account, one fact a line, and the holders the Restart Manager named in it.</returns>
    private static (string Account, string Holders) WhatThePassDidAbout(StraySweepResult pass, int pid, long created, string session)
    {
        var lockFile = SessionPath.For(session).LockFile;

        // First and at once: a handle that refused the pass may be gone a moment later.
        var guard = LockFile.Probe(lockFile);
        var holders = HoldersOf(lockFile);
        var spared = pass.Spared.Where(entry => entry.ProcessId == pid).Select(entry => entry.Why).ToList();
        var id = pid.ToString(CultureInfo.InvariantCulture);

        var account = string.Join(
            Environment.NewLine,
            string.Empty,
            $"the pass: {pass.Summary}",
            $"pid {id} spared: {(spared.Count is 0 ? "no" : string.Join(" | ", spared))}",
            $"pid {id} unattributable: {(pass.Unattributable.Any(entry => entry.ProcessId == pid) ? "yes" : "no")}",
            $"pid {id} alive after the pass: {(ProcessIdentity.IsAlive(pid, created) ? "yes" : "no")}",
            $"{SessionLayout.LockFileName} after the pass: {guard.State}{(guard.Why is { } why ? $", {why}" : string.Empty)}",
            $"{SessionLayout.LockFileName} held after the pass by: {holders}");

        return (account, holders);
    }

    /// <summary>Every process the Restart Manager says holds a file, with this test host marked.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The holders, or why there is no answer.</returns>
    private static string HoldersOf(string file)
    {
        try
        {
            var holders = RestartManager.HoldersOf(file);

            return holders.Count is 0
                ? "no process the Restart Manager can name"
                : string.Join(
                    ", ",
                    holders.Select(holder =>
                        $"pid {holder.ProcessId.ToString(CultureInfo.InvariantCulture)} '{holder.Description}'"
                        + (holder.ProcessId == Environment.ProcessId ? " (this test host)" : string.Empty)));
        }
        catch (Win32Exception failure)
        {
            return $"unknown, the Restart Manager refused the question: {failure.Message}";
        }
    }

    /// <summary>Puts one waited-out refusal in the machine's process log.</summary>
    /// <remarks>
    /// <b>Never fatal</b>, for the reason the harness's other announcements are not: a wait
    /// that could not describe itself has still waited.
    /// </remarks>
    /// <param name="guard">The guard the pass was refused.</param>
    /// <param name="pass">Which pass of the second half it was.</param>
    /// <param name="arm">The arm.</param>
    /// <param name="pid">The probe the pass spared.</param>
    /// <param name="why">The reason the pass recorded.</param>
    /// <param name="holders">What the Restart Manager named.</param>
    private static void AnnounceTheRefusal(string guard, int pass, string arm, int pid, string why, string holders)
    {
        try
        {
            using var log = ProcessLog.Create(BrowserAiPaths.Real, LogLevel.Information);

            GuardRefusalAnnouncement.WaitedOut(log.Factory.CreateLogger(GuardRefusalCategory), guard, pass, arm, pid, why, holders);
        }
#pragma warning disable CA1031 // An announcement never becomes the outage.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>What <see cref="TheSecondHalfAsync"/> found.</summary>
    /// <param name="LastPass">The last pass made.</param>
    /// <param name="Passes">How many passes were made.</param>
    /// <param name="Refusals">The account of each pass refused the guard and waited out.</param>
    /// <param name="Account">The account of the last pass, or a short sentence when it ended the probe.</param>
    private sealed record SecondHalf(StraySweepResult LastPass, int Passes, IReadOnlyList<string> Refusals, string Account);
}

/// <summary>The one record the second half writes about a refusal it waited out.</summary>
/// <remarks>
/// Source-generated so the message is a single literal, at <see cref="LogLevel.Warning"/>:
/// a refusal of a guard no session holds is a handle somebody else had open, and the next
/// investigation asks whose.
/// </remarks>
internal static partial class GuardRefusalAnnouncement
{
    /// <summary>Records one refusal the second half waited out.</summary>
    /// <param name="logger">The process log's logger.</param>
    /// <param name="guard">The guard.</param>
    /// <param name="pass">Which pass of the second half it was.</param>
    /// <param name="arm">The arm.</param>
    /// <param name="probe">The probe the pass spared.</param>
    /// <param name="why">The reason the pass recorded.</param>
    /// <param name="holders">What the Restart Manager named.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The test harness waited out the stray sweep's refusal of {Guard} in pass {Pass} of {Arm}: the pass spared pid {Probe} because {Why}, and the Restart Manager named {Holders} as holding the file.")]
    public static partial void WaitedOut(ILogger logger, string guard, int pass, string arm, int probe, string why, string holders);
}

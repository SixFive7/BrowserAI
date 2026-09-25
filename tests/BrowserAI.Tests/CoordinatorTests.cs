// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The coordinator's pipe, end to end: what it is called, what it is, who wins
/// when many start at once, and what a second start does; and the coordinator's
/// loop, which waits for the install to be free and applies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q280 b and Q284 a, the maintainer's words verbatim: <i>"Q284 a"</i>.</b>
/// The configuration app is the coordinator when it holds its own pipe, created
/// with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>; any second start connects to it,
/// learns the coordinator's pid, grants it the foreground for a person's start,
/// sends <c>show</c> or <c>recheck</c>, and exits.
/// </para>
/// <para>
/// <b>Headless, both halves.</b> The foreground grant and the window are seams
/// here, so a <c>show</c> is a recorded call and never a window, and a grant is a
/// recorded pid and never a call to Windows. The one arm that drives the
/// published app starts it on a private desktop, as a second start against a
/// coordinator this host holds, so a regression that opened the window would open
/// it where nobody is looking.
/// </para>
/// </remarks>
internal sealed class CoordinatorTests
{
    /// <summary>
    /// The coordinator's pipe is named for the install root with the census gate's
    /// own key, and every spelling of one root names one pipe.
    /// </summary>
    /// <remarks>
    /// <b>The key is read off the census gate and not recomputed here</b>, so the
    /// arm holds the two names to each other and not to a copy of their
    /// derivation. A different root is the positive control: an implementation
    /// that named every root's coordinator alike would pass the first half.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCoordinatorsPipeIsNamedForTheInstallRootWithTheCensusGatesKey()
    {
        using var root = ScratchDirectory.Create("coordinator-name");
        using var other = ScratchDirectory.Create("coordinator-name-other");

        var gate = LiveInstances.MutexNameFor(root.Path);
        var key = gate[LiveInstances.MutexPrefix.Length..];

        await Assert.That(CoordinatorProtocol.NameFor(root.Path)).IsEqualTo(@"\\.\pipe\BrowserAI-Coordinator-" + key);
        await Assert.That(key.Length).IsEqualTo(32);

        // Every spelling of one root is one coordinator.
        await Assert.That(CoordinatorProtocol.NameFor(root.Path.ToUpperInvariant()))
            .IsEqualTo(CoordinatorProtocol.NameFor(root.Path));
        await Assert.That(CoordinatorProtocol.NameFor(root.Path + Path.DirectorySeparatorChar))
            .IsEqualTo(CoordinatorProtocol.NameFor(root.Path));

        // Another root is another coordinator.
        await Assert.That(CoordinatorProtocol.NameFor(other.Path)).IsNotEqualTo(CoordinatorProtocol.NameFor(root.Path));

        // And no server's pipe can carry the coordinator's prefix: a server's is
        // named after its marker, which begins with a pid.
        await Assert.That(ServerPipeProtocol.NameFor(@"C:\x\live\1234-0123456789abcdef.live")
            .StartsWith(CoordinatorProtocol.NamePrefix, StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    /// <summary>
    /// The coordinator's pipe has a server pipe's three properties: one DACL entry
    /// for the current user, the refusal of remote clients visible from the client
    /// end, and a second creation refused while it is held.
    /// </summary>
    /// <remarks>
    /// <b>Through the product's own open</b>, which is the server pipe's creation,
    /// so this is the arm that fails if the coordinator's pipe is ever created some
    /// other way. The framework's default pipe is the positive control for both
    /// readings, the way <see cref="ServerPipeTests"/> uses it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCoordinatorsPipeIsOwnerOnlyRefusesRemoteClientsAndHasOneInstance()
    {
        const uint RejectRemote = 0x8;
        const int PipeBusy = unchecked((int)0x800700E7);

        using var root = ScratchDirectory.Create("coordinator-pipe");
        using var inbox = new CoordinatorInbox();
        using var pipe = CoordinatorPipe.Open(root.Path, inbox, NullLogger.Instance);

        using (var client = new NamedPipeClientStream(".", ServerPipeRig.ShortName(pipe.Name), PipeDirection.InOut))
        {
            await client.ConnectAsync(TestDefaults.InProcessHang, CancellationToken.None);

            var dacl = ServerPipeRig.DaclOf(client.SafePipeHandle);
            var aces = Enumerable.Range(0, dacl.Count).Select(index => dacl[index]).OfType<CommonAce>().ToList();

            using var identity = WindowsIdentity.GetCurrent();

            await Assert.That(dacl.Count).IsEqualTo(1);
            await Assert.That(aces.Count).IsEqualTo(1);
            await Assert.That(aces[0].SecurityIdentifier).IsEqualTo(identity.User!);
            await Assert.That(ServerPipeRig.FlagsOf(client.SafePipeHandle) & RejectRemote).IsEqualTo(RejectRemote);
        }

        using var second = new CoordinatorInbox();

        var refused = Assert.Throws<IOException>(() => CoordinatorPipe.Open(root.Path, second, NullLogger.Instance).Dispose());

        await Assert.That(refused.HResult).IsEqualTo(PipeBusy);

        // The positive control: the framework's default pipe, read by the same two readers.
        var control = $"BrowserAI-coordinator-control-{Guid.NewGuid():N}";

        using var server = new NamedPipeServerStream(control, PipeDirection.InOut, 1);
        using var reader = new NamedPipeClientStream(".", control, PipeDirection.InOut);

        var accepted = server.WaitForConnectionAsync();

        await reader.ConnectAsync(TestDefaults.InProcessHang, CancellationToken.None);
        await accepted;

        await Assert.That(ServerPipeRig.DaclOf(reader.SafePipeHandle).Count).IsGreaterThan(1);
        await Assert.That(ServerPipeRig.FlagsOf(reader.SafePipeHandle) & RejectRemote).IsEqualTo(0u);
    }

    /// <summary>
    /// Of twenty starts at once, exactly one becomes the coordinator, every other
    /// one hands its verb over and learns the coordinator's pid, and once the
    /// coordinator lets go the next start becomes the coordinator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The research's own shape, in the product's code</b>: twenty contenders
    /// released together, three rounds, measured 2026-09-24 with a prototype
    /// ([kb](../../kb/windows/processes.md#a-second-start-finds-the-first-through-a-pipe-and-learns-its-pid)).
    /// Here each contender is a thread in this host running
    /// <see cref="CoordinatorStart.Settle"/>, the call every start of the app makes.
    /// </para>
    /// <para>
    /// <b>Every verb is counted where it lands</b>, in the winner's inbox, so a
    /// hand-over that answered and delivered nothing would still fail this.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OfTwentyStartsAtOnceOneBecomesTheCoordinatorAndEveryOtherHandsOver()
    {
        const int Contenders = 20;

        using var root = ScratchDirectory.Create("coordinator-twenty");

        for (var round = 0; round < 3; round++)
        {
            var inboxes = Enumerable.Range(0, Contenders).Select(_ => new CoordinatorInbox()).ToArray();
            var outcomes = new CoordinatorStart[Contenders];

            try
            {
                using (var barrier = new Barrier(Contenders))
                {
                    var threads = Enumerable.Range(0, Contenders).Select(index => new Thread(() =>
                    {
                        barrier.SignalAndWait();
                        outcomes[index] = CoordinatorStart.Settle(
                            root.Path, inboxes[index], CoordinatorVerb.Recheck, grant: null, NullLogger.Instance, TestDefaults.InProcessHang);
                    })
                    {
                        IsBackground = true,
                        Name = "coordinator contender",
                    }).ToList();

                    threads.ForEach(thread => thread.Start());

                    foreach (var thread in threads)
                    {
                        await Assert.That(thread.Join(TestDefaults.InProcessHang)).IsTrue();
                    }
                }

                var winners = Enumerable.Range(0, Contenders).Where(index => outcomes[index].Outcome is CoordinatorStartOutcome.Coordinator).ToList();

                await Assert.That(winners.Count).IsEqualTo(1);

                var handed = outcomes.Where(outcome => outcome.Outcome is CoordinatorStartOutcome.HandedOver).ToList();

                await Assert.That(handed.Count).IsEqualTo(Contenders - 1)
                    .Because(string.Join(" | ", outcomes.Where(outcome => outcome.Outcome is CoordinatorStartOutcome.Neither).Select(outcome => outcome.Why)));

                foreach (var handover in handed)
                {
                    await Assert.That(handover.HandOver!.CoordinatorProcessId).IsEqualTo(Environment.ProcessId);
                }

                var arrived = await DrainAsync(inboxes[winners[0]], Contenders - 1);

                await Assert.That(arrived.Count).IsEqualTo(Contenders - 1);
                await Assert.That(arrived.All(arrival => arrival.Verb is CoordinatorVerb.Recheck)).IsTrue();

                // The coordinator lets go, and the next start takes over.
                outcomes[winners[0]].Pipe!.Dispose();

                using var next = new CoordinatorInbox();

                var takeover = CoordinatorStart.Settle(root.Path, next, CoordinatorVerb.Recheck, grant: null, NullLogger.Instance, TestDefaults.InProcessHang);

                await Assert.That(takeover.Outcome).IsEqualTo(CoordinatorStartOutcome.Coordinator);

                takeover.Pipe!.Dispose();
            }
            finally
            {
                foreach (var outcome in outcomes)
                {
                    outcome?.Pipe?.Dispose();
                }

                foreach (var inbox in inboxes)
                {
                    inbox.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// A person's second start grants the coordinator the foreground and asks for
    /// its window; a hidden one grants nothing and asks it to look again; and the
    /// coordinator stops once nothing is staged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The coordinator here is the product's own loop</b>, on a thread of this
    /// host, holding the pipe through <see cref="CoordinatorStart.Settle"/>, with a
    /// staged update the arm controls and a window that records. The grant records
    /// the pid it would have handed <c>AllowSetForegroundWindow</c>, which in one
    /// process is this process's own. One stand-in the scan reports runs from the
    /// install the whole time, so the loop waits and never applies.
    /// </para>
    /// <para>
    /// <b>The order is asserted, not only the calls</b>: the grant is made before
    /// the window is asked for, because a grant after the coordinator had called
    /// <c>SetForegroundWindow</c> would be one Windows had already refused.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APersonsStartGrantsTheForegroundAndAsksForTheWindowAndAHiddenOneAsksToLookAgain()
    {
        using var root = ScratchDirectory.Create("coordinator-show");
        using var inbox = new CoordinatorInbox();
        using var blocker = new ScriptedScan(root.Path, standIns: 1);

        var events = new ConcurrentQueue<string>();
        var staged = new ScriptedStaged { Candidate = Candidate("9.9.9") };
        using var window = new RecordedWindow(events);
        var start = CoordinatorStart.Settle(root.Path, inbox, CoordinatorVerb.Recheck, grant: null, NullLogger.Instance, TestDefaults.InProcessHang);

        await Assert.That(start.Outcome).IsEqualTo(CoordinatorStartOutcome.Coordinator);

        using var pipe = start.Pipe;

        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, staged, blocker.Next, window, NullLogger.Instance));

        // A person's start.
        using (var person = new CoordinatorInbox())
        {
            var grant = new RecordedGrant(events);
            var handed = CoordinatorStart.Settle(root.Path, person, CoordinatorVerb.Show, grant, NullLogger.Instance, TestDefaults.InProcessHang);

            await Assert.That(handed.Outcome).IsEqualTo(CoordinatorStartOutcome.HandedOver).Because(handed.Why);
            await Assert.That(grant.Granted.ToArray()).IsEquivalentTo([Environment.ProcessId]);
            await Assert.That(await window.ShownAsync(1)).IsTrue();
            await Assert.That(string.Join(", ", events)).IsEqualTo($"grant {Environment.ProcessId}, show");
        }

        // A hidden start: no grant, no window.
        using (var hidden = new CoordinatorInbox())
        {
            var grant = new RecordedGrant(events);
            var handed = CoordinatorStart.Settle(root.Path, hidden, CoordinatorVerb.Recheck, grant, NullLogger.Instance, TestDefaults.InProcessHang);

            await Assert.That(handed.Outcome).IsEqualTo(CoordinatorStartOutcome.HandedOver).Because(handed.Why);
            await Assert.That(grant.Granted.IsEmpty).IsTrue();
        }

        // Nothing staged any more, and a recheck is what makes the loop see it.
        staged.Candidate = null;

        var last = await CoordinatorClient.SendAsync(root.Path, CoordinatorVerb.Recheck, grant: null, TestDefaults.InProcessHang);

        await Assert.That(last.Outcome).IsEqualTo(HandOverOutcome.Answered).Because(last.Why);
        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.NothingPending);
        await Assert.That(window.Shows).IsEqualTo(1);
        await Assert.That(staged.Applies).IsEqualTo(0);
    }

    /// <summary>
    /// The coordinator holds what runs from the install, counts in the census while
    /// it waits, and applies once that has exited, woken by the exit and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q285 a, the maintainer's words verbatim: <i>"Q285 a"</i>. The product's own
    /// scan against a real process.</b> The stand-in is a copy of <c>cmd.exe</c>
    /// planted under the scratch root's <c>current</c> directory, waited for until the
    /// product's enumeration sees it, in a job the arm owns. Ending that job is the
    /// exit the loop has to wake on; the loop has no timer to wake it instead.
    /// </para>
    /// <para>
    /// <b>The census is read by a second member</b>, the way a server finishing its
    /// update pass reads it, so the arm fails if the coordinator stops counting as
    /// somebody who is there: a server alone would apply on its own exit, and the
    /// apply's kill pass would end the coordinator.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCoordinatorWaitsWhileAProcessRunsFromTheInstallAndAppliesOnceItHasExited()
    {
        using var root = ScratchDirectory.Create("coordinator-wait");
        using var inbox = new CoordinatorInbox();
        using var logs = new CapturingLoggerProvider();
        using var scanned = new SemaphoreSlim(0);
        using var censusRead = new ManualResetEventSlim();
        using var window = new RecordedWindow(new ConcurrentQueue<string>());
        using var scope = new JobObjectScope();

        var (standIn, _) = await PlantedProcess.StartInAsync(scope, Path.Combine(root.Path, RegistrationTarget.CurrentDirectoryName), root.Path);
        var standInId = standIn.Id;
        var staged = new ScriptedStaged { Candidate = Candidate("9.9.9") };
        var scans = 0;

        var run = RunOnItsOwnThread(new CoordinatorLoop(
            root.Path,
            inbox,
            staged,
            () =>
            {
                var found = BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId);

                // The first pass stops here while the arm reads the census, between
                // the loop's join and its verdict on what the scan found.
                if (Interlocked.Increment(ref scans) is 1)
                {
                    _ = scanned.Release();
                    _ = censusRead.Wait(TestDefaults.InProcessHang);
                }

                return found;
            },
            window,
            logs.CreateLogger("coordinator")));

        await Assert.That(await scanned.WaitAsync(TestDefaults.InProcessHang)).IsTrue();

        try
        {
            // A second member of the census counts the coordinator.
            using var peer = LiveInstances.Join(root.Path, NullLogger.Instance);

            await Assert.That(peer).IsNotNull();

            var census = peer!.Census();

            await Assert.That(census.State).IsEqualTo(Liveness.NotAlone).Because(census.Why ?? "the census gave no reason");
            await Assert.That(census.Others).IsEqualTo(1);
        }
        finally
        {
            censusRead.Set();
        }

        // The stand-in ends with its job.
        scope.Dispose();

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.Applied);
        await Assert.That(staged.Applies).IsEqualTo(1);

        // It waited on the stand-in, said so once naming it, and looked again when it exited.
        var waiting = logs.Records.Where(record => record.EventId.Id is 8).Select(record => record.Message).ToList();

        await Assert.That(waiting.Count).IsEqualTo(1);
        await Assert.That(waiting[0]).Contains($"pid {standInId} ");
        await Assert.That(Volatile.Read(ref scans)).IsEqualTo(2);

        // And it left the census when it stopped.
        using var after = LiveInstances.Join(root.Path, NullLogger.Instance);

        await Assert.That(after).IsNotNull();
        await Assert.That(after!.Census().State).IsEqualTo(Liveness.Alone);
    }

    /// <summary>
    /// With nothing staged the coordinator stops on its first pass, before it scans
    /// anything or joins the census.
    /// </summary>
    /// <remarks>
    /// <b>This is every start of a build that is not installed</b>, whose staged
    /// updates are <see cref="NothingStaged"/>'s: it adds no marker to the census of
    /// the root it was keyed to, and scans nothing under it. The live directory not
    /// being there is the reading; the arm above, which joins, is its positive control.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNothingStagedTheCoordinatorStopsAtOnceWithoutScanningOrJoiningTheCensus()
    {
        using var root = ScratchDirectory.Create("coordinator-nothing");
        using var inbox = new CoordinatorInbox();
        using var scan = new ScriptedScan(root.Path, standIns: 1);
        using var window = new RecordedWindow(new ConcurrentQueue<string>());

        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, NothingStaged.Instance, scan.Next, window, NullLogger.Instance));

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.NothingPending);
        await Assert.That(scan.Passes.Count).IsEqualTo(0);
        await Assert.That(Directory.Exists(LiveInstances.DirectoryUnder(root.Path))).IsFalse();
    }

    /// <summary>
    /// More processes than one wait can hold are all held and all waited for, 63
    /// at a time beside the pipe; a verb and an exit are each a new pass, and the
    /// apply waits for the last of them.
    /// </summary>
    /// <remarks>
    /// <b>One wait holds at most 64 handles</b>, and the loop's holds the pipe's and
    /// up to 63 processes'. Seventy real processes would cost the machine more than the
    /// rule is worth, so here the scan is scripted: each stand-in is an event the arm
    /// sets, handed to the loop through a fresh handle on every pass, the way the
    /// product's scan opens a fresh handle to every process.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task MoreProcessesThanOneWaitCanHoldAreAllWaitedForSixtyThreeAtATime()
    {
        const int StandIns = CoordinatorLoop.ProcessesPerWait + 7;

        using var root = ScratchDirectory.Create("coordinator-many");
        using var inbox = new CoordinatorInbox();
        using var logs = new CapturingLoggerProvider();
        using var scan = new ScriptedScan(root.Path, StandIns);
        using var window = new RecordedWindow(new ConcurrentQueue<string>());

        var staged = new ScriptedStaged { Candidate = Candidate("9.9.9") };
        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, staged, scan.Next, window, logs.CreateLogger("coordinator")));

        // The first pass holds all seventy.
        await Assert.That(await scan.PassesAsync(1, run)).IsTrue();

        // A recheck is a new pass over the same seventy.
        inbox.Post(CoordinatorVerb.Recheck, from: 1);

        await Assert.That(await scan.PassesAsync(2, run)).IsTrue();

        // The first 63 exit, and the next pass holds the other seven.
        scan.Exit(0, CoordinatorLoop.ProcessesPerWait);

        await Assert.That(await scan.PassesAsync(3, run)).IsTrue();

        // The last seven exit, and the pass after them applies.
        scan.Exit(CoordinatorLoop.ProcessesPerWait, StandIns - CoordinatorLoop.ProcessesPerWait);

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.Applied);
        await Assert.That(staged.Applies).IsEqualTo(1);
        await Assert.That(string.Join(" ", scan.Passes)).IsEqualTo($"{StandIns} {StandIns} 7 0");

        // One line each time the count changed, and none for the recheck that changed nothing.
        var waiting = logs.Records.Where(record => record.EventId.Id is 8).Select(record => record.Message).ToList();

        await Assert.That(waiting.Count).IsEqualTo(2);
        await Assert.That(waiting[0]).Contains($"{StandIns} process(es) run from this install");
        await Assert.That(waiting[1]).Contains("7 process(es) run from this install");
    }

    /// <summary>
    /// A <c>show</c> that arrives while the window is open brings that window
    /// forward, from the dialog's own timer, and a recheck waits for it to close.
    /// </summary>
    /// <remarks>
    /// <b>Driven through the host's dispatch with a made-up window handle</b>, the
    /// way <see cref="ConfigurationAppTests"/> drives a click: the raise is a seam
    /// that records the handle, so no message is ever sent to it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShowWhileTheWindowIsOpenRaisesThatWindowAndLeavesTheRecheckWaiting()
    {
        const nint Window = 4321;

        using var install = ScratchDirectory.Create("coordinator-raise");
        using var inbox = new CoordinatorInbox();

        var raised = new List<nint>();
        var state = new AppState
        {
            Version = "0.0.0-test",
            InstallRoot = install.Path,
            DataRoot = install.Path,
            ServerCommand = null,
            ServerRefusal = "not installed",
            Clients = [],
        };

        using var session = new ConfigurationSession(
            state,
            new UnusedCommands(),
            new BrowserAI.Hosting.LocalAppDataPaths(install.Path),
            NullLogger.Instance,
            Occasion.Ordinary,
            imagePath: null,
            (_, _) => FolderPick.Cancelled,
            () => state,
            inbox,
            window =>
            {
                raised.Add(window);
                return true;
            });

        using var host = session.Attach();

        _ = host.Dispatch(Window, TaskDialogInterop.Notification.Created, 0, 0);

        // A tick with nothing asked raises nothing.
        _ = host.Dispatch(Window, TaskDialogInterop.Notification.Timer, 0, 0);
        await Assert.That(raised.Count).IsEqualTo(0);

        inbox.Post(CoordinatorVerb.Recheck, from: 1);
        inbox.Post(CoordinatorVerb.Show, from: 2);

        _ = host.Dispatch(Window, TaskDialogInterop.Notification.Timer, 0, 0);

        await Assert.That(raised).IsEquivalentTo([Window]);

        // The recheck is still there for the coordinator once the window closes.
        await Assert.That(inbox.TryTake(out var left)).IsTrue();
        await Assert.That(left!.Verb).IsEqualTo(CoordinatorVerb.Recheck);
        await Assert.That(inbox.TryTake(out _)).IsFalse();
    }

    /// <summary>
    /// The published app, started as a second start, hands its verb to the
    /// coordinator this host holds and exits: <c>recheck</c> for
    /// <c>--coordinate</c> and <c>--sign-in</c>, <c>show</c> for a start with no
    /// arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one arm on the published binary</b>, because it is the only place
    /// the mode parsing and the settle are wired together. The scratch data root
    /// is the child's own root through <c>BROWSERAI_ROOT</c>, set on the child
    /// alone, which is also the root an uninstalled app keys its coordinator to.
    /// </para>
    /// <para>
    /// <b>On a private desktop</b>: the start with no arguments is a person's start,
    /// and a regression that made it the coordinator would open the dialog.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APublishedSecondStartHandsItsVerbToTheCoordinatorAndExits()
    {
        PublishedSlice.EnsureAppFresh();

        using var root = ScratchDirectory.Create("coordinator-published");
        using var inbox = new CoordinatorInbox();

        var start = CoordinatorStart.Settle(root.Path, inbox, CoordinatorVerb.Recheck, grant: null, NullLogger.Instance, TestDefaults.InProcessHang);

        await Assert.That(start.Outcome).IsEqualTo(CoordinatorStartOutcome.Coordinator);

        using var pipe = start.Pipe;

        var environment = PublishedSlice.InheritedEnvironment();

        environment[BrowserAiPaths.AppRootOverride] = root.Path;
        environment[CodexRegistration.HomeVariable] = Directory.CreateDirectory(Path.Combine(root.Path, "codex")).FullName;

        (string[] Arguments, CoordinatorVerb Verb)[] starts =
        [
            ([CoordinatorProtocol.CoordinateArgument], CoordinatorVerb.Recheck),
            ([CoordinatorProtocol.SignInArgument, "$(Arg0)"], CoordinatorVerb.Recheck),
            ([], CoordinatorVerb.Show),
        ];

        using var desktop = PrivateDesktop.Create("coordinator-second-start");

        foreach (var (arguments, verb) in starts)
        {
            using var job = JobObject.CreateKillOnClose();
            using var process = desktop.Launch(job, PublishedSlice.AppExecutable, arguments, root.Path, environment);

            var drained = Task.WhenAll(
                process.StandardOutput.CopyToAsync(Stream.Null),
                process.StandardError.CopyToAsync(Stream.Null));

            await Assert.That(await process.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
            await drained;
            await Assert.That(process.TryReadExitCode()).IsEqualTo(0);

            var arrived = await DrainAsync(inbox, 1);

            await Assert.That(arrived.Count).IsEqualTo(1);
            await Assert.That(arrived[0].Verb).IsEqualTo(verb);
            await Assert.That(arrived[0].From).IsEqualTo(process.Id);
        }

        // Nothing the three starts did reached a window on their desktop.
        await Assert.That(desktop.TopLevelWindows().Count).IsEqualTo(0);
    }

    /// <summary>Takes verbs out of an inbox until there are enough, or the hang detector runs out.</summary>
    /// <param name="inbox">The inbox.</param>
    /// <param name="expected">How many to wait for.</param>
    /// <returns>What arrived.</returns>
    private static async Task<List<CoordinatorArrival>> DrainAsync(CoordinatorInbox inbox, int expected)
    {
        var arrived = new List<CoordinatorArrival>();
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (arrived.Count < expected && waited.Elapsed < TestDefaults.InProcessHang)
        {
            if (inbox.TryTake(out var arrival))
            {
                arrived.Add(arrival!);
                continue;
            }

            _ = await Task.Run(() => inbox.Arrived.WaitOne(TestDefaults.InProcessHang));
        }

        return arrived;
    }

    /// <summary>Runs the loop on a thread of its own, the way the app runs it on its main thread.</summary>
    /// <param name="loop">The loop.</param>
    /// <returns>How it ended, or what it threw.</returns>
    private static Task<CoordinatorEnd> RunOnItsOwnThread(CoordinatorLoop loop)
    {
        var completion = new TaskCompletionSource<CoordinatorEnd>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(loop.Run());
            }
#pragma warning disable CA1031 // Whatever the loop threw belongs to the awaiting arm, not to this thread.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                completion.SetException(failure);
            }
        })
        {
            IsBackground = true,
            Name = "coordinator loop under test",
        };

        thread.Start();
        return completion.Task;
    }

    /// <summary>A candidate the loop can be handed.</summary>
    /// <param name="version">Its version.</param>
    /// <returns>The candidate.</returns>
    private static UpdateCandidate Candidate(string version) => new()
    {
        Version = version,
        IsDowngrade = false,
        DeltaCount = 0,
        FullPackageSize = 1,
    };

    /// <summary>A staged update the arm controls.</summary>
    private sealed class ScriptedStaged : IStagedUpdates
    {
        /// <summary>What is staged; <see langword="null"/> for nothing.</summary>
        /// <remarks>
        /// Written by the arm and read by the loop's thread after a verb has woken
        /// it, and the wake is a wait on an event, which is a full fence.
        /// </remarks>
        public UpdateCandidate? Candidate { get; set; }

        /// <summary>How many applies were asked for, read once the loop has ended.</summary>
        public int Applies { get; private set; }

        public UpdateCandidate? Pending() => Candidate;

        public void ApplyAfterThisProcessExits(UpdateCandidate candidate) => Applies++;
    }

    /// <summary>
    /// A scan the arm scripts: stand-ins the loop is told run from the install, each
    /// an event the arm sets, so an exit is a call and never a kill.
    /// </summary>
    /// <remarks>
    /// <b>Every pass hands the loop a fresh handle to each stand-in still running</b>,
    /// the way the product's scan opens a fresh handle to each process, and the loop
    /// disposes them with the pass. The events are named so that the arm's handle and
    /// each pass's are different handles to one event: one wait refuses the same
    /// handle twice, and the arm has to be able to set what a pass holds.
    /// </remarks>
    private sealed class ScriptedScan : IDisposable
    {
        private const int FirstProcessId = 100_000;

        private readonly Lock _gate = new();
        private readonly string _root;
        private readonly string[] _names;
        private readonly EventWaitHandle[] _exits;
        private readonly bool[] _exited;
        private readonly List<int> _passes = [];
        private readonly SemaphoreSlim _scanned = new(0);

        public ScriptedScan(string root, int standIns)
        {
            var prefix = $"BrowserAI-coordinator-loop-{Guid.NewGuid():N}-";

            _root = root;
            _names = [.. Enumerable.Range(0, standIns).Select(index => $"{prefix}{index}")];
            _exits = [.. _names.Select(name => new EventWaitHandle(initialState: false, EventResetMode.ManualReset, name))];
            _exited = new bool[standIns];
        }

        /// <summary>How many stand-ins each pass held, in order.</summary>
        public IReadOnlyList<int> Passes
        {
            get
            {
                lock (_gate)
                {
                    return [.. _passes];
                }
            }
        }

        /// <summary>One pass: a fresh handle to every stand-in that has not exited.</summary>
        /// <returns>The scan, which the loop disposes.</returns>
        public RootScan Next()
        {
            List<HeldProcess> held = [];

            lock (_gate)
            {
                for (var index = 0; index < _names.Length; index++)
                {
                    if (!_exited[index])
                    {
                        held.Add(Hold(index));
                    }
                }

                _passes.Add(held.Count);
            }

            _ = _scanned.Release();

            return new RootScan(held, []);
        }

        /// <summary>Ends a run of stand-ins: gone from every later pass first, then set.</summary>
        /// <param name="first">The first to end.</param>
        /// <param name="count">How many.</param>
        public void Exit(int first, int count)
        {
            lock (_gate)
            {
                Array.Fill(_exited, true, first, count);
            }

            foreach (var exit in _exits.AsSpan(first, count))
            {
                _ = exit.Set();
            }
        }

        /// <summary>
        /// Waits until the loop has made this many passes, the loop has ended, or the
        /// hang detector runs out.
        /// </summary>
        /// <remarks>
        /// <b>A loop that threw ends the wait with what it threw</b>, so an arm whose
        /// loop failed says why at once and not after the hang detector.
        /// </remarks>
        /// <param name="count">How many passes.</param>
        /// <param name="loop">The loop's run.</param>
        /// <returns>Whether it made them.</returns>
        public async Task<bool> PassesAsync(int count, Task loop)
        {
            while (Passes.Count < count)
            {
                var next = _scanned.WaitAsync(TestDefaults.InProcessHang);

                if (await Task.WhenAny(next, loop) == loop)
                {
                    await loop;
                    return Passes.Count >= count;
                }

                if (!await next)
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            foreach (var exit in _exits)
            {
                exit.Dispose();
            }

            _scanned.Dispose();
        }

        /// <summary>A fresh handle to one stand-in's event, owned by the held process it is given to.</summary>
        /// <param name="index">Which stand-in.</param>
        /// <returns>The held process.</returns>
        private HeldProcess Hold(int index)
        {
            using var opened = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, _names[index]);

            var handle = opened.SafeWaitHandle;

            // The held process owns the handle from here, and the emptied wrapper disposes nothing.
            opened.SafeWaitHandle = null;

            return new HeldProcess(FirstProcessId + index, 0, Path.Combine(_root, RegistrationTarget.CurrentDirectoryName, $"stand-in-{index}.exe"), handle);
        }
    }

    /// <summary>A window that records each time it is shown.</summary>
    /// <param name="events">Where the order of calls is kept.</param>
    private sealed class RecordedWindow(ConcurrentQueue<string> events) : ICoordinatorWindow, IDisposable
    {
        private readonly SemaphoreSlim _shown = new(0);
        private int _shows;

        public void Dispose() => _shown.Dispose();

        public int Shows => Volatile.Read(ref _shows);

        public int Show()
        {
            events.Enqueue("show");
            _ = Interlocked.Increment(ref _shows);
            _shown.Release();
            return 0;
        }

        public async Task<bool> ShownAsync(int times)
        {
            for (var index = 0; index < times; index++)
            {
                if (!await _shown.WaitAsync(TestDefaults.InProcessHang))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>A foreground grant that records the pid it would have granted.</summary>
    /// <param name="events">Where the order of calls is kept.</param>
    private sealed class RecordedGrant(ConcurrentQueue<string> events) : IForegroundGrant
    {
        public ConcurrentQueue<int> Granted { get; } = new();

        public bool Allow(int processId)
        {
            events.Enqueue($"grant {processId}");
            Granted.Enqueue(processId);
            return true;
        }
    }

    /// <summary>A command seam no arm here reaches.</summary>
    private sealed class UnusedCommands : IRegistrationCommand
    {
        public string? Locate(string executableName) => null;

        public CommandOutcome Run(string executable, IReadOnlyList<string> arguments, TimeSpan budget, string? workingDirectory) =>
            throw new InvalidOperationException("No arm in this class starts a client.");

        public CommandOutcome Run(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan budget,
            string? workingDirectory,
            IReadOnlyDictionary<string, string> environment) =>
            throw new InvalidOperationException("No arm in this class starts a client.");
    }
}

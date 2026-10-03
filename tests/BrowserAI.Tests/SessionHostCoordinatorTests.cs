// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.App;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The coordinator's half of the session host: the start mode and the verb that ask
/// for one, the loop that stays while it runs and stops it before an update, and the
/// keeper that holds it in a job of the coordinator's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q366 b - lets go
/// with a fully build option c. If the server crashes and the coordinator loses the
/// pipe, keep the browser around with the already running activity timeout timer
/// active."</i> The host the coordinator starts is in the coordinator's own
/// kill-on-close job, so the coordinator has to stay for as long as the host runs,
/// and has to stop it itself before an update.
/// </para>
/// <para>
/// <b>The loop's arms script the host</b>, the way the coordinator's own arms script
/// the scan, so an exit is a call and never a kill. The keeper's arms start real
/// processes, copies of <c>cmd.exe</c> running <c>ping</c> on the loopback with no
/// window, because what they assert is about a job and the processes in it.
/// </para>
/// </remarks>
internal sealed class SessionHostCoordinatorTests
{
    /// <summary>
    /// The logon task's <c>--start-host</c> starts the app as the coordinator to start
    /// the host, and a start that finds a coordinator running hands it <c>host</c>.
    /// </summary>
    /// <remarks>
    /// <b>The task appends its parameter after <c>--sign-in</c></b>, so the start a
    /// server asks for carries both and means the second, the way <c>--coordinate</c>
    /// already does; and <c>--coordinate</c> still wins over both.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLogonTasksStartHostArgumentMeansStartTheHostAndAsksARunningCoordinatorForIt()
    {
        await Assert.That(StartModes.Of([CoordinatorProtocol.SignInArgument, CoordinatorProtocol.StartHostArgument])).IsEqualTo(StartMode.StartHost);
        await Assert.That(StartModes.Of([CoordinatorProtocol.StartHostArgument])).IsEqualTo(StartMode.StartHost);
        await Assert.That(StartModes.Of([CoordinatorProtocol.CoordinateArgument, CoordinatorProtocol.StartHostArgument])).IsEqualTo(StartMode.Coordinate);
        await Assert.That(StartModes.Of([CoordinatorProtocol.SignInArgument, "$(Arg0)"])).IsEqualTo(StartMode.SignIn);

        await Assert.That(StartModes.VerbOf(StartMode.StartHost)).IsEqualTo(CoordinatorVerb.Host);
        await Assert.That(CoordinatorProtocol.Spelling(CoordinatorVerb.Host)).IsEqualTo(CoordinatorProtocol.HostVerb);
        await Assert.That(CoordinatorProtocol.Parse(CoordinatorProtocol.HostVerb)).IsEqualTo(CoordinatorVerb.Host);
    }

    /// <summary>
    /// A <c>host</c> starts the session host on the pipe's own thread before it is
    /// answered, and a coordinator that cannot start one refuses it with the reason.
    /// </summary>
    /// <remarks>
    /// <b>Answered on the pipe's thread, and not queued</b>, because the coordinator's
    /// own thread may be inside its window for as long as a person keeps it open, and
    /// a server waits a few seconds for a host and no longer. A refusal is what lets
    /// that server serve its client itself at once. An acknowledged <c>host</c> is
    /// still posted, so the loop wakes and starts waiting on the host it now has.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHostVerbStartsTheHostBeforeItIsAnsweredAndOneThatCannotIsRefusedWithWhy()
    {
        using var root = ScratchDirectory.Create("coordinator-host-verb");

        var starts = 0;
        var starting = true;

        using var inbox = new CoordinatorInbox(() =>
        {
            _ = Interlocked.Increment(ref starts);
            return Volatile.Read(ref starting);
        });

        using var pipe = CoordinatorPipe.Open(root.Path, inbox, NullLogger.Instance);

        var started = await CoordinatorClient.SendAsync(root.Path, CoordinatorVerb.Host, grant: null, TestDefaults.InProcessHang);

        await Assert.That(started.Outcome).IsEqualTo(HandOverOutcome.Answered).Because(started.Why);
        await Assert.That(Volatile.Read(ref starts)).IsEqualTo(1);
        await Assert.That(inbox.Arrived.WaitOne(TestDefaults.InProcessHang)).IsTrue();
        await Assert.That(inbox.TryTake(out var arrival)).IsTrue();
        await Assert.That(arrival!.Verb).IsEqualTo(CoordinatorVerb.Host);

        Volatile.Write(ref starting, false);

        var refused = await CoordinatorClient.SendAsync(root.Path, CoordinatorVerb.Host, grant: null, TestDefaults.InProcessHang);

        await Assert.That(refused.Outcome).IsEqualTo(HandOverOutcome.Refused);
        await Assert.That(refused.Why).Contains(CoordinatorProtocol.NoHostRefusal);
        await Assert.That(Volatile.Read(ref starts)).IsEqualTo(2);
        await Assert.That(inbox.IsEmpty).IsTrue().Because("a refused verb is not posted, so nothing wakes the loop for a host that does not run");

        // A coordinator with nothing to start a host with refuses too: a build that
        // is not installed.
        using var bare = ScratchDirectory.Create("coordinator-host-verb-bare");
        using var bareInbox = new CoordinatorInbox();
        using var barePipe = CoordinatorPipe.Open(bare.Path, bareInbox, NullLogger.Instance);

        var none = await CoordinatorClient.SendAsync(bare.Path, CoordinatorVerb.Host, grant: null, TestDefaults.InProcessHang);

        await Assert.That(none.Outcome).IsEqualTo(HandOverOutcome.Refused);
        await Assert.That(none.Why).Contains(CoordinatorProtocol.NoHostRefusal);
    }

    /// <summary>
    /// With nothing staged the coordinator stays while its session host runs, in the
    /// census, through any verb, and stops once the host has exited.
    /// </summary>
    /// <remarks>
    /// <b>Without the stay the coordinator stops on its first pass</b>, as it did before
    /// Q366 b, and its job takes the host and every session with it: the very loss the
    /// host exists to prevent. The census half is what lets a server that stages an
    /// update meanwhile wake this process instead of applying on its own exit.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNothingStagedTheCoordinatorStaysWhileItsSessionHostRunsAndStopsOnceItExits()
    {
        using var root = ScratchDirectory.Create("coordinator-host-stay");
        using var inbox = new CoordinatorInbox();
        using var host = new ScriptedHost(new ConcurrentQueue<string>());

        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, NothingStaged.Instance, NoScan, new NoPage(), NullLogger.Instance)
        {
            Host = host,
        });

        await Assert.That(await host.LookedAtAsync(1, run)).IsTrue();

        // In the census while it waits.
        using (var peer = LiveInstances.Join(root.Path, NullLogger.Instance))
        {
            await Assert.That(peer).IsNotNull();

            var census = peer!.Census();

            await Assert.That(census.State).IsEqualTo(Liveness.NotAlone).Because(census.Why ?? "the census gave no reason");
            await Assert.That(census.Others).IsEqualTo(1);
        }

        // A verb is a pass, and the host still runs, so it stays.
        inbox.Post(CoordinatorVerb.Recheck, from: 1);

        await Assert.That(await host.LookedAtAsync(2, run)).IsTrue();
        await Assert.That(run.IsCompleted).IsFalse();

        host.Exit();

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.NothingPending);
        await Assert.That(host.Stops).IsEqualTo(0).Because("nothing was staged, so nothing was stopped for an update");

        // And it left the census when it stopped.
        using var after = LiveInstances.Join(root.Path, NullLogger.Instance);

        await Assert.That(after).IsNotNull();
        await Assert.That(after!.Census().State).IsEqualTo(Liveness.Alone);
    }

    /// <summary>
    /// With a package staged the coordinator waits for every process a client started,
    /// leaves its host out of that wait, and stops the host before it applies.
    /// </summary>
    /// <remarks>
    /// <b>The scan it is handed leaves the host out</b>, which is the keeper's half and
    /// has its own arm below; this one holds the order: nothing stopped while a server
    /// a client started still runs, the host stopped once none does, the scan taken
    /// again after the stop, and then the apply.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithAPackageStagedTheHostIsStoppedOnceNothingElseRunsAndBeforeTheApply()
    {
        using var root = ScratchDirectory.Create("coordinator-host-update");
        using var inbox = new CoordinatorInbox();

        var events = new ConcurrentQueue<string>();

        using var host = new ScriptedHost(events);
        using var scan = new ScriptedServers(root.Path, events);

        var staged = new RecordedStaged(events) { Candidate = Candidate("9.9.9") };
        var server = scan.Arrive();

        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, staged, scan.Next, new NoPage(), NullLogger.Instance)
        {
            Host = host,
        });

        await Assert.That(await scan.PassesAsync(1, run)).IsTrue();
        await Assert.That(host.Stops).IsEqualTo(0).Because("a server a client started still runs, so nothing is stopped yet");

        scan.Exit(server);

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.Applied);
        await Assert.That(string.Join(" | ", events)).IsEqualTo("scan 1 | scan 0 | stop | scan 0 | apply 9.9.9");
        await Assert.That(host.Reopens).IsEqualTo(0);
    }

    /// <summary>
    /// A server a client starts while the host is being stopped for an update is waited
    /// for like any other, and the host may be started again meanwhile.
    /// </summary>
    /// <remarks>
    /// <b>The scan after the stop is the whole of this</b>: without it the apply would
    /// end a server that had just started, and the keeper would go on refusing every
    /// server a host until the coordinator exited.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AServerThatStartsWhileTheHostStopsIsWaitedForAndTheHostIsReopened()
    {
        using var root = ScratchDirectory.Create("coordinator-host-race");
        using var inbox = new CoordinatorInbox();

        var events = new ConcurrentQueue<string>();

        using var scan = new ScriptedServers(root.Path, events);

        var arrived = -1;

        using var host = new ScriptedHost(events)
        {
            // A client starts a server while the host closes its browsers.
            WhileStopping = () => Volatile.Write(ref arrived, scan.Arrive()),
        };

        var staged = new RecordedStaged(events) { Candidate = Candidate("9.9.9") };

        var run = RunOnItsOwnThread(new CoordinatorLoop(root.Path, inbox, staged, scan.Next, new NoPage(), NullLogger.Instance)
        {
            Host = host,
        });

        // The scan after the stop finds the newcomer, and the loop waits on it.
        await Assert.That(await scan.PassesAsync(3, run)).IsTrue();
        await Assert.That(host.Reopens).IsEqualTo(1);
        await Assert.That(staged.Applies).IsEqualTo(0);

        scan.Exit(Volatile.Read(ref arrived));

        await Assert.That(await run.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(CoordinatorEnd.Applied);
        await Assert.That(string.Join(" | ", events)).IsEqualTo("scan 0 | stop | scan 1 | reopen | scan 1 | scan 0 | apply 9.9.9");
    }

    /// <summary>
    /// The sign-in step applies nothing while the session host runs, and leaves the
    /// apply to the loop, which closes the host's browsers first.
    /// </summary>
    /// <remarks>
    /// <b>The scan leaves the host out</b>, because the loop stops it before an apply
    /// and does not wait for it. A host a server asked for while the step ran would
    /// therefore read as nothing running, the step would hand the package over and
    /// this process would exit for it to apply, and closing the keeper's job on the
    /// way out would end the host and every browser in it with no close. The second
    /// call is the positive control: with the host gone, the same step applies.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSignInStepAppliesNothingWhileTheSessionHostRunsAndLeavesItToTheLoop()
    {
        var events = new ConcurrentQueue<string>();

        using var host = new ScriptedHost(events);

        var staged = new RecordedStaged(events) { Candidate = Candidate("9.9.9") };

        static RootScan nothingElse() => new([], []);

        var held = SignInStep.Run(staged, nothingElse, NullLogger.Instance, host);

        await Assert.That(held.Outcome).IsEqualTo(SignInOutcome.NotAlone).Because("the session host runs from the install, and only the loop closes its browsers before an apply");
        await Assert.That(staged.Applies).IsEqualTo(0);
        await Assert.That(host.Stops).IsEqualTo(0).Because("the step stops nothing; the loop it hands over to does");

        host.Exit();

        var applied = SignInStep.Run(staged, nothingElse, NullLogger.Instance, host);

        await Assert.That(applied.Outcome).IsEqualTo(SignInOutcome.Applied);
        await Assert.That(string.Join(" | ", events)).IsEqualTo("apply 9.9.9");
    }

    /// <summary>
    /// The keeper starts one host in a job of its own and no second one while it runs,
    /// and closing the keeper ends the host and everything the host started.
    /// </summary>
    /// <remarks>
    /// <b>The second half is P7's, the maintainer's words verbatim: <i>"it all needs to
    /// be done in a super safe way so we don't permanently leak stuff."</i></b> The
    /// coordinator ending, however it ends, closes this job's only handle; the stand-in
    /// host starts a child of its own, the way the real host starts <c>node</c>, and
    /// both are watched to their end by pid and creation time.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheKeeperStartsOneHostInAJobOfItsOwnAndClosingItEndsTheHostAndWhatItStarted()
    {
        using var root = ScratchDirectory.Create("keeper-job");

        // ⚠️ AN HOUR OF PINGS, LONGER THAN THE HANG DETECTOR, so a close that left the
        // job open is a red arm: with 600 the stand-in ended on its own about when the
        // wait below gave up, and a planted open job could pass on that timing alone.
        var standIn = PlantHost(root.Path);
        var keeper = Keeper(root.Path, standIn, ["/c", "ping", "-n", "3600", "127.0.0.1"], askToStop: _ => false);
        var members = new List<(int ProcessId, long Created)>();

        try
        {
            await Assert.That(keeper.EnsureStarted()).IsTrue();
            await Assert.That(keeper.Running).IsNotNull();

            await WaitUntilAsync(() => keeper.Members().Count >= 2, "the stand-in host never started its child inside the keeper's job");

            var first = keeper.Members();

            await Assert.That(keeper.EnsureStarted()).IsTrue();
            await Assert.That(keeper.Members().Order().ToList()).IsEquivalentTo(first.Order().ToList())
                .Because("a host was running, so none was started");

            members.AddRange(first.Select(processId => (processId, ProcessIdentity.CreationTimeOf(processId))));
        }
        finally
        {
            keeper.Dispose();
        }

        // Waited for together, so the hang detector is one bound for the whole job
        // and not one per member.
        var waits = await Task.WhenAll(members.Select(member => Task.Run(() =>
            (Member: member, Gone: ProcessIdentity.WaitUntilGone(member.ProcessId, member.Created, TestDefaults.ProcessHang)))));

        var outlived = waits.Where(wait => !wait.Gone).Select(wait => wait.Member).ToList();

        // A red arm leaves nothing behind either: what outlived the close is ended
        // here, by the pid and creation time recorded while it was in the job.
        foreach (var (processId, created) in outlived)
        {
            if (ProcessIdentity.IsAlive(processId, created))
            {
                ProcessIdentity.Terminate(processId, created);
            }
        }

        await Assert.That(outlived.Select(member => member.ProcessId).ToList()).IsEmpty()
            .Because("every process in the keeper's job ends with its close");
        await Assert.That(keeper.EnsureStarted()).IsFalse().Because("a closed keeper starts nothing");
    }

    /// <summary>
    /// A stop for an update asks the host by its own pid, waits for it to end, and
    /// refuses to start another until it is reopened.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStopForAnUpdateAsksTheHostByItsPidWaitsForItAndRefusesANewHostUntilReopened()
    {
        using var root = ScratchDirectory.Create("keeper-stop");

        var standIn = PlantHost(root.Path);
        var asked = new ConcurrentQueue<int>();

        using var keeper = Keeper(root.Path, standIn, ["/c", "ping", "-n", "600", "127.0.0.1"], askToStop: processId =>
        {
            asked.Enqueue(processId);

            // What the host does when its pipe takes the stop: it ends. The pid is
            // held by the keeper, so it is still this process.
            ProcessIdentity.Terminate(processId, ProcessIdentity.CreationTimeOf(processId));
            return true;
        });

        await Assert.That(keeper.EnsureStarted()).IsTrue();

        var hostId = keeper.ProcessId;

        await Assert.That(hostId).IsNotNull();
        await Assert.That(keeper.Members()).Contains(hostId!.Value);
        await Assert.That(keeper.StopForUpdate()).IsTrue();
        await Assert.That(asked.ToList()).IsEquivalentTo([hostId.Value]);
        await Assert.That(keeper.Running).IsNull();

        await Assert.That(keeper.EnsureStarted()).IsFalse().Because("an update is closing the host, so a server asking now serves its client itself");

        keeper.Reopen();

        await Assert.That(keeper.EnsureStarted()).IsTrue();
        await Assert.That(keeper.Running).IsNotNull();
    }

    /// <summary>
    /// The keeper leaves every process in its job out of a scan of the install root,
    /// and leaves every other process in.
    /// </summary>
    /// <remarks>
    /// <b>Without this the apply would wait on the coordinator's own host forever</b>:
    /// the host runs from the install root, the scan finds it, and the loop waits for
    /// it to exit, which it does only when it has held no session for a minute.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheKeeperLeavesItsOwnProcessesOutOfAScanOfTheInstallAndKeepsEveryOther()
    {
        using var root = ScratchDirectory.Create("keeper-scan");
        using var scope = new JobObjectScope();

        var standIn = PlantHost(root.Path);

        using var keeper = Keeper(root.Path, standIn, ["/c", "ping", "-n", "600", "127.0.0.1"], askToStop: _ => false);

        await Assert.That(keeper.EnsureStarted()).IsTrue();

        var (other, _) = await PlantedProcess.StartInAsync(scope, Path.Combine(root.Path, RegistrationTarget.CurrentDirectoryName, "a-client-started-it"), root.Path);

        var hostId = keeper.ProcessId!.Value;

        await WaitUntilAsync(
            () =>
            {
                using var found = BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId);
                return found.Held.Any(process => process.ProcessId == hostId);
            },
            "the scan never found the stand-in host under the install root");

        using var scanned = keeper.LeaveOutMine(BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId));

        var left = scanned.Held.Select(process => process.ProcessId).ToList();

        await Assert.That(left).DoesNotContain(hostId);
        await Assert.That(left).Contains(other.Id).Because("a process the keeper did not start is still waited for");
    }

    private static readonly Func<RootScan> NoScan = () => throw new InvalidOperationException("With nothing staged the loop scans nothing.");

    private static UpdateCandidate Candidate(string version) => new()
    {
        Version = version,
        IsDowngrade = false,
        DeltaCount = 0,
        FullPackageSize = 1,
    };

    /// <summary>Copies <c>cmd.exe</c> under the install root's <c>current</c>, where a host runs from.</summary>
    /// <param name="root">The scratch install root.</param>
    /// <returns>The copy's path.</returns>
    private static string PlantHost(string root)
    {
        var directory = Path.Combine(root, RegistrationTarget.CurrentDirectoryName);
        var planted = Path.Combine(directory, "stand-in-host.exe");

        _ = Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), planted, overwrite: true);

        return planted;
    }

    /// <summary>A keeper over a stand-in host, with the stop the arm scripts.</summary>
    /// <param name="root">The scratch install root.</param>
    /// <param name="standIn">The stand-in's image.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="askToStop">What asking it to stop does.</param>
    /// <returns>The keeper, which the arm disposes.</returns>
    private static SessionHostKeeper Keeper(string root, string standIn, IReadOnlyList<string> arguments, Func<int, bool> askToStop) =>
        new(root, standIn, arguments, root, askToStop, TestDefaults.ProcessHang, NullLogger.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > TestDefaults.ProcessHang)
            {
                throw new TimeoutException(whatWentWrong);
            }

            await Task.Delay(25);
        }
    }

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
            Name = "coordinator loop under test, with a session host",
        };

        thread.Start();
        return completion.Task;
    }

    /// <summary>A session host the arm scripts: running until the arm says it exits.</summary>
    /// <param name="events">Where the order of stops and reopens is kept.</param>
    private sealed class ScriptedHost(ConcurrentQueue<string> events) : ISessionHostHold, IDisposable
    {
        private readonly ManualResetEvent _exited = new(initialState: false);
        private readonly SemaphoreSlim _looked = new(0);
        private int _looks;
        private int _stops;
        private int _reopens;
        private int _gone;

        /// <summary>What happens while the host is stopping, before it has ended.</summary>
        public Action? WhileStopping { get; init; }

        public int Stops => Volatile.Read(ref _stops);

        public int Reopens => Volatile.Read(ref _reopens);

        public WaitHandle? Running
        {
            get
            {
                _ = Interlocked.Increment(ref _looks);
                _ = _looked.Release();
                return Volatile.Read(ref _gone) is 0 ? _exited : null;
            }
        }

        public void Exit()
        {
            Volatile.Write(ref _gone, 1);
            _ = _exited.Set();
        }

        public bool StopForUpdate()
        {
            events.Enqueue("stop");
            _ = Interlocked.Increment(ref _stops);
            WhileStopping?.Invoke();
            Exit();
            return true;
        }

        public void Reopen()
        {
            events.Enqueue("reopen");
            _ = Interlocked.Increment(ref _reopens);
        }

        /// <summary>Waits until the loop has looked at the host this many times, or has ended.</summary>
        /// <param name="count">How many looks.</param>
        /// <param name="loop">The loop's run.</param>
        /// <returns>Whether it looked that often.</returns>
        public async Task<bool> LookedAtAsync(int count, Task loop)
        {
            while (Volatile.Read(ref _looks) < count)
            {
                var next = _looked.WaitAsync(TestDefaults.InProcessHang);

                if (await Task.WhenAny(next, loop) == loop)
                {
                    await loop;
                    return Volatile.Read(ref _looks) >= count;
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
            _exited.Dispose();
            _looked.Dispose();
        }
    }

    /// <summary>
    /// Servers a client started, as the scan sees them: each an event the arm sets, so
    /// an exit is a call and never a kill.
    /// </summary>
    /// <param name="root">The scratch install root.</param>
    /// <param name="events">Where each pass's count is kept.</param>
    private sealed class ScriptedServers(string root, ConcurrentQueue<string> events) : IDisposable
    {
        private const int FirstProcessId = 200_000;

        private readonly Lock _gate = new();
        private readonly string _prefix = $"BrowserAI-coordinator-host-{Guid.NewGuid():N}-";
        private readonly List<EventWaitHandle> _exits = [];
        private readonly List<bool> _exited = [];
        private readonly SemaphoreSlim _scanned = new(0);
        private int _passes;

        /// <summary>A server starts.</summary>
        /// <returns>Its index, for <see cref="Exit"/>.</returns>
        public int Arrive()
        {
            lock (_gate)
            {
                _exits.Add(new EventWaitHandle(initialState: false, EventResetMode.ManualReset, _prefix + _exits.Count));
                _exited.Add(false);
                return _exits.Count - 1;
            }
        }

        /// <summary>A server exits: gone from every later pass first, then set.</summary>
        /// <param name="index">Which one.</param>
        public void Exit(int index)
        {
            lock (_gate)
            {
                _exited[index] = true;
            }

            _ = _exits[index].Set();
        }

        /// <summary>One pass: a fresh handle to every server that has not exited.</summary>
        /// <returns>The scan, which the loop disposes.</returns>
        public RootScan Next()
        {
            List<HeldProcess> held = [];

            lock (_gate)
            {
                for (var index = 0; index < _exits.Count; index++)
                {
                    if (!_exited[index])
                    {
                        held.Add(Hold(index));
                    }
                }

                events.Enqueue($"scan {held.Count}");
            }

            _ = Interlocked.Increment(ref _passes);
            _ = _scanned.Release();

            return new RootScan(held, []);
        }

        /// <summary>Waits until the loop has scanned this many times, or has ended.</summary>
        /// <param name="count">How many passes.</param>
        /// <param name="loop">The loop's run.</param>
        /// <returns>Whether it made them.</returns>
        public async Task<bool> PassesAsync(int count, Task loop)
        {
            while (Volatile.Read(ref _passes) < count)
            {
                var next = _scanned.WaitAsync(TestDefaults.InProcessHang);

                if (await Task.WhenAny(next, loop) == loop)
                {
                    await loop;
                    return Volatile.Read(ref _passes) >= count;
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

        private HeldProcess Hold(int index)
        {
            using var opened = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, _prefix + index);

            var handle = opened.SafeWaitHandle;

            // The held process owns the handle from here, and the emptied wrapper disposes nothing.
            opened.SafeWaitHandle = null;

            return new HeldProcess(FirstProcessId + index, 0, Path.Combine(root, RegistrationTarget.CurrentDirectoryName, $"server-{index}.exe"), handle);
        }
    }

    /// <summary>A staged package the arm sets, whose apply is recorded in order.</summary>
    /// <param name="events">Where the apply is recorded.</param>
    private sealed class RecordedStaged(ConcurrentQueue<string> events) : IStagedUpdates
    {
        public UpdateCandidate? Candidate { get; set; }

        public int Applies { get; private set; }

        public UpdateCandidate? Pending() => Candidate;

        public void ApplyAfterThisProcessExits(UpdateCandidate candidate)
        {
            events.Enqueue($"apply {candidate.Version}");
            Applies++;
        }
    }

    /// <summary>
    /// The browser tab's page as no arm here uses it: no listener, nothing queued, and
    /// a stop that always succeeds, so the loop's own rules are what the arms read.
    /// </summary>
    private sealed class NoPage : ICoordinatorPage
    {
        public bool IsServing => false;

        public void RunQueuedWork()
        {
        }

        public bool IsExitRequested() => false;

        public void Staged(UpdateCandidate? pending)
        {
        }

        public bool TryStop(bool final) => true;

        public void Tell(string sentence)
        {
        }
    }
}

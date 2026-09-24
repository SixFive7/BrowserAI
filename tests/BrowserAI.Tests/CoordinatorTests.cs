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
/// when many start at once, and what a second start does.
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
    /// process is this process's own.
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

        var events = new ConcurrentQueue<string>();
        var staged = new ScriptedStaged { Candidate = Candidate("9.9.9") };
        using var window = new RecordedWindow(events);
        var start = CoordinatorStart.Settle(root.Path, inbox, CoordinatorVerb.Recheck, grant: null, NullLogger.Instance, TestDefaults.InProcessHang);

        await Assert.That(start.Outcome).IsEqualTo(CoordinatorStartOutcome.Coordinator);

        using var pipe = start.Pipe;

        var ended = default(CoordinatorEnd?);
        var loop = new Thread(() => ended = new CoordinatorLoop(inbox, staged, window, NullLogger.Instance).Run())
        {
            IsBackground = true,
            Name = "coordinator loop under test",
        };

        loop.Start();

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
        await Assert.That(loop.Join(TestDefaults.InProcessHang)).IsTrue();
        await Assert.That(ended).IsEqualTo(CoordinatorEnd.NothingPending);
        await Assert.That(window.Shows).IsEqualTo(1);
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests;

/// <summary>
/// How an installed server a client started finds the session host: it connects
/// when one serves, and otherwise has one started, through the coordinator or, when
/// none runs, through the logon task.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b's front door, driven in process.</b> The maintainer's words of
/// 2026-10-03, verbatim: <i>"Q366 b - lets go with a fully build option c."</i> And the
/// brief it was relayed in: the server starts the coordinator through the logon task
/// if it is not running. This is the path every installed server takes before it
/// relays a byte, and no arm of the suite runs an installed server that far.
/// </para>
/// <para>
/// <b>Nothing here starts a process or touches the real scheduler.</b> The host is a
/// pipe the arm creates under the name the scratch root gives it; no coordinator
/// serves a scratch root, so asking one finds none; and the scheduler is
/// <see cref="ScratchLogonTasks"/>, which records what it is asked and starts nothing.
/// </para>
/// </remarks>
internal sealed class SessionHostAccessTests
{
    /// <summary>A host that already serves is connected to, and nothing is asked to start another.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHostThatServesIsConnectedToAndNothingIsAskedToStartOne()
    {
        using var root = ScratchDirectory.Create("host-access-serving");
        using var host = NamedPipes.CreateStreamServer(SessionHostProtocol.NameFor(root.Path));

        var tasks = new ScratchLogonTasks();

        using var connected = SessionHostAccess.Reach(root.Path, ScratchLogonTasks.AppId, tasks, NullLogger.Instance, out var how);

        await Assert.That(connected).IsNotNull();
        await Assert.That(how).IsEqualTo(HostReach.Connected);
        await Assert.That(tasks.Calls.ToList()).IsEmpty()
            .Because("a host that serves is used as it is, and no task is run to start another");
    }

    /// <summary>
    /// With no host and no coordinator, an installed server runs its logon task with
    /// <c>--start-host</c>, and connects to the host that task's coordinator starts.
    /// </summary>
    /// <remarks>
    /// <b>The task is the one the install hook registered</b>, named for the pack id and
    /// the install root, so the coordinator it starts is outside every client's tree
    /// and job; measured with stand-ins under every client exit, 21 of 21
    /// (<c>docs/evidence/2026-10-03-coordinator-survival</c>). Here the scheduler's
    /// start is the arm creating the host's pipe.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNoHostAndNoCoordinatorAnInstallRunsItsLogonTaskToStartOneAndConnectsToIt()
    {
        using var root = ScratchDirectory.Create("host-access-task");

        var name = SessionHostProtocol.NameFor(root.Path);
        var task = SignInTask.NameFor(ScratchLogonTasks.AppId, root.Path);
        var tasks = new ScratchLogonTasks();

        _ = tasks.Register(task, "the sign-in task's definition, as the install hook registers it");

        SafeFileHandle? started = null;

        try
        {
            var scheduler = new StartingScheduler(tasks, () => started = NamedPipes.CreateStreamServer(name));

            using var connected = SessionHostAccess.Reach(root.Path, ScratchLogonTasks.AppId, scheduler, NullLogger.Instance, out var how);

            await Assert.That(connected).IsNotNull();
            await Assert.That(how).IsEqualTo(HostReach.StartedThroughTheTask);
            await Assert.That(tasks.Calls.ToList()).IsEquivalentTo([$"register {task}", $"run {task} {CoordinatorProtocol.StartHostArgument}"]);
        }
        finally
        {
            started?.Dispose();
        }
    }

    /// <summary>A build that is not installed has no task to run, and serves its client itself.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABuildThatIsNotInstalledRunsNoTaskAndServesItsClientItself()
    {
        using var root = ScratchDirectory.Create("host-access-uninstalled");

        var tasks = new ScratchLogonTasks();

        using var connected = SessionHostAccess.Reach(root.Path, appId: null, tasks, NullLogger.Instance, out var how);

        await Assert.That(connected).IsNull();
        await Assert.That(how).IsEqualTo(HostReach.None);
        await Assert.That(tasks.Calls.ToList()).IsEmpty()
            .Because("an uninstalled build has no logon task, and running one named for it would start nothing of its own");
    }

    /// <summary>
    /// A logon task that does not start leaves the server to serve its client itself,
    /// and the log says why.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALogonTaskThatDoesNotStartLeavesTheServerToServeItsClientAndTheLogSaysWhy()
    {
        using var root = ScratchDirectory.Create("host-access-no-task");
        using var capture = new CapturingLoggerProvider();

        var tasks = new ScratchLogonTasks();
        var task = SignInTask.NameFor(ScratchLogonTasks.AppId, root.Path);

        using var connected = SessionHostAccess.Reach(root.Path, ScratchLogonTasks.AppId, tasks, capture.CreateLogger("BrowserAI.Access"), out var how);

        await Assert.That(connected).IsNull();
        await Assert.That(how).IsEqualTo(HostReach.None);
        await Assert.That(tasks.Calls.ToList()).IsEquivalentTo([$"run {task} {CoordinatorProtocol.StartHostArgument}"]);
        await Assert.That(capture.Logged($"the logon task did not start: no {task}")).IsTrue()
            .Because(string.Join(Environment.NewLine, capture.Records.Select(record => record.Message)));
    }

    /// <summary>A scheduler whose start of a registered task is the arm's own action.</summary>
    /// <param name="inner">The scheduler that records.</param>
    /// <param name="whenStarted">What a start does: the arm creating the host's pipe.</param>
    private sealed class StartingScheduler(ILogonTasks inner, Action whenStarted) : ILogonTasks
    {
        public TaskReport Register(string name, string definition) => inner.Register(name, definition);

        public TaskReport Remove(string name) => inner.Remove(name);

        public TaskReport Run(string name, string argument)
        {
            var report = inner.Run(name, argument);

            if (report.Change is TaskChange.Started)
            {
                whenStarted();
            }

            return report;
        }
    }
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// A blocked server's wake: a recheck to the coordinator that serves, or the
/// per-user logon task started on demand, whose process is the task scheduler's
/// child and not the server's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q283 a, the maintainer's words verbatim: <i>"Q283 a"</i>.</b> When the update
/// lane's census says <i>staged but not alone</i>, the server starts the scheduled
/// task on demand, which puts the coordinator outside the client's process tree --
/// the tree a client ends with <c>taskkill /T /F</c> -- and sends <c>recheck</c>
/// instead when the coordinator's pipe already exists.
/// </para>
/// <para>
/// <b>The last arm is the real thing</b>: a task under the test pack's id whose action
/// is the published app, started through the product's wake, and the process it
/// starts caught on a coordinator pipe this host holds for the data root that app
/// keys to when nothing tells it otherwise. That app is a second start there, so it
/// hands over and exits, and shows nothing.
/// </para>
/// </remarks>
internal sealed class CoordinatorWakeTests
{
    /// <summary>A serving coordinator is asked to look again, and no task is started.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AServingCoordinatorIsAskedToLookAgainAndNoTaskIsStarted()
    {
        using var root = ScratchDirectory.Create("wake-recheck");
        using var inbox = new CoordinatorInbox();
        using var pipe = CoordinatorPipe.Open(root.Path, inbox, NullLogger.Instance);

        var tasks = new ScratchLogonTasks();
        var report = new CoordinatorWake(root.Path, ScratchLogonTasks.AppId, tasks, NullLogger.Instance).Wake();

        await Assert.That(report.Outcome).IsEqualTo(WakeOutcome.Rechecked).Because(report.Why);
        await Assert.That(tasks.Calls.IsEmpty).IsTrue();

        await Assert.That(inbox.TryTake(out var arrival)).IsTrue();
        await Assert.That(arrival!.Verb).IsEqualTo(CoordinatorVerb.Recheck);
    }

    /// <summary>
    /// With no coordinator, the logon task named for the pack and the root is started
    /// with <c>--coordinate</c>; a task that is not there is a failure that says so,
    /// and without a pack id nothing is started.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNoCoordinatorTheLogonTaskIsStartedWithTheCoordinateArgument()
    {
        using var root = ScratchDirectory.Create("wake-task");

        var name = SignInTask.NameFor(ScratchLogonTasks.AppId, root.Path);
        var tasks = new ScratchLogonTasks();

        _ = tasks.Register(name, "<definition>");

        var started = new CoordinatorWake(root.Path, ScratchLogonTasks.AppId, tasks, NullLogger.Instance).Wake();

        await Assert.That(started.Outcome).IsEqualTo(WakeOutcome.Started).Because(started.Why);
        await Assert.That(tasks.Calls.Last()).IsEqualTo($"run {name} {CoordinatorProtocol.CoordinateArgument}");

        _ = tasks.Remove(name);

        var missing = new CoordinatorWake(root.Path, ScratchLogonTasks.AppId, tasks, NullLogger.Instance).Wake();

        await Assert.That(missing.Outcome).IsEqualTo(WakeOutcome.Failed);
        await Assert.That(missing.Why).Contains("did not start");

        var calls = tasks.Calls.Count;
        var unnamed = new CoordinatorWake(root.Path, appId: null, tasks, NullLogger.Instance).Wake();

        await Assert.That(unnamed.Outcome).IsEqualTo(WakeOutcome.Failed);
        await Assert.That(tasks.Calls.Count).IsEqualTo(calls);
    }

    /// <summary>
    /// The logon task started on demand runs the app with <c>--sign-in
    /// --coordinate</c>, as the task scheduler's child and not this process's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Caught in the act.</b> This host holds the coordinator's pipe for the data
    /// root the published app keys to when nothing overrides it, with an answer table
    /// that reads the calling process's command line, image and parent while that
    /// process waits for its acknowledgement. The task is the test pack's, under a
    /// scratch root's key, and is removed in a <c>finally</c>.
    /// </para>
    /// <para>
    /// <b>Its parent is the process hosting the task scheduler's service</b>, which the
    /// lifecycle research measured for a logon run; here it is measured for a run
    /// started on demand, and it is the whole of Q283 a's reason: a process whose
    /// parent is the scheduler is outside every client's tree. That host cannot be
    /// opened by this user, so it is identified by the pid the service control
    /// manager gives for the <c>Schedule</c> service.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATaskStartedOnDemandRunsTheAppWithCoordinateAsTheSchedulersChild()
    {
        PublishedSlice.EnsureAppFresh();

        using var root = ScratchDirectory.Create("wake-real");

        var caught = new CallerRecord();

        // The pipe first: without it the started app would become a coordinator.
        using var pipe = ServerPipe.OpenNamed(CoordinatorProtocol.NameFor(BrowserAiPaths.Real.RootAppDir), caught, NullLogger.Instance);

        var name = SignInTask.NameFor(ReleaseLayout.TestPackId, root.Path);

        try
        {
            var registered = ScheduledTasks.Instance.Register(name, SignInTask.DefinitionFor(PublishedSlice.AppExecutable, NamedPipes.CurrentUserSid(), root.Path));

            await Assert.That(registered.Change).IsEqualTo(TaskChange.Registered).Because(registered.Detail);

            var report = new CoordinatorWake(root.Path, ReleaseLayout.TestPackId, ScheduledTasks.Instance, NullLogger.Instance).Wake();

            await Assert.That(report.Outcome).IsEqualTo(WakeOutcome.Started).Because(report.Why);
            await Assert.That(await caught.Arrived.WaitAsync(TestDefaults.ProcessHang)).IsTrue();
        }
        finally
        {
            _ = ScheduledTasks.Instance.Remove(name);
        }

        await Assert.That(caught.Verb).IsEqualTo(CoordinatorProtocol.RecheckVerb);
        await Assert.That(caught.Image).IsEqualTo(PublishedSlice.AppExecutable, StringComparison.OrdinalIgnoreCase);
        await Assert.That(caught.CommandLine).EndsWith($"{CoordinatorProtocol.SignInArgument} {CoordinatorProtocol.CoordinateArgument}");
        await Assert.That(caught.Parent).IsNotEqualTo(Environment.ProcessId);

        // The parent is the process hosting the task scheduler's service, which this
        // user cannot open and so cannot recognise by its image: its pid comes from
        // the service control manager instead.
        await Assert.That(caught.Parent).IsEqualTo(ServiceHost.ProcessIdOf("Schedule"));
        await Assert.That(caught.ParentImage).IsNull();
    }

    /// <summary>A coordinator's answer table that reads who is asking before it answers.</summary>
    private sealed class CallerRecord : IPipeAnswers
    {
        /// <summary>Released when the first verb has been read.</summary>
        public SemaphoreSlim Arrived { get; } = new(0);

        public IReadOnlyList<string> Verbs { get; } = [CoordinatorProtocol.ShowVerb, CoordinatorProtocol.RecheckVerb];

        public string? Verb { get; private set; }

        public string? CommandLine { get; private set; }

        public string? Image { get; private set; }

        public int Parent { get; private set; }

        public string? ParentImage { get; private set; }

        public ServerPipeReply? Answer(string verb, int? clientProcessId)
        {
            if (CoordinatorProtocol.Parse(verb) is not { } taken)
            {
                return null;
            }

            // Read while the caller is alive: it waits for this answer before it exits.
            if (Verb is null && clientProcessId is { } caller)
            {
                Verb = verb;
                CommandLine = ProcessCommandLine.Of(caller);
                Image = ProcessCommandLine.ImagePathOf(caller);
                Parent = ParentProcess.IdOf(caller);
                ParentImage = ProcessCommandLine.ImagePathOf(Parent);
                Arrived.Release();
            }

            return new ServerPipeReply(CoordinatorProtocol.Acknowledged(taken, Environment.ProcessId));
        }
    }
}

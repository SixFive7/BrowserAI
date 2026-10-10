// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Background;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The uninstall hook's stop: what it says when nothing serves the pipe, when the
/// background answers and has not ended by the bound, and when something takes the
/// connection and refuses or never answers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-10</b> for the two outcomes lane ARCH's helper T1 left untested on
/// 2026-10-09, <see cref="BackgroundStopOutcome.StillRunning"/> and
/// <see cref="BackgroundStopOutcome.NotAsked"/>; the ended outcome is the real
/// installer's arms', through a background the Task Scheduler started. The hook goes on
/// to remove the task and the registrations whichever it is, so the outcome is its log's
/// sentence and nothing else, and these arms hold the sentences.
/// </para>
/// <para>
/// <b>The bound is the background's own on a first message</b>,
/// <see cref="BackgroundServer.FirstFrameBound"/>: an in-process background answers a
/// stop well inside it, and two arms wait it out once each, for a process that never
/// ends and for a question nothing answers.
/// </para>
/// </remarks>
internal sealed class BackgroundStopTests
{
    /// <summary>How long each stop here waits: for the answer, and then for the process to end.</summary>
    private static TimeSpan Bound => BackgroundServer.FirstFrameBound;

    /// <summary>
    /// A background that answers the stop and is still running when the bound runs out
    /// is <see cref="BackgroundStopOutcome.StillRunning"/>, named by the pid its record
    /// gave; with no pipe served at all it is <see cref="BackgroundStopOutcome.NoneRunning"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The record names this test host</b>, which is alive for the whole arm and is
    /// held by its pid and creation time exactly as a background's would be; the in-process
    /// background answers the stop and ends nothing, so the bound runs out.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a stop that called every answered stop ended.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnAnsweredStopWhoseBackgroundRunsOnIsStillRunningAndNoPipeIsNoneRunning()
    {
        using var data = ScratchDirectory.Create("background-stop-still-running");

        var pipe = PublishedBackground.NewPipeName();
        var record = BackgroundRecord.PathFor(data.Path, pipe);

        var (none, noneSaid) = await Task.Run(() => BackgroundStop.AskAndWait(pipe, record, Bound));

        await Assert.That(none).IsEqualTo(BackgroundStopOutcome.NoneRunning).Because(noneSaid);
        await Assert.That(noneSaid).IsEqualTo("No BrowserAI background was running, so none was stopped.");

        await using var background = BackgroundServerRig.Start(pipe);

        _ = BackgroundRecord.Started(record, BackgroundServerRig.Build, "image", HandWrittenRecord.StartedAt);

        var (outcome, detail) = await Task.Run(() => BackgroundStop.AskAndWait(pipe, record, Bound));

        await Assert.That(outcome).IsEqualTo(BackgroundStopOutcome.StillRunning).Because(detail);
        await Assert.That(detail).IsEqualTo($"BrowserAI's background, pid {Environment.ProcessId}, was asked to stop and was still closing its sessions when the hook moved on.");
        await Assert.That(background.Verbs.Stops).IsEqualTo(1).Because("the background was never asked");
    }

    /// <summary>
    /// Something that takes the connection and never answers, and something that answers
    /// with a refusal, are each <see cref="BackgroundStopOutcome.NotAsked"/>, with what
    /// went wrong in the sentence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neither is a background of ours</b>, so neither is the in-process server, which
    /// answers every stop: each is the server end of a pipe this arm holds, one that never
    /// reads and one that reads the question and writes a refusal.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a stop that called any answer it could not
    /// read nobody running.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStopNobodyAnswersAndOneThatIsRefusedAreNotAsked()
    {
        using var data = ScratchDirectory.Create("background-stop-not-asked");

        // Takes the connection, and never reads or answers.
        var silentPipe = PublishedBackground.NewPipeName();

        using (var silent = NamedPipes.CreateStreamServer(silentPipe))
        {
            var (outcome, detail) = await Task.Run(() => BackgroundStop.AskAndWait(silentPipe, BackgroundRecord.PathFor(data.Path, silentPipe), Bound));

            await Assert.That(outcome).IsEqualTo(BackgroundStopOutcome.NotAsked).Because(detail);
            await Assert.That(detail).StartsWith("BrowserAI's background could not be asked to stop: The background did not answer within ");
        }

        // Answers with a refusal.
        const string Refused = "The suite's stand-in refuses to stop.";
        var refusingPipe = PublishedBackground.NewPipeName();

        using var refusing = NamedPipes.CreateStreamServer(refusingPipe);

        var answering = Task.Run(async () =>
        {
            using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

            if (!NamedPipes.WaitForClient(refusing))
            {
                return;
            }

            await using var stream = new FileStream(refusing, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

            var one = new byte[1];

            while (await stream.ReadAsync(one, hang.Token) is 1 && one[0] is not (byte)'\n')
            {
            }

            var refusal = """{"jsonrpc":"2.0","id":"browserai/start-1","error":{"code":-32601,"message":"The suite's stand-in refuses to stop."}}""" + "\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(refusal), hang.Token);
            await stream.FlushAsync(hang.Token);
        });

        var (refusedOutcome, refusedDetail) = await Task.Run(() => BackgroundStop.AskAndWait(refusingPipe, BackgroundRecord.PathFor(data.Path, refusingPipe), Bound));

        await answering.WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(refusedOutcome).IsEqualTo(BackgroundStopOutcome.NotAsked).Because(refusedDetail);
        await Assert.That(refusedDetail).IsEqualTo($"BrowserAI's background could not be asked to stop: {Refused}");
    }
}

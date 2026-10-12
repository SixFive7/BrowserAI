// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The start Windows makes for a folder link: it asks the background over the pipe, opens the
/// folder or shows the picker itself, and with no background writes one line and does nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>55 a, 71 a and 75 a, the maintainer's words of 2026-10-11 verbatim: <i>"55 a"</i>,
/// <i>"71 a"</i> and <i>"75 a"</i>.</b> The in-process arms drive <see cref="LinkStart"/>
/// against the product's own <see cref="BrowserAI.Background.BackgroundServer"/> on a pipe of the arm's
/// (<see cref="BackgroundServerRig"/>), with the verbs and the desktop stood in for: no arm opens
/// Explorer or a picker, which are <c>LinkDesktop.Windows</c> in the product and banned in
/// the suite. The last arm starts the published binary for a link with no background, which
/// opens nothing by design.
/// </para>
/// </remarks>
internal sealed class LinkStartTests
{
    /// <summary>The scheme the arms' links carry: the test pack's.</summary>
    private const string Scheme = "browserai-test";

    /// <summary>
    /// A link clicked while no background answers writes one line, names what the link asked
    /// for and never its token, and opens nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkWhileNoBackgroundAnswersWritesOneLineAndOpensNothing()
    {
        using var logs = new CapturingLoggerProvider();

        var desktop = new RecordingDesktop();
        var token = PageGate.NewToken();
        var exit = await LinkStart.RunAsync($"{Scheme}:open-folder?folder=data&t={token}", Scheme, PublishedBackground.NewPipeName(), desktop.AsDesktop(), logs.CreateLogger("link"));

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(desktop.Opened).IsEmpty();
        await Assert.That(desktop.Prompts).IsEmpty();
        await Assert.That(logs.Records.Count).IsEqualTo(1);
        await Assert.That(logs.Records[0].Message).Contains("no background answered");
        await Assert.That(logs.Records[0].Message).Contains("open-folder 'data'");
        await Assert.That(logs.Records[0].Message).DoesNotContain(token);
    }

    /// <summary>
    /// A link the background refuses writes one line with the background's reason and opens
    /// nothing; the background was asked with the link's own token and what it names.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkTheBackgroundRefusesWritesOneLineAndOpensNothing()
    {
        await using var background = await BackgroundServerRig.StartAsync();
        using var logs = new CapturingLoggerProvider();

        var desktop = new RecordingDesktop();
        var token = PageGate.NewToken();
        var checkout = new string('a', 16);

        var exit = await LinkStart.RunAsync($"{Scheme}:open-session?session={checkout}&t={token}", Scheme, background.Identity.PipeName, desktop.AsDesktop(), logs.CreateLogger("link"));

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(desktop.Opened).IsEmpty();
        await Assert.That(background.Verbs.Links.Count).IsEqualTo(1);
        await Assert.That(background.Verbs.Links[0]).IsEqualTo(new LinkAsk(LinkVerb.OpenSession, token, Session: checkout));
        await Assert.That(logs.Records.Count).IsEqualTo(1);
        await Assert.That(logs.Records[0].Message).Contains("The suite's background refuses every folder link.");
        await Assert.That(logs.Records[0].Message).DoesNotContain(token);
    }

    /// <summary>
    /// A link the background answers with a folder is opened by the link start itself, which is
    /// the process the browser had Windows start.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkTheBackgroundAnswersIsOpenedByTheLinkStartItself()
    {
        var folder = @"C:\Users\sam\AppData\Local\BrowserAI\logs";
        var verbs = new FakeBackgroundVerbs
        {
            Link = static _ => Task.FromResult<LinkAnswer>(new LinkAnswer.Open(@"C:\Users\sam\AppData\Local\BrowserAI\logs")),
        };

        await using var background = await BackgroundServerRig.StartAsync(verbs: verbs);
        using var logs = new CapturingLoggerProvider();

        var desktop = new RecordingDesktop();
        var exit = await LinkStart.RunAsync($"{Scheme}:open-folder?folder=logs&t={PageGate.NewToken()}", Scheme, background.Identity.PipeName, desktop.AsDesktop(), logs.CreateLogger("link"));

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(desktop.Opened.ToList()).IsEquivalentTo([folder]);
        await Assert.That(logs.Logged($"opened {folder} in Explorer")).IsTrue();
    }

    /// <summary>
    /// A link to Windows' folder picker: the link start shows the picker with the background's
    /// sentence, and the choice goes back on the same connection, which the background acts on;
    /// a choice of nothing goes back as nothing chosen, and a link start that goes with its
    /// picker open leaves the background with nothing chosen.
    /// </summary>
    /// <remarks>
    /// <b>75 a</b>: <i>"the link's own program opens the folder or the picker itself. For the
    /// picker it holds the line to the background open, and sends the chosen folder back."</i>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APickIsShownByTheLinkStartAndItsChoiceGoesBackOnTheSameConnection()
    {
        var chosen = new ConcurrentQueue<FolderPick>();
        var verbs = new FakeBackgroundVerbs
        {
            Link = _ => Task.FromResult<LinkAnswer>(new LinkAnswer.Pick("Choose the folder to set up BrowserAI in for Codex.", picked =>
            {
                chosen.Enqueue(picked);
                return Task.FromResult(picked.Outcome is FolderPickOutcome.Picked ? "BrowserAI is set up in that project for Codex." : "No folder was chosen, so nothing changed.");
            })),
        };

        await using var background = await BackgroundServerRig.StartAsync(verbs: verbs);
        using var logs = new CapturingLoggerProvider();

        var link = $"{Scheme}:pick?action=register-in-project&client=codex&t={PageGate.NewToken()}";
        var desktop = new RecordingDesktop { Pick = FolderPick.Of(@"C:\Code\docs-site") };

        await Assert.That(await LinkStart.RunAsync(link, Scheme, background.Identity.PipeName, desktop.AsDesktop(), logs.CreateLogger("link"))).IsEqualTo(0);
        await Assert.That(desktop.Prompts.ToList()).IsEquivalentTo(["Choose the folder to set up BrowserAI in for Codex."]);
        await Assert.That(chosen.TryDequeue(out var first)).IsTrue();
        await Assert.That(first.Path).IsEqualTo(@"C:\Code\docs-site");
        await Assert.That(logs.Logged("BrowserAI is set up in that project for Codex.")).IsTrue();

        // Nothing chosen goes back as nothing chosen.
        desktop.Pick = FolderPick.Cancelled;

        await Assert.That(await LinkStart.RunAsync(link, Scheme, background.Identity.PipeName, desktop.AsDesktop(), logs.CreateLogger("link"))).IsEqualTo(0);
        await Assert.That(chosen.TryDequeue(out var second)).IsTrue();
        await Assert.That(second.Outcome).IsEqualTo(FolderPickOutcome.Cancelled);

        // A link start that goes with its picker open: the connection closes, and the
        // background is told nothing was chosen.
        desktop.Fails = true;

        await Assert.That(async () => await LinkStart.RunAsync(link, Scheme, background.Identity.PipeName, desktop.AsDesktop(), logs.CreateLogger("link"))).Throws<InvalidOperationException>();
        await BackgroundServerRig.WaitUntilAsync(() => !chosen.IsEmpty, "the background was never told that the gone link start chose nothing");
        await Assert.That(chosen.TryDequeue(out var third)).IsTrue();
        await Assert.That(third.Outcome).IsEqualTo(FolderPickOutcome.Cancelled);
    }

    /// <summary>
    /// A link that is not one of the page's, or of another scheme than the install's, is
    /// refused with one line before anything is asked.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMalformedOrForeignLinkIsRefusedWithOneLineAndNothingIsAsked()
    {
        await using var background = await BackgroundServerRig.StartAsync();

        var token = PageGate.NewToken();

        string[] links =
        [
            $"browserai:open-folder?folder=data&t={token}",
            $"{Scheme}:open-folder?folder=C:\\Windows&t={token}",
            $"{Scheme}:open-folder?folder=data",
            $"{Scheme}:remove?folder=data&t={token}",
            "https://attacker.example/",
            string.Empty,
        ];

        foreach (var link in links)
        {
            using var logs = new CapturingLoggerProvider();

            var desktop = new RecordingDesktop();

            await Assert.That(await LinkStart.RunAsync(link, Scheme, background.Identity.PipeName, desktop.AsDesktop(), logs.CreateLogger("link"))).IsEqualTo(1).Because(link);
            await Assert.That(logs.Records.Count).IsEqualTo(1).Because(link);
            await Assert.That(logs.Records[0].Message).Contains("is not one of its folder links").Because(link);
            await Assert.That(desktop.Opened).IsEmpty();
        }

        await Assert.That(background.Verbs.Links).IsEmpty();

        // A build that is not installed reads a link of any scheme BrowserAI could have
        // registered, and of no other.
        await Assert.That(LinkStart.Read($"browserai-test:open-folder?folder=data&t={token}", null)?.Verb).IsEqualTo(LinkVerb.OpenFolder);
        await Assert.That(LinkStart.Read($"browserai:open-folder?folder=data&t={token}", null)?.Verb).IsEqualTo(LinkVerb.OpenFolder);
        await Assert.That(LinkStart.Read($"other:open-folder?folder=data&t={token}", null)).IsNull();
    }

    /// <summary>
    /// The published binary started for a link with no background writes one line in its data
    /// root's log, exits with 1, and starts nothing: no background record, no window.
    /// </summary>
    /// <remarks>
    /// <b>71 a</b>, end to end: the start Windows makes for a link is the one executable's
    /// <see cref="BrowserAI.Registration.LinkScheme.LinkArgument"/>, and it must not start BrowserAI, since
    /// any web page can show such a link. The window watch fails the run on any window.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedBinaryStartedForALinkWithNoBackgroundWritesOneLineAndExits()
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        PublishedSlice.EnsureFresh();

        using var root = await ScratchDirectory.CreateAsync("link-start-published");

        var pipe = PublishedBackground.NewPipeName();
        var token = PageGate.NewToken();
        List<string> arguments = [BrowserAI.Registration.LinkScheme.LinkArgument, $"browserai:open-folder?folder=data&t={token}", BackgroundPipe.PipeArgument, pipe, BrowserAI.App.Program.DataRootArgument, root.Path];

        using var job = JobObject.CreateKillOnClose();
        using var process = JobLauncher.Start(job, PublishedSlice.Executable, arguments, root.Path, PublishedSlice.InheritedEnvironment());

        await Assert.That(await process.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(process.TryReadExitCode()).IsEqualTo(1);

        var logs = Path.Combine(root.Path, "logs");
        var said = Directory.Exists(logs)
            ? string.Join(Environment.NewLine, await Task.WhenAll(Directory.EnumerateFiles(logs).Select(ReadSharedAsync)))
            : string.Empty;

        await Assert.That(said).Contains("A folder link reached BrowserAI while no background answered, so nothing was opened (open-folder 'data').");
        await Assert.That(said).DoesNotContain(token);
        await Assert.That(File.Exists(BackgroundRecord.PathFor(root.Path, pipe))).IsFalse()
            .Because("a link with no background starts none");
    }

    /// <summary>Reads a log file its writer may still hold open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }

    /// <summary>A desktop that records what it was asked to open and to pick, and opens nothing.</summary>
    private sealed class RecordingDesktop
    {
        public ConcurrentQueue<string> Opened { get; } = new();

        public ConcurrentQueue<string> Prompts { get; } = new();

        /// <summary>What the picker answers.</summary>
        public FolderPick Pick { get; set; } = FolderPick.Cancelled;

        /// <summary>Whether the picker throws, as a link start that goes with its picker open does.</summary>
        public bool Fails { get; set; }

        public LinkDesktop AsDesktop() => new(
            directory =>
            {
                Opened.Enqueue(directory);
                return Task.FromResult(true);
            },
            prompt =>
            {
                Prompts.Enqueue(prompt);
                return Fails ? throw new InvalidOperationException("The link start went with its picker open.") : Task.FromResult(Pick);
            });
    }
}

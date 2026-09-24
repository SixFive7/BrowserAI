// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The suite's own window watch: whose a window is, what the run is told, and a
/// child run that shows one and is refused for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q278, and the maintainer's words are the specification:</b> <i>"make sure
/// this focus stealing is not something that ends up in the testbed."</i> The watch
/// itself is <see cref="WindowWatch"/>, started and stopped by the session hooks in
/// <see cref="SuiteCoverage"/>; this class is what proves it can see, can tell
/// the suite's windows from everybody else's, and fails a run that shows one.
/// </para>
/// <para>
/// <b>Three planted reds and not one, because each is a different failure.</b> The
/// classifier is driven both ways over synthetic sightings on every run, since a
/// live run on a clean machine only ever exercises <i>not ours</i>. The whole
/// mechanism is driven end to end by a child test host on a desktop nobody is
/// looking at, which shows a window and must exit 10 naming its class -- the
/// positive control a watch that stopped seeing would fail. And the real offender,
/// <c>RealInstallerTests.TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow</c>,
/// was watched red against the unmodified tree on 2026-09-24 before Q279 moved it,
/// which the CHANGELOG entry records with the run's own numbers.
/// </para>
/// </remarks>
internal sealed class WindowWatchTests
{
    private static readonly ProcessMark Host = new(1000, 5000);

    private const string Repository = @"C:\Source\SixFive7\BrowserAI";

    private const string ProfileScratch = @"C:\Users\someone\AppData\Local\BrowserAI-test-scratch";

    private const string SharedBrowsers = @"C:\Users\someone\AppData\Local\BrowserAI\browsers";

    private static readonly WindowWatchContext Context = new(
        Host,
        [Repository, Repository + @"\.work\test-scratch", ProfileScratch],
        SharedBrowsers,
        new HashSet<(long Window, int ProcessId)> { (0x10010, 4242) });

    /// <summary>
    /// The classifier counts what the suite started, by its three rules, and
    /// nothing else -- in both directions for every rule.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClassifierCountsWhatTheSuiteStartedAndNothingElse()
    {
        // The host's own window, and the host with its creation time unread.
        await Assert.That(WindowWatch.Classify(Sighting(1000, 5000), Context)).IsEqualTo(WindowOwnership.Host);
        await Assert.That(WindowWatch.Classify(Sighting(1000, null), Context)).IsEqualTo(WindowOwnership.Host);

        // A pid the host had, now somebody else's: same number, another creation.
        await Assert.That(WindowWatch.Classify(Sighting(1000, 9000), Context)).IsEqualTo(WindowOwnership.NotOurs);

        // A grandchild by a live chain, each parent created before its child.
        await Assert.That(WindowWatch.Classify(Sighting(3000, 7000, ancestry: [new(2000, 6000), Host]), Context))
            .IsEqualTo(WindowOwnership.Descendant);

        // ⚠️ A chain through a REUSED pid is not a descent: the "parent" was
        // created after its child, so the real parent is gone and a stranger holds
        // its number.
        await Assert.That(WindowWatch.Classify(Sighting(3000, 7000, ancestry: [new(2000, 8000), Host]), Context))
            .IsEqualTo(WindowOwnership.NotOurs);

        // A chain that never reaches the host, which is every window on the
        // machine that is not the suite's -- including the host's own ancestors.
        await Assert.That(WindowWatch.Classify(Sighting(3000, 7000, ancestry: [new(2000, 6000), new(900, 100)]), Context))
            .IsEqualTo(WindowOwnership.NotOurs);
        await Assert.That(WindowWatch.Classify(Sighting(800, 4000, ancestry: [new(700, 3000)]), Context))
            .IsEqualTo(WindowOwnership.NotOurs);

        // An image under each suite root, with no chain at all: the parent is gone,
        // which is the orphaned-console rig's whole shape.
        await Assert.That(WindowWatch.Classify(Sighting(5555, 7000, image: Repository + @"\src\BrowserAI\bin\Release\net10.0-windows\win-x64\publish\BrowserAI.Server.exe"), Context))
            .IsEqualTo(WindowOwnership.UnderSuiteRoot);
        await Assert.That(WindowWatch.Classify(Sighting(5555, 7000, image: ProfileScratch + @"\real-install-window-1\current\BrowserAI.exe"), Context))
            .IsEqualTo(WindowOwnership.UnderSuiteRoot);

        // Case and slashes are not the subject; whole segments are.
        await Assert.That(WindowWatch.Classify(Sighting(5555, 7000, image: "c:/source/sixfive7/browserai/x.exe"), Context))
            .IsEqualTo(WindowOwnership.UnderSuiteRoot);
        await Assert.That(WindowWatch.Classify(Sighting(5555, 7000, image: @"C:\Source\SixFive7\BrowserAI-other\x.exe"), Context))
            .IsEqualTo(WindowOwnership.NotOurs);

        // An image that could not be read, with nothing else to go on, is not ours.
        await Assert.That(WindowWatch.Classify(Sighting(5555, null, image: null), Context)).IsEqualTo(WindowOwnership.NotOurs);

        // Somebody else's editor.
        await Assert.That(WindowWatch.Classify(Sighting(6000, 7000, image: @"C:\Users\someone\AppData\Local\Programs\Microsoft VS Code\Code.exe"), Context))
            .IsEqualTo(WindowOwnership.NotOurs);

        // ⚠️ THE SHARED BROWSERS ROOT IS NEVER THE SUITE'S BY PATH, even under a
        // root that would otherwise contain it -- the maintainer's own sessions run
        // Chromium from there. A browser the suite started still counts, through
        // its chain.
        var wide = Context with { SuiteRoots = [@"C:\Users\someone\AppData\Local"] };
        var chrome = SharedBrowsers + @"\chromium-1245\chrome-win64\chrome.exe";

        await Assert.That(WindowWatch.Classify(Sighting(7777, 7000, image: chrome), wide)).IsEqualTo(WindowOwnership.NotOurs);
        await Assert.That(WindowWatch.Classify(Sighting(7777, 7000, image: chrome, ancestry: [Host]), wide)).IsEqualTo(WindowOwnership.Descendant);
        await Assert.That(WindowWatch.Classify(Sighting(7777, 7000, image: @"C:\Users\someone\AppData\Local\Elsewhere\x.exe"), wide))
            .IsEqualTo(WindowOwnership.UnderSuiteRoot);

        // A window that was visible before the session started is nobody's
        // offence, whoever owns it -- and the same handle under another owner is a
        // new window.
        await Assert.That(WindowWatch.Classify(Sighting(4242, 7000, window: 0x10010, image: Repository + @"\x.exe"), Context))
            .IsEqualTo(WindowOwnership.Baseline);
        await Assert.That(WindowWatch.Classify(Sighting(4243, 7000, window: 0x10010, image: Repository + @"\x.exe"), Context))
            .IsEqualTo(WindowOwnership.UnderSuiteRoot);

        // And which of those the run has to answer for.
        await Assert.That(WindowWatch.IsOffence(WindowOwnership.Host)).IsTrue();
        await Assert.That(WindowWatch.IsOffence(WindowOwnership.Descendant)).IsTrue();
        await Assert.That(WindowWatch.IsOffence(WindowOwnership.UnderSuiteRoot)).IsTrue();
        await Assert.That(WindowWatch.IsOffence(WindowOwnership.Baseline)).IsFalse();
        await Assert.That(WindowWatch.IsOffence(WindowOwnership.NotOurs)).IsFalse();
    }

    /// <summary>
    /// The row and the refusal say what a watch saw, in all three states, and
    /// only a shown window or an unwatched release costs a run.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRowAndTheRefusalSayWhatTheWatchSaw()
    {
        var dialog = Sighting(3000, 7000, className: "#32770", title: "BrowserAI", ancestry: [Host], image: ProfileScratch + @"\real-install-window-1\current\BrowserAI.exe");

        var clean = new WindowWatchReading(true, null, "Default", 120, 7, 30, []);
        var shown = new WindowWatchReading(true, null, "Default", 121, 8, 30, [(dialog with { Kind = WindowEventKind.Shown }, WindowOwnership.Descendant)]);
        var unwatched = new WindowWatchReading(false, "SetWinEventHook refused", "<unread>", 0, 0, 0, []);

        await Assert.That(WindowWatch.Judge(clean)).IsEqualTo(WindowWatchVerdict.Clean);
        await Assert.That(WindowWatch.Judge(shown)).IsEqualTo(WindowWatchVerdict.Shown);
        await Assert.That(WindowWatch.Judge(unwatched)).IsEqualTo(WindowWatchVerdict.Unwatched);

        // A shown window fails every run; a run nobody watched fails only a release.
        await Assert.That(WindowWatch.Decide(WindowWatchVerdict.Clean, isReleaseRun: false)).IsEqualTo(WindowWatchDecision.Proceed);
        await Assert.That(WindowWatch.Decide(WindowWatchVerdict.Clean, isReleaseRun: true)).IsEqualTo(WindowWatchDecision.Proceed);
        await Assert.That(WindowWatch.Decide(WindowWatchVerdict.Shown, isReleaseRun: false)).IsEqualTo(WindowWatchDecision.Refuse);
        await Assert.That(WindowWatch.Decide(WindowWatchVerdict.Shown, isReleaseRun: true)).IsEqualTo(WindowWatchDecision.Refuse);
        await Assert.That(WindowWatch.Decide(WindowWatchVerdict.Unwatched, isReleaseRun: false)).IsEqualTo(WindowWatchDecision.Proceed);
        await Assert.That(WindowWatch.Decide(WindowWatchVerdict.Unwatched, isReleaseRun: true)).IsEqualTo(WindowWatchDecision.Refuse);

        // Three words, none another's prefix, so a reader of one log can tell them apart.
        var words = new[] { WindowWatchVerdict.Clean, WindowWatchVerdict.Shown, WindowWatchVerdict.Unwatched }
            .Select(verdict => WindowWatch.StateWord(verdict).Trim())
            .ToList();

        await Assert.That(words.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(3);
        await Assert.That(words.Count(word => words.Exists(other => other != word && other.StartsWith(word, StringComparison.Ordinal)))).IsEqualTo(0);

        var cleanRow = WindowWatch.RowFor(clean);

        await Assert.That(cleanRow).Contains(WindowWatch.CleanState);
        await Assert.That(cleanRow).Contains("'Default'");
        await Assert.That(cleanRow).DoesNotContain("⚠️");

        // ⚠️ THE ROW A RENDERING BUG WOULD SHOW UP IN: the window by class, title,
        // image and reason, and the warning.
        var shownRow = WindowWatch.RowFor(shown);

        await Assert.That(shownRow).Contains(WindowWatch.ShownState);
        await Assert.That(shownRow).Contains("#32770 'BrowserAI'");
        await Assert.That(shownRow).Contains(@"real-install-window-1\current\BrowserAI.exe");
        await Assert.That(shownRow).Contains("a descendant of the test host");
        await Assert.That(shownRow).Contains("⚠️");
        await Assert.That(shownRow).DoesNotContain(WindowWatch.CleanState);

        await Assert.That(WindowWatch.RowFor(unwatched)).Contains(WindowWatch.UnwatchedState);
        await Assert.That(WindowWatch.RowFor(unwatched)).Contains("SetWinEventHook refused");

        // The refusal names the window and is the only place the marker appears.
        await Assert.That(WindowWatch.Refusal(shown, isReleaseRun: false)).Contains("#32770");
        await Assert.That(WindowWatch.Refusal(shown, isReleaseRun: false)).Contains(WindowWatch.RefusalMarker);
        await Assert.That(WindowWatch.Refusal(clean, isReleaseRun: true)).IsNull();
        await Assert.That(WindowWatch.Refusal(unwatched, isReleaseRun: false)).IsNull();
        await Assert.That(WindowWatch.Refusal(unwatched, isReleaseRun: true)).Contains(SuiteEnvironment.ReleaseRunVariable);
        await Assert.That(WindowWatch.RowFor(shown)).DoesNotContain(WindowWatch.RefusalMarker);
    }

    /// <summary>This run is watched, from the desktop its host runs on.</summary>
    /// <remarks>
    /// <b>The premise every clean row rests on.</b> A row reading <c>CLEAN</c>
    /// from a watch whose hooks never went in would be the false green the whole
    /// mechanism exists to prevent, so the hooks are asserted in, by name.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThisRunIsWatched()
    {
        var reading = WindowWatch.Reading;

        await Assert.That(reading.Watched).IsTrue().Because(reading.Failure ?? "the watch reported no failure and no success");
        await Assert.That(reading.Desktop).IsNotEqualTo("<unread>");
        await Assert.That(WindowWatch.CoverageRow).Contains(WindowWatch.Title);
    }

    /// <summary>
    /// A child run on a private desktop shows one window, exits 10 naming its
    /// class, and this run never sees it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The positive control a watch that stopped seeing would fail.</b> A
    /// run on a clean machine reports <c>CLEAN</c> whether or not its hooks are
    /// delivering anything, so the only way to know the refusal fires is to make it
    /// fire: the child is this same test host, filtered to
    /// <see cref="APlantedArmShowsOneWindowOnlyInAChildThatAsksForIt"/>, which
    /// shows one window through the probe's <c>window-show</c> mode and passes. The
    /// only thing left that can fail the child is its own session hook.
    /// </para>
    /// <para>
    /// <b>On a desktop nobody is looking at, and that is two assertions and not
    /// one.</b> The window never reaches the screen of the person using the machine,
    /// and this run's own watch -- on the default desktop -- never reports its class.
    /// The second is what <see cref="PrivateDesktop"/> is relied on for everywhere
    /// else in the suite, measured here on every run.
    /// </para>
    /// <para>
    /// <b>Exit 10 exactly</b>, which is what the platform returns for a session
    /// hook that throws; a failed test is 2, and a child whose planted arm failed
    /// would be 2 and would name nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AChildRunThatShowsAWindowExitsTenNamesItsClassAndThisRunNeverSeesIt()
    {
        // The recursion guard: the child's filter names the planted arm only, and
        // the child is the one process that has the variable.
        if (Environment.GetEnvironmentVariable(WindowWatch.PlantVariable) is { Length: > 0 })
        {
            await Assert.That(SuiteCoverage.ProbeReportFile).IsNotNull();
            return;
        }

        var host = Path.Combine(AppContext.BaseDirectory, "BrowserAI.Tests.exe");

        await Assert.That(File.Exists(host)).IsTrue().Because(host);

        using var scratch = ScratchDirectory.Create("window-watch-child");

        var className = $"BrowserAI_SuiteShownWindow_{Guid.NewGuid():N}";
        const string Filter = "/*/*/WindowWatchTests/" + nameof(APlantedArmShowsOneWindowOnlyInAChildThatAsksForIt);

        var environment = PublishedSlice.InheritedEnvironment();
        environment[SuiteFilter.ProbeVariable] = Path.Combine(scratch.Path, "filter.txt");
        environment[WindowWatch.PlantVariable] = className;

        // The child's refusal has to be the window one and nothing else, so it is
        // never a release run whatever this one is.
        _ = environment.Remove(SuiteEnvironment.ReleaseRunVariable);

        using var desktop = PrivateDesktop.Create("window-watch");
        using var job = JobObject.CreateKillOnClose();
        using var child = desktop.Launch(job, host, ["--disable-logo", "--treenode-filter", Filter], AppContext.BaseDirectory, environment);

        var standardOutput = ReadToEndAsync(child.StandardOutput);
        var standardError = ReadToEndAsync(child.StandardError);

        // A hang detector: the child discovers one assembly and runs one arm.
        var exited = await child.WaitForExitAsync(TestDefaults.ProcessHang);

        await Assert.That(exited).IsTrue().Because("a child test host running one arm that has not exited inside the suite's own hang detector is a hang");

        var console = await standardOutput + await standardError;

        await Assert.That(child.TryReadExitCode()).IsEqualTo(10).Because(console);
        await Assert.That(console).Contains(WindowWatch.RefusalMarker).Because(console);
        await Assert.That(console).Contains(className).Because(console);

        // The child's own coverage block, written beside its probe report before
        // the refusal threw, names the window in its row.
        var block = await File.ReadAllTextAsync(Path.Combine(scratch.Path, "suite-coverage.txt"));

        await Assert.That(block).Contains(WindowWatch.ShownState).Because(block);
        await Assert.That(block).Contains(className).Because(block);
        await Assert.That(block).Contains(desktop.Name).Because(block);

        // ⚠️ AND THIS RUN NEVER SAW IT: the private desktop is out of reach of the
        // default desktop's hooks, which is the property every private-desktop
        // launch in this suite depends on.
        await Assert.That(WindowWatch.SawClass(className)).IsFalse();
    }

    /// <summary>
    /// The planted arm: in a child that asks for it, one window is shown and seen;
    /// in any other run, nothing is shown.
    /// </summary>
    /// <remarks>
    /// <b>It passes in both cases, and the refusal is not its to raise.</b> In the
    /// child it shows one window, waits until that process's own watch has
    /// reported the class, and closes it; the session hook then fails the child.
    /// Everywhere else the variable is unset and nothing is launched.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APlantedArmShowsOneWindowOnlyInAChildThatAsksForIt()
    {
        var className = Environment.GetEnvironmentVariable(WindowWatch.PlantVariable);

        if (className is not { Length: > 0 })
        {
            // An ordinary run: the variable is unset, so nothing is launched.
            await Assert.That(className).IsNull();
            return;
        }

        // The child's scratch is the parent's, handed over through the probe
        // report's path. ScratchRoot is never taken here: its reclaim would sweep
        // the parent's live directories.
        var directory = Path.GetDirectoryName(SuiteCoverage.ProbeReportFile)
            ?? throw new InvalidOperationException("The planted arm runs only in a child that was handed a probe report path.");

        var report = Path.Combine(directory, "shown-window.json");

        using var scope = new JobObjectScope();

        _ = scope.Launch(
            Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe"),
            directory,
            "window-show",
            className,
            "BrowserAI suite window probe",
            report);

        var published = await ProbeReport.ReadAsync(report, TestDefaults.ProcessHang);

        await Assert.That((string?)published["error"]).IsNull();
        await Assert.That((bool?)published["visible"]).IsTrue();

        // Until this process's own watch has reported it: the event is delivered
        // asynchronously. ⚠️ Bounded by the IN-PROCESS hang detector and not the
        // process one, because the parent waits ProcessHang for this whole child:
        // with the same bound here, a blind watch made the parent's wait expire
        // first and the red read as a hang -- watched on 2026-09-24 with the show
        // events planted out, ten minutes to a message that named nothing. The
        // window goes with the scope's job when the arm returns.
        var deadline = DateTime.UtcNow + TestDefaults.InProcessHang;

        while (!WindowWatch.SawClass(className) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await Assert.That(WindowWatch.SawClass(className)).IsTrue();
    }

    private static async Task<string> ReadToEndAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    private static WindowSighting Sighting(
        int processId,
        long? created,
        IReadOnlyList<ProcessMark>? ancestry = null,
        string? image = null,
        long window = 0x20020,
        string className = "SomeClass",
        string title = "a window") =>
        new(
            WindowEventKind.Shown,
            window,
            processId,
            created,
            ThreadId: 1,
            className,
            title,
            "10,10 300x200",
            image,
            new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc),
            ancestry ?? []);
}

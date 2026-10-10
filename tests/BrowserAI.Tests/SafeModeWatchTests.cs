// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The safe-mode watch: what it reads a switch out of, whose a process is, what
/// the row and the refusal say, and a real process found by a real scan.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q312 b, decided 2026-10-03 by the maintainer, in his words: <i>"Q312
/// b"</i>.</b> The watch fails any run in which a Firefox of the suite's starts in
/// safe mode. Its live plant is <c>MOZ_SAFE_MODE_RESTART=1</c> in a child's
/// environment, which forces safe mode by a route the product does not switch off;
/// that was watched red against real Firefox launches, and it is recorded in the
/// changelog and not kept as an arm, because an arm that starts a Firefox in safe
/// mode is a run the watch exists to fail.
/// </para>
/// <para>
/// <b>What stays in the suite is both halves of the reasoning and the
/// plumbing.</b> The pure functions are driven both ways over synthetic readings.
/// The plumbing -- the enumeration by image path, the command-line read and the
/// parent chain -- is driven against a real process carrying a switch the
/// session's own watch does not look for, so this arm can never fail the run it
/// runs in.
/// </para>
/// </remarks>
internal sealed class SafeModeWatchTests
{
    private const long SessionStart = 6000;
    private const string BrowsersRoot = @"C:\Users\someone\AppData\Local\BrowserAI\browsers";
    private const string ProfileScratch = @"C:\Users\someone\AppData\Local\BrowserAI-test-scratch";
    private const string Firefox = BrowsersRoot + @"\firefox-1549\firefox\firefox.exe";

    /// <summary>
    /// A content process's command line in the shape measured on 2026-09-25,
    /// with the handle numbers and the paths shortened.
    /// </summary>
    private const string ContentProcess =
        "\"" + Firefox + "\" -contentproc -childID 1 -isForBrowser -prefsHandle 2228 -prefMapHandle 2232 -safeMode -appDir \"C:\\x\\browser\" 11532 tab";

    private static readonly ProcessMark Host = new(1000, 5000);

    private static readonly SafeModeWatchContext Context = new(Host, SessionStart, [ProfileScratch]);

    /// <summary>
    /// The switch is read as a whole argument in either spelling and any case, and
    /// nothing that merely contains it is.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSwitchIsReadAsAWholeArgumentAndNothingThatContainsItIs()
    {
        await Assert.That(SafeModeWatch.CarriesASwitch(ContentProcess, SafeModeWatch.Switches)).IsTrue();
        await Assert.That(SafeModeWatch.CarriesASwitch("\"" + Firefox + "\" -safe-mode", SafeModeWatch.Switches)).IsTrue();
        await Assert.That(SafeModeWatch.CarriesASwitch("firefox.exe -SAFEMODE", SafeModeWatch.Switches)).IsTrue();

        // The same command line without the switch is the control for the first line.
        await Assert.That(SafeModeWatch.CarriesASwitch(ContentProcess.Replace(" -safeMode", string.Empty, StringComparison.Ordinal), SafeModeWatch.Switches)).IsFalse();

        // A longer argument that starts with it, and a path that contains it.
        await Assert.That(SafeModeWatch.CarriesASwitch("firefox.exe -safeModeX", SafeModeWatch.Switches)).IsFalse();
        await Assert.That(SafeModeWatch.CarriesASwitch(@"C:\-safeMode\firefox.exe -contentproc", SafeModeWatch.Switches)).IsFalse();
    }

    /// <summary>
    /// A process is the run's when it was created after the session began and
    /// either descends from the host by a live chain or runs from a suite root.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessIsTheRunsByTheWindowWatchsRulesAndNothingBeforeTheRunIs()
    {
        // The chain a suite Firefox has: browser, launcher, node, then the host.
        ProcessMark[] chain = [new(3000, 6600), new(2900, 6500), new(2800, 6400), Host];

        await Assert.That(SafeModeWatch.Classify(Sighting(3100, 7000, Firefox, chain), Context)).IsEqualTo(SafeModeOwnership.Descendant);

        // Created before the session began: whatever its chain, not this run's.
        await Assert.That(SafeModeWatch.Classify(Sighting(3100, 5500, Firefox, chain), Context)).IsEqualTo(SafeModeOwnership.BeforeTheRun);

        // A chain that ends before the host, from the machine's own browsers root:
        // the maintainer's BrowserAI, or another worktree's run.
        await Assert.That(SafeModeWatch.Classify(Sighting(3100, 7000, Firefox, [new(3000, 6600), new(1, 100)]), Context)).IsEqualTo(SafeModeOwnership.NotOurs);

        // A "parent" created after its child is a stranger holding a reused pid.
        await Assert.That(SafeModeWatch.Classify(Sighting(3100, 7000, Firefox, [new(3000, 8000), Host]), Context)).IsEqualTo(SafeModeOwnership.NotOurs);

        // An image under a suite root, with no chain at all.
        await Assert.That(SafeModeWatch.Classify(Sighting(3100, 7000, ProfileScratch + @"\rig\browsers\firefox-1549\firefox\firefox.exe", []), Context))
            .IsEqualTo(SafeModeOwnership.UnderSuiteRoot);

        await Assert.That(SafeModeWatch.IsOffence(SafeModeOwnership.Descendant)).IsTrue();
        await Assert.That(SafeModeWatch.IsOffence(SafeModeOwnership.UnderSuiteRoot)).IsTrue();
        await Assert.That(SafeModeWatch.IsOffence(SafeModeOwnership.NotOurs)).IsFalse();
        await Assert.That(SafeModeWatch.IsOffence(SafeModeOwnership.BeforeTheRun)).IsFalse();
    }

    /// <summary>
    /// The three verdicts, the row each prints and the refusal each earns: only a
    /// clean run passes, and somebody else's safe mode is printed and does not fail.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRowAndTheRefusalSayWhatTheWatchSaw()
    {
        ProcessMark[] chain = [new(3000, 6600), Host];

        var ours = (Sighting(3100, 7000, Firefox, chain), SafeModeOwnership.Descendant);
        var theirs = (Sighting(4100, 7000, Firefox, []), SafeModeOwnership.NotOurs);

        var unwatched = new SafeModeWatchReading(0, "a scan failed: Win32Exception: refused", 0, []);
        var clean = new SafeModeWatchReading(12, null, 40, []);
        var someoneElses = new SafeModeWatchReading(12, null, 40, [theirs]);
        var safe = new SafeModeWatchReading(12, null, 40, [ours, theirs]);

        await Assert.That(SafeModeWatch.Judge(unwatched)).IsEqualTo(SafeModeVerdict.Unwatched);
        await Assert.That(SafeModeWatch.Judge(clean)).IsEqualTo(SafeModeVerdict.Clean);
        await Assert.That(SafeModeWatch.Judge(someoneElses)).IsEqualTo(SafeModeVerdict.Clean);
        await Assert.That(SafeModeWatch.Judge(safe)).IsEqualTo(SafeModeVerdict.SafeMode);

        await Assert.That(SafeModeWatch.Refusal(clean)).IsNull();
        await Assert.That(SafeModeWatch.Refusal(someoneElses)).IsNull();
        await Assert.That(SafeModeWatch.Refusal(unwatched)).Contains("refused");
        await Assert.That(SafeModeWatch.Refusal(safe)).Contains(SafeModeWatch.RefusalMarker);
        await Assert.That(SafeModeWatch.Refusal(safe)).Contains("pid 3100@7000");
        await Assert.That(SafeModeWatch.Refusal(safe)).DoesNotContain("pid 4100@7000");

        await Assert.That(SafeModeWatch.RowFor(clean)).Contains(SafeModeWatch.CleanState);
        await Assert.That(SafeModeWatch.RowFor(someoneElses)).Contains(SafeModeWatch.CleanState);
        await Assert.That(SafeModeWatch.RowFor(someoneElses)).Contains("pid 4100@7000");
        await Assert.That(SafeModeWatch.RowFor(safe)).Contains(SafeModeWatch.SafeModeState);
        await Assert.That(SafeModeWatch.RowFor(safe)).Contains("pid 3100@7000");

        // The switch is named on its own, because a long command line is cut
        // before it: Firefox puts -safeMode after its handles.
        await Assert.That(SafeModeWatch.RowFor(safe)).Contains("carries -safeMode");
        await Assert.That(SafeModeWatch.RowFor(unwatched)).Contains(SafeModeWatch.UnwatchedState);

        // And the session's own row reaches the block.
        await Assert.That(SuiteEnvironment.Summary()).Contains("  " + SafeModeWatch.Title.PadRight(20));
    }

    /// <summary>
    /// A scan finds a real process by a switch on its command line, under the root
    /// it was pointed at, and reads its chain back to the host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The process is a copy of <c>cmd.exe</c> in a scratch directory</b>, started
    /// with <c>/d /q /k</c> and a switch nothing else carries, inside a kill-on-close
    /// job the arm owns: the same stand-in <c>UpdateInProgressTests</c> uses for
    /// <c>Update.exe</c>. What a scan does with a Firefox content process is exactly
    /// what it does with this one, because it never reads anything but the image
    /// path, the command line and the parent chain.
    /// </para>
    /// <para>
    /// <b>The switch is one the session's watch does not look for</b>, so the
    /// session's own scan reads this process too and finds nothing to fail the run
    /// for. The control is the same scan asked for the real switches: it finds
    /// nothing here.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AScanFindsARealProcessByItsSwitchAndReadsItsChainBackToTheHost()
    {
        using var scratch = ScratchDirectory.Create("safe-mode-control");

        var image = Path.Combine(scratch.Path, "firefox.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), image);

        var planted = $"-browseraiSafeModeControl{Guid.NewGuid():N}";

        var context = new SafeModeWatchContext(
            new ProcessMark(Environment.ProcessId, ProcessIdentity.CreationTimeOf(Environment.ProcessId)),
            DateTime.UtcNow.ToFileTimeUtc(),
            [scratch.Path]);

        using var job = JobObject.CreateKillOnClose();

        // /d: no AutoRun commands; /q: no echo; /k rem: run a comment and stay,
        // reading a standard input nothing writes to.
        using var control = JobLauncher.Start(job, image, ["/d", "/q", "/k", "rem", planted], scratch.Path, ChildEnvironment.Build());

        var found = new SafeModeScan(context, [scratch.Path], [planted]).Poll();
        var real = new SafeModeScan(context, [scratch.Path], SafeModeWatch.Switches).Poll();

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].Sighting.ProcessId).IsEqualTo(control.Id);
        await Assert.That(found[0].Sighting.CommandLine).Contains(planted);
        await Assert.That(found[0].Sighting.Switch).IsEqualTo(planted);
        await Assert.That(found[0].Sighting.ImagePath).IsEqualTo(image, StringComparison.OrdinalIgnoreCase);
        await Assert.That(found[0].Ownership).IsEqualTo(SafeModeOwnership.Descendant);

        await Assert.That(real.Count).IsEqualTo(0);
    }

    private static SafeModeSighting Sighting(int processId, long created, string image, IReadOnlyList<ProcessMark> ancestry) =>
        new(processId, created, image, ContentProcess, "-safeMode", DateTime.UnixEpoch, ancestry);
}

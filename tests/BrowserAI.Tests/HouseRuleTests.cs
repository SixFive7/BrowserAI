// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Eight rules from <c>CLAUDE.md</c> that were held by habit alone — two until
/// 2026-08-17, four until 2026-08-23, one until 2026-08-24 and one until
/// 2026-09-15.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Prefer a mechanism over a habit. If a rule can be a failing test, a hook
/// or an analyzer, make it one."</b> Every rule below could be, and none was. The
/// SPDX rule was kept by 222 files out of 224 — which is what a habit looks like
/// right up to the moment somebody adds the 225th.
/// </para>
/// <para>
/// <b>Three of the four added on 2026-08-23 were found the same way: by somebody
/// noticing, rather than by a run.</b> Console windows flashing over whatever is
/// on screen, six suite runs to a release batch, from two launch sites out of ten
/// that had not set <c>CreateNoWindow</c>. A wall-clock bound on a call that
/// cannot block, which survived a hand sweep that deleted five of its siblings.
/// And a <c>STARTUPINFO</c> field the launcher had carried unread since it was
/// written, because the flag that makes Windows look at it was never set.
/// </para>
/// <para>
/// <b>The fourth is different and is the one to read the remarks on:</b>
/// <see cref="EveryRawHandleThatOutlivesItsExpressionIsRefCounted"/> is the
/// assertable part of a fix that could not be planted red, and it is a weaker
/// claim than the others make.
/// </para>
/// <para>
/// <b>The eighth, added 2026-09-15, is the only one a release gate found
/// rather than a person.</b>
/// <see cref="EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/> is
/// the rule a doc comment had already stated and stated wrongly — it asked for
/// a shared <i>key</i>, which holds an arm apart from the arms carrying the same
/// key and from nothing else, while the readers of a process-wide environment
/// variable are every child every other arm starts. Three arms went red for a
/// download nobody could see the cause of, and the release stopped at item 8.
/// </para>
/// <para>
/// <b>The seventh, added 2026-08-24, is the one the other six rest on.</b>
/// <see cref="TheScannedCorpusIsExactlyWhatGitSaysTheRepositoryHolds"/> asserts
/// that the corpus every scan here reads is the set of files git considers part
/// of this repository. It was a sentence in a doc comment that had been false by
/// 520 files, and a rule applied to the wrong corpus is a rule nobody is
/// keeping.
/// </para>
/// </remarks>
internal sealed partial class HouseRuleTests
{
    [Test]
    public async Task EverySourceFileCarriesTheTwoLineSpdxHeader()
    {
        // "Source files carry the two-line SPDX header used at the top of this
        // file. Use the LicenseRef- form; the bare FSL-1.1-MIT identifier is
        // forbidden by the licence."
        var offenders = new List<string>();

        foreach (var file in Scope())
        {
            // The head only. A header is a header because it is at the top, and
            // matching anywhere would pass on a file that merely mentions SPDX
            // in prose -- which several files in this repository do.
            var head = string.Join('\n', (await File.ReadAllLinesAsync(file.FullName)).Take(4));

            if (!head.Contains("SPDX-FileCopyrightText", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: no SPDX-FileCopyrightText line in the first four");
            }

            if (!head.Contains("SPDX-License-Identifier: LicenseRef-", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: no SPDX-License-Identifier line in the LicenseRef- form");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous. A scope that stopped resolving would leave the assertion
        // above green over nothing at all, which is the standing failure mode of
        // every test that reads the tree rather than the code.
        await Assert.That(Scope().Count).IsGreaterThan(200);
    }

    [Test]
    public async Task NoTestInTheTreeIsSkipped()
    {
        // "No release with a skipped, quarantined or conditionally-ignored test.
        // A Skip in the tree at release time is a red build wearing a disguise."
        //
        // ⚠️ The needle is composed rather than spelled, so this file does not
        // match its own scan -- the trap NeverByImageNameTests assembles its
        // needles to avoid, and one this session fell into twice before
        // learning. Comments are stripped as well, so that the sentence above
        // quoting the rule is not itself a violation of it.
        var needle = "[" + "Skip";
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(file => file.Extension is ".cs"))
        {
            if ((await RepositoryLayout.ReadCodeAsync(file)).Contains(needle, StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: carries a skip attribute");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // What this does NOT cover, said rather than implied: a test skipped at
        // run time because something it depends on failed. TUnit reports that as
        // "Skipped due to failed dependencies", and it is a consequence of a red
        // test rather than a second defect -- the run summary already carries it,
        // and the failing dependency is what has to be fixed.
        await Assert.That(RepositoryLayout.SourceAndScriptFiles.Count(file => file.Extension is ".cs")).IsGreaterThan(100);
    }

    /// <summary>
    /// <b>Every process this tree starts is started without a console
    /// window</b>, asserted at each launch site rather than counted per file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Redirecting the streams is not what suppresses the window, and the
    /// difference was measured rather than assumed</b> — 2026-08-23, Windows 11
    /// 26200, .NET 10, from a parent with <b>no console of its own</b>, streams
    /// redirected on both arms:
    /// </para>
    /// <list type="table">
    /// <listheader><term>Launch</term><description>New visible top-level windows</description></listheader>
    /// <item><term><c>CreateNoWindow = true</c></term><description><b>0</b></description></item>
    /// <item><term><c>CreateNoWindow = false</c></term><description><b>2</b> — <c>CASCADIA_HOSTING_WINDOW_CLASS</c> titled <c>pwsh.exe</c>, plus a <c>PseudoConsoleWindow</c></description></item>
    /// </list>
    /// <para>
    /// <b>The parent's own console decides whether the omission is visible,
    /// which is why this went unnoticed for so long.</b> Measured the same day:
    /// with a console-bearing parent, a child started without the flag
    /// <i>joins</i> that console — its process list went from 3 to 4 — and no
    /// window appears. With the flag, the child gets a private console nothing
    /// is attached to (process list 1). So a suite run from a terminal shows
    /// nothing, and the identical run from an agent harness, a scheduled task or
    /// any other windowless parent flashes a terminal per launch.
    /// <b>The console host on Windows 11 is Windows Terminal</b>, so a scan for
    /// the old <c>ConsoleWindowClass</c> finds nothing — that was measured too,
    /// and it is why the figures above come from a diff of every visible
    /// top-level window rather than from a class filter.
    /// </para>
    /// <para>
    /// <b>A tree-as-text scan rather than an analyzer, because the rule is not
    /// expressible as a banned symbol.</b> <c>BannedApiAnalyzers</c> can forbid a
    /// type; it cannot say <i>this type is fine as long as one of its properties
    /// is set</i>, and a real Roslyn analyzer would mean a new project shipped to
    /// enforce one rule. <c>NeverByImageNameTests</c> is the precedent, and it
    /// buys the same thing here: no exception for test code.
    /// </para>
    /// <para>
    /// <b>What it cannot see, said rather than implied.</b> (1) A factory
    /// returning a half-built <c>ProcessStartInfo</c> for its caller to finish is
    /// reported as a violation — deliberately, because the flag must be set where
    /// the object is built or a reader cannot tell either. (2) The flag must be
    /// within <see cref="LaunchWindowLines"/> lines of the launch; further away
    /// is not credited, and that is the same judgement. (3) It reads text, so a
    /// launch through something it does not name — a shell helper, a scheduled
    /// task, a script this repository does not hand-write — is invisible to it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryProcessLaunchInTheTreeSuppressesTheConsoleWindow()
    {
        var offenders = new List<string>();
        var sites = 0;

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(file => file.Extension is ".cs"))
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            foreach (var (line, what) in LaunchSites(lines))
            {
                sites++;

                if (!Window(lines, line).Contains(Flag, StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{Relative(file)}: {what} with no {Flag} within {LaunchWindowLines} lines"
                        + " — every suite run started by a windowless parent flashes a console window from it");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, synthetic on purpose. A scan whose needles
        // stopped matching reports the tree clean, and that is indistinguishable
        // from the tree BEING clean. This proves the predicate still catches the
        // exact omission the two real sites carried on 2026-08-23.
        string[] planted =
        [
            "        var start = new Process" + "StartInfo(\"pwsh\")",
            "        {",
            "            RedirectStandardOutput = true,",
            "            UseShellExecute = false,",
            "        };",
        ];

        await Assert.That(LaunchSites(planted).Count).IsEqualTo(1);
        await Assert.That(Window(planted, LaunchSites(planted)[0].Line).Contains(Flag, StringComparison.Ordinal)).IsFalse();

        // The other half of it: the same shape WITH the flag is not reported, so
        // the rule is about the flag and not about the type.
        var corrected = planted.Take(4).Append("            " + Flag + " = true,").Append("        };").ToArray();

        await Assert.That(Window(corrected, LaunchSites(corrected)[0].Line).Contains(Flag, StringComparison.Ordinal)).IsTrue();

        // Not vacuous over the tree either: ten launch sites as of 2026-08-23.
        await Assert.That(sites).IsGreaterThanOrEqualTo(8);
    }

    /// <summary>
    /// How far below a launch the flag may be set and still be credited.
    /// </summary>
    /// <remarks>
    /// The furthest real one in this tree is seven lines. Forty is generous
    /// enough that no honest site is reported and short enough that the flag
    /// stays where a reader of the launch can see it.
    /// </remarks>
    private const int LaunchWindowLines = 40;

    /// <summary>
    /// The managed property and the native creation flag share a name, so one
    /// needle covers both launch paths. Composed so this file does not match its
    /// own scan.
    /// </summary>
    private const string Flag = "CreateNo" + "Window";

    /// <summary>Every place in one file's lines that starts a process.</summary>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <returns>The line index of each launch, and what it is.</returns>
    private static List<(int Line, string What)> LaunchSites(string[] lines)
    {
        var managed = "Process" + "StartInfo";
        var native = "CreateProcess" + "W(";
        var found = new List<(int, string)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            // ⚠️ THE TYPE NAME AND `new ` SEPARATELY, not the two joined.
            // `new ProcessStartInfo` as one string missed
            // `new System.Diagnostics.ProcessStartInfo` — which is precisely
            // how one of the two sites this rule was written for was spelled,
            // so the first version of this scan reported one offender where
            // there were two. Requiring `new ` as well is what keeps the
            // mention on line 55 of FakeChildHarnessTests, and the type's name
            // in a `using`, from being read as a launch.
            if (line.Contains(managed, StringComparison.Ordinal)
                && line.Contains("new ", StringComparison.Ordinal))
            {
                found.Add((index, managed));
            }
            else if (line.Contains(native, StringComparison.Ordinal)
                && !line.Contains("partial", StringComparison.Ordinal)
                && !line.Contains("extern", StringComparison.Ordinal))
            {
                // The extern declaration is not a launch, and it is excluded by
                // what declares it rather than by the file it sits in — so a
                // second declaration is excluded for the same reason, and a
                // second CALL is not excluded at all.
                found.Add((index, native));
            }
        }

        return found;
    }

    /// <summary>The lines a launch's flag may be set in.</summary>
    /// <param name="lines">The file's lines.</param>
    /// <param name="start">The launch's own line.</param>
    /// <returns>Those lines, joined.</returns>
    private static string Window(string[] lines, int start) =>
        string.Join('\n', lines.Skip(start).Take(LaunchWindowLines));

    /// <summary>
    /// No assertion in the tree bounds a <b>measured</b> duration from above by
    /// a number it invented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule is that every duration is a hang detector or it is a
    /// defect</b>, settled 2026-08-18 and written out in
    /// [Testing](../../TESTING.md). A hang detector derives its
    /// bound from the vocabulary — <c>TestDefaults</c>, or the product constant
    /// the call is actually governed by. A promptness claim reaches for a
    /// literal, because there is nothing for it to derive from. So the
    /// mechanisable half of the rule is exactly that: <b>an upper bound on a
    /// measured duration has to name something.</b>
    /// </para>
    /// <para>
    /// <b>Kept by hand, and by hand it did not hold.</b> The 2026-08-18 sweep
    /// deleted five of these; two survived it, and both were still there on
    /// 2026-08-23 — one bounding a zero-timeout acquire by the product's own
    /// five-second gate, which then measured 5.2 s, and one bounding the same
    /// call in each of eight probe processes by a bare 1000. The second was
    /// found only because the first was fixed and somebody went looking.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not catch, stated rather than glossed:</b> a
    /// promptness claim wearing a named constant. <c>IsLessThan</c> against a
    /// small <c>TestDefaults</c> value is indistinguishable from a hang detector
    /// by text alone, and it is the reason the prose half of the rule still
    /// needs a reader. Lower bounds are not examined at all — load can only make
    /// one pass — and neither is the inverse shape a test uses to watch a call
    /// <i>fail</i> to return.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoAssertionBoundsAMeasuredDurationWithANumberItInvented()
    {
        var offenders = new List<string>();
        var bounds = 0;

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(file => file.Extension is ".cs"))
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            foreach (var (line, bound) in DurationBounds(lines))
            {
                bounds++;

                if (IsInvented(bound))
                {
                    offenders.Add(
                        $"{Relative(file)}: an assertion over a measured duration is bounded above by '{bound}'"
                        + " — a hang detector derives its bound; a number written here is a promptness claim,"
                        + " and a busy machine decides when it goes red");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, synthetic because the tree is clean and a
        // clean tree is indistinguishable from a scan whose needles stopped
        // matching. This is the exact assertion removed from
        // SessionLockTests.TheSweepScopeIsTryAcquireAndSkipAtZeroTimeout on
        // 2026-08-23, rebuilt from pieces so that this file does not match its
        // own scan.
        var measured = "(double)report[\"" + ClockKey + "\"]!)";
        string[] planted = ["        await " + Assertion + measured + "." + Upper + "(1000);"];

        var caught = DurationBounds(planted);

        await Assert.That(caught.Count).IsEqualTo(1);
        await Assert.That(IsInvented(caught[0].Bound)).IsTrue();

        // The other half of it: the same assertion deriving its bound is not
        // reported, so the rule is about the number and not about the shape.
        string[] corrected = ["        await " + Assertion + measured + "." + Upper + "(LockScopes.PerDirectoryGate.TotalMilliseconds);"];

        await Assert.That(IsInvented(DurationBounds(corrected)[0].Bound)).IsFalse();

        // And a wrapped duration is not a way round it either.
        string[] wrapped = ["        await " + Assertion + measured + "." + Upper + "(TimeSpan.FromSeconds(5));"];

        await Assert.That(IsInvented(DurationBounds(wrapped)[0].Bound)).IsTrue();

        // Not vacuous over the tree: the two surviving bounds as of 2026-08-23,
        // both of which derive.
        await Assert.That(bounds).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// A raw handle value that outlives the expression that read it is held by a
    /// reference count for as long as it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>SafeHandle</c>'s protection ends at
    /// <c>DangerousGetHandle</c>.</b> What comes back is a number, and a number
    /// does not root anything, does not stop a concurrent <c>Dispose</c> and
    /// does not survive Windows recycling the value. Used inline, in one
    /// expression, that is fine and every site here does it. Stored — written
    /// into native memory, or wrapped in a non-owning handle a thread pool will
    /// read later — it is a use-after-close waiting for a race, and the two ways
    /// out are a reference count or a barrier.
    /// </para>
    /// <para>
    /// <b>This is the assertable part of a fix that could not be planted
    /// red</b>, and the exception is recorded where the rule is, in
    /// [the working instructions](../../CLAUDE.md). It cannot
    /// prove the pair is correctly placed and it does not claim to: what it
    /// holds is that a file where a raw value escapes carries one at all, so the
    /// next escape written without one is a red build rather than a review
    /// somebody has to happen to do.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryRawHandleThatOutlivesItsExpressionIsRefCounted()
    {
        var offenders = new List<string>();
        var escapes = 0;

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            escapes += HandleEscapes(code.Split('\n')).Count;

            if (Offends(code) is { } escaped)
            {
                offenders.Add(
                    $"{Relative(file)}: {escaped} — the raw value outlives the expression that read it,"
                    + $" and this file has no {AddRef}/{ReleaseRef} pair holding the handle open across it");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL. Both escape shapes this tree has, rebuilt
        // from pieces: the job handle written into the process attribute list,
        // and the process handle wrapped for a thread-pool wait.
        var intoNativeStorage = "                " + Storage + "IntPtr(jobStorage, job.Handle." + Raw + "());";
        var intoANonOwningHandle = "            signal.SafeWaitHandle = new SafeWaitHandle(handle." + Raw + "(), " + NonOwning + ");";

        await Assert.That(HandleEscapes([intoNativeStorage, intoANonOwningHandle]).Count).IsEqualTo(2);
        await Assert.That(Offends(string.Join('\n', [intoNativeStorage, intoANonOwningHandle]))).IsNotNull();

        // The same two shapes WITH the pair are not reported, so the rule is
        // about the reference count and not about the escape.
        string[] held =
        [
            "                job.Handle." + AddRef + "(ref counted);",
            intoNativeStorage,
            intoANonOwningHandle,
            "            _job." + ReleaseRef + "();",
        ];

        await Assert.That(Offends(string.Join('\n', held))).IsNull();

        // And an inline use is not an escape at all, which is what keeps the
        // five sites that pass the value straight into one call out of this.
        string[] inline = ["        if (!IsProcessInJob(process, Handle." + Raw + "(), out var result))"];

        await Assert.That(HandleEscapes(inline).Count).IsEqualTo(0);
        await Assert.That(Offends(string.Join('\n', inline))).IsNull();

        // Not vacuous over the tree: the two real escapes as of 2026-08-23.
        await Assert.That(escapes).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// <b>Every arm in a test file that overrides a process-wide environment
    /// variable carries <c>[NotInParallel]</c> with no key</b> — which in TUnit
    /// means it runs beside nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A key is the wrong instrument, and the reason is the reader.</b>
    /// <c>Environment.SetEnvironmentVariable(name, value)</c> is process scope,
    /// and every child any arm in the suite starts inherits the block — so the
    /// readers of a scoped variable are not the other arms that scope it, they
    /// are <i>all of them</i>, and they hold no key, open no scope and cannot be
    /// enumerated. <b>Measured on the 2026-09-15 release gate, run 1:</b> the
    /// real-installer arm, correctly <c>[NotInParallel]</c> on the MCP client's
    /// group, held <c>BROWSERAI_ROOT</c> at an empty scratch data root for a few
    /// seconds; three arms in two other classes launched product children in that
    /// window, the children read the override, correctly reported first use of
    /// Chromium, began a <b>203.8 MB</b> provisioning download into the installer
    /// arm's scratch root and refused the call. Three red arms, none of them the
    /// one that opened the scope, and the release stopped.
    /// </para>
    /// <para>
    /// <b>The file is the unit, not the arm, and that is deliberate.</b> The
    /// scope is usually opened through a helper — this repository's is a private
    /// factory method returning one — so an arm-by-arm rule would have to resolve
    /// call graphs out of text, and the arm that forgets is by definition the one
    /// nobody classified. A file that constructs one is a file whose arms run
    /// alone. <c>[NotInParallel]</c> on the class satisfies it in one line and is
    /// what a new arm inherits.
    /// </para>
    /// <para>
    /// ⚠️ <b>This is the assertable half of a race no timing test can plant, and
    /// it is not a new exception to the rule that a behaviour change is watched
    /// red.</b> <b>The scan itself was planted red</b>, against this tree's own
    /// <c>RealInstallerTests</c> as it stood on 2026-09-15 — the keyed attribute
    /// the race was measured through — and against synthetic keyed, absent and
    /// keyless controls. <b>And once against a live reproduction</b>: a full
    /// suite run with the keyed attributes put back went red on all three of the
    /// original arms with the original message, on this scan naming the keyed
    /// arm, and on the installer arm's own data-root assertion naming sixteen
    /// foreign files — five reds, three of them the race and two of them the
    /// halves of the fix seeing it. What cannot be planted is the <i>race</i>
    /// <b>on demand</b>: it needs one
    /// arm's few-second window to overlap another arm's browser launch, which is a
    /// scheduler outcome rather than a call, and a test that provoked it by timing
    /// would be the promptness claim
    /// <see cref="NoAssertionBoundsAMeasuredDurationWithANumberItInvented"/>
    /// forbids. This sits where
    /// <see cref="EveryRawHandleThatOutlivesItsExpressionIsRefCounted"/> sits and
    /// makes the same weaker claim in the same words: it holds that the attribute
    /// is <i>there</i>, never that the sandbox is correct.
    /// </para>
    /// <para>
    /// <b>What it reads is the spelling, which is stricter than TUnit's semantics
    /// and is said rather than implied.</b> The trimmed line must be exactly the
    /// keyless attribute; <c>[NotInParallel(Order = 1)]</c> carries no key either
    /// and would still be reported. An attribute written on the same line as the
    /// member, or a key composed at run time, is outside it — the same line-based
    /// limit every scan in this file has.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing()
    {
        var offenders = new List<string>();
        var files = 0;

        foreach (var file in RepositoryLayout.SourceFilesUnder(["tests"], ["*.cs"]))
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            if (!OverridesTheEnvironment(code))
            {
                continue;
            }

            files++;
            offenders.AddRange(Offences(code).Select(arm =>
                $"{Relative(file)}: {arm} — this file constructs a scope over a PROCESS-wide environment"
                + " variable, and every child every other arm starts inherits it, so the arm has to carry"
                + $" a keyless {Exclusive} rather than a key or nothing"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE CONTROLS, synthetic because the tree is clean and a clean tree
        // is indistinguishable from a scan whose needles stopped matching. The
        // construction is rebuilt from pieces so that this file does not put
        // itself in scope.
        var construction = "        using var sandbox = new " + Sandbox + "(name, value);";
        string[] armAndBody = ["    [Test]", "    public async Task AnArmThatScopesOne()", "    {", construction, "    }"];

        // Keyed: the exact shape the race was measured through, and it does not
        // satisfy the rule.
        string[] keyed = ["internal sealed class Synthetic", "{", "    [NotInParallel(Group)]", .. armAndBody, "}"];

        await Assert.That(OverridesTheEnvironment(string.Join('\n', keyed))).IsTrue();
        await Assert.That(Offences(string.Join('\n', keyed)).Count).IsEqualTo(1);

        // Absent: nothing at all.
        string[] absent = ["internal sealed class Synthetic", "{", .. armAndBody, "}"];

        await Assert.That(Offences(string.Join('\n', absent)).Count).IsEqualTo(1);

        // Keyless on the arm, and keyless on the class — the form this repository
        // uses. Both satisfy it.
        string[] onTheArm = ["internal sealed class Synthetic", "{", "    " + Exclusive, .. armAndBody, "}"];
        string[] onTheClass = [Exclusive, "internal sealed class Synthetic", "{", .. armAndBody, "}"];

        await Assert.That(Offences(string.Join('\n', onTheArm))).IsEmpty();
        await Assert.That(Offences(string.Join('\n', onTheClass))).IsEmpty();

        // And the target-typed construction a factory method uses, which is the
        // shape a scan looking only for `new Sandbox(` would walk straight past.
        string[] throughAFactory =
        [
            "internal sealed class Synthetic",
            "{",
            .. armAndBody,
            "    private static " + Sandbox + " PointItAt(string directory) => new(Variable, directory);",
            "}",
        ];

        await Assert.That(OverridesTheEnvironment(string.Join('\n', throughAFactory))).IsTrue();
        await Assert.That(Offences(string.Join('\n', throughAFactory)).Count).IsEqualTo(1);

        // The other half of it: a file that opens no scope is not examined at
        // all -- an ordinary arm carrying no attribute is the normal state of
        // every other test file here -- so this is a rule about the scope
        // rather than about parallelism.
        string[] noScope = ["internal sealed class Synthetic", "{", "    [Test]", "    public async Task AnOrdinaryArm()", "    {", "    }", "}"];

        await Assert.That(OverridesTheEnvironment(string.Join('\n', noScope))).IsFalse();
        await Assert.That(Offences(string.Join('\n', noScope))).IsEmpty();

        // Not vacuous over the tree: the two files that construct one as of
        // 2026-09-15 -- the real-installer arm, and the registration arms.
        await Assert.That(files).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>The keyless attribute, which in TUnit runs an arm beside nothing at all.</summary>
    private const string Exclusive = "[NotInParallel]";

    /// <summary>The harness type, composed so this file does not match its own scan.</summary>
    private const string Sandbox = "Environment" + "Scope";

    /// <summary>The whole rule, for one file: nothing to say unless it opens a scope.</summary>
    /// <remarks>
    /// <b>The guard is inside the rule rather than beside it</b>, so the controls
    /// below exercise what the scan applies rather than a piece of it. Watched
    /// red on exactly that distinction: with the two halves separate, the
    /// no-scope control reported the ordinary unserialised arm that every other
    /// test file in this repository has.
    /// </remarks>
    /// <param name="code">The file's text, comment-only lines already blanked.</param>
    /// <returns>One description per uncovered arm; empty for a file with no scope in it.</returns>
    private static List<string> Offences(string code) =>
        OverridesTheEnvironment(code) ? Unserialised(code) : [];

    /// <summary>Whether a test file constructs a process-wide environment scope.</summary>
    /// <remarks>
    /// <b>Two shapes, because the second is the one in this tree.</b> A direct
    /// <c>new Sandbox(</c>, and a target-typed <c>new(</c> on a line that names
    /// the type — which is how a factory method returning one is written, and
    /// which a scan looking only for the first would miss entirely.
    /// </remarks>
    /// <param name="code">The file's text, comment-only lines already blanked.</param>
    /// <returns><see langword="true"/> when the file constructs one.</returns>
    private static bool OverridesTheEnvironment(string code) =>
        code.Split('\n').Any(line =>
            line.Contains("new " + Sandbox + "(", StringComparison.Ordinal)
            || (line.Contains(Sandbox, StringComparison.Ordinal) && line.Contains("new(", StringComparison.Ordinal)));

    /// <summary>The arms in a file that are not held apart from the whole suite.</summary>
    /// <param name="code">The file's text, comment-only lines already blanked.</param>
    /// <returns>One description per uncovered arm; empty when the class carries it.</returns>
    private static List<string> Unserialised(string code)
    {
        var lines = code.Split('\n');
        var uncovered = new List<string>();

        // On the class: the attribute, then whatever else the type carries, then
        // the declaration. One line covers every arm below it.
        if (lines.Index().Any(entry => entry.Item.Trim() is Exclusive && DeclaresAType(lines, entry.Index + 1)))
        {
            return [];
        }

        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() is not "[Test]")
            {
                continue;
            }

            var (first, last) = AttributeBlock(lines, index);

            if (!Enumerable.Range(first, last - first + 1).Any(line => lines[line].Trim() is Exclusive))
            {
                uncovered.Add(Member(lines, last));
            }
        }

        return uncovered;
    }

    /// <summary>Whether a type declaration follows, past any further attributes.</summary>
    /// <param name="lines">The file's lines.</param>
    /// <param name="from">The line after the attribute.</param>
    /// <returns><see langword="true"/> when the next declaration is a type.</returns>
    private static bool DeclaresAType(string[] lines, int from)
    {
        for (var index = from; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.Length is 0 || line.StartsWith('['))
            {
                continue;
            }

            return line.Contains(" class ", StringComparison.Ordinal)
                || line.Contains(" record ", StringComparison.Ordinal)
                || line.Contains(" struct ", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>The contiguous run of attribute lines one attribute sits in.</summary>
    /// <param name="lines">The file's lines.</param>
    /// <param name="at">A line holding an attribute.</param>
    /// <returns>The first and last line of the run.</returns>
    private static (int First, int Last) AttributeBlock(string[] lines, int at)
    {
        var first = at;
        var last = at;

        while (first > 0 && lines[first - 1].Trim().StartsWith('['))
        {
            first--;
        }

        while (last + 1 < lines.Length && lines[last + 1].Trim().StartsWith('['))
        {
            last++;
        }

        return (first, last);
    }

    /// <summary>The member an attribute block belongs to, for the failure message.</summary>
    /// <param name="lines">The file's lines.</param>
    /// <param name="last">The last line of the attribute block.</param>
    /// <returns>The declaration, trimmed, or the line number when there is none.</returns>
    private static string Member(string[] lines, int last) =>
        last + 1 < lines.Length && lines[last + 1].Trim().Length > 0
            ? lines[last + 1].Trim()
            : $"the arm declared at line {(last + 1).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// <b>No index walk follows every entry on the machine and then throws away
    /// the ones outside its subtree</b> — the filter belongs above the open, not
    /// below it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Following an entry is not free and the cost is not local.</b>
    /// <c>SessionIndex.Follow</c> ends at <c>SessionLock.ReadRecord</c>, which
    /// opens the session's store read-only and waits <c>SQLITE_BUSY</c> and
    /// <c>SQLITE_IOERR</c> out inside <c>RenameWindow</c>'s budget — so one
    /// session anywhere on the machine whose <c>browserai.data</c> is denied or
    /// whose holder is dying adds that budget to a walk scoped to a completely
    /// unrelated tree. A subtree caller that filters afterwards pays it for every
    /// session on the machine, to report the few that matched.
    /// <i>(Corrected 2026-08-26, previously "a strict parse of up to 250 log
    /// entries and all their arguments, opened through
    /// <c>RenameWindow.WaitOut</c> … whose <c>browserai.json</c> is denied or
    /// held" — the record is a database, there is no cap on entries and no
    /// argument is stored at all. The cost is smaller and the position of the
    /// filter is the same rule.)</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>This is a rule about the POSITION of the filter and not about the
    /// word.</b> <c>Follow()</c> stays exactly as it is and stays whole-machine:
    /// <c>SessionIndex.Sweep</c>, <c>SessionManager.LiveSessions</c> and
    /// <c>StraySweep</c> all need every entry, and the non-vacuity assertion
    /// below is what keeps a later change from quietly moving the two of them
    /// this scan can see onto the subtree read.
    /// </para>
    /// <para>
    /// <b>What it cannot see:</b> a filter applied to the walk's result somewhere
    /// other than inside the loop body — a LINQ <c>Where</c> over
    /// <c>Follow()</c>, or a second pass over a list. It reads the loop that
    /// <c>.Follow()</c> opens and nothing else, which is the shape both offenders
    /// had.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry()
    {
        var offenders = new List<string>();
        var walks = 0;

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            foreach (var (line, body) in IndexWalks(lines))
            {
                walks++;

                if (body.Contains(Subtree, StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{Relative(file)}: the loop opened at line {(line + 1).ToString(CultureInfo.InvariantCulture)}"
                        + $" follows every entry on the machine and then applies {Subtree} to what came back"
                        + " — every session's record is opened and strictly parsed to report the few under the prefix");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, synthetic on purpose. A scan whose needles
        // stopped matching reports the tree clean, and that is indistinguishable
        // from the tree BEING clean. This is the exact shape both offenders
        // carried on 2026-08-24.
        string[] planted =
        [
            "        foreach (var entry in _index." + Walk + "())",
            "        {",
            "            if (entry.Session is not { } session || !" + Subtree + "session, prefix))",
            "            {",
            "                continue;",
            "            }",
            "        }",
        ];

        await Assert.That(IndexWalks(planted).Count).IsEqualTo(1);
        await Assert.That(IndexWalks(planted)[0].Body.Contains(Subtree, StringComparison.Ordinal)).IsTrue();

        // The other half of it: the same loop over the scoped read, with no
        // filter in the body, is not reported — so the rule is the position of
        // the filter rather than the presence of the word.
        string[] corrected =
        [
            "        foreach (var entry in _index.FollowUnder(prefix))",
            "        {",
            "            if (entry.Session is not { } session)",
            "            {",
            "                continue;",
            "            }",
            "        }",
        ];

        await Assert.That(IndexWalks(corrected)).IsEmpty();

        // Not vacuous over the tree either: two loops of this shape as of
        // 2026-08-24, re-measured rather than estimated — `SessionManager`'s
        // reinstall census and `StraySweep`'s — and both must stay
        // whole-machine. `SessionIndex.Sweep` is a third whole-machine reader and
        // is deliberately not one of these two: it calls `Follow()` on itself and
        // reads the result into a local, which is a shape this scan does not see
        // and says so above.
        await Assert.That(walks).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// <b>The subtree prefix is derived in exactly one place</b> — case-fold,
    /// then a separator — and that place is <c>CanonicalPath</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>W8, and the reason it needs a scan rather than a note.</b>
    /// <c>SessionIndex.IsUnder</c>'s own remark says <i>"Do not re-derive it …
    /// Two spellings of this predicate is the class of defect this repository
    /// keeps re-finding"</i> — and <c>SessionManager.Beneath</c> was re-deriving
    /// the prefix three lines below a call into that very member, and had been
    /// since before the remark was written. It was benign because its input
    /// happened to be canonical already; what makes it worth a mechanism is that
    /// nothing said so and nothing would have noticed when it stopped being
    /// true.
    /// </para>
    /// <para>
    /// <b>The shape, and it is the shape both offenders had:</b> a case-fold
    /// whose result is extended by a directory separator within the next few
    /// lines. The predicate that <i>consumes</i> a prefix is deliberately not
    /// caught — <c>IsUnder</c> composes <c>Key + separator</c> with no fold of
    /// its own, because <see cref="BrowserAI.Sessions.SessionPath.Key"/> is
    /// already folded — so this is a rule about deriving the prefix rather than
    /// about the characters.
    /// </para>
    /// <para>
    /// <b>What it cannot see:</b> a derivation split across more than the window
    /// below, or one that folds through a helper rather than through
    /// <c>ToUpperInvariant</c>. Both would be new shapes rather than the one
    /// that was there.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePrefixIsDerivedInOnePlaceAndTheRestOfTheTreeAsksForIt()
    {
        var offenders = new List<string>();
        var folds = 0;

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            folds += lines.Count(line => line.Contains(Fold, StringComparison.Ordinal));

            if (string.Equals(file.Name, Home, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var line in PrefixDerivations(lines))
            {
                offenders.Add(
                    $"{Relative(file)}: line {(line + 1).ToString(CultureInfo.InvariantCulture)} case-folds a path and then extends it"
                    + $" with a directory separator, which is a second spelling of the subtree prefix. Call {Home[..^3]}.PrefixOf instead");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, synthetic on purpose and copied from the
        // offender as it stood on 2026-08-26. A scan whose needle stopped
        // matching reports the tree clean, which is indistinguishable from the
        // tree BEING clean.
        string[] planted =
        [
            "        var prefix = root." + Fold + ";",
            "        prefix = prefix.EndsWith(Path.DirectorySeparatorChar) ? prefix : prefix + Path.DirectorySeparatorChar;",
        ];

        await Assert.That(PrefixDerivations(planted).Count).IsEqualTo(1);

        // The other half: the corrected shape, and the consumer that is allowed
        // to compose a separator because it folds nothing.
        string[] corrected =
        [
            "        var prefix = " + Home[..^3] + ".PrefixOf(root);",
            "        return (candidate.Key + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.Ordinal);",
        ];

        await Assert.That(PrefixDerivations(corrected)).IsEmpty();

        // Not vacuous over the tree: the fold itself is still there to be found,
        // in the identity chain and in the provisioning gate's own digest.
        await Assert.That(folds).IsGreaterThanOrEqualTo(3);
    }

    /// <summary>
    /// <b>The three whole-machine index readers still take the whole-machine
    /// read</b>, by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-08-24, because <c>SessionIndex.Follow</c>'s own remark
    /// said this was already asserted and it was not.</b> That remark named three
    /// callers — <c>Sweep</c>, <c>SessionManager.LiveSessions</c> and
    /// <c>StraySweep</c> — and cited
    /// <see cref="NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry"/> for it.
    /// That scan names none of them: it holds that no <c>foreach</c> over the
    /// whole-machine read filters by subtree inside its body, and that at least
    /// two such loops exist. Two of the three were therefore held only by a
    /// <c>&gt;= 2</c> lower bound on a loop <i>shape</i>, and the third — <c>Sweep</c>,
    /// which reads the result into a local — by nothing at all, as that scan's own
    /// comment concedes.
    /// </para>
    /// <para>
    /// <b>Each is machine-wide by design and the reason differs per caller</b>:
    /// <c>SessionIndex.Sweep</c> would otherwise leave entries un-swept forever;
    /// <c>SessionManager.LiveSessions</c> answers about a browsers root that is
    /// machine-wide; <c>StraySweep.AttributeByProfileLock</c>'s whole reach is the
    /// point of it. Moving any of them onto the subtree read is a behaviour change
    /// that has to be made deliberately, which is what a named assertion costs and
    /// a lower bound does not.
    /// </para>
    /// <para>
    /// <b>It reads the member's brace-matched body</b>, so the call may sit in a
    /// loop header, in a local, or in an expression — the shape is not the rule,
    /// the whole-machine read is. The positive control is synthetic and runs in
    /// both directions, because every member in the tree satisfies this today and
    /// a scan that can only come back clean is indistinguishable from one that
    /// cannot read.
    /// </para>
    /// <para>
    /// <b>Watched red twice, against the real regression rather than against the
    /// control.</b> With <c>SessionManager.LiveSessions</c> moved onto the subtree
    /// read this failed naming the file and the member, and the older scan failed
    /// too — with <i>Expected to be greater than or equal to 2 but received 1</i>,
    /// which names nothing. With <c>SessionIndex.Sweep</c> moved instead, this was
    /// the <b>only</b> red of the nine in this class: that caller reads the result
    /// into a local, which is a shape the loop scan cannot see. That is the gap
    /// this test closes, stated as what was measured.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheThreeWholeMachineIndexReadersStillTakeTheWholeMachineRead()
    {
        (string File, string Member)[] readers =
        [
            (Path.Combine("src", "BrowserAI", "Sessions", "SessionIndex.cs"), "public SessionIndexSweep Sweep()"),
            (Path.Combine("src", "BrowserAI", "Sessions", "SessionManager.cs"), "private List<string> LiveSessions()"),
            (Path.Combine("src", "BrowserAI", "Sessions", "StraySweep.cs"), "private int AttributeByProfileLock("),
        ];

        var offenders = new List<string>();

        foreach (var (file, member) in readers)
        {
            var full = new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, file));

            if (!full.Exists)
            {
                offenders.Add($"{file}: there is no such file, so the reader it was supposed to hold cannot be checked");
                continue;
            }

            offenders.AddRange(NotTakingTheWholeMachineRead((await RepositoryLayout.ReadCodeAsync(full)).Split('\n'), file, member));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, BOTH DIRECTIONS. The first is the shape a
        // later change would leave behind; the second is what the tree has now.
        string[] moved =
        [
            "    private List<string> LiveSessions()",
            "    {",
            "        foreach (var entry in _index." + Walk + "Under(prefix))",
            "        {",
            "        }",
            "    }",
        ];

        string[] kept =
        [
            "    private List<string> LiveSessions()",
            "    {",
            "        foreach (var entry in _index." + Walk + "())",
            "        {",
            "        }",
            "    }",
        ];

        // ⚠️ AND THE SECOND SPELLING, BOTH DIRECTIONS. Since 2026-08-26 a caller
        // may take the whole-machine read at a stated depth — the sweep does, so
        // that it opens no session's store — and scope is what this gate is
        // about. `Walk(under: null, …)` is the same scope and passes;
        // `Walk(prefix, …)` is the regression at any depth and does not.
        string[] atADepth =
        [
            "    private List<string> LiveSessions()",
            "    {",
            "        foreach (var entry in " + WholeMachine + ", SessionIndexDepth.Guard))",
            "        {",
            "        }",
            "    }",
        ];

        string[] scopedAtADepth =
        [
            "    private List<string> LiveSessions()",
            "    {",
            "        foreach (var entry in " + WholeMachine.Replace("under: null", "prefix", StringComparison.Ordinal) + ", SessionIndexDepth.Guard))",
            "        {",
            "        }",
            "    }",
        ];

        await Assert.That(NotTakingTheWholeMachineRead(moved, "synthetic.cs", "private List<string> LiveSessions()").Count).IsEqualTo(1);
        await Assert.That(NotTakingTheWholeMachineRead(kept, "synthetic.cs", "private List<string> LiveSessions()")).IsEmpty();
        await Assert.That(NotTakingTheWholeMachineRead(atADepth, "synthetic.cs", "private List<string> LiveSessions()")).IsEmpty();
        await Assert.That(NotTakingTheWholeMachineRead(scopedAtADepth, "synthetic.cs", "private List<string> LiveSessions()").Count).IsEqualTo(1);

        // And a member that is not there at all is a finding rather than a pass,
        // because a renamed member would otherwise be silently unchecked.
        await Assert.That(NotTakingTheWholeMachineRead(kept, "synthetic.cs", "private List<string> Renamed()").Count).IsEqualTo(1);
    }

    /// <summary>
    /// Whether one named member still reads the whole machine, as findings.
    /// </summary>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <param name="file">What to call it in a finding.</param>
    /// <param name="member">The declaration the body is matched from.</param>
    /// <returns>Nothing when it does; one finding when it does not.</returns>
    private static List<string> NotTakingTheWholeMachineRead(string[] lines, string file, string member)
    {
        var declared = Array.FindIndex(lines, line => line.Contains(member, StringComparison.Ordinal));

        if (declared < 0)
        {
            return [$"{file}: '{member}' is not declared there, so nothing holds it to the whole-machine read"];
        }

        var body = Body(lines, declared);

        // ⚠️ TWO SPELLINGS OF ONE READ, and the second arrived 2026-08-26 with
        // the sweep going probe-first. What this gate means is that the reader's
        // SCOPE is the whole machine; what it used to test was the single
        // spelling that then existed. `Walk(under: null, …)` is the same scope at
        // a shallower depth -- the sweep opens no session's store -- and reading
        // depth as scope would have made the honest fix indistinguishable from
        // the regression this exists to catch.
        return body.Contains(Walk + "()", StringComparison.Ordinal) || body.Contains(WholeMachine, StringComparison.Ordinal)
            ? []
            : [$"{file}: '{member}' calls neither {Walk}() nor {WholeMachine}…), so a whole-machine reader has been scoped to a subtree"];
    }

    /// <summary>
    /// The predicate that decides a session is under a subtree, composed so this
    /// file does not match its own scan.
    /// </summary>
    private const string Subtree = "IsUnder" + "(";

    /// <summary>The whole-machine read, composed for the same reason.</summary>
    private const string Walk = "Fol" + "low";

    /// <summary>
    /// The whole-machine read spelled as the walk itself, which is what a caller
    /// that also chooses a depth writes.
    /// </summary>
    /// <remarks>
    /// <b>The <c>under: null</c> is the load-bearing half and the depth is not.</b>
    /// Scope is what this gate is about: <c>Walk(prefix, …)</c> is the regression,
    /// whatever depth follows it.
    /// </remarks>
    private const string WholeMachine = "Wal" + "k(under: null";

    /// <summary>The case-fold half of a prefix derivation.</summary>
    private const string Fold = "ToUpper" + "Invariant()";

    /// <summary>The one file a prefix derivation may live in.</summary>
    private const string Home = "Canonical" + "Path.cs";

    /// <summary>
    /// How many lines past a case-fold the separator may appear before this
    /// stops reading the two as one derivation.
    /// </summary>
    /// <remarks>
    /// <b>Two, because that is what the shape needs and one more than it uses.</b>
    /// Both offenders wrote the fold and the separator on consecutive lines; a
    /// window wide enough to span a whole method would report the identity
    /// chain, which folds a path for a hash and never for a prefix.
    /// </remarks>
    private const int PrefixWindow = 2;

    /// <summary>Every prefix derivation in one file's lines.</summary>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <returns>The line index of each derivation.</returns>
    private static List<int> PrefixDerivations(string[] lines)
    {
        var found = new List<int>();

        for (var at = 0; at < lines.Length; at++)
        {
            if (!lines[at].Contains(Fold, StringComparison.Ordinal))
            {
                continue;
            }

            var window = string.Join('\n', lines.Skip(at).Take(PrefixWindow + 1));

            if (window.Contains("DirectorySeparatorChar", StringComparison.Ordinal))
            {
                found.Add(at);
            }
        }

        return found;
    }

    /// <summary>Every loop in one file's lines that walks the whole index.</summary>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <returns>The line index of each walk, and the text of its body.</returns>
    private static List<(int Line, string Body)> IndexWalks(string[] lines)
    {
        var walks = new List<(int Line, string Body)>();

        for (var at = 0; at < lines.Length; at++)
        {
            if (!lines[at].Contains("foreach", StringComparison.Ordinal)
                || !lines[at].Contains("." + Walk + "()", StringComparison.Ordinal))
            {
                continue;
            }

            walks.Add((at, Body(lines, at)));
        }

        return walks;
    }

    /// <summary>One loop body, brace-matched from the header down.</summary>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <param name="header">The line the <c>foreach</c> is on.</param>
    /// <returns>Every line of the body, joined.</returns>
    private static string Body(string[] lines, int header)
    {
        var body = new List<string>();
        var depth = 0;
        var opened = false;

        for (var at = header; at < lines.Length; at++)
        {
            foreach (var character in lines[at])
            {
                if (character is '{')
                {
                    depth++;
                    opened = true;
                }
                else if (character is '}')
                {
                    depth--;
                }
            }

            if (opened)
            {
                body.Add(lines[at]);
            }

            if (opened && depth <= 0)
            {
                break;
            }
        }

        return string.Join('\n', body);
    }

    /// <summary>
    /// Every <c>STARTUPINFO</c> field this tree assigns is paired with the flag
    /// that makes <c>CreateProcessW</c> read it, and every flag it sets is paired
    /// with a field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>STARTUPINFO</c> field with no flag beside it is not a setting —
    /// it is dead memory that reads like one</b>, and there is no diagnostic
    /// anywhere: the struct takes the value, the call ignores it, and the source
    /// says what somebody meant rather than what happens. That is how
    /// <c>ShowWindow</c> sat in <c>JobLauncher</c>'s struct unread from the day
    /// it was written until 2026-08-23, with <c>CREATE_NO_WINDOW</c> beside it
    /// looking like the thing that covered it — which it is not, because
    /// <c>CREATE_NO_WINDOW</c> is about a console child and says nothing about a
    /// GUI child's first window.
    /// </para>
    /// <para>
    /// <b>Both directions, because each catches a different mistake.</b> A field
    /// without its flag is a setting that silently does nothing. A flag without
    /// its field is a promise to Windows that the struct carries a value, so the
    /// zero left there by <c>default</c> is used as if it had been chosen — and
    /// for <c>STARTF_USESHOWWINDOW</c> that zero is <c>SW_HIDE</c>.
    /// </para>
    /// <para>
    /// <b>The precedent is
    /// <c>ProcessLogTests.EveryTimedWaitForExitIsFollowedByABareOne</c></b>: a
    /// pairing no analyzer and no banned symbol can require, because each half is
    /// perfectly legal on its own.
    /// </para>
    /// <para>
    /// <b>What it cannot see, stated rather than glossed:</b> a struct built any
    /// way other than <c>= default(StartupInfo…)</c>. It finds the local from its
    /// declaration and reads the assignments through that name, so an object
    /// initialiser or a <c>new()</c> would be invisible — which is what the
    /// non-vacuity assertion at the end is for, and why it counts declarations
    /// rather than only offences.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryStartupInfoFieldIsPairedWithTheFlagThatMakesWindowsReadIt()
    {
        var offenders = new List<string>();
        var sites = 0;

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(file => file.Extension is ".cs"))
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            foreach (var site in StartupInfoSites(code))
            {
                sites++;
                offenders.AddRange(Unpaired(site).Select(complaint => $"{Relative(file)}: {complaint}"));
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, and its first case is the real defect: the
        // struct carrying ShowWindow with only STARTF_USESTDHANDLES in its flags,
        // which is what this tree shipped until 2026-08-23. Composed from pieces
        // so that this file does not match its own scan.
        var declared = $"            var startupInfo = default({Struct}Ex);";
        var showWindow = $"            startupInfo.{Struct}.ShowWindow = ShowNoActivate;";
        var handles = $"            startupInfo.{Struct}.StdInput = pipes.ChildStandardInput;";
        const string Constants = "    private const uint UseStd = 0x00000100;\n    private const uint UseShow = 0x00000001;\n    private const uint UseSize = 0x00000002;";

        var dead = $"{Constants}\n{declared}\n            startupInfo.{Struct}.Flags = UseStd;\n{handles}\n{showWindow}";

        await Assert.That(StartupInfoSites(dead).Count).IsEqualTo(1);
        await Assert.That(Unpaired(StartupInfoSites(dead)[0])).IsNotEmpty();

        // The same struct with the flag added is not reported, so the rule is
        // about the pairing and not about the field.
        var paired = $"{Constants}\n{declared}\n            startupInfo.{Struct}.Flags = UseStd | UseShow;\n{handles}\n{showWindow}";

        await Assert.That(Unpaired(StartupInfoSites(paired)[0])).IsEmpty();

        // ⚠️ AND THE CONVERSE, which is the half a one-directional version would
        // miss: a flag promising Windows a value the struct never carries, so
        // the zero from `default` is honoured as though somebody chose it.
        var promised = $"{Constants}\n{declared}\n            startupInfo.{Struct}.Flags = UseStd | UseSize;\n{handles}";

        await Assert.That(Unpaired(StartupInfoSites(promised)[0])).IsNotEmpty();

        // A flag this scan cannot resolve to a number is an offence rather than a
        // silent zero — an unreadable flags expression must not read as clean.
        var opaque = $"{Constants}\n{declared}\n            startupInfo.{Struct}.Flags = SomethingElse;\n{handles}";

        await Assert.That(Unpaired(StartupInfoSites(opaque)[0])).IsNotEmpty();

        // Not vacuous over the tree: the product's launcher and the breakaway
        // probe, as of 2026-08-23. The probe assigns no gated field and sets no
        // flags, which is the compliant shape rather than an exemption.
        await Assert.That(sites).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>How many lines of a fluent assertion carry its bound.</summary>
    private const int AssertionWindowLines = 4;

    /// <summary>
    /// Composed so this file does not match its own scan — every needle below is
    /// built at run time and appears nowhere in the source as one string.
    /// </summary>
    private const string Assertion = "Assert" + ".That(";

    private const string Upper = "IsLess" + "Than";

    private const string ClockKey = "elapsed" + "Milliseconds";

    private const string Raw = "Dangerous" + "GetHandle";

    private const string AddRef = "Dangerous" + "AddRef";

    private const string ReleaseRef = "Dangerous" + "Release";

    private const string NonOwning = "owns" + "Handle: false";

    private const string Storage = "Marshal" + ".Write";

    /// <summary>The words that mean a value was measured off a clock.</summary>
    private static readonly string[] ClockWords = ["Elap" + "sed", ClockKey, "Stop" + "watch"];

    /// <summary>
    /// Every upper bound an assertion in one file's lines puts on a measured
    /// duration.
    /// </summary>
    /// <remarks>
    /// A window rather than a line, because a fluent assertion wraps and its
    /// bound is routinely two lines below its subject. A window that mentions no
    /// clock is skipped entirely, which is what keeps every ordinary comparison
    /// in the suite out of this.
    /// </remarks>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <returns>The line index of each assertion, and the bound it used.</returns>
    private static List<(int Line, string Bound)> DurationBounds(string[] lines)
    {
        var found = new List<(int, string)>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains(Assertion, StringComparison.Ordinal))
            {
                continue;
            }

            var window = string.Join('\n', lines.Skip(index).Take(AssertionWindowLines));

            if (!ClockWords.Any(word => window.Contains(word, StringComparison.Ordinal)))
            {
                continue;
            }

            found.AddRange(FluentUpperBound().Matches(window)
                .Concat(ComparedUpperBound().Matches(window))
                .Select(match => (index, match.Groups["bound"].Value.Trim())));
        }

        return found;
    }

    /// <summary>Whether a bound is a number rather than the name of one.</summary>
    /// <param name="bound">The bound expression, as written.</param>
    /// <returns><see langword="true"/> if nothing named it.</returns>
    private static bool IsInvented(string bound) =>
        BareNumber().IsMatch(bound) || WrappedNumber().IsMatch(bound);

    /// <summary>
    /// Every place in one file's lines where a raw handle value is stored rather
    /// than consumed.
    /// </summary>
    /// <param name="lines">The file, comment-only lines already removed.</param>
    /// <returns>The escaping lines, trimmed.</returns>
    private static List<string> HandleEscapes(string[] lines) =>
    [
        .. lines
            .Where(line => line.Contains(Raw, StringComparison.Ordinal)
                && (line.Contains(Storage, StringComparison.Ordinal) || line.Contains(NonOwning, StringComparison.Ordinal)))
            .Select(line => line.Trim()),
    ];

    /// <summary>The struct's own name, composed so this file does not match its own scan.</summary>
    private const string Struct = "Startup" + "Info";

    /// <summary>How long any scan regex may run before it is a defect in the scan.</summary>
    private static readonly TimeSpan ScanPatience = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The <c>STARTUPINFO</c> fields Windows reads only when a companion flag
    /// says to, with the bit that says so.
    /// </summary>
    /// <remarks>
    /// <b>The bit, not the spelling.</b> The flags expression is resolved through
    /// the file's own <c>const</c> declarations down to a number, so renaming a
    /// constant cannot quietly satisfy this and writing <c>0x101</c> by hand
    /// cannot quietly dodge it.
    /// </remarks>
    private static readonly (uint Bit, string Flag, string[] Fields)[] GatedFields =
    [
        (0x00000001, "STARTF_USESHOWWINDOW", ["ShowWindow"]),
        (0x00000002, "STARTF_USESIZE", ["XSize", "YSize"]),
        (0x00000004, "STARTF_USEPOSITION", ["X", "Y"]),
        (0x00000008, "STARTF_USECOUNTCHARS", ["XCountChars", "YCountChars"]),
        (0x00000010, "STARTF_USEFILLATTRIBUTE", ["FillAttribute"]),
        (0x00000100, "STARTF_USESTDHANDLES", ["StdInput", "StdOutput", "StdError"]),
    ];

    /// <summary>Every <c>STARTUPINFO</c> a file builds, with what it assigned.</summary>
    /// <param name="code">One file's code.</param>
    /// <returns>The assigned field names and the flags expression, per struct.</returns>
    private static List<(HashSet<string> Fields, string? Flags, IReadOnlyDictionary<string, uint> Constants)> StartupInfoSites(string code)
    {
        var constants = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (var declaration in Regex.Matches(code, @"\bconst\s+u?(?:int|short|long)\s+(?<name>\w+)\s*=\s*(?<value>0[xX][0-9A-Fa-f]+|\d+)\s*;", RegexOptions.None, ScanPatience).Cast<Match>())
        {
            if (TryNumber(declaration.Groups["value"].Value, out var value))
            {
                constants[declaration.Groups["name"].Value] = value;
            }
        }

        var sites = new List<(HashSet<string>, string?, IReadOnlyDictionary<string, uint>)>();

        foreach (var declaration in Regex.Matches(code, @"\b(?<name>\w+)\s*=\s*default\(" + Struct + @"(?:Ex)?\)\s*;", RegexOptions.None, ScanPatience).Cast<Match>())
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            string? flags = null;

            var pattern = @"\b" + Regex.Escape(declaration.Groups["name"].Value) + @"(?:\." + Struct + @")?\.(?<field>\w+)\s*=\s*(?<value>[^;]+);";

            foreach (var assignment in Regex.Matches(code, pattern, RegexOptions.None, ScanPatience).Cast<Match>())
            {
                var field = assignment.Groups["field"].Value;

                if (field is "Flags")
                {
                    flags = assignment.Groups["value"].Value;
                }
                else
                {
                    _ = fields.Add(field);
                }
            }

            sites.Add((fields, flags, constants));
        }

        return sites;
    }

    /// <summary>Both halves of the pairing, for one struct.</summary>
    /// <param name="site">One <c>STARTUPINFO</c> and what was assigned to it.</param>
    /// <returns>One complaint per unpaired field or flag.</returns>
    private static List<string> Unpaired((HashSet<string> Fields, string? Flags, IReadOnlyDictionary<string, uint> Constants) site)
    {
        var complaints = new List<string>();
        var bits = 0u;

        foreach (var token in (site.Flags ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (site.Constants.TryGetValue(token, out var named))
            {
                bits |= named;
            }
            else if (TryNumber(token, out var literal))
            {
                bits |= literal;
            }
            else
            {
                complaints.Add(
                    $"the flags expression carries '{token}', which does not resolve to a number here"
                    + " — an unreadable flags expression must be an offence rather than a silent zero");
            }
        }

        foreach (var (bit, flag, gated) in GatedFields)
        {
            var assigned = gated.Where(site.Fields.Contains).ToList();
            var set = (bits & bit) is not 0;

            if (assigned.Count is not 0 && !set)
            {
                complaints.Add(
                    $"{string.Join(", ", assigned)} is assigned and {flag} is not set"
                    + " — CreateProcessW never reads that field, so the assignment is dead memory that reads like a setting");
            }

            if (set && assigned.Count is 0)
            {
                complaints.Add(
                    $"{flag} is set and none of {string.Join(", ", gated)} is assigned"
                    + " — the struct promises Windows a value it does not carry, so the zero from `default` is honoured as if it had been chosen");
            }
        }

        return complaints;
    }

    /// <summary>
    /// <b>The two literals the session guard is made of are the two literals it
    /// is written with</b> — <c>Hold</c>'s <c>FileShare.Read</c>, and the probe's
    /// <c>FileAccess.ReadWrite</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Six properties rest on two arguments, and neither of them looks load-
    /// bearing at the call site.</b> <c>FileShare.Read</c> on the hold is what
    /// makes one writer per directory, admits every reader, and lets the OS
    /// release the claim on death; <c>FileAccess.ReadWrite</c> on the probe is
    /// what makes the probe detectable by that share mode at all. Widen the
    /// first to <c>ReadWrite</c> and two BrowserAIs drive one profile. Narrow
    /// the second to <c>Read</c> and every probe answers <i>free</i> about a
    /// session somebody is driving — <b>and every test above still passes</b>,
    /// because both perturbations produce a file that opens.
    /// </para>
    /// <para>
    /// <b>Why a tree-as-text scan and not an assertion in
    /// <c>LockFileTests</c>.</b> The behavioural arms there drive the real
    /// kernel and are the primary check; what they cannot say is <i>this is the
    /// mechanism, do not negotiate it</i>. A banned symbol cannot either —
    /// <c>FileShare</c> is fine everywhere else in the tree, and what is
    /// forbidden is one value in one method. This is the same judgement
    /// <see cref="EveryProcessLaunchInTheTreeSuppressesTheConsoleWindow"/> makes
    /// about <c>CreateNoWindow</c>.
    /// </para>
    /// <para>
    /// <b>Q119, and it is the half a document cannot keep.</b> The six
    /// properties are written down in
    /// [ARCHITECTURE](../../ARCHITECTURE.md#locking-ownership-and-the-sweep);
    /// this is what makes the two that live in an argument list survive an edit
    /// nobody reviews.
    /// </para>
    /// <para>
    /// <b>What it cannot see:</b> a third open of the same file added elsewhere,
    /// and whether the values are <i>correct</i> — it holds that they are the
    /// ones this design chose, which is why the behavioural arms stay.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSessionGuardsTwoLoadBearingLiteralsAreTheOnesItIsWrittenWith()
    {
        var guards = RepositoryLayout.ProductSourceFiles
            .Where(file => string.Equals(file.Name, GuardFile, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Named rather than walked, so a rename or a move is a red build instead
        // of a scan that quietly reads nothing. That is the failure mode of
        // every rule keyed on a file name.
        await Assert.That(guards.Count).IsEqualTo(1);

        var code = await RepositoryLayout.ReadCodeAsync(guards[0]);

        await Assert.That(string.Join(Environment.NewLine, GuardOffences(code))).IsEmpty();

        // ⚠️ BOTH DIRECTIONS, off a synthetic pair rather than off a rewrite of
        // the real file: the two opens are formatted differently -- one line and
        // five -- so a perturbation spelled against today's layout would go
        // looking for a string that a reflow had moved, and report the reflow as
        // the defect. What the shape supports is a control that carries the same
        // declarations with the same arguments, which is what this is.
        const string Correct = """
            public static LockFileHold Hold(string path) =>
                new(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1), path);

            public static LockFileAnswer Probe(string path)
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            }
            """;

        await Assert.That(GuardOffences(Correct)).IsEmpty();

        // The two perturbations an edit would plausibly make, each with the
        // sentence that makes it sound reasonable: share writes "so a second
        // handle can be taken", and probe read-only "so the look disturbs
        // nothing". Both leave a file that opens, and both are caught here.
        var shared = Correct.Replace(
            "FileShare.Read, bufferSize: 1",
            "FileShare.ReadWrite, bufferSize: 1",
            StringComparison.Ordinal);

        await Assert.That(shared).IsNotEqualTo(Correct);
        await Assert.That(GuardOffences(shared).Count).IsEqualTo(1);

        var harmless = Correct.Replace(
            "FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete",
            "FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete",
            StringComparison.Ordinal);

        await Assert.That(harmless).IsNotEqualTo(Correct);
        await Assert.That(GuardOffences(harmless).Count).IsEqualTo(1);

        // And a file the scan cannot find the opens in is two offences rather
        // than a pass, which is the vacuity every rule of this shape is prone
        // to.
        await Assert.That(GuardOffences("internal static class LockFile { }").Count).IsEqualTo(2);
    }

    /// <summary>The file the whole of directory ownership is written in.</summary>
    private const string GuardFile = "LockFile.cs";

    /// <summary>
    /// The two opens, and what each one must ask for.
    /// </summary>
    /// <remarks>
    /// Keyed on the declaration rather than on a line number, and read out of
    /// the <c>new FileStream(</c> that follows it, so reformatting the argument
    /// list cannot satisfy this and moving the method cannot vacate it.
    /// </remarks>
    private static readonly (string Declaration, string Required, string Because)[] GuardOpens =
    [
        (
            "LockFileHold Hold(",
            "FileShare.Read",
            "the hold's share mode is the whole of one-writer-per-directory: it admits every reader and refuses every writer, and widening it lets a second BrowserAI drive one profile"),
        (
            "LockFileAnswer Probe(",
            "FileAccess.ReadWrite",
            "the probe must ask for access outside Read or a holder's FileShare.Read cannot refuse it, and a probe nothing refuses reports every driven session as free"),
    ];

    /// <summary>The whole per-file judgement, so the control can drive both directions through it.</summary>
    /// <param name="code">The guard's code, comments already stripped.</param>
    /// <returns>One complaint per open that does not ask for what it must.</returns>
    private static List<string> GuardOffences(string code)
    {
        var offences = new List<string>();

        foreach (var (declaration, required, because) in GuardOpens)
        {
            if (Arguments(code, declaration) is not { } arguments)
            {
                offences.Add($"{GuardFile}: no 'new FileStream(' follows '{declaration}' — {because}");
                continue;
            }

            if (!Names(arguments, required))
            {
                offences.Add($"{GuardFile}: the open in '{declaration}' does not ask for {required} — {because}");
            }
        }

        return offences;
    }

    /// <summary>Whether an argument list names an enumeration member, whole.</summary>
    /// <remarks>
    /// ⚠️ <b>A plain <c>Contains</c> is satisfied by the one edit this rule
    /// exists to catch</b>: <c>FileShare.ReadWrite</c> carries
    /// <c>FileShare.Read</c> inside it, so the widened share mode would read as
    /// the narrow one. The member has to end where it ends.
    /// </remarks>
    /// <param name="arguments">The argument list.</param>
    /// <param name="member">The <c>Type.Member</c> spelling required.</param>
    /// <returns>Whether it is named as a whole token.</returns>
    private static bool Names(string arguments, string member)
    {
        for (var at = arguments.IndexOf(member, StringComparison.Ordinal); at >= 0;
             at = arguments.IndexOf(member, at + 1, StringComparison.Ordinal))
        {
            var after = at + member.Length;

            if (after >= arguments.Length || !(char.IsLetterOrDigit(arguments[after]) || arguments[after] is '_'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The argument list of the first <c>new FileStream(</c> after a declaration.</summary>
    /// <param name="code">The file's code.</param>
    /// <param name="declaration">The declaration to start looking at.</param>
    /// <returns>The arguments, or <see langword="null"/> when there is no such open.</returns>
    private static string? Arguments(string code, string declaration)
    {
        var at = code.IndexOf(declaration, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        const string Opener = "new FileStream(";

        var opened = code.IndexOf(Opener, at, StringComparison.Ordinal);
        if (opened < 0)
        {
            return null;
        }

        var start = opened + Opener.Length;
        var depth = 1;

        for (var index = start; index < code.Length; index++)
        {
            depth += code[index] switch { '(' => 1, ')' => -1, _ => 0 };

            if (depth is 0)
            {
                return code[start..index];
            }
        }

        return null;
    }

    /// <summary>Parses a hexadecimal or decimal literal as written in source.</summary>
    /// <param name="token">The literal.</param>
    /// <param name="value">The value.</param>
    /// <returns>Whether it parsed.</returns>
    private static bool TryNumber(string token, out uint value) =>
        token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>The whole per-file judgement, so the control can drive both directions through it.</summary>
    /// <param name="code">One file's code.</param>
    /// <returns>The escaping lines if it offends, otherwise <see langword="null"/>.</returns>
    private static string? Offends(string code)
    {
        var escapes = HandleEscapes(code.Split('\n'));

        if (escapes.Count is 0)
        {
            return null;
        }

        return code.Contains(AddRef, StringComparison.Ordinal) && code.Contains(ReleaseRef, StringComparison.Ordinal)
            ? null
            : string.Join(" · ", escapes);
    }

    [GeneratedRegex(@"(?:IsLess" + @"ThanOrEqualTo|IsLess" + @"Than)\(\s*(?<bound>[^()]*(?:\([^()]*\)[^()]*)*?)\s*\)")]
    private static partial Regex FluentUpperBound();

    [GeneratedRegex(@"<=?\s*(?<bound>[^)\n]+)")]
    private static partial Regex ComparedUpperBound();

    [GeneratedRegex(@"^[-+]?[0-9][0-9_]*(?:\.[0-9]+)?[dDfFmM]?$")]
    private static partial Regex BareNumber();

    [GeneratedRegex(@"^TimeSpan\.From[A-Za-z]+\(\s*[-+]?[0-9][0-9_]*(?:\.[0-9]+)?[dDfFmM]?\s*\)$")]
    private static partial Regex WrappedNumber();

    /// <summary>
    /// Every file this repository hand-writes in a form that can carry a comment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code, the scripts, the prose and the build files. <b>What is outside
    /// it is outside for a reason, and the reasons are not interchangeable:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>JSON has no comment syntax at all</b> — thirteen tracked files, four of
    /// them lock files nobody writes by hand. <c>drift-check.json</c>,
    /// <c>upstream-review.json</c> and <c>tool-verdicts.json</c> carry a
    /// <c>_license</c> key instead, which is the closest the format allows.
    /// <i>Corrected 2026-08-26 (previously "twelve"), when
    /// <c>tool-verdicts.json</c> arrived.</i>
    /// </item>
    /// <item>
    /// <b><c>upstream-snapshots/</c> is foreign bytes</b>, exempted from
    /// normalisation by <c>.gitattributes</c> and compared byte-for-byte against
    /// what the build regenerates. A header would make the gate permanently red
    /// on a difference that is not one.
    /// </item>
    /// <item>
    /// <b><c>LICENSE</c> is the licence</b>, and <c>THIRD-PARTY-NOTICES.txt</c>
    /// carries other people's terms. Stamping this project's identifier on
    /// either would be a claim about somebody else's text.
    /// </item>
    /// </list>
    /// <para>
    /// <c>.gitignore</c>, <c>.gitattributes</c> and <c>BrowserAI.slnx</c> could
    /// carry one and do not. That is a gap rather than a decision, and it is left
    /// open here rather than closed quietly, because widening the scope is a
    /// change to what the rule means and belongs to whoever owns the rule.
    /// </para>
    /// </remarks>
    /// <returns>The files, deduplicated, in path order.</returns>
    private static IReadOnlyList<FileInfo> Scope() =>
    [
        .. RepositoryLayout.LinkBearingFiles
            .Concat(RepositoryLayout.BuildFiles)
            .DistinctBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Where(file => !Relative(file).StartsWith("upstream-snapshots", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>The two update calls that reach the network.</summary>
    private static readonly string[] UpdateCalls = ["CheckAsync(", "DownloadAsync("];

    /// <summary>
    /// No update call in this tree is made with an unbounded cancellation token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>CancellationToken.None</c> on one of these is a call with no
    /// bound at all.</b> <c>VelopackUpdateClient</c> builds its
    /// <c>UpdateManager</c> from a bare URL, so Velopack's own
    /// <c>SimpleWebSource</c> supplies the default <c>HttpClient</c> timeout —
    /// <b>thirty minutes</b>. The server's lane wrapped the identical calls in
    /// <c>UpdateService.CrashTripwire</c> from the day it was written; the
    /// configuration app called them on the UI thread with
    /// <c>.GetAwaiter().GetResult()</c> and <c>CancellationToken.None</c>, which
    /// is a frozen window — no repaint, no cursor, no close button — for as long
    /// as the feed takes to answer. <i>Added 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>A tree-as-text scan, because the defect is an ARGUMENT again.</b> No
    /// analyzer can say <i>this parameter may not be this particular static
    /// property</i>, and no test can hold a slow feed still. What is assertable
    /// is that every caller names a token of its own.
    /// </para>
    /// <para>
    /// <b>What it cannot see:</b> whether the token is bounded by anything
    /// sensible. That half is
    /// <see cref="BrowserAI.Tests.ConfigurationAppTests.WorkTheDialogWaitsForIsBoundedByTheServersOwnDeadline"/>,
    /// which holds the budget against the server's constant.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoUpdateCallIsMadeWithAnUnboundedToken()
    {
        var offenders = new List<string>();
        var sites = 0;

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(
            file => file.Extension is ".cs" && Relative(file).StartsWith("src", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            foreach (var (number, call) in UnboundedUpdateCalls(lines))
            {
                sites++;
                offenders.Add(
                    $"{Relative(file)}:{number.ToString(CultureInfo.InvariantCulture)}: {call}"
                    + " — an update call with no token of its own is bounded only by Velopack's own thirty-minute HttpClient default");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        await Assert.That(sites).IsEqualTo(0);

        // ⚠️ THE POSITIVE CONTROL, synthetic, in both directions — the exact
        // shape the configuration app carried until 2026-09-16, and the shape
        // that replaced it.
        string[] unbounded =
        [
            "            var candidate = client.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();",
            "            client.DownloadAsync(candidate, _ => { }, CancellationToken.None).GetAwaiter().GetResult();",
        ];

        await Assert.That(UnboundedUpdateCalls(unbounded).Count).IsEqualTo(2);

        string[] bounded =
        [
            "            var candidate = client.CheckAsync(token).GetAwaiter().GetResult();",
            "            client.DownloadAsync(candidate, _ => { }, token).GetAwaiter().GetResult();",
        ];

        await Assert.That(UnboundedUpdateCalls(bounded).Count).IsEqualTo(0);
    }

    /// <summary>Update calls handed <c>CancellationToken.None</c>.</summary>
    /// <param name="lines">The file, as lines.</param>
    /// <returns>The one-based line number and the line, trimmed.</returns>
    private static List<(int Number, string Call)> UnboundedUpdateCalls(string[] lines)
    {
        var found = new List<(int, string)>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains("CancellationToken.None", StringComparison.Ordinal))
            {
                continue;
            }

            if (Array.Exists(UpdateCalls, call => lines[index].Contains(call, StringComparison.Ordinal)))
            {
                found.Add((index + 1, lines[index].Trim()));
            }
        }

        return found;
    }

    /// <summary>
    /// The call this scan is about — through the type name, so the picker's own
    /// declaration is not read as a call site that forgot its owner.
    /// </summary>
    private const string PickerCall = "ShellInterop.PickFolder(";

    /// <summary>
    /// Every folder picker this tree opens is owned by the dialog's own window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A modal dialog with a zero owner is modal to nothing.</b>
    /// <c>SHBrowseForFolderW</c> disables its owner while it is up; given zero
    /// it disables nothing, so the task dialog underneath stays live. Its command
    /// links can then be clicked while the picker is on top — which re-enters
    /// <c>OnCommand</c>, and a <c>TDM_NAVIGATE_PAGE</c> issued from there
    /// rebuilds the page out from under a modal child. It also means the picker
    /// can end up behind the dialog, where a person cannot find it and the
    /// application looks hung.
    /// </para>
    /// <para>
    /// <b>A tree-as-text scan, because the defect is an ARGUMENT.</b> Nothing an
    /// analyzer can express says <i>this parameter may not be a literal zero</i>,
    /// and no test here can open a modal window to observe the consequence. What
    /// is assertable is that the owner passed is the dialog's window, which is
    /// what <c>TaskDialogHost.Window</c> exists for. <i>Added 2026-09-16, after
    /// the call site was found passing <c>0</c> because that member was
    /// private.</i>
    /// </para>
    /// <para>
    /// <b>What it cannot see:</b> whether the window it names is the right one,
    /// or whether it is zero at the moment of the call. It holds that the owner
    /// is read from a dialog rather than written as a constant, which is the
    /// assertable half.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFolderPickerIsOwnedByTheDialogThatOpenedIt()
    {
        var offenders = new List<string>();
        var sites = 0;

        // ⚠️ src/ ONLY, like the raw-handle scan: the rule is about the product,
        // and this file's own needle and its synthetic controls are literal call
        // sites that would otherwise report themselves.
        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(
            file => file.Extension is ".cs" && Relative(file).StartsWith("src", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            foreach (var (number, owner) in PickerOwners(lines))
            {
                sites++;

                if (!owner.Contains("Window", StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{Relative(file)}:{number.ToString(CultureInfo.InvariantCulture)}: the folder picker is opened with owner '{owner}',"
                        + " which is not a dialog window — an unowned modal leaves the task dialog's command links live underneath it");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous: there is a picker in this tree and it was read.
        await Assert.That(sites).IsGreaterThan(0);

        // ⚠️ THE CONTROLS, synthetic, in both directions. A scan whose needle
        // stopped matching reports the tree clean and is indistinguishable from
        // a tree that is clean, so the exact shape the call site carried until
        // 2026-09-16 is planted here and must be reported.
        string[] unowned = ["        var picked = " + PickerCall + "0, \"Choose a folder.\");"];

        await Assert.That(PickerOwners(unowned).Count).IsEqualTo(1);
        await Assert.That(PickerOwners(unowned)[0].Owner).IsEqualTo("0");

        string[] owned = ["        var picked = " + PickerCall + "_host?.Window ?? 0, \"Choose a folder.\");"];

        await Assert.That(PickerOwners(owned).Count).IsEqualTo(1);
        await Assert.That(PickerOwners(owned)[0].Owner.Contains("Window", StringComparison.Ordinal)).IsTrue();

        // And the WRAPPED shape of each, which is what the real call site is.
        string[] wrappedUnowned = ["        var picked = " + PickerCall, "            0,", "            \"Choose a folder.\");"];

        await Assert.That(PickerOwners(wrappedUnowned).Count).IsEqualTo(1);
        await Assert.That(PickerOwners(wrappedUnowned)[0].Owner).IsEqualTo("0");

        string[] wrappedOwned = ["        var picked = " + PickerCall, "            _host?.Window ?? 0,", "            \"Choose a folder.\");"];

        await Assert.That(PickerOwners(wrappedOwned).Count).IsEqualTo(1);
        await Assert.That(PickerOwners(wrappedOwned)[0].Owner.Contains("Window", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>How many lines of a wrapped call are read looking for its first argument.</summary>
    private const int PickerArgumentLines = 4;

    /// <summary>Every folder-picker call in some lines, and the owner each passes.</summary>
    /// <remarks>
    /// <b>The call is read across lines</b>, because the real one wraps: a
    /// scanner that read only the line carrying the call would see an empty
    /// first argument and report a perfectly owned call as unowned. That is not
    /// hypothetical — it is what this answered the moment the fix made the call
    /// three lines long.
    /// </remarks>
    /// <param name="lines">The file, as lines.</param>
    /// <returns>The one-based line number and the first argument, verbatim.</returns>
    private static List<(int Number, string Owner)> PickerOwners(string[] lines)
    {
        var found = new List<(int, string)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var at = lines[index].IndexOf(PickerCall, StringComparison.Ordinal);

            if (at < 0)
            {
                continue;
            }

            var rest = new StringBuilder(lines[index][(at + PickerCall.Length)..]);

            for (var next = index + 1;
                 next < lines.Length
                     && next <= index + PickerArgumentLines
                     && rest.ToString().IndexOf(',', StringComparison.Ordinal) < 0;
                 next++)
            {
                _ = rest.Append(' ').Append(lines[next]);
            }

            var argument = rest.ToString();
            var comma = argument.IndexOf(',', StringComparison.Ordinal);

            found.Add((index + 1, (comma < 0 ? argument : argument[..comma]).Trim()));
        }

        return found;
    }

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName);

    /// <summary>
    /// <b>No text file in the tree carries a C0 control byte</b> — nothing below
    /// <c>0x20</c> but tab, line feed and carriage return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-16, after three of them were found in one byte
    /// search.</b> Two were a regex word boundary — <c>backslash-b</c>, typed
    /// into a file through something that expanded the escape and wrote the
    /// character it means: <c>0x08</c>, backspace. In
    /// <see cref="BrowserIdleTimerTests.TheShippedClockIsTheRealOneAndNothingInTheProductReplacesIt"/>
    /// that turned a scan over every product source file into one that <b>can
    /// never match anything</b> — the pattern asked for a literal backspace
    /// before <c>Clock</c>, so the arm asserting that no shipped file assigns the
    /// clock seam was passing over a result set nothing could ever enter. The
    /// third was a path in <c>HAZARDS.md</c> prose, where the browsers root
    /// rendered as <c>BrowserAI</c> followed by <c>rowsers</c>.
    /// </para>
    /// <para>
    /// <b>The failure mode is why this is a scan rather than a habit: the byte is
    /// invisible.</b> It renders as nothing in every editor, in every diff and in
    /// every review on GitHub. A reader sees the escape they meant to type where
    /// the file holds one character that is not it, which is not a mistake
    /// anybody catches by looking — and a regex that cannot match is a green test
    /// either way.
    /// </para>
    /// <para>
    /// <b>What counts as text.</b> A file of one of
    /// <see cref="RepositoryLayout.IsLinkBearing"/>'s kinds is always read, NUL
    /// included in what is forbidden — those are the kinds this repository writes
    /// its prose and its code in, and a NUL in one of them is the same defect
    /// wearing a different byte. Anything else is binary if it holds a NUL in its
    /// first 8,000 bytes, which is git's own heuristic, and is skipped: today
    /// that is exactly one file, <c>assets\BrowserAI.ico</c>, and both counts are
    /// asserted so that a corpus quietly re-classifying itself as binary cannot
    /// empty this scan.
    /// </para>
    /// <para>
    /// <b>Planted red before it was trusted, twice:</b> against the three real
    /// offenders, which it named at <c>HAZARDS.md</c> line 145,
    /// <c>BrowserIdleTimerTests.cs</c> line 978 and <c>SessionToolTests.cs</c>
    /// line 95; and against the synthetic control below, which is written to disk
    /// rather than passed as a string so that the reading half is exercised too.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoTextFileInTheTreeCarriesAControlByte()
    {
        // The positive control first, so an emptiness that comes from a scan
        // reading nothing is caught before the emptiness is believed. Both
        // directions: the file with the byte is named, the file without it is
        // not, and the message says which byte and where.
        //
        // The escapes are C#'s, so THIS file stays clean: the compiler makes the
        // byte at run time and the scan below reads this file too.
        using var scratch = ScratchDirectory.Create("control-bytes");

        var planted = Path.Combine(scratch.Path, "planted.md");
        var clean = Path.Combine(scratch.Path, "clean.md");

        await File.WriteAllTextAsync(planted, "A line.\nA \bword boundary that is not one.\n");
        await File.WriteAllTextAsync(clean, "A line.\nA \\bword boundary that is one.\n\tTabs and\r\nCRLF are not controls.\n");

        var caught = ControlByteOffences(new FileInfo(planted));

        await Assert.That(caught.Count).IsEqualTo(1);
        await Assert.That(caught[0]).Contains("0x08");
        await Assert.That(caught[0]).Contains("line 2");
        await Assert.That(ControlByteOffences(new FileInfo(clean))).IsEmpty();

        var scanned = 0;
        var skipped = 0;
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.AllFiles)
        {
            if (IsBinary(file))
            {
                skipped++;
                continue;
            }

            scanned++;
            offenders.AddRange(ControlByteOffences(file));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous, in both halves. A prune that reached too far, or a
        // classifier that called everything binary, would satisfy the emptiness
        // above perfectly.
        await Assert.That(scanned).IsGreaterThan(250);
        await Assert.That(skipped).IsLessThan(20);
    }

    /// <summary>
    /// Whether a file is binary, and so is not this rule's to read.
    /// </summary>
    /// <remarks>
    /// <b>Git's own heuristic — a NUL in the first 8,000 bytes — and only for the
    /// kinds this repository does not write prose in.</b> Deciding it by
    /// extension instead would need a list that goes stale the day a new kind of
    /// asset is committed, and a stale list here fails in the direction that
    /// costs most: a false red on a file nobody typed.
    /// </remarks>
    /// <param name="file">The file.</param>
    /// <returns>Whether to skip it.</returns>
    private static bool IsBinary(FileInfo file)
    {
        if (RepositoryLayout.IsLinkBearing(file.Name))
        {
            return false;
        }

        var head = new byte[8000];
        using var stream = file.OpenRead();
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return head.AsSpan(0, read).IndexOf((byte)0) >= 0;
    }

    /// <summary>
    /// Every C0 control byte in a file, with where it is and what it probably
    /// was.
    /// </summary>
    /// <remarks>
    /// <b>The message names the escape, because that is the fix.</b> A reader
    /// told only "0x08 at line 978" has to work out what a backspace is doing in
    /// a regex; one who is told it is what a word-boundary escape becomes when
    /// something expands it has the answer already.
    /// </remarks>
    /// <param name="file">The file to read.</param>
    /// <returns>One line per offending byte.</returns>
    private static List<string> ControlByteOffences(FileInfo file)
    {
        var bytes = File.ReadAllBytes(file.FullName);
        var offences = new List<string>();
        var line = 1;
        var column = 1;

        foreach (var value in bytes)
        {
            if (value is 0x0A)
            {
                line++;
                column = 1;
                continue;
            }

            if (value is >= 0x20 or 0x09 or 0x0D)
            {
                column++;
                continue;
            }

            offences.Add(
                $"{Relative(file)}: byte 0x{value:X2} at line {line}, column {column}"
                + (value is 0x08
                    ? " — a backspace, which is what a word-boundary escape becomes when something expands it before the file is written. Whatever carries it does not mean what it reads as."
                    : " — a C0 control byte. Nothing in this repository's text is written with one."));

            column++;
        }

        return offences;
    }

    /// <summary>
    /// <b>What the suite scans is exactly what git says this repository holds</b>
    /// — no file the walk invented, and none it lost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the rule every other tree-as-text rule rests on, and it was
    /// a sentence in a remark until 2026-08-24.</b>
    /// <see cref="RepositoryLayout.LinkBearingFiles"/> claimed in its own doc
    /// comment to yield "the same 215 files as <c>git ls-files</c>", verified
    /// once by hand on 2026-08-17. It was <b>false by 520 files</b> while
    /// nothing noticed: the walk prunes <c>.git</c>, <c>.vs</c>, <c>.work</c>,
    /// <c>payload</c>, <c>bin</c>, <c>obj</c> and <c>node_modules</c> and does
    /// <b>not</b> prune <c>.claude</c>, so when agent worktrees appeared under
    /// <c>.claude\worktrees\</c> every scan built on that list read a second
    /// checkout as repository content — the fragment scan counted <b>2,378</b>
    /// against a real <b>797</b>, and three gate arms went red for a reason no
    /// message named.
    /// </para>
    /// <para>
    /// <b>The fix was to assert the invariant, not to change the corpus.</b>
    /// Pruning <c>.claude</c> was considered and rejected at the maintainer's
    /// decision: <c>.claude\settings.json</c> and <c>.claude\hooks\</c> are
    /// committed, so a prune would silently drop them out of the SPDX check and
    /// the link check to fix a cause that retired with the worktrees. This arm
    /// costs nothing, catches the next stray directory whatever it is called,
    /// and takes no exception list of its own.
    /// </para>
    /// <para>
    /// <b>Both directions, and the two are different defects.</b> A path the
    /// walk has and git does not is ignored-but-present — a worktree, a cache, a
    /// build output nobody added to the prune list — and it <i>inflates</i>
    /// every scan. A path git has and the walk does not is a prune that reaches
    /// too far, which is how <c>src\BrowserAI\Artifacts\</c> lost five product
    /// source files to the root's <c>artifacts\</c> rule on case-insensitive
    /// Windows: it <i>hides</i> files, silently, from the SPDX header check and
    /// the link check alike.
    /// </para>
    /// <para>
    /// <b>Skips loudly without git, and that is the whole answer to the export
    /// objection.</b> The suite must run where there is no repository; git here
    /// is an oracle rather than a source of truth, so its absence is
    /// <see cref="SuiteCapability.Git"/> reading ABSENT in the coverage block and
    /// this arm reporting <i>skipped</i> — never <i>passed</i>. A release run
    /// fails instead, because a release whose corpus was never checked has every
    /// tree scan resting on an unverified list.
    /// </para>
    /// <para>
    /// <b>Planted red on 2026-08-24 before it was trusted</b>, with an ignored
    /// directory under <c>.claude\</c> holding one Markdown file: the walk found
    /// it, git did not, and this arm named it. Removing the plant returned the
    /// run to green.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScannedCorpusIsExactlyWhatGitSaysTheRepositoryHolds()
    {
        SuiteEnvironment.RequireGit();

        // Through RepositoryLayout.IsLinkBearing on both sides, so the two lists
        // cannot be filtered by two different opinions about what a scanned file
        // is -- and so a new extension joining the walk joins the oracle in the
        // same edit.
        var oracle = (await GitOracle.RepositoryFilesAsync())
            .Where(RepositoryLayout.IsLinkBearing)
            .Select(path => Path.GetFullPath(Path.Combine(RepositoryLayout.Root.FullName, path)))
            // A path in the index whose file is not on disk -- staged, then
            // deleted -- is not something a walk of the disk could ever yield,
            // and it is a different question from this one.
            .Where(File.Exists)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var walked = RepositoryLayout.LinkBearingFiles
            .Select(file => file.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // OrdinalIgnoreCase on both sides. Git records the spelling that was
        // committed and the walk reads the spelling on disk, and on a
        // case-insensitive filesystem a disagreement between those two is a
        // real but entirely different defect -- reporting it here would say
        // "this file is missing AND extra", which is the least useful true
        // sentence available.
        var invented = walked.Except(oracle, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, path)}: the walk scans it and git does not consider it part of the repository, so every tree-as-text rule is being applied to a file that is not ours")
            .ToList();

        var lost = oracle.Except(walked, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, path)}: git considers it part of the repository and the walk prunes it away, so every tree-as-text rule is silently blind to it")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, invented.Concat(lost))).IsEmpty();

        // Not vacuous: two empty sets agree perfectly, and a git that answered
        // nothing at all would satisfy the comparison above exactly as a healthy
        // one does. This is the same shape of hole the record count in
        // SaturationTests had, asserted here before it can open.
        await Assert.That(walked.Count).IsGreaterThan(200);
        await Assert.That(oracle.Count).IsEqualTo(walked.Count);
    }

    /// <summary>
    /// <b>Nothing in this tree asks a volume how much room it has.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's decision, 2026-09-17, in his words:</b> <i>"Remove
    /// the free space check. Checking for free space is out of scope and makes
    /// our project more complicated. I do not want to check for that at all."</i>
    /// <c>SessionManager.RequiredFreeBytes</c>, the <c>init</c> refusal it drove
    /// and <c>SessionErrors.InsufficientDisk</c> went with it, and so did the
    /// <c>FreeBytesOn</c> seam that made the refusal reachable from a test.
    /// </para>
    /// <para>
    /// <b>A scan rather than a behaviour arm, because the behaviour is now an
    /// absence and an absence has no seam.</b> A volume with no room is not
    /// something a test can arrange — that is why the refusal was injected
    /// through <c>SessionEnvironment.FreeBytesOn</c> in the first place — so once
    /// the product stops asking, there is no condition left to provoke. What can
    /// be held is that nobody asks the question again: the check was cheap to
    /// write, reads as prudence, and is exactly the shape that comes back.
    /// </para>
    /// <para>
    /// <b>The whole tree, not just <c>src\</c>.</b> A guard re-introduced in the
    /// suite's own harness would be the same decision taken somewhere a product
    /// scan cannot see, and the rule is that the question is out of scope rather
    /// than that one project must not ask it.
    /// <c>DriveInfo.GetDrives</c> is deliberately NOT a needle — enumerating
    /// mounted volumes is how <c>SessionIndexTests</c> finds a letter nothing is
    /// mounted on, and it says nothing about free space.
    /// </para>
    /// <para>
    /// <b>Watched red 2026-09-17 against the tree as it stood</b>, where it named
    /// <c>src\BrowserAI\Sessions\SessionEnvironment.cs</c> and
    /// <c>tests\BrowserAI.Tests\Harness\RigSessionEnvironment.cs</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingAsksAVolumeHowMuchRoomItHas()
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(FreeSpaceNeedles(code)
                .Select(needle => $"{Relative(file)}: asks a volume for its free space ({needle}), "
                    + "which is out of scope by the maintainer's decision of 2026-09-17 — see DECISIONS.md"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL. A scan whose needles stopped matching reports
        // the tree clean, and that reads identically to the tree BEING clean.
        // Each of the three is a real spelling the deleted code used or could
        // have used: the managed property, its whole-volume sibling, and the
        // Win32 entry point underneath both.
        await Assert.That(FreeSpaceNeedles("var free = new DriveInfo(root).Available" + "FreeSpace;")).IsNotEmpty();
        await Assert.That(FreeSpaceNeedles("var whole = drive.Total" + "FreeSpace;")).IsNotEmpty();
        await Assert.That(FreeSpaceNeedles("[LibraryImport] static partial bool GetDiskFree" + "SpaceExW(...);")).IsNotEmpty();

        // And the other direction, so the rule is about free space rather than
        // about the type: enumerating the mounted volumes stays legal, and one
        // real caller does it.
        await Assert.That(FreeSpaceNeedles("var mounted = DriveInfo.GetDrives();")).IsEmpty();
        await Assert.That(RepositoryLayout.SourceAndScriptFiles.Count).IsGreaterThan(100);
    }

    /// <summary>
    /// Every way this tree could ask a volume how much room it has.
    /// </summary>
    /// <remarks>
    /// Composed from halves so that this file does not match its own scan, which
    /// is the same arrangement <see cref="Flag"/> uses one rule above.
    /// </remarks>
    private static readonly string[] FreeSpaceSpellings =
    [
        "Available" + "FreeSpace",
        "Total" + "FreeSpace",
        "GetDiskFree" + "Space",
        "Required" + "FreeBytes",
        "Free" + "BytesOn",
    ];

    /// <param name="code">A file's text, comment-only lines already blanked.</param>
    /// <returns>The free-space spellings it carries, in the order they are declared.</returns>
    private static List<string> FreeSpaceNeedles(string code) =>
        [.. FreeSpaceSpellings.Where(needle => code.Contains(needle, StringComparison.Ordinal))];
}

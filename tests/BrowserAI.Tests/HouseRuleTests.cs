// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests;

/// <summary>
/// Three rules from <c>CLAUDE.md</c> that were held by habit alone — two until
/// 2026-08-17 and the third until 2026-08-23.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Prefer a mechanism over a habit. If a rule can be a failing test, a hook
/// or an analyzer, make it one."</b> All three rules below could be, and none
/// was. The SPDX rule was kept by 222 files out of 224 — which is what a habit
/// looks like right up to the moment somebody adds the 225th.
/// </para>
/// <para>
/// <b>The third arrived the same way and was found the same way: by the
/// maintainer noticing it while doing something else, twice.</b> Console windows
/// flashing over whatever is on screen, six suite runs to a release batch, from
/// two launch sites out of ten that had not set <c>CreateNoWindow</c>.
/// </para>
/// </remarks>
internal sealed class HouseRuleTests
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
    /// Every file this repository hand-writes in a form that can carry a comment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code, the scripts, the prose and the build files. <b>What is outside
    /// it is outside for a reason, and the reasons are not interchangeable:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>JSON has no comment syntax at all</b> — twelve tracked files, four of
    /// them lock files nobody writes by hand. <c>drift-check.json</c> carries a
    /// <c>_license</c> key instead, which is the closest the format allows.
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

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName);
}

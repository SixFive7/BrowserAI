// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The half of <i>never by image name</i> that an analyzer cannot see, and the
/// wiring that makes the analyzer cover every project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structural, not a review item.</b> A review already passed on code that
/// violated this rule: the Chromium probes this project grew out of counted and
/// killed by image name, which was harmless for Chromium on that machine and
/// would have killed roughly forty personal <c>firefox.exe</c> processes if
/// adapted naively.
/// </para>
/// <para>
/// <b>Two mechanisms, because neither covers the other.</b>
/// <c>Microsoft.CodeAnalysis.BannedApiAnalyzers</c> catches the
/// enumerate-by-name call at the call site, in every project, at error
/// severity. It is blind to a string: a kill-by-image-name command line
/// in a PowerShell script, a WMI query filtered on <c>Name</c>, a toolhelp walk
/// that compares <c>szExeFile</c>. That is what the scan below reads the tree
/// for.
/// </para>
/// <para>
/// <b>What is deliberately not banned:</b> reading a process's name. The rule
/// forbids matching, counting and terminating by name, not observing one --
/// <c>SdkStdioClientTransportTests</c> reads a parent's image name to prove
/// that the SDK's own transport interposes a shell, which is a defect being
/// exposed and not a process being chosen.
/// </para>
/// </remarks>
internal sealed class NeverByImageNameTests
{
    /// <summary>
    /// Kept as the record of what this scan used to be, and it is not read by
    /// anything: the five substrings the scan matched until 2026-09-17.
    /// </summary>
    /// <remarks>
    /// <b>Deleted, not retired</b> -- the shapes moved into
    /// <see cref="ProcessSelection"/>, which reads them as filters and not as
    /// APIs, and each carries its own reason there. The one that needed carrying
    /// over in full is <c>szExeFile</c>: it was <i>claimed</i> by
    /// <c>build/BannedSymbols.txt</c> and absent for a day in 2026-08, and a
    /// false claim of coverage is worse than none because it stops anyone
    /// looking. The needle is the FIELD and not the walk, because
    /// <c>CreateToolhelp32Snapshot</c> is how a pid and a parent are read without
    /// touching a name at all -- <c>JobProbe</c> declares that member as
    /// <c>ImageNameWeDoNotRead</c> for exactly that reason -- so banning the walk
    /// would have made this repository's one deliberately name-blind toolhelp use
    /// into the exclusion this file refuses to create.
    /// </remarks>
    [Test]
    public async Task NoSourceOrScriptFileNamesAProcessToActOnIt()
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles)
        {
            var text = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(
                ProcessSelection.OffencesIn(text)
                    .Select(offence => $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}: '{offence.Spelling}' -- {offence.Why}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    /// <summary>
    /// <b>The scan reads the FILTER and not the API: every name form is caught
    /// and every pid form passes.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this arm exists at all.</b> Until 2026-09-17 the scan asked
    /// whether a file contained one of five substrings, which cannot tell
    /// <c>-Id $pid</c> from a bare image name -- opposite things sharing a cmdlet.
    /// The cost was not theoretical: of the rigs under
    /// [`docs/probes`](../../docs/probes/README.md), <b>15 files in 7 of the 14
    /// directories</b> tripped the old scan and <b>fourteen of those fifteen were
    /// false positives</b> -- pid-keyed throughout -- so a measurement's own rig
    /// could not live anywhere the scan reads. <b>Q203</b>, decided 2026-09-17.
    /// ⚠️ <b>The fifteenth is real and still keeps that directory out of
    /// <c>build/</c>:</b> <c>2026-09-14-firstrun/observe.ps1</c> calls
    /// <c>GetProcessesByName</c> over a literal watch list, which is matching and
    /// counting by name and not the observing this rule permits.
    /// </para>
    /// <para>
    /// ⚠️ <b>A narrowing needs both directions or it is a hole with a test in
    /// front of it.</b> Every violation shape below must be caught <i>and</i> the
    /// pid-keyed spelling of the same call must pass, so an over-eager rewrite of
    /// the predicate reddens here instead of quietly permitting a kill by name.
    /// The third block is the mixed case -- a pid filter on a line that also names
    /// an image -- which must be a violation, because the name is what decides and
    /// the pid is decoration.
    /// </para>
    /// <para>
    /// <b>Every control is composed at run time</b>, exactly as
    /// <see cref="ProcessSelection"/> does and for the same reason: this file is inside
    /// the corpus the scan above reads, so a literal control would make the test
    /// its own first offender.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScanReadsTheFilterRatherThanTheApi()
    {
        var get = "Get-" + "Process";
        var stop = "Stop-" + "Process";
        var kill = "task" + "kill";
        var wmi = "Win32_" + "Process";
        var byName = "GetProcessesBy" + "Name";

        // The column, composed for the same reason as everything above it:
        // a literal `Name=` or `$_.Name -eq` in a control would be read by
        // the scan this file is inside, and this file does process work
        // (EveryProjectIsCoveredByTheBannedApiAnalyzer names the banned
        // API, which is enough to make it one).
        var nm = "Na" + "me";
        var field = "szExe" + "File";

        // ---- Every shape that MUST be caught ---------------------------------
        string[] violations =
        [
            $"{get} -Name chrome",
            $"{get} -ProcessName chrome",
            $"{get} chrome",
            $"{get} 'chrome'",
            $"{get} $imageName",
            $"{stop} -Name chrome -Force",
            $"{kill} /IM chrome.exe /F",
            $"& {kill} /F /IM $image",
            $"var live = Process.{byName}(\"chrome\");",
            $"Get-CimInstance {wmi} -Filter \"{nm}='chrome.exe'\"",
            $"Get-CimInstance {wmi} -Filter \"{nm} LIKE 'chrome%'\"",
            $"Get-CimInstance {wmi} -Query \"SELECT * FROM {wmi} WHERE {nm} = 'chrome.exe'\"",
            $"$all | Where-Object {{ $_.{nm} -eq 'chrome.exe' }}   # in a file that runs {get}",
            $"if (entry.{field}[0] == 'c') {{ }}",

            // The unreadable taskkill: neither switch is spelled, so what it
            // selects cannot be read, and an unreadable case falls to refused.
            $"{kill} $arguments",
        ];

        foreach (var violation in violations)
        {
            await Assert.That(ProcessSelection.OffencesIn(violation).Count)
                .IsGreaterThan(0)
                .Because($"'{violation}' selects a process by its image name and the scan must say so");
        }

        // ---- Every pid form that MUST pass ------------------------------------
        string[] permitted =
        [
            $"{get} -Id $pid",
            $"{get} -Id $pid -ErrorAction SilentlyContinue",
            $"$pwshPath = ({get} -Id $PID).Path",
            $"{stop} -Id $pid -Force",
            $"{kill} /F /PID $pid",
            $"Get-CimInstance {wmi} -Filter \"ProcessId = $pid\"",
            $"Get-CimInstance {wmi} -Filter \"ParentProcessId = $pid\"",
            $"Get-CimInstance {wmi} -Filter \"ParentProcessId={{pid}}\"",

            // No filter at all: it enumerates everything and whatever it narrows
            // on afterwards is read by the rules in their own right.
            $"$all = Get-CimInstance {wmi} -ErrorAction Stop",
            $"{get} | Where-Object {{ $_.Path -and $_.Path.StartsWith($ours) }}",
            $"$procs = {get}",

            // Reading a name to REPORT it, which the rule's own remark permits:
            // this observes a process, it does not select one.
            $"Get-CimInstance {wmi} | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath",
            $"@{{ pid_ = $p.ProcessId; ppid = $p.ParentProcessId; name = $p.Name }}",
            $"OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);",
        ];

        foreach (var allowed in permitted)
        {
            await Assert.That(string.Join(" | ", ProcessSelection.OffencesIn(allowed).Select(offence => offence.Spelling)))
                .IsEmpty()
                .Because($"'{allowed}' names a pid the caller already holds and the rule has never forbidden that");
        }

        // ---- The mixed line: a pid filter that ALSO names an image ------------
        // The name is what decides here and the pid is decoration, so this is a
        // violation. It is the shape a narrowing is most likely to let through.
        string[] mixed =
        [
            $"Get-CimInstance {wmi} -Filter \"ParentProcessId = $pid AND {nm} = 'chrome.exe'\"",
            $"{get} -Id $pid -Name chrome",
            $"{kill} /PID $pid /IM chrome.exe",
        ];

        foreach (var line in mixed)
        {
            await Assert.That(ProcessSelection.OffencesIn(line).Count)
                .IsGreaterThan(0)
                .Because($"'{line}' still decides by image name, and a pid beside it does not make that safe");
        }

        // ---- And the file-scoped gate on the comparison rule -------------------
        // `$_.Name -eq` over a DIRECTORY listing is not a process selection, and a
        // scan that flagged it would be unusable in a repository that reads files.
        var listing = $"$files = Get-ChildItem -LiteralPath $root\n$match = $files | Where-Object {{ $_.{nm} -eq 'payload.json' }}";

        await Assert.That(ProcessSelection.EnumeratesProcesses(listing)).IsFalse();
        await Assert.That(ProcessSelection.OffencesIn(listing).Count).IsEqualTo(0);

        // The same two lines in a file that DOES enumerate processes is caught,
        // which is the whole reason the gate is on the file and not the line:
        // a query built on one line and filtered on the next.
        var hunting = $"$all = Get-CimInstance {wmi}\n$match = $all | Where-Object {{ $_.{nm} -eq 'chrome.exe' }}";

        await Assert.That(ProcessSelection.EnumeratesProcesses(hunting)).IsTrue();
        await Assert.That(ProcessSelection.OffencesIn(hunting).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task TheScanReadsMoreThanTheProductsOwnFiles()
    {
        // The scan is worth nothing if it is looking at four files. This fails
        // if the enumeration in RepositoryLayout ever narrows -- an excluded
        // directory, a pattern that stops matching -- which is a change that
        // otherwise leaves every assertion above passing.
        var scanned = RepositoryLayout.SourceAndScriptFiles;

        await Assert.That(scanned.Count).IsGreaterThan(15);
        await Assert.That(scanned.Any(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(scanned.Any(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(scanned.Any(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(scanned.Any(file => file.Extension is ".ps1")).IsTrue();
    }

    [Test]
    public async Task EveryProjectIsCoveredByTheBannedApiAnalyzer()
    {
        // The analyzer and the shared list are declared in Directory.Build.props
        // and not per project, which is what makes a project added later
        // covered by construction. This asserts that arrangement, because
        // moving either line back into one .csproj would leave the suite green
        // and the other projects unguarded.
        var shared = Path.Combine(RepositoryLayout.Root.FullName, "build", "BannedSymbols.txt");

        await Assert.That(File.Exists(shared)).IsTrue();
        // Composed, for the same reason the needles above are: this file is
        // scanned by the test above it.
        await Assert.That(await File.ReadAllTextAsync(shared)).Contains("Process.GetProcessesBy" + "Name(System.String)");

        var props = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "Directory.Build.props"));

        await Assert.That(props).Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers");
        await Assert.That(props).Contains(@"build\BannedSymbols.txt");

        // No project may drop out of it. `Remove` on an AdditionalFiles item is
        // the quiet way to do exactly that.
        var opted = RepositoryLayout.ProjectFiles
            .Where(file => File.ReadAllText(file.FullName).Contains("AdditionalFiles Remove", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Name);

        await Assert.That(string.Join(", ", opted)).IsEmpty();
    }
}
